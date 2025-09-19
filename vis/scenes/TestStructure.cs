using Godot;
using System;

namespace Ritgard;

public partial class TestStructure : Node3D, IWithVoxelLibrary
{
    private MeshInstance3D mesh;
    private StaticBody3D body;

    public const int Radius = 3;

    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public Material Material { get; set; }

    [Export]
    public Material HighlightMaterial { get; set; }

    public (Guid id, string absoluteLink)? Identifier { get; set; }

    public bool IsHighlighted { get; set; }

    public override void _EnterTree()
    {
        mesh = GetNode<MeshInstance3D>("Mesh");
        body = GetNode<StaticBody3D>("Body");
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
        mesh.Position = new Vector3(-Radius + 0.5f, 0, -Radius + 0.5f);
        body.Position = new Vector3(0, Radius - 0.5f, 0);
    }

    public void ToggleHighlight(bool? value)
    {
        if (value is not null && value == IsHighlighted)
        {
            return;
        }

        IsHighlighted = value ?? !IsHighlighted;
        mesh.GetActiveMaterial(0).NextPass = IsHighlighted ? HighlightMaterial : null;
    }
}
