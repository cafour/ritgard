using System.Collections.Generic;
using Godot;

namespace Ritgard;

public sealed partial class StructureBuffer
{
    private Dictionary<Vector3I, uint> data = [];

    public Aabb BuildBoundingBox()
    {
        var box = new Aabb();
        foreach (var pos in data.Keys)
        {
            box.Expand(pos);
        }

        return box;
    }

    public (VoxelBuffer, Aabb) WriteToVoxelBuffer(VoxelBuffer buffer = null)
    {
        var box = BuildBoundingBox();
        if (buffer is null)
        {
            buffer = new();
            buffer.Create(
                Mathf.CeilToInt(box.Size.X),
                Mathf.CeilToInt(box.Size.Y),
                Mathf.CeilToInt(box.Size.Z)
            );
        }

        var offset = Utils.ToVector3I(box.Position);

        foreach (var (coords, value) in data)
        {
            buffer.SetVoxel(value, offset.X + coords.X, offset.Y + coords.Y, offset.Z + coords.Z);
        }

        return (buffer, box);
    }
}
