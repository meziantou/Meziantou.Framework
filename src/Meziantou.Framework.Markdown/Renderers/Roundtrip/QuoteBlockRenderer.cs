// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Roundtrip;

/// <summary>
/// A Roundtrip renderer for a <see cref="QuoteBlock"/>.
/// </summary>
public class QuoteBlockRenderer : RoundtripObjectRenderer<QuoteBlock>
{
    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, QuoteBlock quoteBlock)
    {
        renderer.RenderLinesBefore(quoteBlock);
        renderer.Write(quoteBlock.TriviaBefore);

        var quoteLines = quoteBlock.QuoteLines;
        var indents = new string[quoteLines.Count];
        int[]? lazyLines = null;
        for (int i = 0; i < quoteLines.Count; i++)
        {
            var quoteLine = quoteLines[i];
            var wsb = quoteLine.TriviaBefore.ToString();
            var quoteChar = quoteLine.QuoteChar ? ">" : "";
            var spaceAfterQuoteChar = quoteLine.HasSpaceAfterQuoteChar ? " " : "";
            var wsa = quoteLine.TriviaAfter.ToString();
            indents[i] = (wsb + quoteChar + spaceAfterQuoteChar + wsa);
            if (quoteLine.LazyLinesAfter > 0)
            {
                (lazyLines ??= new int[quoteLines.Count])[i] = quoteLine.LazyLinesAfter;
            }
        }

        renderer.PushIndent(indents, lazyLines);
        renderer.WriteChildren(quoteBlock);

        // The quote lines that are not written yet have no content (all of them when the quote has no children). The parser
        // attached them after the quote, before the blank lines that follow it.
        var remaining = renderer.RemainingIndentLines;
        var linesInQuote = 0;

        // Find the quote line of the first line not written yet, counting the lazy lines that follow each quote line
        var lineIndex = quoteLines.Count;
        var lineOffset = 0;
        for (var skip = remaining; skip > 0;)
        {
            lineIndex--;
            var lineCount = 1 + quoteLines[lineIndex].LazyLinesAfter;
            lineOffset = Math.Max(0, lineCount - skip);
            skip -= lineCount;
        }

        for (var i = 0; i < remaining; i++)
        {
            if (quoteBlock.LinesAfter is { } linesAfter && i < linesAfter.Count)
            {
                renderer.Write(linesAfter[i]);
                renderer.WriteLine(linesAfter[i].NewLine);
                linesInQuote++;
            }
            else
            {
                renderer.WriteLine(quoteLines[lineIndex].NewLine);
            }

            if (++lineOffset > quoteLines[lineIndex].LazyLinesAfter)
            {
                lineIndex++;
                lineOffset = 0;
            }
        }

        renderer.PopIndent();
        renderer.RenderLinesAfter(quoteBlock, linesInQuote);
    }
}
