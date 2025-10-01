using System.Collections.Immutable;

namespace Ritgard.Mining;

public record Topic(
    string Title,
    ImmutableHashSet<long> Ids
);
