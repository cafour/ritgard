using System;

namespace Ritgard.Mining;

[Flags]
public enum RepoMinerScope
{
    BasicMetadata = 0,
    Issues = 1 << 0,
    IssueComments = 1 << 1,
    IssueEvents = 1 << 2,
    PullRequests = 1 << 3,
    PullRequestComments = 1 << 4,
    PullRequestEvents = 1 << 5,
    Discussions = 1 << 6,
    DiscussionComments = 1 << 7,
    // TODO: Implement DiscussionEvents, if they are ever obtainable through either GH API.
    // DiscussionEvents = 1 << 8,
    Milestones = 1 << 9,
    Conversations = Issues | PullRequests | Discussions,
    ConversationsWithComments = Conversations | IssueComments | PullRequestComments | DiscussionComments,
    ConversationsWithEvents = Conversations | IssueEvents | PullRequestEvents,
    ConversationsFull = ConversationsWithComments | ConversationsWithEvents,
    LoCStats = 1 << 10,
    GitLoCStats = 1 << 11,
    All = int.MaxValue
}
