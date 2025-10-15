using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NetTopologySuite.Index.KdTree;
using Ritgard.Mining;

namespace Ritgard;

public class ActiveRepository
{
    public static readonly TimeSpan DefaultSlidingWindow = TimeSpan.FromDays(365);
    public static readonly TimeSpan DefaultStep = TimeSpan.FromDays(1);

    public DatasetInfo Dataset { get; private set; }

    public MiningResult Mining { get; private set; }

    public TopicModellingResult TopicModelling { get; private set; }

    public ImmutableDictionary<string, ActiveItem> Items { get; private set; }

    public KdTree<ActiveItem> ItemTree { get; private set; }

    public TimeSpan SlidingWindow { get; private set; } = DefaultSlidingWindow;

    public TimeSpan Step { get; private set; } = DefaultStep;

    public DateTimeOffset MinDate { get; private set; }

    public DateTimeOffset MaxDate { get; private set; }

    public TimeSpan AvgIssueLength { get; private set; }

    public int StepCount { get; private set; }

    public static async Task<ActiveRepository> Load(
        DatasetInfo dataset,
        TimeSpan? slidingWindow = null,
        TimeSpan? step = null
    )
    {
        var mining = Utils.ReadGodotJson<MiningResult>(dataset.DataFilePath);
        var topicModelling = Utils.ReadGodotJson<TopicModellingResult>(dataset.TopicFilePath);
        var bbox = new Rect2(
            topicModelling.Items.Values.Min(p => (float)p.X),
            topicModelling.Items.Values.Min(p => (float)p.Y),
            Vector2.Zero
        );
        foreach (var item in topicModelling.Items.Values)
        {
            bbox = bbox.Expand(new Vector2((float)item.X, (float)item.Y));
        }
        var center = bbox.GetCenter();

        var repo = new ActiveRepository
        {
            Dataset = dataset,
            Mining = mining,
            TopicModelling = topicModelling,
            Items = mining.Issues.ToImmutableDictionary(
                i => i.Key,
                i => ActiveItem.Create(
                    i.Value,
                    new Vector2(
                        x: (float)topicModelling.Items[i.Value.Id].X - center.X,
                        y: (float)topicModelling.Items[i.Value.Id].Y - center.Y
                    )
                )
            )
        };
        repo.SlidingWindow = slidingWindow ?? repo.SlidingWindow;
        repo.Step = step ?? repo.Step;
        repo.MaxDate = Ritgard.Mining.Utils.Max(
            mining.Repository.UpdatedAt ?? default,
            mining.Issues.Values.Max(i => i.UpdatedAt ?? i.CreatedAt)
        );
        repo.MinDate = Ritgard.Mining.Utils.Min(
            mining.Repository.CreatedAt,
            mining.Issues.Values.Min(i => i.UpdatedAt ?? i.CreatedAt)
        );
        repo.AvgIssueLength = TimeSpan.FromSeconds(mining.Issues.Values.Average(i => i.GetTimeSpan().TotalSeconds));
        repo.StepCount = Mathf.CeilToInt((repo.MaxDate - repo.MinDate) / repo.Step);

        var kdTree = new KdTree<ActiveItem>();
        foreach (var item in repo.Items.Values)
        {
            kdTree.Insert(
                new NetTopologySuite.Geometries.Coordinate(item.Position.X, item.Position.Y),
                item
            );
        }
        repo.ItemTree = kdTree;
        return repo;
    }
}
