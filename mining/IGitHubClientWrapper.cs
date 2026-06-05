using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ritgard.Mining;

public interface IGitHubClientWrapper : IAsyncDisposable
{
    public GitHubToken Token { get; }
    public GitHubRateLimiter Limiter { get; }

    public Task<bool> CheckBlocked(CancellationToken ct = default);
}
