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

public partial class Overlord : Node3D
{
    private ConcurrentDictionary<Vector3I, HashSet<Node3D>> structures = [];

    [Export]
    public PackedScene TestStructure { get; set; }

    [Export]
    public string DataCsvPath { get; set; }

    private VoxelTerrain terrain;
    private VoxelGenerator generator;
    private RandomNumberGenerator rng;
    private ImmutableDictionary<(Guid id, string absoluteLink), DocumentationItem> data;
    private ImmutableDictionary<(Guid id, string absoluteLink), Vector2I> itemPositions;

    public const float ItemRadius = 256.0f;

    public override void _EnterTree()
    {
        terrain = GetNode<VoxelTerrain>("VoxelTerrain");
        generator = (VoxelGenerator)terrain.Generator;
        rng = new RandomNumberGenerator();
    }

    public override void _Ready()
    {
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

        foreach (var (identifier, position) in itemPositions)
        {
            var height = generator.GetHeight(position.X, position.Y);
            var instance = TestStructure.Instantiate<Node3D>();
            instance.Position = new Vector3(position.X, height, position.Y);
            AddChild(instance);
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
