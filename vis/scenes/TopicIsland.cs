using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.KdTree;
using Ritgard.Mining;

namespace Ritgard.Structures;

[SceneTree("topic_island.tscn")]
public partial class TopicIsland : Node3D
{
    public const int GrassWidth = 1;
    public const int DirtWidth = 3;
    public const int StructureRadius = 3;

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
        GenerateMesh();
    }

    private void GenerateMesh()
    {
        if (Topic is null)
        {
            throw new NullReferenceException("Topic is not set.");
        }

        var points = Topic.Ids.Select(id => Overlord.Instance.Positions.GetValueOrDefault(id)).ToArray();

        var startCorner = new Vector3(
            points.Min(p => p.X) - SmoothRadius,
            0,
            points.Min(p => p.Z) - SmoothRadius
        );
        var endCorner = new Vector3(
            points.Max(p => p.X) + SmoothRadius,
            points.Max(p => p.Y),
            points.Max(p => p.Z) + SmoothRadius
        );
        var bbox = new Aabb(startCorner, endCorner - startCorner);
        var intSize = Utils.RoundToInt(bbox.Size) + new Vector3I(StructureRadius * 2, 0, StructureRadius * 2);
        var buffer = new StructureBuffer(
            size: intSize,
            library: Library,
            offset: new Vector3I(1, 1, 1)
        );

        Heightmap = new byte[intSize.Z, intSize.X];
        // ComputeLeveledHeightmap(points, bbox);
        ComputeSmoothHeightmap(points, bbox);

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
                    Blocks.Grass
                );
            }
        }

        var shape = new ConcavePolygonShape3D();
        var mesher = new VoxelMesherBlocky();
        mesher.Library = Library;
        var mesh = mesher.BuildMesh(buffer.Data, [Material]);
        shape.SetFaces(mesh.GetFaces());
        _.Mesh.Mesh = mesh;
        _.Mesh.Position = bbox.Position;
        _.Body.Collider.Shape = shape;
        _.Body.Get().Position = bbox.Position;
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

    private void ComputeLeveledHeightmap(IEnumerable<Vector3> points, Aabb bbox)
    {
        var coords = points.Select(v => new Coordinate(v.X, v.Z)).ToArray();
        var pointCloud = Geometry.DefaultFactory.CreateMultiPointFromCoords(coords);
        var hull = ConcaveHull.ConcaveHullByLengthRatio(pointCloud, 0.3);
        var kdTree = new KdTree<Issue>();
        foreach (var id in Topic.Ids)
        {
            var pos = Overlord.Instance.Positions[id];
            kdTree.Insert(new Coordinate(pos.X, pos.Z), Overlord.Instance.Data[id]);
        }

        var testPoint = Geometry.DefaultFactory.CreatePoint(new Coordinate(0, 0));
        for (int z = SmoothRadius; z < Heightmap.GetLength(0); ++z)
        {
            for (int x = SmoothRadius; x < Heightmap.GetLength(1); ++x)
            {
                var px = x + bbox.Position.X;
                var py = z + bbox.Position.Z;

                testPoint.Coordinate.X = px;
                testPoint.Coordinate.Y = py;

                var nearestPoint = kdTree.NearestNeighbor(testPoint.Coordinate);
                if (nearestPoint is null || nearestPoint.Count == 0)
                {
                    continue;
                }

                var nearestIssue = nearestPoint.Data;
                var nearestPointHeight = Overlord.Instance.Positions[nearestIssue.Id].Y;

                if (hull.Contains(testPoint))
                {
                    Heightmap[z, x] = (byte)Math.Clamp(Mathf.RoundToInt(nearestPointHeight), 0, 255);
                }
                else
                {
                    var distance = nearestPoint.Coordinate.Distance(testPoint.Coordinate);
                    if (distance < StructureRadius)
                    {
                        Heightmap[z, x] = (byte)nearestPointHeight;
                    }
                    else
                    {
                        distance -= StructureRadius;
                        var height = nearestPointHeight / (distance * distance + 1);
                        Heightmap[z, x] = (byte)height;
                    }
                }
            }
        }
    }

    public const int SmoothRadius = 10;

    private void ComputeSmoothHeightmap(IEnumerable<Vector3> points, Aabb bbox)
    {
        foreach (var point in points)
        {
            var radius = SmoothRadius;
            for (int z = -radius; z < radius; ++z)
            {
                var hz = Mathf.RoundToInt(point.Z - bbox.Position.Z + z);
                if (hz < 0 || hz >= Heightmap.GetLength(0))
                {
                    continue;
                }

                for (int x = -radius; x < radius; ++x)
                {
                    var hx = Mathf.RoundToInt(point.X - bbox.Position.X + x);
                    if (hx < 0 || hx >= Heightmap.GetLength(1))
                    {
                        continue;
                    }

                    var distSquared = x * x + z * z;
                    // var height = point.Y * Mathf.Exp(-distSquared / (float)(SmoothRadius * SmoothRadius));
                    var height = point.Y * Mathf.Exp(-Mathf.Log(point.Y) / (radius * radius) * distSquared);
                    var byteHeight = (byte)Math.Clamp(Mathf.RoundToInt(height), 0, 255);
                    // Heightmap[hz, hx] = Math.Max(Heightmap[hz, hx], byteHeight);
                    Heightmap[hz, hx] += byteHeight;
                }
            }
        }
    }
}
