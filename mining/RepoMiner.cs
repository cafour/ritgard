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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware;
using Ritgard.Mining.GitHub;
using StrawberryShake;

namespace Ritgard.Mining;

public class RepoMiner
{
    private readonly ILogger<RepoMiner> logger;

    private readonly ConcurrentDictionary<string, Issue> issues = [];
    private readonly ConcurrentDictionary<string, PullRequest> pullRequests = [];
    private readonly ConcurrentDictionary<string, Discussion> discussions = [];
    private readonly ConcurrentDictionary<string, Milestone> milestones = [];
    private readonly MiningOptions options = new();
    private ImmutableDictionary<string, GitHubToken> gitHubTokens = [];
    private ImmutableDictionary<string, GitHubGraphQLClientWrapper> graphQlClients = [];
    private ImmutableDictionary<string, GitHubRestClientWrapper> restClients = [];

    private readonly SemaphoreSlim httpLock = new(1, 1);
    private readonly SemaphoreSlim graphQlLock = new(1, 1);

    public RepoMiner(ILogger<RepoMiner> logger, string repoOwner, string repoName, RepoMinerScope scope)
    {
        RepoOwner = repoOwner;
        RepoName = repoName;
        Scope = scope;
        this.logger = logger;

        var services = new ServiceCollection();
        services.AddHttpClient(nameof(GitHubRestClient))
            .AddHttpMessageHandler(() => new HeadersInspectionHandler());
    }

    public string RepoOwner { get; }
    public string RepoName { get; }
    public RepoMinerScope Scope { get; }
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();
    public GitHubRestClientWrapper? Rest { get; private set; }
    public GitHubGraphQLClientWrapper? GraphQl { get; private set; }

    public async Task<MiningResult?> MineRepo(CancellationToken cancellationToken = default)
    {
        await Initialize(cancellationToken);

        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine the repo without the GitHub HTTP API client.");
        }

        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Started mining '{RepoOwner}/{RepoName}'.", RepoOwner, RepoName);
        var repo = await ExecuteRest(
            (rest, ct) => Rest.Client.Repos[RepoOwner][RepoName].GetAsync(cancellationToken: ct),
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
            Repository: GitHubRestMapping.MapRepository(repo) with { Cloc = cloc, GitLoc = gitLoc },
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

        gitHubTokens = options.GitHubTokens
            .Where(t => t.Value.Token.StartsWith("github"))
            .ToImmutableDictionary(
                p => p.Key,
                p => new GitHubToken(p.Key, p.Value.Token, p.Value.HttpLimit, p.Value.GraphQlLimit)
            );
        restClients = gitHubTokens.ToImmutableDictionary(
            t => t.Key,
            t => GitHubRestClientWrapper.Create(t.Value)
        );
        graphQlClients = gitHubTokens.ToImmutableDictionary(
            t => t.Key,
            t => GitHubGraphQLClientWrapper.Create(t.Value)
        );

        await RefreshRestApi(cancellationToken);
        await RefreshGraphQlApi(cancellationToken);

        logger.LogInformation("Initialized");
    }

