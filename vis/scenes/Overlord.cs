using Godot;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;
using NetTopologySuite.Triangulate.QuadEdge;
using Ritgard.Data;
using Ritgard.Mining;
using Ritgard.Structures;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

namespace Ritgard;

public partial class Overlord : Node
{
    public static Overlord Instance { get; private set; }

    private ConcurrentDictionary<Vector3I, HashSet<Node3D>> structures = [];

    [Export]
    public PackedScene TestStructure { get; set; }

    [Export]
    public PackedScene TopicIslandScene { get; set; }

    [Export]
    public string DataJsonPath { get; set; }

    [Export]
    public string PositionsCsvPath { get; set; }

    [Export]
    public Label ItemDescriptionLabel { get; set; }

    [Export]
    public Player Player { get; set; }

    [Export]
    public Control ControlsContainer { get; set; }

    public const int ByteLengthMappingMin = 3;
    public const int ByteLengthMappingMax = 20;
    public const int WordLengthMappingMin = 3;
    public const int WordLengthMappingMax = 20;
    public const int TagsLengthMappingMin = 3;
    public const int TagsLengthMappingMax = 20;

    public Func<long, int> ByteLengthMapping { get; private set; }
    public Func<long, int> WordLengthMapping { get; private set; }
    public Func<long, int> TagCountMapping { get; private set; }
    public MiningResult MiningResult { get; private set; }
    public ImmutableDictionary<long, Issue> Data { get; private set; }
    public ImmutableDictionary<string, Topic> Topics { get; private set; }
    public ImmutableDictionary<long, Vector3> Positions { get; private set; }
    public DateTimeOffset MinDate { get; private set; }
    public DateTimeOffset MaxDate { get; private set; }
    public TimeSpan AvgIssueLength { get; private set; }

    private RandomNumberGenerator rng;
    private TestStructure currentItem;

    public const float ItemRadius = 256.0f;
    public const float StructureRadius = 7f;
    public const int HeightmapSize = 1024;
    public const string DefaultHint = "Hover over a structure to see its description...";
    public const byte MaxTerrainHeight = 50;

    public override void _EnterTree()
    {
        if (Instance is not null)
        {
            Instance.QueueFree();
        }
        Instance = this;

        rng = new RandomNumberGenerator();
    }

