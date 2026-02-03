using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using NetTopologySuite.Utilities;
using Color = Godot.Color;

namespace Ritgard.Voxel;

/// <summary>
/// A simple, blocky voxel mesher. More-or-less completely ported from the godot_voxel addon by Zylann
/// (https://github.com/Zylann/godot_voxel).
/// </summary>
public class VoxelMesher
{
    private static readonly Vector3[] CornerPositions =
    [
        new Vector3(1, 0, 0),
        new Vector3(0, 0, 0),
        new Vector3(0, 0, 1),
        new Vector3(1, 0, 1),
        new Vector3(1, 1, 0),
        new Vector3(0, 1, 0),
        new Vector3(0, 1, 1),
        new Vector3(1, 1, 1)
    ];

    public static readonly int[,] SideCorners =
    {
        { 3, 0, 4, 7 },
        { 1, 2, 6, 5 },
        { 1, 0, 3, 2 },
        { 4, 5, 6, 7 },
        { 0, 1, 5, 4 },
        { 2, 3, 7, 6 }
    };

    // 3---2
    // |   |
    // 0---1
    public static readonly int[,] SideQuadTriangles =
    {
        { 0, 2, 1, 0, 3, 2 }, // LEFT (+x)
        { 0, 2, 1, 0, 3, 2 }, // RIGHT (-x)
        { 0, 2, 1, 0, 3, 2 }, // BOTTOM (-y)
        { 0, 2, 1, 0, 3, 2 }, // TOP (+y)
        { 0, 2, 1, 0, 3, 2 }, // BACK (-z)
        { 0, 2, 1, 0, 3, 2 }, // FRONT (+z)
    };

    public static readonly Vector3[] SideNormals =
    [
        new Vector3(1, 0, 0), // LEFT
        new Vector3(-1, 0, 0), // RIGHT
        new Vector3(0, -1, 0), // BOTTOM
        new Vector3(0, 1, 0), // TOP
        new Vector3(0, 0, -1), // BACK
        new Vector3(0, 0, 1) // FRONT
    ];

    public static readonly Vector4[] SideTangents =
    {
        new Vector4(0.0f, 0.0f, -1.0f, 1.0f),
        new Vector4(0.0f, 0.0f, 1.0f, 1.0f),

        new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
        new Vector4(-1.0f, 0.0f, 0.0f, 1.0f),

        new Vector4(-1.0f, 0.0f, 0.0f, 1.0f),
        new Vector4(1.0f, 0.0f, 0.0f, 1.0f)
    };

    public static readonly int[,] SideEdges =
    {
        { 3, 7, 11, 4 },
        { 1, 6, 9, 5 },
        { 0, 1, 2, 3 },
        { 8, 9, 10, 11 },
        { 0, 5, 8, 4 },
        { 2, 6, 10, 7 }
    };

