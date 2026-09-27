using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// A roundtrip renderer for a <see cref="FootnoteGroup"/>. With trivia, <see cref="RoundtripFootnoteDocumentRenderer"/> writes the
/// footnotes where they are in the source instead.
/// </summary>
internal sealed class RoundtripFootnoteGroupRenderer : RoundtripObjectRenderer<FootnoteGroup>
{
    protected override void Write(RoundtripRenderer renderer, FootnoteGroup group)
    {
        if (group.SourceFootnotes is null)
        {
            renderer.WriteChildren(group);
        }
    }
}
