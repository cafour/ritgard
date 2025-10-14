using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public class RepoMiner(ILogger<RepoMiner> logger, string repoOwner, string repoName, RepoMinerScope scope)
{
    private readonly ConcurrentDictionary<string, Issue> issues = [];
    private readonly ConcurrentDictionary<string, PullRequest> pullRequests = [];
    private readonly ConcurrentDictionary<string, Discussion> discussions = [];
    private readonly ConcurrentDictionary<string, Milestone> milestones = [];
    private readonly MiningOptions options = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> ghTokenCooldowns = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> ghqTokenCooldowns = [];
    private IAsyncDisposable ghqDisposable = null!;

    public string RepoOwner { get; } = repoOwner;
    public string RepoName { get; } = repoName;
    public RepoMinerScope Scope { get; } = scope;
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();
    public GitHubClient Http { get; private set; } = null!;
    public GitHubGraphQLClient? GraphQl { get; private set; }

    public async Task<MiningResult?> MineRepo(CancellationToken cancellationToken = default)
    {
        await Initialize(cancellationToken);

        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Started mining '{RepoOwner}/{RepoName}'.", RepoOwner, RepoName);
        var octoRepo = await Http.Repository.Get(RepoOwner, RepoName);
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
            tasks.Add(MineDiscussions());
        }

        if (Scope.HasFlag(RepoMinerScope.Milestones))
        {
            tasks.Add(MineMilestones(octoRepo.Id));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        return new MiningResult(
            MiningStartedAt: startedAt,
            MiningCompletedAt: DateTimeOffset.UtcNow,
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

        Http = new GitHubClient(new ProductHeaderValue("ritgard"));
        foreach (var (tokenName, _) in options.GitHubTokens)
        {
            ghTokenCooldowns.TryAdd(tokenName, default);
            ghqTokenCooldowns.TryAdd(tokenName, default);
        }

        await RefreshHttpApi(cancellationToken);
        await RefreshGraphQlApi(cancellationToken);

        logger.LogInformation("Initialized");
    }

    private async Task MineIssues(long repoId)
    {
        logger.LogInformation("Mining issues");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var octoIssues = await Http.Issue.GetAllForRepository(
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
                    octoIssues.Select(async octoIssue =>
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
                    octoIssues.Select(async octoIssue =>
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
        logger.LogInformation("Mining pull requests");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var octoPrs = await Http.PullRequest.GetAllForRepository(
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
            var discussionQueryResult = await GraphQl.DiscussionQuery.ExecuteAsync(
                RepoOwner,
                RepoName,
                after: cursor,
                cancellationToken: cancellationToken
            );
            if (discussionQueryResult.Errors.Count > 0)
            {
                logger.LogError(
                    "Failed to mine discussions of '{RepoOwner}/{RepoName}'. The query returned errors: {Errors}",
                    RepoOwner,
                    RepoName,
                    discussionQueryResult.Errors.Select(e => e.Message)
                );
                break;
            }

            if (discussionQueryResult.Data?.Repository is null)
            {
                logger.LogError(
                    "Failed to mine discussions of '{RepoOwner}/{RepoName}'. The query returned null.",
                    RepoOwner,
                    RepoName
                );
                break;
            }

            foreach (var octoDiscussion in discussionQueryResult.Data.Repository.Discussions.Edges ?? [])
            {
                if (octoDiscussion?.Node is null)
                {
                    continue;
                }

                discussions.TryAdd(octoDiscussion.Node.Id, OctokitMapping.MapDiscussion(octoDiscussion.Node));
            }

            if (Scope.HasFlag(RepoMinerScope.DiscussionComments)
                && discussionQueryResult.Data.Repository.Discussions.Edges is not null)
            {
                await Task.WhenAll(
                    discussionQueryResult.Data.Repository.Discussions.Edges
                        .Where(d => d?.Node != null)
                        .Select(async octoDiscussion =>
                            {
                                var comments = await MineDiscussionComments(
                                    octoDiscussion!.Node!.Id,
                                    cancellationToken
                                );
                                discussions.AddOrUpdate(
                                    octoDiscussion!.Node!.Id,
                                    _ => throw new InvalidOperationException(),
                                    (_, pr) => pr with { Comments = comments }
                                );
                            }
                        )
                );
            }

            cursor = discussionQueryResult.Data.Repository.Discussions.PageInfo.HasNextPage
                ? discussionQueryResult.Data.Repository.Discussions.PageInfo.EndCursor
                : null;
        } while (cursor is not null);
    }

    private async Task MineMilestones(long repoId)
    {
        logger.LogInformation("Mining milestones");
        var pageIndex = 0;
        while (true)
        {
            pageIndex++;

            var octoMilestones = await Http.Issue.Milestone.GetAllForRepository(
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
        logger.LogInformation("Mining comments for #{Number}", number);
        var pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<Comment>();
        while (true)
        {
            pageIndex++;

            var comments = await Http.Issue.Comment.GetAllForIssue(
                repoId,
                number,
                new ApiOptions
                {
                    PageCount = 1,
                    PageSize = 100,
                    StartPage = pageIndex
                }
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
            var queryResult = await GraphQl.DiscussionCommentQuery.ExecuteAsync(nodeId, cursor, cancellationToken);
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
        logger.LogInformation("Mining issue events for #{Number}", number);
        // var events = await GH.Issue.Events.GetAllForIssue(repoId, number);
        int pageIndex = 0;
        var builder = ImmutableArray.CreateBuilder<IssueEvent>();
        while (true)
        {
            pageIndex++;

            var events = await Http.Issue.Timeline.GetAllForIssue(
                repoId,
                number,
                new ApiOptions
                {
                    PageCount = 1,
                    PageSize = 100,
                    StartPage = pageIndex
                }
            );
            if (events is null || events.Count == 0)
            {
                break;
            }

            builder.AddRange(events.Select(e => OctokitMapping.MapTimelineEventInfo(e)));
        }

        return builder.ToImmutable();
    }

    private async Task RefreshHttpApi(CancellationToken cancellationToken = default)
    {
        var (tokenName, tokenCooldown) = ghTokenCooldowns.MinBy(p => p.Value);
        if (tokenCooldown > DateTimeOffset.UtcNow)
        {
            logger.LogInformation(
                "HTTP API is waiting for token '{TokenName}' to cool down till {CooldownTime}.",
                tokenName,
                tokenCooldown
            );
            await Task.Delay(DateTimeOffset.UtcNow - tokenCooldown, cancellationToken);
        }

        var tokenValue = options.GitHubTokens[tokenName];
        if (Http.Credentials.Password == tokenValue)
        {
            return;
        }

        lock (Http)
        {
            if (Http.Credentials.Password == tokenValue)
            {
                return;
            }

            logger.LogInformation("Switching GitHub HTTP API token to '{TokenApi}'.", tokenName);
            Http.Credentials = new Credentials(tokenValue);
        }
    }

    private async Task RefreshGraphQlApi(CancellationToken cancellationToken = default)
    {
        var (tokenName, tokenCooldown) = ghqTokenCooldowns.MinBy(p => p.Value);
        if (tokenCooldown > DateTimeOffset.UtcNow)
        {
            logger.LogInformation(
                "GraphQL PI is waiting for token '{TokenName}' to cool down till {CooldownTime}.",
                tokenName,
                tokenCooldown
            );
            await Task.Delay(DateTimeOffset.UtcNow - tokenCooldown, cancellationToken);
        }

        if (GraphQl is not null)
        {
            await ghqDisposable.DisposeAsync();
            GraphQl = null!;
        }

        var tokenValue = options.GitHubTokens[tokenName];
        (GraphQl, ghqDisposable) = Utils.CreateGitHubGraphQLClient(tokenValue);
    }
}
