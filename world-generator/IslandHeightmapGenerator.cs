using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.KdTree;
using NetTopologySuite.Triangulate.Tri;
using Ritgard.Mining;

namespace Ritgard.WorldGenerator;

public class IslandHeightmapGenerator(
    ActiveRepository repo,
    int topicId,
    ConversationScope scope,
    ILogger<IslandHeightmapGenerator>? logger = null
)
{
    public const int HeightmapPadding = 8;
    public const double TriOutlierCutoff = 2.0;
    public const int StructureRadius = ActiveRepository.StructureRadius;
    public const int StructureSafetyRange = 1;

    private static readonly float[] BlurKernel = GaussianBlur.CreateKernel(2f, StructureRadius - 1);

    private readonly ILogger<IslandHeightmapGenerator> logger
        = logger ?? NullLoggerFactory.Instance.CreateLogger<IslandHeightmapGenerator>();

    private ImmutableArray<ActiveItem> items = [];
    private readonly KdTree<int> itemTree = new KdTree<int>();
    private string topicName = "<Unknown topic>";
    private (int x, int y) min;
    private (int x, int y) max;
    private (int x, int y) size;
    private MaskElement[,] mask = null!;

    public ActiveRepository Repo { get; } = repo;
    public int TopicId { get; } = topicId;
    public ConversationScope Scope { get; } = scope;

    public void Initialize(CancellationToken ct = default)
    {
        if (!Repo.TopicModelling.Topics.TryGetValue(TopicId, out var topic))
        {
            throw new ArgumentException($"Topic '{TopicId}' does not exist.", nameof(topicId));
        }

        topicName = topic.GetPreferredTitle();

        logger.LogInformation("Initializing island generator for topic '{TopicName}'.", topicName);

        items =
        [
            ..Repo.Items.Values
                .Where(i => i.TopicId == TopicId && i.Conversation.IsInScope(Scope))
        ];

        if (items.Length == 0)
        {
            logger.LogWarning(
                "Topic '{TopicName}' is empty. An invalid heightmap will be generated. This is probably a bug.",
                topic.GetPreferredTitle()
            );
        }

        for (int i = 0; i < items.Length; ++i)
        {
            var item = items[i];
            itemTree.Insert(new Coordinate(item.Position.X, item.Position.Y), i);
        }

        min = (
            (int)MathF.Floor(items.Min(i => i.Position.X)) - HeightmapPadding,
            (int)MathF.Floor(items.Min(i => i.Position.Y)) - HeightmapPadding
        );
        max = (
            (int)MathF.Ceiling(items.Max(i => i.Position.X)) + HeightmapPadding,
            (int)MathF.Ceiling(items.Max(i => i.Position.Y)) + HeightmapPadding
        );
        size = (
            max.x - min.x,
            max.y - min.y
        );
        mask = PrepareMask(ct);
    }

    public IslandHeightmap Generate(
        TimeSpan slidingWindow,
        TimeSpan stepLength,
        int startStep = 0,
        int stepCount = -1,
        CancellationToken ct = default
    )
    {
        if (startStep < 0)
        {
            throw new ArgumentException("Start step must not be negative.", nameof(startStep));
        }

        if (stepCount == 0)
        {
            throw new ArgumentException("Step count cannot be zero.", nameof(stepCount));
        }

        if (stepCount < 0)
        {
            var startDate = Repo.MinDate + startStep * stepLength;
            stepCount = Math.Max(1, (int)Math.Ceiling((Repo.MaxDate - startDate) / stepLength));
        }

        if (slidingWindow == TimeSpan.MaxValue)
        {
            stepLength = Repo.MaxDate - Repo.MinDate;
            stepCount = 1;
        }

        logger.LogInformation("Computing heights for each step and item in topic '{TopicName}'.",topicName);
        var heights = GetHeights(Repo, items, slidingWindow, stepLength, stepCount);
        var maxHeight = heights.Max();
        // NB: +2 because of the two special height values (0 = deep sea, 1 = shallow sea)
        var scale = 1 << Math.Max(0, BitOperations.Log2((uint)maxHeight + 2) - 7);

        logger.LogInformation("Initializing heightmap for topic '{TopicName}'.", topicName);
        var heightmap = IslandHeightmap.CreateEmpty(
            sizeX: size.x,
            sizeY: size.y,
            positionX: min.x,
            positionY: min.y,
            stepCount: stepCount,
            scale: scale
        );

        Parallel.ForEach(
            Partitioner.Create(startStep, startStep + stepCount),
            (range, state) =>
            {
                logger.LogInformation(
                    "Thread {ThreadId} is processing step range [{RangeStart}, {RangeEnd}).",
                    Environment.CurrentManagedThreadId,
                    range.Item1,
                    range.Item2
                );
                var blurTemp = new float[heightmap.SizeY, heightmap.SizeX];
                for (int step = range.Item1; step < range.Item2; ++step)
                {
                    if (ct.IsCancellationRequested)
                    {
                        state.Stop();
                    }

                    if (state.ShouldExitCurrentIteration)
                    {
                        return;
                    }

                    // logger.LogInformation(
                    //     "{ThreadId}: step {Step} {RelativeStep}/{RelativeStepCount}",
                    //     Environment.CurrentManagedThreadId,
                    //     step,
                    //     step - range.Item1,
                    //     range.Item2 - range.Item1
                    // );
                    ComputeHeightmapStep(
                        heightmap: heightmap,
                        heights: heights.AsSpan(items.Length * step, items.Length),
                        blurTemp: blurTemp,
                        step: step,
                        ct: ct
                    );
                }
            }
        );

        return heightmap;
    }

    private void ComputeHeightmapStep(
        IslandHeightmap heightmap,
        ReadOnlySpan<int> heights,
        int step,
        float[,] blurTemp,
        CancellationToken ct = default
    )
    {
        var slice = heightmap.GetRawStepSpan(step);

        for (int y = 0; y < heightmap.SizeY; ++y)
        {
            for (int x = 0; x < heightmap.SizeX; ++x)
            {
                ct.ThrowIfCancellationRequested();

                ref var value = ref slice[x + y * heightmap.SizeX];
                var currentMask = mask[y, x];
                value = currentMask.BaseHeight;
                if (currentMask.BarycentricCoords != default)
                {
                    var (alpha, beta, gamma) = (
                        currentMask.BarycentricCoords.X,
                        currentMask.BarycentricCoords.Y,
                        currentMask.BarycentricCoords.Z
                    );

                    var v0Height = heights[currentMask.ItemIndices.item1];
                    var v1Height = heights[currentMask.ItemIndices.item2];
                    var v2Height = heights[currentMask.ItemIndices.item3];

                    var height = v0Height * Utils.Smoothstep(0, 1, alpha)
                        + v1Height * Utils.Smoothstep(0, 1, beta)
                        + v2Height * Utils.Smoothstep(0, 1, gamma);
                    value = heightmap.ToByteHeight(height);
                }
            }
        }

        for (int i = 0; i < items.Length; ++i)
        {
            ct.ThrowIfCancellationRequested();

            var item = items[i];
            var height = heightmap.ToByteHeight(heights[i]);
            var px = (int)MathF.Round(item.Position.X) - heightmap.PositionX;
            var py = (int)MathF.Round(item.Position.Y) - heightmap.PositionY;
            for (int y = -StructureRadius; y <= StructureRadius; ++y)
            {
                for (int x = -StructureRadius; x <= StructureRadius; ++x)
                {
                    if (new Vector2(x, y).Length() < StructureRadius)
                    {
                        slice[(x + px) + (y + py) * heightmap.SizeX] = height;
                    }
                }
            }
        }

        GaussianBlur.Blur(heightmap, step, BlurKernel, blurTemp);

        for (int i = 0; i < items.Length; ++i)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var item = items[i];
            var height = heightmap.ToByteHeight(heights[i]);
            var px = (int)MathF.Round(item.Position.X) - heightmap.PositionX;
            var py = (int)MathF.Round(item.Position.Y) - heightmap.PositionY;
            for (int y = -StructureSafetyRange; y <= StructureSafetyRange; ++y)
            {
                for (int x = -StructureSafetyRange; x <= StructureSafetyRange; ++x)
                {
                    slice[(x + px) + (y + py) * heightmap.SizeX] = height;
                }
            }
        }
    }

    private static ImmutableArray<int> GetHeights(
        ActiveRepository repo,
        ImmutableArray<ActiveItem> items,
        TimeSpan slidingWindow,
        TimeSpan stepLength,
        int stepCount
    )
    {
        if (items.Length == 0)
        {
            return [];
        }

        // NB: adjust for when sliding window is meant to capture the entire history
        slidingWindow = slidingWindow == TimeSpan.MaxValue ? repo.MaxDate - repo.MinDate : slidingWindow;

        var result = ImmutableArray.CreateBuilder<int>(items.Length * stepCount);
        for (int s = 0; s < stepCount; ++s)
        {
            var now = repo.MinDate + stepLength * s;
            foreach (var item in items)
            {
                result.Add(item.Events.GetRange(now - slidingWindow, now, maxInclusive: true).Count());
            }
        }

        return result.ToImmutable();
    }

    private (KdTree<Tri> triTree, Geometry? hullPolygon) Triangulate(CancellationToken ct)
    {
        var coords = items.Select(i => new Coordinate(i.Position.X, i.Position.Y)).ToArray();
        var pointCloud = Geometry.DefaultFactory.CreateMultiPointFromCoords(coords);
        var hull = new ConcaveHull(pointCloud)
        {
            MaximumEdgeLengthRatio = 0.5,
            HolesAllowed = true
        };
        var hullTris = hull.GetHullTris() ?? [];
        var triTree = new KdTree<Tri>();
        if (hullTris.Count > 0)
        {
            var triMaxSides = hullTris.Select(t => Math.Max(t.GetLength(0), Math.Max(t.GetLength(1), t.GetLength(2))))
                .ToImmutableArray();
            var avgTriMaxSide = triMaxSides.Average();
            var stdDeviation = Math.Sqrt(triMaxSides.Average(l => (l - avgTriMaxSide) * (l - avgTriMaxSide)));
            for (int i = hullTris.Count - 1; i >= 0; --i)
            {
                ct.ThrowIfCancellationRequested();

                var tri = hullTris[i];
                var maxSide = Math.Max(tri.GetLength(0), Math.Max(tri.GetLength(1), tri.GetLength(2)));
                if (maxSide > avgTriMaxSide + TriOutlierCutoff * stdDeviation)
                {
                    tri.Remove();
                    hullTris.RemoveAt(i);
                    continue;
                }

                var triPolygon = tri.ToPolygon(GeometryFactory.Default);
                var intrudingPoints = Repo.ItemTree.Query(triPolygon.EnvelopeInternal)
                    .Where(n => n.Data.TopicId != TopicId && n.Data.Conversation.IsInScope(Scope))
                    .Where(n => triPolygon.Contains(
                            GeometryFactory.Default.CreatePoint(
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
        }

        Geometry? hullPolygon = null;
        try
        {
            hullPolygon = hull.GetHull(hullTris);
            hullPolygon = hullPolygon.Buffer(MathF.Min(1, (int)MathF.Round(StructureRadius / 2f)));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to get a hull polygon for topic '{TopicName}'. Result will look 'spotty'.",
                Repo.TopicModelling.Topics[TopicId].GetPreferredTitle()
            );
        }

        return (triTree, hullPolygon);
    }

    private MaskElement[,] PrepareMask(CancellationToken ct = default)
    {
        var (triTree, hullPolygon) = Triangulate(ct);

        var mask = new MaskElement[size.y, size.x];
        for (int y = 0; y < size.y; ++y)
        {
            for (int x = 0; x < size.x; ++x)
            {
                ct.ThrowIfCancellationRequested();

                ref var current = ref mask[y, x];

                var px = x + min.x;
                var py = y + min.y;

                var coord = new Coordinate(px, py);

                if (hullPolygon is not null && hullPolygon.Contains(GeometryFactory.Default.CreatePoint(coord)))
                {
                    current.BaseHeight = 1;
                }

                var nearestTri = triTree.NearestNeighbor(coord);
                if (nearestTri is not null)
                {
                    var (containingTri, barycentricCoords) =
                        Utils.LocateTriangle(nearestTri.Data, new Vector2(px, py));
                    if (containingTri is not null)
                    {
                        var v0Item = itemTree.NearestNeighbor(containingTri.GetCoordinate(0));
                        var v1Item = itemTree.NearestNeighbor(containingTri.GetCoordinate(1));
                        var v2Item = itemTree.NearestNeighbor(containingTri.GetCoordinate(2));
                        if (v0Item is null
                            || v1Item is null
                            || v2Item is null
                            || v0Item == v1Item
                            || v0Item == v2Item
                            || v1Item == v2Item
                           )
                        {
                            logger.LogWarning(
                                "Couldn't re-determine which item is at coordinate ({CoordX}, {CoordY}).",
                                px,
                                py
                            );
                            continue;
                        }

                        current.ItemIndices = (v0Item.Data, v1Item.Data, v2Item.Data);
                        current.BarycentricCoords = barycentricCoords;
                    }
                }

                var nearestPoint = itemTree.NearestNeighbor(coord);
                if (nearestPoint is not null)
                {
                    var distance = nearestPoint.Coordinate.Distance(coord);
                    if (hullPolygon is not null
                        && !hullPolygon.Contains(GeometryFactory.Default.CreatePoint(nearestPoint.Coordinate))
                        && distance < StructureRadius * 1.5f)
                    {
                        current.BaseHeight = 1;
                    }
                }
            }
        }

        return mask;
    }

    private record struct MaskElement(
        (int item1, int item2, int item3) ItemIndices,
        Vector3 BarycentricCoords,
        byte BaseHeight
    );
}
