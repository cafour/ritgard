using Godot;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate.Tri;

namespace Ritgard;

public static class NetTopologySuiteExtensions
{
    public static Vector2 ToVector2(this Coordinate coord)
    {
        return new Vector2((float)coord.X, (float)coord.Y);
    }

    public static Vector2 ToVector2(this Point point)
    {
        return point.Coordinate.ToVector2();
    }

    public static Vector2 GetCentroid(this Tri tri)
    {
        var v0 = tri.GetCoordinate(0);
        var v1 = tri.GetCoordinate(1);
        var v2 = tri.GetCoordinate(2);
        return new Vector2(
            (float)(v0.X + v1.X + v2.X) / 3f,
            (float)(v0.Y + v1.Y + v2.Y) / 3f
        );
    }
}
