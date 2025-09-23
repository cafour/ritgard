using System.Buffers;
using Godot;

namespace Ritgard.Structures;

public sealed class Acacia : IStructure
{
    public const float BarkGrowth = 0.8f;

    public int Height { get; set; }

    public int Breadth { get; set; }

    public float Leafiness { get; set; } = 1.0f;

    public (Vector3I min, Vector3I max) Measure()
    {
        var halfSize = Mathf.Max(3, Mathf.Max(Breadth, (int)Mathf.Log(Height * BarkGrowth)));
        return (
            new(-halfSize, 0, -halfSize),
            new(halfSize, Height + 1, halfSize)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        var trunk = (int)Mathf.Log(Height * BarkGrowth);
        buffer.FillLine(Vector3I.Zero, Vector3I.Up * (Height - 2), trunk, 1, Blocks.Bark);

        if (Height >= 4)
        {
            var span = Mathf.Max(Breadth - 1, 3);
            buffer.FillLine(
                Vector3I.Up * (Height - 3),
                Vector3I.Up * (Height - 2) + Vector3I.Forward * (span - 1),
                1,
                1,
                Blocks.Bark
            );
            buffer.FillLine(
                Vector3I.Up * (Height - 3),
                Vector3I.Up * (Height - 2) + Vector3I.Back * (span - 1),
                1,
                1,
                Blocks.Bark
            );
            buffer.FillLine(
                Vector3I.Up * (Height - 3),
                Vector3I.Up * (Height - 2) + Vector3I.Left * (span - 1),
                1,
                1,
                Blocks.Bark
            );
            buffer.FillLine(
                Vector3I.Up * (Height - 3),
                Vector3I.Up * (Height - 2) + Vector3I.Right * (span - 1),
                1,
                1,
                Blocks.Bark
            );
        }

        if (Breadth > 0)
        {
            buffer.FillSpottyCylinder(Vector3I.Up * (Height - 1), Breadth, 1, Blocks.Leaves, Leafiness);
            // buffer.FillSpottyCylinder(Vector3I.Up * (Height - 2), Breadth, 1, Blocks.Leaves, Leafiness);
        }
    }

}
