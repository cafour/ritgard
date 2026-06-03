using System;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public sealed class GitHubGraphQlClientWrapper(
    GitHubToken token,
    GitHubGraphQLClient client,
    GitHubRateLimitHeaders headerInspector,
    ServiceProvider serviceProvider
) : IAsyncDisposable
{
    public GitHubToken Token { get; } = token;
    public GitHubGraphQLClient Client { get; } = client;
    public GitHubRateLimitHeaders HeaderInspector { get; } = headerInspector;
    public ServiceProvider ServiceProvider { get; } = serviceProvider;

    public int RateLimit => Math.Clamp(
        Token.GraphQlLimit < 0 ? HeaderInspector.RateLimit : Token.GraphQlLimit,
        0,
        HeaderInspector.RateLimit
    );

    public int RateRemaining => RateLimit - RateUsed;

    public int RateUsed => HeaderInspector.RateUsed;

    public DateTimeOffset RateReset => HeaderInspector.RateReset;

    public bool IsBlocked => RateRemaining <= 0;

    public static GitHubGraphQlClientWrapper Create(GitHubToken token)
    {
        var headerInspector = new GitHubRateLimitHeaders();

        var services = new ServiceCollection();
        services.AddGitHubGraphQLClient()
            .ConfigureHttpClient(
                c =>
                {
                    c.BaseAddress = new Uri("https://api.github.com/graphql");
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                },
                cb => cb.AddHttpMessageHandler(() => headerInspector)
            );
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<GitHubGraphQLClient>();
        return new GitHubGraphQlClientWrapper(token, client, headerInspector, provider);
    }

    public async Task<bool> CheckBlocked(CancellationToken ct = default)
    {
        _ = await Client.RateLimitQuery.ExecuteAsync(ct);
        return IsBlocked;
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
    }
}
