using System;
using Godot;

namespace Ritgard;

public sealed partial class VoxelGenerator : VoxelGeneratorScript
{
    public const int GrassWidth = 1;
    public const int DirtWidth = 3;

    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public Noise Noise { get; set; }

    public override void _GenerateBlock(VoxelBuffer buffer, Vector3I origin, int lod)
    {
        // if (origin.Y < 0)
        // {
        //     // buffer.Fill((ulong)Library.GetModelIndexFromResourceName("stone"));
        // }

        int GetHeight(int gx, int gz)
        {
            return (int)Mathf.Round((Noise.GetNoise2D(gx, gz) + 1.0f) * 0.5f * 100f);
        }

        var chunkSize = buffer.GetSize();
        for (int x = 0; x < chunkSize.X; ++x)
        {
            var gx = origin.X + x;
            for (int z = 0; z < chunkSize.Z; ++z)
            {
                var gz = origin.Z + z;
                var height = GetHeight(gx, gz);
                var relativeHeight = height - origin.Y;
                if (relativeHeight < 0)
                {
                    FillBlockArea(buffer, Blocks.Air, new(x, 0, z), new(x + 1, chunkSize.Y, z + 1));
                }
                else if (relativeHeight > chunkSize.Y + GrassWidth + DirtWidth)
                {
                    FillBlockArea(buffer, Blocks.Stone, new(x, 0, z), new(x + 1, chunkSize.Y, z + 1));
                }
                else
                {
                    FillBlockArea(
                        buffer,
                        Blocks.Stone,
                        new(x, 0, z),
                        new(x + 1, relativeHeight - DirtWidth - GrassWidth, z + 1)
                    );
                    FillBlockArea(
                        buffer,
                        Blocks.Dirt,
                        new(x, relativeHeight - DirtWidth - GrassWidth, z),
                        new(x + 1, relativeHeight - GrassWidth, z + 1)
                    );
                    FillBlockArea(
                        buffer,
                        Blocks.Grass,
                        new(x, relativeHeight - GrassWidth, z),
                        new(x + 1, relativeHeight, z + 1)
                    );
                }
            }
        }

        var rng = new RandomNumberGenerator();
        var structCount = rng.RandiRange(0, 1);
        var voxelTool = buffer.GetVoxelTool();
        for (int i = 0; i < structCount; ++i)
        {
            var position = new Vector3I(rng.RandiRange(0, chunkSize.X - 1), 0, rng.RandiRange(0, chunkSize.Z - 1));
            var height = GetHeight(origin.X + position.X, origin.Z + position.Z);
            if (height > origin.Y + chunkSize.Y || height < origin.Y)
            {
                continue;
            }

            position.Y = height - origin.Y;

            voxelTool.Value = (ulong)Library.GetModelIndexFromResourceName(Blocks.Important);
            voxelTool.DoSphere(position, 3);
        }
    }

    private void SetBlock(VoxelBuffer buffer, string blockType, int x, int y, int z)
    {
        buffer.SetVoxel(
            value: (ulong)Library.GetModelIndexFromResourceName(blockType),
            x: 0,
            y: 0,
            z: 0,
            channel: (uint)VoxelBuffer.ChannelId.ChannelType
        );
    }

    private void FillBlock(VoxelBuffer buffer, string blockType)
    {
        buffer.Fill((ulong)Library.GetModelIndexFromResourceName(blockType), (int)VoxelBuffer.ChannelId.ChannelType);
    }

    private void FillBlockArea(VoxelBuffer buffer, string blockType, Vector3I min, Vector3I max)
    {
        buffer.FillArea(
            value: (ulong)Library.GetModelIndexFromResourceName(blockType),
            min: min,
            max: max,
            channel: (int)VoxelBuffer.ChannelId.ChannelType
        );
    }

    public static class Blocks
    {
        public const string Air = "air";
        public const string Stone = "stone";
        public const string Grass = "grass";
        public const string Dirt = "dirt";
        public const string Important = "important";
    }
}
