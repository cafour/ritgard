using System.Runtime.CompilerServices;
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

    extension(System.Numerics.Vector2 vec2)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Godot.Vector2 ToGodot()
        {
            return new Godot.Vector2(vec2.X, vec2.Y);
        }
    }

    extension(System.Numerics.Vector3 vec3)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Godot.Vector3 ToGodot()
        {
            return new Godot.Vector3(vec3.X, vec3.Y, vec3.Z);
        }
    }

    extension(System.Numerics.Vector4 vec4)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Godot.Vector4 ToGodot()
        {
            return new Godot.Vector4(vec4.X, vec4.Y, vec4.Z, vec4.W);
        }
    }
}
