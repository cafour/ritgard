using System;
using System.Numerics;
using Godot;

namespace Ritgard.Structures;

public class Broadleaf : IStructure
{
    public int TrunkHeight { get; set; }

    public int CrownBreadth { get; set; }

    public float Leafiness { get; set; } = 1.0f;

    public (Vector3I min, Vector3I max) Measure()
    {
        return (
            new(-CrownBreadth, 0, -CrownBreadth),
            new(CrownBreadth, TrunkHeight + CrownBreadth * 2, CrownBreadth)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var totalHeight = TrunkHeight + CrownBreadth * 2 - 1;
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * TrunkHeight, Blocks.Bark);
        buffer.FillSpottySphere(Vector3I.Up * (TrunkHeight + CrownBreadth), CrownBreadth, Blocks.Leaves02, Leafiness);
    }
}
