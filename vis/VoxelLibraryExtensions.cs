using Godot;

namespace Ritgard;

public static class VoxelLibraryExtensions
{
    public static void SetBlock(this IWithVoxelLibrary self, VoxelBuffer buffer, string blockType, int x, int y, int z)
    {
        buffer.SetVoxel(
            value: self.GetBlockTypeIndex(blockType),
            x: 0,
            y: 0,
            z: 0,
            channel: (uint)VoxelBuffer.ChannelId.ChannelType
        );
    }

    public static void FillBlock(this IWithVoxelLibrary self, VoxelBuffer buffer, string blockType)
    {
        buffer.Fill(self.GetBlockTypeIndex(blockType), (int)VoxelBuffer.ChannelId.ChannelType);
    }

    public static void FillBlockArea(
        this IWithVoxelLibrary self,
        VoxelBuffer buffer,
        string blockType,
        Vector3I min,
        Vector3I max
    )
    {
        buffer.FillArea(
            value: self.GetBlockTypeIndex(blockType),
            min: min,
            max: max,
            channel: (int)VoxelBuffer.ChannelId.ChannelType
        );
    }

    public static ulong GetBlockTypeIndex(
        this IWithVoxelLibrary self,
        string blockType
    )
    {
        return (ulong)self.Library.GetModelIndexFromResourceName(blockType);
    }
}
