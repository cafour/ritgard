using Godot;

namespace Ritgard;

public sealed partial class VoxelGenerator : VoxelGeneratorScript
{
    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    public override void _GenerateBlock(VoxelBuffer buffer, Vector3I origin, int lod)
    {
        if (lod != 0)
        {
            return;
        }

        buffer.SetVoxel((ulong)Library.GetModelIndexFromResourceName("stone"), 0, 0, 0);
        // if (origin.Y < 0)
        // {
        //     // buffer.Fill((ulong)Library.GetModelIndexFromResourceName("stone"));
        // }
    }
}
