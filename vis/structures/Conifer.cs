using Godot;

namespace Ritgard.Structures;

public sealed class Confifer : IStructure
{
    public const float BarkGrowth = 0.4f;

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
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * (Height - 2), trunk, 1, Blocks.Bark);
        var coneHeight = Mathf.RoundToInt(Height / 3.0f * 2.0f);
        buffer.FillCone(
            Vector3I.Up * (Height - coneHeight),
            coneHeight,
            Breadth,
            Blocks.ConiferLeaves
        );
    }

}
