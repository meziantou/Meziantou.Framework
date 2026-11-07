using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Footers;

/// <summary>
/// A roundtrip renderer for a <see cref="FooterBlock"/>, whose lines start with <c>^^</c> like the lines of a quote start with <c>&gt;</c>.
/// </summary>
internal sealed class RoundtripFooterBlockRenderer : RoundtripObjectRenderer<FooterBlock>
{
    protected override void Write(RoundtripRenderer renderer, FooterBlock obj)
    {
        if (((IQuoteLikeBlock)obj).QuoteLines.Count == 0)
        {
            // A footer that was not parsed has no lines: each line of its content starts with the marker
            renderer.RenderLinesBefore(obj);
            renderer.PushIndent(((IQuoteLikeBlock)obj).Marker + " ");
            renderer.WriteChildren(obj);
            renderer.PopIndent();
            renderer.RenderLinesAfter(obj);
            return;
        }

        QuoteBlockRenderer.WriteQuote(renderer, obj, writeFirstLine: null);
    }
}
