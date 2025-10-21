using System;
using System.Collections.Immutable;

namespace Ritgard.Mining;

public record Discussion(
    string Id,
    int Number,
    string Url,
    string Title,
    string Body,
    string? Author,
    string Category,
    int UpvoteCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? LastEditedAt,
    DateTimeOffset? AnswerChosenAt,
    IssueState State,
    ImmutableArray<string> Labels,
    int CommentCount,
    ImmutableArray<Comment> Comments
) : IConversation
{
    public override string ToString()
    {
        return $"Discussion #{Number}: {Title}";
    }
}
