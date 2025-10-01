using Godot;
using Ritgard.Structures;

namespace Ritgard;

[SceneTree("./outlier_rock.tscn")]
public partial class OutlierRock : Node3D, IWithVoxelLibrary
{
    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public Material Material { get; set; }

    public int Height { get; set; }

    public int Breadth { get; set; }

    public override void _Ready()
    {
        var structure = new Rock
        {
            Height = Height,
            Breadth = Breadth,
            Pointiness = 1
        };

        var (min, max) = structure.Measure();
        var size = max - min;
        var buffer = new StructureBuffer(size, Library);
        structure.Build(buffer);
        var mesher = new VoxelMesherBlocky
        {
            Library = Library
        };
        _.Mesh.Mesh = mesher.BuildMesh(buffer.Data, [Material]);
        _.Mesh.Position = new Vector3(-size.X / 2 + 0.5f, 0, -size.Z / 2 + 0.5f);
    }
}
