using CsvHelper;
using CsvHelper.Configuration;
using Godot;
using Ritgard.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Ritgard;

public partial class Overlord : Node
{
    private ConcurrentDictionary<Vector3I, HashSet<Node3D>> structures = [];

    [Export]
    public PackedScene TestStructure { get; set; }

    [Export]
    public string DataCsvPath { get; set; }

    [Export]
    public Label ItemDescriptionLabel { get; set; }

    [Export]
    public Player Player { get; set; }

    private VoxelTerrain terrain;
    private VoxelGenerator generator;
    private RandomNumberGenerator rng;
    private ImmutableDictionary<(Guid id, string absoluteLink), DocumentationItem> data;
    private ImmutableDictionary<(Guid id, string absoluteLink), Vector2I> itemPositions;
    private ImmutableDictionary<string, int> edgeCounts;
    private TestStructure currentItem;

    public const float ItemRadius = 256.0f;
    public const int HeightmapSize = 512;
    public const string DefaultHint = "Hover over a structure to see its description...";

    public override void _EnterTree()
    {
        terrain = GetNode<VoxelTerrain>("VoxelTerrain");
        generator = (VoxelGenerator)terrain.Generator;
        rng = new RandomNumberGenerator();
        generator.Heightmap = Image.CreateEmpty(HeightmapSize, HeightmapSize, false, Image.Format.L8);
    }

    public override void _Ready()
    {
        ItemDescriptionLabel.Text = DefaultHint;

        using var stream = new FileAccessStream(DataCsvPath, FileAccess.ModeFlags.Read);
        using var reader = new System.IO.StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = a => GD.Print($"Found bad data in {a.Field}.")
        });
        var records = csv.GetRecords<DocumentationItem>().ToList();
        var dataBuilder = ImmutableDictionary.CreateBuilder<(Guid id, string absoluteLink), DocumentationItem>();
        foreach (var record in records)
        {
            if (!dataBuilder.ContainsKey((record.Id, record.AbsoluteLink)))
            {
                dataBuilder.Add((record.Id, record.AbsoluteLink), record);
            }
        }
        data = dataBuilder.ToImmutable();

        var positionsBuilder = ImmutableDictionary.CreateBuilder<(Guid id, string absoluteLink), Vector2I>();
        foreach (var (identifier, item) in data)
        {
            var radius = ItemRadius * Mathf.Sqrt(rng.Randf());
            var angle = rng.RandfRange(0, Mathf.Tau);
            positionsBuilder.Add(identifier, new Vector2I(
                Mathf.RoundToInt(radius * Mathf.Cos(angle)),
                Mathf.RoundToInt(radius * Mathf.Sin(angle))
            ));
        }
        itemPositions = positionsBuilder.ToImmutable();

        var edgeCountBuilder = ImmutableDictionary.CreateBuilder<string, int>();
        foreach (var record in records)
        {
            edgeCountBuilder[record.Url] = edgeCountBuilder.TryGetValue(record.Url, out var count)
                ? count + 1 : 1;
            if (!string.IsNullOrEmpty(record.AbsoluteLink))
            {
                edgeCountBuilder[record.AbsoluteLink] =
                    edgeCountBuilder.TryGetValue(record.AbsoluteLink, out var aCount)
                        ? aCount + 1
                        : 1;
            }
        }
        edgeCounts = edgeCountBuilder.ToImmutable();

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

        foreach (var (identifier, position) in itemPositions)
        {
            var height = generator.GetHeight(position.X, position.Y);
            var instance = TestStructure.Instantiate<TestStructure>();
            instance.Identifier = identifier;
            instance.Position = new Vector3(position.X, height, position.Y);
            AddChild(instance);
        }

        Player.HoverChanged += OnHoverChanged;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsAction("interact") && @event.IsPressed() && currentItem is not null)
        {
            var item = data.GetValueOrDefault(currentItem.Identifier.Value);
            var url = Utils.Coalesce(item?.AbsoluteLink, item?.Url);
            if (url is not null)
            {
                OS.ShellOpen(url);
            }
        }
    }


    private void OnHoverChanged(CollisionObject3D hoveree)
    {
        var structure = hoveree?.GetParent<TestStructure>();
        if (structure is not null
            && structure != currentItem
            && structure.Identifier is not null
            && data.TryGetValue(structure.Identifier.Value, out var item)
        )
        {
            currentItem?.ToggleHighlight(false);
            currentItem = structure;
            currentItem.ToggleHighlight(true);

            if (string.IsNullOrEmpty(item.LinkText))
            {
                ItemDescriptionLabel.Text = $"`{item.ShortRepresentation}` at `{item.Url}`";
            }
            else
            {
                ItemDescriptionLabel.Text = $"`{item.LinkText ?? item.RelativeLink}` from "
                    + $"`{item.ShortRepresentation}` at `{item.Url}`";
            }

            Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
        }

        if (structure is null)
        {
            currentItem.ToggleHighlight(false);
            currentItem = null;
            ItemDescriptionLabel.Text = DefaultHint;

            Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        }
    }

    public void _OnMeshBlockEntered(Vector3I blockPos)
    {
        return;
        var origin = terrain.DataBlockToVoxel(blockPos);
        var structCount = rng.RandiRange(0, 2);
        if (structCount == 0)
        {
            return;
        }

        var set = new HashSet<Node3D>();
        for (int i = 0; i < structCount; ++i)
        {
            var position = new Vector3I(
                rng.RandiRange(0, (int)terrain.MeshBlockSize - 1),
                0,
                rng.RandiRange(0, (int)terrain.MeshBlockSize - 1)
            );
            var height = generator.GetHeight(origin.X + position.X, origin.Z + position.Z);
            if (height > origin.Y + (int)terrain.MeshBlockSize || height < origin.Y)
            {
                continue;
            }

            var instance = TestStructure.Instantiate<Node3D>();
            instance.Position = new Vector3(origin.X + position.X, height, origin.Z + position.Z);
            AddChild(instance);
            set.Add(instance);
        }
        structures.AddOrUpdate(blockPos, _ => set, (_, e) =>
        {
            foreach (var existing in e)
            {
                e.Remove(existing);
                RemoveChild(existing);
                existing.QueueFree();
            }
            return set;
        });
    }

    public void _OnMeshBlockExited(Vector3I blockPos)
    {
        return;
        var set = structures.GetValueOrDefault(blockPos);
        if (set is null || set.Count == 0)
        {
            return;
        }

        foreach (var existing in set)
        {
            set.Remove(existing);
            RemoveChild(existing);
            existing.QueueFree();
        }
    }
}
