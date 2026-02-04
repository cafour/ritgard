using Godot;

namespace Ritgard.Voxel;

[GlobalClass]
public partial class VoxelBlockType : Resource
{
    [Export]
    public Color Color { get; set; } = Color.Color8(0xff, 0x00, 0x00);

    [Export]
    public Material? Material { get; set; }

    public string? Name => ResourceName;
}
