using System;
using System.Collections.Immutable;
using System.IO;

namespace Ritgard;

public static partial class Utils
{
    public const string PositionsFileSuffix = "-positions";
    public const string TerrainFileSuffix = "-terrain";

    public static ImmutableArray<DatasetInfo> DiscoverDatasets(string dataPath)
    {
        var dataDir = new DirectoryInfo(dataPath);
        if (!dataDir.Exists)
        {
            throw new ArgumentException("Directory does not exist.", nameof(dataPath));
        }

        var builder = ImmutableArray.CreateBuilder<DatasetInfo>();

        foreach (var candidateFile in dataDir.EnumerateFiles("*.json"))
        {
            var datasetName = Path.GetFileNameWithoutExtension(candidateFile.Name);
            var positionsFilename = $"{datasetName}{PositionsFileSuffix}.json";
            var positionsPath = Path.Combine(dataDir.FullName, positionsFilename);
            var terrainFilename = $"{datasetName}{TerrainFileSuffix}.json";
            var terrainPath = Path.Combine(dataDir.FullName, terrainFilename);
            if (!File.Exists(terrainPath))
            {
                terrainPath = null;
            }

            if (File.Exists(positionsPath))
            {
                builder.Add(
                    new DatasetInfo
                    {
                        Name = datasetName,
                        DataFilePath = candidateFile.FullName,
                        TopicFilePath = positionsPath,
                        TerrainFilePath = terrainPath
                    }
                );
            }
        }

        builder.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.InvariantCultureIgnoreCase));
        return builder.ToImmutable();
    }
}
