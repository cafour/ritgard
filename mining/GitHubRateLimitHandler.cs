using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ritgard.Mining;

public sealed class GitHubRateLimitHandler(GitHubRateLimiter limiter) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        return limiter.Handle(request, base.SendAsync, ct);
    }
}
