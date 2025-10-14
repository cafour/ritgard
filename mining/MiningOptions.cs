using System.Collections.Generic;

namespace Ritgard.Mining;

public record MiningOptions
{
    public Dictionary<string, string> GitHubTokens { get; set; } = [];
}
