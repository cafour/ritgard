using Godot;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;
using NetTopologySuite.Triangulate.QuadEdge;
using Ritgard.Data;
using Ritgard.Mining;
using Ritgard.Structures;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ritgard;

public partial class Overlord : Node
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const float DefaultMaxNormalizedHeight = 42f;
    public const string DefaultHint = "Hover over a structure to see its description...";
    public const float BorderWidth = 5f;
    public const float SunRotationStep = Mathf.Pi / 6;

    public static readonly TimeSpan DefaultStep = TimeSpan.FromDays(1);

    public static Overlord Instance { get; private set; }

    [Export]
    public PackedScene ItemStructureScene { get; set; }

    [Export]
    public PackedScene TopicIslandScene { get; set; }

    [Export]
    public PackedScene OutlierRockScene { get; set; }

    [Export]
    public Player Player { get; set; }

    [Export]
    public Godot.Collections.Array<DatasetInfo> Datasets { get; set; }

    [Export]
    public UIWrapper UI { get; set; }

    [Export]
    public VoxelBlockyLibrary Library { get; set; }

    [Export]
    public MeshInstance3D Ocean { get; set; }

    [Export]
    public DirectionalLight3D Sun { get; set; }

    /// <summary>
    /// Exists to prevent the ocean from being semi-transparent on clean screenshots.
    /// </summary>
    [Export]
    public MeshInstance3D DeepOcean { get; set; }

    [Export]
    public MeshInstance3D TopBorder { get; set; }

    [Export]
    public MeshInstance3D RightBorder { get; set; }

    [Export]
    public MeshInstance3D BottomBorder { get; set; }

    [Export]
    public MeshInstance3D LeftBorder { get; set; }

    [Export]
    public WorldEnvironment Environment { get; set; }

    public int CurrentDataset { get; set; } = -1;

    public int CurrentStep { get; private set; }

    public VisualizationScope CurrentScope { get; private set; } = VisualizationScope.All;

    public SlidingWindowPreset SlidingWindowPreset { get; private set; } = SlidingWindowPreset.Month;

    public TimeSpan SlidingWindowLength { get; private set; }

    public TimeSpan StepLength { get; private set; } = DefaultStep;

    public int StepCount { get; private set; }

    public bool ShowClosedAsStubs { get; private set; } = true;

    public bool ShowOnlyPopulatedIslands { get; private set; } = false;

    public bool ShouldNormalizeHeights { get; private set; } = false;

    public bool ShouldShowTrees { get; private set; } = true;

    public float MaxNormalizedHeight { get; private set; } = DefaultMaxNormalizedHeight;

    public ActiveRepository Repo { get; private set; }

    public float CameraDistance { get; private set; } = 100f;

    public Dictionary<string, float> Heights { get; } = [];

    private readonly Dictionary<int, TopicIsland> topicIslands = [];
    private readonly Dictionary<string, ItemStructure> itemStructures = [];
    private readonly Dictionary<string, OutlierRock> outlierRocks = [];
    private ItemStructure currentStructure;
    private TopicIsland currentIsland;
    private Node generatedNodesContainer;
    private Texture2D topicIdTexture;
    private Texture2D itemIdTexture;
    private CancellationTokenSource? lastHeightmapCts = null;
    private Task? lastHeightmapTask = null;

    public override void _EnterTree()
    {
        if (Instance is not null)
        {
            Instance.QueueFree();
        }

        Instance = this;

        generatedNodesContainer = GetNode<Node>("GeneratedNodesContainer");

        Library.Bake();
    }

    public override async void _Ready()
    {
        BindUserInterface();

        await ShowDataset(0);
    }

    private async Task ShowDataset(int index)
    {
        if (CurrentDataset == index)
        {
            return;
        }

        CurrentDataset = index;

        var oldNodes = generatedNodesContainer.GetChildren();
        foreach (var old in oldNodes)
        {
            old.QueueFree();
            generatedNodesContainer.RemoveChild(old);
        }

        topicIslands.Clear();
        itemStructures.Clear();

        var dataset = Datasets[index];
        Repo = await ActiveRepository.Load(dataset);
        CameraDistance = Mathf.Sqrt(Repo.BBox.Size.X * Repo.BBox.Size.X + Repo.BBox.Size.Y * Repo.BBox.Size.Y);
        Player.MovementMode.ResetCamera();
        StepCount = Math.Max(1, Mathf.CeilToInt((Repo.MaxDate - Repo.MinDate) / StepLength));
        Heights.Clear();

        // NB: if preset is All, we have to recompute it
        SlidingWindowLength = GetSlidingWindowLength(SlidingWindowPreset);
        UI.CurrentStepSpinBox.MaxValue = StepCount - 1;

        ((PlaneMesh)Ocean.Mesh).Size = Repo.BBox.Size;
        ((PlaneMesh)DeepOcean.Mesh).Size = Repo.BBox.Size + new Vector2(BorderWidth, BorderWidth);
        ((PlaneMesh)TopBorder.Mesh).Size = new Vector2(Repo.BBox.Size.X + BorderWidth * 2, BorderWidth);
        TopBorder.Position = TopBorder.Position with { Z = -Repo.BBox.Size.Y / 2 };
        ((PlaneMesh)RightBorder.Mesh).Size = new Vector2(BorderWidth, Repo.BBox.Size.Y + BorderWidth * 2);
        RightBorder.Position = RightBorder.Position with { X = Repo.BBox.Size.X / 2 };
        ((PlaneMesh)BottomBorder.Mesh).Size = new Vector2(Repo.BBox.Size.X + BorderWidth * 2, BorderWidth);
        BottomBorder.Position = BottomBorder.Position with { Z = Repo.BBox.Size.Y / 2 };
        ((PlaneMesh)LeftBorder.Mesh).Size = new Vector2(BorderWidth, Repo.BBox.Size.Y + BorderWidth * 2);
        LeftBorder.Position = LeftBorder.Position with { X = -Repo.BBox.Size.X / 2 };

        foreach (var (topicId, topic) in Repo.TopicModelling.Topics)
        {
            if (topicId == -1)
            {
                continue;
            }

            var topicIsland = TopicIslandScene.Instantiate<TopicIsland>();
            topicIsland.Name = $"Topic{topic.Id}";
            topicIsland.Topic = topic;
            generatedNodesContainer.AddChild(topicIsland);
            topicIslands[topicId] = topicIsland;
        }

        foreach (var item in Repo.Items.Values)
        {
            var instance = ItemStructureScene.Instantiate<ItemStructure>();
            instance.Name = $"Item_{item.Id}";
            instance.Item = item;
            generatedNodesContainer.AddChild(instance);
            itemStructures[item.Id] = instance;

            if (Repo.TopicModelling.Items[item.Id].TopicId == -1)
            {
                var outlierRock = OutlierRockScene.Instantiate<OutlierRock>();
                outlierRock.Name = $"OutlierRock_{item.Id}";
                outlierRock.Item = item;
                generatedNodesContainer.AddChild(outlierRock);
                outlierRocks[item.Id] = outlierRock;
            }
        }

        Player.HoverChanged += OnHoverChanged;

        await ShowStep(0);
    }

    private async Task ShowStep(int step)
    {
        step = Math.Clamp(step, 0, StepCount - 1);

        var now = Repo.MinDate + StepLength * step;
        var slidingEvents = Repo.Items.Values.ToImmutableDictionary(
            v => v.Id,
            v => v.Events.GetRange(now - SlidingWindowLength, now, true).Count()
        );
        var maxEventCount = slidingEvents.Values.Max();
        var scale = ShouldNormalizeHeights && maxEventCount > 0 ? MaxNormalizedHeight / maxEventCount : 1f;
        foreach (var item in Repo.Items.Values)
        {
            if (!item.Conversation.IsInScope(CurrentScope))
            {
                Heights[item.Id] = 0;
            }
            else
            {
                Heights[item.Id] = slidingEvents[item.Id] * scale;
            }

            var itemStructure = itemStructures.GetValueOrDefault(item.Id);
            if (itemStructure is not null)
            {
                itemStructure.ShouldBeVisible = ShouldShowTrees;
                itemStructure.OnShowStep(step);
            }

            outlierRocks.GetValueOrDefault(item.Id)?.OnShowStep(step);
        }

        foreach (var topicIsland in topicIslands.Values)
        {
            topicIsland.Scope = CurrentScope;
            topicIsland.ShowOnlyWhenPopulated = ShowOnlyPopulatedIslands;
        }

        RefreshCurrentStepControls(step);

        if (lastHeightmapTask is not null && !lastHeightmapTask.IsCompleted)
        {
            GD.Print("Cancelling previous heightmap task.");
            if (lastHeightmapCts is not null)
            {
                await lastHeightmapCts.CancelAsync();
                lastHeightmapCts.Dispose();
            }

            lastHeightmapCts = null;
            lastHeightmapTask = null;
        }

        var currentHeightmapCts = new CancellationTokenSource();
        var currentHeightmapTask = Task.WhenAll(
            topicIslands.Values.Select(i => Task.Run(
                    () => i.ComputeHeightmap(currentHeightmapCts.Token),
                    currentHeightmapCts.Token
                )
            )
        );
        lastHeightmapTask = currentHeightmapTask;
        lastHeightmapCts = currentHeightmapCts;

        try
        {
            await currentHeightmapTask;
            foreach (var topicIsland in topicIslands.Values)
            {
                topicIsland.UpdatePlane();
            }
        }
        catch (TaskCanceledException)
        {
            // nop
        }
    }

    public override async void _Input(InputEvent @event)
    {
        if (@event.IsAction("interact") && @event.IsPressed())
        {
            if (currentStructure is not null)
            {
                var item = Repo.Items.GetValueOrDefault(currentStructure.Item.Id);
                if (!string.IsNullOrEmpty(item.Conversation.Url))
                {
                    OS.ShellOpen(item.Conversation.Url);
                }
            }
        }

        if (@event.IsAction(InputActions.LargeStepNext) && @event.IsPressed())
        {
            await ShowStep(CurrentStep + Mathf.RoundToInt(SlidingWindowLength / StepLength));
        }
        else if (@event.IsAction(InputActions.LargeStepPrev) && @event.IsPressed())
        {
            await ShowStep(CurrentStep - Mathf.RoundToInt(SlidingWindowLength / StepLength));
        }
        else if (@event.IsAction(InputActions.StepNext) && @event.IsPressed())
        {
            await ShowStep(CurrentStep + 1);
        }
        else if (@event.IsAction(InputActions.StepPrev) && @event.IsPressed())
        {
            await ShowStep(CurrentStep - 1);
        }

        if (@event.IsAction(InputActions.RotateSun) && @event.IsPressed())
        {
            Sun.RotateY(SunRotationStep);
        }

        if (@event.IsAction(InputActions.PrintIslands) && @event.IsPressed())
        {
            var topicStats = Repo.Items.Values.Where(i => Heights[i.Id] > 0)
                .GroupBy(i => i.TopicId)
                .Select(g => (
                        topicName: g.Key == -1
                            ? "<outliers>"
                            : Repo.TopicModelling.Topics.GetValueOrDefault(g.Key)?.GetPreferredTitle(),
                        treeCount: g.Count()
                    )
                )
                .Where(r => r.treeCount != 0)
                .OrderBy(r => r.treeCount);
            GD.Print("== TOPIC SUMMARY ==");
            foreach (var topic in topicStats)
            {
                GD.Print($"\t{topic.topicName}: {topic.treeCount} trees");
            }
        }
    }

    private void OnHoverChanged(CollisionObject3D hoveree)
    {
        var parent = hoveree?.GetParent();
        switch (parent)
        {
            case ItemStructure structure:
                if (Repo.Items.TryGetValue(structure.Item.Id, out var item))
                {
                    currentIsland?.ToggleHighlight(false);
                    currentIsland = null;

                    currentStructure?.ToggleHighlight(false);
                    structure.ToggleHighlight(true);
                    currentStructure = structure;

                    UI.ItemDescriptionLabel.Text = $"{item} (h={Heights[item.Id]})";

                    Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
                }

                break;

            case TopicIsland island:
            {
                currentStructure?.ToggleHighlight(false);
                currentStructure = null;

                currentIsland?.ToggleHighlight(false);
                island.ToggleHighlight(true);
                currentIsland = island;

                var issueCount = 0;
                var prCount = 0;
                var discussionCount = 0;
                foreach (var visibleItem in Heights.Where(p => p.Value > 0).Select(p => Repo.Items[p.Key])
                             .Where(i => i.TopicId == island.Topic.Id))
                {
                    switch (visibleItem.Conversation)
                    {
                        case Issue:
                            issueCount++;
                            break;
                        case PullRequest:
                            prCount++;
                            break;
                        case Discussion:
                            discussionCount++;
                            break;
                    }
                }

                UI.ItemDescriptionLabel.Text =
                    $"Topic #{island.Topic.Id}: {island.Topic.GetPreferredTitle()}, {issueCount} Issues, {prCount} PRs, {discussionCount} Discussions";
                break;
            }

            case null:
            {
                currentStructure?.ToggleHighlight(false);
                currentStructure = null;

                currentIsland?.ToggleHighlight(false);
                currentIsland = null;

                Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                var issueCount = 0;
                var prCount = 0;
                var discussionCount = 0;
                foreach (var visibleItem in Heights.Where(p => p.Value > 0).Select(p => Repo.Items[p.Key]))
                {
                    switch (visibleItem.Conversation)
                    {
                        case Issue:
                            issueCount++;
                            break;
                        case PullRequest:
                            prCount++;
                            break;
                        case Discussion:
                            discussionCount++;
                            break;
                    }
                }

                var now = Repo.MinDate + CurrentStep * StepLength;
                UI.ItemDescriptionLabel.Text =
                    $"[{now:yyyy-MM-dd}] {Repo.Mining.Repository.Name}, {issueCount} Issues, {prCount} PRs, {discussionCount} Discussions";
                break;
            }
        }
    }

    private void RefreshCurrentStepControls(int step)
    {
        var now = Repo.MinDate + StepLength * step;
        CurrentStep = step;
        UI.CurrentStepSpinBox.SetValueNoSignal(step);
        UI.CurrentDateTime.Text = now.ToString(DateTimeFormat);
    }

    private async Task OnScopeCheck(CheckButton button, VisualizationScope scope)
    {
        if (button.ButtonPressed)
        {
            CurrentScope |= scope;
        }
        else
        {
            CurrentScope &= ~scope;
        }

        await ShowStep(CurrentStep);
    }

    private TimeSpan GetSlidingWindowLength(SlidingWindowPreset preset)
    {
        return preset switch
        {
            SlidingWindowPreset.All => StepCount * StepLength,
            SlidingWindowPreset.Week => TimeSpan.FromDays(7),
            SlidingWindowPreset.Month => TimeSpan.FromDays(30),
            SlidingWindowPreset.Quarter => TimeSpan.FromDays(120),
            SlidingWindowPreset.HalfYear => TimeSpan.FromDays(180),
            SlidingWindowPreset.Year => TimeSpan.FromDays(365),
            SlidingWindowPreset.YearAndHalf => TimeSpan.FromDays(547),
            SlidingWindowPreset.TwentyMonths => TimeSpan.FromDays(600),
            SlidingWindowPreset.TwoYears => TimeSpan.FromDays(730),
            _ => throw new NotImplementedException()
        };
    }

    private void BindUserInterface()
    {
        UI.ItemDescriptionLabel.Text = DefaultHint;
        for (int i = 0; i < Datasets.Count; ++i)
        {
            UI.DatasetDropdown.AddItem(Datasets[i].Name, i);
        }

        foreach (var name in Enum.GetNames<SlidingWindowPreset>())
        {
            UI.SlidingWindowDropdown.AddItem(name, (int)Enum.Parse<SlidingWindowPreset>(name));
        }

        UI.SlidingWindowDropdown.ItemSelected += async i =>
        {
            var name = UI.SlidingWindowDropdown.GetItemText((int)i);
            SlidingWindowPreset = Enum.Parse<SlidingWindowPreset>(name);
            SlidingWindowLength = GetSlidingWindowLength(SlidingWindowPreset);

            await ShowStep(CurrentStep);
        };
        UI.SlidingWindowDropdown.Selected = UI.SlidingWindowDropdown.GetItemIndex((int)SlidingWindowPreset);

        UI.IssuesCheck.ButtonPressed = CurrentScope.HasFlag(VisualizationScope.Issues);
        UI.IssuesCheck.Pressed += async () => await OnScopeCheck(UI.IssuesCheck, VisualizationScope.Issues);
        UI.PRsCheck.ButtonPressed = CurrentScope.HasFlag(VisualizationScope.PullRequests);
        UI.PRsCheck.Pressed += async () => await OnScopeCheck(UI.PRsCheck, VisualizationScope.PullRequests);
        UI.DiscussionsCheck.ButtonPressed = CurrentScope.HasFlag(VisualizationScope.Discussions);
        UI.DiscussionsCheck.Pressed +=
            async () => await OnScopeCheck(UI.DiscussionsCheck, VisualizationScope.Discussions);
        UI.OnlyPopulatedIslandsCheck.ButtonPressed = ShowOnlyPopulatedIslands;
        UI.OnlyPopulatedIslandsCheck.Pressed += async () =>
        {
            ShowOnlyPopulatedIslands = UI.OnlyPopulatedIslandsCheck.IsPressed();
            await ShowStep(CurrentStep);
        };

        UI.DatasetDropdown.ItemSelected += async i => await ShowDataset((int)i);

        UI.CurrentStepSpinBox.ValueChanged += async value => await ShowStep(Mathf.FloorToInt(value));

        UI.CurrentDateTime.TextSubmitted += async text =>
        {
            if (DateTimeOffset.TryParseExact(
                    text,
                    [DateTimeFormat, "yyyy-MM-dd"],
                    formatProvider: null,
                    styles: DateTimeStyles.AssumeUniversal,
                    result: out var dateTime
                ))
            {
                var step = dateTime < Repo.MinDate ? 0
                    : dateTime >= Repo.MaxDate ? StepCount - 1
                    : Mathf.FloorToInt((dateTime - Repo.MinDate) / StepLength);
                await ShowStep(step);
            }
            else
            {
                RefreshCurrentStepControls(CurrentStep);
            }
        };

        UI.MaxNormalizedHeightSpinBox.ValueChanged += async mnh =>
        {
            MaxNormalizedHeight = (float)mnh;
            if (ShouldNormalizeHeights)
            {
                await ShowStep(CurrentStep);
            }
        };
        UI.MaxNormalizedHeightSpinBox.Value = MaxNormalizedHeight;
        UI.NormalizeHeightsCheck.Pressed += async () =>
        {
            ShouldNormalizeHeights = UI.NormalizeHeightsCheck.IsPressed();
            await ShowStep(CurrentStep);
        };
        UI.NormalizeHeightsCheck.ButtonPressed = ShouldNormalizeHeights;

        UI.StubsCheck.ButtonPressed = ShowClosedAsStubs;
        UI.StubsCheck.Pressed += async () =>
        {
            ShowClosedAsStubs = UI.StubsCheck.ButtonPressed;
            await ShowStep(CurrentStep);
        };

        UI.ShowTreesCheck.ButtonPressed = ShouldShowTrees;
        UI.ShowTreesCheck.Pressed += async () =>
        {
            ShouldShowTrees = UI.ShowTreesCheck.ButtonPressed;
            await ShowStep(CurrentStep);
        };
    }
}
