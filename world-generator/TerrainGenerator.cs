using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public class TerrainGenerator(
    ActiveRepository repo,
    ILoggerFactory loggerFactory
)
{
    public static readonly TimeSpan StepLength = TimeSpan.FromDays(1);

    private readonly ILogger<TerrainGenerator> logger = loggerFactory.CreateLogger<TerrainGenerator>();
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions(), loggerFactory);

    public TerrainGenerationResult Generate(
        ConversationScope scope = ConversationScope.All,
        int startStep = 0,
        int stepCount = -1,
        CancellationToken ct = default
    )
    {
        var startedAt = DateTimeOffset.UtcNow;

        var islandGenerators = repo.TopicModelling.Topics
            .Where(p => p.Key != -1)
            .ToImmutableDictionary(
                p => p.Key,
                p => cache.GetOrCreate<IslandHeightmapGenerator>(
                    new CacheKey(scope, p.Key),
                    entry =>
                    {
                        // TODO: entry size
                        return new IslandHeightmapGenerator(
                            repo,
                            p.Key,
                            scope,
                            loggerFactory.CreateLogger<IslandHeightmapGenerator>()
                        );
                    }
                )!
            );
        Parallel.ForEach(
            islandGenerators.Values.Where(g => !g.IsInitialized),
            generator =>
            {
                ct.ThrowIfCancellationRequested();
                generator.Initialize(ct);
            }
        );

        var terrains = ImmutableArray.CreateBuilder<TerrainPreset>();

        for (int slidingWindow = 0; slidingWindow <= (int)SlidingWindowPreset.MaxValue; ++slidingWindow)
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
                        StepLength,
                        startStep: startStep,
                        stepCount: stepCount,
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
            RepositoryName: repo.Mining.Repository.Name,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Terrains: terrains.ToImmutable()
        );
    }

    private record CacheKey(
        ConversationScope Scope,
        int TopicId
    );
}
