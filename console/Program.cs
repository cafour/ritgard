using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Ritgard.Mining;
using Ritgard.WorldGenerator;
using Utils = Ritgard.Mining.Utils;

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args);


internal class Commands
{
    public static readonly ILoggerFactory LoggerFactory
        = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
            {
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddSimpleConsole(c =>
                    {
                        c.SingleLine = true;
                        c.TimestampFormat = "HH:mm:ss.fff ";
                        c.UseUtcTimestamp = true;
                    }
                );
            }
        );

    public static readonly ILogger Log
        = LoggerFactory.CreateLogger("Global");

    /// <summary>
    /// Mine a GitHub repo.
    /// </summary>
    /// <param name="repo">owner/repo_name</param>
    /// <param name="scope">The scope of the data mining.</param>
    /// <param name="output">Where to write the mined output.</param>
    /// <param name="cancellationToken">Optional token to cancel the async operation in progress.</param>
    [Command("repo")]
    public async Task MineRepo(
        [Argument] string repo,
        RepoMinerScope scope = RepoMinerScope.All,
        string? output = null,
        CancellationToken cancellationToken = default
    )
    {
        var (owner, repoName) = Utils.ParseRepoString(repo);
        await using var miner = new RepoMiner(
            logger: LoggerFactory.CreateLogger<RepoMiner>(),
            repoOwner: owner,
            repoName: repoName,
            scope: scope
        );
        var result = await miner.MineRepo(cancellationToken);
        if (result is not null)
        {
            if (!string.IsNullOrEmpty(output))
            {
                var parentDir = new FileInfo(output).Directory;
                if (parentDir is not null && !parentDir.Exists)
                {
                    parentDir.Create();
                }
            }

            output = $"{repoName.ToLower()}_{result.MiningCompletedAt:yyyy-MM-dd_HH-mm-ss}.json";
            Log.LogInformation("Saving to '{OutputPath}'.", output);
            await Utils.WriteJson(
                result,
                output,
                cancellationToken
            );
        }
    }

    [Command("count-files")]
    public async Task CountFiles(
        [Argument] string repoUrl,
        CancellationToken cancellationToken = default
    )
    {
        var fileCount = await Utils.GetFileCount(repoUrl, Log, cancellationToken);
        Log.LogInformation("Repository has {FileCount} files.", fileCount);
    }

    [Command("cloc")]
    public async Task Cloc(
        [Argument] string repoUrl,
        CancellationToken cancellationToken = default
    )
    {
        var clocInfo = await Utils.GetCloc(repoUrl, Log, cancellationToken);
        if (clocInfo is not null)
        {
            Log.LogInformation(
                "Repository has {FileCount} files and {LineCount} total lines.",
                clocInfo.Header.FileCount,
                clocInfo.Header.LineCount
            );
            foreach (var entry in clocInfo.Entries.OrderByDescending(e => e.Value.CodeCount))
            {
                Log.LogInformation(
                    "{FileType}: {FileCount} files, {CodeCount} code lines",
                    entry.Key,
                    entry.Value.FileCount,
                    entry.Value.CodeCount
                );
            }
        }
    }

    [Command("git-loc")]
    public async Task GitLoc(
        [Argument] string repoUrl,
        CancellationToken cancellationToken = default
    )
    {
        var gitLocInfo = await Utils.GetGitLoc(repoUrl, Log, cancellationToken);
        if (gitLocInfo is not null)
        {
            Log.LogInformation(
                "Repository has {AddedLineCount} total added line and {DeletedLineCount} total deleted lines.",
                gitLocInfo.AddedLineCount,
                gitLocInfo.DeletedLineCount
            );
            foreach (var entry in gitLocInfo.Entries.OrderByDescending(e =>
                         e.Value.AddedLineCount + e.Value.DeletedLineCount
                     ))
            {
                Log.LogInformation(
                    "{FileType}: {AddedLineCount} added, {DeletedLineCount} deleted",
                    entry.Key,
                    entry.Value.AddedLineCount,
                    entry.Value.DeletedLineCount
                );
            }
        }
    }

    /// <summary>
    /// Compute terrain for a mined and data-processed repo.
    /// </summary>
    /// <param name="datasetPath">Path to the mined data.</param>
    /// <param name="positionsPath">Path to the positional data.</param>
    /// <param name="output">Where to write the mined output.</param>
    /// <param name="stepLengthMultiplier">The number of "default" steps (i.e., days) in a single step.</param>
    /// <param name="batchSize">Number of steps in a batched heightmap. By default -1, meaning no batching.</param>
    /// <param name="cancellationToken">Optional token to cancel the async operation in progress.</param>
    [Command("terrain")]
    public async Task ComputeTerrain(
        [Argument] string datasetPath,
        [Argument] string positionsPath,
        string? output = null,
        int stepLengthMultiplier = 1,
        int batchSize = -1,
        CancellationToken cancellationToken = default
    )
    {
        var miningResult = await Utils.ReadJson<MiningResult>(datasetPath, cancellationToken);
        if (miningResult is null)
        {
            throw new ArgumentException($"Could not read '{datasetPath}'.", nameof(datasetPath));
        }

        var topicResult = await Utils.ReadJson<TopicModellingResult>(positionsPath, cancellationToken);
        if (topicResult is null)
        {
            throw new ArgumentException($"Could not read '{positionsPath}'.", nameof(positionsPath));
        }

        var repo = ActiveRepository.Create(
            new DatasetId(
                Name: miningResult.Repository.Name,
                DataFilePath: datasetPath,
                TopicFilePath: positionsPath
            ),
            miningResult,
            topicResult
        );

        var generator = new TerrainGenerator(repo, LoggerFactory);
        var terrainResult = generator.Generate(
            stepLength: TerrainGenerator.DefaultStepLength * stepLengthMultiplier,
            batchSize: batchSize,
            ct: cancellationToken
        );

        output ??= $"{terrainResult.RepositoryName.ToLower()}_{terrainResult.CompletedAt:yyyy-MM-dd_HH-mm-ss}.json";
        Log.LogInformation("Saving to '{OutputPath}'.", output);

        await Utils.WriteJson(terrainResult, output, cancellationToken);
    }
}
