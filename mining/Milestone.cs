using System;

namespace Ritgard.Mining;

public record Milestone(
    long Id,
    int Number,
    string Url,
    string Title,
    string? Description,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueOn,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? UpdatedAt
);
