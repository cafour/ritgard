using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ritgard.Mining;

public record GitHubPullRequestJson
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}
