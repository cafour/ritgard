using System;

namespace Ritgard;

public record struct ActiveItemEvent(
    DateTimeOffset Timestamp,
    object? OriginalEvent
) : IComparable<ActiveItemEvent>
{
    public int CompareTo(ActiveItemEvent other)
    {
        return Timestamp.CompareTo(other.Timestamp);
    }
}
