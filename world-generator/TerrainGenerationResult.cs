using System;
using System.Collections.Immutable;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public record TerrainGenerationResult(
    string RepositoryName,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    ImmutableArray<TerrainPreset> Terrains
);

public record TerrainPreset(
    SlidingWindowPreset SlidingWindow,
    ConversationScope Scope,
    ImmutableDictionary<int, IslandHeightmap> IslandHeightmaps
);
