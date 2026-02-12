using System;

namespace Ritgard.WorldGenerator;

public enum SlidingWindowPreset
{
    All,
    Week,
    Month,
    Quarter,
    HalfYear,
    Year,
    YearAndHalf,
    TwentyMonths,
    TwoYears,
    MaxValue = TwoYears
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
