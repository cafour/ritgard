using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConsoleAppFramework;
using CsvHelper;
using Markdig;
using Markdig.Syntax;
using Microsoft.Extensions.Configuration;
using Octokit;

var urlRegex = GetUrlRegex();

var mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

await ConsoleApp.RunAsync(args, async (string repo) =>
{
    var configBuilder = new ConfigurationBuilder();
    configBuilder
        .AddJsonFile("appsettings.json")
        .AddUserSecrets<Program>();
    var config = configBuilder.Build();

    var github = new GitHubClient(new ProductHeaderValue("ritgard"));
    github.Credentials = new Credentials(config["GitHubToken"]);

    var repoParts = repo.Split(['/']).Select(s => s.Trim()).ToArray();
    if (repoParts.Length != 2)
    {
        throw new ArgumentException("The report argument must be in the <owner>/<repo> format.");
    }
    var (owner, repoName) = (repoParts[0], repoParts[1]);
    Console.WriteLine($"owner={owner}; repo={repoName}");
    var repoInfo = await github.Repository.Get(owner, repoName);

    var issues = await GetIssues(github, repoInfo);
    using var writer = new StreamWriter("./issues.csv");
    using var cvs = new CsvWriter(writer, CultureInfo.InvariantCulture);
    cvs.WriteRecords(issues);
});

async Task PrintLinks(GitHubClient github, Repository repoInfo)
{
    var readme = await github.Repository.Content.GetReadme(repoInfo.Id);
    var fileExtension = Path.GetExtension(new Uri(readme.Url).AbsolutePath);
    Console.WriteLine($"Found README at: {readme.Url} with the {fileExtension} file extension.");

    var links = GetLinks(readme.Content, fileExtension);
    Console.WriteLine($"Found {links.Length} links:");
    foreach (var link in links)
    {
        Console.WriteLine($"\t{link}");
    }
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

async Task<ImmutableArray<GitHubIssue>> GetIssues(GitHubClient gh, Repository repository)
{
    var issues = await gh.Issue.GetAllForRepository(repository.Id, new RepositoryIssueRequest
    {
        State = ItemStateFilter.All
    });
    return issues.Select(i => new GitHubIssue(
        Id: i.Id,
        Number: i.Number,
        Title: i.Title,
        Author: i.User.Login,
        CreatedAt: i.CreatedAt,
        UpdatedAt: i.UpdatedAt,
        ClosedAt: i.ClosedAt,
        Labels: string.Join(';', i.Labels.Select(l => l.Name)),
        Body: i.Body
    )).ToImmutableArray();
}

partial class Program
{
    [GeneratedRegex(
        @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)"
    )]
    private static partial Regex GetUrlRegex();
}

public record GitHubIssue(
    long Id,
    int Number,
    string Title,
    string Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ClosedAt,
    string Labels,
    string Body
);
