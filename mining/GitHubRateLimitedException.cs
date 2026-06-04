using System;

namespace Ritgard.Mining;

public class GitHubRateLimitedException : Exception
{
    public GitHubRateLimitedException()
    {
    }

    public GitHubRateLimitedException(string message) : base(message)
    {
    }

    public GitHubRateLimitedException(string message, Exception inner) : base(message, inner)
    {
    }

    public DateTimeOffset Reset { get; set; }
}
