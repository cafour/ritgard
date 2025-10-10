using System;
using Godot;

namespace Ritgard.Structures;

public sealed class Rock : IStructure
{
    public int Height { get; set; }

    public int Breadth { get; set; }

    public float Pointiness { get; set; } = 0.75f;

    public (Vector3I min, Vector3I max) Measure()
    {
        return (
            new Vector3I(-Breadth, 0, -Breadth),
            new Vector3I(Breadth + 1, Height + 1, Breadth + 1)
        );
    }

    public void Build(StructureBuffer buffer)
    {
        for (int z = -Breadth; z < Breadth; ++z)
        {
            for (int x = -Breadth; x < Breadth; ++x)
            {
                var dist = Mathf.Sqrt(x * x + z * z);
                if (dist < Breadth)
                {
                    var height = Mathf.RoundToInt(Pointiness * Height / (dist + Pointiness));
                    buffer.FillArea(
                        new Vector3I(x, 0, z),
                        new Vector3I(x + 1, height, z + 1),
                        Blocks.Stone
                    );
                }
            }
        }
        buffer.FillArea(
            new Vector3I(0, 0, 0),
            new Vector3I(1, Height + 1, 1),
            Blocks.Stone
        );
    }

}
