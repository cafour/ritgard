using System;
using System.Collections.Concurrent;
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
        SlidingWindowPreset slidingWindowPresets = SlidingWindowPreset.AllPresets,
        int startStep = 0,
        int stepCount = -1,
        CancellationToken ct = default
    )
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (scope == ConversationScope.None)
        {
            return new TerrainGenerationResult(
                RepositoryName: repo.Mining.Repository.Name,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                Terrains: []
            );
        }

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

        for (int slidingWindow = 1; slidingWindow <= (int)SlidingWindowPreset.MaxValue; slidingWindow <<= 1)
        {
            if (!slidingWindowPresets.HasFlag((SlidingWindowPreset)slidingWindow))
            {
                continue;
            }

            var heightmaps = new ConcurrentDictionary<int, IslandHeightmap>();
            var window = slidingWindow;
            Parallel.ForEach(
                islandGenerators.Keys,
                topicId =>
                {
                    var generator = islandGenerators[topicId];
                    logger.LogInformation(
                        "Generating heightmap for topic '{TopicId}', the '{SlidingWindow}', and '{Scope}' scope.",
                        topicId,
                        (SlidingWindowPreset)window,
                        scope
                    );
                    heightmaps.TryAdd(
                        topicId,
                        generator.Generate(
                            ((SlidingWindowPreset)window).ToTimeSpan(),
                            StepLength,
                            startStep: startStep,
                            stepCount: stepCount,
                            ct: ct
                        )
                    );
                }
            );

            terrains.Add(
                new TerrainPreset(
                    SlidingWindow: (SlidingWindowPreset)slidingWindow,
                    Scope: scope,
                    IslandHeightmaps: heightmaps.ToImmutableDictionary()
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
