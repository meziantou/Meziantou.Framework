using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Rebuilds a ruled table from a region of a page: the horizontal and vertical rules the page paints in the region make
/// the grid, and the words of the text (<see cref="PdfPage.ExtractTextLayout"/>) fill its cells. When the table cannot be
/// rebuilt, the result tells why (<see cref="PdfTableExtractionFailureKind"/>) instead of returning a wrong table.
/// </summary>
/// <remarks>
/// The rules are the segments of the stroked paths, the edges of their rectangles included, and the thin filled
/// rectangles. A rule must cross the region to be a line of the grid: the outer border of the table is part of the grid.
/// A word belongs to the cell its center is in.
/// </remarks>
public static class PdfTableExtractor
{
    private const int MaxLines = 1000;

    /// <summary>Rebuilds the table of a region of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="region">The region of the table, in default user space: the grid is looked for in it.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfTableExtractionOptions.Default"/>.</param>
    /// <returns>The table, or the reason why it could not be rebuilt.</returns>
    /// <exception cref="PdfException">The content of the page cannot be read.</exception>
    public static PdfTableExtractionResult Extract(PdfPage page, PdfRectangle region, PdfTableExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= PdfTableExtractionOptions.Default;
        var tolerance = options.Tolerance;

        // The words of the region; a word that is not horizontal cannot be put in a row
        var words = new List<PdfTextWord>();
        foreach (var line in page.ExtractTextLayout().Lines)
        {
            foreach (var word in line.Words)
            {
                if (!Contains(region, Center(word.Bounds)))
                    continue;

                if (word.Glyphs.Any(glyph => glyph.IsVertical || Math.Abs(glyph.Angle) > 1))
                    return Fail(PdfTableExtractionFailureKind.NonHorizontalText, $"The word '{word.Text}' at {word.Bounds} is not horizontal");

                words.Add(word);
            }
        }

        if (words.Count == 0 && page.GetImages().Any(image => Intersects(region, image.Bounds)))
            return Fail(PdfTableExtractionFailureKind.ImageOnly, "The region has images but no text");

        // The rules: horizontal and vertical segments, clipped to the region
        var horizontal = new List<(double Position, double Start, double End)>();
        var vertical = new List<(double Position, double Start, double End)>();
        foreach (var (x1, y1, x2, y2) in PdfPageElements.GetRules(page, options.MaxRuleThickness))
        {
            if (Math.Abs(y1 - y2) <= tolerance && Math.Abs(x1 - x2) > tolerance)
            {
                var y = (y1 + y2) / 2;
                var start = Math.Max(Math.Min(x1, x2), region.Left);
                var end = Math.Min(Math.Max(x1, x2), region.Right);
                if (y >= region.Bottom - tolerance && y <= region.Top + tolerance && end - start > tolerance)
                {
                    horizontal.Add((y, start, end));
                }
            }
            else if (Math.Abs(x1 - x2) <= tolerance && Math.Abs(y1 - y2) > tolerance)
            {
                var x = (x1 + x2) / 2;
                var start = Math.Max(Math.Min(y1, y2), region.Bottom);
                var end = Math.Min(Math.Max(y1, y2), region.Top);
                if (x >= region.Left - tolerance && x <= region.Right + tolerance && end - start > tolerance)
                {
                    vertical.Add((x, start, end));
                }
            }
        }

        var rows = Cluster(horizontal.Select(rule => rule.Position), tolerance);
        var columns = Cluster(vertical.Select(rule => rule.Position), tolerance);
        if (rows.Count < 2 || columns.Count < 2)
            return Fail(PdfTableExtractionFailureKind.NoGrid, string.Create(CultureInfo.InvariantCulture, $"The region has {rows.Count} horizontal and {columns.Count} vertical lines: a grid needs two of each"));

        if (rows.Count > MaxLines || columns.Count > MaxLines)
            return Fail(PdfTableExtractionFailureKind.NoGrid, "The region has too many rules to be a table");

        // Top to bottom, left to right; the grid is the rectangle the outer lines make
        rows.Reverse();
        var left = columns[0];
        var right = columns[^1];
        var top = rows[0];
        var bottom = rows[^1];

        // Every edge between two cells must be ruled: a missing rule merges the cells
        for (var r = 0; r < rows.Count - 1; r++)
        {
            for (var c = 1; c < columns.Count - 1; c++)
            {
                if (!IsCovered(vertical, columns[c], rows[r + 1], rows[r], tolerance))
                    return Fail(PdfTableExtractionFailureKind.MergedCells, string.Create(CultureInfo.InvariantCulture, $"The cells of the row {r} on each side of x = {columns[c]:0.##} are merged: no vertical rule separates them"));
            }
        }

        for (var r = 1; r < rows.Count - 1; r++)
        {
            for (var c = 0; c < columns.Count - 1; c++)
            {
                if (!IsCovered(horizontal, rows[r], columns[c], columns[c + 1], tolerance))
                    return Fail(PdfTableExtractionFailureKind.MergedCells, string.Create(CultureInfo.InvariantCulture, $"The cells of the column {c} on each side of y = {rows[r]:0.##} are merged: no horizontal rule separates them"));
            }
        }

        // The words of each cell, by line then from left to right
        var cellWords = new List<PdfTextWord>?[(rows.Count - 1) * (columns.Count - 1)];
        var columnCount = columns.Count - 1;
        foreach (var word in words)
        {
            var (x, y) = Center(word.Bounds);
            if (x < left || x > right || y < bottom || y > top)
                continue;

            var row = rows.FindIndex(position => position < y) - 1;
            var column = columns.FindIndex(position => position > x) - 1;
            if (row < 0 || column < 0)
                continue;

            (cellWords[(row * columnCount) + column] ??= []).Add(word);
        }

        var cells = new string[rows.Count - 1][];
        for (var r = 0; r < cells.Length; r++)
        {
            cells[r] = new string[columns.Count - 1];
            for (var c = 0; c < cells[r].Length; c++)
            {
                cells[r][c] = cellWords[(r * columnCount) + c] is { } list ? JoinWords(list, tolerance) : "";
            }
        }

        return new PdfTableExtractionResult(new PdfExtractedTable(columns, rows, cells), failure: null, message: null);
    }

