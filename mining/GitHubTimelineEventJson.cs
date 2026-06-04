using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ritgard.Mining;

public record GitHubTimelineEventJson
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("actor")]
    public GitHubSimpleUserJson? Actor { get; init; }

    [JsonPropertyName("assignee")]
    public GitHubSimpleUserJson? Assignee { get; init; }

    [JsonPropertyName("label")]
    public GitHubLabelJson? Label { get; init; }

    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("commit_id")]
    public string? CommitId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("rename")]
    public GitHubRenameJson? Rename { get; init; }

    [JsonPropertyName("requested_reviewer")]
    public GitHubSimpleUserJson? RequestedReviewer { get; init; }

    [JsonPropertyName("review_requester")]
    public GitHubSimpleUserJson? ReviewRequester { get; init; }

    [JsonPropertyName("assigner")]
    public GitHubSimpleUserJson? Assigner { get; init; }

    [JsonPropertyName("lock_reason")]
    public string? LockReason { get; init; }

    [JsonPropertyName("milestone")]
    public GitHubMilestoneJson? Milestone { get; init; }

    [JsonPropertyName("source")]
    public GitHubTimelineEventSourceJson? Source { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}

public record GitHubSimpleUserJson
{
    [JsonPropertyName("login")]
    public string? Login { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}

public record GitHubLabelJson
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}

public record GitHubRenameJson
{
    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}

public record GitHubMilestoneJson
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}

public record GitHubTimelineEventSourceJson
{
    [JsonPropertyName("id")]
    public GitHubSimpleUserJson? Actor { get; init; }

    [JsonPropertyName("issue")]
    public GitHubIssueJson? Issue { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}

public record GitHubIssueJson
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}
