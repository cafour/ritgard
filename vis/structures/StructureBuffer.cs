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
        Data.Create(size.X, size.Y, size.Z);
        Tool = Data.GetVoxelTool();
        OriginOffset = new Vector3I(size.X / 2, 0, size.Z / 2);
    }

    public StructureBuffer SetAt(Vector3I pos, string blockType)
    {
        this.SetBlock(Data, blockType, pos.X + OriginOffset.X, pos.Y + OriginOffset.Y, pos.Z + OriginOffset.Z);
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

    public StructureBuffer FillLine(Vector3I from, Vector3I to, int fromRadius, int toRadius, string blockType)
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
                            Tool.SetVoxel(pos + new Vector3I(x, h, z), this.GetBlockTypeIndex(blockType));
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

        for (int h = 0; h < height; ++h)
        {
            var r = radius / (float)height * h;
            var rSquared = r * r;
            for (int x = -radius + 1; x < radius; ++x)
            {
                for (int z = -radius + 1; z < radius; ++z)
                {
                    if (x * x + z * z < rSquared)
                    {
                        Tool.SetVoxel(
                            pos + new Vector3I(x, height - h, z) + OriginOffset,
                            this.GetBlockTypeIndex(blockType)
                        );
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