    public override void _Ready()
    {
        ItemDescriptionLabel.Text = DefaultHint;

        MiningResult = Utils.ReadGodotJson<MiningResult>(DataJsonPath);
        Data = MiningResult.Issues;
        MaxDate = Data.Values.Max(i => i.UpdatedAt ?? i.CreatedAt);
        MinDate = Data.Values.Min(i => i.UpdatedAt ?? i.CreatedAt);
        AvgIssueLength = TimeSpan.FromSeconds(Data.Values.Average(i => i.GetTimeSpan().TotalSeconds));

        // var minBytes = data.Values.Min(v => v.ByteLength) ?? 0;
        // var maxBytes = data.Values.Max(v => v.ByteLength) ?? 1;
        // ByteLengthMapping = b => Mathf.RoundToInt(Mathf.Remap(
        //     b,
        //     minBytes,
        //     maxBytes,
        //     ByteLengthMappingMin,
        //     ByteLengthMappingMax
        // ));

        // var minWords = data.Values.Min(v => v.WordLength) ?? 0;
        // var maxWords = data.Values.Max(v => v.WordLength) ?? 1;
        // WordLengthMapping = w => Mathf.RoundToInt(Mathf.Remap(
        //     w,
        //     minWords,
        //     maxWords,
        //     WordLengthMappingMin,
        //     WordLengthMappingMax
        // ));

        // var minTags = data.Values.Min(v => v.TagCount) ?? 0;
        // var maxTags = data.Values.Max(v => v.TagCount) ?? 1;
        // TagCountMapping = t => Mathf.RoundToInt(Mathf.Remap(
        //     t,
        //     minTags,
        //     maxTags,
        //     TagsLengthMappingMin,
        //     TagsLengthMappingMax
        // ));

        var issueTopics = Utils.ReadGodotCsv<IssueTopic>(PositionsCsvPath);
        var averageDistance = issueTopics.Average(i => i.NearestNeighborDistance);
        var bbox = new Rect2(issueTopics.Min(p => (float)p.X), issueTopics.Min(p => (float)p.Y), Vector2.Zero);
        foreach (var position in issueTopics)
        {
            bbox = bbox.Expand(new Vector2((float)position.X, (float)position.Y));
        }
        var center = bbox.GetCenter();

        var factor = StructureRadius / averageDistance;
        Positions = issueTopics.ToImmutableDictionary(i => i.Id, i => new Vector3(
            (float)((i.X - center.X) * factor),
            GetLevelForIssue(i.Id),
            (float)((i.Y - center.Y) * factor)
        ));
        Topics = issueTopics.GroupBy(i => i.Topic)
            .ToImmutableDictionary(g => g.Key, g => new Topic(g.Key, g.Select(i => i.Id).ToImmutableHashSet()));
        // foreach (var topic in Topics.Values)
        // {
        //     var topicIsland = TopicIslandScene.Instantiate<TopicIsland>();
        //     topicIsland.Topic = topic;
        // }

        var firstTopic = Topics.Values.OrderBy(t => t.Title).First();
        var topicIsland = TopicIslandScene.Instantiate<TopicIsland>();
        topicIsland.Topic = firstTopic;
        AddChild(topicIsland);

        // foreach (var (identifier, item) in data)
        // {
        //     var edgeCount = edgeCounts.GetValueOrDefault(Utils.Coalesce(item.AbsoluteLink, item.Url));
        //     if (edgeCount == 0)
        //     {
        //         continue;
        //     }
        //     var position = itemPositions[identifier] + new Vector2I(256, 256);
        //     generator.Heightmap.FillRect(
        //         new Rect2I(position - new Vector2I(3, 3), new Vector2I(5, 5)),
        //         new Color() { R8 = Math.Clamp(edgeCount, 0, 255) }
        //     );
        // }

        // ComputeHeighmapCircles(Mathf.RoundToInt(StructureRadius));

        foreach (var (id, position) in Positions)
        {
            var instance = TestStructure.Instantiate<TestStructure>();
            instance.Id = id;
            instance.ControlsContainer = ControlsContainer;
            instance.Item = Data[id];
            instance.Position = position;
            AddChild(instance);
        }

        Player.HoverChanged += OnHoverChanged;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsAction("interact") && @event.IsPressed() && currentItem is not null)
        {
            var item = Data.GetValueOrDefault(currentItem.Id.Value);
            if (!string.IsNullOrEmpty(item.Url))
            {
                OS.ShellOpen(item.Url);
            }
        }
    }

    private void ComputeHeighmapTriangularization()
    {
        var inversePositions = Positions.ToImmutableDictionary(
            t => new Coordinate(t.Value.X, t.Value.Y),
            t => t.Key
        );
        var triangulationBuilder = new DelaunayTriangulationBuilder();
        triangulationBuilder.SetSites([.. inversePositions.Keys]);
        var subdivision = triangulationBuilder.GetSubdivision();
        var dateLength = MaxDate - MinDate;

        double GetHeightAtPoint(double x, double y)
        {
            if (!inversePositions.TryGetValue(new Coordinate(x, y), out var id))
            {
                return 0.0;
            }
            var issue = Data[id];
            var date = issue.UpdatedAt ?? issue.CreatedAt;
            return (date - MinDate) / dateLength * 100.0;
        }

        for (int y = -HeightmapSize / 2; y < HeightmapSize / 2; ++y)
        {
            for (int x = -HeightmapSize / 2; x < HeightmapSize / 2; ++x)
            {
                var hx = x + HeightmapSize / 2;
                var hy = y + HeightmapSize / 2;
                QuadEdge? edge = null;
                try
                {
                    edge = subdivision.Locate(new Coordinate(x, y));
                }
                catch (LocateFailureException)
                {
                }

                if (edge is null)
                {
                    // generator.Heightmap.SetPixel(hx, hy, new Color() { R8 = 0 });
                    // generator.Heightmap[hy, hx] = 0;
                    continue;
                }

                var p1 = edge.Orig;
                var p2 = edge.Dest;
                var p3 = edge.ONext.Dest;
                var height = InterpolateBarycentric(
                    x, y,
                    p1.X, p1.Y, GetHeightAtPoint(p1.X, p1.Y),
                    p2.X, p2.Y, GetHeightAtPoint(p2.X, p2.Y),
                    p3.X, p3.Y, GetHeightAtPoint(p3.X, p3.Y)
                );
                height = Mathf.Max(height, 0.0);
                // generator.Heightmap.SetPixel(hx, hy, new Color { R8 = Mathf.RoundToInt(height) });
                // generator.Heightmap[hy, hx] = (byte)Mathf.RoundToInt(height);
            }
        }
    }

    private void ComputeHeighmapCircles(int circleRadius)
    {
        var maxDate = Data.Values.Max(i => i.UpdatedAt ?? i.CreatedAt);
        var minDate = Data.Values.Min(i => i.UpdatedAt ?? i.CreatedAt);
        var dateLength = maxDate - minDate;

        var radiusSquared = circleRadius * circleRadius;
        foreach (var (id, pos) in Positions)
        {
            // var height = GetHeightForIssue(id);
            var height = (byte)(GetLevelForIssue(id) * 10);
            for (int y = -circleRadius + 1; y < circleRadius; ++y)
            {
                for (int x = -circleRadius + 1; x < circleRadius; ++x)
                {
                    var hx = Mathf.RoundToInt(pos.X) + HeightmapSize / 2 + x;
                    var hy = Mathf.RoundToInt(pos.Y) + HeightmapSize / 2 + y;

                    if (x * x + y * y < radiusSquared + 1)
                    {
                        // generator.Heightmap[hy, hx] = Math.Max(generator.Heightmap[hy, hx], height);
                    }
                    else
                    {
                        // generator.Heightmap[hy, hx] = Math.Max(generator.Heightmap[hy, hx], (byte)0);
                    }
                }
            }
        }
    }

    private void OnHoverChanged(CollisionObject3D hoveree)
    {
        var parent = hoveree?.GetParent();
        if (parent is TestStructure structure)
        {
            if (structure.Id is not null && Data.TryGetValue(structure.Id.Value, out var item))
            {
                currentItem?.ToggleHighlight(false);
                currentItem = structure;
                currentItem.ToggleHighlight(true);
                ItemDescriptionLabel.Text = $"#{item.Number} {item.Title}\n\t{item.CreatedAt:s}--{item.UpdatedAt:s} ({item.UpdatedAt - item.CreatedAt:c}, {item.CommentCount} comments)";

                Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
            }
        }
        else if (parent is TopicIsland island)
        {
            ItemDescriptionLabel.Text = $"The '{island.Topic.Title}' topic island";
        }
        else if (parent is null)
        {
            currentItem?.ToggleHighlight(false);
            currentItem = null;
            Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
            ItemDescriptionLabel.Text = DefaultHint;
        }
    }

    public void _OnMeshBlockEntered(Vector3I blockPos)
    {
        return;
        // var origin = terrain.DataBlockToVoxel(blockPos);
        // var structCount = rng.RandiRange(0, 2);
        // if (structCount == 0)
        // {
        //     return;
        // }

        // var set = new HashSet<Node3D>();
        // for (int i = 0; i < structCount; ++i)
        // {
        //     var position = new Vector3I(
        //         rng.RandiRange(0, (int)terrain.MeshBlockSize - 1),
        //         0,
        //         rng.RandiRange(0, (int)terrain.MeshBlockSize - 1)
        //     );
        //     var height = generator.GetHeight(origin.X + position.X, origin.Z + position.Z);
        //     if (height > origin.Y + (int)terrain.MeshBlockSize || height < origin.Y)
        //     {
        //         continue;
        //     }

        //     var instance = TestStructure.Instantiate<Node3D>();
        //     instance.Position = new Vector3(origin.X + position.X, height, origin.Z + position.Z);
        //     AddChild(instance);
        //     set.Add(instance);
        // }
        // structures.AddOrUpdate(blockPos, _ => set, (_, e) =>
        // {
        //     foreach (var existing in e)
        //     {
        //         e.Remove(existing);
        //         RemoveChild(existing);
        //         existing.QueueFree();
        //     }
        //     return set;
        // });
    }

    public void _OnMeshBlockExited(Vector3I blockPos)
    {
        return;
        // var set = structures.GetValueOrDefault(blockPos);
        // if (set is null || set.Count == 0)
        // {
        //     return;
        // }

        // foreach (var existing in set)
        // {
        //     set.Remove(existing);
        //     RemoveChild(existing);
        //     existing.QueueFree();
        // }
    }

    public static double InterpolateBarycentric(
        double x,
        double y,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        double x3,
        double y3,
        double z3
    )
    {
        double detT = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
        double alpha = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / detT;
        double beta = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / detT;
        double gamma = 1.0 - alpha - beta;
        return alpha * z1 + beta * z2 + gamma * z3;
    }

    private byte GetHeightForIssue(long id)
    {
        var issue = Data[id];
        var date = issue.UpdatedAt ?? issue.CreatedAt;
        var dateLength = MaxDate - MinDate;
        return (byte)Mathf.RoundToInt(Math.Clamp((date - MinDate) / dateLength * MaxTerrainHeight, 0.0, 255.0));
    }
    
    private byte GetLevelForIssue(long id)
    {
        var issue = Data[id];
        var date = issue.UpdatedAt ?? issue.CreatedAt;
        if (date.Date == MaxDate.Date)
        {
            return 50;
        }
        else if (date > MaxDate - TimeSpan.FromDays(7))
        {
            return 40;
        }
        else if (date > MaxDate - TimeSpan.FromDays(30))
        {
            return 30;
        }
        else if (date > MaxDate - TimeSpan.FromDays(365))
        {
            return 20;
        }
        else
        {
            return 10;
        }
    }
}
