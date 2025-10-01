using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Ritgard.Mining;

public class PlainTextLinkInlineRenderer : HtmlObjectRenderer<LinkInline>
{
    protected override void Write(HtmlRenderer renderer, LinkInline obj)
    {
        if (!obj.IsAutoLink)
        {
            renderer.WriteChildren(obj);
        }
    }
}
