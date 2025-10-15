using System.Collections.Immutable;

namespace Ritgard.Mining;

public record TopicItem(
    string Id,
    double X,
    double Y,
    int TopicId,
    ImmutableDictionary<int, double> Probabilities
);
