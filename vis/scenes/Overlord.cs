using Godot;
using Ritgard.Mining;
using Ritgard.Structures;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Ritgard.Voxel;
using Ritgard.WorldGenerator;

namespace Ritgard;

public partial class Overlord : Node
{
    public const double SteppingCooldown = 0.5;
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const float DefaultMaxNormalizedHeight = 42f;
    public const string DefaultHint = "Hover over a structure to see its description...";
    public const string DatasetErrorHint = "No datasets found. Check 'DataPath' in appsettings.json.";
    public const float BorderWidth = 5f;
    public const float SunRotationStep = Mathf.Pi / 6;

    public static readonly ImmutableArray<SlidingWindowPreset> SlidingWindowOptions =
    [
        SlidingWindowPreset.All,
        SlidingWindowPreset.Week,
        SlidingWindowPreset.Month,
        SlidingWindowPreset.Quarter,
        SlidingWindowPreset.HalfYear,
        SlidingWindowPreset.Year,
        SlidingWindowPreset.YearAndHalf,
        SlidingWindowPreset.TwentyMonths,
        SlidingWindowPreset.TwoYears,
    ];

    public static Overlord Instance { get; private set; } = null!;

    [Export]
    public PackedScene ItemStructureScene { get; set; } = null!;

    [Export]
    public PackedScene TopicIslandScene { get; set; } = null!;

    [Export]
    public PackedScene OutlierRockScene { get; set; } = null!;

    [Export]
    public Player Player { get; set; } = null!;

    [Export]
    // ReSharper disable once InconsistentNaming
    public UIWrapper UI { get; set; } = null!;

    [Export]
    public VoxelBlockLibrary Library { get; set; } = null!;

    [Export]
    public MeshInstance3D Ocean { get; set; } = null!;

    [Export]
    public DirectionalLight3D Sun { get; set; } = null!;

    /// <summary>
    /// Exists to prevent the ocean from being semi-transparent on clean screenshots.
    /// </summary>
    [Export]
    public MeshInstance3D DeepOcean { get; set; } = null!;

    [Export]
    public MeshInstance3D TopBorder { get; set; } = null!;

    [Export]
    public MeshInstance3D RightBorder { get; set; } = null!;

    [Export]
    public MeshInstance3D BottomBorder { get; set; } = null!;

    [Export]
    public MeshInstance3D LeftBorder { get; set; } = null!;

    [Export]
    public WorldEnvironment Environment { get; set; } = null!;

    public int CurrentDataset { get; set; } = -1;

    public int CurrentStep { get; private set; }

    public int CurrentStepMaxHeight { get; private set; }

    public DateTimeOffset Now => Repo is not null
        ? Repo.MinDate + CurrentStep * SingleStepLength
        : throw new InvalidOperationException("Now is only available when a repo is loaded.");

    // NB: Currently not configurable at runtime.
    public TimeSpan SingleStepLength => Repo?.Terrain?.StepLength ?? TerrainGenerator.DefaultStepLength;

    public int StepLength { get; set; } = 1;

    public int StepLengthMultiplier { get; private set; } = 1;

    public ConversationScope CurrentScope { get; private set; } = ConversationScope.All;

    public float CurrentHeightScale { get; private set; } = 1.0f;

    public SlidingWindowPreset SlidingWindowPreset { get; private set; } = SlidingWindowPreset.Year;

    public TimeSpan SlidingWindowLength { get; private set; }

    public int StepCount { get; private set; }

    public bool ShowClosedAsStubs { get; private set; } = false;

    public bool ShowOnlyPopulatedIslands { get; private set; } = false;

    public bool ShouldNormalizeHeights { get; private set; } = false;

    public bool ShouldShowTrees { get; private set; } = true;

    public float MaxNormalizedHeight { get; private set; } = DefaultMaxNormalizedHeight;

    public ActiveRepository? Repo { get; private set; }
    public TerrainPreset? CurrentTerrain { get; private set; }

    public float CameraDistance { get; private set; } = 100f;

