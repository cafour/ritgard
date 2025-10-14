using System;
using System.Collections.Immutable;

namespace Ritgard.Mining;

public record MiningResult(
    DateTimeOffset MiningStartedAt,
    DateTimeOffset MiningCompletedAt,
    Repository Repository,
    ImmutableDictionary<string, Issue> Issues,
    ImmutableDictionary<string, PullRequest> PullRequests,
    ImmutableDictionary<string, Discussion> Discussions,
    ImmutableDictionary<string, Milestone> Milestones
);
