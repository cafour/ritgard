using System;
using System.Runtime.CompilerServices;
using Godot;

namespace Ritgard.Voxel;

public readonly struct VoxelBuffer
{
    public const byte NoneValue = 0b00;

    public Vector3I Size { get; }

    public uint Width { get; }
    public uint Height { get; }
    public uint Depth { get; }

    public VoxelBuffer(Vector3I size)
    {
        if (size.X < 0 || size.Y < 0 || size.Z < 0)
        {
            throw new ArgumentException("Voxel buffer size must be non-negative.");
        }

        Size = size;
        Width = (uint)size.X;
        Height = (uint)size.Y;
        Depth = (uint)size.Z;
        RawData = new byte[Width * Height * Depth];
    }

    public VoxelBuffer(uint width, uint height, uint depth)
    {
        Width = width;
        Height = height;
        Depth = depth;
        Size = new Vector3I((int)width, (int)height, (int)depth);
        RawData = new byte[width * height * depth];
    }

    public byte[] RawData { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(byte value, uint x, uint y, uint z)
    {
        RawData[GetIndex(x, y, z)] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(byte value, Vector3I pos)
    {
        RawData[GetIndex(pos.X, pos.Y, pos.Z)] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetVoxel(Vector3I pos)
    {
        return RawData[GetIndex(pos.X, pos.Y, pos.Z)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetVoxel(uint x, uint y, uint z)
    {
        return RawData[GetIndex(x, y, z)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetIndex(int x, int y, int z)
    {
        AssertRange(x, y, z);
        return (uint)y + Height * ((uint)x + Width * (uint)z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetIndex(uint x, uint y, uint z)
    {
        AssertRange(x, y, z);
        return y + Height * (x + Width * z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssertRange(int x, int y, int z)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "X may not be negative in a voxel buffer");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Y may not be negative in a voxel buffer");
        }

        if (z < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(z), "Z may not be negative in a voxel buffer");
        }

        AssertRange((uint)x, (uint)y, (uint)z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssertRange(uint x, uint y, uint z)
    {
        if (x >= Size.X)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"X={x} is out of range of the buffer with X size {Size.X}."
            );
        }

        if (y >= Size.Y)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                $"Y={y} is out of range of the buffer with Y size {Size.Y}."
            );
        }

        if (z >= Size.Z)
        {
            throw new ArgumentOutOfRangeException(
                nameof(z),
                $"Z={z} is out of range of the buffer with Z size {Size.Z}."
            );
        }
    }
}
