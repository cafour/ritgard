using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Ritgard.Mining.GitHub;
using StrawberryShake;

namespace Ritgard.Mining;

public class RepoMiner(ILogger<RepoMiner> logger, string repoOwner, string repoName, RepoMinerScope scope)
{
    public const string GitHubApiVersion = "2026-03-10";

    private readonly ConcurrentDictionary<string, Issue> issues = [];
    private readonly ConcurrentDictionary<string, PullRequest> pullRequests = [];
    private readonly ConcurrentDictionary<string, Discussion> discussions = [];
    private readonly ConcurrentDictionary<string, Milestone> milestones = [];
    private readonly MiningOptions options = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> httpCooldowns = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> graphQlCooldowns = [];
    private IAsyncDisposable ghqDisposable = null!;
    private ImmutableDictionary<string, GhToken> githubTokens = ImmutableDictionary<string, GhToken>.Empty;
    private GhToken currentGraphQlToken = null!;
    private GhToken currentHttpToken = null!;
    private int httpSpent = 0;
    private int graphQlSpent = 0;
    private readonly SemaphoreSlim httpLock = new(1, 1);
    private readonly SemaphoreSlim graphQlLock = new(1, 1);

    private ImmutableDictionary<string, GitHubRestClient> githubClients =
        ImmutableDictionary<string, GitHubRestClient>.Empty;

    public string RepoOwner { get; } = repoOwner;
    public string RepoName { get; } = repoName;
    public RepoMinerScope Scope { get; } = scope;
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();
    public GitHubRestClient? Http { get; private set; }
    public GitHubGraphQLClient? GraphQl { get; private set; }

