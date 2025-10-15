using System;
using System.Collections.Immutable;

namespace Ritgard.Mining;

public record PullRequest(
    string Id,
    int Number,
    string Url,
    IssueState State,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
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
    ImmutableArray<string> Labels,
    ImmutableArray<Comment> Comments,
    ImmutableArray<IssueEvent> Events
) : IConversation
{
    public override string ToString()
    {
        return $"Pull request #{Number}: {Title}";
    }
}
