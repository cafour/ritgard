namespace Ritgard.Mining;

public record GitReference(
    string Label,
    string Url,
    string Ref,
    string Sha,
    string Author
);
