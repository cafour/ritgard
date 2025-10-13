using System;
using System.Collections.Concurrent;
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

public class RepoMiner
{
    private readonly ILogger<RepoMiner> logger;
    private readonly ConcurrentDictionary<long, Issue> issues = [];
    private readonly ConcurrentDictionary<long, PullRequest> pullRequests = [];
    private readonly ConcurrentDictionary<long, Milestone> milestones = [];

    public RepoMiner(ILogger<RepoMiner> logger)
    {
        this.logger = logger;
    }

    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();

    public GitHubClient GH { get; private set; } = null!;

    public GitHubGraphQLClient GHQ { get; private set; } = null!;

    public Task Initialize()
    {
        Configuration = Utils.BuildConfiguration();
        var authToken = Configuration["GitHubToken"];
        if (authToken is null)
        {
            throw new InvalidOperationException("Cannot mine GitHub repositories without an authorization token.");
        }

        GH = new GitHubClient(new ProductHeaderValue("ritgard"))
        {
            Credentials = new Credentials(authToken)
        };
        GHQ = Utils.CreateGitHubGraphQLClient(authToken);
        logger.LogInformation("Initialized");
        return Task.CompletedTask;
    }

    public async Task<MiningResult?> MineRepo(
        string owner,
        string repoName,
        bool shouldMineIssues = true,
        bool shouldMinePRs = true,
        bool shouldMineDiscussions = true,
        bool shouldMineMilestones = true
    )
    {
        var startedAt = DateTimeOffset.UtcNow;

        logger.LogInformation("Mining '{owner}/{repoName}'.", owner, repoName);
        var octoRepo = await GH.Repository.Get(owner, repoName);
        if (octoRepo is null)
        {
            logger.LogInformation("Repo '{owner}/{repoName}' could not be found.", owner, repoName);
            return null;
        }

        if (shouldMineIssues)
        {
            logger.LogInformation("Mining issues");
            var octoIssues = await GH.Issue.GetAllForRepository(octoRepo.Id, new RepositoryIssueRequest
                {
                    State = ItemStateFilter.All
                }
            );
            foreach (var octoIssue in octoIssues)
            {
                var issue = OctokitMapping.MapIssue(octoIssue);
                if (octoIssue.PullRequest is not null)
                {
                    continue;
                }

                issues.TryAdd(octoIssue.Id, issue);

                var comments = await MineComments(octoRepo.Id, octoIssue.Number);
                issue = issue with { Comments = comments };
                issue = issues.AddOrUpdate(octoIssue.Id, issue, (id, i) => i with { Comments = comments });

                var events = await MineIssueEvents(octoRepo.Id, octoIssue.Number);
                issue = issue with { Events = events };
                issue = issues.AddOrUpdate(octoIssue.Id, issue, (id, i) => i with { Events = events });
            }
        }

        if (shouldMinePRs)
        {
            logger.LogInformation("Mining pull requests");
            var octoPRs = await GH.PullRequest.GetAllForRepository(octoRepo.Id, new PullRequestRequest
                {
                    State = ItemStateFilter.All
                }
            );

            foreach (var octoPR in octoPRs)
            {
                var pr = OctokitMapping.MapPullRequest(octoPR);
                pullRequests.TryAdd(octoPR.Id, pr);

                var comments = await MineComments(octoRepo.Id, octoPR.Number);
                pr = pr with { Comments = comments };
                pullRequests.AddOrUpdate(octoPR.Id, pr, (id, p) => p with { Comments = comments });
            }
        }

        if (shouldMineDiscussions)
        {
            logger.LogInformation("Mining discussions");
            string? cursor = null;
            do
            {
                var discussionQueryResult = await GHQ.DiscussionQuery.ExecuteAsync(owner, repoName, after: cursor);
                if (discussionQueryResult.Errors.Count > 0)
                {
                    logger.LogError(
                        "Failed to mine discussions of '{Owner}/{RepoName}'. The query returned errors: {Errors}",
                        owner,
                        repoName,
                        discussionQueryResult.Errors.Select(e => e.Message)
                    );
                    break;
                }

                if (discussionQueryResult.Data?.Repository is null)
                {
                    logger.LogError(
                        "Failed to mine discussions of '{Owner}/{RepoName}'. The query returned null.",
                        owner,
                        repoName
                    );
                    break;
                }

                foreach (var discussion in discussionQueryResult.Data.Repository.Discussions.Edges ?? [])
                {
                    if (discussion is null || discussion.Node is null)
                    {
                        continue;
                    }

                    logger.LogInformation("\t#{Number}: {Title}", discussion.Node.Number, discussion.Node.Title);
                }

                cursor = discussionQueryResult.Data.Repository.Discussions.PageInfo.EndCursor;
            } while (cursor is not null);
        }

        if (shouldMineMilestones)
        {
            logger.LogInformation("Mining milestones");
            var octoMilestones = await GH.Issue.Milestone.GetAllForRepository(octoRepo.Id, new MilestoneRequest
                {
                    State = ItemStateFilter.All
                }
            );

            foreach (var octoMilestone in octoMilestones)
            {
                milestones.TryAdd(octoMilestone.Id, OctokitMapping.MapMilestone(octoMilestone));
            }
        }

        return new MiningResult(
            MiningStartedAt: startedAt,
            MiningCompletedAt: DateTimeOffset.UtcNow,
            Repository: OctokitMapping.MapRepository(octoRepo),
            Issues: issues.ToImmutableDictionary(),
            PullRequests: pullRequests.ToImmutableDictionary(),
            Milestones: milestones.ToImmutableDictionary()
        );
    }

    private async Task<ImmutableArray<Comment>> MineComments(long repoId, int number)
    {
        logger.LogInformation("Mining comments for #{Number}", number);
        var comments = await GH.Issue.Comment.GetAllForIssue(repoId, number);
        if (comments is null)
        {
            return [];
        }

        return [.. comments.Select(c => OctokitMapping.MapIssueComment(c))];
    }

    private async Task<ImmutableArray<IssueEvent>> MineIssueEvents(long repoId, int number)
    {
        logger.LogInformation("Mining issue events for #{Number}", number);
        // var events = await GH.Issue.Events.GetAllForIssue(repoId, number);
        var events = await GH.Issue.Timeline.GetAllForIssue(repoId, number);
        if (events is null)
        {
            return [];
        }

        return [.. events.Select(e => OctokitMapping.MapTimelineEventInfo(e))];
    }
}
