using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ritgard.Mining;

public record GitHubLabelJson
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}
