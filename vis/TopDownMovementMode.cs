using Godot;

namespace Ritgard;

public class TopDownMovementMode(Player player) : IMovementMode
{
    public const float BaseHeight = 0f;
    public const float CameraBaseSize = 1000f;
    public const float DefaultPitch = -Mathf.Pi / 6;
    public const float DefaultYaw = -Mathf.Pi / 6;

    public Player Player { get; } = player;

    public float Pitch { get; private set; } = DefaultPitch;

    public float Yaw { get; private set; } = DefaultYaw;

    public float ZoomLevel { get; private set; } = 1f;

    public void Activate()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.Crosshair.Visible = false;

        Player.Camera.Size = CameraBaseSize * ZoomLevel;
        Player.Camera.Projection = Camera3D.ProjectionType.Orthogonal;
        RotateCamera();

        Player.Position = Player.Position with { Y = BaseHeight };
    }

    public void OnInput(InputEvent @event)
    {
        if (@event.IsAction(InputActions.ResetCamera) && @event.IsPressed())
        {
            Pitch = DefaultPitch;
            Yaw = DefaultYaw;
            RotateCamera();
            Player.Position = new Vector3(0, BaseHeight, 0);
        }

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

        if (Input.IsMouseButtonPressed(MouseButton.Left) && @event is InputEventMouseMotion pan)
        {
            var windowFactor = Player.Camera.Size / Player.Camera.GetWindow().Size.Y;
            var pitchFactor = -1f / Mathf.Sin(Pitch);
            var diff = new Vector2(pan.ScreenRelative.X, pan.ScreenRelative.Y * pitchFactor);
            diff = diff.Rotated(-Yaw) * windowFactor;
            Player.Position += new Vector3(-diff.X, 0, -diff.Y);
        }
        else if (Input.IsMouseButtonPressed(MouseButton.Right) && @event is InputEventMouseMotion rot)
        {
            Pitch -= rot.ScreenRelative.Y * 0.005f;
            Pitch = Mathf.Clamp(Pitch, -Mathf.Pi / 2, -Mathf.Pi / 12);
            Yaw -= rot.ScreenRelative.X * 0.005f;
            Yaw = Mathf.Wrap(Yaw, -Mathf.Pi, Mathf.Pi);

            RotateCamera();
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

    private void RotateCamera()
    {
        Player.Camera.Basis = Basis.Identity;
        Player.Camera.RotateObjectLocal(Vector3.Up, Yaw);
        Player.Camera.RotateObjectLocal(Vector3.Right, Pitch);
        Player.Camera.Position -= Player.Camera.Transform * (Vector3.Forward * Player.Camera.Size);
    }
}
