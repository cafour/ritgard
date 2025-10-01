using System.IO;
using Markdig;
using Markdig.Parsers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;

namespace Ritgard.Mining;

public static partial class Utils
{
    public static readonly MarkdownPipeline MarkdownPipeline;

    public static void WriteMarkdownAsPlainText(string markdown, TextWriter writer)
    {
        var document = MarkdownParser.Parse(markdown, MarkdownPipeline);
        var renderer = new PlainTextRenderer(writer);
        MarkdownPipeline.Setup(renderer);
        renderer.ObjectRenderers.RemoveAll(r => r is CodeBlockRenderer || r is AutolinkInlineRenderer);
        renderer.Render(document);
        writer.Flush();
    }
}
