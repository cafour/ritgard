using Godot;

namespace Ritgard;

[SceneTree("ui.tscn")]
public partial class UIWrapper : Control
{
    public Label ItemDescriptionLabel => _.HoverBar.ItemDescriptionLabel;

    public OptionButton DatasetDropdown => _.Controls.Container.DatasetDropdown;

    public OptionButton SlidingWindowDropdown => _.Controls.Container.SlidingWindowDropdown;

    public SpinBox CurrentStepSpinBox => _.Controls.Container.CurrentStep;

    public LineEdit CurrentDateTime => _.Controls.Container.CurrentDateTime;

    public CheckButton ShowTreesCheck => _.Controls.Container.ShowTreesCheck;

    public CheckButton IssuesCheck => _.Controls.Container.IssuesCheck;

    public CheckButton PRsCheck => _.Controls.Container.PRsCheck;

    public CheckButton DiscussionsCheck => _.Controls.Container.DiscussionsCheck;

    public CheckButton StubsCheck => _.Controls.Container.StubsCheck;

    public CheckButton OnlyPopulatedIslandsCheck => _.Controls.Container.OnlyPopulatedIslandsCheck;

    public CheckButton NormalizeHeightsCheck => _.Controls.Container.NormalizeHeightsCheck;

    public SpinBox MaxNormalizedHeightSpinBox => _.Controls.Container.MaxNormalizedHeight;

    public ColorRect Crosshair => _.CrosshairContainer.Crosshair;
}