    private async Task MineIssues(CancellationToken cancellationToken = default)
    {
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine issues without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining issues");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghIssues = await ExecuteRest(
                (rest, ct) => rest.Repos[RepoOwner][RepoName].Issues.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                        config.QueryParameters.StateAsGetStateQueryParameterType =
                            Repos.Item.Item.Issues.GetStateQueryParameterType.All;
                    },
                    ct
                ),
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

        logger.LogInformation("Mining pull requests");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghPrs = await ExecuteRest(
                (rest, ct) => rest.Repos[RepoOwner][RepoName].Pulls.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                        config.QueryParameters.StateAsGetStateQueryParameterType =
                            Repos.Item.Item.Pulls.GetStateQueryParameterType.All;
                    },
                    ct
                ),
                ct: cancellationToken
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

                var ghPrDetails = await ExecuteRest(
                    (rest, ct) => rest.Repos[RepoOwner][RepoName].Pulls[ghPr.Number.Value]
                        .GetAsync(cancellationToken: ct),
                    ct: cancellationToken
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
            var queryResult = await ExecuteGraphQl(
                execute: (graphQl, ct) => graphQl.DiscussionQuery.ExecuteAsync(
                    RepoOwner,
                    RepoName,
                    after: cursor,
                    cancellationToken: ct
                ),
                errorsAccessor: r => r.Errors,
                rateRemainingAccessor: r => r.Data?.RateLimit?.Remaining ?? -1,
                rateResetAccessor: r => r.Data?.RateLimit?.ResetAt ?? default,
                ct: cancellationToken
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
        if (Rest is null)
        {
            throw new InvalidOperationException("Cannot mine milestones event without the GitHub HTTP API client.");
        }

        logger.LogInformation("Mining milestones");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var ghMilestones = await ExecuteRest(
                (rest, ct) => rest.Repos[RepoOwner][RepoName].Milestones.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                        config.QueryParameters.StateAsGetStateQueryParameterType =
                            Repos.Item.Item.Milestones.GetStateQueryParameterType.All;
                    },
                    ct
                ),
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

        logger.LogInformation("Mining comments for #{Number}", number);
        var pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        while (true)
        {
            pageIndex++;

            var comments = await ExecuteRest(
                (rest, ct) => rest.Repos[RepoOwner][RepoName].Issues[number].Comments.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                    },
                    ct
                ),
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

        logger.LogInformation("Mining discussion comments for {DiscussionId}.", nodeId);
        string? cursor = null;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        do
        {
            var queryResult = await ExecuteGraphQl(
                execute: (graphQl, ct) => graphQl.DiscussionCommentQuery.ExecuteAsync(nodeId, cursor, ct),
                errorsAccessor: r => r.Errors,
                rateRemainingAccessor: r => r.Data?.RateLimit?.Remaining ?? -1,
                rateResetAccessor: r => r.Data?.RateLimit?.ResetAt ?? default,
                ct: cancellationToken
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

    private async Task<ImmutableArray<IssueEvent>> MineIssueEvents(
        long number,
        CancellationToken cancellationToken = default
    )
    {
        if (Rest is null)
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

            var events = await ExecuteRest(
                (rest, ct) => rest.Repos[RepoOwner][RepoName].Issues[number].Timeline.GetAsync(
                    config =>
                    {
                        config.QueryParameters.Page = pageIndex;
                        config.QueryParameters.PerPage = 100;
                    },
                    ct
                ),
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

    private async Task RefreshRestApi(CancellationToken ct = default)
    {
        var newClient = restClients
            .OrderBy(c => c.Value.RateReset)
            .ThenByDescending(c => c.Value.AdjustedRateRemaining)
            .First().Value;
        if (newClient.AdjustedRateRemaining == 0)
        {
            var waitTime = newClient.RateReset > DateTimeOffset.UtcNow
                ? newClient.RateReset - DateTimeOffset.UtcNow
                : TimeSpan.FromMinutes(5);
            logger.LogInformation(
                "REST API is waiting for token '{TokenName}' to cool down for {CooldownTime}.",
                newClient.Token.Name,
                waitTime
            );
            await Task.Delay(waitTime, ct);
        }

        logger.LogInformation("Switching GitHub REST API client to '{TokenName}'.", newClient.Token.Name);
        Rest = newClient;
    }

    private async Task RefreshGraphQlApi(CancellationToken ct = default)
    {
        var newClient = graphQlClients
            .OrderBy(c => c.Value.RateReset)
            .ThenByDescending(c => c.Value.RateRemaining)
            .First().Value;
        if (newClient.RateRemaining == 0)
        {
            var waitTime = newClient.RateReset > DateTimeOffset.UtcNow
                ? newClient.RateReset - DateTimeOffset.UtcNow
                : TimeSpan.FromMinutes(5);
            logger.LogInformation(
                "GraphQL API is waiting for token '{TokenName}' to cool down for {CooldownTime}.",
                newClient.Token.Name,
                waitTime
            );
            await Task.Delay(waitTime, ct);
        }

        logger.LogInformation("Switching GitHub GraphQL API client to '{TokenName}'.", newClient.Token.Name);
        GraphQl = newClient;
    }

    private async Task EnsureRestAvailable(CancellationToken cancellationToken = default)
    {
        var tmpToken = Rest!.Token;
        await httpLock.WaitAsync(cancellationToken);
        try
        {
            if (Rest.Token != tmpToken)
            {
                // some other thread switched the token first
                return;
            }

            await RefreshRestApi(cancellationToken);
        }
        finally
        {
            httpLock.Release();
        }
    }

    private async Task EnsureGraphQlAvailable(CancellationToken cancellationToken = default)
    {
        var tmpToken = GraphQl!.Token;
        await graphQlLock.WaitAsync(cancellationToken);
        try
        {
            if (GraphQl.Token != tmpToken)
            {
                // some other thread switched the token first
                return;
            }

            await RefreshGraphQlApi(cancellationToken);
        }
        finally
        {
            graphQlLock.Release();
        }
    }

    private async Task<TResult> ExecuteGraphQl<TResult>(
        Func<GitHubGraphQLClient, CancellationToken, Task<TResult>> execute,
        Func<TResult, IReadOnlyList<IClientError>> errorsAccessor,
        Func<TResult, int> rateRemainingAccessor,
        Func<TResult, DateTimeOffset> rateResetAccessor,
        CancellationToken ct = default
    )
    {
        if (GraphQl!.IsBlocked)
        {
            await EnsureGraphQlAvailable(ct);
        }

        while (true)
        {
            var queryResult = await GraphQl.Query(execute, rateRemainingAccessor, rateResetAccessor, ct);
            var errors = errorsAccessor(queryResult);
            if (errors.Count == 0)
            {
                return queryResult;
            }

            if (errors is [{ Code: "graphql_rate_limit" }])
            {
                await EnsureGraphQlAvailable(ct);
            }

            return queryResult;
        }
    }

    private async Task<TResult> ExecuteRest<TResult>(
        Func<GitHubRestClient, CancellationToken, Task<TResult>> execute,
        CancellationToken ct = default
    )
    {
        if (Rest!.IsBlocked)
        {
            await EnsureRestAvailable(ct);
        }

        while (true)
        {
            try
            {
                return await execute(Rest!.Client, ct);
            }
            catch (ApiException ex) when (IsRateLimitException(ex))
            {
                await EnsureRestAvailable(ct);
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
            && exception.ResponseHeaders["X-RateLimit-Remaining"]?.SingleOrDefault() == "0";
    }
}
