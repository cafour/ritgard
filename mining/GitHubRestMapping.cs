using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using GhAuthorAssociation = Ritgard.Mining.Models.AuthorAssociation;
using GhFullRepository = Ritgard.Mining.Models.FullRepository;
using GhIssue = Ritgard.Mining.Models.Issue;
using GhIssueComment = Ritgard.Mining.Models.IssueComment;
using GhIssueStateReason = Ritgard.Mining.Models.Issue_state_reason;
using GhMilestone = Ritgard.Mining.Models.Milestone;
using GhPullRequest = Ritgard.Mining.Models.PullRequest;
using GhPullRequestBase = Ritgard.Mining.Models.PullRequest_base;
using GhPullRequestHead = Ritgard.Mining.Models.PullRequest_head;
using GhPullRequestState = Ritgard.Mining.Models.PullRequest_state;
using GhTimelineIssueEvents = Ritgard.Mining.Models.TimelineIssueEvents;

namespace Ritgard.Mining;

public static class GitHubRestMapping
{
    public static Issue MapIssue(GhIssue value)
    {
        return new Issue(
            Id: value.NodeId ?? value.Id?.ToString(CultureInfo.InvariantCulture) ?? $"issue-{value.Number}",
            Number: (int)(value.Number ?? 0),
            Url: value.HtmlUrl ?? string.Empty,
            Title: value.Title ?? string.Empty,
            Author: value.User?.Login ?? string.Empty,
            CreatedAt: value.CreatedAt ?? default,
            UpdatedAt: value.UpdatedAt ?? value.CreatedAt ?? default,
            ClosedAt: value.ClosedAt,
            Labels: [.. (value.Labels ?? []).Where(l => !string.IsNullOrWhiteSpace(l))],
            Body: value.Body ?? string.Empty,
            State: MapIssueState(value.State),
            StateReason: MapIssueStateReason(value.StateReason),
            ClosedBy: value.ClosedBy?.Login,
            Assignees:
            [
                ..(value.Assignees ?? [])
                .Select(a => a.Login)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!)
            ],
            IsLocked: value.Locked ?? false,
            LockReason: MapLockReason(value.ActiveLockReason),
            MilestoneId: value.Milestone?.Id,
            PullRequestId: null,
            CommentCount: (int)(value.Comments ?? 0),
            Comments: [],
            Events: []
        );
    }

    public static PullRequest MapPullRequest(GhPullRequest value)
    {
        return new PullRequest(
            Id: value.NodeId ?? value.Id?.ToString(CultureInfo.InvariantCulture) ?? $"pr-{value.Number}",
            Number: (int)(value.Number ?? 0),
            Url: value.HtmlUrl ?? string.Empty,
            State: value.State == GhPullRequestState.Closed ? IssueState.Closed : IssueState.Open,
            Title: value.Title ?? string.Empty,
            Body: value.Body ?? string.Empty,
            CreatedAt: value.CreatedAt ?? default,
            UpdatedAt: value.UpdatedAt ?? value.CreatedAt ?? default,
            ClosedAt: value.ClosedAt,
            MergedAt: value.MergedAt,
            Head: MapGitReference(value.Head),
            Base: MapGitReference(value.Base),
            Author: value.User?.Login ?? string.Empty,
            Assignees:
            [
                ..(value.Assignees ?? [])
                .Select(a => a.Login)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!)
            ],
            MilestoneId: value.Milestone?.Id,
            IsDraft: value.Draft ?? false,
            IsMerged: value.Merged ?? false,
            IsMergeable: value.Mergeable,
            MergeableState: MapMergeableState(value.MergeableState),
            MergedBy: value.MergedBy?.Login,
            MergeCommitSha: string.Empty,
            CommentCount: (int)(value.Comments ?? 0),
            CommitCount: (int)(value.Commits ?? 0),
            AdditionCount: (int)(value.Additions ?? 0),
            DeletionCount: (int)(value.Deletions ?? 0),
            ChangedFileCount: (int)(value.ChangedFiles ?? 0),
            IsLocked: value.Locked ?? false,
            LockReason: MapLockReason(value.ActiveLockReason),
            RequestedReviewers:
            [
                ..(value.RequestedReviewers ?? [])
                .Select(r => r.Login)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r!)
            ],
            RequestedTeams:
            [
                ..(value.RequestedTeams ?? [])
                .Select(t => t.Slug)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!)
            ],
            Labels:
            [
                ..(value.Labels ?? [])
                .Select(l => l.Name)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l!)
            ],
            Comments: [],
            Events: []
        );
    }

    public static Milestone MapMilestone(GhMilestone value)
    {
        return new Milestone(
            Id: value.NodeId ?? value.Id?.ToString(CultureInfo.InvariantCulture) ?? $"milestone-{value.Number}",
            Number: (int)(value.Number ?? 0),
            Url: value.HtmlUrl ?? string.Empty,
            Title: value.Title ?? string.Empty,
            Description: value.Description,
            Author: value.Creator?.Login ?? string.Empty,
            CreatedAt: value.CreatedAt ?? default,
            DueOn: value.DueOn,
            ClosedAt: value.ClosedAt,
            UpdatedAt: value.UpdatedAt
        );
    }

    public static Comment MapIssueComment(GhIssueComment value)
    {
        return new Comment(
            Id: value.NodeId ?? value.Id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Body: value.Body ?? string.Empty,
            CreatedAt: value.CreatedAt ?? default,
            UpdatedAt: value.UpdatedAt ?? value.CreatedAt ?? default,
            Author: value.User?.Login,
            AuthorAssociation: MapAuthorAssociation(value.AuthorAssociation)
        );
    }

    public static Repository MapRepository(GhFullRepository value)
    {
        return new Repository(
            Id: value.Id ?? 0,
            Url: value.HtmlUrl ?? string.Empty,
            Owner: value.Owner?.Login ?? string.Empty,
            Name: value.Name ?? string.Empty,
            FullName: value.FullName ?? string.Empty,
            Description: value.Description ?? string.Empty,
            Homepage: value.Homepage ?? string.Empty,
            Language: value.Language ?? string.Empty,
            IsTemplate: value.IsTemplate ?? false,
            IsPrivate: value.Private ?? false,
            IsFork: value.Fork ?? false,
            IsArchived: value.Archived ?? false,
            ForkCount: (int)(value.ForksCount ?? 0),
            StargazerCount: (int)(value.StargazersCount ?? 0),
            OpenIssuesCount: (int)(value.OpenIssuesCount ?? 0),
            DefaultBranch: value.DefaultBranch ?? string.Empty,
            CreatedAt: value.CreatedAt ?? default,
            UpdatedAt: value.UpdatedAt,
            PushedAt: value.PushedAt,
            HasDiscussions: value.HasDiscussions ?? false,
            HasIssues: value.HasIssues ?? false,
            HasWiki: value.HasWiki ?? false,
            HasDownloads: true,
            HasPages: value.HasPages ?? false,
            SubscribersCount: (int)(value.SubscribersCount ?? 0),
            Size: value.Size ?? 0,
            Cloc: null,
            GitLoc: null,
            Topics: [.. (value.Topics ?? [])],
            Visibility: MapRepositoryVisibility(value.Visibility)
        );
    }

    public static IssueEvent MapTimelineEventInfo(GhTimelineIssueEvents value)
    {
        object? e = null;
        e ??= value.AddedToProjectIssueEvent;
        e ??= value.ConvertedNoteToIssueIssueEvent;
        e ??= value.DemilestonedIssueEvent;
        e ??= value.LabeledIssueEvent;
        e ??= value.LockedIssueEvent;
        e ??= value.MilestonedIssueEvent;
        e ??= value.MovedColumnInProjectIssueEvent;
        e ??= value.RemovedFromProjectIssueEvent;
        e ??= value.RenamedIssueEvent;
        e ??= value.ReviewDismissedIssueEvent;
        e ??= value.ReviewRequestedIssueEvent;
        e ??= value.ReviewRequestRemovedIssueEvent;
        e ??= value.StateChangeIssueEvent;
        e ??= value.TimelineAssignedIssueEvent;
        e ??= value.TimelineCommentEvent;
        e ??= value.TimelineCommitCommentedEvent;
        e ??= value.TimelineCommittedEvent;
        e ??= value.TimelineCrossReferencedEvent;
        e ??= value.TimelineLineCommentedEvent;
        e ??= value.TimelineReviewedEvent;
        e ??= value.TimelineUnassignedIssueEvent;
        e ??= value.UnlabeledIssueEvent;
        if (e is null)
        {
            throw new NotSupportedException("Unknown timeline event type.");
        }

        return new IssueEvent(
            Id: GetLong(GetObject(e, "Id")) ?? 0,
            Author: GetString(GetObject(GetObject(e, "Actor"), "Login")),
            Assignee: GetString(GetObject(GetObject(e, "Assignee"), "Login")),
            Label: GetString(GetObject(GetObject(e, "Label"), "Name")),
            Kind: MapIssueEventKind(GetString(GetObject(e, "Event"))),
            CommitId: GetString(GetObject(e, "CommitId")),
            CreatedAt: GetDateTimeOffset(GetObject(e, "CreatedAt")),
            RenamedFrom: GetString(GetObject(GetObject(e, "Rename"), "From")),
            RenamedTo: GetString(GetObject(GetObject(e, "Rename"), "To")),
            RequestedReviewer: GetString(GetObject(GetObject(e, "RequestedReviewer"), "Login")),
            ReviewRequester: GetString(GetObject(GetObject(e, "ReviewRequester"), "Login")),
            Assigner: GetString(GetObject(GetObject(e, "Assigner"), "Login")),
            LockReason: MapLockReason(GetString(GetObject(e, "LockReason"))),
            MilestoneId: GetLong(GetObject(GetObject(e, "Milestone"), "Id")),
            SourceActor: GetString(GetObject(GetObject(GetObject(e, "Source"), "Actor"), "Login")),
            SourceIssueId: GetLong(GetObject(GetObject(GetObject(e, "Source"), "Issue"), "Id"))
        );
    }

    private static GitReference MapGitReference(GhPullRequestHead? value)
    {
        return new GitReference(
            Label: value?.Label ?? string.Empty,
            Ref: value?.Ref ?? string.Empty,
            Sha: value?.Sha ?? string.Empty,
            Author: value?.User?.Login
        );
    }

    private static GitReference MapGitReference(GhPullRequestBase? value)
    {
        return new GitReference(
            Label: value?.Label ?? string.Empty,
            Ref: value?.Ref ?? string.Empty,
            Sha: value?.Sha ?? string.Empty,
            Author: value?.User?.Login
        );
    }

    private static IssueState MapIssueState(string? value)
    {
        return string.Equals(value, "closed", StringComparison.OrdinalIgnoreCase)
            ? IssueState.Closed
            : IssueState.Open;
    }

    private static IssueStateReason? MapIssueStateReason(GhIssueStateReason? value)
    {
        return value switch
        {
            null => null,
            GhIssueStateReason.Completed => IssueStateReason.Completed,
            GhIssueStateReason.Not_planned => IssueStateReason.NotPlanned,
            GhIssueStateReason.Reopened => IssueStateReason.Reopened,
            _ => null
        };
    }

    private static MergeableState? MapMergeableState(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "dirty" => MergeableState.Dirty,
            "unknown" => MergeableState.Unknown,
            "blocked" => MergeableState.Blocked,
            "behind" => MergeableState.Behind,
            "unstable" => MergeableState.Unstable,
            "has_hooks" => MergeableState.HasHooks,
            "clean" => MergeableState.Clean,
            "draft" => MergeableState.Draft,
            _ => null
        };
    }

    private static LockReason? MapLockReason(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "off-topic" => LockReason.OffTopic,
            "resolved" => LockReason.Resolved,
            "spam" => LockReason.Spam,
            "too heated" => LockReason.TooHeated,
            _ => null
        };
    }

    private static AuthorAssociation? MapAuthorAssociation(GhAuthorAssociation? value)
    {
        return value switch
        {
            null => null,
            GhAuthorAssociation.COLLABORATOR => AuthorAssociation.Collaborator,
            GhAuthorAssociation.CONTRIBUTOR => AuthorAssociation.Contributor,
            GhAuthorAssociation.FIRST_TIMER => AuthorAssociation.FirstTimer,
            GhAuthorAssociation.FIRST_TIME_CONTRIBUTOR => AuthorAssociation.FirstTimeContributor,
            GhAuthorAssociation.MEMBER => AuthorAssociation.Member,
            GhAuthorAssociation.OWNER => AuthorAssociation.Owner,
            GhAuthorAssociation.NONE => AuthorAssociation.None,
            _ => null
        };
    }

    private static RepositoryVisibility? MapRepositoryVisibility(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "public" => RepositoryVisibility.Public,
            "private" => RepositoryVisibility.Private,
            "internal" => RepositoryVisibility.Internal,
            _ => null
        };
    }

    private static IssueEventKind MapIssueEventKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return IssueEventKind.Unknown;
        }

        var pascal = string.Concat(
            value
                .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => char.ToUpperInvariant(p[0]) + p[1..])
        );

        return Enum.TryParse<IssueEventKind>(pascal, out var kind)
            ? kind
            : IssueEventKind.Unknown;
    }

    private static object? GetObject(object? source, string propertyName)
    {
        return source?.GetType().GetProperty(propertyName)?.GetValue(source);
    }

    private static string? GetString(object? source)
    {
        return source as string;
    }

    private static long? GetLong(object? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source is int i)
        {
            return i;
        }

        if (source is long l)
        {
            return l;
        }

        return null;
    }

    private static DateTimeOffset GetDateTimeOffset(object? source)
    {
        if (source is DateTimeOffset dto)
        {
            return dto;
        }

        if (source is string s && DateTimeOffset.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return default;
    }
}
