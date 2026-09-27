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

        var indents = new string[quoteBlock.QuoteLines.Count];
        for (int i = 0; i < quoteBlock.QuoteLines.Count; i++)
        {
            var quoteLine = quoteBlock.QuoteLines[i];
            var wsb = quoteLine.TriviaBefore.ToString();
            var quoteChar = quoteLine.QuoteChar ? ">" : "";
            var spaceAfterQuoteChar = quoteLine.HasSpaceAfterQuoteChar ? " " : "";
            var wsa = quoteLine.TriviaAfter.ToString();
            indents[i] = (wsb + quoteChar + spaceAfterQuoteChar + wsa);
        }

        renderer.PushIndent(indents);
        renderer.WriteChildren(quoteBlock);

        // The quote lines that are not written yet have no content (all of them when the quote has no children). The parser
        // attached them after the quote, before the blank lines that follow it.
        var remaining = renderer.RemainingIndentLines;
        var linesInQuote = 0;
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
                renderer.WriteLine(quoteBlock.QuoteLines[quoteBlock.QuoteLines.Count - remaining + i].NewLine);
            }
        }

        renderer.PopIndent();
        renderer.RenderLinesAfter(quoteBlock, linesInQuote);
    }
}
