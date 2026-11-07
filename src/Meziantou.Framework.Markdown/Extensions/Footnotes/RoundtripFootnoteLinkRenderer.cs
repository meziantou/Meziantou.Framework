using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// A roundtrip renderer for a <see cref="FootnoteLink"/>.
/// </summary>
internal sealed class RoundtripFootnoteLinkRenderer : RoundtripObjectRenderer<FootnoteLink>
{
    protected override void Write(RoundtripRenderer renderer, FootnoteLink link)
    {
        // The parser adds the back links, which are not in the source
        if (link.IsBackLink)
        {
            return;
        }

        renderer.Write('[');
        if (link.LabelWithTrivia.Text is not null)
        {
            renderer.Write(link.LabelWithTrivia);
        }
        else
        {
            renderer.Write(link.Footnote.Label);
        }

        renderer.Write(']');
    }
}
