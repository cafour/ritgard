using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace Ritgard.Mining;

public class LayoutAdjuster
{
    private readonly ILogger<LayoutAdjuster> logger;

    public LayoutAdjuster(ILogger<LayoutAdjuster> logger)
    {
        this.logger = logger;
    }

    public ImmutableArray<IssueTopic> AdjustPositions(ImmutableArray<IssueTopic> issues)
    {
        var builder = new DelaunayTriangulationBuilder();
        builder.SetSites([.. issues.Select(i => new Coordinate(i.X, i.Y))]);
        var subdivision = builder.GetSubdivision();

        var edges = subdivision.GetPrimaryEdges(false);
        var lengths = edges.Select(e => e.Length).ToArray();
        var minLength = lengths.Min();
        var maxLength = lengths.Max();
        var avgLength = lengths.Average();

        return issues;
    }
}
