using System;
using System.Collections.Immutable;

namespace Ritgard.Mining;

public record Repository(
    long Id,
    string Url,
    string Owner,
    string Name,
    string FullName,
    string Description,
    string Homepage,
    string Language,
    bool IsTemplate,
    bool IsPrivate,
    bool IsFork,
    bool IsArchived,
    int ForkCount,
    int StargazerCount,
    int OpenIssuesCount,
    string DefaultBranch,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? PushedAt,
    bool HasDiscussions,
    bool HasIssues,
    bool HasWiki,
    bool HasDownloads,
    bool HasPages,
    int SubscribersCount,
    long Size,
    ImmutableArray<string> Topics,
    RepositoryVisibility? Visibility
);
