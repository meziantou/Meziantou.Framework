using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// A roundtrip renderer for a <see cref="MarkdownDocument"/> with footnotes. The parser moves the footnotes to a group at the end
/// of the document, so they are written back between the blocks where they are in the source.
/// </summary>
internal sealed class RoundtripFootnoteDocumentRenderer : RoundtripObjectRenderer<MarkdownDocument>
{
    protected override void Write(RoundtripRenderer renderer, MarkdownDocument document)
    {
        var group = document.LastChild as FootnoteGroup;
        var footnotes = group?.SourceFootnotes;
        if (group is null || footnotes is null)
        {
            renderer.WriteChildren(document);
            return;
        }

        var next = 0;
        foreach (var block in document)
        {
            if (block is FootnoteGroup)
            {
                continue;
            }

            while (next < footnotes.Count && footnotes[next].Line < block.Line)
            {
                renderer.Write(footnotes[next++]);
            }

            renderer.Write(block);
        }

        while (next < footnotes.Count)
        {
            renderer.Write(footnotes[next++]);
        }

        // The blank lines at the end of the document are attached to the group when the last block is in a footnote
        renderer.RenderLinesAfter(group);
    }
}
