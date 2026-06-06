using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public class RepoMiner(ILogger<RepoMiner> logger, string repoOwner, string repoName, RepoMinerScope scope)
    : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Issue> issues = [];
    private readonly ConcurrentDictionary<string, PullRequest> pullRequests = [];
    private readonly ConcurrentDictionary<string, Discussion> discussions = [];
    private readonly ConcurrentDictionary<string, Milestone> milestones = [];
    private readonly MiningOptions options = new();

    public string RepoOwner { get; } = repoOwner;
    public string RepoName { get; } = repoName;
    public RepoMinerScope Scope { get; } = scope;
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();
    public GitHubClientCoordinator<GitHubRestClientWrapper> Rest { get; private set; } = null!;
    public GitHubClientCoordinator<GitHubGraphQlClientWrapper> GraphQl { get; private set; } = null!;

    public async Task<MiningResult?> MineRepo(CancellationToken cancellationToken = default)
    {
        await Initialize(cancellationToken);

        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine the repo without the GitHub HTTP API client.");
        }

        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Started mining '{RepoOwner}/{RepoName}'.", RepoOwner, RepoName);
        var repo = await Rest.Execute(
            (rest, ct) => rest.Client.Repos[RepoOwner][RepoName].GetAsync(cancellationToken: ct),
            ct: cancellationToken
        );
        if (repo is null)
        {
            logger.LogInformation("Repo '{RepoOwner}/{RepoName}' could not be found.", RepoOwner, RepoName);
            return null;
        }

        var cloneUrl = repo.CloneUrl ?? $"https://github.com/{RepoOwner}/{RepoName}.git";

        var tasks = new List<Task>(6);

        ClocInfo? cloc = null;
        if (Scope.HasFlag(RepoMinerScope.LoCStats))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        cloc = await Utils.GetCloc(cloneUrl, logger, cancellationToken);
                        if (cloc is null)
                        {
                            logger.LogError(
                                "Failed to cloc '{RepoOwner}/{RepoName}'. No file and line count data will be available.",
                                RepoOwner,
                                RepoName
                            );
                        }
                        else
                        {
                            logger.LogInformation(
                                "Repository '{RepoOwner}/{RepoName}' has {FileCount} files.",
                                RepoOwner,
                                RepoName,
                                cloc.Header.FileCount
                            );
                        }
                    },
                    cancellationToken
                )
            );
        }

        GitLocInfo? gitLoc = null;
        if (Scope.HasFlag(RepoMinerScope.GitLoCStats))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        gitLoc = await Utils.GetGitLoc(cloneUrl, logger, cancellationToken);
                        if (gitLoc is null)
                        {
                            logger.LogError(
                                "Failed to count LoC '{RepoOwner}/{RepoName}' across its history. "
                                + "No file and line count data will be available.",
                                RepoOwner,
                                RepoName
                            );
                        }
                        else
                        {
                            logger.LogInformation(
                                "Repository '{RepoOwner}/{RepoName}' has {AddedLineCount} added lines "
                                + "and {DeletedLineCount} deleted lines.",
                                RepoOwner,
                                RepoName,
                                gitLoc.AddedLineCount,
                                gitLoc.DeletedLineCount
                            );
                        }
                    },
                    cancellationToken
                )
            );
        }

        if (Scope.HasFlag(RepoMinerScope.Issues))
        {
            tasks.Add(MineIssues(cancellationToken));
        }

        if (Scope.HasFlag(RepoMinerScope.PullRequests))
        {
            tasks.Add(MinePullRequests(cancellationToken));
        }

        if (Scope.HasFlag(RepoMinerScope.Discussions))
        {
            tasks.Add(MineDiscussions(cancellationToken));
        }

        if (Scope.HasFlag(RepoMinerScope.Milestones))
        {
            tasks.Add(MineMilestones(cancellationToken));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        var completedAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Mining complete in {Duration}.",
            completedAt - startedAt
        );

        return new MiningResult(
            MiningStartedAt: startedAt,
            MiningCompletedAt: completedAt,
            Repository: GitHubRestMapping.MapRepository(repo) with { Cloc = cloc, GitLoc = gitLoc },
            Issues: issues.ToImmutableDictionary(),
            PullRequests: pullRequests.ToImmutableDictionary(),
            Discussions: discussions.ToImmutableDictionary(),
            Milestones: milestones.ToImmutableDictionary()
        );
    }

    private async Task Initialize(CancellationToken ct = default)
    {
        issues.Clear();
        pullRequests.Clear();
        discussions.Clear();
        milestones.Clear();

        Configuration = Utils.BuildConfiguration();
        Configuration.Bind(options);
        if (options.GitHubTokens is null || options.GitHubTokens.Count == 0)
        {
            throw new InvalidOperationException("Cannot mine GitHub repositories without any tokens.");
        }

        var gitHubTokens = options.GitHubTokens
            .Where(t => t.Value.Token.StartsWith("github"))
            .Select(p => new GitHubToken(p.Key, p.Value.Token, p.Value.HttpLimit, p.Value.GraphQlLimit))
            .ToImmutableArray();

        Rest = await GitHubClientCoordinator<GitHubRestClientWrapper>.Create(
            logger: logger,
            clientFactory: GitHubRestClientWrapper.Create,
            kind: "REST",
            authTokens: gitHubTokens,
            ct: ct
        );

        GraphQl = await GitHubClientCoordinator<GitHubGraphQlClientWrapper>.Create(
            logger: logger,
            clientFactory: GitHubGraphQlClientWrapper.Create,
            kind: "REST",
            authTokens: gitHubTokens,
            ct: ct
        );

        logger.LogInformation("Initialized");
    }

    private async Task MineIssues(CancellationToken cancellationToken = default)
    {
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine issues without the GitHub HTTP API client.");
        }

        logger.LogInformation("Logging issues.");

        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghIssues = await Rest.Execute(
                async (rest, ct) =>
                {
                    logger.LogDebug(
                        "Mining issues page {PageIndex} with REST client '{TokenName}' ({Remaining} remaining).",
                        pageIndex,
                        rest.Token.Name,
                        rest.Limiter.EffectiveRemaining
                    );
                    var issuesResponse = await rest.HttpClient.SendAsync(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            $"/repos/{RepoOwner}/{RepoName}/issues?page={pageIndex}&per_page=100&state=all"
                        ),
                        ct
                    );
                    issuesResponse.EnsureSuccessStatusCode();
                    return await issuesResponse.Content.ReadFromJsonAsync<List<GitHubIssueJson>>(ct);
                },
                ct: cancellationToken
            ) ?? [];
            if (ghIssues.Count == 0)
            {
                break;
            }

            foreach (var ghIssue in ghIssues)
            {
                var issue = GitHubRestMapping.MapIssue(ghIssue);
                if (ghIssue.PullRequest is not null)
                {
                    continue;
                }

                issues.TryAdd(issue.Id, issue);
            }

            if (Scope.HasFlag(RepoMinerScope.IssueComments))
            {
                await Task.WhenAll(
                    ghIssues
                        .Where(i => i.PullRequest is null)
                        .Select(async ghIssue =>
                            {
                                if (ghIssue.Number is null)
                                {
                                    return;
                                }

                                var comments = await MineComments(ghIssue.Number.Value, cancellationToken);
                                var issueId = ghIssue.NodeId ?? ghIssue.Id?.ToString(CultureInfo.InvariantCulture);
                                if (issueId is null)
                                {
                                    return;
                                }

                                issues.AddOrUpdate(
                                    issueId,
                                    _ => throw new InvalidOperationException(),
                                    (_, issue) => issue with { Comments = comments }
                                );
                            }
                        )
                );
            }

            if (Scope.HasFlag(RepoMinerScope.IssueEvents))
            {
                await Task.WhenAll(
                    ghIssues
                        .Where(i => i.PullRequest is null)
                        .Select(async ghIssue =>
                            {
                                if (ghIssue.Number is null)
                                {
                                    return;
                                }

                                var events = await MineIssueEvents(ghIssue.Number.Value, cancellationToken);
                                var issueId = ghIssue.NodeId ?? ghIssue.Id?.ToString(CultureInfo.InvariantCulture);
                                if (issueId is null)
                                {
                                    return;
                                }

                                issues.AddOrUpdate(
                                    issueId,
                                    _ => throw new InvalidOperationException(),
                                    (_, issue) => issue with { Events = events }
                                );
                            }
                        )
                );
            }
        }
    }

    private async Task MinePullRequests(CancellationToken cancellationToken = default)
    {
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine pull requests without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining pull requests.");

        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghPrs = await Rest.Execute(
                (rest, ct) =>
                {
                    logger.LogDebug(
                        "Mining pull requests page {PageIndex} with REST client '{TokenName}' ({Remaining} remaining).",
                        pageIndex,
                        rest.Token.Name,
                        rest.Limiter.EffectiveRemaining
                    );
                    return rest.Client.Repos[RepoOwner][RepoName].Pulls.GetAsync(
                        config =>
                        {
                            config.QueryParameters.Page = pageIndex;
                            config.QueryParameters.PerPage = 100;
                            config.QueryParameters.StateAsGetStateQueryParameterType =
                                Repos.Item.Item.Pulls.GetStateQueryParameterType.All;
                        },
                        ct
                    );
                },
                ct: cancellationToken
            ) ?? [];
            if (ghPrs.Count == 0)
            {
                break;
            }

            var ghPrDetails = await Task.WhenAll(
                ghPrs.Where(pr => pr.Number.HasValue)
                    .Select(ghPr => Rest.Execute(
                            async (rest, ct) =>
                            {
                                logger.LogDebug(
                                    "Mining pull request details #{Number} with REST client '{TokenName}' ({Remaining} remaining).",
                                    ghPr.Number,
                                    rest.Token.Name,
                                    rest.Limiter.EffectiveRemaining
                                );
                                try
                                {
                                    return await rest.Client.Repos[RepoOwner][RepoName].Pulls[ghPr.Number!.Value]
                                        .GetAsync(cancellationToken: ct);
                                }
                                catch (ApiException ex)
                                {
                                    // this happens for some reason :/
                                    logger.LogError(
                                        ex,
                                        "Failed to obtain pull request details #{Number}. Ignoring.",
                                        ghPr.Number
                                    );
                                    return null;
                                }
                            },
                            ct: cancellationToken
                        )
                    )
            );

            foreach (var ghPrDetail in ghPrDetails)
            {
                if (ghPrDetail is null)
                {
                    continue;
                }

                var pr = GitHubRestMapping.MapPullRequest(ghPrDetail);
                pullRequests.TryAdd(pr.Id, pr);
            }

            if (Scope.HasFlag(RepoMinerScope.PullRequestComments))
            {
                await Task.WhenAll(
                    ghPrs.Select(async ghPr =>
                        {
                            if (ghPr.Number is null)
                            {
                                return;
                            }

                            var comments = await MineComments(ghPr.Number.Value, cancellationToken);
                            var prId = ghPr.NodeId ?? ghPr.Id?.ToString(CultureInfo.InvariantCulture);
                            if (prId is null)
                            {
                                return;
                            }

                            if (!pullRequests.ContainsKey(prId))
                            {
                                logger.LogError(
                                    "Cannot append comments to pull request #{Number} because it was not mined.",
                                    ghPr.Number
                                );
                                return;
                            }

                            pullRequests.AddOrUpdate(
                                prId,
                                _ => throw new InvalidOperationException(),
                                (_, pr) => pr with { Comments = comments }
                            );
                        }
                    )
                );
            }

            if (Scope.HasFlag(RepoMinerScope.PullRequestEvents))
            {
                await Task.WhenAll(
                    ghPrs.Select(async ghPr =>
                        {
                            if (ghPr.Number is null)
                            {
                                return;
                            }

                            var events = await MineIssueEvents(ghPr.Number.Value, cancellationToken);
                            var prId = ghPr.NodeId ?? ghPr.Id?.ToString(CultureInfo.InvariantCulture);
                            if (prId is null)
                            {
                                return;
                            }

                            if (!pullRequests.ContainsKey(prId))
                            {
                                logger.LogError(
                                    "Cannot append events to pull request #{Number} because it was not mined.",
                                    ghPr.Number
                                );
                                return;
                            }

                            pullRequests.AddOrUpdate(
                                prId,
                                _ => throw new InvalidOperationException(),
                                (_, pr) => pr with { Events = events }
                            );
                        }
                    )
                );
            }
        }
    }

    private async Task MineDiscussions(CancellationToken cancellationToken = default)
    {
        if (GraphQl is null)
        {
            throw new InvalidOperationException("Cannot mine discussions without the GraphQL client.");
        }

        logger.LogDebug("Mining discussions");
        string? cursor = null;
        do
        {
            var queryResult = await GraphQl.Execute(
                execute: (graphQl, ct) => graphQl.Client.DiscussionQuery.ExecuteAsync(
                    RepoOwner,
                    RepoName,
                    after: cursor,
                    cancellationToken: ct
                )!,
                ct: cancellationToken
            );

            if (queryResult is null || queryResult.Errors.Count > 0)
            {
                logger.LogError(
                    "Failed to mine discussions of '{RepoOwner}/{RepoName}'. The query returned errors: {Errors}",
                    RepoOwner,
                    RepoName,
                    queryResult?.Errors.Select(e => e.Message)
                );
                break;
            }

            if (queryResult.Data?.Repository is null)
            {
                logger.LogError(
                    "Failed to mine discussions of '{RepoOwner}/{RepoName}'. The query returned null.",
                    RepoOwner,
                    RepoName
                );
                break;
            }

            if (cursor is null)
            {
                logger.LogInformation(
                    "Repository '{RepoOwner}/{RepoName}' has {DiscussionCount} discussions.",
                    RepoOwner,
                    RepoName,
                    queryResult.Data.Repository.Discussions.TotalCount
                );
            }

            foreach (var octoDiscussion in queryResult.Data.Repository.Discussions.Edges ?? [])
            {
                if (octoDiscussion?.Node is null)
                {
                    continue;
                }

                discussions.TryAdd(octoDiscussion.Node.Id, OctokitMapping.MapDiscussion(octoDiscussion.Node));
            }

            if (Scope.HasFlag(RepoMinerScope.DiscussionComments)
                && queryResult.Data.Repository.Discussions.Edges is not null)
            {
                await Task.WhenAll(
                    queryResult.Data.Repository.Discussions.Edges
                        .Where(d => d?.Node != null)
                        .Select(async octoDiscussion =>
                            {
                                var comments = await MineDiscussionComments(
                                    octoDiscussion!.Node!.Id,
                                    cancellationToken
                                );
                                discussions.AddOrUpdate(
                                    octoDiscussion.Node.Id,
                                    _ => throw new InvalidOperationException(),
                                    (_, pr) => pr with { Comments = comments }
                                );
                            }
                        )
                );
            }

            cursor = queryResult.Data.Repository.Discussions.PageInfo.HasNextPage
                ? queryResult.Data.Repository.Discussions.PageInfo.EndCursor
                : null;
        } while (cursor is not null);
    }

    private async Task MineMilestones(CancellationToken cancellationToken = default)
    {
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine milestones event without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining milestones.");

        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghMilestones = await Rest.Execute(
                (rest, ct) =>
                {
                    logger.LogDebug(
                        "Mining milestones page {PageIndex} with REST client '{TokenName}' ({Remaining} remaining).",
                        pageIndex,
                        rest.Token.Name,
                        rest.Limiter.EffectiveRemaining
                    );
                    return rest.Client.Repos[RepoOwner][RepoName].Milestones.GetAsync(
                        config =>
                        {
                            config.QueryParameters.Page = pageIndex;
                            config.QueryParameters.PerPage = 100;
                            config.QueryParameters.StateAsGetStateQueryParameterType =
                                Repos.Item.Item.Milestones.GetStateQueryParameterType.All;
                        },
                        ct
                    );
                },
                ct: cancellationToken
            ) ?? [];
            if (ghMilestones.Count == 0)
            {
                break;
            }

            foreach (var ghMilestone in ghMilestones)
            {
                var milestone = GitHubRestMapping.MapMilestone(ghMilestone);
                milestones.TryAdd(milestone.Id, milestone);
            }
        }
    }

    private async Task<ImmutableArray<Comment>> MineComments(long number, CancellationToken cancellationToken = default)
    {
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot issue comments without the GitHub HTTP API client.");
        }

        var pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        while (true)
        {
            pageIndex++;

            var comments = await Rest.Execute(
                (rest, ct) =>
                {
                    logger.LogDebug(
                        "Mining comments page {PageIndex} for #{Number} with REST client '{TokenName}' ({Remaining} remaining).",
                        pageIndex,
                        number,
                        rest.Token.Name,
                        rest.Limiter.EffectiveRemaining
                    );
                    return rest.Client.Repos[RepoOwner][RepoName].Issues[number].Comments.GetAsync(
                        config =>
                        {
                            config.QueryParameters.Page = pageIndex;
                            config.QueryParameters.PerPage = 100;
                        },
                        ct
                    );
                },
                ct: cancellationToken
            ) ?? [];
            if (comments.Count == 0)
            {
                break;
            }

            builder.AddRange(comments.Select(c => GitHubRestMapping.MapIssueComment(c)));
        }

        return builder.ToImmutable();
    }

    private async Task<ImmutableArray<Comment>> MineDiscussionComments(
        string nodeId,
        CancellationToken cancellationToken = default
    )
    {
        if (GraphQl is null)
        {
            throw new InvalidOperationException("Cannot mine discussion comments without the GraphQL client.");
        }

        logger.LogDebug("Mining discussion comments for {DiscussionId}.", nodeId);
        string? cursor = null;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        do
        {
            var queryResult = await GraphQl.Execute(
                execute: (graphQl, ct) => graphQl.Client.DiscussionCommentQuery.ExecuteAsync(nodeId, cursor, ct)!,
                ct: cancellationToken
            );

            if (queryResult is null || queryResult.Errors.Count > 0)
            {
                logger.LogError(
                    "Failed to mine discussion comments of '{DiscussionId}'. The query returned errors: {Errors}",
                    nodeId,
                    queryResult?.Errors.Select(e => e.Message)
                );
                break;
            }

            if (queryResult.Data?.Node is not IDiscussionCommentQuery_Node_Discussion discussionNode)
            {
                logger.LogError(
                    "Failed to mine discussions comments of '{DiscussionId}'. The query returned null.",
                    nodeId
                );
                break;
            }

            foreach (var discussionComment in discussionNode.Comments.Edges ?? [])
            {
                if (discussionComment?.Node is null)
                {
                    continue;
                }

                builder.Add(OctokitMapping.MapDiscussionComment(discussionComment.Node));
            }

            cursor = discussionNode.Comments.PageInfo.HasNextPage
                ? discussionNode.Comments.PageInfo.EndCursor
                : null;
        } while (cursor is not null);

        return builder.ToImmutable();
    }

    private async Task<ImmutableArray<IssueEvent>> MineIssueEvents(
        long number,
        CancellationToken cancellationToken = default
    )
    {
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine issue/PR events without the GitHub HTTP API client.");
        }

        int pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<IssueEvent>();
        while (true)
        {
            pageIndex++;

            var events = await Rest.Execute(
                async (rest, ct) =>
                {
                    logger.LogDebug(
                        "Mining issue/PR events page {PageIndex} for #{Number} with REST client '{TokenName}' ({Remaining} remaining).",
                        pageIndex,
                        number,
                        rest.Token.Name,
                        rest.Limiter.EffectiveRemaining
                    );
                    using var timelineResponse = await rest.HttpClient.SendAsync(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            $"/repos/{RepoOwner}/{RepoName}/issues/{number}/timeline?page={pageIndex}&per_page=100"
                        ),
                        ct
                    );
                    timelineResponse.EnsureSuccessStatusCode();
                    return await timelineResponse.Content.ReadFromJsonAsync<List<GitHubTimelineEventJson>>(ct);
                },
                ct: cancellationToken
            ) ?? [];
            if (events.Count == 0)
            {
                break;
            }

            builder.AddRange(events.Select(GitHubRestMapping.MapTimelineEventInfo));
        }

        return builder.ToImmutable();
    }

    public async ValueTask DisposeAsync()
    {
        await Rest.DisposeAsync();
        await GraphQl.DisposeAsync();
    }
}
