using Godot;

namespace Ritgard;

public class TopDownMovementMode(Player player) : IMovementMode
{
    public const float BaseHeight = 100f;
    public const float CameraBaseSize = 1000f;

    public Player Player { get; } = player;

    public float Pitch { get; private set; } = -Mathf.Pi / 6;

    public float Yaw { get; private set; } = -Mathf.Pi / 6;

    public float ZoomLevel { get; private set; } = 1f;

    public void Activate()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.Crosshair.Visible = false;

        Player.Camera.Basis = Basis.Identity;
        Player.Camera.RotateObjectLocal(Vector3.Up, -Mathf.Pi / 6);
        Player.Camera.RotateObjectLocal(Vector3.Right, -Mathf.Pi / 6);
        Player.Camera.Projection = Camera3D.ProjectionType.Orthogonal;
        Player.Camera.Size = CameraBaseSize * ZoomLevel;

        Player.Position = Player.Position with { Y = BaseHeight };
    }

    public void OnInput(InputEvent @event)
    {
        var step = Player.ZoomStep;
        if (Input.IsActionPressed(InputActions.MoveRun))
        {
            step *= 2;
        }

        if (@event.IsAction(InputActions.ZoomIn) && @event.IsPressed())
        {
            ZoomLevel -= step;
        }
        else if (@event.IsAction(InputActions.ZoomOut) && @event.IsPressed())
        {
            ZoomLevel += step;
        }

        ZoomLevel = Mathf.Clamp(ZoomLevel, 0.1f, 10f);
    }

    public void Move(double delta)
    {
        var move = Input.GetVector(
            InputActions.MoveLeft,
            InputActions.MoveRight,
            InputActions.MoveForward,
            InputActions.MoveBackward
        );

        move = move.Rotated(-Player.Camera.Rotation.Y);

        var speed = Player.MoveSpeed * Mathf.Clamp(ZoomLevel, 0.5f, 2f);
        if (Input.IsActionPressed(InputActions.MoveRun))
        {
            speed *= 2;
        }

        Player.Position += new Vector3(move.X, 0, move.Y) * (float)delta * speed;
        Player.Camera.Size = Mathf.Lerp(
            Player.Camera.Size,
            CameraBaseSize * ZoomLevel,
            Mathf.Min(1.0f, (float)delta * Player.ZoomSpeed)
        );
    }

    public void OnPhysicsProcess(double delta)
    {
        Player.Hover(Player.Camera.GetViewport().GetMousePosition());
    }
}
