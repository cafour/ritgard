using System;
using System.Collections.Immutable;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public record TerrainGenerationResult(
    string RepositoryName,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    ImmutableArray<TerrainPreset> Terrains,
    TimeSpan StepLength,
    int StartStep,
    int StepCount,
    int BatchSize = -1
);

public record TerrainPreset(
    SlidingWindowPreset SlidingWindow,
    ConversationScope Scope,
    ImmutableDictionary<int, ImmutableArray<string>> IslandHeightmaps
);
