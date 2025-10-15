using System;

namespace Ritgard.Mining;

public record Comment(
    string Id,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Author,
    AuthorAssociation? AuthorAssociation
);
