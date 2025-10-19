using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ritgard.Mining;

[JsonConverter(typeof(ClocInfoConverter))]
public record ClocInfo(
    ClocHeader Header,
    ImmutableDictionary<string, ClocEntry> Entries
);

public record ClocHeader(
    [property: JsonPropertyName("cloc_url")]
    string ClocUrl,
    [property: JsonPropertyName("cloc_version")]
    string ClocVersion,
    [property: JsonPropertyName("elapsed_seconds")]
    double ElapsedSeconds,
    [property: JsonPropertyName("n_files")]
    long FileCount,
    [property: JsonPropertyName("n_lines")]
    long LineCount,
    [property: JsonPropertyName("files_per_second")]
    double FilesPerSecond,
    [property: JsonPropertyName("lines_per_second")]
    double LinesPerSecond
);

public record ClocEntry(
    [property: JsonPropertyName("nFiles")]
    long FileCount,
    [property: JsonPropertyName("blank")]
    long BlankCount,
    [property: JsonPropertyName("comment")]
    long CommentCount,
    [property: JsonPropertyName("code")]
    long CodeCount
);

public class ClocInfoConverter : JsonConverter<ClocInfo>
{
    public override ClocInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var header = root.GetProperty("header").Deserialize<ClocHeader>(options);
        if (header is null)
        {
            throw new JsonException("Failed to read cloc header.");
        }

        var builder = ImmutableDictionary.CreateBuilder<string, ClocEntry>();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("header"))
            {
                continue;
            }

            var value = prop.Value.Deserialize<ClocEntry>(options);
            if (value is null)
            {
                throw new JsonException("Failed to read a cloc entry.");
            }

            builder.Add(prop.Name, value);
        }

        return new ClocInfo(header, builder.ToImmutable());
    }

    public override void Write(Utf8JsonWriter writer, ClocInfo value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("header");
        JsonSerializer.Serialize(writer, value.Header, options);
        foreach (var entry in value.Entries)
        {
            writer.WritePropertyName(entry.Key);
            JsonSerializer.Serialize(writer, entry.Value, options);
        }

        writer.WriteEndObject();
    }
}
