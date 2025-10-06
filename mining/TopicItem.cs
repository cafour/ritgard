using System.Collections.Immutable;

namespace Ritgard.Mining;

public record TopicItem(
    long Id,
    double X,
    double Y,
    int TopicId,
    ImmutableDictionary<int, double> Probabilities
);
