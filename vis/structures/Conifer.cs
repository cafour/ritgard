using Godot;

namespace Ritgard.Structures;

public sealed class Confifer : IStructure
{
    public const float BarkGrowth = 0.4f;

    public int TrunkHeight { get; set; }

    public int CrownBreadth { get; set; }

    public float Leafiness { get; set; } = 1.0f;

    public (Vector3I min, Vector3I max) Measure()
    {
        return (
            new(-CrownBreadth, 0, -CrownBreadth),
            new(CrownBreadth, TrunkHeight + Mathf.RoundToInt(CrownBreadth * 2f), CrownBreadth)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var coneHeight = Mathf.RoundToInt(CrownBreadth * 2f);
        var totalHeight = TrunkHeight + coneHeight;
        var trunk = Mathf.RoundToInt(Mathf.Log(totalHeight * BarkGrowth));
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * TrunkHeight, trunk, Mathf.CeilToInt(trunk / 2f), Blocks.Bark);
        buffer.FillCone(
            Vector3I.Up * (TrunkHeight - 1),
            coneHeight,
            CrownBreadth,
            Blocks.ConiferLeaves
        );
    }

}
