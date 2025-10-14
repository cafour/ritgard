using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using Ritgard.Mining.GitHub;
using StrawberryShake;

namespace Ritgard.Mining;

public class RepoMiner(ILogger<RepoMiner> logger, string repoOwner, string repoName, RepoMinerScope scope)
{
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

    private ImmutableDictionary<string, GitHubClient> githubClients =
        ImmutableDictionary<string, GitHubClient>.Empty;

    public string RepoOwner { get; } = repoOwner;
    public string RepoName { get; } = repoName;
    public RepoMinerScope Scope { get; } = scope;
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();
    public GitHubClient? Http { get; private set; }
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
        var octoRepo = await QueryHttp(
            _ => Http.Repository.Get(RepoOwner, RepoName),
            cancellationToken: cancellationToken
        );
        if (octoRepo is null)
        {
            logger.LogInformation("Repo '{RepoOwner}/{RepoName}' could not be found.", RepoOwner, RepoName);
            return null;
        }

        var tasks = new List<Task>(4);

        if (Scope.HasFlag(RepoMinerScope.Issues))
        {
            tasks.Add(MineIssues(octoRepo.Id));
        }

        if (Scope.HasFlag(RepoMinerScope.PullRequests))
        {
            tasks.Add(MinePullRequests(octoRepo.Id));
        }

        if (Scope.HasFlag(RepoMinerScope.Discussions))
        {
            tasks.Add(MineDiscussions(cancellationToken));
        }

        if (Scope.HasFlag(RepoMinerScope.Milestones))
        {
            tasks.Add(MineMilestones(octoRepo.Id));
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
            Repository: OctokitMapping.MapRepository(octoRepo),
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

        githubTokens = options.GitHubTokens.ToImmutableDictionary(
            p => p.Key,
            p => new GhToken(p.Key, p.Value.Token, p.Value.HttpLimit, p.Value.GraphQlLimit)
        );

        githubClients = githubTokens.ToImmutableDictionary(
            p => p.Key,
            p => new GitHubClient(new ProductHeaderValue($"ritgard-{p.Key}"))
            {
                Credentials = new Credentials(p.Value.Token)
            }
        );

        foreach (var token in githubTokens.Values)
        {
            await SetCooldownsFor(token);
        }

        await RefreshHttpApi(cancellationToken);
        await RefreshGraphQlApi(cancellationToken);

        logger.LogInformation("Initialized");
    }

    private async Task MineIssues(long repoId)
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

            var octoIssues = await QueryHttp(_ => Http.Issue.GetAllForRepository(
                    repoId,
                    new RepositoryIssueRequest
                    {
                        State = ItemStateFilter.All
                    },
                    new ApiOptions
                    {
                        PageCount = 1,
                        PageSize = 100,
                        StartPage = pageIndex
                    }
                )
            );
            if (octoIssues.Count == 0)
            {
                break;
            }

            foreach (var octoIssue in octoIssues)
            {
                var issue = OctokitMapping.MapIssue(octoIssue);
                if (octoIssue.PullRequest is not null)
                {
                    continue;
                }

                issues.TryAdd(octoIssue.NodeId, issue);
            }

