using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using Ritgard.Mining;
using Ritgard.Voxel;
using Ritgard.WorldGenerator;

namespace Ritgard.Structures;

[SceneTree("topic_island.tscn")]
public partial class TopicIsland : Node3D
{
    public const int MaxHeight = 70;
    public const string FallbackTopicLabel = "Unknown topic";

    public static readonly ImmutableArray<Blocks> Palette =
    [
        Blocks.Vis01,
        Blocks.Vis02,
        Blocks.Vis03,
        Blocks.Vis04,
        Blocks.Vis05,
        Blocks.Vis06,
        Blocks.Vis07,
        Blocks.Vis08,
    ];

    private ArrayMesh arrayMesh = new();
    private Vector3[] vertices = [];
    private Color islandColor;

    public Topic? Topic { get; set; }

    [Export]
    public VoxelBlockLibrary Library { get; set; } = null!;

    [Export]
    public Material Material { get; set; } = null!;

    [Export]
    public Material HighlightMaterial { get; set; } = null!;

    [Export]
    public float LabelVerticalOffset { get; set; } = 6f;

    public bool IsHighlighted { get; set; }

    public ConversationScope Scope { get; set; } = ConversationScope.All;

    public bool ShowOnlyWhenPopulated { get; set; } = false;

    public override void _EnterTree()
    {
        if (Topic is null)
        {
            throw new NullReferenceException("Topic is not set.");
        }

        var colorPalette = Palette
            .Select(b => Library.GetColor((byte)b))
            .ToImmutableArray();
        islandColor = colorPalette[Topic.Id % colorPalette.Length];
        InitializePlane();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsAction(InputActions.ToggleLabels) && @event.IsPressed())
        {
            _.Label.Visible = !_.Label.Visible;
        }
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

