// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Figures;

/// <summary>
/// The block parser for a <see cref="Figure"/> block.
/// </summary>
/// <seealso cref="BlockParser" />
public class FigureBlockParser : BlockParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FencedBlockParserBase"/> class.
    /// </summary>
    public FigureBlockParser()
    {
        OpeningCharacters = ['^'];
    }

    /// <summary>
    /// Attempts to open a block at the current parser position.
    /// </summary>
    public override BlockState TryOpen(BlockProcessor processor)
    {
        // We expect no indentation for a figure block.
        if (processor.IsCodeIndent)
        {
            return BlockState.None;
        }

        // Match fenced char
        var line = processor.Line;
        char openingChar = line.CurrentChar;
        int count = line.CountAndSkipChar(openingChar);

        // Requires at least 3 opening chars
        if (count < 3)
        {
            return BlockState.None;
        }

        int startPosition = processor.Start;
        int column = processor.Column;
        var figure = new Figure(this)
        {
            Span = new SourceSpan(startPosition, line.End),
            Line = processor.LineIndex,
            Column = column,
            OpeningCharacter = openingChar,
            OpeningCharacterCount = count
        };

        var afterFence = line.Start;
        line.TrimStart();
        FigureCaption? caption = null;
        if (!line.IsEmpty)
        {
            caption = new FigureCaption(this)
            {
                Span = new SourceSpan(line.Start, line.End),
                Line = processor.LineIndex,
                Column = column + line.Start - startPosition,
                IsOpen = false
            };
            caption.AppendLine(ref line, caption.Column, processor.LineIndex, processor.CurrentLineStartPosition, processor.TrackTrivia);
            figure.Add(caption);
        }

        if (processor.TrackTrivia)
        {
            figure.LinesBefore = processor.TakeLinesBefore();
            figure.TriviaBefore = processor.UseTrivia(startPosition - 1);
            var trivia = figure.GetOrCreateSourceTrivia();
            trivia.TriviaAfterOpeningFence = new StringSlice(line.Text, afterFence, caption is null ? line.End : line.Start - 1);
            trivia.OpeningNewLine = line.NewLine;
            trivia.OpeningCaption = caption;
        }

        processor.NewBlocks.Push(figure);

        // Discard the current line as it is already parsed
        return BlockState.ContinueDiscard;
    }

    /// <summary>
    /// Attempts to continue parsing the specified block.
    /// </summary>
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        var figure = (Figure)block;
        int count = figure.OpeningCharacterCount;
        char matchChar = figure.OpeningCharacter;

        int column = processor.Column;
        // Match if we have a closing fence
        var line = processor.Line;
        int startPosition = line.Start;
        var closingCount = line.CountAndSkipChar(matchChar);
        count -= closingCount;

        // If we have a closing fence, close it and discard the current line
        // The line must contain only fence opening character followed only by whitespaces.
        if (count <= 0 && !processor.IsCodeIndent)
        {
            var afterFence = line.Start;
            line.TrimStart();
            FigureCaption? caption = null;
            if (!line.IsEmpty)
            {
                caption = new FigureCaption(this)
                {
                    Span = new SourceSpan(line.Start, line.End),
                    Line = processor.LineIndex,
                    Column = column + line.Start - startPosition,
                    IsOpen = false
                };
                caption.AppendLine(ref line, caption.Column, processor.LineIndex, processor.CurrentLineStartPosition, processor.TrackTrivia);
                figure.Add(caption);
            }

            if (processor.TrackTrivia)
            {
                var trivia = figure.GetOrCreateSourceTrivia();
                trivia.TriviaBeforeClosingFence = processor.UseTrivia(startPosition - 1);
                trivia.ClosingCharacterCount = closingCount;
                trivia.TriviaAfterClosingFence = new StringSlice(line.Text, afterFence, caption is null ? line.End : line.Start - 1);
                trivia.ClosingCaption = caption;
                figure.NewLine = line.NewLine;
            }

            figure.UpdateSpanEnd(line.End);

            // Don't keep the last line
            return BlockState.BreakDiscard;
        }

        // Reset the indentation to the column before the indent
        processor.GoToColumn(processor.ColumnBeforeIndent);

        figure.UpdateSpanEnd(line.End);

        return BlockState.Continue;
    }
}
