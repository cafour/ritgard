namespace Ritgard.Mining;

public record Discussion(
    long Id,
    int Number,
    string Url,
    string Title,
    string Body,
    string Author,
    int CommentCount
);
