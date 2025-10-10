using Godot;

namespace Ritgard;

public interface IMovementMode
{
    void Activate();
    void OnInput(InputEvent @event);
    void Move(double delta);
    void OnPhysicsProcess(double delta);

}
