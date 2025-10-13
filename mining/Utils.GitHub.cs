using System;
using System.Collections.Immutable;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Ritgard.Mining.GitHub;

namespace Ritgard.Mining;

public static partial class Utils
{
    public static GitHubGraphQLClient CreateGitHubGraphQLClient(string authToken)
    {
        var services = new ServiceCollection();
        services.AddGitHubGraphQLClient()
            .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri("https://api.github.com/graphql");
                    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                }
            );
        return services.BuildServiceProvider().GetRequiredService<GitHubGraphQLClient>();
    }
}
