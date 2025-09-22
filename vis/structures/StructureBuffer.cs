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
