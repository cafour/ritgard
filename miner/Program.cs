using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using Markdig;
using Markdig.Syntax;
using Octokit;

var urlRegex = GetUrlRegex();

var mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

await ConsoleApp.RunAsync(args, async (string repo) =>
{
    var github = new GitHubClient(new ProductHeaderValue("ritgard"));

    var repoParts = repo.Split(['/']).Select(s => s.Trim()).ToArray();
    if (repoParts.Length != 2)
    {
        throw new ArgumentException("The report argument must be in the <owner>/<repo> format.");
    }
    var (owner, repoName) = (repoParts[0], repoParts[1]);
    Console.WriteLine($"owner={owner}; repo={repoName}");
    var repoInfo = await github.Repository.Get(owner, repoName);
    var readme = await github.Repository.Content.GetReadme(repoInfo.Id);
    var fileExtension = Path.GetExtension(new Uri(readme.Url).AbsolutePath);
    Console.WriteLine($"Found README at: {readme.Url} with the {fileExtension} file extension.");

    var links = GetLinks(readme.Content, fileExtension);
    Console.WriteLine($"Found {links.Length} links:");
    foreach (var link in links)
    {
        Console.WriteLine($"\t{link}");
    }
});

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
    var links = urlRegex.Matches(content);
    return [.. links.Select(m => m.Value)];
}

ImmutableArray<string> GetMarkdownLinks(string content)
{
    var doc = Markdown.Parse(content, mdPipeline);
    var links = ImmutableArray.CreateBuilder<string>();
    void Visit(MarkdownObject node)
    {
        if (node is Markdig.Syntax.Inlines.LinkInline link
            && !string.IsNullOrWhiteSpace(link.Url)
            && urlRegex.Match(link.Url).Success
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

partial class Program
{
    [GeneratedRegex(
        @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)"
    )]
    private static partial Regex GetUrlRegex();
}
