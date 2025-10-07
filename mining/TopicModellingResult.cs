using System.Collections.Immutable;

namespace Ritgard.Mining;

public record TopicModellingResult(
    ImmutableDictionary<int, Topic> Topics,
    ImmutableDictionary<long, TopicItem> Items
);
