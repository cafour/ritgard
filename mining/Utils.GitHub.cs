using System;
using System.Collections.Immutable;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public static partial class Utils
{
    public static (GitHubGraphQLClient client, IAsyncDisposable clientDisposable) CreateGitHubGraphQLClient(string authToken)
    {
        var services = new ServiceCollection();
        services.AddGitHubGraphQLClient()
            .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri("https://api.github.com/graphql");
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                }
            );
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<GitHubGraphQLClient>(), provider);
    }
}
