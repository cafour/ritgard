using System;

namespace Ritgard.Mining;

public record Issue(
    long Id,
    int Number,
    string Title,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ClosedAt,
    string Labels,
    string Body
);
