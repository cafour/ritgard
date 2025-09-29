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

    public Task Initialize()
    {
        Configuration = Utils.BuildConfiguration();
        GH = new GitHubClient(new ProductHeaderValue("ritgard"))
        {
            Credentials = new Credentials(Configuration["GitHubToken"])
        };
        logger.LogInformation("Initialized");
        return Task.CompletedTask;
    }

    public async Task<MiningResult?> MineRepo(string owner, string repoName)
    {
        var startedAt = DateTimeOffset.UtcNow;
        
        logger.LogInformation("Mining '{owner}/{repoName}'.", owner, repoName);
        var octoRepo = await GH.Repository.Get(owner, repoName);
        if (octoRepo is null)
        {
            logger.LogInformation("Repo '{owner}/{repoName}' could not be found.", owner, repoName);
            return null;
        }

        logger.LogInformation("Mining issues");
        var octoIssues = await GH.Issue.GetAllForRepository(octoRepo.Id, new RepositoryIssueRequest
        {
            State = ItemStateFilter.All
        });

        foreach (var octoIssue in octoIssues)
        {
            var issue = OctokitMapping.MapIssue(octoIssue);
            if (octoIssue.PullRequest is not null)
            {
                continue;
            }

            issues.TryAdd(octoIssue.Id, issue);

            if (octoIssue.Comments > 0)
            {
                var comments = await MineComments(octoRepo.Id, octoIssue.Number);
                issue = issue with { Comments = comments };
                issues.AddOrUpdate(octoIssue.Id, issue, (id, i) => i with { Comments = comments });
            }
        }

        logger.LogInformation("Mining pull requests");
        var octoPRs = await GH.PullRequest.GetAllForRepository(octoRepo.Id, new PullRequestRequest
        {
            State = ItemStateFilter.All
        });

        foreach (var octoPR in octoPRs)
        {
            var pr = OctokitMapping.MapPullRequest(octoPR);
            pullRequests.TryAdd(octoPR.Id, pr);

            if (octoPR.Comments > 0)
            {
                var comments = await MineComments(octoRepo.Id, octoPR.Number);
                pr = pr with { Comments = comments };
                pullRequests.AddOrUpdate(octoPR.Id, pr, (id, p) => p with { Comments = comments });
            }
        }

        logger.LogInformation("Mining milestones");
        var octoMilestones = await GH.Issue.Milestone.GetAllForRepository(octoRepo.Id, new MilestoneRequest
        {
            State = ItemStateFilter.All
        });

        foreach (var octoMilestone in octoMilestones)
        {
            milestones.TryAdd(octoMilestone.Id, OctokitMapping.MapMilestone(octoMilestone));
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
        return [.. comments.Select(c => OctokitMapping.MapIssueComment(c))];
    }


}
