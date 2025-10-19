using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.HPRtree;
using NetTopologySuite.Index.KdTree;
using NetTopologySuite.Triangulate.Tri;
using Ritgard.Mining;

namespace Ritgard.Structures;

[SceneTree("topic_island.tscn")]
public partial class TopicIsland : Node3D
{
    public const int GrassWidth = 1;
    public const int DirtWidth = 3;
    public const int StructureRadius = 3;
    public const int HeightmapPadding = 8;
    public const int MaxHeight = 70;
    public const int SmoothRadius = 10;
    public const int StructureSafetyRange = 1;

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

    private static readonly float[] BlurKernel = GaussianBlur.CreateKernel(2f, StructureRadius - 1);

    private ImmutableHashSet<string> itemIds;
    private ImmutableArray<Vector2> itemPoints;
    private Rect2I heightmapBox;
    private ArrayMesh arrayMesh = new();
    private Vector3[] vertices;
    private Color islandColor;
    private float[,] blurTemp;

    public Topic Topic { get; set; }

    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public VoxelMesherBlocky Mesher { get; set; }

    [Export]
    public Material Material { get; set; }

    [Export]
    public Material HighlightMaterial { get; set; }

    public bool IsHighlighted { get; set; }

    public byte[,] Heightmap { get; set; }


