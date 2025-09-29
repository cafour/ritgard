using System;
using System.Collections.Immutable;

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
    IssueState State,
    IssueStateReason? StateReason,
    string? ClosedBy,
    ImmutableArray<string> Assignees,
    bool IsLocked,
    LockReason? LockReason,
    long? MilestoneId,
    long? PullRequestId,
    int CommentCount,
    ImmutableArray<Comment> Comments
);
