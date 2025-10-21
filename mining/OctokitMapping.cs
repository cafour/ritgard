using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Octokit;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public static class OctokitMapping
{
    [return: NotNullIfNotNull(nameof(value))]
    public static IssueState? MapItemState(ItemState? value)
    {
        return value switch
        {
            null => null,
            ItemState.Open => IssueState.Open,
            ItemState.Closed => IssueState.Closed,
            _ => throw new NotSupportedException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static IssueStateReason? MapItemStateReason(ItemStateReason? value)
    {
        return value switch
        {
            null => null,
            ItemStateReason.Completed => IssueStateReason.Completed,
            ItemStateReason.NotPlanned => IssueStateReason.NotPlanned,
            ItemStateReason.Reopened => IssueStateReason.Reopened,
            _ => throw new NotSupportedException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static LockReason? MapLockReason(Octokit.LockReason? value)
    {
        return value switch
        {
            null => null,
            Octokit.LockReason.OffTopic => LockReason.OffTopic,
            Octokit.LockReason.Resolved => LockReason.Resolved,
            Octokit.LockReason.Spam => LockReason.Spam,
            Octokit.LockReason.TooHeated => LockReason.TooHeated,
            _ => throw new NotSupportedException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static GitReference? MapGitReference(Octokit.GitReference? value)
    {
        if (value is null)
        {
            return null;
        }

        return new GitReference(
            Label: value.Label,
            Ref: value.Ref,
            Sha: value.Sha,
            Author: value.User?.Login
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static MergeableState? MapMergeableState(Octokit.MergeableState? value)
    {
        return value switch
        {
            null => null,
            Octokit.MergeableState.Dirty => MergeableState.Dirty,
            Octokit.MergeableState.Unknown => MergeableState.Unknown,
            Octokit.MergeableState.Blocked => MergeableState.Blocked,
            Octokit.MergeableState.Behind => MergeableState.Behind,
            Octokit.MergeableState.Unstable => MergeableState.Unstable,
            Octokit.MergeableState.HasHooks => MergeableState.HasHooks,
            Octokit.MergeableState.Clean => MergeableState.Clean,
            Octokit.MergeableState.Draft => MergeableState.Draft,
            _ => throw new NullReferenceException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static Issue? MapIssue(Octokit.Issue? value)
    {
        if (value is null)
        {
            return null;
        }

        return new Issue(
            Id: value.NodeId,
            Number: value.Number,
            Url: value.HtmlUrl,
            Title: value.Title,
            Author: value.User.Login,
            CreatedAt: value.CreatedAt,
            UpdatedAt: value.UpdatedAt ?? value.CreatedAt,
            ClosedAt: value.ClosedAt,
            Labels: [.. value.Labels.Select(l => l.Name)],
            Body: value.Body,
            State: MapItemState(UnwrapStringEnum<ItemState>(value.State)) ?? default,
            StateReason: MapItemStateReason(UnwrapStringEnum(value.StateReason)),
            ClosedBy: value.ClosedBy?.Login,
            Assignees: [.. value.Assignees.Select(a => a.Login)],
            IsLocked: value.Locked,
            LockReason: MapLockReason(UnwrapStringEnum(value.ActiveLockReason)),
            MilestoneId: value.Milestone?.Id,
            PullRequestId: value.PullRequest?.Id,
            CommentCount: value.Comments,
            Comments: [],
            Events: []
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static PullRequest? MapPullRequest(Octokit.PullRequest? value)
    {
        if (value is null)
        {
            return null;
        }

        return new PullRequest(
            Id: value.NodeId,
            Number: value.Number,
            Url: value.HtmlUrl,
            State: MapItemState(UnwrapStringEnum<ItemState>(value.State)) ?? default,
            Title: value.Title,
            Body: value.Body,
            CreatedAt: value.CreatedAt,
            UpdatedAt: value.UpdatedAt,
            ClosedAt: value.ClosedAt,
            MergedAt: value.MergedAt,
            Head: MapGitReference(value.Head),
            Base: MapGitReference(value.Base),
            Author: value.User.Login,
            Assignees: [.. value.Assignees.Select(a => a.Login)],
            MilestoneId: value.Milestone?.Id,
            IsDraft: value.Draft,
            IsMerged: value.Merged,
            IsMergeable: value.Mergeable,
            MergeableState: MapMergeableState(UnwrapStringEnum(value.MergeableState)),
            MergedBy: value.MergedBy?.Login,
            MergeCommitSha: value.MergeCommitSha,
            CommentCount: value.Comments,
            CommitCount: value.Commits,
            AdditionCount: value.Additions,
            DeletionCount: value.Deletions,
            ChangedFileCount: value.ChangedFiles,
            IsLocked: value.Locked,
            LockReason: MapLockReason(UnwrapStringEnum(value.ActiveLockReason)),
            RequestedReviewers: [.. value.RequestedReviewers.Select(r => r.Login)],
            RequestedTeams: [.. value.RequestedTeams.Select(t => t.Slug)],
            Labels: [.. value.Labels.Select(l => l.Name)],
            Comments: [],
            Events: []
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static Milestone? MapMilestone(Octokit.Milestone? value)
    {
        if (value is null)
        {
            return null;
        }

        return new Milestone(
            Id: value.NodeId,
            Number: value.Number,
            Url: value.HtmlUrl,
            Title: value.Title,
            Description: value.Description,
            Author: value.Creator.Login,
            CreatedAt: value.CreatedAt,
            DueOn: value.DueOn,
            ClosedAt: value.ClosedAt,
            UpdatedAt: value.UpdatedAt
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static AuthorAssociation? MapAuthorAssociation(Octokit.AuthorAssociation? value)
    {
        return value switch
        {
            null => null,
            Octokit.AuthorAssociation.Collaborator => AuthorAssociation.Collaborator,
            Octokit.AuthorAssociation.Contributor => AuthorAssociation.Contributor,
            Octokit.AuthorAssociation.FirstTimer => AuthorAssociation.FirstTimer,
            Octokit.AuthorAssociation.FirstTimeContributor => AuthorAssociation.FirstTimeContributor,
            Octokit.AuthorAssociation.Member => AuthorAssociation.Member,
            Octokit.AuthorAssociation.Owner => AuthorAssociation.Owner,
            Octokit.AuthorAssociation.None => AuthorAssociation.None,
            _ => throw new NotImplementedException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static AuthorAssociation? MapCommentAuthorAssociation(CommentAuthorAssociation? value)
    {
        return value switch
        {
            null => null,
            CommentAuthorAssociation.Collaborator => AuthorAssociation.Collaborator,
            CommentAuthorAssociation.Contributor => AuthorAssociation.Contributor,
            CommentAuthorAssociation.FirstTimer => AuthorAssociation.FirstTimer,
            CommentAuthorAssociation.FirstTimeContributor => AuthorAssociation.FirstTimeContributor,
            CommentAuthorAssociation.Member => AuthorAssociation.Member,
            CommentAuthorAssociation.Owner => AuthorAssociation.Owner,
            CommentAuthorAssociation.None => AuthorAssociation.None,
            _ => throw new NotImplementedException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static Comment? MapIssueComment(IssueComment? value)
    {
        if (value is null)
        {
            return null;
        }

        return new Comment(
            Id: value.NodeId,
            Body: value.Body,
            CreatedAt: value.CreatedAt,
            UpdatedAt: value.UpdatedAt ?? value.CreatedAt,
            Author: value.User.Login,
            AuthorAssociation: MapAuthorAssociation(
                UnwrapStringEnum<Octokit.AuthorAssociation>(value.AuthorAssociation)
            )
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static RepositoryVisibility? MapRepositoryVisibility(Octokit.RepositoryVisibility? value)
    {
        return value switch
        {
            null => null,
            Octokit.RepositoryVisibility.Internal => RepositoryVisibility.Internal,
            Octokit.RepositoryVisibility.Private => RepositoryVisibility.Private,
            Octokit.RepositoryVisibility.Public => RepositoryVisibility.Public,
            _ => throw new NotSupportedException()
        };
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static Repository? MapRepository(Octokit.Repository? value)
    {
        if (value is null)
        {
            return null;
        }

        return new Repository(
            Id: value.Id,
            Url: value.HtmlUrl,
            Owner: value.Owner.Login,
            Name: value.Name,
            FullName: value.FullName,
            Description: value.Description,
            Homepage: value.Homepage,
            Language: value.Language,
            IsTemplate: value.IsTemplate,
            IsPrivate: value.Private,
            IsFork: value.Fork,
            IsArchived: value.Archived,
            ForkCount: value.ForksCount,
            StargazerCount: value.StargazersCount,
            OpenIssuesCount: value.OpenIssuesCount,
            DefaultBranch: value.DefaultBranch,
            CreatedAt: value.CreatedAt,
            UpdatedAt: value.UpdatedAt,
            PushedAt: value.PushedAt,
            HasDiscussions: value.HasDiscussions,
            HasIssues: value.HasIssues,
            HasWiki: value.HasWiki,
            HasDownloads: value.HasDownloads,
            HasPages: value.HasPages,
            SubscribersCount: value.SubscribersCount,
            Size: value.Size,
            Cloc: null,
            GitLoc: null,
            Topics: [.. value.Topics],
            Visibility: MapRepositoryVisibility(value.Visibility)
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static IssueEvent? MapIssueEvent(Octokit.IssueEvent? value)
    {
        if (value is null)
        {
            return null;
        }

        return new IssueEvent(
            Id: value.Id,
            Author: value.Actor?.Login,
            Assignee: value.Assignee?.Login,
            Label: value.Label?.Name,
            Kind: MapEventInfoState(UnwrapStringEnum<EventInfoState>(value.Event)) ?? default,
            CommitId: value.CommitId,
            CreatedAt: value.CreatedAt,
            RenamedFrom: value.Rename?.From,
            RenamedTo: value.Rename?.To,
            RequestedReviewer: value.RequestedReviewer?.Login,
            ReviewRequester: value.ReviewRequester?.Login,
            Assigner: value.Assigner?.Login,
            LockReason: MapLockReason(UnwrapStringEnum<Octokit.LockReason>(value.LockReason)),
            MilestoneId: value.Milestone?.Id,
            SourceActor: default,
            SourceIssueId: default
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static IssueEvent? MapTimelineEventInfo(TimelineEventInfo? value)
    {
        if (value is null)
        {
            return null;
        }

        return new IssueEvent(
            Id: value.Id,
            Author: value.Actor?.Login,
            Assignee: value.Assignee?.Login,
            Label: value.Label?.Name,
            Kind: MapEventInfoState(UnwrapStringEnum<EventInfoState>(value.Event)) ?? default,
            CommitId: value.CommitId,
            CreatedAt: value.CreatedAt,
            RenamedFrom: value.Rename?.From,
            RenamedTo: value.Rename?.To,
            RequestedReviewer: default,
            ReviewRequester: default,
            Assigner: default,
            LockReason: default,
            MilestoneId: value.Milestone?.Id,
            SourceActor: value.Source?.Actor?.Login,
            SourceIssueId: value.Source?.Issue?.Id
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static Discussion? MapDiscussion(IDiscussionQuery_Repository_Discussions_Edges_Node? value)
    {
        if (value is null)
        {
            return null;
        }

        return new Discussion(
            Id: value.Id,
            Number: value.Number,
            Url: value.Url.ToString(),
            Title: value.Title,
            Body: value.Body,
            Author: (value.Author as IDiscussionQuery_Repository_Discussions_Edges_Node_Author_User)?.Login,
            Category: value.Category.Name,
            UpvoteCount: value.UpvoteCount,
            CreatedAt: value.CreatedAt,
            UpdatedAt: value.UpdatedAt,
            PublishedAt: value.PublishedAt,
            LastEditedAt: value.LastEditedAt,
            AnswerChosenAt: value.AnswerChosenAt,
            State: value.Closed ? IssueState.Closed : IssueState.Open,
            Labels:
            [
                ..value.Labels?.Edges?
                    .Where(l => l?.Node is not null).Select(l => l!.Node!.Name) ?? []
            ],
            CommentCount: value.Comments.TotalCount,
            Comments: []
        );
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static Comment? MapDiscussionComment(IDiscussionCommentQuery_Node_Comments_Edges_Node? value)
    {
        if (value is null)
        {
            return null;
        }

        return new Comment(
            Id: value.Id,
            Body: value.Body,
            CreatedAt: value.CreatedAt,
            UpdatedAt: value.UpdatedAt,
            Author: (value.Author as IDiscussionCommentQuery_Node_Comments_Edges_Node_Author_User)?.Login,
            AuthorAssociation: MapCommentAuthorAssociation(value.AuthorAssociation)
        );
    }

    public static IssueEventKind? MapEventInfoState(EventInfoState? value)
    {
        return value switch
        {
            null => null,
            EventInfoState.AddedToProject => IssueEventKind.AddedToProject,
            EventInfoState.Assigned => IssueEventKind.Assigned,
            EventInfoState.AutomaticBaseChangeFailed => IssueEventKind.AutomaticBaseChangeFailed,
            EventInfoState.AutomaticBaseChangeSucceeded => IssueEventKind.AutomaticBaseChangeSucceeded,
            EventInfoState.BaseRefChanged => IssueEventKind.BaseRefChanged,
            EventInfoState.Closed => IssueEventKind.Closed,
            EventInfoState.Commented => IssueEventKind.Commented,
            EventInfoState.Committed => IssueEventKind.Committed,
            EventInfoState.Connected => IssueEventKind.Connected,
            EventInfoState.ConvertToDraft => IssueEventKind.ConvertToDraft,
            EventInfoState.ConvertedNoteToIssue => IssueEventKind.ConvertedNoteToIssue,
            EventInfoState.Crossreferenced => IssueEventKind.Crossreferenced,
            EventInfoState.Demilestoned => IssueEventKind.Demilestoned,
            EventInfoState.Deployed => IssueEventKind.Deployed,
            EventInfoState.Disconnected => IssueEventKind.Disconnected,
            EventInfoState.HeadRefDeleted => IssueEventKind.HeadRefDeleted,
            EventInfoState.HeadRefRestored => IssueEventKind.HeadRefRestored,
            EventInfoState.HeadRefForcePushed => IssueEventKind.HeadRefForcePushed,
            EventInfoState.Labeled => IssueEventKind.Labeled,
            EventInfoState.Locked => IssueEventKind.Locked,
            EventInfoState.Mentioned => IssueEventKind.Mentioned,
            EventInfoState.MarkedAsDuplicate => IssueEventKind.MarkedAsDuplicate,
            EventInfoState.Merged => IssueEventKind.Merged,
            EventInfoState.Milestoned => IssueEventKind.Milestoned,
            EventInfoState.MovedColumnsInProject => IssueEventKind.MovedColumnsInProject,
            EventInfoState.Pinned => IssueEventKind.Pinned,
            EventInfoState.ReadyForReview => IssueEventKind.ReadyForReview,
            EventInfoState.Referenced => IssueEventKind.Referenced,
            EventInfoState.RemovedFromProject => IssueEventKind.RemovedFromProject,
            EventInfoState.Renamed => IssueEventKind.Renamed,
            EventInfoState.Reopened => IssueEventKind.Reopened,
            EventInfoState.ReviewDismissed => IssueEventKind.ReviewDismissed,
            EventInfoState.ReviewRequested => IssueEventKind.ReviewRequested,
            EventInfoState.ReviewRequestRemoved => IssueEventKind.ReviewRequestRemoved,
            EventInfoState.Reviewed => IssueEventKind.Reviewed,
            EventInfoState.Subscribed => IssueEventKind.Subscribed,
            EventInfoState.Transferred => IssueEventKind.Transferred,
            EventInfoState.Unassigned => IssueEventKind.Unassigned,
            EventInfoState.Unlabeled => IssueEventKind.Unlabeled,
            EventInfoState.Unlocked => IssueEventKind.Unlocked,
            EventInfoState.UnmarkedAsDuplicate => IssueEventKind.UnmarkedAsDuplicate,
            EventInfoState.Unpinned => IssueEventKind.Unpinned,
            EventInfoState.Unsubscribed => IssueEventKind.Unsubscribed,
            EventInfoState.UserBlocked => IssueEventKind.UserBlocked,
            EventInfoState.LineCommented => IssueEventKind.LineCommented,
            EventInfoState.CommitCommented => IssueEventKind.CommitCommented,
            EventInfoState.CommentDeleted => IssueEventKind.CommentDeleted,
            _ => throw new NotSupportedException()
        };
    }

    public static TEnum? UnwrapStringEnum<TEnum>(StringEnum<TEnum>? value)
        where TEnum : struct
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value.TryParse(out var enumValue))
        {
            return enumValue;
        }

        return null;
    }

    public static string GetIssuePlainText(string title, string? body)
    {
        var sb = new StringBuilder();
        var sw = new StringWriter(sb);
        Utils.WriteMarkdownAsPlainText(title.Trim(), sw);

        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.Append('\n');
            Utils.WriteMarkdownAsPlainText(body.Trim(), sw);
        }

        var plainText = sb.ToString();
        var urlRegex = ReadmeMiner.GetUrlRegex();
        return urlRegex.Replace(plainText, "");
    }
}
