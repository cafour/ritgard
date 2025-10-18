using System;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http.Headers;
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

    public static async Task<int?> GetFileCount(
        string gitUrl,
        ILogger? logger = null,
        CancellationToken cancellationToken = default
    )
    {
        logger ??= NullLogger.Instance;

        var tempParent = Path.GetTempPath();
        var tempDirName = Path.GetFileNameWithoutExtension(Path.GetTempFileName());
        var tempDir = Path.Combine(tempParent, tempDirName);
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = await Command.Run(
                "git",
                ["clone", "--filter=blob:none", "--no-checkout", "--depth=1", gitUrl, tempDirName],
                o => o.WorkingDirectory(tempParent)
                    .CancellationToken(cancellationToken)
            ).Task;

            if (!result.Success)
            {
                logger.LogError("Git failed with: {GitError}", result.StandardError);
                logger.LogError("Could not do a shallow clone of '{GitUrl}.'", gitUrl);
                return null;
            }

            var tree = Command.Run(
                "git",
                ["ls-tree", "-r", "--name-only", "HEAD"],
                o => o.WorkingDirectory(tempDir)
                    .CancellationToken(cancellationToken)
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
                cancellationToken
            );

            var treeResult = await tree.Task;
            if (!treeResult.Success)
            {
                logger.LogError("Failed to count files in '{GitUrl}'.", gitUrl);
                return null;
            }

            return fileCount;
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
                catch(Exception)
                {
                    if (i == GitCleanupAttemptCount - 1)
                    {
                        logger.LogError(
                            "Failed to clean up after counting files of '{GitRepo}' after {AttempCount} attempts. "
                            + "Please remove '{TempDir}' manually.",
                            gitUrl,
                            GitCleanupAttemptCount,
                            tempDir
                        );
                    }
                }
            }
        }
    }
}
