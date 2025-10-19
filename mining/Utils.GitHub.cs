using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public static partial class Utils
{
    public const int GitCleanupAttemptCount = 5;

    [GeneratedRegex(
        @"^(\d+|-)\s+(\d+|-)\s(.+)$"
    )]
    public static partial Regex GetGitLocRegex();

    public static (GitHubGraphQLClient client, IAsyncDisposable clientDisposable) CreateGitHubGraphQLClient(
        string authToken
    )
    {
        var services = new ServiceCollection();
        services.AddGitHubGraphQLClient()
            .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri("https://api.github.com/graphql");
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                }
            );
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<GitHubGraphQLClient>(), provider);
    }

    public static Task<int?> GetFileCount(
        string cloneUrl,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        logger ??= NullLogger.Instance;

        return DoInRepo<int?>(
            cloneUrl: cloneUrl,
            action: async (repoDir, ct) =>
            {
                var tree = Command.Run(
                    "git",
                    ["ls-tree", "-r", "--name-only", "HEAD"],
                    o => o.WorkingDirectory(repoDir)
                        .CancellationToken(ct)
                );
                var fileCount = await Task.Run(
                    () =>
                    {
                        var lineCount = 0;
                        while (tree.StandardOutput.ReadLine() is not null)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return -1;
                            }

                            lineCount++;
                        }

                        return lineCount;
                    },
                    ct
                );

                var treeResult = await tree.Task;
                if (!treeResult.Success)
                {
                    logger.LogError("Failed to count files in '{CloneUrl}'.", cloneUrl);
                    return null;
                }

                return fileCount;
            },
            cloneArgs: ["--filter=blob:none", "--depth=1", "--no-checkout"],
            logger: logger,
            cancellationToken: cancellationToken
        );
    }

    public static Task<ClocInfo?> GetCloc(
        string cloneUrl,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        logger ??= NullLogger.Instance;

        return DoInRepo<ClocInfo?>(
            cloneUrl: cloneUrl,
            action: async (repoDir, ct) =>
            {
                logger.LogInformation("Counting code lines of '{CloneUrl}'.", cloneUrl);
                var tree = Command.Run(
                    "cloc",
                    ["--vcs", "git", "--no-autogen", "--json"],
                    o => o.WorkingDirectory(repoDir)
                        .CancellationToken(ct)
                );
                var clocInfo = await JsonSerializer.DeserializeAsync<ClocInfo>(
                    tree.StandardOutput.BaseStream,
                    cancellationToken: ct
                );

                if (clocInfo is null)
                {
                    logger.LogError("Failed to cloc '{CloneUrl}'.", cloneUrl);
                    return null;
                }

                var treeResult = await tree.Task;
                if (!treeResult.Success)
                {
                    logger.LogError("Failed to count files in '{CloneUrl}'.", cloneUrl);
                    return null;
                }

                return clocInfo;
            },
            cloneArgs: ["--filter=blob:none", "--depth=1"],
            logger: logger,
            cancellationToken: cancellationToken
        );
    }

    public static Task<GitLocInfo?> GetGitLoc(
        string cloneUrl,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        logger ??= NullLogger.Instance;

        return DoInRepo<GitLocInfo?>(
            cloneUrl: cloneUrl,
            action: async (repoDir, ct) =>
            {
                logger.LogInformation("Counting total line changes of '{CloneUrl}'.", cloneUrl);
                var regex = GetGitLocRegex();
                var changes = Command.Run(
                    "git",
                    ["log", "--numstat", "--pretty=tformat:", "--no-renames"],
                    o => o.WorkingDirectory(repoDir).CancellationToken(ct)
                );
                var entries = await Task.Run(() =>
                    {
                        var i = 0;
                        var builder = ImmutableDictionary.CreateBuilder<string, GitLocEntry>();
                        while (changes.StandardOutput.ReadLine() is { } line)
                        {
                            i++;
                            if (i % 10 == 0)
                            {
                                logger.LogInformation("Reading {Index}", i);
                            }

                            if (cancellationToken.IsCancellationRequested)
                            {
                                return null;
                            }

                            var match = regex.Match(line);
                            if (!match.Success)
                            {
                                logger.LogError("Failed to match line '{Line}' for expected git output.", line);
                                return null;
                            }

                            if (match.Groups[1].ValueSpan is "-" || match.Groups[2].ValueSpan is "-")
                            {
                                // binary blobs, ignore
                                continue;
                            }

                            var addedLineCount = long.Parse(match.Groups[1].ValueSpan);
                            var deletedLineCount = long.Parse(match.Groups[2].ValueSpan);
                            var extension = Path.GetExtension(match.Groups[3].ValueSpan).ToString();

                            if (!builder.ContainsKey(extension))
                            {
                                builder[extension] = new GitLocEntry();
                            }

                            builder[extension].AddedLineCount += addedLineCount;
                            builder[extension].DeletedLineCount += deletedLineCount;
                        }

                        return builder.ToImmutable();
                    },
                    ct
                );

                if (entries is null)
                {
                    return null;
                }

                var changesResult = await changes.Task;
                if (!changesResult.Success)
                {
                    logger.LogError("Git log failed with an error: {GitError}", changesResult.StandardError);
                    return null;
                }

                return new GitLocInfo(
                    Entries: entries,
                    AddedLineCount: entries.Values.Sum(v => v.AddedLineCount),
                    DeletedLineCount: entries.Values.Sum(v => v.DeletedLineCount)
                );
            },
            cloneArgs: ["--filter=blob:none", "--no-checkout"],
            logger: logger,
            cancellationToken: cancellationToken
        );
    }

    public static async Task<TResult?> DoInRepo<TResult>(
        string cloneUrl,
        Func<string, CancellationToken, Task<TResult>> action,
        IEnumerable<string>? cloneArgs = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        logger ??= NullLogger.Instance;
        cloneArgs ??= ["--filter=blob:none", "--depth=1"];

        var tempParent = Path.GetTempPath();
        var tempDirName = Path.GetFileNameWithoutExtension(Path.GetTempFileName());
        var tempDir = Path.Combine(tempParent, tempDirName);
        Directory.CreateDirectory(tempDir);
        try
        {
            logger.LogInformation("Cloning '{CloneUrl}'.", cloneUrl);
            var cloneResult = await Command.Run(
                "git",
                ["clone", ..cloneArgs, cloneUrl, tempDirName],
                o => o.WorkingDirectory(tempParent)
                    .CancellationToken(cancellationToken)
            ).Task;

            if (!cloneResult.Success)
            {
                logger.LogError("Git failed with: {GitError}", cloneResult.StandardError);
                logger.LogError("Could not do a shallow clone of '{CloneUrl}.'", cloneUrl);
                return default;
            }

            return await action(tempDir, cancellationToken);
        }
        finally
        {
            for (int i = 0; i < GitCleanupAttemptCount; ++i)
            {
                try
                {
                    foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(tempDir, recursive: true);
                    break;
                }
                catch (Exception)
                {
                    if (i == GitCleanupAttemptCount - 1)
                    {
                        logger.LogError(
                            "Failed to clean '{GitRepo}' after {AttemptCount} attempts. "
                            + "Please remove '{TempDir}' manually.",
                            cloneUrl,
                            GitCleanupAttemptCount,
                            tempDir
                        );
                    }
                }
            }
        }
    }
}
