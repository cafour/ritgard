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

    public IslandHeightmap Generate(
        ActiveRepository repo,
        int topicId,
        TimeSpan slidingWindow,
        TimeSpan stepLength,
        ConversationScope scope = ConversationScope.All,
        int startStep = 0,
        int stepCount = -1,
        CancellationToken ct = default
    )
    {
        if (!repo.TopicModelling.Topics.TryGetValue(topicId, out var topic))
        {
            throw new ArgumentException($"Topic '{topicId}' does not exist.", nameof(topicId));
        }

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
            var startDate = repo.MinDate + startStep * stepLength;
            stepCount = Math.Max(1, (int)Math.Ceiling((repo.MaxDate - startDate) / stepLength));
        }

        var items = repo.Items.Values
            .Where(i => i.TopicId == topicId && i.Conversation.IsInScope(scope))
            .ToImmutableArray();

        if (items.Length == 0)
        {
            logger.LogWarning(
                "Topic '{TopicName}' is empty. An invalid heightmap will be generated. This is probably a bug.",
                topic.GetPreferredTitle()
            );
        }

        var itemTree = new KdTree<int>();
        for (int i =0; i < items.Length; ++i)
        {
            var item = items[i];
            itemTree.Insert(new Coordinate(item.Position.X, item.Position.Y), i);
        }

        logger.LogInformation(
            "Computing heights for each step and item in topic '{TopicName}'.",
            topic.GetPreferredTitle()
        );
        var heights = GetHeights(repo, items, slidingWindow, stepLength, stepCount);
        var maxHeight = heights.Max();
        // NB: +2 because of the two special height values (0 = deep sea, 1 = shallow sea)
        var scale = 1 << Math.Min(0, BitOperations.Log2((uint)maxHeight + 2) - 7);

        logger.LogInformation("Initializing heightmap for topic '{TopicName}'.", topic.GetPreferredTitle());
        var heightmap = InitializeHeightmap(items, stepCount, scale);

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

                    ComputeHeightmapStep(
                        heightmap: heightmap,
                        items: items,
                        itemTree,
                        heights: heights.AsSpan(items.Length * step, items.Length),
                        repo: repo,
                        topicId: topicId,
                        blurTemp: blurTemp,
                        step: step,
                        scope: scope,
                        ct: ct
                    );
                }
            }
        );

        return heightmap;
    }

    private IslandHeightmap InitializeHeightmap(ImmutableArray<ActiveItem> items, int stepCount, int scale)
    {
        var bbox = (
            minX: (int)MathF.Floor(items.Min(i => i.Position.X)) - HeightmapPadding,
            minY: (int)MathF.Floor(items.Min(i => i.Position.Y)) - HeightmapPadding,
            maxX: (int)MathF.Ceiling(items.Max(i => i.Position.X)) + HeightmapPadding,
            maxY: (int)MathF.Ceiling(items.Max(i => i.Position.Y)) + HeightmapPadding
        );

        return IslandHeightmap.CreateEmpty(
            sizeX: bbox.maxX - bbox.minX,
            sizeY: bbox.maxY - bbox.minY,
            positionX: bbox.minX,
            positionY: bbox.minY,
            stepCount: stepCount,
            scale: scale
        );
    }

    private void ComputeHeightmapStep(
        IslandHeightmap heightmap,
        ImmutableArray<ActiveItem> items,
        KdTree<int> itemTree,
        ReadOnlySpan<int> heights,
        ActiveRepository repo,
        int topicId,
        int step,
        float[,] blurTemp,
        ConversationScope scope,
        CancellationToken ct = default
    )
    {
        var slice = heightmap.GetRawStepSpan(step);

        var maxHeight = 0;
        foreach (var height in heights)
        {
            maxHeight = Math.Max(maxHeight, height);
        }

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
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var tri = hullTris[i];
                var maxSide = Math.Max(tri.GetLength(0), Math.Max(tri.GetLength(1), tri.GetLength(2)));
                if (maxSide > avgTriMaxSide + TriOutlierCutoff * stdDeviation)
                {
                    tri.Remove();
                    hullTris.RemoveAt(i);
                    continue;
                }

                var triPolygon = tri.ToPolygon(GeometryFactory.Default);
                var intrudingPoints = repo.ItemTree.Query(triPolygon.EnvelopeInternal)
                    .Where(n => n.Data.TopicId != topicId && n.Data.Conversation.IsInScope(scope))
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
                "Failed to get a hull polygon for topic '{TopicName}' at step '{StepIndex}'. Result will look 'spotty'.",
                repo.TopicModelling.Topics[topicId].GetPreferredTitle(),
                step
            );
        }

        for (int z = 0; z < heightmap.SizeY; ++z)
        {
            for (int x = 0; x < heightmap.SizeX; ++x)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                ref var value = ref slice[x + z * heightmap.SizeY];

                var px = x + heightmap.PositionX;
                var py = z + heightmap.PositionY;

                var coord = new Coordinate(px, py);

                if (hullPolygon is not null && hullPolygon.Contains(GeometryFactory.Default.CreatePoint(coord)))
                {
                    value = 1;
                }

                var nearestTri = triTree.NearestNeighbor(coord);
                if (nearestTri is not null)
                {
                    var (containingTri, barycentricCoords) =
                        Utils.LocateTriangle(nearestTri.Data, new Vector2(px, py));
                    var (alpha, beta, gamma) = (barycentricCoords.X, barycentricCoords.Y, barycentricCoords.Z);
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
                            logger.LogWarning("Couldn't re-determine which item is at coordinate ({CoordX}, {CoordY}).",
                                px, py);
                            continue;
                        }

                        var v0Height = heights[v0Item.Data];
                        var v1Height = heights[v1Item.Data];
                        var v2Height = heights[v2Item.Data];

                        var height = v0Height * Utils.Smoothstep(0, 1, alpha)
                            + v1Height * Utils.Smoothstep(0, 1, beta)
                            + v2Height * Utils.Smoothstep(0, 1, gamma);
                        value = heightmap.ToByteHeight(height);
                    }
                }

                var nearestPoint = itemTree.NearestNeighbor(coord);
                if (nearestPoint is not null)
                {
                    var nearestPointHeight = heights[nearestPoint.Data];
                    var distance = nearestPoint.Coordinate.Distance(coord);
                    if (distance < StructureRadius)
                    {
                        value = heightmap.ToByteHeight(nearestPointHeight);
                    }

                    if (hullPolygon is not null
                        && !hullPolygon.Contains(GeometryFactory.Default.CreatePoint(nearestPoint.Coordinate))
                        && distance < StructureRadius * 1.5f)
                    {
                        value = 1;
                    }
                }
            }
        }

        GaussianBlur.Blur(heightmap, step, BlurKernel, blurTemp);

        for (int i = 0; i < items.Length; ++i)
        {
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
}
