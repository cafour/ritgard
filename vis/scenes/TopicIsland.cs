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

    public static ImmutableArray<Blocks> Palette = [
        Blocks.Vis01,
        Blocks.Vis02,
        Blocks.Vis03,
        Blocks.Vis04,
        Blocks.Vis05,
        Blocks.Vis06,
        Blocks.Vis07,
        Blocks.Vis08,
    ];

    private ImmutableHashSet<long> itemIds;
    private ImmutableArray<Vector2> itemPoints;
    private Rect2I heightmapBox;
    private ArrayMesh arrayMesh = new();
    private RenderingDevice device;
    private float[] vertices;

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


    public override void _Ready()
    {
        if (Topic is null)
        {
            throw new NullReferenceException("Topic is not set.");
        }

        itemIds = [.. Overlord.Instance.Repo.TopicModelling.Items
            .Where(i => i.Value.TopicId == Topic.Id)
            .Select(i => i.Value.Id)];
        InitializeHeightmap();
        InitializePlane();

        device = RenderingServer.GetRenderingDevice();
    }

    public void OnShowStep(int step)
    {
        // ComputeLeveledHeightmap(points, bbox);
        ComputeSmoothHeightmap();
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

    // private void ComputeLeveledHeightmap(IEnumerable<Vector3> points, Aabb bbox)
    // {
    //     var coords = points.Select(v => new Coordinate(v.X, v.Z)).ToArray();
    //     var pointCloud = Geometry.DefaultFactory.CreateMultiPointFromCoords(coords);
    //     var hull = ConcaveHull.ConcaveHullByLengthRatio(pointCloud, 0.3);
    //     var kdTree = new KdTree<Issue>();
    //     foreach (var id in itemIds)
    //     {
    //         var pos = Overlord.Instance.Positions[id];
    //         kdTree.Insert(new Coordinate(pos.X, pos.Z), Overlord.Instance.Data[id]);
    //     }

    //     var testPoint = Geometry.DefaultFactory.CreatePoint(new Coordinate(0, 0));
    //     for (int z = SmoothRadius; z < Heightmap.GetLength(0); ++z)
    //     {
    //         for (int x = SmoothRadius; x < Heightmap.GetLength(1); ++x)
    //         {
    //             var px = x + bbox.Position.X;
    //             var py = z + bbox.Position.Z;

    //             testPoint.Coordinate.X = px;
    //             testPoint.Coordinate.Y = py;

    //             var nearestPoint = kdTree.NearestNeighbor(testPoint.Coordinate);
    //             if (nearestPoint is null || nearestPoint.Count == 0)
    //             {
    //                 continue;
    //             }

    //             var nearestIssue = nearestPoint.Data;
    //             var nearestPointHeight = Overlord.Instance.Positions[nearestIssue.Id].Y;

    //             if (hull.Contains(testPoint))
    //             {
    //                 Heightmap[z, x] = (byte)Math.Clamp(Mathf.RoundToInt(nearestPointHeight), 0, 255);
    //             }
    //             else
    //             {
    //                 var distance = nearestPoint.Coordinate.Distance(testPoint.Coordinate);
    //                 if (distance < StructureRadius)
    //                 {
    //                     Heightmap[z, x] = (byte)nearestPointHeight;
    //                 }
    //                 else
    //                 {
    //                     distance -= StructureRadius;
    //                     var height = nearestPointHeight / (distance * distance + 1);
    //                     Heightmap[z, x] = (byte)height;
    //                 }
    //             }
    //         }
    //     }
    // }

    public const int SmoothRadius = 10;

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
        itemPoints = itemIds.Select(i => new Vector2(
            x: Overlord.Instance.Repo.Items[i].Position.X,
            y: Overlord.Instance.Repo.Items[i].Position.Y
        )).ToImmutableArray();
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
        vertices = new float[3 * hh * hw];
        var indices = new int[(hh - 1) * (hw - 1) * 3 * 2];
        // x goes right, y is actually z and goes "down", towards the camera
        for (int y = 0; y < hh; ++y)
        {
            for (int x = 0; x < hw; ++x)
            {
                var baseIndex = y * hw + x;
                vertices[3 * baseIndex + 0] = x;
                vertices[3 * baseIndex + 1] = 0;
                vertices[3 * baseIndex + 2] = y;

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
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[hh * hw];
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
        _.Mesh.SetSurfaceOverrideMaterial(0, Material);
        _.Body.Get().Position = _.Mesh.Position;
    }

    private void UpdatePlane()
    {
        var hh = Heightmap.GetLength(0);
        var hw = Heightmap.GetLength(1);
        // var format = arrayMesh.SurfaceGetFormat(0);
        // var testOffset = RenderingServer.MeshSurfaceGetFormatOffset(
        //     (RenderingServer.ArrayFormat)format,
        //     hh * hw,
        //     0
        // );
        // var testStride = RenderingServer.MeshSurfaceGetFormatVertexStride(
        //     (RenderingServer.ArrayFormat)format,
        //     hh * hw
        // );
        for (int y = 0; y < hh; ++y)
        {
            for (int x = 0; x < hw; ++x)
            {
                vertices[3 * (y * hw + x) + 1] = Heightmap[y, x];
            }
        }
        var bytes = MemoryMarshal.Cast<float, byte>(vertices.AsSpan());
        arrayMesh.SurfaceUpdateVertexRegion(0, 0, bytes);

        // var shape = new ConvexPolygonShape3D();
        // shape.Points = arrayMesh.SurfaceGetArrays(0)[0].AsVector3Array();
        // _.Body.Collider.Shape = shape;
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
}
