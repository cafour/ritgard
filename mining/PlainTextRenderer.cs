using System.IO;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace Ritgard.Mining;

public class PlainTextRenderer : HtmlRenderer
{
    public PlainTextRenderer(TextWriter writer) : base(writer)
    {
        EnableHtmlEscape = false;
        EnableHtmlForBlock = false;
        EnableHtmlForInline = false;
    }
}
