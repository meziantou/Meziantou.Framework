using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Footnotes;

/// <summary>
/// A roundtrip renderer for a <see cref="Footnote"/>.
/// </summary>
internal sealed class RoundtripFootnoteRenderer : RoundtripObjectRenderer<Footnote>
{
    protected override void Write(RoundtripRenderer renderer, Footnote footnote)
    {
        renderer.RenderLinesBefore(footnote);

        var marker = footnote.TriviaBefore.ToString() + "[" + footnote.LabelWithTrivia.ToString() + "]:";
        if (footnote.IsFirstLineEmpty)
        {
            // The rest of the first line and its newline are a blank line before the content
            renderer.Write(marker);
            WriteContent(renderer, footnote);
        }
        else
        {
            // The content starts after the marker, and the next lines keep their own indentation
            renderer.PushIndent([marker]);
            WriteContent(renderer, footnote);
            renderer.PopIndent();
        }

        renderer.RenderLinesAfter(footnote);
    }

    private static void WriteContent(RoundtripRenderer renderer, Footnote footnote)
    {
        foreach (var block in footnote)
        {
            // The paragraph that the parser adds for the back links is not in the source
            if (block is ParagraphBlock { Parser: null })
            {
                continue;
            }

            renderer.Write(block);
        }
    }
}
