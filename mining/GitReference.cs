namespace Ritgard.Mining;

public record GitReference(
    string Label,
    string Ref,
    string Sha,
    string? Author
);
