#nullable enable

using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Godot;

namespace Ritgard;

public static partial class Utils
{
    public static ImmutableArray<T> ReadGodotCsv<T>(string resourcePath)
    {
        using var stream = new FileAccessStream(resourcePath, FileAccess.ModeFlags.Read);
        using var reader = new System.IO.StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<T>().ToImmutableArray();
    }

    public static T? ReadGodotJson<T>(string resourcePath)
    {
        using var stream = new FileAccessStream(resourcePath, FileAccess.ModeFlags.Read);
        return JsonSerializer.Deserialize<T>(stream, Mining.Utils.JsonSerializerOptions);
    }

    public static ValueTask<T?> ReadGodotJsonAsync<T>(string resourcePath)
    {
        using var stream = new FileAccessStream(resourcePath, FileAccess.ModeFlags.Read);
        return JsonSerializer.DeserializeAsync<T>(stream, Mining.Utils.JsonSerializerOptions);
    }
}
