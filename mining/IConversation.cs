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

    /// <summary>
    /// Checks whether the conversation is closed. By default checks against the present sent,
    /// optionally against time specified by <paramref name="asOf"/>.
    /// </summary>
    /// <remarks>
    /// If both <paramref name="asOf"/> and <paramref name="stepLength"/> are provided, ensures in at least one step the conversation will be reported
    /// as open, although it should technically be already closed.
    /// </remarks>
    /// <param name="self"></param>
    /// <param name="asOf"></param>
    /// <param name="stepLength"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static bool IsClosed(this IConversation self, DateTimeOffset asOf = default, TimeSpan stepLength = default)
    {
        if (asOf == default)
        {
            return self switch
            {
                Issue i => i.State == IssueState.Closed,
                PullRequest pr => pr.State == IssueState.Closed,
                Discussion d => d.State == IssueState.Closed || d.AnswerChosenAt is not null,
                _ => throw new NotImplementedException()
            };
        }

        // TODO: This whole first step feature is a hack.
        var isFirstStep = stepLength != TimeSpan.Zero && asOf < self.CreatedAt + stepLength;

        return self switch
        {
            Issue i => i.ClosedAt is not null && !isFirstStep && i.ClosedAt < asOf,
            PullRequest pr => pr.ClosedAt is not null && !isFirstStep && pr.ClosedAt < asOf,
            Discussion d => d.AnswerChosenAt is not null && !isFirstStep && d.AnswerChosenAt < asOf,
            _ => throw new NotImplementedException()
        };
    }
}
