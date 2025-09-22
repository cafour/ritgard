using Godot;

namespace Ritgard.Structures;

public interface IStructure
{
    void Build(StructureBuffer buffer);

    (Vector3I min, Vector3I max) Measure();
}
