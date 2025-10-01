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
);
