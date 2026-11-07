using System.Linq;
using Meziantou.Framework.Markdown.Renderers.Normalize;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// A Normalize renderer for a <see cref="Table"/> in normalized form.
/// </summary>
/// <seealso cref="NormalizeObjectRenderer{Table}" />
public class NormalizeTableRenderer : NormalizeObjectRenderer<Table>
{
    private const string PipeSeparator = "|";
    private const string AlignmentChar = ":";
    private const string MarginSeparator = " ";

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(NormalizeRenderer renderer, Table obj)
    {
        renderer.EnsureLine();

        bool firstRow = true;

        foreach (var row in obj.OfType<TableRow>())
        {
            if (!firstRow)
            {
                renderer.WriteLine();
            }
            firstRow = false;

            renderer.Write(PipeSeparator);

            // A header row that looks like a delimiter row is tried as the delimiter row of the paragraph before it, which
            // prevents the table, as a GFM table is tried once per paragraph
            var escapeHeader = row.IsHeader && obj.Parser is GfmPipeTableParser && row.Count > 0 && row.OfType<TableCell>().All(IsDelimiterLike);
            foreach (var tableCell in row)
            {
                renderer.Write(MarginSeparator);
                if (escapeHeader)
                {
                    renderer.Write('\\');
                    escapeHeader = false;
                }

                bool previousEscapeTablePipes = renderer.EscapeTablePipes;
                bool previousEscapeLiteralPipes = renderer.EscapeLiteralPipes;
                renderer.EscapeTablePipes = obj.Parser is GfmPipeTableParser;

                // The pipes of a literal, such as the text of a link, were not column delimiters, which the content written
                // differently could make them
                renderer.EscapeLiteralPipes = !renderer.EscapeTablePipes;
                try
                {
                    renderer.Render(tableCell);
                }
                finally
                {
                    renderer.EscapeTablePipes = previousEscapeTablePipes;
                    renderer.EscapeLiteralPipes = previousEscapeLiteralPipes;
                }

                renderer.Write(MarginSeparator);
                renderer.Write(PipeSeparator);
            }

            if (row.IsHeader)
            {
                renderer.WriteLine();

                renderer.Write(PipeSeparator);

                for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
                {
                    var column = columnIndex < obj.ColumnDefinitions.Count ? obj.ColumnDefinitions[columnIndex] : null;
                    // Match the HTML renderer's alignment fallback for expanded rows.
                    var alignment = obj.ColumnDefinitions.Count > 0
                        ? obj.ColumnDefinitions[Math.Min(columnIndex, obj.ColumnDefinitions.Count - 1)].Alignment
                        : null;
                    renderer.Write(MarginSeparator);
                    if (alignment == TableColumnAlign.Left || alignment == TableColumnAlign.Center)
                    {
                        renderer.Write(AlignmentChar);
                    }
                    renderer.Write('-', column?.SeparatorDashCount ?? 3);
                    if (alignment == TableColumnAlign.Right || alignment == TableColumnAlign.Center)
                    {
                        renderer.Write(AlignmentChar);
                    }
                    renderer.Write(MarginSeparator);
                    renderer.Write(PipeSeparator);
                }
            }
        }

        // The paragraph after a pipe table parsed from the same paragraph, whose first line would start another block, such as
        // an HTML tag alone on its line, is written right after the table, as in the source
        // In a tight list, the next paragraph is right after the table too, and continues its paragraph
        if (obj.Parser is not GfmPipeTableParser && !renderer.IsLastInContainer && renderer.GetNextSibling(obj) is ParagraphBlock next && (renderer.CompactParagraph || renderer.IsParagraphContinuationOnly(next)))
        {
            renderer.EnsureLine();
            renderer.ContinuesParagraph = true;
        }
        else
        {
            renderer.FinishBlock(true);
        }
    }

    private static bool IsDelimiterLike(TableCell cell)
    {
        if (cell is not [ParagraphBlock { Inline: { } inline }])
        {
            return false;
        }

        var text = new System.Text.StringBuilder();
        foreach (var child in inline)
        {
            // An escaped character, which is written escaped, is not part of a delimiter row
            if (child is not LiteralInline { IsFirstCharacterEscaped: false } literal)
            {
                return false;
            }

            text.Append(literal.Content.AsSpan());
        }

        // A cell that starts a block, such as a list item ("-") or a thematic break ("---"), is escaped already
        var content = text.ToString().AsSpan().Trim(" \t");
        return content.TrimStart(':').TrimEnd(':') is { Length: > 0 } dashes && !dashes.ContainsAnyExcept('-')
            && content is not "-" && !(content.Length >= 3 && !content.ContainsAnyExcept('-'));
    }
}
