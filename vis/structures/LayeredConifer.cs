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

    public (Vector3I min, Vector3I max) Measure()
    {
        var breadth = Math.Max(3, Layers.Length / 4);
        return (
            new(-breadth, 0, -breadth),
            new(breadth, TrunkHeight + Layers.Length + 1, breadth)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var coneHeight = Layers.Length;
        var totalHeight = TrunkHeight + coneHeight;
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * totalHeight, 0.5f, 0.5f, Blocks.Bark);
        for (int i = 0; i < Layers.Length; ++i)
        {
            if (!Layers[i])
            {
                continue;
            }

            var currentBreadth = Math.Max(1, (coneHeight - i) / 4);
            buffer.FillSpottyCylinder(Vector3I.Up * (TrunkHeight + i), currentBreadth, 1, Blocks.ConiferLeaves, 1);
        }
    }

}
