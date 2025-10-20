import datatypes as dt
from pathlib import Path
import pandas as pd
import matplotlib.pyplot as plt
import datetime


def main():
    model = dt.MiningResult.model_validate_json(Path("./datasets/git.json").read_bytes())
    issues = pd.Series(pd.to_datetime(list({item.created_at for item in model.issues.values()})))
    prs = pd.Series(pd.to_datetime(list({item.created_at for item in model.pull_requests.values()})))
    discussions = pd.Series(pd.to_datetime(list({item.created_at for item in model.discussions.values()})))

    df = pd.DataFrame({
        "issue_dates": issues,
        "pr_dates": prs,
        "discussion_dates": discussions
    })
    for col in ['issue_dates', 'pr_dates', 'discussion_dates']:
        df[col + '_week'] = df[col].dt.to_period('W').dt.start_time

    weekly_counts = pd.DataFrame({
        'Issues': df['issue_dates_week'].value_counts(),
        'PRs': df['pr_dates_week'].value_counts(),
        'Discussions': df['discussion_dates_week'].value_counts()
    }).fillna(0).sort_index()
    ax = weekly_counts.plot(kind='bar', stacked=True, figsize=(50, 6))
    ax.set_xticklabels([x.strftime('%Y-%m-%d') for x in weekly_counts.index])
    plt.tight_layout()
    plt.show()
    # df["issues"] = pd.to_datetime(df["issues"]).to_period("period[M]")
    # df["prs"] = pd.to_datetime(df["prs"]).to_period("period[M]")
    # df["discussions"] = pd.to_datetime(df["discussions"]).to_period("period[M]")
    # # df['week'] = df['date'].dt.to_period('W')
    # df.plot.bar(stacked=True)
    # plt.show()
    # ax = df.plot.bar(stacked=True)

if __name__ == '__main__':
    main()
