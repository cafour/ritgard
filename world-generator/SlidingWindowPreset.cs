using System;
using System.Collections.Immutable;

namespace Ritgard.WorldGenerator;

[Flags]
public enum SlidingWindowPreset
{
    Invalid = 0,
    All = 1 << 0,
    Week = 1 << 1,
    Month = 1 << 2,
    Quarter = 1 << 3,
    HalfYear = 1 << 4,
    Year = 1 << 5,
    YearAndHalf = 1 << 6,
    TwentyMonths = 1 << 7,
    TwoYears = 1 << 8,
    MaxValue = TwoYears,
    AllPresets = int.MaxValue
}

public static class SlidingWindowPresetExtensions
{
    extension(SlidingWindowPreset swp)
    {
        public TimeSpan ToTimeSpan()
        {
            return swp switch
            {
                SlidingWindowPreset.All => TimeSpan.MaxValue,
                SlidingWindowPreset.Week => TimeSpan.FromDays(7),
                SlidingWindowPreset.Month => TimeSpan.FromDays(30),
                SlidingWindowPreset.Quarter => TimeSpan.FromDays(120),
                SlidingWindowPreset.HalfYear => TimeSpan.FromDays(180),
                SlidingWindowPreset.Year => TimeSpan.FromDays(365),
                SlidingWindowPreset.YearAndHalf => TimeSpan.FromDays(547),
                SlidingWindowPreset.TwentyMonths => TimeSpan.FromDays(600),
                SlidingWindowPreset.TwoYears => TimeSpan.FromDays(730),
                _ => throw new NotSupportedException()
            };
        }
    }
}