    public Dictionary<string, float> Heights { get; } = [];

    private IConfiguration configuration = null!;
    private VisualizationOptions options = null!;
    private ImmutableArray<DatasetInfo> datasets = [];
    private readonly Dictionary<int, TopicIsland> topicIslands = [];
    private readonly Dictionary<string, ItemStructure> itemStructures = [];
    private readonly Dictionary<string, OutlierRock> outlierRocks = [];
    private ItemStructure? currentStructure;
    private TopicIsland? currentIsland;
    private Node generatedNodesContainer = null!;
    private TerrainGenerator? generator;

    private IMemoryCache heightmapCache = new MemoryCache(
        new MemoryCacheOptions
        {
            SizeLimit = 4_000_000_000
        }
    );

    // private Texture2D topicIdTexture;
    // private Texture2D itemIdTexture;
    // private CancellationTokenSource? lastHeightmapCts = null;
    private double steppingStartedAt = -1;

    public override void _EnterTree()
    {
        Instance?.QueueFree();
        Instance = this;

        generatedNodesContainer = GetNode<Node>("GeneratedNodesContainer");

        Library.Bake();

        configuration = Mining.Utils.BuildConfiguration();
        options = new VisualizationOptions();
        configuration.Bind(options);

        options.DataPath ??= "./datasets";
        var dataPath = FindDataPath(options.DataPath);
        if (dataPath is null)
        {
            var dotnetDataDir = new DirectoryInfo(".").EnumerateDirectories()
                .First(d => d.Name.StartsWith("data_Ritgard")).FullName;
            GD.Print($"Found .NET data directory in '{dotnetDataDir}'.");
            dataPath = FindDataPath(Path.Combine(dotnetDataDir, options.DataPath));
        }

        GD.Print($"Found '{dataPath}'.");
        if (dataPath is null)
        {
            throw new ArgumentException(
                $"Set a valid 'DataPath' in the tool's 'appsettings.json' file. "
                + $"Could not find '{options.DataPath}' in '{new DirectoryInfo(".").FullName}' or any parent directory."
            );
        }

        datasets = Utils.DiscoverDatasets(dataPath);
        GD.Print($"Discovered {datasets.Length} datasets in '{dataPath}'.");
    }

    public override async void _Ready()
    {
        BindUserInterface();

        await ShowDataset(0);
    }

    public override async void _Process(double delta)
    {
        var justStarted = Input.IsActionJustPressed(InputActions.LargeStepNext)
            || Input.IsActionJustPressed(InputActions.LargeStepPrev) || Input.IsActionJustPressed(InputActions.StepPrev)
            || Input.IsActionJustPressed(InputActions.StepNext);
        if (justStarted)
        {
            steppingStartedAt = Time.GetUnixTimeFromSystem();
        }

        if (Input.IsActionPressed(InputActions.LargeStepNext))
        {
            await ShowStep(
                CurrentStep + Mathf.RoundToInt(SlidingWindowLength / SingleStepLength),
                checkCooldown: !justStarted
            );
        }
        else if (Input.IsActionPressed(InputActions.LargeStepPrev))
        {
            await ShowStep(
                CurrentStep - Mathf.RoundToInt(SlidingWindowLength / SingleStepLength),
                checkCooldown: !justStarted
            );
        }
        else if (Input.IsActionPressed(InputActions.StepNext))
        {
            await ShowStep(CurrentStep + StepLength, checkCooldown: !justStarted);
        }
        else if (Input.IsActionPressed(InputActions.StepPrev))
        {
            await ShowStep(CurrentStep - StepLength, checkCooldown: !justStarted);
        }
    }

    private static string? FindDataPath(string dataPath)
    {
        var current = new DirectoryInfo(dataPath);
        while (!current.Exists)
        {
            var parentDir = current.Parent?.Parent?.FullName;
            if (parentDir is null)
            {
                break;
            }

            current = new DirectoryInfo(Path.Combine(parentDir, current.Name));
        }

        return current.Exists ? current.FullName : null;
    }