    public void InitializePlane()
    {
        if (Topic is null)
        {
            throw new InvalidOperationException(
                "Cannot initialize the topic island if either its Topic property is null."
            );
        }

        if (Overlord.Instance.CurrentTerrain is null)
        {
            Visible = false;
            return;
        }

        Visible = true;
        arrayMesh.ClearSurfaces();

        var heightmapList = Overlord.Instance.CurrentTerrain.IslandHeightmaps[Topic.Id];
        if (heightmapList.Length > 1)
        {
            throw new NotImplementedException("Batching is not implemented yet.");
        }

        var heightmap = heightmapList.Single();
        if (heightmap.SizeX < 1 || heightmap.SizeY < 1)
        {
            Visible = false;
            // GD.PushWarning($"Island for topic '{Topic?.Id}' has a heightmap that is too small to be turned into mesh.");
            return;
        }

        var hh = heightmap.SizeY;
        var hw = heightmap.SizeX;
        vertices = new Vector3[hh * hw];
        var indices = new int[(hh - 1) * (hw - 1) * 3 * 2];
        // x goes right, y is actually z and goes "down", towards the camera
        for (int y = 0; y < hh; ++y)
        {
            for (int x = 0; x < hw; ++x)
            {
                var baseIndex = y * hw + x;
                vertices[baseIndex] = new Vector3(x, 0, y);

                if (x < hw - 1 && y < hh - 1)
                {
                    var baseTriIndex = 6 * (y * (hw - 1) + x);
                    if (baseIndex % 2 == 0)
                    {
                        indices[baseTriIndex + 0] = baseIndex;
                        indices[baseTriIndex + 1] = baseIndex + 1;
                        indices[baseTriIndex + 2] = baseIndex + hw;

                        indices[baseTriIndex + 3] = baseIndex + hw;
                        indices[baseTriIndex + 4] = baseIndex + 1;
                        indices[baseTriIndex + 5] = baseIndex + hw + 1;
                    }
                    else
                    {
                        indices[baseTriIndex + 0] = baseIndex;
                        indices[baseTriIndex + 1] = baseIndex + hw + 1;
                        indices[baseTriIndex + 2] = baseIndex + hw;

                        indices[baseTriIndex + 3] = baseIndex + hw + 1;
                        indices[baseTriIndex + 4] = baseIndex;
                        indices[baseTriIndex + 5] = baseIndex + 1;
                    }
                }
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        arrayMesh.CustomAabb = new Aabb(
            position: new Vector3(
                0,
                0,
                0
            ),
            size: new Vector3(
                heightmap.SizeX,
                MaxHeight,
                heightmap.SizeY
            )
        );

        _.Mesh.Mesh = arrayMesh;
        _.Mesh.Position = new Vector3(heightmap.PositionX, -0.01f, heightmap.PositionY);
        var material = Material.Duplicate();
        if (material is ShaderMaterial shaderMaterial)
        {
            shaderMaterial.SetShaderParameter("color", islandColor);
        }

        _.Mesh.SetSurfaceOverrideMaterial(0, material as Material);
        _.Body.Get().Position = _.Mesh.Position;
        _.Label.Text = Topic?.GetPreferredTitle() ?? FallbackTopicLabel;
        _.Label.Position = _.Mesh.Position + new Vector3(
            heightmap.SizeX / 2f,
            LabelVerticalOffset,
            heightmap.SizeY / 2f
        );
    }

    public void UpdatePlane(int step, float scale = 1.0f)
    {
        if (Topic is null)
        {
            throw new InvalidOperationException(
                "Cannot update the island terrain if this island has no Topic."
            );
        }

        if (Overlord.Instance.CurrentTerrain is null)
        {
            Visible = false;
            return;
        }

        Visible = true;
        var heightmapList = Overlord.Instance.CurrentTerrain.IslandHeightmaps[Topic.Id];
        if (heightmapList.Length > 1)
        {
            throw new NotImplementedException("Batching is not implemented yet.");
        }

        var heightmap = heightmapList.Single();
        var hh = heightmap.SizeY;
        var hw = heightmap.SizeX;

        if (hh < 1 || hw < 1)
        {
            Visible = false;
            return;
        }

        for (int y = 0; y < hh; ++y)
        {
            for (int x = 0; x < hw; ++x)
            {
                float height = heightmap.GetHeight(x, y, step);
                if (height > 0)
                {
                    height *= scale;
                }
                vertices[y * hw + x].Y = height;
            }
        }

        var bytes = MemoryMarshal.AsBytes(vertices.AsSpan());
        arrayMesh.SurfaceUpdateVertexRegion(0, 0, bytes);

        var maxHeight = heightmap.GetMaxHeight(step);
        var isCompletelySubmerged = maxHeight < 0;

        if (ShowOnlyWhenPopulated && isCompletelySubmerged)
        {
            Visible = false;
            return;
        }

        if (_.Body.Get() is not null)
        {
            Visible = true;
        }

        if (_.Body.Collider is not null)
        {
            if (Overlord.Instance.Repo?.Mining.Repository.Name == "dotvvm")
            {
                var shape = new ConvexPolygonShape3D();
                shape.SetPoints(vertices);
                _.Body.Collider.Shape = shape;
            }
            else
            {
                var shape = new ConcavePolygonShape3D();
                shape.SetFaces(arrayMesh.GetFaces());
                _.Body.Collider.Shape = shape;
            }
        }

        _.Label.Position = _.Label.Position with { Y = (isCompletelySubmerged ? 0 : LabelVerticalOffset) + maxHeight };
    }

    public static byte ToByteHeight(float height)
    {
        // NB: height 0 and 1 have special meanings; 0 is invisible deep sea; 1 is shallow sea
        var intHeight = height < 0f ? 0
            : height == 0 ? 1
            : Mathf.CeilToInt(height) + 2;
        return (byte)Math.Clamp(intHeight, 0, 255);
    }

    private static float ToFloatHeight(byte height)
    {
        return height switch
        {
            0 => -10f,
            1 => -1f,
            _ => height - 2
        };
    }
}
