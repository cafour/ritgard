using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public class TerrainGenerator(ILoggerFactory loggerFactory)
{
    private readonly ILogger<TerrainGenerator> logger = loggerFactory.CreateLogger<TerrainGenerator>();

    public TerrainGenerationResult Generate(ActiveRepository repo, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var scope = ConversationScope.All;
        var islandGenerators = repo.TopicModelling.Topics
            .Where(p => p.Key != -1)
            .ToImmutableDictionary(
                p => p.Key,
                p => new IslandHeightmapGenerator(
                    repo,
                    p.Key,
                    scope,
                    loggerFactory.CreateLogger<IslandHeightmapGenerator>()
                )
            );
        foreach (var generator in islandGenerators.Values)
        {
            ct.ThrowIfCancellationRequested();

            generator.Initialize(ct);
        }

        var terrains = ImmutableArray.CreateBuilder<TerrainPreset>();

        for (int slidingWindow = 0; slidingWindow < (int)SlidingWindowPreset.MaxValue; ++slidingWindow)
        {
            var heightmaps = ImmutableDictionary.CreateBuilder<int, IslandHeightmap>();
            foreach (var (topicId, generator) in islandGenerators)
            {
                ct.ThrowIfCancellationRequested();

                logger.LogInformation(
                    "Generating heightmap for topic '{TopicId}', the '{SlidingWindow}', and '{Scope}' scope.",
                    topicId,
                    (SlidingWindowPreset)slidingWindow,
                    scope
                );
                heightmaps.Add(
                    topicId,
                    generator.Generate(
                        ((SlidingWindowPreset)slidingWindow).ToTimeSpan(),
                        TimeSpan.FromDays(1),
                        ct: ct
                    )
                );
            }

            terrains.Add(
                new TerrainPreset(
                    SlidingWindow: (SlidingWindowPreset)slidingWindow,
                    Scope: scope,
                    IslandHeightmaps: heightmaps.ToImmutable()
                )
            );
        }

        return new TerrainGenerationResult(
            RepositoryFullName: repo.Mining.Repository.FullName,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Terrains: terrains.ToImmutable()
        );
    }
}
