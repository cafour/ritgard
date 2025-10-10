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
        Player.Camera.RotateObjectLocal(Vector3.Up, Pitch);
        Player.Camera.RotateObjectLocal(Vector3.Right, Yaw);
        Player.Camera.Projection = Camera3D.ProjectionType.Orthogonal;
        Player.Camera.Size = CameraBaseSize * ZoomLevel;
        Player.Camera.Position -= Player.Camera.Transform * (Vector3.Forward * Player.Camera.Size);

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

        if (Input.IsMouseButtonPressed(MouseButton.Left) && @event is InputEventMouseMotion motion)
        {
            var windowFactor = Player.Camera.Size / Player.Camera.GetWindow().Size.Y;
            var pitchFactor = -1f / Mathf.Sin(Pitch);
            var diff = new Vector2(motion.ScreenRelative.X, motion.ScreenRelative.Y * pitchFactor);
            diff = diff.Rotated(-Yaw) * windowFactor;
            Player.Position += new Vector3(-diff.X, 0, -diff.Y);
        }
    }

    public void Move(double delta)
    {
        var move = Input.GetVector(
            InputActions.MoveLeft,
            InputActions.MoveRight,
            InputActions.MoveForward,
            InputActions.MoveBackward
        );

        move = move.Rotated(-Yaw);

        var speed = Player.PanSpeed * Player.Camera.Size;
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
        Player.Camera.Position -= Player.Camera.Transform * (Vector3.Forward * Player.Camera.Size);
    }

    public void OnPhysicsProcess(double delta)
    {
        Player.Hover(Player.Camera.GetViewport().GetMousePosition());
    }
}
