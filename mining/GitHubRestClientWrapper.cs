using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;

namespace Ritgard.Mining;

public sealed class GitHubRestClientWrapper(
    GitHubToken token,
    GitHubRestClient client,
    HeadersInspectionHandlerOption headerInspector,
    ServiceProvider serviceProvider
) : IAsyncDisposable
{
    public const string GitHubRestApiVersion = "2026-03-10";
    public const string RateLimitHeader = "x-ratelimit-limit";
    public const string RateRemainingHeader = "x-ratelimit-remaining";
    public const string RateUsedHeader = "x-ratelimit-used";
    public const string RateResetHeader = "x-ratelimit-reset";


    public GitHubToken Token { get; } = token;
    public GitHubRestClient Client { get; } = client;
    public HeadersInspectionHandlerOption HeaderInspector { get; } = headerInspector;
    public ServiceProvider ServiceProvider { get; } = serviceProvider;

    public int RateLimit =>
        HeaderInspector.ResponseHeaders.TryGetValue(RateLimitHeader, out var strValue)
        && int.TryParse(strValue.SingleOrDefault(), out var intValue)
            ? intValue
            : -1;

    public int RateRemaining =>
        HeaderInspector.ResponseHeaders.TryGetValue(RateRemainingHeader, out var strValue)
        && int.TryParse(strValue.SingleOrDefault(), out var intValue)
            ? intValue
            : -1;

    public int AdjustedRateRemaining => RateUsed - Math.Min(RateLimit, Math.Max(0, Token.HttpLimit));

    public int RateUsed =>
        HeaderInspector.ResponseHeaders.TryGetValue(RateUsedHeader, out var strValue)
        && int.TryParse(strValue.SingleOrDefault(), out var intValue)
            ? intValue
            : -1;

    public DateTimeOffset RateReset =>
        HeaderInspector.ResponseHeaders.TryGetValue(RateResetHeader, out var strValue)
        && long.TryParse(strValue.SingleOrDefault(), out var longValue)
            ? DateTimeOffset.FromUnixTimeSeconds(longValue)
            : default;

    public bool IsBlocked => RateRemaining <= 0 || RateRemaining < Token.HttpLimit;

    public static GitHubRestClientWrapper Create(GitHubToken token)
    {
        var headerInspector = new HeadersInspectionHandlerOption()
        {
            InspectRequestHeaders = false,
            InspectResponseHeaders = true,
        };
        var services = new ServiceCollection();
        services.AddHttpClient(nameof(GitHubRestClient))
            .AddHttpMessageHandler(() => new HeadersInspectionHandler(headerInspector));
        var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredKeyedService<HttpClient>(nameof(GitHubRestClient));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ritgard");

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubRestApiVersion);

        var authProvider = new AnonymousAuthenticationProvider();
        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        var client = new GitHubRestClient(adapter);
        return new(token, client, headerInspector, provider);
    }

    public async Task<bool> CheckBlocked(CancellationToken ct = default)
    {
        _ = await Client.Rate_limit.GetAsync(cancellationToken: ct);
        // NB: We check the headers, because the info from the `rate_limit` endpoint is unreliable.
        return IsBlocked;
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
    }
}
