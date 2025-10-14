using System.IO;
using System.Linq;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Parsers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Ritgard.Mining;

public static partial class Utils
{
    public static readonly MarkdownPipeline MarkdownPipeline;

    public static void WriteMarkdownAsPlainText(string markdown, TextWriter writer)
    {
        var document = MarkdownParser.Parse(markdown, MarkdownPipeline);
        var renderer = new PlainTextRenderer(writer);
        MarkdownPipeline.Setup(renderer);
        renderer.ObjectRenderers.RemoveAll(r =>
            r is HtmlObjectRenderer<CodeBlock>
            || r is HtmlObjectRenderer<AutolinkInline>
            || r is HtmlObjectRenderer<LinkInline>
            || r is HtmlObjectRenderer<Table>
            || r is HtmlObjectRenderer<PipeTableDelimiterInline>
        );
        renderer.ObjectRenderers.InsertRange(
            0,
            [
                new PlainTextLinkInlineRenderer(),
                new PlainTextTableRenderer(),
                new PlainTextPipeTableDelimiterInlineRenderer()
            ]
        );
        renderer.Render(document);
        writer.Flush();
    }
}
