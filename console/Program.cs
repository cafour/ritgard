using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Ritgard.Mining;

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args);


public class Commands
{
    public static readonly ILoggerFactory LoggerFactory
        = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole());

    /// <summary>
    /// Mine a GitHub repo.
    /// </summary>
    /// <param name="repo">owner/repo_name</param>
    [Command("repo")]
    public async Task MineRepo([Argument] string repo)
    {
        var miner = new RepoMiner(LoggerFactory.CreateLogger<RepoMiner>());
        await miner.Initialize();
        var (owner, repoName) = Utils.ParseRepoString(repo);
        var result = await miner.MineRepo(owner, repoName);
        if (result is not null)
        {
            await Utils.WriteJson(result, $"{repoName.ToLower()}_{result.MiningCompletedAt:yyyy-MM-dd_HH-mm-ss}.json");
        }
    }

    /// <summary>
    /// Mine links from a repo's README.
    /// </summary>
    /// <param name="repo">owner/repo_name</param>
    [Command("readme")]
    public void MineReadme(string repo)
    {   

    }

    /// <summary>
    /// Given a CSV with issues and a CSV with topic per issue, calculate positions suitable for the voxel landscape.
    /// </summary>
    /// <param name="issuesCsv">CSV with issues</param>
    /// <param name="topicsCsv">CSV with topics</param>
    [Command("adjust-layout")]
    public async Task AdjustLayout([Argument] string issuesCsv, [Argument] string topicsCsv)
    {
        var issues = await Utils.ReadCsv<Issue>(issuesCsv);
        var topics = await Utils.ReadCsv<IssueTopic>(topicsCsv);
        var adjuster = new LayoutAdjuster(LoggerFactory.CreateLogger<LayoutAdjuster>());
        var newPositions = adjuster.AdjustPositions(topics);
    }
}
