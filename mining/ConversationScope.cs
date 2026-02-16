using System;

namespace Ritgard.Mining;

[Flags]
public enum ConversationScope
{
    None = 0,
    Issues = 1 << 0,
    PullRequests = 1 << 1,
    Discussions = 1 << 2,
    All = Issues | PullRequests | Discussions
}

public static class ConversationScopeExtensions
{
    public static bool IsInScope(this ConversationScope self, IConversation conversation)
    {
        return conversation switch
        {
            Issue => self.HasFlag(ConversationScope.Issues),
            PullRequest => self.HasFlag(ConversationScope.PullRequests),
            Discussion => self.HasFlag(ConversationScope.Discussions),
            _ => false
        };
    }

    public static bool IsInScope(this IConversation self, ConversationScope scope)
    {
        return scope.IsInScope(self);
    }
}
