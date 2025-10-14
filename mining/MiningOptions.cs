using System.Collections.Generic;

namespace Ritgard.Mining;

public record MiningOptions
{
    public Dictionary<string, GitHubTokenEntry> GitHubTokens { get; set; } = [];
}

public record GitHubTokenEntry
{
    public string Token { get; set; } = string.Empty;

    public int HttpLimit { get; set; } = -1;

    public int GraphQlLimit { get; set; } = -1;
}
