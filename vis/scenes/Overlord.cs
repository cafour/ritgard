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
using System.Threading.Tasks;

namespace Ritgard;

public partial class Overlord : Node
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";

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
    public MeshInstance3D TopBorder { get; set; }

    [Export]
    public MeshInstance3D RightBorder { get; set; }

    [Export]
    public MeshInstance3D BottomBorder { get; set; }

    [Export]
    public MeshInstance3D LeftBorder { get; set; }

    public int CurrentDataset { get; set; } = -1;

    public int CurrentStep { get; private set; }

    public VisualizationScope CurrentScope { get; private set; } = VisualizationScope.All;

    public SlidingWindowPreset SlidingWindowPreset { get; private set; } = SlidingWindowPreset.Month;

    public TimeSpan SlidingWindowLength { get; private set; }

    public TimeSpan StepLength { get; private set; } = DefaultStep;

    public int StepCount { get; private set; }

    public bool ShowOnlyPopulatedIslands { get; private set; } = false;

    public ActiveRepository Repo { get; private set; }

    public Dictionary<string, float> Heights { get; } = [];

    private readonly Dictionary<int, TopicIsland> topicIslands = [];
    private readonly Dictionary<string, ItemStructure> itemStructures = [];
    private readonly Dictionary<string, OutlierRock> outlierRocks = [];
    private ItemStructure currentStructure;
    private TopicIsland currentIsland;
    private Node generatedNodesContainer;
    private Texture2D topicIdTexture;
    private Texture2D itemIdTexture;

    public const float ItemRadius = 256.0f;
    public const float StructureRadius = 10f;
    public const int HeightmapSize = 1024;
    public const string DefaultHint = "Hover over a structure to see its description...";
    public const byte MaxTerrainHeight = 75;
    public const float BorderWidth = 5f;

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
            var preset = Enum.Parse<SlidingWindowPreset>(name);
            SlidingWindowLength = GetSlidingWindowLength(preset);

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
        await ShowDataset(0);

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

        var dataset = Datasets[index];
        Repo = await ActiveRepository.Load(dataset);
        StepCount = Mathf.CeilToInt((Repo.MaxDate - Repo.MinDate) / StepLength);
        // NB: if preset is All, we have to recompute it
        SlidingWindowLength = GetSlidingWindowLength(SlidingWindowPreset);
        UI.CurrentStepSpinBox.MaxValue = StepCount - 1;

        var oceanPlane = (PlaneMesh)Ocean.Mesh;
        oceanPlane.Size = Repo.BBox.Size;
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
            topicIsland.Topic = topic;
            generatedNodesContainer.AddChild(topicIsland);
            topicIslands[topicId] = topicIsland;
        }

        // var firstTopic = Topics.Values.OrderBy(t => t.Title).First();
        // var topicIsland = TopicIslandScene.Instantiate<TopicIsland>();
        // topicIsland.Topic = firstTopic;
        // AddChild(topicIsland);

        // foreach (var (identifier, item) in data)
        // {
        //     var edgeCount = edgeCounts.GetValueOrDefault(Utils.Coalesce(item.AbsoluteLink, item.Url));
        //     if (edgeCount == 0)
        //     {
        //         continue;
        //     }
        //     var position = itemPositions[identifier] + new Vector2I(256, 256);
        //     generator.Heightmap.FillRect(
        //         new Rect2I(position - new Vector2I(3, 3), new Vector2I(5, 5)),
        //         new Color() { R8 = Math.Clamp(edgeCount, 0, 255) }
        //     );
        // }

        // ComputeHeighmapCircles(Mathf.RoundToInt(StructureRadius));

        foreach (var item in Repo.Items.Values)
        {
            var instance = ItemStructureScene.Instantiate<ItemStructure>();
            instance.Item = item;
            generatedNodesContainer.AddChild(instance);
            itemStructures[item.Id] = instance;

            if (Repo.TopicModelling.Items[item.Id].TopicId == -1)
            {
                var outlierRock = OutlierRockScene.Instantiate<OutlierRock>();
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
        foreach (var item in Repo.Items.Values)
        {
            if (!item.Conversation.IsInScope(CurrentScope))
            {
                Heights[item.Id] = 0;
            }
            else
            {
                var slidingEvents = Repo.Items[item.Id].Events.GetRange(now - SlidingWindowLength, now, true);
                Heights[item.Id] = slidingEvents.Count();
            }

            itemStructures.GetValueOrDefault(item.Id)?.OnShowStep(step);
            outlierRocks.GetValueOrDefault(item.Id)?.OnShowStep(step);
        }

        foreach (var topicIsland in topicIslands.Values)
        {
            topicIsland.Scope = CurrentScope;
            topicIsland.ShowOnlyWhenPopulated = ShowOnlyPopulatedIslands;
        }

        await Task.WhenAll(topicIslands.Values.Select(i => Task.Run(i.ComputeHeightmap)));

        foreach (var topicIsland in topicIslands.Values)
        {
            topicIsland.UpdatePlane();
        }

        RefreshCurrentStepControls(step);
    }

    public override async void _Input(InputEvent @event)
    {
        if (@event.IsAction("interact") && @event.IsPressed() && currentStructure is not null)
        {
            var item = Repo.Items.GetValueOrDefault(currentStructure.Item.Id);
            if (!string.IsNullOrEmpty(item.Conversation.Url))
            {
                OS.ShellOpen(item.Conversation.Url);
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
    }

    // private void ComputeHeighmapTriangularization()
    // {
    //     var inversePositions = Positions.ToImmutableDictionary(
    //         t => new Coordinate(t.Value.X, t.Value.Y),
    //         t => t.Key
    //     );
    //     var triangulationBuilder = new DelaunayTriangulationBuilder();
    //     triangulationBuilder.SetSites([.. inversePositions.Keys]);
    //     var subdivision = triangulationBuilder.GetSubdivision();
    //     var dateLength = MaxDate - MinDate;

    //     double GetHeightAtPoint(double x, double y)
    //     {
    //         if (!inversePositions.TryGetValue(new Coordinate(x, y), out var id))
    //         {
    //             return 0.0;
    //         }
    //         var issue = Data[id];
    //         var date = issue.UpdatedAt ?? issue.CreatedAt;
    //         return (date - MinDate) / dateLength * 100.0;
    //     }

    //     for (int y = -HeightmapSize / 2; y < HeightmapSize / 2; ++y)
    //     {
    //         for (int x = -HeightmapSize / 2; x < HeightmapSize / 2; ++x)
    //         {
    //             var hx = x + HeightmapSize / 2;
    //             var hy = y + HeightmapSize / 2;
    //             QuadEdge? edge = null;
    //             try
    //             {
    //                 edge = subdivision.Locate(new Coordinate(x, y));
    //             }
    //             catch (LocateFailureException)
    //             {
    //             }

    //             if (edge is null)
    //             {
    //                 // generator.Heightmap.SetPixel(hx, hy, new Color() { R8 = 0 });
    //                 // generator.Heightmap[hy, hx] = 0;
    //                 continue;
    //             }

    //             var p1 = edge.Orig;
    //             var p2 = edge.Dest;
    //             var p3 = edge.ONext.Dest;
    //             var height = InterpolateBarycentric(
    //                 x, y,
    //                 p1.X, p1.Y, GetHeightAtPoint(p1.X, p1.Y),
    //                 p2.X, p2.Y, GetHeightAtPoint(p2.X, p2.Y),
    //                 p3.X, p3.Y, GetHeightAtPoint(p3.X, p3.Y)
    //             );
    //             height = Mathf.Max(height, 0.0);
    //             // generator.Heightmap.SetPixel(hx, hy, new Color { R8 = Mathf.RoundToInt(height) });
    //             // generator.Heightmap[hy, hx] = (byte)Mathf.RoundToInt(height);
    //         }
    //     }
    // }

    // private void ComputeHeighmapCircles(int circleRadius)
    // {
    //     var maxDate = Data.Values.Max(i => i.UpdatedAt ?? i.CreatedAt);
    //     var minDate = Data.Values.Min(i => i.UpdatedAt ?? i.CreatedAt);
    //     var dateLength = maxDate - minDate;

    //     var radiusSquared = circleRadius * circleRadius;
    //     foreach (var (id, pos) in Positions)
    //     {
    //         // var height = GetHeightForIssue(id);
    //         var height = (byte)(GetLevelForIssue(id) * 10);
    //         for (int y = -circleRadius + 1; y < circleRadius; ++y)
    //         {
    //             for (int x = -circleRadius + 1; x < circleRadius; ++x)
    //             {
    //                 var hx = Mathf.RoundToInt(pos.X) + HeightmapSize / 2 + x;
    //                 var hy = Mathf.RoundToInt(pos.Y) + HeightmapSize / 2 + y;

    //                 if (x * x + y * y < radiusSquared + 1)
    //                 {
    //                     // generator.Heightmap[hy, hx] = Math.Max(generator.Heightmap[hy, hx], height);
    //                 }
    //                 else
    //                 {
    //                     // generator.Heightmap[hy, hx] = Math.Max(generator.Heightmap[hy, hx], (byte)0);
    //                 }
    //             }
    //         }
    //     }
    // }

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
                currentStructure?.ToggleHighlight(false);
                currentStructure = null;

                currentIsland?.ToggleHighlight(false);
                island.ToggleHighlight(true);
                currentIsland = island;

                UI.ItemDescriptionLabel.Text = $"Topic #{island.Topic.Id}: {island.Topic.GetPreferredTitle()}";
                break;

            case null:
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

    public void _OnMeshBlockEntered(Vector3I blockPos)
    {
        return;
        // var origin = terrain.DataBlockToVoxel(blockPos);
        // var structCount = rng.RandiRange(0, 2);
        // if (structCount == 0)
        // {
        //     return;
        // }

        // var set = new HashSet<Node3D>();
        // for (int i = 0; i < structCount; ++i)
        // {
        //     var position = new Vector3I(
        //         rng.RandiRange(0, (int)terrain.MeshBlockSize - 1),
        //         0,
        //         rng.RandiRange(0, (int)terrain.MeshBlockSize - 1)
        //     );
        //     var height = generator.GetHeight(origin.X + position.X, origin.Z + position.Z);
        //     if (height > origin.Y + (int)terrain.MeshBlockSize || height < origin.Y)
        //     {
        //         continue;
        //     }

        //     var instance = TestStructure.Instantiate<Node3D>();
        //     instance.Position = new Vector3(origin.X + position.X, height, origin.Z + position.Z);
        //     AddChild(instance);
        //     set.Add(instance);
        // }
        // structures.AddOrUpdate(blockPos, _ => set, (_, e) =>
        // {
        //     foreach (var existing in e)
        //     {
        //         e.Remove(existing);
        //         RemoveChild(existing);
        //         existing.QueueFree();
        //     }
        //     return set;
        // });
    }

    public void _OnMeshBlockExited(Vector3I blockPos)
    {
        return;
        // var set = structures.GetValueOrDefault(blockPos);
        // if (set is null || set.Count == 0)
        // {
        //     return;
        // }

        // foreach (var existing in set)
        // {
        //     set.Remove(existing);
        //     RemoveChild(existing);
        //     existing.QueueFree();
        // }
    }

    private void RefreshCurrentStepControls(int step)
    {
        var now = Repo.MinDate + StepLength * step;
        CurrentStep = step;
        UI.CurrentStepSpinBox.SetValueNoSignal(step);
        UI.CurrentDateTime.Text = now.ToString(DateTimeFormat);
    }

    // private byte GetHeightForIssue(long id)
    // {
    //     var issue = Data[id];
    //     var date = issue.UpdatedAt ?? issue.CreatedAt;
    //     var dateLength = MaxDate - MinDate;
    //     return (byte)Mathf.RoundToInt(Math.Clamp((date - MinDate) / dateLength * MaxTerrainHeight, 0.0, 255.0));
    // }

    // private byte GetLevelForIssue(long id)
    // {
    //     var issue = Data[id];
    //     var date = issue.UpdatedAt ?? issue.CreatedAt;
    //     if (date.Date == MaxDate.Date)
    //     {
    //         return 50;
    //     }
    //     else if (date > MaxDate - TimeSpan.FromDays(7))
    //     {
    //         return 40;
    //     }
    //     else if (date > MaxDate - TimeSpan.FromDays(30))
    //     {
    //         return 30;
    //     }
    //     else if (date > MaxDate - TimeSpan.FromDays(365))
    //     {
    //         return 20;
    //     }
    //     else
    //     {
    //         return 10;
    //     }
    // }

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
            _ => throw new NotImplementedException()
        };
    }
}
