using Godot;

namespace Ritgard.Structures;

public interface ILayoutable
{
    Aabb Measure();

    void SetLayoutPosition(Vector3 position);
}
