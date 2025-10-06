using System.Collections.Immutable;

namespace Ritgard.Mining;

public record TopicModellingResult(
    ImmutableDictionary<long, Topic> Topics,
    ImmutableDictionary<long, TopicItem> Items
);
