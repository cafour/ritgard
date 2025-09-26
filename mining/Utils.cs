using System;
using System.Linq;
using System.Reflection;
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
}
