import datatypes as dt
from pathlib import Path
import pandas as pd
import matplotlib.pyplot as plt
import datetime


def main():
    banned_topics = [80]
    model = dt.MiningResult.model_validate_json(Path("../vis/sample/git.json").read_bytes())
    topic_model = dt.TopicModellingResult.model_validate_json(Path("../vis/sample/git-topics.json").read_bytes())
    allowed_topics = {topic.id for topic in topic_model.topics.values() if topic.id not in banned_topics}
    # allowed_topics = {0, 1}
    fig, axes = plt.subplots(len(allowed_topics), 1, figsize=(40, 4 * len(allowed_topics)), sharex=False)
    for allowed_topic, ax in zip(allowed_topics, axes):
        # issues = pd.Series(pd.to_datetime(list({item.created_at for item in model.issues.values() if
        #                                         topic_model.items[item.id].topic_id == allowed_topic}.union(
        #     {c.created_at for item in model.issues.values() for c in item.comments if
        #      topic_model.items[item.id].topic_id == allowed_topic}))))
        # prs = pd.Series(pd.to_datetime(list({item.created_at for item in model.pull_requests.values() if
        #                                      topic_model.items[item.id].topic_id == allowed_topic}.union(
        #     {c.created_at for item in model.pull_requests.values() for c in item.comments if
        #      topic_model.items[item.id].topic_id == allowed_topic}))))
        # discussions = pd.Series(pd.to_datetime(list({item.created_at for item in model.discussions.values() if
        #                                              topic_model.items[item.id].topic_id == allowed_topic}.union(
        #     {c.created_at for item in model.discussions.values() for c in item.comments if
        #      topic_model.items[item.id].topic_id == allowed_topic}))))

        issues = pd.Series(pd.to_datetime(list({item.created_at for item in model.issues.values() if
                                                topic_model.items[item.id].topic_id == allowed_topic})))
        prs = pd.Series(pd.to_datetime(list({item.created_at for item in model.pull_requests.values() if
                                             topic_model.items[item.id].topic_id == allowed_topic})))
        discussions = pd.Series(pd.to_datetime(list({item.created_at for item in model.discussions.values() if
                                                     topic_model.items[item.id].topic_id == allowed_topic})))

        df = pd.DataFrame({
            "issue_dates": issues,
            "pr_dates": prs,
            "discussion_dates": discussions
        })
        for col in ['issue_dates', 'pr_dates', 'discussion_dates']:
            df[col + '_bin'] = df[col].dt.to_period('M').dt.start_time

        binned_counts = pd.DataFrame({
            'Issues': df['issue_dates_bin'].value_counts(),
            'PRs': df['pr_dates_bin'].value_counts(),
            'Discussions': df['discussion_dates_bin'].value_counts()
        }).fillna(0)
        full_range = pd.date_range(
            start=binned_counts.index.min(),
            end=binned_counts.index.max(),
            freq="MS"
        )
        binned_counts = binned_counts.reindex(full_range, fill_value=0)
        ax = binned_counts.plot(kind='bar', stacked=True, ax=ax)
        ax.set_title(topic_model.topics[allowed_topic].representations["LLM"][0][0])
        ax.set_xticklabels([x.strftime('%Y-%m-%d') for x in binned_counts.index])

    plt.tight_layout()
    plt.savefig("git_topics_plot_filled.png")
    # df["issues"] = pd.to_datetime(df["issues"]).to_period("period[M]")
    # df["prs"] = pd.to_datetime(df["prs"]).to_period("period[M]")
    # df["discussions"] = pd.to_datetime(df["discussions"]).to_period("period[M]")
    # # df['week'] = df['date'].dt.to_period('W')
    # df.plot.bar(stacked=True)
    # plt.show()
    # ax = df.plot.bar(stacked=True)


if __name__ == '__main__':
    main()
