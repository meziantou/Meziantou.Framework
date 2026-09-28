// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// The inline parser used to transform a <see cref="ParagraphBlock"/> into a <see cref="Table"/> at inline parsing time.
/// </summary>
/// <seealso cref="InlineParser" />
/// <seealso cref="IPostInlineProcessor" />
public class PipeTableParser : InlineParser, IPostInlineProcessor
{
    private readonly LineBreakInlineParser _lineBreakParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeTableParser" /> class.
    /// </summary>
    /// <param name="lineBreakParser">The line break parser to use</param>
    /// <param name="options">The options.</param>
    public PipeTableParser(LineBreakInlineParser lineBreakParser, PipeTableOptions? options = null)
    {
        _lineBreakParser = lineBreakParser ?? throw new ArgumentNullException(nameof(lineBreakParser));
        OpeningCharacters = ['|', '\n', '\r', ':'];
        Options = options ?? new PipeTableOptions();
    }

    /// <summary>
    /// Gets the options.
    /// </summary>
    public PipeTableOptions Options { get; }

    /// <summary>
    /// Attempts to match the parser at the current position.
    /// </summary>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // Only working on Paragraph block
        if (!processor.Block!.IsParagraphBlock)
        {
            return false;
        }

        var c = slice.CurrentChar;

        if (c == ':')
        {
            var tableStateForColon = processor.ParserStates[Index] as TableState;
            if (tableStateForColon is null || !IsHeaderSeparatorColonBeforePipe(slice, tableStateForColon))
            {
                return false;
            }

            MatchLiteralColon(processor, ref slice);
            return true;
        }

        // If we have not a delimiter on the first line of a paragraph, don't bother to continue
        // tracking other delimiters on following lines
        var tableState = processor.ParserStates[Index] as TableState;
        bool isFirstLineEmpty = false;


        var position = processor.GetSourcePosition(slice.Start, out int globalLineIndex, out int column);
        var localLineIndex = globalLineIndex - processor.LineIndex;

        if (tableState is null)
        {
            // A table could be preceded by an empty line or a line containing an inline
            // that has not been added to the stack, so we consider this as a valid
            // start for a table. Typically, with this, we can have an attributes {...}
            // starting on the first line of a pipe table, even if the first line
            // doesn't have a pipe
            if (processor.Inline != null && (c == '\n' || c == '\r'))
            {
                return false;
            }

            if (processor.Inline is null)
            {
                isFirstLineEmpty = true;
            }

            // Else setup a table processor
            tableState = new TableState();
            processor.ParserStates[Index] = tableState;
        }

        if (c == '\n' || c == '\r')
        {
            if (!isFirstLineEmpty && !tableState.LineHasPipe)
            {
                // A line without a pipe ends the table: the table ends with the line break before it
                if (tableState.EndOfLines.Count == 0)
                {
                    tableState.IsInvalidTable = true;
                }
                else if (tableState.EndOfTable is null && !tableState.IsInvalidTable)
                {
                    // The pipes of the line are not column delimiters (e.g. in a code span). Its inlines are removed from
                    // this paragraph, and the line is parsed again in the paragraph after the table.
                    tableState.EndOfTable = tableState.EndOfLines[^1];
                    tableState.EndOfTableLineStart = tableState.LineStart;
                    tableState.IsLineAfterTableParsed = true;
                    if (EndTable(processor, tableState))
                    {
                        StopParsing(ref slice);
                        return true;
                    }
                }
            }
            tableState.LineHasPipe = false;
            _lineBreakParser.Match(processor, ref slice);
            if (!isFirstLineEmpty)
            {
                var lineBreak = processor.Inline!;
                tableState.ColumnAndLineDelimiters.Add(lineBreak);
                tableState.EndOfLines.Add(lineBreak);
                tableState.LineStart = slice.Start;
                if (tableState.EndOfTable is null && !tableState.IsInvalidTable)
                {
                    if (!CanHaveColumnDelimiter(processor, slice))
                    {
                        // The next line ends the table: when the lines before it are a table, the parse of this paragraph
                        // ends here, and the lines that follow are parsed once, in a paragraph after the table
                        processor.Emit(lineBreak);
                        tableState.EndOfTable = lineBreak;
                        tableState.EndOfTableLineStart = slice.Start;
                        if (EndTable(processor, tableState))
                        {
                            StopParsing(ref slice);
                        }
                    }
                    else
                    {
                        // The state to restore if the next line has no column delimiter: it is parsed again after the table
                        tableState.SaveLineStartState(processor);
                    }
                }
            }
        }
        else
        {
            processor.Inline = new PipeTableDelimiterInline(this)
            {
                Span = new SourceSpan(position, position),
                Line = globalLineIndex,
                Column = column,
                LocalLineIndex = localLineIndex,
                IsClosed = true  // Creates flat sibling structure for O(n) traversal
            };

            tableState.LineHasPipe = true;
            slice.SkipChar(); // Skip the `|` character

            tableState.ColumnAndLineDelimiters.Add(processor.Inline);
        }

