using System.Collections.Immutable;

namespace Ritgard.Mining;

public record MiningResult(
    Repository Repository,
    ImmutableDictionary<long, Issue> Issues,
    ImmutableDictionary<long, PullRequest> PullRequests,
    ImmutableDictionary<long, Milestone> Milestones
    // ImmutableDictionary<long, Discussion> Discussions
);
