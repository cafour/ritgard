using System;
using System.Collections.Immutable;
using System.IO;

namespace Ritgard;

public static partial class Utils
{
    public const string PositionsFileSuffix = "-positions";

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
            if (File.Exists(positionsPath))
            {
                builder.Add(
                    new DatasetInfo
                    {
                        Name = datasetName,
                        DataFilePath = candidateFile.FullName,
                        TopicFilePath = positionsPath
                    }
                );
            }
        }

        builder.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.InvariantCultureIgnoreCase));
        return builder.ToImmutable();
    }
}
