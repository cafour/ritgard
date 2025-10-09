using Godot;
using NetTopologySuite.Triangulate.Tri;

namespace Ritgard;

public static partial class Utils
{
    public static (float alpha, float beta, float gamma) GetBarycentricCoords(
        float x,
        float y,
        float x1,
        float y1,
        float x2,
        float y2,
        float x3,
        float y3
    )
    {
        float detT = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
        float alpha = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / detT;
        float beta = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / detT;
        float gamma = 1.0f - alpha - beta;
        return (alpha, beta, gamma);
    }

    public static Vector3 GetBarycentricCoords(Vector2 point, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        var (alpha, beta, gamma) = GetBarycentricCoords(
            point.X,
            point.Y,
            v1.X,
            v1.Y,
            v2.X,
            v2.Y,
            v3.X,
            v3.Y
        );
        return new Vector3(alpha, beta, gamma);
    }

    public static Vector3 GetBarycentricCoords(Vector2 point, Tri tri)
    {
        return GetBarycentricCoords(
            point,
            tri.GetCoordinate(0).ToVector2(),
            tri.GetCoordinate(1).ToVector2(),
            tri.GetCoordinate(2).ToVector2()
        );
    }

    public static (Tri? tri, Vector3 barycentricCoords) LocateTriangle(Tri start, Vector2 point)
    {
        var current = start;
        do
        {
            var (alpha, beta, gamma) = Utils.GetBarycentricCoords(point, current);
            if (alpha >= 0 && beta >= 0 && gamma >= 0)
            {
                return (current, new(alpha, beta, gamma));
            }

            if (alpha < 0 && current.HasAdjacent(1))
            {
                current = current.GetAdjacent(1);
            }
            else if (beta < 0 && current.HasAdjacent(2))
            {
                current = current.GetAdjacent(2);
            }
            else if (gamma < 0 && current.HasAdjacent(0))
            {
                current = current.GetAdjacent(0);
            }
            else
            {
                current = null;
            }
        } while (current is not null);

        return (null, Vector3.Zero);
    }
}
