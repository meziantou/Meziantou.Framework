// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// Block parser for a <see cref="ParagraphBlock"/>.
/// </summary>
/// <seealso cref="BlockParser" />
public class ParagraphBlockParser : BlockParser
{
    /// <summary>
    /// Gets or sets the parse setex headings.
    /// </summary>
    public bool ParseSetexHeadings { get; set; } = true;

    /// <summary>
    /// Attempts to open a block at the current parser position.
    /// </summary>
    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsBlankLine)
        {
            return BlockState.None;
        }

        // We continue trying to match by default
        var paragraph = new ParagraphBlock(this)
        {
            Column = processor.Column,
            Span = new SourceSpan(processor.Line.Start, processor.Line.End),
        };

        if (processor.TrackTrivia)
        {
            paragraph.LinesBefore = processor.TakeLinesBefore();
            paragraph.NewLine = processor.Line.NewLine;
        }

        processor.NewBlocks.Push(paragraph);
        return BlockState.Continue;
    }

    /// <summary>
    /// Attempts to continue parsing the specified block.
    /// </summary>
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (processor.IsBlankLine)
        {
            return BlockState.BreakDiscard;
        }

        if (!processor.IsCodeIndent && ParseSetexHeadings)
        {
            return TryParseSetexHeading(processor, block);
        }
        block.NewLine = processor.Line.NewLine;
        block.UpdateSpanEnd(processor.Line.End);
        return BlockState.Continue;
    }

    /// <summary>
    /// Performs the close operation.
    /// </summary>
    public override bool Close(BlockProcessor processor, Block block)
    {
        if (block is ParagraphBlock paragraph)
        {
            ref var lines = ref paragraph.Lines;

            if (processor.TrackTrivia)
            {
                TryMatchLinkReferenceDefinitionTrivia(ref lines, processor, paragraph);
            }
            else
            {
                TryMatchLinkReferenceDefinition(ref lines, processor);
            }

            int lineCount = lines.Count;

            // If Paragraph is empty, we can discard it
            if (lineCount == 0)
            {
                return false;
            }

            if (!processor.TrackTrivia)
            {
                for (int i = 0; i < lineCount; i++)
                {
                    lines.Lines[i].Slice.TrimStart();
                }
                lines.Lines[lineCount - 1].Slice.TrimEnd();
            }
        }

        return true;
    }

    private BlockState TryParseSetexHeading(BlockProcessor state, Block block)
    {
        var line = state.Line;
        var sourcePosition = line.Start;
        int count = 0;
        char headingChar = GetHeadingChar(ref line, ref count);

        if (headingChar != 0)
        {
            var paragraph = (ParagraphBlock)block;

            bool foundLrd;
            if (state.TrackTrivia)
            {
                foundLrd = TryMatchLinkReferenceDefinitionTrivia(ref paragraph.Lines, state, paragraph);
            }
            else
            {
                foundLrd = TryMatchLinkReferenceDefinition(ref paragraph.Lines, state);
            }

            // If we matched a LinkReferenceDefinition before matching the heading, and the remaining
            // lines are empty, we can early exit and remove the paragraph
            var parent = block.Parent;
            bool isSetTextHeading = !state.IsLazy || paragraph.Column == state.Column || !(parent is QuoteBlock || parent is ListItemBlock);
            if (!(foundLrd && paragraph.Lines.Count == 0) && isSetTextHeading)
            {
                // We discard the paragraph that will be transformed to a heading
                state.Discard(paragraph);

                while (state.CurrentChar == headingChar)
                {
                    state.NextChar();
                }

                int level = headingChar == '=' ? 1 : 2;

                var heading = new HeadingBlock(this)
                {
                    Column = paragraph.Column,
                    Span = new SourceSpan(paragraph.Span.Start, line.End),
                    Level = level,
                    Lines = paragraph.Lines,
                    IsSetext = true,
                    HeaderCharCount = count,
                };

                if (state.TrackTrivia)
                {
                    heading.LinesBefore = paragraph.LinesBefore;
                    heading.TriviaBefore = state.UseTrivia(sourcePosition - 1); // remove dashes
                    heading.TriviaAfter = new StringSlice(state.Line.Text, state.Start, line.End);
                    heading.NewLine = state.Line.NewLine;
                    heading.SetextNewline = paragraph.NewLine;
                }
                else
                {
                    heading.Lines.Trim();
                }

                // Remove the paragraph as a pending block
                state.NewBlocks.Push(heading);

                return BlockState.BreakDiscard;
            }
        }

        block.UpdateSpanEnd(state.Line.End);

        return BlockState.Continue;
    }

    private static char GetHeadingChar(ref StringSlice line, ref int count)
    {
        char headingChar = line.CurrentChar;

        if (headingChar == '=' || headingChar == '-')
        {
            count = line.CountAndSkipChar(headingChar);

            while (line.CurrentChar.IsSpaceOrTab())
            {
                line.NextChar();
            }

            if (line.IsEmpty)
            {
                return headingChar;
            }
        }

        return (char)0;
    }

    private static bool TryMatchLinkReferenceDefinition(ref StringLineGroup lines, BlockProcessor state)
    {
        bool atLeastOneFound = false;

        // The definitions are parsed from the first line left by the previous one, and the lines they use are removed
        // once at the end: removing them after each definition makes a block of n definitions O(n²)
        var startLine = 0;
        var end = lines.GetCharacterCount(0) - 1;
        while (startLine < lines.Count)
        {
            // If we have found a LinkReferenceDefinition, we can discard the previous paragraph
            var iterator = lines.ToCharIterator(startLine, end);
            if (LinkReferenceDefinition.TryParse(ref iterator, out LinkReferenceDefinition? linkReferenceDefinition))
            {
                state.Document.SetLinkReferenceDefinition(linkReferenceDefinition.Label!, linkReferenceDefinition, true);
                atLeastOneFound = true;

                // Correct the locations of each field
                linkReferenceDefinition.Line = lines.Lines[startLine].Line;

                linkReferenceDefinition.Span = lines.ConvertToAbsoluteSpan(linkReferenceDefinition.Span, startLine);
                linkReferenceDefinition.LabelSpan = lines.ConvertToAbsoluteSpan(linkReferenceDefinition.LabelSpan, startLine);
                linkReferenceDefinition.UrlSpan = lines.ConvertToAbsoluteSpan(linkReferenceDefinition.UrlSpan, startLine);
                linkReferenceDefinition.TitleSpan = lines.ConvertToAbsoluteSpan(linkReferenceDefinition.TitleSpan, startLine);
                if (!SkipConsumedLines(ref iterator, ref startLine, ref end))
                {
                    lines.Clear();
                    return true;
                }
            }
            else
            {
                break;
            }
        }

        if (startLine > 0)
        {
            lines.RemoveStartRange(startLine);
        }

        return atLeastOneFound;
    }

    private static bool TryMatchLinkReferenceDefinitionTrivia(ref StringLineGroup lines, BlockProcessor state, ParagraphBlock paragraph)
    {
        bool atLeastOneFound = false;

        // See TryMatchLinkReferenceDefinition. The definitions are inserted before the paragraph, which is usually the last
        // block of its parent.
        var startLine = 0;
        var end = lines.GetCharacterCount(0) - 1;
        var index = -1;
        while (startLine < lines.Count)
        {
            // If we have found a LinkReferenceDefinition, we can discard the previous paragraph
            var iterator = lines.ToCharIterator(startLine, end);
            if (LinkReferenceDefinition.TryParseTrivia(
                ref iterator,
                out LinkReferenceDefinition? lrd,
                out SourceSpan triviaBeforeLabel,
                out SourceSpan labelWithTrivia,
                out SourceSpan triviaBeforeUrl,
                out SourceSpan unescapedUrl,
                out SourceSpan triviaBeforeTitle,
                out SourceSpan unescapedTitle,
                out SourceSpan triviaAfterTitle))
            {
                // The definition is also added to the document, where the roundtrip renderer writes it
                state.Document.GetLinkReferenceDefinitions(false).SetDetached(lrd.Label!, lrd);
                atLeastOneFound = true;

                // Correct the locations of each field
                lrd.Line = lines.Lines[startLine].Line;
                var text = lines.Lines[startLine].Slice.Text;

                triviaBeforeLabel = lines.ConvertToAbsoluteSpan(triviaBeforeLabel, startLine);
                labelWithTrivia = lines.ConvertToAbsoluteSpan(labelWithTrivia, startLine);
                triviaBeforeUrl = lines.ConvertToAbsoluteSpan(triviaBeforeUrl, startLine);
                unescapedUrl = lines.ConvertToAbsoluteSpan(unescapedUrl, startLine);
                triviaBeforeTitle = lines.ConvertToAbsoluteSpan(triviaBeforeTitle, startLine);
                unescapedTitle = lines.ConvertToAbsoluteSpan(unescapedTitle, startLine);
                triviaAfterTitle = lines.ConvertToAbsoluteSpan(triviaAfterTitle, startLine);
                lrd.Span = lines.ConvertToAbsoluteSpan(lrd.Span, startLine);
                lrd.TriviaBefore = new StringSlice(text, triviaBeforeLabel.Start, triviaBeforeLabel.End);
                lrd.LabelSpan = lines.ConvertToAbsoluteSpan(lrd.LabelSpan, startLine);
                lrd.LabelWithTrivia = new StringSlice(text, labelWithTrivia.Start, labelWithTrivia.End);
                lrd.TriviaBeforeUrl = new StringSlice(text, triviaBeforeUrl.Start, triviaBeforeUrl.End);
                lrd.UrlSpan = lines.ConvertToAbsoluteSpan(lrd.UrlSpan, startLine);
                lrd.UnescapedUrl = new StringSlice(text, unescapedUrl.Start, unescapedUrl.End);
                lrd.TriviaBeforeTitle = new StringSlice(text, triviaBeforeTitle.Start, triviaBeforeTitle.End);
                lrd.TitleSpan = lines.ConvertToAbsoluteSpan(lrd.TitleSpan, startLine);
                lrd.UnescapedTitle = new StringSlice(text, unescapedTitle.Start, unescapedTitle.End);
                lrd.TriviaAfter = new StringSlice(text, triviaAfterTitle.Start, triviaAfterTitle.End);
                lrd.LinesBefore = paragraph.LinesBefore;

                state.LinesBefore = paragraph.LinesAfter; // ensure closed paragraph with linesafter placed back on stack

                var consumedAll = !SkipConsumedLines(ref iterator, ref startLine, ref end);
                index = index < 0 ? paragraph.Parent!.LastIndexOf(paragraph) : index + 1;
                paragraph.Parent!.Insert(index, lrd);
                if (consumedAll)
                {
                    lines.Clear();
                    return true;
                }
            }
            else
            {
                break;
            }
        }

        if (startLine > 0)
        {
            lines.RemoveStartRange(startLine);
        }

        return atLeastOneFound;
    }

    // Moves to the first line left after a definition, or returns false when the definition used all the lines
    private static bool SkipConsumedLines(ref StringLineGroup.Iterator iterator, ref int startLine, ref int end)
    {
        var nextLine = iterator.SkipConsumedLines(out var consumedCharacters);
        if (nextLine < 0)
        {
            return false;
        }

        startLine = nextLine;
        end -= consumedCharacters;
        return true;
    }
}
