using System.Collections.Immutable;

namespace Ritgard.Mining;

public record Discussion(
    string Id,
    int Number,
    string Url,
    string Title,
    string Body,
    string? Author,
    int CommentCount,
    ImmutableArray<Comment> Comments
);
