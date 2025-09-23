using Godot;
using Ritgard.Data;
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
    
    public (Guid id, string? absoluteLink)? Identifier { get; set; }

    public DocumentationItem? Item { get; set; }

    public bool IsHighlighted { get; set; }

    public override void _Ready()
    {
        mesh = GetNode<MeshInstance3D>("Mesh");
        body = GetNode<StaticBody3D>("Body");
        // GenerateSphere();

        IStructure structure = Item?.ResourceType switch
        {
            "Text" or "MarkDown" or "HTML" => new Acacia
            {
                Breadth = Item.WordLength.HasValue
                    ? Overlord.Instance.WordLengthMapping(Item.WordLength.Value)
                    : Overlord.WordLengthMappingMin,
                Height = Item.ByteLength.HasValue
                    ? Overlord.Instance.ByteLengthMapping(Item.ByteLength.Value)
                    : Overlord.ByteLengthMappingMin,
                // Leafiness = rng.RandfRange(0.1f, 1.0f)
                Leafiness = 1.0f
            },
            "GitHub Resource" => new Confifer
            {
                Breadth = Item.WordLength.HasValue
                    ? Overlord.Instance.WordLengthMapping(Item.WordLength.Value)
                    : Overlord.WordLengthMappingMin,
                Height = Item.ByteLength.HasValue
                    ? Overlord.Instance.ByteLengthMapping(Item.ByteLength.Value)
                    : Overlord.ByteLengthMappingMin,
            },
            _ => new Broadleaf
            {
                CrownBreadth = Item.WordLength.HasValue
                    ? Overlord.Instance.WordLengthMapping(Item.WordLength.Value)
                    : Overlord.WordLengthMappingMin,
                TrunkHeight = Item.ByteLength.HasValue
                    ? Overlord.Instance.ByteLengthMapping(Item.ByteLength.Value)
                    : Overlord.ByteLengthMappingMin,
                // Leafiness = rng.RandfRange(0.1f, 1.0f)
                Leafiness = 1.0f
            }
        };
        
        var (min, max) = structure.Measure();
        // NB: + Vector3.One is the margin so that all surfaces get properly meshed.
        var size = max - min + Vector3I.One;
        var buffer = new StructureBuffer(size, Library);
        structure.Build(buffer);
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
