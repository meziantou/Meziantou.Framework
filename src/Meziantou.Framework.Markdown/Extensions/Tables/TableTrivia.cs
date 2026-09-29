using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// Computes the trivia of a pipe table from the source positions of its cells, so that the roundtrip renderer writes the pipes and
/// the spaces around the content of the cells as they are in the source.
/// </summary>
internal static class TableTrivia
{
    public static void SetFromSource(InlineProcessor processor, Table table, ParagraphBlock paragraph, ParagraphBlock? trailingParagraph)
    {
        foreach (var block in table)
        {
            var row = (TableRow)block;
            if (processor.TryGetLine(row.SourceLine, out var line, out var sourcePosition))
            {
                SetRowTrivia(row, line, sourcePosition);
            }
        }

        if (table.DelimiterRowSourceLine >= 0 && processor.TryGetLine(table.DelimiterRowSourceLine, out var delimiterRow, out _))
        {
            table.DelimiterRow = delimiterRow;
        }

        // A table that starts the paragraph replaces it, and takes the trivia before it. The empty lines after the paragraph are
        // after the last block made from it.
        if (paragraph.Inline?.FirstChild is null)
        {
            table.LinesBefore = paragraph.LinesBefore;
            table.TriviaBefore = paragraph.TriviaBefore;
        }

        Block last = (Block?)trailingParagraph ?? table;
        last.LinesAfter = paragraph.LinesAfter;
        paragraph.LinesAfter = null;
    }

    private static void SetRowTrivia(TableRow row, StringSlice line, int sourcePosition)
    {
        var text = line.Text;
        row.NewLine = line.NewLine;

        // The text between the end of the content of a cell (or the start of the line) and the content of the next cell
        var previousEnd = line.Start - 1;
        Block previous = row;
        foreach (var block in row)
        {
            var cell = (TableCell)block;
            if (cell.SourceStart == int.MinValue)
            {
                continue;
            }

            var start = cell.SourceStart < 0 ? line.Start : line.Start + cell.SourceStart - sourcePosition;
            var end = cell.SourceEnd < 0 ? line.End : line.Start + cell.SourceEnd - sourcePosition;
            while (start <= end && text[start].IsSpaceOrTab())
            {
                start++;
            }

            while (end >= start && text[end].IsSpaceOrTab())
            {
                end--;
            }

            SetTrivia(previous, new StringSlice(text, previousEnd + 1, start - 1));
            previous = cell;
            previousEnd = end;
        }

        SetTrivia(previous, new StringSlice(text, previousEnd + 1, line.End));

        // The trivia before the first cell is the trivia before the row, and the trivia after a cell is before the next one
        static void SetTrivia(Block block, StringSlice trivia)
        {
            if (block is TableRow)
            {
                block.TriviaBefore = trivia;
            }
            else
            {
                block.TriviaAfter = trivia;
            }
        }
    }
}