    private async Task ShowDataset(int index, CancellationToken ct = default)
    {
        if (CurrentDataset == index)
        {
            return;
        }

        UI.LoadingBox.Visible = true;

        CurrentDataset = index;

        var oldNodes = generatedNodesContainer.GetChildren();
        foreach (var old in oldNodes)
        {
            old.QueueFree();
            generatedNodesContainer.RemoveChild(old);
        }

        topicIslands.Clear();
        itemStructures.Clear();

        var dataset = datasets[index];
        await WithLoading(async () => { Repo = await dataset.Load(ct); });
        if (Repo is null)
        {
            throw new InvalidOperationException($"Failed to load repo at index '{index}'.");
        }

        StepCount = Math.Max(1, Mathf.CeilToInt((Repo.MaxDate - Repo.MinDate) / SingleStepLength));
        CurrentStep = Math.Min(CurrentStep, StepCount - 1);
        CurrentScope = ConversationScope.All;

        if (generator is not null)
        {
            generator.Dispose();
        }

        generator = new TerrainGenerator(Repo, Utils.LoggerFactory);
        await PrepareTerrain(CurrentStep, ct);

        CameraDistance = Mathf.Sqrt(Repo.BBoxSize.X * Repo.BBoxSize.X + Repo.BBoxSize.Y * Repo.BBoxSize.Y);
        Player.MovementMode.ResetCamera();
        Heights.Clear();

        // NB: if preset is All, we have to recompute it
        SlidingWindowLength = SlidingWindowPreset == SlidingWindowPreset.All
            ? Math.Ceiling((Repo.MaxDate - Repo.MinDate) / SingleStepLength) * SingleStepLength
            : SlidingWindowPreset.ToTimeSpan();
        UI.CurrentStepSpinBox.MaxValue = StepCount - 1;

        var stepLengthMultiplier = SingleStepLength / TimeSpan.FromDays(1);
        if (stepLengthMultiplier % 1 > double.Epsilon)
        {
            throw new InvalidOperationException("Step length must be a multiple of one day!");
        }

        stepLengthMultiplier = Math.Floor(stepLengthMultiplier);
        StepLengthMultiplier = (int)stepLengthMultiplier;
        UI.StepLengthSpinBox.Step = stepLengthMultiplier;
        UI.StepLengthSpinBox.MinValue = stepLengthMultiplier;
        UI.StepLengthSpinBox.Value = Math.Min(
            stepLengthMultiplier,
            Math.Round(UI.StepLengthSpinBox.Value / stepLengthMultiplier) * stepLengthMultiplier
        );

        ((PlaneMesh)Ocean.Mesh).Size = Repo.BBoxSize.ToGodot();
        ((PlaneMesh)DeepOcean.Mesh).Size = Repo.BBoxSize.ToGodot() + new Vector2(BorderWidth, BorderWidth);
        ((PlaneMesh)TopBorder.Mesh).Size = new Vector2(Repo.BBoxSize.X + BorderWidth * 2, BorderWidth);
        TopBorder.Position = TopBorder.Position with { Z = -Repo.BBoxSize.Y / 2 };
        ((PlaneMesh)RightBorder.Mesh).Size = new Vector2(BorderWidth, Repo.BBoxSize.Y + BorderWidth * 2);
        RightBorder.Position = RightBorder.Position with { X = Repo.BBoxSize.X / 2 };
        ((PlaneMesh)BottomBorder.Mesh).Size = new Vector2(Repo.BBoxSize.X + BorderWidth * 2, BorderWidth);
        BottomBorder.Position = BottomBorder.Position with { Z = Repo.BBoxSize.Y / 2 };
        ((PlaneMesh)LeftBorder.Mesh).Size = new Vector2(BorderWidth, Repo.BBoxSize.Y + BorderWidth * 2);
        LeftBorder.Position = LeftBorder.Position with { X = -Repo.BBoxSize.X / 2 };

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

        await ShowStep(StepCount - 1);
        UI.LoadingBox.Visible = false;
    }

