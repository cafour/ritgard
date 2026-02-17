using Godot;
using Ritgard.Structures;
using Ritgard.Voxel;
using Ritgard.WorldGenerator;

namespace Ritgard;

[SceneTree("./outlier_rock.tscn")]
public partial class OutlierRock : Node3D, IWithVoxelLibrary
{
    [Export]
    public required VoxelBlockLibrary Library { get; set; }

    [Export]
    public required Material Material { get; set; }

    public required ActiveItem Item { get; set; }

    public int Height { get; private set; }

    public override void _Ready()
    {
        Position = new Vector3(Item.Position.X, 0, Item.Position.Y);
    }

    public void OnShowStep(int step)
    {
        var height = Mathf.RoundToInt(Overlord.Instance.Heights[Item.Id] * Overlord.Instance.CurrentHeightScale);
        if (height == Height)
        {
            return;
        }

        Height = height;

        if (Height == 0)
        {
            _.Mesh.Visible = false;
            return;
        }

        _.Mesh.Visible = true;

        var structure = new Rock
        {
            Height = Height,
            Breadth = 3,
            Pointiness = 1
        };

        var (min, max) = structure.Measure();
        var size = max - min;
        var buffer = new StructureBuffer(size, Library);
        structure.Build(buffer);
        var mesher = new VoxelMesher();
        _.Mesh.Mesh = mesher.BuildMesh(buffer.Data, Library, Material);
        var meshPosition = new Vector3(-buffer.OriginOffset.X + 0.5f, 0, -buffer.OriginOffset.Z + 0.5f);
        _.Mesh.Position = meshPosition;
    }
}
