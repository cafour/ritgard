using Godot;
using Ritgard.Structures;
using System;

namespace Ritgard;

public partial class TestStructure : Node3D, IWithVoxelLibrary
{
    private MeshInstance3D mesh;
    private StaticBody3D body;
    private static RandomNumberGenerator rng = new();

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
        // GenerateSphere();

        var broadleaf = new Broadleaf
        {
            Breadth = rng.RandiRange(2, 5),
            Height = rng.RandiRange(5, 15),
            Leafiness = rng.Randf()
        };
        var (min, max) = broadleaf.Measure();
        // NB: + Vector3.One is the margin so that all surfaces get properly meshed.
        var size = max - min + Vector3I.One;
        var buffer = new StructureBuffer(size, Library);
        broadleaf.Build(buffer);
        var mesher = new VoxelMesherBlocky
        {
            Library = Library
        };
        mesh.Mesh = mesher.BuildMesh(buffer.Data, [Material]);
        mesh.Position = new Vector3(-size.X / 2 + 0.5f, 0, -size.Z / 2 + 0.5f);
        // body.Position = new Vector3(0, Radius - 0.5f, 0);
    }

    public void ToggleHighlight(bool? value)
    {
        if (value is not null && value == IsHighlighted)
        {
            return;
        }

        IsHighlighted = value ?? !IsHighlighted;
        var material = mesh.GetActiveMaterial(0);
        if (material is not null)
        {
            material.NextPass = IsHighlighted ? HighlightMaterial : null;
        }
    }

    private void GenerateSphere()
    {
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
}
