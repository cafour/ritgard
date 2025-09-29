using System;
using System.Collections.Immutable;
using System.Linq;

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
)
{
    public DateTimeOffset GetLastCommentDate()
    {
        if (Comments.IsDefaultOrEmpty)
        {
            return default;
        }

        return Comments.Max(i => i.CreatedAt);
    }

    public TimeSpan GetTimeSpan()
    {
        return Utils.Max(UpdatedAt ?? default, GetLastCommentDate()) - CreatedAt;
    }
}
