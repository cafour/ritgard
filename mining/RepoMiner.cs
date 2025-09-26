using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
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

    public async Task<ImmutableArray<Issue>> MineIssues(string owner, string repoName)
    {
        var repository = await GH.Repository.Get(owner, repoName);
        logger.LogInformation("Mining issues of '{owner}/{repoName}'.", owner, repoName);
        var issues = await GH.Issue.GetAllForRepository(repository.Id, new RepositoryIssueRequest
        {
            State = ItemStateFilter.All
        });
        return issues.Select(i => new Issue(
            Id: i.Id,
            Number: i.Number,
            Title: i.Title,
            Author: i.User.Login,
            CreatedAt: i.CreatedAt,
            UpdatedAt: i.UpdatedAt,
            ClosedAt: i.ClosedAt,
            Labels: string.Join(';', i.Labels.Select(l => l.Name)),
            Body: i.Body
        )).ToImmutableArray();
    }

    public static async Task WriteIssuesCSV(
        ImmutableArray<Issue> issues,
        string projectName,
        CancellationToken token = default)
    {
        projectName = projectName.ToLower().Replace(" ", "_");
        using var writer = new StreamWriter($"./{projectName}.csv");
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(issues, token);
    }
}