    public async Task<MiningResult?> MineRepo(CancellationToken cancellationToken = default)
    {
        await Initialize(cancellationToken);

        if (Http is null)
        {
            throw new InvalidOperationException("Cannot mine the repo without the GitHub HTTP API client.");
        }

        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Started mining '{RepoOwner}/{RepoName}'.", RepoOwner, RepoName);
        var repo = await QueryHttp(
            ct => Http.Repos[RepoOwner][RepoName].GetAsync(cancellationToken: ct),
            cancellationToken: cancellationToken
        );
        if (repo is null)
        {
            logger.LogInformation("Repo '{RepoOwner}/{RepoName}' could not be found.", RepoOwner, RepoName);
            return null;
        }

        var cloneUrl = repo.CloneUrl ?? $"https://github.com/{RepoOwner}/{RepoName}.git";

        var tasks = new List<Task>(6);

        ClocInfo? cloc = null;
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

        GitLocInfo? gitLoc = null;
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
            Repository: GitHubRestMapping.MapRepository(repo) with { Cloc = cloc, GitLoc = gitLoc},
            Issues: issues.ToImmutableDictionary(),
            PullRequests: pullRequests.ToImmutableDictionary(),
            Discussions: discussions.ToImmutableDictionary(),
            Milestones: milestones.ToImmutableDictionary()
        );
    }

    private async Task Initialize(CancellationToken cancellationToken = default)
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

        githubTokens = options.GitHubTokens
            .Where(t => t.Value.Token.StartsWith("github"))
            .ToImmutableDictionary(
                p => p.Key,
                p => new GhToken(p.Key, p.Value.Token, p.Value.HttpLimit, p.Value.GraphQlLimit)
            );

        githubClients = githubTokens.ToImmutableDictionary(
            p => p.Key,
            p => CreateGitHubRestClient(p.Key, p.Value.Token)
        );

        foreach (var token in githubTokens.Values)
        {
            await SetCooldownsFor(token);
        }

        await RefreshHttpApi(cancellationToken);
        await RefreshGraphQlApi(cancellationToken);

        logger.LogInformation("Initialized");
    }

    private async Task MineIssues(CancellationToken cancellationToken = default)
    {
        if (Http is null)
        {
            throw new InvalidOperationException("Cannot mine issues without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining issues");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghIssues = await QueryHttp(
                ct => Http.Repos[RepoOwner][RepoName].Issues.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                        config.QueryParameters.StateAsGetStateQueryParameterType =
                            Repos.Item.Item.Issues.GetStateQueryParameterType.All;
                    },
                    ct
                ),
                cancellationToken: cancellationToken
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
        if (Http is null)
        {
            throw new InvalidOperationException("Cannot mine pull requests without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining pull requests");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghPrs = await QueryHttp(
                ct => Http.Repos[RepoOwner][RepoName].Pulls.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                        config.QueryParameters.StateAsGetStateQueryParameterType =
                            Repos.Item.Item.Pulls.GetStateQueryParameterType.All;
                    },
                    ct
                ),
                cancellationToken: cancellationToken
            ) ?? [];
            if (ghPrs.Count == 0)
            {
                break;
            }

            foreach (var ghPr in ghPrs)
            {
                if (ghPr.Number is null)
                {
                    continue;
                }

                var ghPrDetails = await QueryHttp(
                    ct => Http.Repos[RepoOwner][RepoName].Pulls[ghPr.Number.Value].GetAsync(cancellationToken: ct),
                    cancellationToken: cancellationToken
                );
                if (ghPrDetails is null)
                {
                    continue;
                }

                var pr = GitHubRestMapping.MapPullRequest(ghPrDetails);
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

        logger.LogInformation("Mining discussions");
        string? cursor = null;
        do
        {
            var queryResult = await QueryGraphQl(
                execute: ct => GraphQl.DiscussionQuery.ExecuteAsync(
                    RepoOwner,
                    RepoName,
                    after: cursor,
                    cancellationToken: ct
                ),
                errorsAccessor: r => r.Errors,
                costAccessor: r => r.Data?.RateLimit?.Cost ?? 1,
                cancellationToken: cancellationToken
            );

            if (queryResult.Errors.Count > 0)
            {
                logger.LogError(
                    "Failed to mine discussions of '{RepoOwner}/{RepoName}'. The query returned errors: {Errors}",
                    RepoOwner,
                    RepoName,
                    queryResult.Errors.Select(e => e.Message)
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
        if (Http is null)
        {
            throw new InvalidOperationException("Cannot mine milestones event without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining milestones");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghMilestones = await QueryHttp(
                ct => Http.Repos[RepoOwner][RepoName].Milestones.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                        config.QueryParameters.StateAsGetStateQueryParameterType =
                            Repos.Item.Item.Milestones.GetStateQueryParameterType.All;
                    },
                    ct
                ),
                cancellationToken: cancellationToken
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

    private async Task<ImmutableArray<Comment>> MineComments(int number, CancellationToken cancellationToken = default)
    {
        if (Http is null)
        {
            throw new InvalidOperationException("Cannot issue comments without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining comments for #{Number}", number);
        var pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        while (true)
        {
            pageIndex++;

            var comments = await QueryHttp(
                ct => Http.Repos[RepoOwner][RepoName].Issues[number].Comments.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                    },
                    ct
                ),
                cancellationToken: cancellationToken
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

        logger.LogInformation("Mining discussion comments for {DiscussionId}.", nodeId);
        string? cursor = null;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        do
        {
            var queryResult = await QueryGraphQl(
                execute: ct => GraphQl.DiscussionCommentQuery.ExecuteAsync(nodeId, cursor, ct),
                errorsAccessor: r => r.Errors,
                costAccessor: r => r.Data?.RateLimit?.Cost ?? 1,
                cancellationToken: cancellationToken
            );

            if (queryResult.Errors.Count > 0)
            {
                logger.LogError(
                    "Failed to mine discussion comments of '{DiscussionId}'. The query returned errors: {Errors}",
                    nodeId,
                    queryResult.Errors.Select(e => e.Message)
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

    private async Task<ImmutableArray<IssueEvent>> MineIssueEvents(int number, CancellationToken cancellationToken = default)
    {
        if (Http is null)
        {
            throw new InvalidOperationException("Cannot mine issue event without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining issue events for #{Number}", number);
        // var events = await GH.Issue.Events.GetAllForIssue(repoId, number);
        int pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<IssueEvent>();
        while (true)
        {
            pageIndex++;

            var events = await QueryHttp(
                ct => Http.Repos[RepoOwner][RepoName].Issues[number].Timeline.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                    },
                    ct
                ),
                cancellationToken: cancellationToken
            ) ?? [];
            if (events.Count == 0)
            {
                break;
            }

            builder.AddRange(events.Select(e => GitHubRestMapping.MapTimelineEventInfo(e)));
        }

        return builder.ToImmutable();
    }

    private async Task SetCooldownsFor(GhToken token)
    {
        var gh = githubClients[token.Name];
        var limits = await gh.Rate_limit.GetAsync();
        var core = limits?.Resources?.Core;
        var graphql = limits?.Resources?.Graphql;

        if (core is not null)
        {
            var httpThreshold = token.HttpLimit == -1 ? 0 : (core.Limit ?? 0) - token.HttpLimit;
            if ((core.Remaining ?? 0) <= httpThreshold)
            {
                var resetAt = DateTimeOffset.FromUnixTimeSeconds(core.Reset ?? 0);
                logger.LogInformation(
                    "Reached {Remaining} remaining HTTP requests, which is below the {Threshold} threshold of '{TokenName}'.",
                    core.Remaining ?? 0,
                    httpThreshold,
                    token.Name
                );
                httpCooldowns.AddOrUpdate(
                    token.Name,
                    _ => resetAt,
                    (_, existing) => Utils.Max(existing, resetAt)
                );
            }
            else
            {
                httpCooldowns.TryAdd(token.Name, default);
            }
        }
        else
        {
            httpCooldowns.TryAdd(token.Name, default);
        }

        if (graphql is not null)
        {
            var graphQlThreshold = token.GraphQlLimit == -1 ? 0 : (graphql.Limit ?? 0) - token.GraphQlLimit;
            if ((graphql.Remaining ?? 0) <= graphQlThreshold)
            {
                var resetAt = DateTimeOffset.FromUnixTimeSeconds(graphql.Reset ?? 0);
                logger.LogInformation(
                    "Reached {Remaining} remaining GraphQL requests, which is below the {Threshold} threshold of '{TokenName}'.",
                    graphql.Remaining ?? 0,
                    graphQlThreshold,
                    token.Name
                );
                graphQlCooldowns.AddOrUpdate(
                    token.Name,
                    _ => resetAt,
                    (_, existing) => Utils.Max(existing, resetAt)
                );
            }
            else
            {
                graphQlCooldowns.TryAdd(token.Name, default);
            }
        }
        else
        {
            graphQlCooldowns.TryAdd(token.Name, default);
        }
    }

    private async Task RefreshHttpApi(CancellationToken cancellationToken = default)
    {
        var (tokenName, tokenCooldown) = httpCooldowns
            .OrderBy(p => p.Key)
            .MinBy(p => p.Value);
        if (tokenCooldown > DateTimeOffset.UtcNow)
        {
            logger.LogInformation(
                "HTTP API is waiting for token '{TokenName}' to cool down till {CooldownTime}.",
                tokenName,
                tokenCooldown.ToLocalTime()
            );
            await Task.Delay(tokenCooldown - DateTimeOffset.UtcNow, cancellationToken);
        }

        if (currentHttpToken?.Name == tokenName)
        {
            return;
        }

        logger.LogInformation("Switching GitHub HTTP API token to '{TokenName}'.", tokenName);
        Http = githubClients[tokenName];
        currentHttpToken = githubTokens[tokenName];
        httpSpent = 0;
    }

    private async Task RefreshGraphQlApi(CancellationToken cancellationToken = default)
    {
        var (tokenName, tokenCooldown) = graphQlCooldowns
            .OrderBy(p => p.Key)
            .MinBy(p => p.Value);
        if (tokenCooldown > DateTimeOffset.UtcNow)
        {
            logger.LogInformation(
                "GraphQL API is waiting for token '{TokenName}' to cool down till {CooldownTime}.",
                tokenName,
                tokenCooldown.ToLocalTime()
            );
            await Task.Delay(tokenCooldown - DateTimeOffset.UtcNow, cancellationToken);
        }

        if (currentGraphQlToken?.Name == tokenName)
        {
            return;
        }

        if (GraphQl is not null)
        {
            await ghqDisposable.DisposeAsync();
            GraphQl = null!;
        }

        logger.LogInformation("Switching GitHub GraphQL API token to '{TokenName}'.", tokenName);
        var tokenValue = options.GitHubTokens[tokenName];
        (GraphQl, ghqDisposable) = Utils.CreateGitHubGraphQLClient(tokenValue.Token);
        currentGraphQlToken = githubTokens[tokenName];
        graphQlSpent = 0;
    }

    private async Task EnsureHttpAvailable(CancellationToken cancellationToken = default)
    {
        var tmpToken = currentHttpToken;
        await httpLock.WaitAsync(cancellationToken);
        try
        {
            if (currentHttpToken != tmpToken)
            {
                // some other thread switched the token first
                return;
            }

            await SetCooldownsFor(currentHttpToken);
            await RefreshHttpApi(cancellationToken);
        }
        finally
        {
            httpLock.Release();
        }
    }

    private async Task EnsureGraphQlAvailable(CancellationToken cancellationToken = default)
    {
        var tmpToken = currentGraphQlToken;
        await graphQlLock.WaitAsync(cancellationToken);
        try
        {
            if (currentGraphQlToken != tmpToken)
            {
                // some other thread switched the token first
                return;
            }

            await SetCooldownsFor(currentGraphQlToken);
            await RefreshGraphQlApi(cancellationToken);
        }
        finally
        {
            graphQlLock.Release();
        }
    }

    private async Task<TResult> QueryGraphQl<TResult>(
        Func<CancellationToken, Task<TResult>> execute,
        Func<TResult, IReadOnlyList<IClientError>> errorsAccessor,
        Func<TResult, int> costAccessor,
        CancellationToken cancellationToken = default
    )
    {
        if (currentHttpToken.GraphQlLimit != -1 && graphQlSpent >= currentHttpToken.GraphQlLimit)
        {
            await EnsureGraphQlAvailable(cancellationToken);
        }

        while (true)
        {
            var queryResult = await execute(cancellationToken);
            var cost = costAccessor(queryResult);
            Interlocked.Add(ref graphQlSpent, cost);
            var errors = errorsAccessor(queryResult);
            if (errors.Count == 0)
            {
                return queryResult;
            }

            if (errors is [{ Code: "graphql_rate_limit" }])
            {
                await EnsureGraphQlAvailable(cancellationToken);
            }

            return queryResult;
        }
    }

    private async Task<TResult> QueryHttp<TResult>(
        Func<CancellationToken, Task<TResult>> execute,
        int cost = 1,
        CancellationToken cancellationToken = default
    )
    {
        if (currentHttpToken.HttpLimit != -1 && httpSpent >= currentHttpToken.HttpLimit)
        {
            await EnsureHttpAvailable(cancellationToken);
        }

        while (true)
        {
            try
            {
                Interlocked.Add(ref httpSpent, cost);
                return await execute(cancellationToken);
            }
            catch (ApiException ex) when (IsRateLimitException(ex))
            {
                await EnsureHttpAvailable(cancellationToken);
            }
        }
    }

    private static bool IsRateLimitException(ApiException exception)
    {
        if (exception.ResponseStatusCode == 429)
        {
            return true;
        }

        return exception.ResponseStatusCode == 403
               && exception.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubRestClient CreateGitHubRestClient(string tokenName, string token)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ritgard-{tokenName}");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GitHubApiVersion);

        var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient);
        return new GitHubRestClient(adapter);
    }

    private record GhToken(
        string Name,
        string Token,
        int HttpLimit,
        int GraphQlLimit
    );
}
