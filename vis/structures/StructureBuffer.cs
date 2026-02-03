using System;
using Godot;
using Ritgard.Voxel;

namespace Ritgard.Structures;

public sealed class StructureBuffer : IWithVoxelLibrary
{
    public VoxelBuffer Data { get; }
    public Vector3I Size { get; }
    public VoxelBlockLibrary Library { get; }
    public Vector3I OriginOffset { get; }

    public StructureBuffer(Vector3I size, VoxelBlockLibrary library, Vector3I? offset = null)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
        {
            throw new ArgumentException($"All components of size must be positive integers.", nameof(size));
        }

        Size = size;
        Library = library;
        Data = new VoxelBuffer(size.X + 2, size.Y + 2, size.Z + 2);
        OriginOffset = offset ?? new Vector3I(size.X / 2, 1, size.Z / 2);
    }

    public StructureBuffer SetAt(Vector3I pos, Blocks blockType)
    {
        pos += OriginOffset;
        Data.SetVoxel((byte)blockType, pos);
        return this;
    }

    public StructureBuffer FillSphere(Vector3I pos, int radius, Blocks blockType)
    {
        pos += OriginOffset;
        float radiusSquared = radius * radius;

        for (int x = -radius + 1; x < radius; x++)
        {
            for (int y = -radius + 1; y < radius; y++)
            {
                for (int z = -radius + 1; z < radius; z++)
                {
                    if (x * x + y * y + z * z < radiusSquared)
                    {
                        Data.SetVoxel((byte)blockType, pos + new Vector3I(x, y, z));
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillSpottySphere(Vector3I pos, int radius, Blocks blockType, float spottiness)
    {
        spottiness = Mathf.Clamp(spottiness, 0.0f, 1.0f);
        if (spottiness == 0.0f)
        {
            return this;
        }

        if (Math.Abs(spottiness - 1.0f) < float.Epsilon)
        {
            return FillSphere(pos, radius, blockType);
        }

        var rng = new RandomNumberGenerator();

        pos += OriginOffset;

        float radiusSquared = radius * radius;

        for (int x = -radius + 1; x < radius; x++)
        {
            for (int y = -radius + 1; y < radius; y++)
            {
                for (int z = -radius + 1; z < radius; z++)
                {
                    if (x * x + y * y + z * z < radiusSquared && rng.Randf() < spottiness)
                    {
                        Data.SetVoxel((byte)blockType, pos + new Vector3I(x, y, z));
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillLine(Vector3I from, Vector3I to, Blocks blockType)
    {
        from += OriginOffset;
        to += OriginOffset;

        // deltas
        int dx = Math.Abs(from.X - to.X);
        int dy = Math.Abs(from.Y - to.Y);
        int dz = Math.Abs(from.Z - to.Z);

        // steps
        int xs = Math.Sign(to.X - from.X);
        int ys = Math.Sign(to.Y - from.Y);
        int zs = Math.Sign(to.Z - from.Z);

        Vector3I current = from;

        void Set(Vector3I p)
        {
            Data.SetVoxel((byte)blockType, p);
        }

        Set(from);

        if (dx >= dy && dx >= dz)
        {
            int p1 = 2 * dy - dx;
            int p2 = 2 * dz - dx;
            while (current.X != to.X)
            {
                current.X += xs;
                if (p1 >= 0)
                {
                    current.Y += ys;
                    p1 -= 2 * dx;
                }

                if (p2 >= 0)
                {
                    current.Z += zs;
                    p2 -= 2 * dx;
                }

                p1 += 2 * dy;
                p2 += 2 * dz;

                if (current.X >= Size.X || current.Y >= Size.Y || current.Z >= Size.Z)
                {
                    return this;
                }

                Set(current);
            }
        }
        else if (dy >= dx && dy >= dz)
        {
            int p1 = 2 * dx - dy;
            int p2 = 2 * dz - dy;
            while (current.Y != to.Y)
            {
                current.Y += ys;
                if (p1 >= 0)
                {
                    current.X += xs;
                    p1 -= 2 * dy;
                }

                if (p2 >= 0)
                {
                    current.Z += zs;
                    p2 -= 2 * dy;
                }

                p1 += 2 * dx;
                p2 += 2 * dz;

                if (current.X >= Size.X || current.Y >= Size.Y || current.Z >= Size.Z)
                {
                    return this;
                }

                Set(current);
            }
        }
        else if (dz >= dx && dz >= dy)
        {
            int p1 = 2 * dy - dz;
            int p2 = 2 * dx - dz;
            while (current.Z != to.Z)
            {
                current.Z += zs;
                if (p1 >= 0)
                {
                    current.Y += ys;
                    p1 -= 2 * dz;
                }

                if (p2 >= 0)
                {
                    current.Z += xs;
                    p2 -= 2 * dz;
                }

                p1 += 2 * dy;
                p2 += 2 * dx;

                if (current.X >= Size.X || current.Y >= Size.Y || current.Z >= Size.Z)
                {
                    return this;
                }

                Set(current);
            }
        }

        Set(to);
        return this;
    }

    public StructureBuffer FillSpottyCylinder(Vector3I pos, int radius, int height, Blocks blockType, float spottiness)
    {
        if (radius <= 0 || height <= 0)
        {
            return this;
        }

        spottiness = Mathf.Clamp(spottiness, 0.0f, 1.0f);
        if (spottiness == 0.0f)
        {
            return this;
        }

        var rng = new RandomNumberGenerator();
        pos += OriginOffset;
        var radiusSquared = radius * radius;

        for (int x = -radius + 1; x < radius; ++x)
        {
            for (int z = -radius + 1; z < radius; ++z)
            {
                if (x * x + z * z < radiusSquared)
                {
                    for (int h = 0; h < height; ++h)
                    {
                        if (Math.Abs(spottiness - 1.0f) < float.Epsilon || spottiness < rng.Randf())
                        {
                            Data.SetVoxel((byte)blockType, pos + new Vector3I(x, h, z));
                        }
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillCone(Vector3I pos, int height, int radius, Blocks blockType)
    {
        if (height == 0 || radius == 0)
        {
            return this;
        }

        for (int h = 0; h < height; ++h)
        {
            var r = radius / (float)height * (h + 1);
            var rSquared = r * r;
            for (int x = -radius + 1; x < radius; ++x)
            {
                for (int z = -radius + 1; z < radius; ++z)
                {
                    if (x * x + z * z < rSquared)
                    {
                        Data.SetVoxel((byte)blockType, pos + new Vector3I(x, height - h - 1, z) + OriginOffset);
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillArea(Vector3I min, Vector3I max, Blocks blockType)
    {
        min = (min + OriginOffset).Clamp(Vector3I.Zero, Size - new Vector3I(1, 1, 1));
        max = (max + OriginOffset).Clamp(Vector3I.Zero, Size);
        if (max.X < min.X || max.Y < min.Y || max.Z < min.Z)
        {
            return this;
        }

        for (int z = min.Z; z < max.Z; ++z)
        {
            for (int y = min.Y; y < max.Y; ++z)
            {
                for (int x = min.X; x < max.X; ++x)
                {
                    var pos = new Vector3I(x, y, z) + OriginOffset;
                    Data.SetVoxel((byte)blockType, pos);
                }
            }
        }

        return this;
    }
}
