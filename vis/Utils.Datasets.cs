using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

            ImmutableArray<string> positionsFilenames =
            [
                $"{datasetName}{PositionsFileSuffix}.json",
                ..dataDir.EnumerateFiles($"{datasetName}{PositionsFileSuffix}.*.json").Select(f => f.Name)
            ];
            foreach (var positionsFilename in positionsFilenames)
            {
                var positionsPath = Path.Combine(dataDir.FullName, positionsFilename);
                if (!File.Exists(positionsPath))
                {
                    continue;
                }

                var subnameMatch = Regex.Match(
                    positionsFilename,
                    @$"^{datasetName}{PositionsFileSuffix}\.*([\w\d-_]*)\.json$"
                );
                if (!subnameMatch.Success)
                {
                    continue;
                }

                var subname = subnameMatch.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(subname))
                {
                    subname = null;
                }

                var terrainFilename = subname is null
                    ? $"{datasetName}{TerrainFileSuffix}.json"
                    : $"{datasetName}{TerrainFileSuffix}.{subname}.json";
                var terrainPath = Path.Combine(dataDir.FullName, terrainFilename);
                if (!File.Exists(terrainPath))
                {
                    terrainPath = null;
                }

                builder.Add(
                    new DatasetInfo
                    {
                        Name = subname is null ? datasetName : $"{datasetName} ({subname})",
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
