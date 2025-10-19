using System.Collections.Immutable;

namespace Ritgard.Mining;

public record GitLocInfo(
    long AddedLineCount,
    long DeletedLineCount,
    ImmutableDictionary<string, GitLocEntry> Entries
);

public record GitLocEntry
{
    public long AddedLineCount { get; set; }

    public long DeletedLineCount { get; set; }
}
