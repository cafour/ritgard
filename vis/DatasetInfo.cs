using System;
using Godot;
using Ritgard.Mining;
using Ritgard.WorldGenerator;

namespace Ritgard;

[GlobalClass]
public partial class DatasetInfo : Resource
{
    [Export]
    public string? Name { get; set; }

    [Export]
    public string? DataFilePath { get; set; }

    [Export]
    public string? TopicFilePath { get; set; }

    public ActiveRepository Load()
    {
        if (Name is null || DataFilePath is null || TopicFilePath is null)
        {
            throw new ArgumentException($"Cannot load dataset '{Name}' because it's missing a name or a path.");
        }

        var mining = Utils.ReadGodotJson<MiningResult>(DataFilePath);
        var topicModelling = Utils.ReadGodotJson<TopicModellingResult>(TopicFilePath);
        if (mining is null || topicModelling is null)
        {
            throw new ArgumentException($"Failed to load data for dataset '${Name}'.");
        }
        return ActiveRepository.Create(new DatasetId(Name, DataFilePath, TopicFilePath), mining, topicModelling);
    }
}