            if (Scope.HasFlag(RepoMinerScope.IssueComments))
            {
                await Task.WhenAll(
                    octoIssues
                        .Where(i => i.PullRequest is null)
                        .Select(async octoIssue =>
                            {
                                var comments = await MineComments(repoId, octoIssue.Number);
                                issues.AddOrUpdate(
                                    octoIssue.NodeId,
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
                    octoIssues
                        .Where(i => i.PullRequest is null)
                        .Select(async octoIssue =>
                            {
                                var events = await MineIssueEvents(repoId, octoIssue.Number);
                                issues.AddOrUpdate(
                                    octoIssue.NodeId,
                                    _ => throw new InvalidOperationException(),
                                    (_, issue) => issue with { Events = events }
                                );
                            }
                        )
                );
            }
        }
    }

    private async Task MinePullRequests(long repoId)
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

            var octoPrs = await QueryHttp(_ => Http.PullRequest.GetAllForRepository(
                    repoId,
                    new PullRequestRequest
                    {
                        State = ItemStateFilter.All
                    },
                    new ApiOptions
                    {
                        PageCount = 1,
                        PageSize = 100,
                        StartPage = pageIndex
                    }
                )
            );
            if (octoPrs is null || octoPrs.Count == 0)
            {
                break;
            }

            foreach (var octoPr in octoPrs)
            {
                var pr = OctokitMapping.MapPullRequest(octoPr);
                pullRequests.TryAdd(octoPr.NodeId, pr);
            }

            if (Scope.HasFlag(RepoMinerScope.PullRequestComments))
            {
                await Task.WhenAll(
                    octoPrs.Select(async octoPr =>
                        {
                            var comments = await MineComments(repoId, octoPr.Number);
                            pullRequests.AddOrUpdate(
                                octoPr.NodeId,
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
                    octoPrs.Select(async octoPr =>
                        {
                            var events = await MineIssueEvents(repoId, octoPr.Number);
                            pullRequests.AddOrUpdate(
                                octoPr.NodeId,
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
                costAccessor: r => r.Data?.RateLimit.Cost ?? 1,
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

    private async Task MineMilestones(long repoId)
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

            var octoMilestones = await QueryHttp(_ => Http.Issue.Milestone.GetAllForRepository(
                    repoId,
                    new MilestoneRequest
                    {
                        State = ItemStateFilter.All
                    },
                    new ApiOptions
                    {
                        PageCount = 1,
                        PageSize = 100,
                        StartPage = pageIndex
                    }
                )
            );
            if (octoMilestones is null || octoMilestones.Count == 0)
            {
                break;
            }

            foreach (var octoMilestone in octoMilestones)
            {
                milestones.TryAdd(octoMilestone.NodeId, OctokitMapping.MapMilestone(octoMilestone));
            }
        }
    }

    private async Task<ImmutableArray<Comment>> MineComments(long repoId, int number)
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

            var comments = await QueryHttp(_ => Http.Issue.Comment.GetAllForIssue(
                    repoId,
                    number,
                    new ApiOptions
                    {
                        PageCount = 1,
                        PageSize = 100,
                        StartPage = pageIndex
                    }
                )
            );
            if (comments is null || comments.Count == 0)
            {
                break;
            }

            builder.AddRange(comments.Select(c => OctokitMapping.MapIssueComment(c)));
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
                costAccessor: r => r.Data?.RateLimit.Cost ?? 1,
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

    private async Task<ImmutableArray<IssueEvent>> MineIssueEvents(long repoId, int number)
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

            var events = await QueryHttp(_ => Http.Issue.Timeline.GetAllForIssue(
                    repoId,
                    number,
                    new ApiOptions
                    {
                        PageCount = 1,
                        PageSize = 100,
                        StartPage = pageIndex
                    }
                )
            );
            if (events is null || events.Count == 0)
            {
                break;
            }

            builder.AddRange(events.Select(e => OctokitMapping.MapTimelineEventInfo(e)));
        }

        return builder.ToImmutable();
    }

    private async Task SetCooldownsFor(GhToken token)
    {
        var gh = githubClients[token.Name];
        var limits = await gh.RateLimit.GetRateLimits();

        var httpThreshold = token.HttpLimit == -1 ? 0 : limits.Resources.Core.Limit - token.HttpLimit;
        if (limits.Resources.Core.Remaining <= httpThreshold)
        {
            logger.LogInformation(
                "Reached {Remaining} remaining HTTP requests, which is below the {Threshold} threshold of '{TokenName}'.",
                limits.Resources.Core.Remaining,
                httpThreshold,
                token.Name
            );
            httpCooldowns.AddOrUpdate(
                token.Name,
                _ => limits.Resources.Core.Reset,
                (_, existing) => Utils.Max(existing, limits.Resources.Core.Reset)
            );
        }

        var graphQlThreshold = token.GraphQlLimit == -1 ? 0 : limits.Resources.Graphql.Limit - token.GraphQlLimit;
        if (limits.Resources.Graphql.Remaining <= graphQlThreshold)
        {
            logger.LogInformation(
                "Reached {Remaining} remaining GraphQL requests, which is below the {Threshold} threshold of '{TokenName}'.",
                limits.Resources.Graphql.Remaining,
                graphQlThreshold,
                token.Name
            );
            graphQlCooldowns.AddOrUpdate(
                token.Name,
                _ => limits.Resources.Graphql.Reset,
                (_, existing) => Utils.Max(existing, limits.Resources.Graphql.Reset)
            );
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
            catch (RateLimitExceededException)
            {
                await EnsureHttpAvailable(cancellationToken);
            }
        }
    }

    private record GhToken(
        string Name,
        string Token,
        int HttpLimit,
        int GraphQlLimit
    );
}
