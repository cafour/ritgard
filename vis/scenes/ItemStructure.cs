using Godot;
using Ritgard.Data;
using Ritgard.Mining;
using Ritgard.Structures;
using System;
using System.Collections;
using System.Collections.Generic;
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

    public static readonly Conifer IssueStructure = new Conifer
    {
        TrunkHeight = TrunkHeight,
        CrownBreadth = Radius
    };

    public static Mesh? IssueMesh { get; private set; }
    public static ConcavePolygonShape3D? IssueCollider { get; private set; }
    public static Vector3? IssueOffset { get; private set; }

    public static readonly Broadleaf PrStructure = new Broadleaf
    {
        TrunkHeight = TrunkHeight,
        CrownBreadth = Radius
    };
    public static Mesh? PrMesh { get; private set; }
    public static ConcavePolygonShape3D? PrCollider { get; private set; }
    public static Vector3? PrOffset { get; private set; }

    public static readonly CubeTree DiscussionStructure = new CubeTree()
    {
        TrunkHeight = TrunkHeight,
        CrownBreadth = Radius
    };
    public static Mesh? DiscussionMesh { get; private set; }
    public static ConcavePolygonShape3D? DiscussionCollider { get; private set; }
    public static Vector3? DiscussionOffset { get; private set; }

    public static readonly Stub ClosedStructure = new Stub { TrunkHeight = TrunkHeight };
    public static Mesh? ClosedMesh { get; private set; }
    public static ConcavePolygonShape3D? ClosedCollider { get; private set; }
    public static Vector3? ClosedOffset { get; private set; }

    [Export]
    public VoxelBlockLibrary Library { get; set; } = null!;

    [Export]
    public Material Material { get; set; } = null!;

    [Export]
    public Material HighlightMaterial { get; set; } = null!;

    public ActiveItem Item { get; set; } = null!;

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
        var height = Overlord.Instance.Heights[Item.Id] * Overlord.Instance.CurrentHeightScale;
        if (height <= 0 || !ShouldBeVisible)
        {
            Visible = false;
            return;
        }

        EnsureMeshes();
        var (mesh, collider, offset) = Overlord.Instance.ShowClosedAsStubs
            && Item.Conversation.IsClosed(Overlord.Instance.Now, Overlord.Instance.SingleStepLength)
                ? (ClosedMesh, ClosedCollider, ClosedOffset)
                : Item.Conversation switch
                {
                    Issue => (IssueMesh, IssueCollider, IssueOffset),
                    PullRequest => (PrMesh, PrCollider, PrOffset),
                    Discussion => (DiscussionMesh, DiscussionCollider, DiscussionOffset),
                    _ => throw new NotSupportedException()
                };

        _.Mesh.Mesh = (Mesh)mesh!.Duplicate(true);
        _.Mesh.Position = offset!.Value;
        _.Body.CollisionShape3D.Shape = collider;
        _.Body.CollisionShape3D.Position = offset!.Value;
        Visible = true;
        Position = new Vector3(
            Position.X,
            Mathf.RoundToInt(height),
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

    private void EnsureMeshes()
    {
        if (IssueMesh is null)
        {
            (IssueMesh, IssueCollider, IssueOffset) = GetMesh(IssueStructure);
        }
        if (PrMesh is null)
        {
            (PrMesh, PrCollider, PrOffset) = GetMesh(PrStructure);
        }
        if (DiscussionMesh is null)
        {
            (DiscussionMesh, DiscussionCollider, DiscussionOffset) = GetMesh(DiscussionStructure);
        }
        if (ClosedMesh is null)
        {
            (ClosedMesh, ClosedCollider, ClosedOffset) = GetMesh(ClosedStructure);
        }
    }

    private (Mesh, ConcavePolygonShape3D, Vector3) GetMesh(IStructure structure)
    {
        var (min, max) = structure.Measure();
        var size = max - min;
        var buffer = new StructureBuffer(size, Library);
        structure.Build(buffer);
        var mesher = new VoxelMesher();
        var mesh = (ArrayMesh)mesher.BuildMesh(buffer.Data, Library, Material);
        var vertices = (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex];
        var collider = new ConcavePolygonShape3D();
        collider.SetFaces(mesh.GetFaces());
        return (mesh, collider, new Vector3(-buffer.OriginOffset.X + 0.5f, 0, -buffer.OriginOffset.Z + 0.5f));
    }
}
