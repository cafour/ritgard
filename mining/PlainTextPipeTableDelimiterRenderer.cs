using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace Ritgard.Mining;

public class PlainTextPipeTableDelimiterInlineRenderer : HtmlObjectRenderer<PipeTableDelimiterInline>
{
    protected override void Write(HtmlRenderer renderer, PipeTableDelimiterInline obj)
    {
        // no-op
    }
}
