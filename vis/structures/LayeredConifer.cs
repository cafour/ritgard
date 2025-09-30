using System;
using System.Collections;
using System.Collections.Immutable;
using Godot;

namespace Ritgard.Structures;

public sealed class LayeredConifer : IStructure
{
    public const float BarkGrowth = 0.4f;

    public int TrunkHeight { get; set; }

    public BitArray Layers { get; set; }
    
    public bool HasCap { get; set; }

    public int MaxBreadth { get; set; } = int.MaxValue;

    public (Vector3I min, Vector3I max) Measure()
    {
        var breadth = Math.Min(Math.Max(2, Layers.Length / 4), MaxBreadth);
        return (
            new(-breadth, 0, -breadth),
            new(breadth, TrunkHeight + Layers.Length + (HasCap ? 2 : 1) + 1, breadth)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var coneHeight = Layers.Length;
        var totalHeight = TrunkHeight + coneHeight;
        buffer.FillBresenhamLine(Vector3I.Zero, Vector3I.Up * (totalHeight - 1), Blocks.Bark);
        buffer.FillSpottyCylinder(Vector3I.Up * (TrunkHeight + 0), 2, 1, Blocks.ConiferLeaves, 1);

        for (int i = 0; i < Layers.Length; ++i)
        {
            if (!Layers[i])
            {
                continue;
            }

            var currentBreadth = Math.Min(Math.Max(2, (coneHeight - i) / 4), MaxBreadth);
            buffer.FillSpottyCylinder(Vector3I.Up * (TrunkHeight + i), currentBreadth, 1, Blocks.ConiferLeaves, 1);
        }

        if (HasCap)
        {
            buffer.FillCone(Vector3I.Up * totalHeight, 2, 2, Blocks.ConiferLeaves);
        }
        else
        {
            buffer.SetAt(Vector3I.Up * totalHeight, Blocks.Bark);
        }
    }

}
