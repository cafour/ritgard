using Godot;

namespace Ritgard;

public partial class Player : Node3D
{
    [Export]
    public float LookaroundSpeed { get; set; } = 0.01f;

    [Export]
    public float MoveSpeed { get; set; } = 10f;

    private Camera3D camera;
    private float pitch;
    private float yaw;

    public override void _EnterTree()
    {
        camera = GetNode<Camera3D>("Camera");
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Process(double delta)
    {
        var move = new Vector3(
            Input.GetAxis("move_left", "move_right"),
            Input.GetAxis("move_down", "move_up"),
            Input.GetAxis("move_forward", "move_backward")
        ).Normalized();

        if (Input.IsActionPressed("move_run"))
        {
            delta *= 2;
        }

        Position += camera.Transform * move * (float)delta * MoveSpeed;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            yaw += mouseMotion.Relative.X * LookaroundSpeed * -1;
            pitch += mouseMotion.Relative.Y * LookaroundSpeed * -1;
            pitch = Mathf.Clamp(pitch, -Mathf.Pi / 2, Mathf.Pi / 2);
            camera.Basis = Basis.Identity;
            camera.RotateObjectLocal(Vector3.Up, yaw);
            camera.RotateObjectLocal(Vector3.Right, pitch);
        }
    }
}
