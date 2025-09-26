using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Markdig;
using Markdig.Syntax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace Ritgard.Mining;

public partial class ReadmeMiner
{
    private readonly ILogger<ReadmeMiner> logger;

    public ReadmeMiner(ILogger<ReadmeMiner> logger)
    {
        this.logger = logger;
    }
    
    public IConfiguration Configuration { get; private set; } = new ConfigurationBuilder().Build();

    public GitHubClient GH { get; private set; } = null!;

    public MarkdownPipeline MDPipeline { get; private set; } = null!;

    public Task Initialize()
    {
        Configuration = Utils.BuildConfiguration();
        GH = new GitHubClient(new ProductHeaderValue("ritgard"))
        {
            Credentials = new Credentials(Configuration["GitHubToken"])
        };
        MDPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        logger.LogInformation("Initialized");
        return Task.CompletedTask;
    }

    public async Task<ImmutableArray<string>> MineReadmeLinks(string owner, string repoName)
    {
        var repository = await GH.Repository.Get(owner, repoName);
        var readme = await GH.Repository.Content.GetReadme(repository.Id);
        var fileExtension = Path.GetExtension(new Uri(readme.Url).AbsolutePath);
        logger.LogInformation($"Found README at: {readme.Url} with the {fileExtension} file extension.");
        var links = GetLinks(readme.Content, fileExtension);
        logger.LogInformation($"Found {links.Length} links:");
        foreach (var link in links)
        {
            logger.LogInformation($"\t{link}");
        }

        return links;
    }

    ImmutableArray<string> GetLinks(string content, string fileExtension)
    {
        return fileExtension switch
        {
            ".md" => GetMarkdownLinks(content),
            _ => GetRawTextLinks(content)
        };
    }

    ImmutableArray<string> GetRawTextLinks(string content)
    {
        var links = GetUrlRegex().Matches(content);
        return [.. links.Select(m => m.Value)];
    }

    private ImmutableArray<string> GetMarkdownLinks(string content)
    {
        var doc = Markdown.Parse(content, MDPipeline);
        var links = ImmutableArray.CreateBuilder<string>();
        void Visit(MarkdownObject node)
        {
            if (node is Markdig.Syntax.Inlines.LinkInline link
                && !string.IsNullOrWhiteSpace(link.Url)
                && GetUrlRegex().Match(link.Url).Success
            )
            {
                links.Add(link.Url);
            }
            if (node is Markdig.Syntax.ContainerBlock containerBlock)
            {
                foreach (var child in containerBlock)
                {
                    Visit(child);
                }
            }
            if (node is Markdig.Syntax.Inlines.ContainerInline containerInline)
            {
                foreach (var child in containerInline)
                {
                    Visit(child);
                }
            }
            if (node is Markdig.Syntax.LeafBlock leafBlock && leafBlock.Inline is not null)
            {
                Visit(leafBlock.Inline);
            }
        }
        Visit(doc);
        return links.ToImmutable();
    }

    [GeneratedRegex(
        @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)"
    )]
    private static partial Regex GetUrlRegex();
}
