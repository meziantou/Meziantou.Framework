using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// A roundtrip renderer for a <see cref="Table"/>.
/// </summary>
internal sealed class RoundtripTableRenderer : RoundtripObjectRenderer<Table>
{
    protected override void Write(RoundtripRenderer renderer, Table obj)
    {
        renderer.RenderLinesBefore(obj);
        if (obj.SourceLines is { } lines)
        {
            // A grid table is written as it is in the source
            foreach (var line in lines)
            {
                renderer.Write(line);
                renderer.WriteLine(line.NewLine);
            }
        }
        else
        {
            renderer.Write(obj.TriviaBefore);
            for (var i = 0; i < obj.Count; i++)
            {
                if (obj[i] is TableRow row)
                {
                    WriteRow(renderer, row, escapePipes: obj.Parser is GfmPipeTableParser);
                }

                if (i == 0)
                {
                    WriteDelimiterRow(renderer, obj);
                }
            }
        }

        renderer.RenderLinesAfter(obj);
    }

    // A pipe table is written with the pipes and spaces of the source around the content of its cells. A row or a cell that is
    // not in the source is written with the default syntax.
    private static void WriteRow(RoundtripRenderer renderer, TableRow row, bool escapePipes)
    {
        if (row.SourceLine < 0)
        {
            renderer.Write('|');
            foreach (var block in row)
            {
                renderer.Write(' ');
                WriteCellContent(renderer, (TableCell)block, escapePipes);
                renderer.Write(" |");
            }

            renderer.WriteLine(NewLine.LineFeed);
            return;
        }

        renderer.Write(row.TriviaBefore);
        foreach (var block in row)
        {
            var cell = (TableCell)block;
            if (cell.SourceStart != int.MinValue)
            {
                WriteCellContent(renderer, cell, escapePipes);
                renderer.Write(cell.TriviaAfter);
            }
        }

        renderer.WriteLine(row.NewLine);
    }

    private static void WriteCellContent(RoundtripRenderer renderer, TableCell cell, bool escapePipes)
    {
        var writer = renderer.Writer;
        if (escapePipes)
        {
            renderer.Writer = new PipeEscapingTextWriter(writer);
        }

        try
        {
            foreach (var block in cell)
            {
                if (block is ParagraphBlock paragraph)
                {
                    renderer.WriteLeafInline(paragraph);
                }
                else
                {
                    renderer.Write(block);
                }
            }
        }
        finally
        {
            renderer.Writer = writer;
        }
    }

    private static void WriteDelimiterRow(RoundtripRenderer renderer, Table table)
    {
        if (table.DelimiterRow.Text is not null)
        {
            renderer.Write(table.DelimiterRow);
            renderer.WriteLine(table.DelimiterRow.NewLine);
            return;
        }

        if (table.ColumnDefinitions.Count == 0)
        {
            return;
        }

        // A table without the source of its header separator row has one for each column
        renderer.Write('|');
        foreach (var column in table.ColumnDefinitions)
        {
            renderer.Write(column.Alignment switch
            {
                TableColumnAlign.Left => " :-- |",
                TableColumnAlign.Center => " :-: |",
                TableColumnAlign.Right => " --: |",
                _ => " --- |",
            });
        }

        renderer.WriteLine(NewLine.LineFeed);
    }
}
