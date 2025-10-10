using Godot;

namespace Ritgard;

public class FlyingMovementMode(Player player) : IMovementMode
{
    public Player Player { get; } = player;

    public float Pitch { get; private set; } = -Mathf.Pi / 6;

    public float Yaw { get; private set; } = -Mathf.Pi / 6;

    public void Activate()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Player.Crosshair.Visible = true;
        Player.Position = Player.Position with { Y = Player.BaseHeight };
        Player.Camera.Projection = Camera3D.ProjectionType.Perspective;
    }

    public void OnInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            Yaw += mouseMotion.Relative.X * Player.LookaroundSpeed * -1;
            Pitch += mouseMotion.Relative.Y * Player.LookaroundSpeed * -1;
            Pitch = Mathf.Clamp(Pitch, -Mathf.Pi / 2, Mathf.Pi / 2);
            Player.Camera.Basis = Basis.Identity;
            Player.Camera.RotateObjectLocal(Vector3.Up, Yaw);
            Player.Camera.RotateObjectLocal(Vector3.Right, Pitch);
        }
    }

    public void Move(double delta)
    {
        var move = new Vector3(
            Input.GetAxis(InputActions.MoveLeft, InputActions.MoveRight),
            Input.GetAxis(InputActions.MoveDown, InputActions.MoveUp),
            Input.GetAxis(InputActions.MoveForward, InputActions.MoveBackward)
        ).Normalized();

        if (Input.IsActionPressed(InputActions.MoveRight))
        {
            delta *= 2;
        }

        Player.Position += Player.Camera.Transform * move * (float)delta * Player.MoveSpeed;
    }

    public void OnPhysicsProcess(double delta)
    {
        Player.Hover(null);
    }
}
