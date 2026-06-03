using System;

namespace Ritgard.Mining;

public record IssueEvent(
    long Id,
    string? Author,
    string? Assignee,
    string? Label,
    IssueEventKind Kind,
    string? CommitId,
    DateTimeOffset CreatedAt,
    string? RenamedFrom,
    string? RenamedTo,
    string? RequestedReviewer,
    string? ReviewRequester,
    string? Assigner,
    LockReason? LockReason,
    long? MilestoneId,
    string? SourceActor,
    long? SourceIssueId
)
{
    public static readonly IssueEvent Invalid = new IssueEvent(
        Id: -1L,
        Author: null,
        Assignee: null,
        Label: null,
        Kind: IssueEventKind.Unknown,
        CommitId: null,
        CreatedAt: default,
        RenamedFrom: null,
        RenamedTo: null,
        RequestedReviewer: null,
        ReviewRequester: null,
        Assigner: null,
        LockReason: null,
        MilestoneId: null,
        SourceActor: null,
        SourceIssueId: null
    );
}
