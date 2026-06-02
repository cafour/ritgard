namespace Ritgard.Mining;

public record GitHubToken(
    string Name,
    string Token,
    int HttpLimit,
    int GraphQlLimit
);
