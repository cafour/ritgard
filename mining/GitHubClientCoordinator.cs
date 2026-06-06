using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

namespace Ritgard.Mining;

public sealed class GitHubClientCoordinator<TClient>(
    ILogger logger,
    string kind,
    Func<GitHubToken, TClient> clientFactory
) : IAsyncDisposable
    where TClient : class, IGitHubClientWrapper
{
    private readonly ConcurrentDictionary<string, TClient> clients = [];
    private readonly SemaphoreSlim currentLock = new(1, 1);

    public string Kind { get; } = kind;

    public Func<GitHubToken, TClient> ClientFactory { get; } = clientFactory;

    public TClient? Current { get; private set; }

    public static async Task<GitHubClientCoordinator<TClient>> Create(
        ILogger logger,
        string kind,
        Func<GitHubToken, TClient> clientFactory,
        IEnumerable<GitHubToken> authTokens,
        CancellationToken ct = default
    )
    {
        var coordinator = new GitHubClientCoordinator<TClient>(logger, kind, clientFactory);
        foreach (var authToken in authTokens)
        {
            await coordinator.CreateClient(authToken, ct: ct);
        }

        await coordinator.RefreshCurrent(ct);

        return coordinator;
    }

    public async Task<TResult?> Execute<TResult>(
        Func<TClient, CancellationToken, Task<TResult?>> execute,
        int attempts = 5,
        CancellationToken ct = default
    )
    {
        for (int attempt = 1; attempt <= attempts; ++attempt)
        {
            var client = Current ?? await EnsureAvailable(null, ct);
            if (client.Limiter.IsBlocked)
            {
                logger.LogError(
                    "{ClientKind} client '{TokenName}' has been depleted (attempt {Attempt}).",
                    Kind,
                    client.Token.Name,
                    attempt
                );
                await EnsureAvailable(client.Token, ct);
                continue;
            }

            try
            {
                return await execute(client, ct);
            }
            catch (GitHubRateLimitedException ex)
            {
                if (!ex.WasRequestBlocked)
                {
                    logger.LogError(
                        "Thread {ThreadId} encountered a {ClientKind} rate limit error with client '{TokenName}' (attempt {Attempt}).",
                        Environment.CurrentManagedThreadId,
                        Kind,
                        client.Token.Name,
                        attempt
                    );
                }
                if (attempt == attempts)
                {
                    throw;
                }

                await EnsureAvailable(client.Token, ct);
            }
            catch (ApiException ex)
            {
                logger.LogError(
                    ex,
                    "Encountered an API error with {ClientKind} client '{TokenName}' (attempt {Attempt}).",
                    Kind,
                    client.Token.Name,
                    attempt
                );
                if (attempt == attempts)
                {
                    throw;
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(
                    ex,
                    "Encountered an unknown HTTP error. Re-creating {ClientKind} client '{TokenName}' (attempt {Attempt}).",
                    Kind,
                    client.Token.Name,
                    attempt
                );
                if (attempt == attempts)
                {
                    throw;
                }

                await CreateClient(client.Token, ct: ct);
            }
        }

        throw new InvalidOperationException("Failed to execute the request.");
    }

    private async Task CreateClient(GitHubToken token, int attempts = 3, CancellationToken ct = default)
    {
        await currentLock.WaitAsync(ct);
        try
        {
            var isSuccess = false;
            for (int attempt = 0; attempt < attempts; ++attempt)
            {
                var client = ClientFactory(token);
                try
                {
                    var isBlocked = await client.CheckBlocked(ct);
                    if (isBlocked)
                    {
                        logger.LogWarning(
                            "{ClientKind} client '{TokenName}' is blocked till '{ResetAt}' (attempt {Attempt}).",
                            Kind,
                            client.Token.Name,
                            client.Limiter.ResetAt,
                            attempt
                        );
                    }
                    else
                    {
                        logger.LogInformation(
                            "{ClientKind} client '{TokenName}' is available with '{Remaining}' remaining (attempt {Attempt}).",
                            Kind,
                            client.Token.Name,
                            client.Limiter.EffectiveRemaining,
                            attempt
                        );
                    }
                }
                catch (HttpRequestException ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to create a {ClientKind} client for '{TokenName}' (attempt {Attempt}).",
                        Kind,
                        token.Name,
                        attempt
                    );
                    continue;
                }

                TClient? existing = null;
                clients.AddOrUpdate(
                    token.Name,
                    client,
                    (_, e) =>
                    {
                        existing = e;
                        return client;
                    }
                );
                isSuccess = true;

                if (existing is not null)
                {
                    await existing.DisposeAsync();
                }

                if (Current is not null && Current.Token == token)
                {
                    Current = client;
                }

                break;
            }

            if (!isSuccess)
            {
                logger.LogError("Failed to create a {ClientKind} client for token '{TokenName}.'", Kind, token.Name);
                clients.TryRemove(token.Name, out _);
                if (Current is not null && Current.Token == token)
                {
                    Current = null;
                }
            }
        }
        finally
        {
            currentLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients.Values)
        {
            await client.DisposeAsync();
        }
    }

    private async Task<TClient> EnsureAvailable(
        GitHubToken? currentToken,
        CancellationToken ct = default
    )
    {
        if (Current is not null && Current.Token != currentToken)
        {
            // some other thread switched the client first
        }

        await currentLock.WaitAsync(ct);
        try
        {
            if (Current is not null && Current!.Token != currentToken)
            {
                // some other thread switched the client first
                return Current;
            }

            return await RefreshCurrent(ct);
        }
        finally
        {
            currentLock.Release();
        }
    }

    private async Task<TClient> RefreshCurrent(CancellationToken ct = default)
    {
        if (clients.Count == 0)
        {
            throw new InvalidOperationException("There are no (more) clients available.");
        }

        var newClient = clients
            .OrderByDescending(c =>
                c.Value.Limiter.ResetAt > DateTimeOffset.UtcNow
                    ? c.Value.Limiter.EffectiveRemaining
                    : c.Value.Limiter.EffectiveLimit
            )
            .ThenBy(c => c.Value.Limiter.ResetAt)
            .First().Value;
        if (newClient.Limiter.ResetAt > DateTimeOffset.UtcNow && newClient.Limiter.EffectiveRemaining <= 0)
        {
            var waitTime = newClient.Limiter.ResetAt.Value - DateTimeOffset.UtcNow;
            logger.LogInformation(
                "{ClientKind} coordinator is waiting for token '{TokenName}' to cool down for {CooldownTime}.",
                Kind,
                newClient.Token.Name,
                waitTime
            );
            await Task.Delay(waitTime, ct);
        }

        logger.LogInformation("Switching GitHub {ClientKind} API client to '{TokenName}'.", Kind, newClient.Token.Name);
        Current = newClient;
        return newClient;
    }
}
