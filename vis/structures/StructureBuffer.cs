using System;
using Godot;

namespace Ritgard.Structures;

public sealed partial class StructureBuffer : IWithVoxelLibrary
{
    public VoxelBuffer Data { get; }
    public VoxelTool Tool { get; }
    public Vector3I Size { get; }
    public VoxelBlockyLibrary Library { get; }
    public Vector3I OriginOffset { get; }

    public StructureBuffer(Vector3I size, VoxelBlockyLibrary library, Vector3I? offset = default)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
        {
            throw new ArgumentException($"All components of size must be positive integers.", nameof(size));
        }

        Size = size;
        Library = library;
        Data = new VoxelBuffer();
        Data.Create(size.X + 2, size.Y + 2, size.Z + 2);
        Tool = Data.GetVoxelTool();
        OriginOffset = offset ?? new Vector3I(size.X / 2, 1, size.Z / 2);
    }

    public StructureBuffer SetAt(Vector3I pos, Blocks blockType)
    {
        pos += OriginOffset;
        SetRaw(pos, (ulong)blockType);
        return this;
    }

    public StructureBuffer SetRaw(Vector3I pos, ulong value)
    {
        Data.SetVoxel(value, pos.X, pos.Y, pos.Z, (uint)VoxelBuffer.ChannelId.ChannelType);
        return this;
    }

    public StructureBuffer FillSphere(Vector3I pos, int radius, Blocks blockType)
    {
        ResetTool();
        Tool.Channel = VoxelBuffer.ChannelId.ChannelType;
        Tool.Value = (ulong)blockType;
        Tool.Mode = VoxelTool.ModeEnum.Set;
        Tool.DoSphere(pos + OriginOffset, radius);
        return this;
    }

    public StructureBuffer FillSpottySphere(Vector3I pos, int radius, Blocks blockType, float spottiness)
    {
        spottiness = Mathf.Clamp(spottiness, 0.0f, 1.0f);
        if (spottiness == 0.0f)
        {
            return this;
        }
        else if (spottiness == 1.0f)
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
                        Tool.SetVoxel(pos + new Vector3I(x, y, z), (ulong)blockType);
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillLine(Vector3I from, Vector3I to, float fromRadius, float toRadius, Blocks blockType)
    {
        ResetTool();
        Tool.Channel = VoxelBuffer.ChannelId.ChannelType;
        Tool.Value = (ulong)blockType;
        Tool.Mode = VoxelTool.ModeEnum.Set;
        Span<Vector3> points = stackalloc Vector3[2];
        Span<float> radii = stackalloc float[2];
        points[0] = from + OriginOffset;
        points[1] = to + OriginOffset;
        radii[0] = fromRadius;
        radii[1] = toRadius;
        Tool.DoPath(points, radii);
        return this;
    }

    public StructureBuffer FillBresenhamLine(Vector3I from, Vector3I to, Blocks blockType)
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
            Data.SetVoxel((ulong)blockType, p.X, p.Y, p.Z, (uint)VoxelBuffer.ChannelId.ChannelType);
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
                        if (spottiness == 1.0f || spottiness < rng.Randf())
                        {
                            SetRaw(pos + new Vector3I(x, h, z), (ulong)blockType);
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
                        SetRaw(pos + new Vector3I(x, height - h - 1, z) + OriginOffset, (ulong)blockType);
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

        Data.FillArea(
            value: (ulong)blockType,
            min: min,
            max: max,
            channel: (int)VoxelBuffer.ChannelId.ChannelType
        );
        return this;
    }

    private void ResetTool()
    {
        Tool.Value = default;
        Tool.Channel = default;
        Tool.EraserValue = default;
        Tool.Mode = default;
        Tool.SdfScale = default;
        Tool.SdfStrength = default;
        Tool.TextureIndex = default;
        Tool.TextureOpacity = default;
        Tool.TextureFalloff = default;
    }
}