    public override void _EnterTree()
    {
        if (Topic is null)
        {
            throw new NullReferenceException("Topic is not set.");
        }

        itemIds =
        [
            .. Overlord.Instance.Repo.TopicModelling.Items
                .Where(i => i.Value.TopicId == Topic.Id)
                .Select(i => i.Value.Id)
        ];
        itemPoints =
        [
            ..itemIds.Select(i => new Vector2(
                    x: Overlord.Instance.Repo.Items[i].Position.X,
                    y: Overlord.Instance.Repo.Items[i].Position.Y
                )
            )
        ];
        var colorPalette = Palette
            .Select(b => ((VoxelBlockyModelCube)Library.GetModel((uint)b)).Color)
            .ToImmutableArray();
        islandColor = colorPalette[Topic.Id % colorPalette.Length];

        InitializeHeightmap();
        InitializePlane();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsAction(InputActions.ToggleLabels) && @event.IsPressed())
        {
            _.Label.Visible = !_.Label.Visible;
        }
    }


    public void OnShowStep(int step)
    {
        ComputeLeveledHeightmap();
        // ComputeSmoothHeightmap();
        // ComputeBlockyMesh();
        UpdatePlane();
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

    private void ComputeLeveledHeightmap()
    {
        var coords = itemPoints.Select(v => new Coordinate(v.X, v.Y)).ToArray();
        var pointCloud = Geometry.DefaultFactory.CreateMultiPointFromCoords(coords);
        var hull = new ConcaveHull(pointCloud)
        {
            MaximumEdgeLengthRatio = 0.5,
            HolesAllowed = true
        };
        var hullTris = hull.GetHullTris();
        var triTree = new KdTree<Tri>();
        for (int i = hullTris.Count - 1; i >= 0; --i)
        {
            var tri = hullTris[i];
            var triPolygon = tri.ToPolygon(GeometryFactory.Default);
            var intrudingPoints = Overlord.Instance.Repo.ItemTree.Query(triPolygon.EnvelopeInternal)
                .Where(n => Overlord.Instance.Repo.TopicModelling.Items[n.Data.Id].TopicId != Topic.Id)
                .Where(n => triPolygon.Contains(GeometryFactory.Default.CreatePoint(
                            new Coordinate(n.Data.Position.X, n.Data.Position.Y)
                        )
                    )
                )
                .ToImmutableArray();
            if (intrudingPoints.Length > 0)
            {
                tri.Remove();
                hullTris.RemoveAt(i);
            }
            else
            {
                var triCentroid = tri.GetCentroid();
                triTree.Insert(new Coordinate(triCentroid.X, triCentroid.Y), tri);
            }
        }

        if (hullTris.Count == 0)
        {
            throw new InvalidOperationException(
                "Removed too many triangles that contained a point from another topic."
            );
        }

        var hullPolygon = hull.GetHull(hullTris);
        hullPolygon = hullPolygon.Buffer(Mathf.Min(1, Mathf.RoundToInt(StructureRadius / 2f)));

        var kdTree = new KdTree<ActiveItem>();
        foreach (var (id, pos) in itemIds.Zip(itemPoints))
        {
            kdTree.Insert(new Coordinate(pos.X, pos.Y), Overlord.Instance.Repo.Items[id]);
        }

        for (int z = 0; z < Heightmap.GetLength(0); ++z)
        {
            for (int x = 0; x < Heightmap.GetLength(1); ++x)
            {
                var px = x + heightmapBox.Position.X;
                var py = z + heightmapBox.Position.Y;

                var coord = new Coordinate(px, py);

                if (hullPolygon.Contains(GeometryFactory.Default.CreatePoint(coord)))
                {
                    Heightmap[z, x] = 1;
                }

                var nearestTri = triTree.NearestNeighbor(coord);
                var (containingTri, (alpha, beta, gamma)) = Utils.LocateTriangle(nearestTri.Data, new Vector2(px, py));
                if (containingTri is not null)
                {
                    var v0Item = kdTree.NearestNeighbor(containingTri.GetCoordinate(0));
                    var v1Item = kdTree.NearestNeighbor(containingTri.GetCoordinate(1));
                    var v2Item = kdTree.NearestNeighbor(containingTri.GetCoordinate(2));
                    if (v0Item is null
                        || v1Item is null
                        || v2Item is null
                        || v0Item == v1Item
                        || v0Item == v2Item
                        || v1Item == v2Item
                       )
                    {
                        GD.PushWarning("Couldn't re-determine which item is at a coordinate. :(");
                        continue;
                    }

                    var v0Height = Overlord.Instance.Heights[v0Item.Data.Id];
                    var v1Height = Overlord.Instance.Heights[v1Item.Data.Id];
                    var v2Height = Overlord.Instance.Heights[v2Item.Data.Id];

                    var height = v0Height * Mathf.SmoothStep(0, 1, alpha)
                                 + v1Height * Mathf.SmoothStep(0, 1, beta)
                                 + v2Height * Mathf.SmoothStep(0, 1, gamma);
                    Heightmap[z, x] = ToByteHeight(height);
                }

                var nearestPoint = kdTree.NearestNeighbor(coord);
                if (nearestPoint is not null)
                {
                    var nearestIssue = nearestPoint.Data;
                    var nearestPointHeight = Overlord.Instance.Heights[nearestIssue.Id];
                    var distance = nearestPoint.Coordinate.Distance(coord);
                    if (distance < StructureRadius)
                    {
                        Heightmap[z, x] = ToByteHeight(nearestPointHeight);
                    }

                    if (!hullPolygon.Contains(GeometryFactory.Default.CreatePoint(nearestPoint.Coordinate))
                        && distance < StructureRadius * 1.5f)
                    {
                        Heightmap[z, x] = 1;
                    }
                }
            }
        }

        GaussianBlur.Blur(Heightmap, BlurKernel, blurTemp);

        foreach (var (id, point) in itemIds.Zip(itemPoints))
        {
            var height = ToByteHeight(Overlord.Instance.Heights[id]);
            var px = Mathf.RoundToInt(point.X) - heightmapBox.Position.X;
            var py = Mathf.RoundToInt(point.Y) - heightmapBox.Position.Y;
            for (int y = -StructureSafetyRange; y <= StructureSafetyRange; ++y)
            {
                for (int x = -StructureSafetyRange; x <= StructureSafetyRange; ++x)
                {
                    Heightmap[py + y, px + x] = height;
                }
            }
        }
    }

    private void ComputeSmoothHeightmap()
    {
        ClearHeightmap();
        foreach (var id in itemIds)
        {
            var point = new Vector3(
                x: Overlord.Instance.Repo.Items[id].Position.X,
                y: Overlord.Instance.Heights[id],
                z: Overlord.Instance.Repo.Items[id].Position.Y
            );
            var radius = SmoothRadius;
            for (int z = -radius; z < radius; ++z)
            {
                var hz = Mathf.RoundToInt(point.Z - heightmapBox.Position.Y + z);
                if (hz < 0 || hz >= Heightmap.GetLength(0))
                {
                    continue;
                }

                for (int x = -radius; x < radius; ++x)
                {
                    var hx = Mathf.RoundToInt(point.X - heightmapBox.Position.X + x);
                    if (hx < 0 || hx >= Heightmap.GetLength(1))
                    {
                        continue;
                    }

                    var distSquared = x * x + z * z;
                    // var height = point.Y * Mathf.Exp(-distSquared / (float)(SmoothRadius * SmoothRadius));
                    var desiredHeight = point.Y;
                    var height = desiredHeight * Mathf.Exp(-Mathf.Log(desiredHeight) / (radius * radius) * distSquared);
                    var byteHeight = (byte)Math.Clamp(Mathf.RoundToInt(height), 0, 255);
                    // Heightmap[hz, hx] = Math.Max(Heightmap[hz, hx], byteHeight);
                    Heightmap[hz, hx] += byteHeight;
                }
            }
        }
    }

    private void InitializeHeightmap()
    {
        var min = new Vector2I(
            x: Mathf.FloorToInt(itemPoints.Min(i => i.X)) - HeightmapPadding,
            y: Mathf.FloorToInt(itemPoints.Min(i => i.Y)) - HeightmapPadding
        );
        var max = new Vector2I(
            x: Mathf.CeilToInt(itemPoints.Max(i => i.X)) + HeightmapPadding,
            y: Mathf.CeilToInt(itemPoints.Max(i => i.Y)) + HeightmapPadding
        );
        heightmapBox = new Rect2I(min, max - min);
        Heightmap = new byte[heightmapBox.Size.Y, heightmapBox.Size.X];
        blurTemp = new float[heightmapBox.Size.Y, heightmapBox.Size.X];
    }

    private void ClearHeightmap()
    {
        var height = Heightmap.GetLength(0);
        var width = Heightmap.GetLength(1);
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                Heightmap[y, x] = 0;
            }
        }
    }

    private void InitializePlane()
    {
        if (Heightmap.GetLength(0) < 1 || Heightmap.GetLength(1) < 1)
        {
            GD.PushWarning($"Island for topic {Topic.Id} has a heightmap that is too small to be turned into mesh.");
            return;
        }

        var hh = Heightmap.GetLength(0);
        var hw = Heightmap.GetLength(1);
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
                heightmapBox.Size.X,
                MaxHeight,
                heightmapBox.Size.Y
            )
        );

        _.Mesh.Mesh = arrayMesh;
        _.Mesh.Position = new Vector3(heightmapBox.Position.X, -0.01f, heightmapBox.Position.Y);
        var material = Material.Duplicate();
        if (material is ShaderMaterial shaderMaterial)
        {
            shaderMaterial.SetShaderParameter("color", islandColor);
        }

        _.Mesh.SetSurfaceOverrideMaterial(0, material as Material);
        _.Body.Get().Position = _.Mesh.Position;
        _.Label.Text = Topic.GetPreferredTitle();
        var halfSize = (Vector2)heightmapBox.Size * 0.5f;
        _.Label.Position = _.Mesh.Position + new Vector3(halfSize.X, 2, halfSize.Y);
    }

    private void UpdatePlane()
    {
        var hh = Heightmap.GetLength(0);
        var hw = Heightmap.GetLength(1);
        for (int y = 0; y < hh; ++y)
        {
            for (int x = 0; x < hw; ++x)
            {
                var height = Heightmap[y, x];
                vertices[y * hw + x].Y = ToFloatHeight(height);
            }
        }

        var bytes = MemoryMarshal.AsBytes(vertices.AsSpan());
        arrayMesh.SurfaceUpdateVertexRegion(0, 0, bytes);

        var shape = new ConcavePolygonShape3D();
        shape.SetFaces(arrayMesh.GetFaces());
        _.Body.Collider.Shape = shape;
    }

    private void ComputeBlockyMesh()
    {
        var maxHeight = Mathf.RoundToInt(itemIds.Max(i => Overlord.Instance.Heights[i]));
        var intSize = new Vector3I(heightmapBox.Size.X, maxHeight, heightmapBox.Size.Y);
        if (intSize.Y == 0)
        {
            _.Mesh.Visible = false;
            return;
        }

        _.Mesh.Visible = true;

        var buffer = new StructureBuffer(
            size: intSize,
            library: Library,
            offset: new Vector3I(1, 1, 1)
        );

        for (int y = 0; y < Heightmap.GetLength(0); ++y)
        {
            for (int x = 0; x < Heightmap.GetLength(1); ++x)
            {
                var height = Heightmap[y, x];
                if (height == 0)
                {
                    continue;
                }

                buffer.FillArea(
                    new(x, 0, y),
                    new(x + 1, height - DirtWidth - GrassWidth, y + 1),
                    Blocks.Stone
                );
                buffer.FillArea(
                    new(x, height - DirtWidth - GrassWidth, y),
                    new(x + 1, height - GrassWidth, y + 1),
                    Blocks.Dirt
                );
                buffer.FillArea(
                    new(x, height - GrassWidth, y),
                    new(x + 1, height, y + 1),
                    Palette[Topic.Id % Palette.Length]
                );
            }
        }

        var shape = new ConcavePolygonShape3D();
        var mesher = new VoxelMesherBlocky();
        mesher.Library = Library;
        var mesh = mesher.BuildMesh(buffer.Data, [Material]);
        if (mesh is null)
        {
            GD.PushWarning($"Topic island '{Topic.GetPreferredTitle()}' did not produce a mesh.");
            return;
        }

        // shape.SetFaces(mesh.GetFaces());
        var position = new Vector3(heightmapBox.Position.X, 0, heightmapBox.Position.Y);
        _.Mesh.Mesh = mesh;
        _.Mesh.Position = position;
        _.Body.Get().Position = position;
        // _.Body.Collider.Shape = shape;
    }

    private static byte ToByteHeight(float height)
    {
        // NB: height 0 and 1 have special meanings; 0 is invisible deep sea; 1 is shallow sea
        return (byte)(height < 0f ? 0
                : height == 0 ? 1
                : Mathf.RoundToInt(height) + 2
            );
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
