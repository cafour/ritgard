using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace Ritgard.Mining;

public class PlainTextTableRenderer : HtmlObjectRenderer<Table>
{
    protected override void Write(HtmlRenderer renderer, Table obj)
    {
        // no-op
    }
}
