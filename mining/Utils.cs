using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Markdig;
using Microsoft.Extensions.Configuration;
using TupleAsJsonArray;

namespace Ritgard.Mining;

public static partial class Utils
{
    public static readonly JsonSerializerOptions JsonSerializerOptions;

    static Utils()
    {
        JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerOptions.Default);
        JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
        JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        JsonSerializerOptions.Converters.Add(new TupleConverterFactory());

        MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    }

    public static IConfiguration BuildConfiguration()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly(), optional: true)
            .AddEnvironmentVariables();
        return configBuilder.Build();
    }

    public static (string owner, string repoName) ParseRepoString(string repo)
    {
        var repoParts = repo.Split(['/']).Select(s => s.Trim()).ToArray();
        if (repoParts.Length != 2)
        {
            throw new ArgumentException("Expected repo in the <owner>/<name> format.");
        }

        return (repoParts[0], repoParts[1]);
    }

    public static async Task WriteCsv<T>(
        IEnumerable<T> values,
        string name,
        CancellationToken token = default)
    {
        name = name.ToLower().Replace(" ", "_");
        using var writer = new StreamWriter($"./{name}.csv");
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(values, token);
    }

    public static async Task<ImmutableArray<T>> ReadCsv<T>(
        string path,
        CancellationToken token = default
    )
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return [..await csv.GetRecordsAsync<T>(token).ToArrayAsync(token)];
    }

    public static async Task WriteJson<T>(
        T value,
        string path,
        CancellationToken token = default
    )
    {
        var parentDir = new FileInfo(path).Directory;
        if (parentDir is not null && !parentDir.Exists)
        {
            parentDir.Create();
        }

        await using var stream = new FileStream(path, FileMode.Create);

        await JsonSerializer.SerializeAsync(stream, value, JsonSerializerOptions, cancellationToken: token);
    }

    public static async Task<T?> ReadJson<T>(
        string path,
        CancellationToken token = default
    )
    {
        await using var stream = new FileStream(path, FileMode.Open);

        return await JsonSerializer.DeserializeAsync<T>(stream, JsonSerializerOptions, cancellationToken: token);
    }

    public static DateTimeOffset Max(DateTimeOffset lhs, DateTimeOffset rhs)
    {
        return lhs > rhs ? lhs : rhs;
    }

    public static DateTimeOffset Min(DateTimeOffset lhs, DateTimeOffset rhs)
    {
        return lhs < rhs ? lhs : rhs;
    }
}
