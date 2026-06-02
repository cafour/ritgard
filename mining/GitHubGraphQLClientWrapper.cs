using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public sealed class GitHubGraphQLClientWrapper(
    GitHubToken token,
    GitHubGraphQLClient client,
    ServiceProvider serviceProvider
) : IAsyncDisposable
{
    private int rateRemaining = -1;
    private long rateReset = 0;

    public GitHubToken Token { get; } = token;
    public GitHubGraphQLClient Client { get; } = client;
    public ServiceProvider ServiceProvider { get; } = serviceProvider;

    public int RateRemaining => rateRemaining;

    public DateTimeOffset RateReset => DateTimeOffset.FromUnixTimeSeconds(rateReset);

    public bool IsBlocked => RateRemaining <= 0 || RateRemaining < Token.GraphQlLimit;

    public static GitHubGraphQLClientWrapper Create(GitHubToken token)
    {
        var services = new ServiceCollection();
        services.AddGitHubGraphQLClient()
            .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri("https://api.github.com/graphql");
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                }
            );
        var provider = services.BuildServiceProvider();
        return new GitHubGraphQLClientWrapper(token, provider.GetRequiredService<GitHubGraphQLClient>(), provider);
    }

    public async Task<TResult> Query<TResult>(
        Func<GitHubGraphQLClient, CancellationToken, Task<TResult>> execute,
        // Func<TResult, IReadOnlyList<IClientError>> errorsAccessor,
        Func<TResult, int> rateRemainingAccessor,
        Func<TResult, DateTimeOffset> rateResetAccessor,
        CancellationToken ct = default
    )
    {
        var queryResult = await execute(Client, ct);
        var newRemaining = rateRemainingAccessor(queryResult);
        while (newRemaining < rateRemaining)
        {
            Interlocked.CompareExchange(ref rateRemaining, newRemaining, rateRemaining);
        }

        var newReset = rateResetAccessor(queryResult).ToUnixTimeSeconds();
        while (newReset > RateRemaining)
        {
            Interlocked.CompareExchange(ref rateReset, newReset, rateReset);
        }

        // var errors = errorsAccessor(queryResult);
        // if (errors.Count == 0)
        // {
        //     return queryResult;
        // }
        //
        // if (errors is [{ Code: "graphql_rate_limit" }])
        // {
        // }

        return queryResult;
    }

    public async Task<bool> CheckBlocked(CancellationToken ct = default)
    {
        _ = await Query(
            (c, ict) => c.RateLimitQuery.ExecuteAsync(ict),
            r => r.Data?.RateLimit?.Remaining ?? -1,
            r => r.Data?.RateLimit?.ResetAt ?? default,
            ct
        );
        return IsBlocked;
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
    }
}