    private async Task ShowStep(int step, bool checkCooldown = false)
    {
        if (checkCooldown && Time.GetUnixTimeFromSystem() - steppingStartedAt < SteppingCooldown)
        {
            return;
        }

        if (Repo is null)
        {
            GD.PushWarning($"No dataset selected. Cannot show step '{step}'.");
            return;
        }

        step = Math.Clamp(step, 0, StepCount - 1);

        await PrepareTerrain(step)
            .ConfigureAwait(continueOnCapturedContext: true);

        var now = Repo.MinDate + SingleStepLength * step;
        var slidingEvents = Repo.Items.Values.ToImmutableDictionary(
            v => v.Id,
            v => v.Events.GetRange(now - SlidingWindowLength, now, true).Count()
        );
        var maxEventCount = slidingEvents.Values.Max();
        CurrentHeightScale = ShouldNormalizeHeights && maxEventCount > 0 ? MaxNormalizedHeight / maxEventCount : 1f;
        foreach (var item in Repo.Items.Values)
        {
            if (!item.Conversation.IsInScope(CurrentScope))
            {
                Heights[item.Id] = 0;
            }
            else
            {
                Heights[item.Id] = slidingEvents[item.Id];
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

        foreach (var topicIsland in topicIslands.Values)
        {
            topicIsland.UpdatePlane(step, CurrentHeightScale);
        }

        // if (lastHeightmapTask is not null && !lastHeightmapTask.IsCompleted)
        // {
        //     GD.Print("Cancelling previous heightmap task.");
        //     if (lastHeightmapCts is not null)
        //     {
        //         await lastHeightmapCts.CancelAsync();
        //         lastHeightmapCts.Dispose();
        //     }
        //
        //     lastHeightmapCts = null;
        //     lastHeightmapTask = null;
        // }
        //
        // var currentHeightmapCts = new CancellationTokenSource();
        // var currentHeightmapTask = Task.WhenAll(
        //     topicIslands.Values.Select(i => Task.Run(
        //             () => i.ComputeHeightmap(currentHeightmapCts.Token),
        //             currentHeightmapCts.Token
        //         )
        //     )
        // );
        // lastHeightmapTask = currentHeightmapTask;
        // lastHeightmapCts = currentHeightmapCts;
        //
        // try
        // {
        //     await currentHeightmapTask;
        //     foreach (var topicIsland in topicIslands.Values)
        //     {
        //         topicIsland.UpdatePlane();
        //     }
        // }
        // catch (TaskCanceledException)
        // {
        //     // nop
        // }
    }

    public override void _Input(InputEvent @event)
    {
        var focusedControl = GetViewport().GuiGetFocusOwner();
        if (focusedControl is not null)
        {
            return;
        }

        if (@event.IsAction("interact") && @event.IsPressed())
        {
            if (currentStructure is not null && Repo is not null)
            {
                var item = Repo.Items.GetValueOrDefault(currentStructure.Item.Id);
                if (!string.IsNullOrEmpty(item?.Conversation.Url))
                {
                    OS.ShellOpen(item.Conversation.Url);
                }
            }
        }

        if (@event.IsAction(InputActions.RotateSun) && @event.IsPressed())
        {
            Sun.RotateY(SunRotationStep);
        }

        if (@event.IsAction(InputActions.PrintIslands) && @event.IsPressed())
        {
            if (Repo is null)
            {
                GD.PushWarning("No dataset selected. Cannot print a list of topics.");
                return;
            }

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

    private void OnHoverChanged(CollisionObject3D? hoveree)
    {
        if (Repo is null)
        {
            return;
        }

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

                    UI.ItemDescriptionLabel.Text = $"{item} (activity={Heights[item.Id]})";

                    Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
                }

                break;

            case TopicIsland island:
            {
                if (island.Topic is null)
                {
                    break;
                }

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

                var now = Repo.MinDate + CurrentStep * SingleStepLength;
                UI.ItemDescriptionLabel.Text =
                    $"[{now:yyyy-MM-dd}] {Repo.Mining.Repository.Name}, {issueCount} Issues, {prCount} PRs, {discussionCount} Discussions";
                break;
            }
        }
    }

    private void RefreshCurrentStepControls(int step)
    {
        if (Repo is null)
        {
            GD.PushWarning("No dataset is selected. Cannot refresh step controls.");
            return;
        }

        var now = Repo.MinDate + SingleStepLength * step;
        CurrentStep = step;
        UI.CurrentStepSpinBox.SetValueNoSignal(step);
        UI.CurrentDateTime.Text = now.ToString(DateTimeFormat);
        UI.MaxStepLabel.Text = $"/ {StepCount - 1}";

        if (currentIsland is null && currentStructure is null)
        {
            OnHoverChanged(null);
        }
    }

    private async Task OnScopeCheck(CheckButton button, ConversationScope scope)
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

    private void BindUserInterface()
    {
        UI.ItemDescriptionLabel.Text = DefaultHint;
        if (datasets.IsDefaultOrEmpty)
        {
            UI.ItemDescriptionLabel.Text = DatasetErrorHint;
        }

        for (int i = 0; i < datasets.Length; ++i)
        {
            UI.DatasetDropdown.AddItem(datasets[i].Name, i);
        }

        foreach (var option in SlidingWindowOptions)
        {
            var name = Enum.GetName(option) ?? throw new NotImplementedException();
            UI.SlidingWindowDropdown.AddItem(name, (int)Enum.Parse<SlidingWindowPreset>(name));
        }

        UI.SlidingWindowDropdown.ItemSelected += async i =>
        {
            if (Repo is null)
            {
                GD.PushWarning("No repo selected. Cannot choose the sliding window length.");
                return;
            }

            var name = UI.SlidingWindowDropdown.GetItemText((int)i);
            SlidingWindowPreset = Enum.Parse<SlidingWindowPreset>(name);
            // NB: if preset is All, we have to recompute it
            SlidingWindowLength = SlidingWindowPreset == SlidingWindowPreset.All
                ? Math.Ceiling((Repo.MaxDate - Repo.MinDate) / SingleStepLength) * SingleStepLength
                : SlidingWindowPreset.ToTimeSpan();

            await ShowStep(CurrentStep);
        };
        UI.SlidingWindowDropdown.Selected = UI.SlidingWindowDropdown.GetItemIndex((int)SlidingWindowPreset);

        UI.IssuesCheck.ButtonPressed = CurrentScope.HasFlag(ConversationScope.Issues);
        UI.IssuesCheck.Pressed += async () => await OnScopeCheck(UI.IssuesCheck, ConversationScope.Issues);
        UI.PRsCheck.ButtonPressed = CurrentScope.HasFlag(ConversationScope.PullRequests);
        UI.PRsCheck.Pressed += async () => await OnScopeCheck(UI.PRsCheck, ConversationScope.PullRequests);
        UI.DiscussionsCheck.ButtonPressed = CurrentScope.HasFlag(ConversationScope.Discussions);
        UI.DiscussionsCheck.Pressed +=
            async () => await OnScopeCheck(UI.DiscussionsCheck, ConversationScope.Discussions);
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
            if (Repo is null)
            {
                GD.PushWarning("No dataset is selected. Cannot change step.");
                return;
            }

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
                    : Mathf.FloorToInt((dateTime - Repo.MinDate) / SingleStepLength);
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

        UI.StepLengthSpinBox.Value = StepLength;
        UI.StepLengthSpinBox.ValueChanged += value => { StepLength = Mathf.RoundToInt(value) / StepLengthMultiplier; };
    }

    private async Task PrepareTerrain(int step, CancellationToken ct = default)
    {
        if (Repo is null || generator is null)
        {
            throw new InvalidOperationException(
                "Cannot generate terrain when no repo is loaded or no generator ready."
            );
        }

        var foundTerrain =
            Repo.Terrain?.Terrains.SingleOrDefault(p =>
                p.Scope == CurrentScope && p.SlidingWindow == SlidingWindowPreset
            );
        if (Repo.Terrain is not null
            && foundTerrain is not null
            && step >= Repo.Terrain.StartStep
            && step < Repo.Terrain.StartStep + Repo.Terrain.StepCount
           )
        {
            if (CurrentTerrain == foundTerrain)
            {
                return;
            }

            CurrentTerrain = foundTerrain;
        }
        else
        {
            await WithLoading(() => Task.Run(
                    () =>
                    {
                        CurrentTerrain = generator.Generate(
                                scope: CurrentScope,
                                slidingWindowPresets: SlidingWindowPreset,
                                batchSize: -1,
                                startStep: step,
                                stepCount: 1,
                                ct: ct
                            )
                            .Terrains
                            .SingleOrDefault();
                    },
                    ct
                )
            );
        }

        foreach (var island in topicIslands.Values)
        {
            island.InitializePlane();
        }
    }

    private async Task<T> WithLoading<T>(Func<Task<T>> action)
    {
        var originalValue = UI.LoadingBox.Visible;
        UI.LoadingBox.Visible = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var result = await action().ConfigureAwait(continueOnCapturedContext: true);

        if (UI.LoadingBox.Visible)
        {
            UI.LoadingBox.Visible = originalValue;
        }

        return result;
    }

    private async Task WithLoading(Func<Task> action)
    {
        _ = await WithLoading(async () =>
            {
                await action().ConfigureAwait(continueOnCapturedContext: true);
                return 0;
            }
        ).ConfigureAwait(continueOnCapturedContext: true);
    }

    public IslandHeightmap GetHeightmap(int topicId, int? step = null)
    {
        if (CurrentTerrain is null || Repo is null)
        {
            throw new InvalidOperationException("No terrain is currently available.");
        }

        if (Repo.Terrain is null || Repo.Terrain.BatchSize == -1 || step is null)
        {
            return GetOrDecompressHeightmap(topicId, 0);
        }

        var index = (step.Value - Repo.Terrain.StartStep) / Repo.Terrain.BatchSize;
        return GetOrDecompressHeightmap(topicId, index);
    }

    private IslandHeightmap GetOrDecompressHeightmap(int topicId, int heightmapIndex)
    {
        if (CurrentTerrain is null)
        {
            throw new InvalidOperationException("No terrain is available.");
        }

        var cacheKey = new HeightmapCacheKey(
            DatasetIndex: CurrentDataset,
            SlidingWindow: CurrentTerrain.SlidingWindow,
            Scope: CurrentTerrain.Scope,
            TopicId: topicId,
            HeightmapIndex: heightmapIndex
        );

        if (heightmapCache.TryGetValue<IslandHeightmap>(cacheKey, out var heightmap))
        {
            return heightmap;
        }

        if (!CurrentTerrain.IslandHeightmaps.TryGetValue(topicId, out var heightmaps))
        {
            throw new ArgumentException($"Topic id '{topicId}' does not exist.");
        }

        if (heightmaps.Length == 0)
        {
            throw new ArgumentException($"Topic id '{topicId}' has no heightmaps.");
        }

        if (heightmapIndex >= heightmaps.Length)
        {
            throw new ArgumentException(
                $"Index '{heightmapIndex}' is outside the bounds of the available batch of heightmaps."
            );
        }

        return heightmapCache.GetOrCreate(
            cacheKey,
            entry =>
            {
                var islandHeightmap = IslandHeightmap.ReadFromString(heightmaps[heightmapIndex]);
                entry.Size = islandHeightmap.SizeX * islandHeightmap.SizeY * islandHeightmap.StepCount;
                return islandHeightmap;
            }
        );
    }

    private record HeightmapCacheKey(
        int DatasetIndex,
        SlidingWindowPreset SlidingWindow,
        ConversationScope Scope,
        int TopicId,
        int HeightmapIndex
    );
}
