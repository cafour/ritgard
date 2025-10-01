using Godot;
using Ritgard.Data;
using Ritgard.Mining;
using Ritgard.Structures;
using System;
using System.Collections;
using System.Linq;

namespace Ritgard;

public partial class TestStructure : Node3D, IWithVoxelLibrary
{
    private MeshInstance3D mesh;
    private StaticBody3D body;
    private static RandomNumberGenerator rng = new();

    public const int Radius = 3;
    public const int MaxConeHeight = 20;

    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public Material Material { get; set; }

    [Export]
    public Material HighlightMaterial { get; set; }

    public long? Id { get; set; }

    public Issue? Item { get; set; }

    public bool IsHighlighted { get; set; }

    public Control ControlsContainer { get; set; }

    private CheckBox isClampedCheckbox;
    private Slider clampHeightSlider;
    private Slider averageHeightSlider;
    private Slider maxBreadthSlider;

    public override void _Ready()
    {
        mesh = GetNode<MeshInstance3D>("Mesh");
        body = GetNode<StaticBody3D>("Body");
        // GenerateSphere();
        if (ControlsContainer is not null)
        {
            isClampedCheckbox = ControlsContainer.GetNode<CheckBox>("IsClamped");
            isClampedCheckbox.Toggled += _ => GenerateMesh();
            clampHeightSlider = ControlsContainer.GetNode<Slider>("ClampedHeight");
            clampHeightSlider.ValueChanged += _ => GenerateMesh();
            averageHeightSlider = ControlsContainer.GetNode<Slider>("AverageHeight");
            averageHeightSlider.ValueChanged += _ => GenerateMesh();
            maxBreadthSlider = ControlsContainer.GetNode<Slider>("MaxBreadth");
            maxBreadthSlider.ValueChanged += _ => GenerateMesh();
        }
        GenerateMesh();
    }

    private void GenerateMesh()
    {
        // IStructure structure = Item?.ResourceType switch
        // {
        //     "Text" or "MarkDown" or "HTML" => new Acacia
        //     {
        //         CrownBreadth = Item.WordLength.HasValue
        //             ? Overlord.Instance.WordLengthMapping(Item.WordLength.Value)
        //             : Overlord.WordLengthMappingMin,
        //         TrunkHeight = Item.ByteLength.HasValue
        //             ? Overlord.Instance.ByteLengthMapping(Item.ByteLength.Value)
        //             : Overlord.ByteLengthMappingMin,
        //         // Leafiness = rng.RandfRange(0.1f, 1.0f)
        //         Leafiness = 1.0f
        //     },
        //     "GitHub Resource" => new Conifer
        //     {
        //         CrownBreadth = Item.WordLength.HasValue
        //             ? Overlord.Instance.WordLengthMapping(Item.WordLength.Value)
        //             : Overlord.WordLengthMappingMin,
        //         TrunkHeight = Item.ByteLength.HasValue
        //             ? Overlord.Instance.ByteLengthMapping(Item.ByteLength.Value)
        //             : Overlord.ByteLengthMappingMin,
        //     },
        //     _ => new Broadleaf
        //     {
        //         CrownBreadth = Item.WordLength.HasValue
        //             ? Overlord.Instance.WordLengthMapping(Item.WordLength.Value)
        //             : Overlord.WordLengthMappingMin,
        //         TrunkHeight = Item.ByteLength.HasValue
        //             ? Overlord.Instance.ByteLengthMapping(Item.ByteLength.Value)
        //             : Overlord.ByteLengthMappingMin,
        //         // Leafiness = rng.RandfRange(0.1f, 1.0f)
        //         Leafiness = 1.0f
        //     }
        // };
        var contributorCount = Item.Comments
            .Select(c => c.Author)
            .Concat([Item.Author])
            .Where(a => a is not null)
            .Distinct()
            .Count();
        var issueLength = Item.GetTimeSpan();
        var coneHeight = Mathf.CeilToInt(issueLength / Overlord.Instance.AvgIssueLength * averageHeightSlider.Value);
        if (isClampedCheckbox.ButtonPressed)
        {
            coneHeight = Mathf.Clamp(coneHeight, 1, (int)clampHeightSlider.Value);
        }

        var step = coneHeight > 0 ? issueLength / coneHeight : TimeSpan.Zero;
        var layers = new BitArray(coneHeight);
        layers.Set(0, true); // it was created
        // layers.Set(coneHeight - 1, true); // it was updated or there must be a comment
        for (int i = 0; i < coneHeight; ++i)
        {
            var start = Item.CreatedAt + i * step;
            var end = Item.CreatedAt + (i + 1) * step;
            if (Item.Comments.Any(c => c.CreatedAt >= start && c.CreatedAt < end))
            {
                layers.Set(i, true);
            }
        }

        // var structure = new LayeredConifer
        // {
        //     TrunkHeight = contributorCount,
        //     Layers = layers,
        //     HasCap = Item.State == IssueState.Closed,
        //     MaxBreadth = (int)maxBreadthSlider.Value
        // };
        var structure = new Conifer
        {
            TrunkHeight = contributorCount,
            CrownBreadth = 3
        };

        var (min, max) = structure.Measure();
        var size = max - min;
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
