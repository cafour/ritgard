using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Microsoft.Extensions.Configuration;

namespace Ritgard.Mining;

public static class Utils
{
    public static IConfiguration BuildConfiguration()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(Assembly.GetEntryAssembly()!, optional: true);
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
        return (await csv.GetRecordsAsync<T>(token).ToArrayAsync()).ToImmutableArray();
    }
}
