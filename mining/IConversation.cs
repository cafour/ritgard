using System;
using System.Collections.Immutable;
using System.Linq;

namespace Ritgard.Mining;

public interface IConversation
{
    string Id { get; }

    string Title { get; }

    string Url { get; }

    DateTimeOffset CreatedAt { get; }

    DateTimeOffset UpdatedAt { get; }

    ImmutableArray<Comment> Comments { get; }
}

public static class ConversationExtensions
{
    public static TimeSpan GetDuration(this IConversation self)
    {
        return Utils.Max(self.UpdatedAt, self.GetLastCommentDate()) - self.CreatedAt;
    }

    public static DateTimeOffset GetLastCommentDate(this IConversation self)
    {
        if (self.Comments.IsDefaultOrEmpty)
        {
            return default;
        }

        return self.Comments.Max(i => i.UpdatedAt);
    }

    public static bool IsClosed(this IConversation self)
    {
        return self switch
        {
            Issue i => i.State == IssueState.Closed,
            PullRequest pr => pr.State == IssueState.Closed,
            Discussion d => d.State == IssueState.Closed || d.AnswerChosenAt is not null,
            _ => throw new NotImplementedException()
        };
    }
}
