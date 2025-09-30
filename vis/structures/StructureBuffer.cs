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

    public StructureBuffer(Vector3I size, VoxelBlockyLibrary library)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
        {
            throw new ArgumentException($"All components of size muse be positive integers.", nameof(size));
        }

        Size = size;
        Library = library;
        Data = new VoxelBuffer();
        Data.Create(size.X + 2, size.Y + 1, size.Z + 2);
        Tool = Data.GetVoxelTool();
        OriginOffset = new Vector3I(size.X / 2, 1, size.Z / 2);
    }

    public StructureBuffer SetAt(Vector3I pos, string blockType)
    {
        pos += OriginOffset;
        SetRaw(pos, this.GetBlockTypeIndex(blockType));
        return this;
    }

    public StructureBuffer SetRaw(Vector3I pos, ulong value)
    {
        Data.SetVoxel(value, pos.X, pos.Y, pos.Z, (uint)VoxelBuffer.ChannelId.ChannelType);
        return this;
    }

    public StructureBuffer FillSphere(Vector3I pos, int radius, string blockType)
    {
        ResetTool();
        Tool.Channel = VoxelBuffer.ChannelId.ChannelType;
        Tool.Value = this.GetBlockTypeIndex(blockType);
        Tool.Mode = VoxelTool.ModeEnum.Set;
        Tool.DoSphere(pos + OriginOffset, radius);
        return this;
    }

    public StructureBuffer FillSpottySphere(Vector3I pos, int radius, string blockType, float spottiness)
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
                        Tool.SetVoxel(pos + new Vector3I(x, y, z), this.GetBlockTypeIndex(blockType));
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillLine(Vector3I from, Vector3I to, float fromRadius, float toRadius, string blockType)
    {
        ResetTool();
        Tool.Channel = VoxelBuffer.ChannelId.ChannelType;
        Tool.Value = this.GetBlockTypeIndex(blockType);
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

    public StructureBuffer FillBresenhamLine(Vector3I from, Vector3I to, string blockType)
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
        var blockValue = this.GetBlockTypeIndex(blockType);
        void Set(Vector3I p)
        {
            Data.SetVoxel(blockValue, p.X, p.Y, p.Z, (uint)VoxelBuffer.ChannelId.ChannelType);
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

    public StructureBuffer FillSpottyCylinder(Vector3I pos, int radius, int height, string blockType, float spottiness)
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
        var blockValue = this.GetBlockTypeIndex(blockType);

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
                            SetRaw(pos + new Vector3I(x, h, z), blockValue);
                        }
                    }
                }
            }
        }

        return this;
    }

    public StructureBuffer FillCone(Vector3I pos, int height, int radius, string blockType)
    {
        if (height == 0 || radius == 0)
        {
            return this;
        }

        var blockValue = this.GetBlockTypeIndex(blockType);

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
                        SetRaw(pos + new Vector3I(x, height - h - 1, z) + OriginOffset, blockValue);
                    }
                }
            }
        }

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
