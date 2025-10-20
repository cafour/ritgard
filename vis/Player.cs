using System.Collections.Generic;
using Godot;

namespace Ritgard;

public partial class Player : Node3D
{
    public const float BaseHeight = 100f;
    public const float HoverReach = 4096;

    private readonly IMovementMode[] movementModes = new IMovementMode[(int)ControlMode.Max + 1];

    [Export]
    public float LookaroundSpeed { get; set; } = 0.01f;

    [Export]
    public float MoveSpeed { get; set; } = 10f;

    [Export]
    public float PanSpeed { get; set; } = 1f;

    [Export]
    public float ZoomStep { get; set; } = 0.05f;

    [Export]
    public float ZoomSpeed { get; set; } = 10f;

    [Export]
    public ControlMode ControlMode { get; set; }

    public ColorRect Crosshair => Overlord.Instance.UI.Crosshair;

    [Signal]
    public delegate void HoverChangedEventHandler(CollisionObject3D hoveree);

    public CollisionObject3D Hoveree { get; private set; }

    public Camera3D Camera { get; private set; }

    public IMovementMode MovementMode { get; private set; }

    public override void _EnterTree()
    {
        Camera = GetNode<Camera3D>("Camera");
        movementModes[(int)ControlMode.TopDown] = new TopDownMovementMode(this);
        movementModes[(int)ControlMode.Flying] = new FlyingMovementMode(this);
        MovementMode = movementModes[(int)ControlMode];
        MovementMode.Activate();
    }

    public override void _Process(double delta)
    {
        MovementMode.Move(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        MovementMode.OnPhysicsProcess(delta);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsAction(InputActions.ToggleMode) && @event.IsPressed())
        {
            ControlMode = (ControlMode)(((int)ControlMode + 1) % ((int)ControlMode.Max + 1));
            MovementMode = movementModes[(int)ControlMode];
            MovementMode.Activate();
        }

        MovementMode.OnInput(@event);
    }

    public void Hover(Vector2? viewportPos)
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = viewportPos is not null
            ? Camera.ProjectRayOrigin(viewportPos.Value)
            : Camera.GlobalPosition;
        var to = viewportPos is not null
            ? from + HoverReach * Camera.ProjectRayNormal(viewportPos.Value)
            : from + Camera.Transform * Vector3.Forward * HoverReach;
        var query = PhysicsRayQueryParameters3D.Create(
            from,
            to,
            1 << 1 // Items
        );
        query.CollideWithAreas = true;
        var result = space.IntersectRay(query);
        var collider = result.GetValueOrDefault("collider").As<CollisionObject3D>();
        if (collider != Hoveree)
        {
            Hoveree = collider;
            EmitSignalHoverChanged(Hoveree);
        }
    }
}
