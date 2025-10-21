using System.Buffers;
using Godot;

namespace Ritgard.Structures;

public sealed class CubeTree : IStructure
{
    public int TrunkHeight { get; set; }

    public int CrownBreadth { get; set; }

    public (Vector3I min, Vector3I max) Measure()
    {
        var totalHeight = TrunkHeight + 1;
        return (
            new Vector3I(-CrownBreadth, 0, -CrownBreadth),
            new Vector3I(CrownBreadth + 2, TrunkHeight + 2 * CrownBreadth + 2, CrownBreadth + 2)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        buffer.FillBresenhamLine(Vector3I.Zero, Vector3I.Up * TrunkHeight, Blocks.Bark);
        if (CrownBreadth > 0)
        {
            // buffer.FillSpottyCylinder(Vector3I.Up * TrunkHeight, CrownBreadth, 1, Blocks.AcaciaLeaves, Leafiness);
            buffer.FillArea(
                Vector3I.Up * TrunkHeight + new Vector3I(-CrownBreadth + 1, 1, -CrownBreadth + 1),
                Vector3I.Up * TrunkHeight + new Vector3I(CrownBreadth, 2 * CrownBreadth, CrownBreadth),
                Blocks.Leaves03
            );
            // buffer.FillSpottyCylinder(Vector3I.Up * (Height - 2), Breadth, 1, Blocks.Leaves, Leafiness);
        }
    }
}
