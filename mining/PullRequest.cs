using System;
using System.Collections.Immutable;

namespace Ritgard.Mining;

public record PullRequest(
    long Id,
    int Number,
    string Url,
    IssueState State,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ClasedAt,
    DateTimeOffset? MergedAt,
    GitReference Head,
    GitReference Base,
    string Author,
    ImmutableArray<string> Assignees,
    long? MilestoneId,
    bool IsDraft,
    bool IsMerged,
    bool? IsMergeable,
    MergeableState? MergeableState,
    string? MergedBy,
    string MergeCommitSha,
    int CommentCount,
    int CommitCount,
    int AdditionCount,
    int DeletionCount,
    int ChangedFileCount,
    bool IsLocked,
    LockReason? LockReason,
    ImmutableArray<string> RequestedReviewers,
    ImmutableArray<string> RequestedTeams,
    ImmutableArray<string> Labels
);
