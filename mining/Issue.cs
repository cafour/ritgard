using System;
using System.Collections.Immutable;
using Octokit;

namespace Ritgard.Mining;

public record Issue(
    long Id,
    int Number,
    string Url,
    string Title,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ClosedAt,
    ImmutableArray<string> Labels,
    string Body,
    ItemState State,
    ItemStateReason? StateReason,
    string? ClosedBy,
    string? Assignee,
    bool IsLocked,
    LockReason? LockReason,
    long? MilestoneId,
    long? PullRequestId,
    int CommentCount,
    ImmutableArray<Comment> Comments
);
