#nullable enable

using System.Collections.Generic;

namespace Ritgard;

public static partial class Utils
{
    public static string? Coalesce(params IEnumerable<string> strings)
    {   
        foreach (var @string in strings)
        {
            if (!string.IsNullOrEmpty(@string))
            {
                return @string;
            }
        }
        return null;
    }
}
