using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public class TerrainGenerator(
    ActiveRepository repo,
    ILoggerFactory loggerFactory
) : IDisposable
{
    public static readonly TimeSpan DefaultStepLength = TimeSpan.FromDays(1);
    public const int DefaultBatchSize = -1;

    private readonly ILogger<TerrainGenerator> logger = loggerFactory.CreateLogger<TerrainGenerator>();

    private readonly IMemoryCache cache = new MemoryCache(
        new MemoryCacheOptions
        {
            SizeLimit = 2_000_000_000
        },
        loggerFactory
    );

    public TerrainGenerationResult Generate(
        ConversationScope scope = ConversationScope.All,
        SlidingWindowPreset slidingWindowPresets = SlidingWindowPreset.AllPresets,
        TimeSpan? stepLength = null,
        int batchSize = DefaultBatchSize,
        int startStep = 0,
        int stepCount = -1,
        bool shouldGenerateScopePermutations = true,
        CancellationToken ct = default
    )
    {
        var startedAt = DateTimeOffset.UtcNow;

        stepLength ??= DefaultStepLength;

        if (stepCount == -1)
        {
            var startDate = repo.MinDate + startStep * stepLength.Value;
            stepCount = Math.Max(1, (int)Math.Ceiling((repo.MaxDate - startDate) / stepLength.Value));
        }

        if (scope == ConversationScope.None)
        {
            return new TerrainGenerationResult(
                RepositoryName: repo.Mining.Repository.Name,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                Terrains: [],
                StepLength: stepLength.Value,
                StartStep: startStep,
                StepCount: stepCount,
                BatchSize: batchSize
            );
        }

        var terrains = ImmutableArray.CreateBuilder<TerrainPreset>();
        if (!shouldGenerateScopePermutations)
        {
            terrains.AddRange(
                GenerateSingleScope(
                    scope,
                    slidingWindowPresets,
                    stepLength.Value,
                    batchSize,
                    startStep,
                    stepCount,
                    ct
                )
            );
        }
        else
        {
            var scopePermutations = GetScopePermutations(scope)
                .Where(s => s != ConversationScope.None)
                .ToImmutableArray();
            foreach (var (i, subScope) in scopePermutations.Index())
            {
                logger.LogInformation(
                    "Generating scope permutation '{Scope}' ({Number}/{Count}).",
                    subScope,
                    i + 1,
                    scopePermutations.Length
                );

                terrains.AddRange(
                    GenerateSingleScope(
                        subScope,
                        slidingWindowPresets,
                        stepLength.Value,
                        batchSize,
                        startStep,
                        stepCount,
                        ct
                    )
                );
            }
        }

        return new TerrainGenerationResult(
            RepositoryName: repo.Mining.Repository.Name,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Terrains: terrains.ToImmutable(),
            StepLength: stepLength.Value,
            StartStep: startStep,
            StepCount: stepCount,
            BatchSize: batchSize
        );
    }

    private ImmutableArray<TerrainPreset> GenerateSingleScope(
        ConversationScope scope,
        SlidingWindowPreset slidingWindowPresets,
        TimeSpan stepLength,
        int batchSize,
        int startStep,
        int stepCount,
        CancellationToken ct = default
    )
    {
        logger.LogInformation(
            "Generating terrain for scope '{Scope}' and sliding windows '{SlidingWindows}'.",
            scope,
            slidingWindowPresets
        );
        var islandGenerators = repo.TopicModelling.Topics
            .Where(p => p.Key != -1)
            .ToImmutableDictionary(
                p => p.Key,
                p => cache.GetOrCreate<IslandHeightmapGenerator>(
                    new CacheKey(scope, p.Key),
                    entry =>
                    {
                        var generator = new IslandHeightmapGenerator(
                            repo,
                            p.Key,
                            scope,
                            loggerFactory.CreateLogger<IslandHeightmapGenerator>()
                        );
                        entry.Size = generator.Size.x * generator.Size.y;
                        return generator;
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
        var slidingWindows = GetSlidingWindowPresets(slidingWindowPresets).ToImmutableArray();
        foreach (var (index, window) in slidingWindows.Index())
        {
            logger.LogInformation(
                "Generating terrain for scope '{Scope}' and single sliding window '{SlidingWindow}' ({Number}/{Count}).",
                scope,
                window,
                index + 1,
                slidingWindows.Length
            );
            var topics = new ConcurrentDictionary<int, ImmutableArray<IslandHeightmap>>();
            Parallel.ForEach(
                islandGenerators.Keys,
                topicId =>
                {
                    var generator = islandGenerators[topicId];
                    var heightmaps = ImmutableArray.CreateBuilder<IslandHeightmap>();
                    logger.LogInformation(
                        "Generating heightmap for topic '{TopicId}' ({TopicNumber}/{TopicCount}), the '{SlidingWindow}', and '{Scope}' scope.",
                        topicId,
                        topicId + 1,
                        repo.TopicModelling.Topics.Count,
                        window,
                        scope
                    );
                    if (batchSize == -1)
                    {
                        heightmaps.Add(
                            generator.Generate(
                                slidingWindow: window.ToTimeSpan(),
                                stepLength: stepLength,
                                startStep: startStep,
                                stepCount: stepCount,
                                ct: ct
                            )
                        );
                    }
                    else
                    {
                        for (int s = startStep; s < startStep + stepCount; s += batchSize)
                        {
                            var currentSize = Math.Min(batchSize, (startStep + stepCount) - s);
                            heightmaps.Add(
                                generator.Generate(
                                    slidingWindow: window.ToTimeSpan(),
                                    stepLength: stepLength,
                                    startStep: s,
                                    stepCount: currentSize,
                                    ct: ct
                                )
                            );
                        }
                    }

                    topics.TryAdd(
                        topicId,
                        heightmaps.ToImmutable()
                    );
                }
            );

            terrains.Add(
                new TerrainPreset(
                    SlidingWindow: window,
                    Scope: scope,
                    IslandHeightmaps: topics.ToImmutableDictionary(
                        p => p.Key,
                        p => p.Value.Select(IslandHeightmap.WriteToString).ToImmutableArray()
                    )
                )
            );
        }

        return terrains.ToImmutable();
    }

    private record CacheKey(
        ConversationScope Scope,
        int TopicId
    );

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        cache.Dispose();
    }

    private static IEnumerable<ConversationScope> GetScopePermutations(ConversationScope scope)
    {
        if (scope == ConversationScope.None)
        {
            yield return ConversationScope.None;
            yield break;
        }

        var highestBit = BitOperations.Log2((uint)scope);
        var withoutHighest = (uint)scope ^ (1u << highestBit);
        foreach (var result in GetScopePermutations((ConversationScope)withoutHighest))
        {
            yield return result;
            yield return (ConversationScope)((uint)result | (1u << highestBit));
        }
    }

    private static IEnumerable<SlidingWindowPreset> GetSlidingWindowPresets(SlidingWindowPreset presets)
    {
        for (int slidingWindow = 1; slidingWindow <= (int)SlidingWindowPreset.MaxValue; slidingWindow <<= 1)
        {
            if (presets.HasFlag((SlidingWindowPreset)slidingWindow))
            {
                yield return (SlidingWindowPreset)slidingWindow;
            }
        }
    }
}
