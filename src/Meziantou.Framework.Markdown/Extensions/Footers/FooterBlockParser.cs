// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Footers;

/// <summary>
/// A block parser for a <see cref="FooterBlock"/>.
/// </summary>
/// <seealso cref="BlockParser" />
public class FooterBlockParser : BlockParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FooterBlockParser"/> class.
    /// </summary>
    public FooterBlockParser()
    {
        OpeningCharacters = ['^'];
    }

    /// <summary>
    /// Attempts to open a block at the current parser position.
    /// </summary>
    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        var column = processor.Column;
        var startPosition = processor.Start;

        // A footer
        // A Footer marker consists of 0-3 spaces of initial indent, plus (a) the characters ^^ together with a following space, or (b) a double character ^^ not followed by a space.
        var openingChar = processor.CurrentChar;
        if (processor.PeekChar(1) != openingChar)
        {
            return BlockState.None;
        }
        processor.NextChar(); // Grab 2nd^
        var c = processor.NextChar(); // grab space
        var afterMarker = processor.Start;
        if (c.IsSpaceOrTab())
        {
            processor.NextColumn();
        }

        var footer = new FooterBlock(this)
        {
            Span = new SourceSpan(startPosition, processor.Line.End),
            OpeningCharacter = openingChar,
            Column = column,
            Line = processor.LineIndex,
        };

        if (processor.TrackTrivia)
        {
            footer.LinesBefore = processor.TakeLinesBefore();
            AddLine(processor, footer, startPosition, afterMarker, c);
        }

        processor.NewBlocks.Push(footer);
        return BlockState.Continue;
    }

    /// <summary>
    /// Attempts to continue parsing the specified block.
    /// </summary>
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        var quote = (FooterBlock) block;
        var sourcePosition = processor.Start;

        // A footer
        // A Footer marker consists of 0-3 spaces of initial indent, plus (a) the characters ^^ together with a following space, or (b) a double character ^^ not followed by a space.
        var c = processor.CurrentChar;
        var result = BlockState.Continue;
        if (c != quote.OpeningCharacter || processor.PeekChar(1) != c)
        {
            result = processor.IsBlankLine ? BlockState.BreakDiscard : BlockState.None;
            if (result == BlockState.None && processor.TrackTrivia)
            {
                // A lazy continuation line, which has no marker
                ((IQuoteLikeBlock)quote).QuoteLines.Add(new QuoteBlockLine
                {
                    QuoteChar = false,
                    NewLine = processor.Line.NewLine,
                });
            }
        }
        else
        {
            processor.NextChar(); // Skip ^^ char (1st)
            c = processor.NextChar(); // Skip ^^ char (2nd)
            var afterMarker = processor.Start;
            if (c.IsSpace())
            {
                processor.NextChar(); // Skip following space
            }

            if (processor.TrackTrivia)
            {
                AddLine(processor, quote, sourcePosition, afterMarker, c);
            }

            block.UpdateSpanEnd(processor.Line.End);
        }
        return result;
    }

    // Records the trivia around the marker of a line, like QuoteBlockParser does for the quote marker
    private static void AddLine(BlockProcessor processor, FooterBlock footer, int sourcePosition, int afterMarker, char c)
    {
        var hasSpaceAfterMarker = c == ' ';
        if (hasSpaceAfterMarker)
        {
            processor.SkipFirstUnwindSpace = true;
        }

        var triviaBefore = processor.UseTrivia(sourcePosition - 1);
        var triviaAfter = StringSlice.Empty;
        if (c == '\t' && processor.Start != afterMarker)
        {
            // The marker consumed the tab: it is not part of the content
            processor.SkipFirstUnwindSpace = true;
            triviaAfter = new StringSlice(processor.Line.Text, afterMarker, processor.Start - 1);
        }

        if (processor.Line.IsEmptyOrWhitespace())
        {
            processor.TriviaStart = afterMarker + (hasSpaceAfterMarker ? 1 : 0);
            triviaAfter = processor.UseTrivia(processor.Line.End);
        }
        else
        {
            processor.TriviaStart = processor.Start;
        }

        ((IQuoteLikeBlock)footer).QuoteLines.Add(new QuoteBlockLine
        {
            TriviaBefore = triviaBefore,
            TriviaAfter = triviaAfter,
            QuoteChar = true,
            HasSpaceAfterQuoteChar = hasSpaceAfterMarker,
            NewLine = processor.Line.NewLine,
        });
    }
}