        return true;
    }

    // Whether the lines before the end of the table are a table, which ends the paragraph: the lines after it are a new
    // paragraph. Else the paragraph is not a table: a table followed by other lines needs a header separator row, and must
    // not end inside an inline (a link) that it would split.
    private bool EndTable(InlineProcessor processor, TableState tableState)
    {
        var endOfTable = tableState.EndOfTable!;
        var delimiters = tableState.ColumnAndLineDelimiters;
        var columnDefinitions = FindHeaderRow(delimiters, delimiters.LastIndexOf(endOfTable) + 1);
        if (columnDefinitions is null || !IsOutsideOfInlines(endOfTable, processor.Root!) || processor.Block!.Parent is null)
        {
            tableState.IsInvalidTable = true;
            return false;
        }

        tableState.ColumnDefinitions = columnDefinitions;
        tableState.IsParsingStopped = true;
        return true;
    }

    // Ends the parse of the paragraph: the text that follows is parsed in the paragraph after the table
    private static void StopParsing(ref StringSlice slice)
    {
        slice.Start = slice.End + 1;
    }

    // Whether the line starting at the slice can have a column delimiter: it has a pipe that is not escaped by a backslash.
    // A pipe can also be in a code span, an HTML inline... but finding these inlines needs the other parsers.
    private static bool CanHaveColumnDelimiter(InlineProcessor processor, StringSlice slice)
    {
        var text = slice.Text;
        var end = slice.End;
        var escapes = processor.Parsers.GetParsersForOpeningCharacter('\\');
        var backslashEscapesPipes = escapes is { Length: > 0 } && escapes[0] is EscapeInlineParser;
        for (var i = slice.Start; i <= end; i++)
        {
            switch (text[i])
            {
                case '\n' or '\r':
                    return false;
                case '\\' when backslashEscapesPipes && i < end && text[i + 1].IsAsciiPunctuation():
                    // The escaped character is a literal
                    i++;
                    break;
                case '|':
                    return true;
            }
        }

        return false;
    }

    private static bool IsHeaderSeparatorColonBeforePipe(StringSlice slice, TableState tableState)
    {
        if (slice.PeekChar() != '|')
        {
            return false;
        }

        // The result only depends on the line, which is scanned entirely: reuse it for the other colons of the line,
        // otherwise a long line of ":|" is quadratic.
        var text = slice.Text;
        if (ReferenceEquals(tableState.ColonCheckText, text)
            && tableState.ColonCheckSliceEnd == slice.End
            && slice.Start >= tableState.ColonCheckLineStart
            && slice.Start <= tableState.ColonCheckLineEnd)
        {
            return tableState.ColonCheckResult;
        }

        int currentLineStart = FindLineStart(text, slice.Start);
        int currentLineEnd = FindLineEnd(text, slice.Start, slice.End);
        var result = IsHeaderSeparatorLine(text, currentLineStart, currentLineEnd);
        if (result)
        {
            int previousLineStart = FindPreviousLineStart(text, currentLineStart);
            result = previousLineStart >= 0 && LineHasPipe(text, previousLineStart, currentLineStart - 1);
        }

        tableState.ColonCheckText = text;
        tableState.ColonCheckSliceEnd = slice.End;
        tableState.ColonCheckLineStart = currentLineStart;
        tableState.ColonCheckLineEnd = currentLineEnd;
        tableState.ColonCheckResult = result;
        return result;
    }

    private static void MatchLiteralColon(InlineProcessor processor, ref StringSlice slice)
    {
        int position = processor.GetSourcePosition(slice.Start, out int line, out int column);
        if (processor.Inline is LiteralInline previousLiteral
            && ReferenceEquals(previousLiteral.Content.Text, slice.Text)
            && previousLiteral.Content.End + 1 == slice.Start)
        {
            previousLiteral.Content.End = slice.Start;
            previousLiteral.Span.End = position;
            processor.Inline = previousLiteral;
        }
        else
        {
            processor.Inline = new LiteralInline
            {
                Content = new StringSlice(slice.Text, slice.Start, slice.Start),
                Span = new SourceSpan(position, position),
                Line = line,
                Column = column,
            };
        }

        slice.SkipChar();
    }

    private static int FindLineStart(string text, int position)
    {
        for (int i = position - 1; i >= 0; i--)
        {
            if (text[i] == '\r' || text[i] == '\n')
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static int FindLineEnd(string text, int position, int sliceEnd)
    {
        int end = Math.Min(sliceEnd, text.Length - 1);
        for (int i = position; i <= end; i++)
        {
            if (text[i] == '\r' || text[i] == '\n')
            {
                return i - 1;
            }
        }

        return end;
    }

    private static int FindPreviousLineStart(string text, int currentLineStart)
    {
        int previousLineEnd = currentLineStart - 2;
        if (previousLineEnd >= 0 && text[previousLineEnd] == '\r')
        {
            previousLineEnd--;
        }

        if (previousLineEnd < 0)
        {
            return -1;
        }

        for (int i = previousLineEnd; i >= 0; i--)
        {
            if (text[i] == '\r' || text[i] == '\n')
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static bool IsHeaderSeparatorLine(string text, int start, int end)
    {
        bool hasPipe = false;
        bool hasDash = false;

        for (int i = start; i <= end; i++)
        {
            switch (text[i])
            {
                case '|':
                    hasPipe = true;
                    break;
                case '-':
                    hasDash = true;
                    break;
                case ':':
                case ' ':
                case '\t':
                    break;
                default:
                    return false;
            }
        }

        return hasPipe && hasDash;
    }

    private static bool LineHasPipe(string text, int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            if (text[i] == '|')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Performs the post process operation.
    /// </summary>
    public bool PostProcess(InlineProcessor state, Inline? root, Inline? lastChild, int postInlineProcessorIndex, bool isFinalProcessing)
    {
        var container = root as ContainerInline;
        var tableState = state.ParserStates[Index] as TableState;

        // If the delimiters are being processed by an image link, we need to transform them back to literals
        if (!isFinalProcessing)
        {
            if (container is null || tableState is null)
            {
                return true;
            }

            // With flat structure, pipes are siblings at root level
            // Walk backwards from the last child to find pipe delimiters
            var child = container.LastChild;
            List<PipeTableDelimiterInline>? delimitersToRemove = null;

            while (child != null)
            {
                if (child is PipeTableDelimiterInline pipeDelimiter)
                {
                    delimitersToRemove ??= new List<PipeTableDelimiterInline>();

                    delimitersToRemove.Add(pipeDelimiter);
                }

                if (child == lastChild)
                {
                    break;
                }

                // Walk siblings instead of descending into containers
                child = child.PreviousSibling;
            }

            // If we have found any delimiters, transform them to literals
            if (delimitersToRemove != null)
            {
                bool leftIsDelimiter = false;
                bool rightIsDelimiter = false;
                for (int i = 0; i < delimitersToRemove.Count; i++)
                {
                    var pipeDelimiter = delimitersToRemove[i];
                    pipeDelimiter.ReplaceByLiteral();

                    // Check that the pipe that is being removed is not going to make a line without pipe delimiters
                    var tableDelimiters = tableState.ColumnAndLineDelimiters;
                    var delimiterIndex = tableDelimiters.IndexOf(pipeDelimiter);

                    if (i == 0)
                    {
                        leftIsDelimiter = delimiterIndex > 0 && tableDelimiters[delimiterIndex - 1] is PipeTableDelimiterInline;
                    }
                    else if (i + 1 == delimitersToRemove.Count)
                    {
                        rightIsDelimiter = delimiterIndex + 1 < tableDelimiters.Count &&
                                           tableDelimiters[delimiterIndex + 1] is PipeTableDelimiterInline;
                    }
                    // Remove this delimiter from the table processor
                    tableState.ColumnAndLineDelimiters.Remove(pipeDelimiter);
                }

                // If we didn't have any delimiter before and after the delimiters we just removed, we mark the processor of the current line as no pipe
                if (!leftIsDelimiter && !rightIsDelimiter)
                {
                    tableState.LineHasPipe = false;
                }
            }

            return true;
        }

        // Remove previous state
        state.ParserStates[Index] = null!;

        // Abort if not a valid table
        if (tableState is null || container is null || tableState.IsInvalidTable)
        {
            if (tableState is not null)
            {
                ReplacePipesByLiterals(tableState.ColumnAndLineDelimiters, 0);
            }
            return true;
        }

        // The table ends before the first line without a pipe, which starts a paragraph after the table
        var lastTableLineBreak = tableState.EndOfTable;
        var trailingTextStart = tableState.EndOfTableLineStart;
        if (lastTableLineBreak is null && !tableState.LineHasPipe)
        {
            // The last line has no column delimiter
            lastTableLineBreak = tableState.EndOfLines.Count > 0 ? tableState.EndOfLines[^1] : null;
            if (lastTableLineBreak is null)
            {
                ReplacePipesByLiterals(tableState.ColumnAndLineDelimiters, 0);
                return true;
            }

            trailingTextStart = tableState.LineStart;
            tableState.IsLineAfterTableParsed = true;
        }

        // Detect the header row
        var delimiters = tableState.ColumnAndLineDelimiters;
        var endOfTableIndex = lastTableLineBreak is null ? delimiters.Count : delimiters.IndexOf(lastTableLineBreak) + 1;
        // When the parse stopped at the end of the table, the table was validated there: the text after it is only parsed
        // in the paragraph after the table
        var aligns = tableState.IsParsingStopped ? tableState.ColumnDefinitions : FindHeaderRow(delimiters, endOfTableIndex);

        // A table followed by other lines needs a header separator row, and these lines must not be inside an inline (a
        // link) that the table would split
        if (!tableState.IsParsingStopped &&
            ((aligns is null && (Options.RequireHeaderSeparator || lastTableLineBreak is not null)) ||
            (lastTableLineBreak is not null && (!IsOutsideOfInlines(lastTableLineBreak, container) || state.Block!.Parent is null))))
        {
            // No valid header separator found - convert all pipe delimiters to literals
            ReplacePipesByLiterals(delimiters, 0);
            return true;
        }

        ParagraphBlock? trailingParagraph = null;
        if (lastTableLineBreak is not null)
        {
            ReplacePipesByLiterals(delimiters, endOfTableIndex);
            delimiters.RemoveRange(endOfTableIndex, delimiters.Count - endOfTableIndex);
            var endOfLinesCount = tableState.EndOfLines.IndexOf(lastTableLineBreak) + 1;
            tableState.EndOfLines.RemoveRange(endOfLinesCount, tableState.EndOfLines.Count - endOfLinesCount);

            // The line after the table was parsed in this paragraph when its pipes had to be parsed to know that they are not
            // column delimiters. Its inlines are removed below, and it is parsed again in the paragraph after the table.
            if (tableState.IsLineAfterTableParsed)
            {
                tableState.RestoreLineStartState(state);
            }

            // The paragraph after the table has the rest of the text, which is only parsed there
            trailingParagraph = state.CreateParagraphFromText(trailingTextStart);
        }

        var table = new Table();

        // If the current paragraph block has any attributes attached, we can copy them
        var attributes = state.Block!.TryGetAttributes();
        if (attributes != null)
        {
            attributes.CopyTo(table.GetAttributes());
        }

        var cells = tableState.Cells;
        cells.Clear();

        // Pipe tables are post-processed before emphasis is resolved so that table boundaries win over
        // emphasis/subscript spans. At this point unmatched emphasis-extra delimiters may still be open
        // containers; for example `**~$0.0015**` leaves an unresolved `~` delimiter inside the delimiter
        // structure for the bold cell.
        // If a pipe or line break was emitted while such a container was open, it is not a root-level
        // sibling and cannot be used reliably as a cell/row boundary. Normalize these delimiters first;
        // cell contents are post-processed for emphasis again after each cell has been isolated.
        if (HasNestedDelimiters(delimiters, container))
        {
            PromoteNestedLineBreaksToRootLevel(container);
        }
        PromoteNestedPipesToRootLevel(delimiters, container);

        if (lastTableLineBreak is not null)
        {
            // The inlines after the table are the ones of the line after it when it was parsed, and the trivia of the paragraph
            while (lastTableLineBreak.NextSibling is { } trailingInline)
            {
                trailingInline.Remove();
            }
        }

        // The inline tree is now flat: all pipes and line breaks are siblings at root level.
        // For example, `| a | b \n| c | d \n` produces:
        //     [|] [a] [|] [b] [\n] [|] [c] [|] [d] [\n]
        //
        // Tables support four row formats:
        //     | a | b |    (leading and trailing pipes)
        //     | a | b      (leading pipe only)
        //       a | b      (no leading or trailing pipes)
        //       a | b |    (trailing pipe only)

        // Ensure the table ends with a line break to simplify row detection
        var lastElement = delimiters[delimiters.Count - 1];
        if (!(lastElement is LineBreakInline))
        {
            // Find the actual last sibling (there may be content after the last delimiter)
            while (lastElement.NextSibling != null)
            {
                lastElement = lastElement.NextSibling;
            }

            var endOfTable = new LineBreakInline();
            lastElement.InsertAfter(endOfTable);
            delimiters.Add(endOfTable);
            tableState.EndOfLines.Add(endOfTable);
        }

        int lastPipePos = 0;

        // Build table rows and cells by iterating through delimiters
        TableRow? row = null;
        TableRow? firstRow = null;
        for (int i = 0; i < delimiters.Count; i++)
        {
            var delimiter = delimiters[i];
            var pipeSeparator = delimiter as PipeTableDelimiterInline;
            var isLine = delimiter is LineBreakInline;

            if (row is null)
            {
                row = new TableRow();

                firstRow ??= row;

                // Skip leading pipe at start of row (e.g., `| a | b` or `| a | b |`). Delimiter promotion
                // can leave empty wrapper inlines before the pipe; ignore them so a leading pipe is not
                // mistaken for an empty first cell.
                var previousSignificantSibling = SkipPreviousEmptyInlines(delimiter.PreviousSibling);
                if (pipeSeparator != null && (previousSignificantSibling is null || previousSignificantSibling is LineBreakInline))
                {
                    delimiter.Remove();
                    if (table.Span.IsEmpty)
                    {
                        table.Span = delimiter.Span;
                        table.Line = delimiter.Line;
                        table.Column = delimiter.Column;
                    }
                    continue;
                }
            }

            // Find cell content by walking backwards from this delimiter to the previous pipe or line break.
            // For `| a | b \n` at delimiter 'x':
            //       [|] [a] [x] [b] [\n]
            //                ^--- current delimiter
            // Walk back: [a] is the cell content (stop at [|])
            Inline? endOfCell = null;
            Inline? beginOfCell = null;
            var cellContentIt = delimiter.PreviousSibling;
            while (cellContentIt != null)
            {
                if (cellContentIt is LineBreakInline || cellContentIt is PipeTableDelimiterInline)
                    break;

                // Stop at the root ContainerInline (which is not necessary to bring into the tree + it contains an invalid span calculation)
                if (cellContentIt.GetType() == typeof(ContainerInline) && cellContentIt.Parent is null)
                    break;

                beginOfCell = cellContentIt;
                endOfCell ??= beginOfCell;

                cellContentIt = cellContentIt.PreviousSibling;
            }

            // A pipe delimiter always starts a new cell. A line break only closes a cell when there is real
            // content between it and the previous boundary. Delimiter promotion may leave only whitespace or
            // empty plain containers in that range; treating those as empty avoids phantom cells at row ends.
            if (!isLine || (beginOfCell != null && endOfCell != null && !IsEmptyInlineRange(beginOfCell, endOfCell)))
            {
                // We trim whitespace at the beginning and ending of the cell
                TrimStart(beginOfCell);
                TrimEnd(endOfCell);

                var cellContainer = new ContainerInline();

                // Copy elements from beginOfCell on the first level
                // The pipe delimiter serves as a boundary - stop when we hit it
                var cellIt = beginOfCell;
                while (cellIt != null && !IsLine(cellIt) && !(cellIt is PipeTableDelimiterInline))
                {
                    var nextSibling = cellIt.NextSibling;

                    // Skip empty inlines (can result from trimming or delimiter promotion)
                    if (IsEmptyInline(cellIt))
                    {
                        cellIt.Remove();
                        cellIt = nextSibling;
                        continue;
                    }

                    cellIt.Remove();
                    AppendCellInline(cellContainer, cellIt);
                    cellIt = nextSibling;
                }

                TrimStart(cellContainer.FirstChild);
                TrimEnd(cellContainer.LastChild);
                RemoveEmptyBoundaryInlines(cellContainer);

                if (!isLine)
                {
                    // Remove the pipe delimiter AFTER copying cell content
                    // This preserves the sibling chain during the copy loop
                    delimiter.Remove();
                    lastPipePos = delimiter.Span.End;
                }

                // Create the cell and add it to the pending row
                var tableParagraph = new ParagraphBlock
                {
                    Span = cellContainer.Span,
                    Line = cellContainer.Line,
                    Column = cellContainer.Column,
                    Inline = cellContainer
                };

                var tableCell = new TableCell()
                {
                    Span = cellContainer.Span,
                    Line = cellContainer.Line,
                    Column = cellContainer.Column,
                };

                tableCell.Add(tableParagraph);
                if (row.Span.IsEmpty)
                {
                    row.Span = cellContainer.Span;
                    row.Line = cellContainer.Line;
                    row.Column = cellContainer.Column;
                }
                row.Add(tableCell);
                cells.Add(tableCell);
            }

            // If we have a new line, we can add the row
            if (isLine)
            {
                Debug.Assert(row != null);
                if (table.Span.IsEmpty)
                {
                    table.Span = row!.Span;
                    table.Line = row.Line;
                    table.Column = row.Column;
                }
                table.Add(row!);
                row = null;
            }
        }

        if (lastPipePos > table.Span.End)
        {
          table.UpdateSpanEnd(lastPipePos);
        }

        // Once we are done with the cells, we can remove all end of lines in the table tree
        foreach (var endOfLine in tableState.EndOfLines)
        {
            endOfLine.Remove();
        }

        // Mark first row as header and remove the separator row if present
        var tableRow = (TableRow)table[0];
        tableRow.IsHeader = Options.RequireHeaderSeparator;
        if (aligns != null)
        {
            tableRow.IsHeader = true;
            table.RemoveAt(1);
            table.ColumnDefinitions.AddRange(aligns);
        }

        // Perform all post-processors on cell content. The first pass deliberately ran pipe tables before
        // emphasis so an unresolved emphasis/subscript delimiter could not claim pipes from neighboring
        // cells. Now each cell has its own inline root, so re-running from index 0 lets emphasis, links, and
        // other post-processors resolve normally, but only within that cell.
        foreach (var cell in cells)
        {
            var paragraph = (ParagraphBlock) cell[0];
            state.PostProcessInlines(0, paragraph.Inline, null, true);
            if (paragraph.Inline?.LastChild is not null)
            {
                paragraph.Inline.Span.End = paragraph.Inline.LastChild.Span.End;
                paragraph.UpdateSpanEnd(paragraph.Inline.LastChild.Span.End);
            }
        }

        // Clear cells when we are done
        cells.Clear();

        // Normalize the table
        if (Options.UseHeaderForColumnCount)
        {
            table.NormalizeUsingHeaderRow(state.Document);
        }
        else
        {
            table.NormalizeUsingMaxWidth(state.Document);
        }

        if (state.Block is ParagraphBlock { Inline.FirstChild: not null } leadingParagraph)
        {
            // The table was preceded by a non-empty paragraph, e.g.
            // ```md
            // Some text
            // | Header |
            // ```
            //
            // Keep the paragraph as-is and insert the table after it.
            // We've processed inlines in the table, but not the leading paragraph itself yet.
            state.PostProcessInlines(0, leadingParagraph.Inline, null, isFinalProcessing: true);
            state.InsertBlockAfter(table, isProcessed: true);
        }
        else
        {
            // Nothing interesting in the existing block, just replace it.
            state.BlockNew = table;
        }

        if (trailingParagraph is not null)
        {
            state.InsertBlockAfter(trailingParagraph, isProcessed: false);
        }

        // We don't want to continue procesing delimiters, as we are already processing them here
        return false;
    }

    private static bool ParseHeaderString(Inline? inline, out TableColumnAlign? align, out int delimiterCount)
    {
        align = 0;
        delimiterCount = 0;
        var literal = inline as LiteralInline;
        if (literal is null)
        {
            return false;
        }

        // Work on a copy of the slice
        var line = literal.Content;
        if (TableHelper.ParseColumnHeader(ref line, '-', out align, out delimiterCount))
        {
            if (line.CurrentChar != '\0')
            {
                return false;
            }
            return true;
        }

        return false;
    }

    private static void ReplacePipesByLiterals(List<Inline> delimiters, int start)
    {
        for (var i = start; i < delimiters.Count; i++)
        {
            if (delimiters[i] is PipeTableDelimiterInline pipeDelimiter)
            {
                pipeDelimiter.ReplaceByLiteral();
            }
        }
    }

    // Whether the inline is at the root of the paragraph, or only inside delimiters that the table flattens
    private static bool IsOutsideOfInlines(Inline inline, ContainerInline root)
    {
        for (var parent = inline.Parent; parent is not null && parent != root; parent = parent.Parent)
        {
            if (parent is not DelimiterInline)
            {
                return false;
            }
        }

        return true;
    }

    // count: the number of delimiters of the table, the delimiters after them belong to the lines that follow the table
    private List<TableColumnDefinition>? FindHeaderRow(List<Inline> delimiters, int count)
    {
        bool isValidRow = false;
        int totalDelimiterCount = 0;
        List<TableColumnDefinition>? columnDefinitions = null;
        for (int i = 0; i < count; i++)
        {
            if (!IsLine(delimiters[i]))
            {
                continue;
            }

            // The separator row is removed from the table, so it must only contain pipes, dashes, colons and whitespace.
            // Any other inline (emphasis, link, code...) would be lost.
            if (!IsSeparatorRowText(delimiters[i].NextSibling))
            {
                break;
            }

            // Parse the separator row (second row) to extract column alignments
            for (int j = i + 1; j < count; j++)
            {
                var delimiter = delimiters[j];
                var nextDelimiter = j + 1 < count ? delimiters[j + 1] : null;

                var columnDelimiter = delimiter as PipeTableDelimiterInline;
                if (j == i + 1 && IsStartOfLineColumnDelimiter(columnDelimiter))
                {
                    continue;
                }

                if (IsLine(delimiter))
                {
                    var previousSignificantSibling = SkipTrailingWhitespace(delimiter.PreviousSibling);

                    // Trailing pipe at end-of-line (`| --- |`) is valid and does not add a new column.
                    if (previousSignificantSibling is PipeTableDelimiterInline)
                    {
                        isValidRow = columnDefinitions is { Count: > 0 };
                        break;
                    }

                    if (!TryParseHeaderColumn(previousSignificantSibling, out var align, out var delimiterCount))
                    {
                        break;
                    }

                    columnDefinitions ??= [];
                    totalDelimiterCount += delimiterCount;
                    columnDefinitions.Add(new TableColumnDefinition() { Alignment = align, Width = delimiterCount });
                    isValidRow = true;
                    break;
                }

                // Parse the content before this delimiter as a column definition (e.g., `:---`, `---:`, `:---:`)
                if (!TryParseHeaderColumn(delimiter.PreviousSibling, out var pipeAlign, out var pipeDelimiterCount))
                {
                    break;
                }

                // Create aligns until we may have a header row

                columnDefinitions ??= new List<TableColumnDefinition>();
                totalDelimiterCount += pipeDelimiterCount;
                columnDefinitions.Add(new TableColumnDefinition() { Alignment =  pipeAlign, Width = pipeDelimiterCount});

                // If this is the last pipe, check for a trailing column definition (row without trailing pipe)
                // e.g., `| :--- | ---:` has content after the last pipe
                if (nextDelimiter is null)
                {
                    var nextSibling = delimiter.NextSibling;

                    // No trailing content means row ends with pipe: `| :--- |`
                    if (IsNullOrSpace(nextSibling))
                    {
                        isValidRow = true;
                        break;
                    }

                    if (!TryParseHeaderColumn(nextSibling, out var trailingAlign, out var trailingDelimiterCount))
                    {
                        break;
                    }
                    totalDelimiterCount += trailingDelimiterCount;
                    isValidRow = true;
                    columnDefinitions.Add(new TableColumnDefinition() { Alignment = trailingAlign, Width = trailingDelimiterCount });
                    break;
                }
            }
            break;
        }

        // calculate the width of the columns in percent based on the delimiter count
        if (!isValidRow || columnDefinitions == null || totalDelimiterCount == 0)
        {
            return null;
        }

        if (Options.InferColumnWidthsFromSeparator)
        {
            foreach (var columnDefinition in columnDefinitions)
            {
                var dashCount = (int)columnDefinition.Width;
                columnDefinition.Width = (columnDefinition.Width * 100) / totalDelimiterCount;
                columnDefinition.SeparatorDashCount = dashCount;
            }
        }
        else
        {
            foreach (var columnDefinition in columnDefinitions)
            {
                columnDefinition.Width = 0;
            }
        }
        return columnDefinitions;
    }

    private static bool TryParseHeaderColumn(Inline? inline, out TableColumnAlign? align, out int delimiterCount)
    {
        align = null;
        delimiterCount = 0;

        // Empty cells are allowed; FindHeaderRow requires at least one dash across the row.
        if (inline is null || inline is PipeTableDelimiterInline)
        {
            return true;
        }

        if (inline is LiteralInline literal && literal.Content.IsEmptyOrWhitespace())
        {
            return true;
        }

        return ParseHeaderString(inline, out align, out delimiterCount);
    }

    private static Inline? SkipTrailingWhitespace(Inline? inline)
    {
        while (inline is LiteralInline literal && literal.Content.IsEmptyOrWhitespace())
        {
            inline = inline.PreviousSibling;
        }

        return inline;
    }

    private static Inline? SkipPreviousEmptyInlines(Inline? inline)
    {
        // Promotion can split an unresolved delimiter container into an empty plain ContainerInline followed
        // by the promoted pipe. Such containers are structural leftovers, not table content.
        while (inline is not null && IsEmptyOrWhitespaceInline(inline))
        {
            inline = inline.PreviousSibling;
        }

        return inline;
    }

    private static bool IsEmptyInlineRange(Inline begin, Inline end)
    {
        var inline = begin;
        while (inline is not null)
        {
            if (!IsEmptyOrWhitespaceInline(inline))
            {
                return false;
            }

            if (inline == end)
            {
                return true;
            }

            inline = inline.NextSibling;
        }

        return true;
    }

    private static bool IsEmptyInline(Inline inline)
    {
        return IsEmptyInline(inline, whitespaceIsEmpty: false);
    }

    private static bool IsEmptyOrWhitespaceInline(Inline inline)
    {
        return IsEmptyInline(inline, whitespaceIsEmpty: true);
    }

    private static bool IsEmptyInline(Inline inline, bool whitespaceIsEmpty)
    {
        if (inline is LiteralInline literal)
        {
            return whitespaceIsEmpty ? literal.Content.IsEmptyOrWhitespace() : literal.Content.IsEmpty;
        }

        // Only a plain ContainerInline is considered synthetic/transparent here. Derived containers such as
        // EmphasisInline or PipeTableDelimiterInline carry markdown semantics and must remain visible.
        if (inline.GetType() == typeof(ContainerInline))
        {
            var child = ((ContainerInline)inline).FirstChild;
            while (child is not null)
            {
                if (!IsEmptyInline(child, whitespaceIsEmpty))
                {
                    return false;
                }

                child = child.NextSibling;
            }

            return true;
        }

        return false;
    }

    private static bool IsLine(Inline inline)
    {
        return inline is LineBreakInline;
    }

    private static bool IsSeparatorRowText(Inline? inline)
    {
        for (; inline is not null && !IsLine(inline); inline = inline.NextSibling)
        {
            if (inline is PipeTableDelimiterInline)
            {
                continue;
            }

            if (inline is not LiteralInline literal)
            {
                return false;
            }

            foreach (var c in literal.Content.AsSpan())
            {
                if (c is not ('-' or ':') && !c.IsWhitespace())
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsStartOfLineColumnDelimiter(Inline? inline)
    {
        if (inline is null)
        {
            return false;
        }

        var previous = inline.PreviousSibling;
        if (previous is null)
        {
            return true;
        }

        if (previous is LiteralInline literal)
        {
            if (!literal.Content.IsEmptyOrWhitespace())
            {
                return false;
            }
            previous = previous.PreviousSibling;
        }
        return previous is null || IsLine(previous);
    }

    private static void TrimStart(Inline? inline)
    {
        while (inline is ContainerInline containerInline && !(containerInline is DelimiterInline))
        {
            inline = containerInline.FirstChild;
        }

        if (inline is LiteralInline literal)
        {
            literal.Content.TrimStart();
        }
    }

    private static void TrimEnd(Inline? inline)
    {
        // Walk into containers to find the last leaf to trim
        // Skip PipeTableDelimiterInline but walk into other containers (including emphasis)
        while (inline is ContainerInline container && !(inline is PipeTableDelimiterInline))
        {
            inline = container.LastChild;
        }

        if (inline is LiteralInline literal)
        {
            literal.Content.TrimEnd();
        }
    }

    private static bool IsNullOrSpace(Inline? inline)
    {
        if (inline is null)
        {
            return true;
        }

        if (inline is LiteralInline literal)
        {
            return literal.Content.IsEmptyOrWhitespace();
        }
        return false;
    }

    private static void AppendCellInline(ContainerInline cellContainer, Inline inline)
    {
        // SplitContainerAtDelimiter introduces plain wrappers for content that used to live after a promoted
        // boundary inside an unresolved delimiter. Flatten those wrappers while moving content into the cell;
        // keeping them would make trimming/emptiness checks see artificial cell content. Semantic containers
        // are preserved because the type check below only matches exactly ContainerInline.
        if (inline.GetType() == typeof(ContainerInline))
        {
            var container = (ContainerInline)inline;
            var child = container.FirstChild;
            while (child is not null)
            {
                var next = child.NextSibling;
                child.Remove();
                AppendCellInline(cellContainer, child);
                child = next;
            }

            return;
        }

        if (IsEmptyInline(inline))
        {
            return;
        }

        if (cellContainer.Span.IsEmpty)
        {
            cellContainer.Line = inline.Line;
            cellContainer.Column = inline.Column;
            cellContainer.Span = inline.Span;
        }

        cellContainer.AppendChild(inline);
        cellContainer.Span.End = inline.Span.End;
    }

    private static void RemoveEmptyBoundaryInlines(ContainerInline container)
    {
        // After flattening, trimming can expose empty synthetic wrappers at the cell boundaries. Drop them so
        // the cell span and later inline post-processing are based on real cell content.
        while (container.FirstChild is not null && IsEmptyOrWhitespaceInline(container.FirstChild))
        {
            container.FirstChild.Remove();
        }

        while (container.LastChild is not null && IsEmptyOrWhitespaceInline(container.LastChild))
        {
            container.LastChild.Remove();
        }

        container.UpdateSpanFromChildren(preserveSelfSpan: false);
    }

    private static bool HasNestedDelimiters(List<Inline> delimiters, ContainerInline root)
    {
        for (int i = 0; i < delimiters.Count; i++)
        {
            if (delimiters[i].Parent != root)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Promotes nested line breaks to root level.
    /// </summary>
    /// <remarks>
    /// Once any tracked table delimiter is nested, row boundaries may be nested in the same unresolved inline
    /// container. Promoting pipes without promoting those line breaks would let cell extraction walk across
    /// physical rows and produce incorrect cells.
    /// </remarks>
    private static void PromoteNestedLineBreaksToRootLevel(ContainerInline root)
    {
        PromoteNestedInlinesToRootLevel(root, static (inline, _) => inline is LineBreakInline, state: (object?)null);
    }

    /// <summary>
    /// Promotes nested pipe delimiters and tracked line breaks to root level.
    /// </summary>
    /// <remarks>
    /// Handles cases like `*a | b*|` and `**~$0.01** |` where a table boundary was emitted while an emphasis
    /// delimiter container was still open. After promotion, all tracked table delimiters become root-level
    /// siblings, so the table parser can count columns without depending on unresolved inline structure.
    /// </remarks>
    private static void PromoteNestedPipesToRootLevel(List<Inline> delimiters, ContainerInline root)
    {
        if (!HasNestedDelimiters(delimiters, root))
        {
            return;
        }

        var tracked = new HashSet<Inline>(delimiters.Count, ReferenceEqualityComparer.Instance);
        foreach (var delimiter in delimiters)
        {
            if (delimiter is PipeTableDelimiterInline or LineBreakInline)
            {
                tracked.Add(delimiter);
            }
        }

        PromoteNestedInlinesToRootLevel(root, static (inline, tracked) => tracked.Contains(inline), tracked);
    }

    /// <summary>
    /// Promotes the nested inlines matching <paramref name="isBoundary"/> to root level, in document order: each one splits
    /// its parent, whose inlines after it move to a plain container, and is inserted after the child of the root that has
    /// it, followed by that container.
    /// </summary>
    /// <remarks>
    /// For input `*a | b*`, the pipe is inside the emphasis container:
    ///     EmphasisDelimiter { "a", Pipe, "b" }
    /// After splitting:
    ///     EmphasisDelimiter { "a" }, Pipe, Container { "b" }
    /// The tail container is intentionally plain. It keeps post-pipe content attached to the root until cell
    /// extraction moves it into a TableCell, without preserving an unresolved delimiter span across the pipe.
    ///
    /// Moving the inlines after each boundary one boundary at a time moves them again for each boundary that follows
    /// in the same container, which is quadratic. The inlines are walked once instead, to find where each boundary
    /// goes and which inlines follow it, and then each inline is moved once. The result is the same: a boundary is
    /// inserted right after the child of the root that has it when it is promoted (the parent of the inlines after a
    /// previous boundary of the same container is the container of that previous boundary, which is a child of the
    /// root), so the boundaries promoted after one child of the root come before the ones promoted earlier.
    /// </remarks>
    private static void PromoteNestedInlinesToRootLevel<TState>(ContainerInline root, Func<Inline, TState, bool> isBoundary, TState state)
    {
        // The boundaries promoted after each child of the root, with the container of the inlines that follow them
        Dictionary<Inline, List<(Inline Boundary, ContainerInline? Tail)>>? promotions = null;
        List<(Inline Boundary, ContainerInline? Tail)>? boundaries = null;

        // The containers being walked, with the container that has their next children (the container of the inlines
        // after their last boundary) and the child of the root that has it
        var stack = new Stack<(Inline Child, ContainerInline Segment, Inline Top)>();
        for (var rootChild = root.FirstChild; rootChild is not null; rootChild = rootChild.NextSibling)
        {
            if (rootChild is not ContainerInline { FirstChild: { } first } rootContainer)
            {
                continue;
            }

            stack.Push((first, rootContainer, rootContainer));
            while (stack.Count > 0)
            {
                var (child, segment, top) = stack.Pop();
                if (child.NextSibling is { } next)
                {
                    stack.Push((next, segment, top));
                }

                if (isBoundary(child, state))
                {
                    ContainerInline? tail = null;
                    if (child.NextSibling is not null)
                    {
                        tail = CreateSplitContainer(segment);

                        // The next children of the container go to the tail, which is a child of the root
                        stack.Pop();
                        stack.Push((child.NextSibling, tail, tail));
                    }

                    promotions ??= new(ReferenceEqualityComparer.Instance);
                    if (!promotions.TryGetValue(top, out var list))
                    {
                        list = [];
                        promotions.Add(top, list);
                    }

                    list.Add((child, tail));
                    (boundaries ??= []).Add((child, tail));

                    // A boundary with children is a child of the root when its children are promoted
                    if (child is ContainerInline { FirstChild: { } boundaryChild } boundaryContainer)
                    {
                        stack.Push((boundaryChild, boundaryContainer, boundaryContainer));
                    }
                }
                else if (child is ContainerInline { FirstChild: { } grandChild } container)
                {
                    stack.Push((grandChild, container, top));
                }
            }
        }

        if (boundaries is null)
        {
            return;
        }

        // Move the inlines after each boundary to its tail, until the next boundary of the same container
        foreach (var (boundary, tail) in boundaries)
        {
            var next = boundary.NextSibling;
            boundary.Remove();
            while (tail is not null && next is not null && !isBoundary(next, state))
            {
                var inline = next;
                next = next.NextSibling;
                inline.Remove();
                tail.AppendChild(inline);
            }
        }

        // Insert the boundaries after the children of the root that have them
        var children = new List<Inline>();
        for (var rootChild = root.FirstChild; rootChild is not null; rootChild = rootChild.NextSibling)
        {
            children.Add(rootChild);
        }

        foreach (var rootChild in children)
        {
            if (promotions!.ContainsKey(rootChild))
            {
                InsertPromotedInlines(rootChild, promotions);
            }
        }
    }

    // Inserts the boundaries promoted after the given child of the root, the last one first. Each one is followed by the
    // boundaries promoted after it (when it has children), its tail, and the boundaries promoted after its tail.
    private static void InsertPromotedInlines(Inline top, Dictionary<Inline, List<(Inline Boundary, ContainerInline? Tail)>> promotions)
    {
        // The inlines to insert, in reverse order, and the inlines whose promoted boundaries are inserted next (Expand)
        var pending = new Stack<(Inline Inline, bool Expand)>();
        pending.Push((top, true));
        var last = top;
        while (pending.Count > 0)
        {
            var (inline, expand) = pending.Pop();
            if (!expand)
            {
                last.InsertAfter(inline);
                last = inline;
                continue;
            }

            if (promotions.TryGetValue(inline, out var list))
            {
                foreach (var (boundary, tail) in list)
                {
                    if (tail is not null)
                    {
                        pending.Push((tail, true));
                        pending.Push((tail, false));
                    }

                    pending.Push((boundary, true));
                    pending.Push((boundary, false));
                }
            }
        }
    }

    /// <summary>
    /// Creates a plain container to wrap content split from the source container.
    /// </summary>
    private static ContainerInline CreateSplitContainer(ContainerInline source)
    {
        // The split tail is post-processed after the table cells are isolated.
        // Use a plain container so unresolved delimiters cannot keep spanning cell boundaries.
        return new ContainerInline
        {
            Span = source.Span,
            Line = source.Line,
            Column = source.Column
        };
    }

    private sealed class TableState
    {
        public bool IsInvalidTable { get; set; }

        // The line break before the first line without a pipe, which ends the table
        public Inline? EndOfTable { get; set; }

        // The position in the text of the first line after the table
        public int EndOfTableLineStart { get; set; }

        // Whether the parse of the paragraph ended with the table: the text after it is parsed in the paragraph after the table
        public bool IsParsingStopped { get; set; }

        // Whether the first line after the table was parsed in the paragraph of the table
        public bool IsLineAfterTableParsed { get; set; }

        // The columns of the table, found when the parse stopped at its end
        public List<TableColumnDefinition>? ColumnDefinitions { get; set; }

        // The position in the text of the line after the last line break
        public int LineStart { get; set; }

        // The state of the processor when the line after the last line break starts. If it ends the table, it is parsed
        // again after the table, so its changes to the processor are undone: the references it expands, and the attributes
        // it adds to the paragraph (copied to the table). The attributes of a block only grow while its inlines are parsed:
        // an id replaces the previous one, and classes and properties are appended. So their state is the id and the
        // number of classes and properties, which is saved at each line break without copying them.
        public long ReferenceExpansionAtLineStart { get; set; }

        public HtmlAttributes? AttributesAtLineStart { get; set; }

        public string? IdAtLineStart { get; set; }

        // -1 when there is no list
        public int ClassCountAtLineStart { get; set; }

        public int PropertyCountAtLineStart { get; set; }

        public SourceSpan AttributesSpanAtLineStart { get; set; }

        public int AttributesLineAtLineStart { get; set; }

        public int AttributesColumnAtLineStart { get; set; }

        public void SaveLineStartState(InlineProcessor processor)
        {
            ReferenceExpansionAtLineStart = processor.ReferenceExpansionLength;
            var attributes = processor.Block!.TryGetAttributes();
            AttributesAtLineStart = attributes;
            if (attributes is not null)
            {
                IdAtLineStart = attributes.Id;
                ClassCountAtLineStart = attributes.Classes?.Count ?? -1;
                PropertyCountAtLineStart = attributes.Properties?.Count ?? -1;
                AttributesSpanAtLineStart = attributes.Span;
                AttributesLineAtLineStart = attributes.Line;
                AttributesColumnAtLineStart = attributes.Column;
            }
        }

        public void RestoreLineStartState(InlineProcessor processor)
        {
            processor.ReferenceExpansionLength = ReferenceExpansionAtLineStart;
            var block = processor.Block!;
            if (AttributesAtLineStart is not { } attributes)
            {
                block.RemoveData(typeof(HtmlAttributes));
                return;
            }

            block.SetAttributes(attributes);
            attributes.Id = IdAtLineStart;
            attributes.Classes = Truncate(attributes.Classes, ClassCountAtLineStart);
            attributes.Properties = Truncate(attributes.Properties, PropertyCountAtLineStart);
            attributes.Span = AttributesSpanAtLineStart;
            attributes.Line = AttributesLineAtLineStart;
            attributes.Column = AttributesColumnAtLineStart;
        }

        // The list with its first items, or null when count is -1 (there was no list)
        private static List<T>? Truncate<T>(List<T>? list, int count)
        {
            if (count < 0)
            {
                return null;
            }

            list!.RemoveRange(count, list.Count - count);
            return list;
        }

        public bool LineHasPipe { get; set; }

        public List<Inline> ColumnAndLineDelimiters { get; } = [];

        public List<TableCell> Cells { get; } = [];

        public List<Inline> EndOfLines { get; } = [];

        // Result of IsHeaderSeparatorColonBeforePipe for the line from ColonCheckLineStart to ColonCheckLineEnd
        public string? ColonCheckText { get; set; }

        public int ColonCheckSliceEnd { get; set; }

        public int ColonCheckLineStart { get; set; }

        public int ColonCheckLineEnd { get; set; } = -1;

        public bool ColonCheckResult { get; set; }
    }
}