    public Mesh BuildMesh(VoxelBuffer buffer, VoxelBlockLibrary library, Material mainMaterial)
    {
        Span<int> sideLut = stackalloc int[(int)Sides.Count];
        Span<int> edgeLut = stackalloc int[(int)Edges.Count];
        Span<int> cornerLut = stackalloc int[(int)Corners.Count];
        ConstructLookupTables(buffer.Size, sideLut, edgeLut, cornerLut);

        var min = (X: 1u, Y: 1u, Z: 1u);
        var max = (X: (uint)buffer.Size.X - 1u, Y: (uint)buffer.Size.Y - 1u, Z: (uint)buffer.Size.Z - 1u);

        Material[] materials = [mainMaterial, ..library.Materials.Except([mainMaterial]).OrderBy(m => m.ResourcePath)];
        var surfaces = materials.Select((m, i) => new VoxelMeshSurface() { MaterialIndex = i }).ToArray();
        var materialIndices = materials
            .Select((material, index) => (material, index))
            .ToImmutableDictionary(p => p.material, p => p.index);

        for (uint z = min.Z; z < max.Z; ++z)
        {
            for (uint x = min.X; x < max.X; ++x)
            {
                for (uint y = min.Y; y < max.Y; ++y)
                {
                    var index = buffer.GetIndex(x, y, z);
                    var value = buffer.RawData[index];
                    if (value == VoxelBuffer.NoneValue)
                    {
                        continue;
                    }

                    var visibleSidesMask = 0;
                    for (int side = 0; side < (int)Sides.Count; ++side)
                    {
                        var otherValue = buffer.RawData[index + sideLut[side]];
                        if (otherValue == VoxelBuffer.NoneValue)
                        {
                            visibleSidesMask |= 1 << side;
                        }
                    }

                    var blockType = library.Types[value];
                    var material = blockType.Material ?? mainMaterial;
                    var surface = surfaces[blockType.Material is null ? 0 : materialIndices[material]];

                    var voxelPosition = new Vector3(x - 1, y - 1, z - 1);

                    for (int side = 0; side < (int)Sides.Count; ++side)
                    {
                        if ((visibleSidesMask & (1 << side)) == 0)
                        {
                            // cull this side
                            continue;
                        }

                        var indexOffset = surface.Positions.Count;

                        // positions
                        {
                            surface.Positions.AddRange(Enumerable.Repeat(Vector3.Zero, 4));
                            var posSpan = CollectionsMarshal.AsSpan(surface.Positions)[^4..];
                            SetSidePositions(posSpan, (Sides)side);
                            for (int i = 0; i < 4; ++i)
                            {
                                posSpan[i] += voxelPosition;
                            }
                        }

                        // UVs
                        {
                            surface.UVs.AddRange(Enumerable.Repeat(Vector2.Zero, 4));
                            var uvSpan = CollectionsMarshal.AsSpan(surface.UVs)[^4..];
                            SetSideUVs(uvSpan, (Sides)side);
                        }

                        // tangents
                        {
                            surface.Tangents.AddRange(Enumerable.Repeat(Vector4.Zero, 4));
                            var tanSpan = CollectionsMarshal.AsSpan(surface.Tangents)[^4..];
                            SetSideTangents(tanSpan, (Sides)side);
                        }

                        // normals
                        {
                            surface.Normals.AddRange(Enumerable.Repeat(Vector3.Zero, 4));
                            var norSpan = CollectionsMarshal.AsSpan(surface.Normals)[^4..];
                            SetSideNormals(norSpan, (Sides)side);
                        }

                        // colors
                        {
                            surface.Colors.AddRange(Enumerable.Repeat(new Color(), 4));
                            var colSpan = CollectionsMarshal.AsSpan(surface.Colors)[^4..];
                            for (int i = 0; i < 4; ++i)
                            {
                                colSpan[i] = blockType.Color;
                            }

                            // TODO: Ambient occlusion
                        }

                        // indices
                        {
                            surface.Indices.AddRange(Enumerable.Repeat(0, 6));
                            var indSpan = CollectionsMarshal.AsSpan(surface.Indices)[^6..];
                            SetSideIndices(indSpan, (Sides)side);
                            for (int i = 0; i < 6; ++i)
                            {
                                indSpan[i] += indexOffset;
                            }
                        }
                    }
                }
            }
        }

        return ConstructGodotMesh(materials, surfaces);
    }

