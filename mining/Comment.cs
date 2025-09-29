using System;

namespace Ritgard.Mining;

public record Comment(
    long Id,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Author,
    AuthorAssociation? AuthorAssociation
);
