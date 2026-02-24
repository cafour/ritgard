using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Ritgard;

public partial class Player : Node3D
{
    public const float BaseHeight = 100f;
    public const float HoverReach = 4096;

    public static Player Instance { get; private set; } = null!;

    private readonly IMovementMode[] movementModes = new IMovementMode[(int)ControlMode.Max + 1];

    [Export]
    public float LookaroundSpeed { get; set; } = 0.01f;

    [Export]
    public float MoveSpeed { get; set; } = 10f;

    [Export]
    public float PanSpeed { get; set; } = 1f;

    [Export]
    public float ZoomStep { get; set; } = 0.025f;

    [Export]
    public float ZoomSpeed { get; set; } = 10f;

    [Export]
    public ControlMode ControlMode { get; set; }

    public ColorRect Crosshair => Overlord.Instance.UI.Crosshair;

    [Signal]
    public delegate void HoverChangedEventHandler(CollisionObject3D hoveree);

    public CollisionObject3D? Hoveree { get; private set; }

    public Camera3D Camera { get; private set; } = null!;

    public IMovementMode MovementMode { get; private set; } = null!;

    public override void _EnterTree()
    {
        Instance?.QueueFree();
        Instance = this;

        Camera = GetNode<Camera3D>("Camera");
        movementModes[(int)ControlMode.TopDown] = new TopDownMovementMode(this);
        movementModes[(int)ControlMode.Flying] = new FlyingMovementMode(this);
        MovementMode = movementModes[(int)ControlMode];
        MovementMode.Activate();
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GuiGetFocusOwner() is not null)
        {
            return;
        }

        MovementMode.Move(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        MovementMode.OnPhysicsProcess(delta);
    }

    public override async void _Input(InputEvent @event)
    {
        var focusedControl = GetViewport().GuiGetFocusOwner();
        if (focusedControl is not null)
        {
            return;
        }

        if (@event.IsAction(InputActions.ToggleMode) && @event.IsPressed())
        {
            ControlMode = (ControlMode)(((int)ControlMode + 1) % ((int)ControlMode.Max + 1));
            MovementMode = movementModes[(int)ControlMode];
            MovementMode.Activate();
        }
        else if ((@event.IsAction(InputActions.Screenshot) || @event.IsAction(InputActions.CleanScreenshot))
                 && @event.IsPressed())
        {
            await TakeScreenshot(@event.IsAction(InputActions.CleanScreenshot));
        }

        MovementMode.OnInput(@event);
    }

    public async Task TakeScreenshot(bool clean)
    {
        if (Overlord.Instance.Repo is null)
        {
            GD.PushWarning("No repo loaded. Cannot take a screenshot.");
            return;
        }

        var viewport = GetViewport();

        Sky? originalSky = null;
        if (clean)
        {
            viewport.TransparentBg = true;
            originalSky = Overlord.Instance.Environment.Environment.Sky;
            // Overlord.Instance.Environment.Environment.Sky = null;
            Overlord.Instance.UI.Visible = false;
        }


        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var image = viewport.GetTexture().GetImage();
        var filename =
            $"screenshot_{Overlord.Instance.Repo.Mining.Repository.Name}_{DateTimeOffset.UtcNow:yyyy-MM-dd_HH-mm-ss}.png";
        GD.Print($"Saving screenshot '{filename}'");
        var error = image.SavePng(filename);
        if (error != Error.Ok)
        {
            GD.PrintErr($"Could not take a screenshot. Godot returned: {error}");
        }

        if (originalSky is not null)
        {
            viewport.TransparentBg = false;
            Overlord.Instance.Environment.Environment.Sky = originalSky;
            Overlord.Instance.UI.Visible = true;
        }
    }

    public void Hover(Vector2? viewportPos)
    {
        var collider = CastRay(viewportPos, 1 << 1 | 1 << 2); // ItemStructures
        if (collider != Hoveree)
        {
            Hoveree = collider;
            EmitSignalHoverChanged(Hoveree);
        }
    }

    private CollisionObject3D? CastRay(Vector2? viewportPos, uint collisionMask)
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
            collisionMask
        );
        query.CollideWithAreas = true;
        var result = space.IntersectRay(query);
        return result.GetValueOrDefault("collider").As<CollisionObject3D>();
    }
}
