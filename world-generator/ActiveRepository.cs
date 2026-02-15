using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using NetTopologySuite.Index.KdTree;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public record ActiveRepository
{
    public const int StructureRadius = 3;

    public required DatasetId Dataset { get; init; }

    public required MiningResult Mining { get; init; }

    public required TopicModellingResult TopicModelling { get; init; }

    public TerrainGenerationResult? Terrain { get; init; } = null;

    public required ImmutableDictionary<string, ActiveItem> Items { get; init; }

    public required KdTree<ActiveItem> ItemTree { get; init; }

    public DateTimeOffset MinDate { get; init; }

    public DateTimeOffset MaxDate { get; init; }

    public TimeSpan AvgIssueLength { get; init; }

    public (Vector2 min, Vector2 max) BBox { get; init; }

    public Vector2 BBoxSize { get; init; }

    public static ActiveRepository Create(
        DatasetId dataset,
        MiningResult mining,
        TopicModellingResult topicModelling,
        TerrainGenerationResult? terrain = null
    )
    {
        var bbox = (
            min: new Vector2(
                topicModelling.Items.Values.Min(p => (float)p.X),
                topicModelling.Items.Values.Min(p => (float)p.Y)
            ),
            max: new Vector2(
                topicModelling.Items.Values.Max(p => (float)p.X),
                topicModelling.Items.Values.Max(p => (float)p.Y)
            )
        );
        var center = (bbox.min + bbox.max) / 2f;
        var items = mining.Issues.Values.Cast<IConversation>()
            .Concat(mining.PullRequests.Values)
            .Concat(mining.Discussions.Values)
            .ToImmutableDictionary(
                i => i.Id,
                i => ActiveItem.FromConversation(
                    conversation: i,
                    position: new Vector2(
                        x: (float)topicModelling.Items[i.Id].X - center.X,
                        y: (float)topicModelling.Items[i.Id].Y - center.Y
                    ),
                    topicId: topicModelling.Items.GetValueOrDefault(i.Id)?.TopicId ?? -1
                )
            );
        var padding = new Vector2(StructureRadius + 1, StructureRadius + 1);
        bbox = (min: bbox.min - center, max: bbox.max - center);
        bbox = (min: bbox.min - padding, max: bbox.max + padding);

        var kdTree = new KdTree<ActiveItem>();
        foreach (var item in items.Values)
        {
            kdTree.Insert(
                new NetTopologySuite.Geometries.Coordinate(item.Position.X, item.Position.Y),
                item
            );
        }

        var repo = new ActiveRepository
        {
            Dataset = dataset,
            Mining = mining,
            TopicModelling = topicModelling,
            Terrain = terrain,
            Items = items,
            MaxDate = Ritgard.Mining.Utils.Max(
                mining.Repository.UpdatedAt ?? default,
                items.Values.Max(i => i.Conversation.UpdatedAt)
            ),
            MinDate = Ritgard.Mining.Utils.Min(
                mining.Repository.CreatedAt,
                items.Values.Min(i => i.Conversation.CreatedAt)
            ),
            AvgIssueLength = TimeSpan.FromSeconds(items.Average(i => i.Value.Conversation.GetDuration().TotalSeconds)),
            BBox = bbox,
            BBoxSize = new Vector2(bbox.max.X - bbox.min.X, bbox.max.Y - bbox.min.Y),
            ItemTree = kdTree
        };

        return repo;
    }
}
