using System;
using System.Collections.Generic;
using Godot;

namespace Ritgard;

public partial class Player : Node3D
{
    public const float BaseHeight = 100f;
    public const float HoverReach = 2048;

    private Camera3D camera;
    private float pitch;
    private float yaw;
    private float zoomLevel;

    [Export]
    public float LookaroundSpeed { get; set; } = 0.01f;

    [Export]
    public float MoveSpeed { get; set; } = 10f;

    [Export]
    public float ZoomStep { get; set; } = 2f;

    [Export]
    public float ZoomSpeed { get; set; } = 10f;

    [Export]
    public ControlMode ControlMode { get; set; }
    
    [Export]
    public ColorRect Crosshair { get; set; }

    [Signal]
    public delegate void HoverChangedEventHandler(CollisionObject3D hoveree);
    
    public CollisionObject3D Hoveree { get; set; }

    public override void _EnterTree()
    {
        camera = GetNode<Camera3D>("Camera");

        zoomLevel = BaseHeight;
        switch (ControlMode)
        {
            case ControlMode.TopDown:
                Position = Position with { Y = BaseHeight };
                InitTopDown();
                break;
            case ControlMode.Flying:
                InitFlying();
                break;
            default:
                throw new NotSupportedException();
        }
    }

    public override void _Process(double delta)
    {
        if (ControlMode == ControlMode.TopDown)
        {
            MoveTopDown(delta);
        }
        else
        {
            MoveFlying(delta);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        switch (ControlMode)
        {
            case ControlMode.TopDown:
                Hover(camera.GetViewport().GetMousePosition());
                break;
            case ControlMode.Flying:
                Hover(null);
                break;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsAction("toggle_mode") && @event.IsPressed())
        {
            ControlMode = (ControlMode)(((int)ControlMode + 1) % ((int)ControlMode.Max + 1));
            switch (ControlMode)
            {
                case ControlMode.TopDown:
                    InitTopDown();
                    break;
                case ControlMode.Flying:
                    InitFlying();
                    break;
                default:
                    throw new NotSupportedException();
            }
            return;
        }

        switch (ControlMode)
        {
            case ControlMode.TopDown:
                InputTopDown(@event);
                break;
            case ControlMode.Flying:
                InputFlying(@event);
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private void InitTopDown()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        camera.Basis = Basis.Identity;
        camera.RotateObjectLocal(Vector3.Right, -Mathf.Pi / 3);
        Position = Position with { Y = zoomLevel };
        Crosshair.Visible = false;
    }

    private void MoveTopDown(double delta)
    {
        var move = Input.GetVector(
            "move_left",
            "move_right",
            "move_forward",
            "move_backward"
        );

        var speed = MoveSpeed * Mathf.Clamp(zoomLevel / BaseHeight, 0.5f, 2f);
        if (Input.IsActionPressed("move_run"))
        {
            speed *= 2;
        }

        var position = Position + new Vector3(move.X, 0, move.Y) * (float)delta * speed;
        position.Y = Mathf.Lerp(position.Y, zoomLevel, (float)delta * ZoomSpeed);
        Position = position;
    }

    private void InputTopDown(InputEvent @event)
    {
        var step = ZoomStep;
        if (Input.IsActionPressed("move_run"))
        {
            step *= 2;
        }
        if (@event.IsAction("zoom_in") && @event.IsPressed())
        {
            zoomLevel -= step;
        }
        else if (@event.IsAction("zoom_out") && @event.IsPressed())
        {
            zoomLevel += step;
        }
    }

    private void InitFlying()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Crosshair.Visible = true;
    }

    private void MoveFlying(double delta)
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

    public void InputFlying(InputEvent @event)
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

    public void Hover(Vector2? viewportPos)
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = viewportPos is not null
            ? camera.ProjectRayOrigin(viewportPos.Value)
            : camera.GlobalPosition;
        var to = viewportPos is not null
            ? from + HoverReach * camera.ProjectRayNormal(viewportPos.Value)
            : from + camera.Transform * Vector3.Forward * HoverReach;
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
