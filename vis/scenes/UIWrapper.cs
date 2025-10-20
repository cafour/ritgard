using Godot;

namespace Ritgard;

[SceneTree("ui.tscn")]
public partial class UIWrapper : Node
{
    public Label ItemDescriptionLabel => _.HoverBar.ItemDescriptionLabel;

    public OptionButton DatasetDropdown => _.Controls.Container.DatasetDropdown;

    public SpinBox CurrentStepSpinBox => _.Controls.Container.CurrentStep;

    public Label CurrentTimeLabel => _.Controls.Container.CurrentTime;

    public CheckButton IssuesCheck => _.Controls.Container.IssuesCheck;

    public CheckButton PRsCheck => _.Controls.Container.PRsCheck;

    public CheckButton DiscussionsCheck => _.Controls.Container.DiscussionsCheck;

    public ColorRect Crosshair => _.CrosshairContainer.Crosshair;
}
