// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.DefinitionLists;

/// <summary>
/// The block parser for a <see cref="DefinitionList"/>.
/// </summary>
/// <seealso cref="BlockParser" />
public class DefinitionListParser : BlockParser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionListParser"/> class.
    /// </summary>
    public DefinitionListParser()
    {
        OpeningCharacters = [':', '~'];
    }

    /// <summary>
    /// Attempts to open a block at the current parser position.
    /// </summary>
    public override BlockState TryOpen(BlockProcessor processor)
    {
        var paragraphBlock = processor.LastBlock as ParagraphBlock;
        if (processor.IsCodeIndent || paragraphBlock is null || paragraphBlock.LastLine - processor.LineIndex > 1)
        {
            return BlockState.None;
        }

        var column = processor.ColumnBeforeIndent;
        var markerPosition = processor.Start;
        processor.NextChar();
        processor.ParseIndent();
        var delta = processor.Column - column;

        // We expect to have a least
        if (delta < 4)
        {
            // Return back to original position
            processor.GoToColumn(column);
            return BlockState.None;
        }

        if (delta > 4)
        {
            processor.GoToColumn(column + 4);
        }

        var previousParent = paragraphBlock.Parent!;
        var currentDefinitionList = GetCurrentDefinitionList(paragraphBlock, previousParent);

        processor.Discard(paragraphBlock);

        // If the paragraph block was not part of the opened blocks, we need to remove it manually from its parent container
        if (paragraphBlock.Parent != null)
        {
            paragraphBlock.Parent.Remove(paragraphBlock);
        }

        if (currentDefinitionList is null)
        {
            currentDefinitionList = new DefinitionList(this)
            {
                Span = new SourceSpan(paragraphBlock.Span.Start, processor.Line.End),
                Column = paragraphBlock.Column,
                Line = paragraphBlock.Line,
            };
            previousParent.Add(currentDefinitionList);
        }

        var definitionItem = new DefinitionItem(this)
        {
            Line = processor.LineIndex,
            Column = column,
            Span = new SourceSpan(paragraphBlock.Span.Start, processor.Line.End),
            OpeningCharacter = processor.Line.Text[markerPosition],
        };
        if (processor.TrackTrivia)
        {
            SetMarkerTrivia(processor, definitionItem, markerPosition);
        }

        for (int i = 0; i < paragraphBlock.Lines.Count; i++)
        {
            var line = paragraphBlock.Lines.Lines[i];
            var term = new DefinitionTerm(this)
            {
                Column =  paragraphBlock.Column,
                Line = line.Line,
                Span = new SourceSpan(paragraphBlock.Span.Start, paragraphBlock.Span.End),
                IsOpen = false
            };
            term.AppendLine(ref line.Slice, line.Column, line.Line, line.Position, processor.TrackTrivia);
            if (processor.TrackTrivia)
            {
                // The terms replace the paragraph, so they take the trivia around it
                if (i == 0)
                {
                    term.LinesBefore = paragraphBlock.LinesBefore;
                    term.TriviaBefore = paragraphBlock.TriviaBefore;
                }

                if (i == paragraphBlock.Lines.Count - 1)
                {
                    term.LinesAfter = paragraphBlock.LinesAfter;
                }
            }

            definitionItem.Add(term);
        }
        currentDefinitionList.Add(definitionItem);
        processor.Open(definitionItem);

        // Update the end position
        currentDefinitionList.UpdateSpanEnd(processor.Line.End);

        return BlockState.Continue;
    }

    // Records the lines and the indent before the opening character. The spaces after it are the trivia of the first block of the definition.
    private static void SetMarkerTrivia(BlockProcessor processor, DefinitionItem item, int markerPosition)
    {
        item.LinesBefore = processor.TakeLinesBefore();
        item.TriviaBefore = processor.UseTrivia(markerPosition - 1);
        item.NewLine = processor.Line.NewLine;
        item.HasOpeningCharacterTrivia = true;
        processor.TriviaStart = markerPosition + 1;
    }

    private static DefinitionList? GetCurrentDefinitionList(ParagraphBlock paragraphBlock, ContainerBlock previousParent)
    {
        var index = previousParent.IndexOf(paragraphBlock) - 1;
        if (index < 0) return null;
        switch (previousParent[index])
        {
            case DefinitionList definitionList:
                return definitionList;

            case BlankLineBlock:
                if (index > 0 && previousParent[index - 1] is DefinitionList definitionList2)
                {
                    previousParent.RemoveAt(index);
                    return definitionList2;
                }
                break;
        }
        return null;
    }

    /// <summary>
    /// Attempts to continue parsing the specified block.
    /// </summary>
    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        var definitionItem = (DefinitionItem)block;
        if (processor.IsCodeIndent)
        {
            processor.GoToCodeIndent();
            return BlockState.Continue;
        }

        var list = (DefinitionList)definitionItem.Parent!;
        var lastBlankLine = definitionItem.LastChild as BlankLineBlock;

        // Check if we have another definition list
        if (Array.IndexOf(OpeningCharacters!, processor.CurrentChar) >= 0)
        {
            var startPosition = processor.Start;
            var column = processor.ColumnBeforeIndent;
            processor.NextChar();
            processor.ParseIndent();
            var delta = processor.Column - column;

            // We expect to have a least
            if (delta < 4)
            {
                // Remove the blankline before breaking this definition item
                if (lastBlankLine != null)
                {
                    definitionItem.RemoveAt(definitionItem.Count - 1);
                }

                list.Span.End = list.LastChild!.Span.End;
                return BlockState.None;
            }

            if (delta > 4)
            {
                processor.GoToColumn(column + 4);
            }

            processor.Close(definitionItem);
            var nextDefinitionItem = new DefinitionItem(this)
            {
                Span = new SourceSpan(startPosition, processor.Line.End),
                Line = processor.LineIndex,
                Column = processor.Column,
                OpeningCharacter = processor.Line.Text[startPosition],
            };
            if (processor.TrackTrivia)
            {
                SetMarkerTrivia(processor, nextDefinitionItem, startPosition);
            }

            list.Add(nextDefinitionItem);
            processor.Open(nextDefinitionItem);

            return BlockState.Continue;
        }

        var isBreakable = definitionItem.LastChild?.IsBreakable ?? true;
        if (processor.IsBlankLine)
        {
            if (lastBlankLine is null && isBreakable)
            {
                definitionItem.Add(new BlankLineBlock());
            }

            if (isBreakable && processor.TrackTrivia)
            {
                // The line is discarded: keep it for the block that follows, like the processor does for the other empty lines
                var line = processor.UseTrivia(processor.Line.End);
                line.NewLine = processor.Line.NewLine;
                (processor.LinesBefore ??= []).Add(line);
            }

            return isBreakable ? BlockState.ContinueDiscard : BlockState.Continue;
        }

        var paragraphBlock = definitionItem.LastChild as ParagraphBlock;
        if (lastBlankLine is null && paragraphBlock != null)
        {
            return BlockState.Continue;
        }

        // Remove the blankline before breaking this definition item
        if (lastBlankLine != null)
        {
            definitionItem.RemoveAt(definitionItem.Count - 1);
        }

        list.Span.End = list.LastChild!.Span.End;
        return BlockState.Break;
    }
}
