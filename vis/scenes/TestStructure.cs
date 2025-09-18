using Godot;
using System;

namespace Ritgard;

public partial class TestStructure : Node3D, IWithVoxelLibrary
{
    private MeshInstance3D mesh;

    public const int Radius = 3;

    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public Material Material { get; set; }

    public override void _EnterTree()
    {
        mesh = GetNode<MeshInstance3D>("Mesh");
        var buffer = new VoxelBuffer();
        buffer.Create(Radius * 2 + 1, Radius * 2 + 1, Radius * 2 + 1);
        var tool = buffer.GetVoxelTool();
        tool.Value = this.GetBlockTypeIndex(Blocks.Important);
        tool.DoSphere(new Vector3(Radius, Radius, Radius), Radius);

        var mesher = new VoxelMesherBlocky
        {
            Library = Library
        };
        mesh.Mesh = mesher.BuildMesh(buffer, [Material]);
    }
}
