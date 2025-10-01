using Godot;

namespace Ritgard;

public static partial class Utils
{
    public static Vector3I FloorToInt(Vector3 vector)
    {
        return new Vector3I(
            (int)vector.X,
            (int)vector.Y,
            (int)vector.Z
        );
    }

    public static Vector3I RoundToInt(Vector3 vector)
    {
        return new Vector3I(
            Mathf.RoundToInt(vector.X),
            Mathf.RoundToInt(vector.Y),
            Mathf.RoundToInt(vector.Z)
        );
    }
}
