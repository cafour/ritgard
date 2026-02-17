using Godot;

namespace Ritgard;

public partial class SidePanel : Control
{
    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton mbe)
        {
            if (MakeInputLocal(mbe) is InputEventMouseButton local)
            {
                var rect = new Rect2(Vector2.Zero, Size);
                if (!rect.HasPoint(local.Position))
                {
                    GetViewport()?.GuiGetFocusOwner()?.ReleaseFocus();
                }
            }
        }
    }
}
