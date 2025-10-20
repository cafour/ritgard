using System;
using Ritgard.Mining;

namespace Ritgard;

[Flags]
public enum VisualizationScope
{
    None = 0,
    Issues = 1 << 0,
    PullRequests = 1 << 1,
    Discussions = 1 << 2,
    All = int.MaxValue
}

public static class VisualizationScopeExtensions
{
    public static bool IsInScope(this VisualizationScope self, IConversation conversation)
    {
        return conversation switch
        {
            Issue => self.HasFlag(VisualizationScope.Issues),
            PullRequest => self.HasFlag(VisualizationScope.PullRequests),
            Discussion => self.HasFlag(VisualizationScope.Discussions),
            _ => false
        };
    }

    public static bool IsInScope(this IConversation self, VisualizationScope scope)
    {
        return scope.IsInScope(self);
    }
}
