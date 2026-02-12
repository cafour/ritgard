using Godot;
using Ritgard.Data;
using Ritgard.Mining;
using Ritgard.Structures;
using System;
using System.Collections;
using System.Linq;
using Ritgard.Voxel;
using Ritgard.WorldGenerator;

namespace Ritgard;

[SceneTree("./item_structure.tscn")]
public partial class ItemStructure : Node3D, IWithVoxelLibrary
{
    public const int TrunkHeight = 2;
    public const int Radius = ActiveRepository.StructureRadius;
    public const int MaxConeHeight = 20;

    [Export]
    public VoxelBlockLibrary Library { get; set; }

    [Export]
    public Material Material { get; set; }

    [Export]
    public Material HighlightMaterial { get; set; }

    public ActiveItem Item { get; set; }

    public bool IsHighlighted { get; set; }

    public bool ShouldBeVisible { get; set; } = true;

    public override void _Ready()
    {
        Position = new Vector3(Item.Position.X, -1000, Item.Position.Y);
        VisibilityChanged += () =>
        {
            _.Body.Get().ProcessMode = Visible ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        };
    }

    public void OnShowStep(int step)
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
        // var contributorCount = Item.Comments
        //     .Select(c => c.Author)
        //     .Concat([Item.Author])
        //     .Where(a => a is not null)
        //     .Distinct()
        //     .Count();
        // var issueLength = Item.GetTimeSpan();
        // var coneHeight = Mathf.CeilToInt(issueLength / Overlord.Instance.AvgIssueLength * averageHeightSlider.Value);
        // if (isClampedCheckbox.ButtonPressed)
        // {
        //     coneHeight = Mathf.Clamp(coneHeight, 1, (int)clampHeightSlider.Value);
        // }

        // var step = coneHeight > 0 ? issueLength / coneHeight : TimeSpan.Zero;
        // var layers = new BitArray(coneHeight);
        // layers.Set(0, true); // it was created
        // // layers.Set(coneHeight - 1, true); // it was updated or there must be a comment
        // for (int i = 0; i < coneHeight; ++i)
        // {
        //     var start = Item.CreatedAt + i * step;
        //     var end = Item.CreatedAt + (i + 1) * step;
        //     if (Item.Comments.Any(c => c.CreatedAt >= start && c.CreatedAt < end))
        //     {
        //         layers.Set(i, true);
        //     }
        // }

        // var structure = new LayeredConifer
        // {
        //     TrunkHeight = contributorCount,
        //     Layers = layers,
        //     HasCap = Item.State == IssueState.Closed,
        //     MaxBreadth = (int)maxBreadthSlider.Value
        // };
        var height = Overlord.Instance.Heights[Item.Id];
        if (height <= 0 || !ShouldBeVisible)
        {
            Visible = false;
            return;
        }

        var now = Overlord.Instance.Repo.MinDate + step * Overlord.Instance.StepLength;
        IStructure structure = Overlord.Instance.ShowClosedAsStubs && Item.Conversation.IsClosed(now, Overlord.Instance.StepLength)
            ? new Stub { TrunkHeight = TrunkHeight }
            : Item.Conversation switch
            {
                Issue => new Conifer
                {
                    TrunkHeight = TrunkHeight,
                    CrownBreadth = Radius
                },
                PullRequest => new Broadleaf
                {
                    TrunkHeight = TrunkHeight,
                    CrownBreadth = Radius
                },
                Discussion => new CubeTree()
                {
                    TrunkHeight = TrunkHeight,
                    CrownBreadth = Radius
                },
                _ => throw new NotSupportedException()
            };


        var (min, max) = structure.Measure();
        var size = max - min;
        var buffer = new StructureBuffer(size, Library);
        structure.Build(buffer);
        var mesher = new VoxelMesher();
        _.Mesh.Mesh = mesher.BuildMesh(buffer.Data, Library, Material);
        _.Mesh.Position = new Vector3(-size.X / 2f + 0.5f, 0, -size.Z / 2f + 0.5f);
        Visible = true;
        Position = new Vector3(
            Position.X,
            Mathf.CeilToInt(height),
            Position.Z
        );
        // body.Position = new Vector3(0, Radius - 0.5f, 0);
    }

    public void ToggleHighlight(bool? value)
    {
        if (value is not null && value == IsHighlighted)
        {
            return;
        }

        IsHighlighted = value ?? !IsHighlighted;
        var material = _.Mesh.GetActiveMaterial(0);
        if (material is not null)
        {
            material.NextPass = IsHighlighted ? HighlightMaterial : null;
        }
    }

    // private void GenerateSphere()
    // {
    //     var buffer = new VoxelBuffer();
    //     buffer.Create(Radius * 2 + 1, Radius * 2 + 1, Radius * 2 + 1);
    //     var tool = buffer.GetVoxelTool();
    //     tool.Value = this.GetBlockTypeIndex(Blocks.Important);
    //     tool.DoSphere(new Vector3(Radius, Radius, Radius), Radius);

    //     var mesher = new VoxelMesherBlocky
    //     {
    //         Library = Library
    //     };
    //     mesh.Mesh = mesher.BuildMesh(buffer, [Material]);
    //     mesh.Position = new Vector3(-Radius + 0.5f, 0, -Radius + 0.5f);
    //     body.Position = new Vector3(0, Radius - 0.5f, 0);
    // }
}
