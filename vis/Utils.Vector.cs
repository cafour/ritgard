using Godot;

namespace Ritgard;

public static partial class Utils
{
    public static Vector3I ToVector3I(Vector3 vector)
    {
        return new Vector3I(
            (int)vector.X,
            (int)vector.Y,
            (int)vector.Z
        );
    }
}
