using System.Buffers;
using Godot;

namespace Ritgard.Structures;

public sealed class Acacia : IStructure
{
    public const float BarkGrowth = 0.4f;

    public int TrunkHeight { get; set; }

    public int CrownBreadth { get; set; }

    public float Leafiness { get; set; } = 1.0f;

    public (Vector3I min, Vector3I max) Measure()
    {
        var totalHeight = TrunkHeight + 1;
        var halfSize = Mathf.Max(3, Mathf.Max(CrownBreadth, (int)Mathf.Log(totalHeight * BarkGrowth)));
        return (
            new(-halfSize, 0, -halfSize),
            new(halfSize, totalHeight, halfSize)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var totalHeight = TrunkHeight + 1;
        var trunk = Mathf.CeilToInt(Mathf.Log(totalHeight * BarkGrowth));
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * TrunkHeight, trunk, Mathf.CeilToInt(trunk / 2f), Blocks.Bark);

        if (TrunkHeight >= 3)
        {
            var span = Mathf.Max(CrownBreadth - 1, 3);
            buffer.FillLine(
                Vector3I.Up * (TrunkHeight - 2),
                Vector3I.Up * (TrunkHeight - 1) + Vector3I.Forward * (span - 1),
                1,
                1,
                Blocks.Bark
            );
            buffer.FillLine(
                Vector3I.Up * (TrunkHeight - 2),
                Vector3I.Up * (TrunkHeight - 1) + Vector3I.Back * (span - 1),
                1,
                1,
                Blocks.Bark
            );
            buffer.FillLine(
                Vector3I.Up * (TrunkHeight - 2),
                Vector3I.Up * (TrunkHeight - 1) + Vector3I.Left * (span - 1),
                1,
                1,
                Blocks.Bark
            );
            buffer.FillLine(
                Vector3I.Up * (TrunkHeight - 2),
                Vector3I.Up * (TrunkHeight - 1) + Vector3I.Right * (span - 1),
                1,
                1,
                Blocks.Bark
            );
        }

        if (CrownBreadth > 0)
        {
            buffer.FillSpottyCylinder(Vector3I.Up * TrunkHeight, CrownBreadth, 1, Blocks.AcaciaLeaves, Leafiness);
            // buffer.FillSpottyCylinder(Vector3I.Up * (Height - 2), Breadth, 1, Blocks.Leaves, Leafiness);
        }
    }

}
