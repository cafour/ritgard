using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ritgard.Mining;

public record GitHubIssueJson
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("number")]
    public long? Number { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("user")]
    public GitHubSimpleUserJson? User { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("closed_at")]
    public DateTimeOffset? ClosedAt { get; init; }

    [JsonPropertyName("labels")]
    public List<GitHubLabelJson>? Labels { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("state_reason")]
    public string? StateReason { get; init; }

    [JsonPropertyName("closed_by")]
    public GitHubSimpleUserJson? ClosedBy { get; init; }

    [JsonPropertyName("assignees")]
    public List<GitHubSimpleUserJson>? Assignees { get; init; }

    [JsonPropertyName("locked")]
    public bool? Locked { get; init; }

    [JsonPropertyName("active_lock_reason")]
    public string? ActiveLockReason { get; init; }

    [JsonPropertyName("milestone")]
    public GitHubMilestoneJson? Milestone { get; init; }

    [JsonPropertyName("comments")]
    public int? Comments { get; init; }

    [JsonPropertyName("pull_request")]
    public GitHubPullRequestJson? PullRequest { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalProperties { get; init; } = [];
}
