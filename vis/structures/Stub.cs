using Godot;

namespace Ritgard.Structures;

public sealed class Stub : IStructure
{
    public int TrunkHeight { get; set; }

    public (Vector3I min, Vector3I max) Measure()
    {
        return (
            new Vector3I(0, 0, 0),
            new Vector3I(2, TrunkHeight + 2, 2)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * TrunkHeight, Blocks.Bark);
    }

}
