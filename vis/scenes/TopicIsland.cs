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
    public const float StructureRadius = 3f;
    
    public Topic Topic { get; set; }

    [Export]
    public VoxelBlockyLibrary Library { get; set; }
    
    [Export]
    public VoxelMesherBlocky Mesher { get; set; }

    [Export]
    public Material Material { get; set; }
    
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
        var coords = points.Select(v => new Coordinate(v.X, v.Z)).ToArray();
        var pointCloud = Geometry.DefaultFactory.CreateMultiPointFromCoords(coords);
        var hull = ConcaveHull.ConcaveHullByLengthRatio(pointCloud, 0.5);
        var kdTree = new KdTree<Issue>();
        foreach (var id in Topic.Ids)
        {
            var pos = Overlord.Instance.Positions[id];
            kdTree.Insert(new Coordinate(pos.X, pos.Z), Overlord.Instance.Data[id]);
        }
        var bbox = new Aabb(
            new Vector3((float)hull.Envelope.Coordinates[0].X, 0f, (float)hull.Envelope.Coordinates[0].Y),
            new Vector3(
                (float)Math.Abs(hull.Envelope.Coordinates[0].X - hull.Envelope.Coordinates[3].X),
                points.Max(p => p.Y),
                (float)Math.Abs(hull.Envelope.Coordinates[0].Y - hull.Envelope.Coordinates[1].Y)
            )
        );
        var intSize = Utils.RoundToInt(bbox.Size);
        var buffer = new StructureBuffer(
            size: intSize,
            library: Library,
            offset: new Vector3I(1, 1, 1)
        );

        Heightmap = new byte[intSize.Z, intSize.X];
        var testPoint = Geometry.DefaultFactory.CreatePoint(new Coordinate(0, 0));
        for (int z = 0; z < intSize.Z; ++z)
        {
            for (int x = 0; x < intSize.X; ++x)
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
                        var height = nearestPointHeight / (distance * distance + 1);
                        Heightmap[z, x] = (byte)height;
                    }
                }
            }
        }

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
}