    private static void ConstructLookupTables(Vector3I size, Span<int> sideLut, Span<int> edgeLut, Span<int> cornerLut)
    {
        var rowSize = size.Y;
        var levelSize = rowSize * size.X;

        sideLut[(int)Sides.Left] = +rowSize;
        sideLut[(int)Sides.Right] = -rowSize;
        sideLut[(int)Sides.Back] = -levelSize;
        sideLut[(int)Sides.Front] = +levelSize;
        sideLut[(int)Sides.Bottom] = -1;
        sideLut[(int)Sides.Top] = +1;

        edgeLut[(int)Edges.BottomBack] = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Back];
        edgeLut[(int)Edges.BottomFront] = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Front];
        edgeLut[(int)Edges.BottomLeft] = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Left];
        edgeLut[(int)Edges.BottomRight] = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Right];
        edgeLut[(int)Edges.BackLeft] = sideLut[(int)Sides.Back] + sideLut[(int)Sides.Left];
        edgeLut[(int)Edges.BackRight] = sideLut[(int)Sides.Back] + sideLut[(int)Sides.Right];
        edgeLut[(int)Edges.FrontLeft] = sideLut[(int)Sides.Front] + sideLut[(int)Sides.Left];
        edgeLut[(int)Edges.FrontRight] = sideLut[(int)Sides.Front] + sideLut[(int)Sides.Right];
        edgeLut[(int)Edges.TopBack] = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Back];
        edgeLut[(int)Edges.TopFront] = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Front];
        edgeLut[(int)Edges.TopLeft] = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Left];
        edgeLut[(int)Edges.TopRight] = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Right];

        cornerLut[(int)Corners.BottomBackLeft]
            = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Back] + sideLut[(int)Sides.Left];
        cornerLut[(int)Corners.BottomBackRight]
            = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Back] + sideLut[(int)Sides.Right];
        cornerLut[(int)Corners.BottomFrontRight]
            = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Front] + sideLut[(int)Sides.Right];
        cornerLut[(int)Corners.BottomFrontLeft]
            = sideLut[(int)Sides.Bottom] + sideLut[(int)Sides.Front] + sideLut[(int)Sides.Left];
        cornerLut[(int)Corners.TopBackLeft]
            = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Back] + sideLut[(int)Sides.Left];
        cornerLut[(int)Corners.TopBackRight]
            = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Back] + sideLut[(int)Sides.Right];
        cornerLut[(int)Corners.TopFrontRight]
            = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Front] + sideLut[(int)Sides.Right];
        cornerLut[(int)Corners.TopFrontLeft]
            = sideLut[(int)Sides.Top] + sideLut[(int)Sides.Front] + sideLut[(int)Sides.Left];
    }

    private Mesh ConstructGodotMesh(IReadOnlyList<Material> materials, IReadOnlyList<VoxelMeshSurface> surfaces)
    {
        var mesh = new ArrayMesh();
        foreach (var surface in surfaces)
        {
            if (surface.Positions.Count == 0)
            {
                continue;
            }

            var meshArrays = new Godot.Collections.Array();
            meshArrays.Resize((int)Mesh.ArrayType.Max);
            meshArrays[(int)Mesh.ArrayType.Vertex] = CollectionsMarshal.AsSpan(surface.Positions);
            meshArrays[(int)Mesh.ArrayType.TexUV] = CollectionsMarshal.AsSpan(surface.UVs);
            meshArrays[(int)Mesh.ArrayType.Tangent] = CollectionsMarshal.AsSpan(surface.Tangents);
            meshArrays[(int)Mesh.ArrayType.Normal] = CollectionsMarshal.AsSpan(surface.Normals);
            meshArrays[(int)Mesh.ArrayType.Color] = CollectionsMarshal.AsSpan(surface.Colors);
            meshArrays[(int)Mesh.ArrayType.Index] = CollectionsMarshal.AsSpan(surface.Indices);

            mesh.AddSurfaceFromArrays(
                primitive: Mesh.PrimitiveType.Triangles,
                arrays: meshArrays
            );
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials[surface.MaterialIndex]);
        }

        return mesh;
    }

    private static void SetSidePositions(Span<Vector3> positions, Sides side, float height = 1.0f)
    {
        Assert.IsEquals(4, positions.Length);
        for (int i = 0; i < 4; ++i)
        {
            var corner = SideCorners[(int)side, i];
            var p = CornerPositions[corner];
            if (p.Y > 0.9f)
            {
                p.Y = height;
            }

            positions[i] = p;
        }
    }

    private static void SetSideUVs(Span<Vector2> uvs, Sides side, float height = 1.0f)
    {
        Assert.IsEquals(4, uvs.Length);

        const float e = 0.001f;
        if (side is Sides.Top or Sides.Bottom)
        {
            uvs[0] = new Vector2(e, 1.0f - e);
            uvs[1] = new Vector2(1.0f - e, 1.0f - e);
            uvs[2] = new Vector2(1.0f - e, e);
            uvs[3] = new Vector2(e, e);
        }
        else
        {
            var topY = Mathf.Lerp(1.0f - e, e, height);
            uvs[0] = new Vector2(e, 1.0f - e);
            uvs[1] = new Vector2(1.0f - e, 1.0f - e);
            uvs[2] = new Vector2(1.0f - e, topY);
            uvs[3] = new Vector2(e, topY);
        }
    }

    private static void SetSideTangents(Span<Vector4> tangents, Sides side)
    {
        Assert.IsEquals(4, tangents.Length);
        for (int i = 0; i < 4; ++i)
        {
            tangents[i] = SideTangents[(int)side];
        }
    }

    private static void SetSideNormals(Span<Vector3> normals, Sides side)
    {
        Assert.IsEquals(4, normals.Length);
        for (int i = 0; i < 4; ++i)
        {
            normals[i] = SideNormals[(int)side];
        }
    }

    private static void SetSideIndices(Span<int> indices, Sides side)
    {
        Assert.IsEquals(6, indices.Length);
        for (int i = 0; i < 6; ++i)
        {
            indices[i] = SideQuadTriangles[(int)side, i];
        }
    }

    private record struct VoxelMeshSurface()
    {
        public List<Vector3> Positions { get; } = [];
        public List<Vector2> UVs { get; } = [];
        public List<Vector4> Tangents { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<Color> Colors { get; } = [];
        public List<int> Indices { get; } = [];
        public int MaterialIndex { get; set; } = 0;
    }

    private enum Sides
    {
        Left = 0,
        Right,
        Bottom,
        Top,
        Back,
        Front,

        Count
    }

    private enum Edges
    {
        BottomBack,
        BottomRight,
        BottomFront,
        BottomLeft,

        BackLeft,
        BackRight,

        FrontRight,
        FrontLeft,

        TopBack,
        TopRight,
        TopFront,
        TopLeft,

        Count
    }

    private enum Corners
    {
        BottomBackLeft,
        BottomBackRight,
        BottomFrontRight,
        BottomFrontLeft,

        TopBackLeft,
        TopBackRight,
        TopFrontRight,
        TopFrontLeft,

        Count
    }
}
