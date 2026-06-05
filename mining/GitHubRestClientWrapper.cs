using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Ritgard.Mining;

public sealed class GitHubRestClientWrapper(
    GitHubToken token,
    HttpClient httpClient,
    GitHubRestClient client,
    GitHubRateLimiter limiter,
    ServiceProvider serviceProvider
) : IGitHubClientWrapper
{
    public const string GitHubRestApiVersion = "2026-03-10";

    public GitHubToken Token { get; } = token;
    public HttpClient HttpClient { get; } = httpClient;
    public GitHubRestClient Client { get; } = client;
    public GitHubRateLimiter Limiter { get; } = limiter;
    public ServiceProvider ServiceProvider { get; } = serviceProvider;

    public static GitHubRestClientWrapper Create(GitHubToken token)
    {
        var limiter = new GitHubRateLimiter
        {
            CustomLimit = token.HttpLimit
        };
        var services = new ServiceCollection();
        services.AddHttpClient(nameof(GitHubRestClient))
            .AddHttpMessageHandler(() => new GitHubRateLimitHandler(limiter));
        var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(nameof(GitHubRestClient));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ritgard");
        httpClient.BaseAddress = new Uri("https://api.github.com/");

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubRestApiVersion);

        var authProvider = new AnonymousAuthenticationProvider();
        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        var client = new GitHubRestClient(adapter);
        return new GitHubRestClientWrapper(token, httpClient, client, limiter, provider);
    }

    public async Task<bool> CheckBlocked(CancellationToken ct = default)
    {
        _ = await Client.Rate_limit.GetAsync(cancellationToken: ct);
        // NB: We check the headers, because the info from the `rate_limit` endpoint is unreliable.
        return Limiter.IsBlocked;
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
    }
}
