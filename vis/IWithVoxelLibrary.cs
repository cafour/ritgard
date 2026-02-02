using Godot;
using Ritgard.Voxel;

namespace Ritgard;

public interface IWithVoxelLibrary
{
    VoxelBlockLibrary Library { get; }
}
