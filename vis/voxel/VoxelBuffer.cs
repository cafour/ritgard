using System;
using Godot;

namespace Ritgard.Voxel;

public struct VoxelBuffer
{
    public Vector3I Size { get; }

    public VoxelBuffer(Vector3I size)
    {
        Size = size;
        RawData = new byte[Size.Z, Size.Y, Size.X];
    }

    public VoxelBuffer(int width, int height, int depth) : this(new Vector3I(width, height, depth))
    {
    }

    public byte[,,] RawData { get; }

    public void SetVoxel(byte value, int x, int y, int z)
    {
        RawData[z, y, x] = value;
    }

    public void SetVoxel(byte value, Vector3I pos)
    {
        if (pos.X < 0 || pos.Y < 0 || pos.Z < 0)
        {
            throw new ArgumentException("Only voxels at non-negative coords can be set.");
        }

        RawData[pos.Z, pos.Y, pos.Z] = value;
    }
}
