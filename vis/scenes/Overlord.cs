using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Ritgard;

public partial class Overlord : Node3D
{
    private ConcurrentDictionary<Vector3I, HashSet<Node3D>> structures = [];

    [Export]
    public PackedScene TestStructure;

    private VoxelTerrain terrain;
    private VoxelGenerator generator;
    private RandomNumberGenerator rng;

    public override void _EnterTree()
    {
        terrain = GetNode<VoxelTerrain>("VoxelTerrain");
        generator = (VoxelGenerator)terrain.Generator;
        rng = new RandomNumberGenerator();
    }

    public void _OnMeshBlockEntered(Vector3I blockPos)
    {
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
