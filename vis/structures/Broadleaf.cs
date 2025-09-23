using System;
using System.Numerics;
using Godot;

namespace Ritgard.Structures;

public class Broadleaf : IStructure
{
    public const float BarkGrowth = 0.8f;
    
    public int Height { get; set; }
    
    public int Breadth { get; set; }

    public float Leafiness { get; set; } = 1.0f;

    public (Vector3I min, Vector3I max) Measure()
    {
        return (
            new(-Breadth, 0, -Breadth),
            new(Breadth, Height + 1, Breadth)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var trunk = (int)Mathf.Log(Height * BarkGrowth);
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * (Height - 1), trunk, 1, Blocks.Bark);
        buffer.FillSpottySphere(Vector3I.Up * (Height - Breadth + 1), Breadth, Blocks.Leaves, Leafiness);
    }
}