    private static PdfTableExtractionResult Fail(PdfTableExtractionFailureKind kind, string message) => new(table: null, kind, message);

    /// <summary>Groups positions closer than the tolerance: the lines of the grid, sorted.</summary>
    private static List<double> Cluster(IEnumerable<double> positions, double tolerance)
    {
        var result = new List<double>();
        foreach (var position in positions.Order())
        {
            if (result.Count == 0 || position - result[^1] > tolerance)
            {
                result.Add(position);
            }
        }

        return result;
    }

    /// <summary>Gets a value indicating whether rules at a position cover an interval, but for the tolerance at its ends.</summary>
    private static bool IsCovered(List<(double Position, double Start, double End)> rules, double position, double start, double end, double tolerance)
    {
        var covered = start + tolerance;
        foreach (var rule in rules.Where(rule => Math.Abs(rule.Position - position) <= tolerance).OrderBy(rule => rule.Start))
        {
            if (rule.Start - tolerance > covered)
                return false;

            covered = Math.Max(covered, rule.End);
            if (covered >= end - tolerance)
                return true;
        }

        return covered >= end - tolerance;
    }

    /// <summary>Joins the words of a cell: the words of a line by spaces, the lines from top to bottom by line feeds.</summary>
    private static string JoinWords(List<PdfTextWord> words, double tolerance)
    {
        var lines = new List<List<PdfTextWord>>();
        foreach (var word in words.OrderByDescending(word => word.Bounds.Top))
        {
            var line = lines.FirstOrDefault(line => Math.Abs(Center(line[0].Bounds).Y - Center(word.Bounds).Y) <= Math.Max(tolerance, word.Bounds.Height / 2));
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.AppendJoin(' ', line.OrderBy(word => word.Bounds.Left).Select(word => word.Text));
        }

        return builder.ToString();
    }

    private static (double X, double Y) Center(PdfRectangle bounds) => ((bounds.Left + bounds.Right) / 2, (bounds.Bottom + bounds.Top) / 2);

    private static bool Contains(PdfRectangle region, (double X, double Y) point) => point.X >= region.Left && point.X <= region.Right && point.Y >= region.Bottom && point.Y <= region.Top;

    private static bool Intersects(PdfRectangle region, PdfRectangle bounds) => bounds.Left < region.Right && bounds.Right > region.Left && bounds.Bottom < region.Top && bounds.Top > region.Bottom;
}
