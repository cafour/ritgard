using Godot;

namespace Ritgard;

public partial class SidePanel : Control
{
    public override void _Input(InputEvent e)
    {
        var focusOwner = GetViewport()?.GuiGetFocusOwner();
        if (focusOwner is null)
        {
            return;
        }

        if (e.IsAction("ui_cancel"))
        {
            focusOwner.ReleaseFocus();
            return;
        }

        if (e is InputEventMouseButton mbe)
        {
            if (MakeInputLocal(mbe) is InputEventMouseButton { Pressed: true } local)
            {
                var rect = new Rect2(Vector2.Zero, Size);
                if (!rect.HasPoint(local.Position))
                {
                    focusOwner.ReleaseFocus();
                }
            }
        }
    }
}
