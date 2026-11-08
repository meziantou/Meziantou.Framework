using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// The reading order of the layout: the lines of the content, split at their wide gaps, are grouped into blocks by a
/// recursive XY-cut of their boxes. A region is cut at the vertical blank spaces that cross it from top to bottom (the
/// gutters of the columns, read from left to right), or else at the horizontal blank spaces that cross it from left to
/// right (read from top to bottom). The lines of a block are then sorted by their baselines, and the pieces of a line
/// are joined again. The positions are those of the page as it is displayed, after its rotation.
/// </summary>
internal sealed partial class TextExtractor
{
    // The narrowest gutter between two columns, in font sizes: wider than the spaces between the words of a line
    private const double ColumnGap = 1;

    // The narrowest gap between two blocks, in font sizes: wider than the gap between the boxes of two lines of a paragraph
    private const double BlockGap = 0.5;

    // The depth of the cuts: each level costs the count of the lines of the page
    private const int MaxCutDepth = 32;

    private PdfTextLayout OrderByLayout(List<PdfTextLine> lines, int rotation)
    {
        var items = new List<LayoutItem>(lines.Count);
        var sizes = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var size = 0d;
            foreach (var glyph in line.Glyphs)
            {
                size = Math.Max(size, glyph.FontSize);
            }

            var first = line.Glyphs[0];
            var (originX, originY) = Rotate(first.Origin.X, first.Origin.Y, rotation);
            var (x1, y1) = Rotate(line.Bounds.Left, line.Bounds.Bottom, rotation);
            var (x2, y2) = Rotate(line.Bounds.Right, line.Bounds.Top, rotation);
            var angle = Math.IEEERemainder(first.Angle - rotation, 360);
            items.Add(new LayoutItem(i, line, new PdfRectangle(x1, y1, x2, y2), originX, originY, size, Math.Abs(angle) < 1));
            sizes[i] = size;
        }

        // The gaps are relative to the most common size of the text of the page, the size of its body
        Array.Sort(sizes);
        var unit = Math.Max(sizes.Length > 0 ? sizes[sizes.Length / 2] : 0, 0.001);
        var leaves = new List<List<LayoutItem>>();
        Cut(items, unit * ColumnGap, unit * BlockGap, depth: 0, leaves);

        var orderedLines = new List<PdfTextLine>(lines.Count);
        var blocks = new List<PdfTextBlock>(leaves.Count);
        foreach (var leaf in leaves)
        {
            var blockLines = OrderBlock(leaf);
            orderedLines.AddRange(blockLines);
            blocks.Add(new PdfTextBlock(blockLines));
        }

        return new PdfTextLayout(string.Join('\n', orderedLines.Select(line => line.Text)), orderedLines, blocks);
    }

    /// <summary>Gets the position of a point on the page as it is displayed: the page turns clockwise by its rotation.</summary>
    private static (double X, double Y) Rotate(double x, double y, int rotation) => rotation switch
    {
        90 => (y, -x),
        180 => (-x, -y),
        270 => (-y, x),
        _ => (x, y),
    };

    private void Cut(List<LayoutItem> items, double columnGap, double blockGap, int depth, List<List<LayoutItem>> leaves)
    {
        Charge(items.Count);
        if (items.Count <= 1 || depth >= MaxCutDepth)
        {
            leaves.Add(items);
            return;
        }

        // The columns: the vertical blank spaces from the top to the bottom of the region. The lines of one row are not
        // cut: their gaps are the gaps of a line that no other line confirms.
        if (!IsOneRow(items))
        {
            var columns = SplitColumns(items, columnGap);
            if (columns.Count > 1)
            {
                foreach (var column in columns)
                {
                    Cut(column, columnGap, blockGap, depth + 1, leaves);
                }

                return;
            }
        }

        // The blocks: the horizontal blank spaces from the left to the right of the region, from top to bottom
        var pieces = SplitRows(items, blockGap);
        if (pieces.Count <= 1)
        {
            leaves.Add(items);
            return;
        }

        // A gap between two paragraphs that is at the same height in two columns cuts both columns: the pieces that a
        // gutter separates are joined again, so that the columns are read one after the other. The region has no
        // gutter, so a group that has one is smaller than the region.
        var groups = new List<List<LayoutItem>>();
        List<LayoutItem>? group = null;
        List<Interval>? groupIntervals = null;
        foreach (var piece in pieces)
        {
            var intervals = GetIntervals(piece);
            if (group is not null)
            {
                var union = Union(groupIntervals!, intervals);
                if (HasGap(union, columnGap))
                {
                    group.AddRange(piece);
                    groupIntervals = union;
                    continue;
                }
            }

            group = [.. piece];
            groupIntervals = intervals;
            groups.Add(group);
        }

        if (groups.Count <= 1)
        {
            leaves.Add(items);
            return;
        }

        foreach (var item in groups)
        {
            Cut(item, columnGap, blockGap, depth + 1, leaves);
        }
    }

    /// <summary>Gets a value indicating whether a horizontal band crosses all the lines: they are on one row.</summary>
    private static bool IsOneRow(List<LayoutItem> items)
    {
        var bottom = double.NegativeInfinity;
        var top = double.PositiveInfinity;
        foreach (var item in items)
        {
            bottom = Math.Max(bottom, item.Box.Bottom);
            top = Math.Min(top, item.Box.Top);
        }

        return bottom < top;
    }

    /// <summary>Splits the lines at the gaps of their projection on the x axis, from left to right.</summary>
    private static List<List<LayoutItem>> SplitColumns(List<LayoutItem> items, double gap)
    {
        var sorted = items.OrderBy(item => item.Box.Left).ThenBy(item => item.Index).ToList();
        var result = new List<List<LayoutItem>>();
        var current = new List<LayoutItem>();
        var right = double.NegativeInfinity;
        foreach (var item in sorted)
        {
            if (current.Count > 0 && item.Box.Left - right >= gap)
            {
                result.Add(current);
                current = [];
            }

            current.Add(item);
            right = Math.Max(right, item.Box.Right);
        }

        result.Add(current);
        return result;
    }

    /// <summary>Splits the lines at the gaps of their projection on the y axis, from top to bottom.</summary>
    private static List<List<LayoutItem>> SplitRows(List<LayoutItem> items, double gap)
    {
        var sorted = items.OrderByDescending(item => item.Box.Top).ThenBy(item => item.Index).ToList();
        var result = new List<List<LayoutItem>>();
        var current = new List<LayoutItem>();
        var bottom = double.PositiveInfinity;
        foreach (var item in sorted)
        {
            if (current.Count > 0 && bottom - item.Box.Top >= gap)
            {
                result.Add(current);
                current = [];
            }

            current.Add(item);
            bottom = Math.Min(bottom, item.Box.Bottom);
        }

        result.Add(current);
        return result;
    }

    /// <summary>Gets the projection of the lines on the x axis: the sorted intervals that do not overlap.</summary>
    private static List<Interval> GetIntervals(List<LayoutItem> items)
    {
        var intervals = items.Select(item => new Interval(item.Box.Left, item.Box.Right)).OrderBy(interval => interval.Start).ToList();
        return Merge(intervals);
    }

    private static List<Interval> Union(List<Interval> first, List<Interval> second)
    {
        var all = new List<Interval>(first.Count + second.Count);
        int i = 0, j = 0;
        while (i < first.Count || j < second.Count)
        {
            all.Add(j >= second.Count || (i < first.Count && first[i].Start <= second[j].Start) ? first[i++] : second[j++]);
        }

        return Merge(all);
    }

    private static List<Interval> Merge(List<Interval> sorted)
    {
        var result = new List<Interval>(sorted.Count);
        foreach (var interval in sorted)
        {
            if (result.Count > 0 && interval.Start <= result[^1].End)
            {
                result[^1] = result[^1] with { End = Math.Max(result[^1].End, interval.End) };
            }
            else
            {
                result.Add(interval);
            }
        }

        return result;
    }

    private static bool HasGap(List<Interval> intervals, double gap)
    {
        for (var i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].Start - intervals[i - 1].End >= gap)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Orders the lines of a block: the horizontal lines by their baselines from top to bottom, the pieces of a line (on
    /// one baseline, within half a font size) from left to right and joined with a space where they are apart. A block
    /// that has text in another direction keeps the order of the content.
    /// </summary>
    private static List<PdfTextLine> OrderBlock(List<LayoutItem> items)
    {
        if (!items.TrueForAll(item => item.IsHorizontal))
            return [.. items.OrderBy(item => item.Index).Select(item => item.Line)];

        var sorted = items.OrderByDescending(item => item.BaselineY).ThenBy(item => item.Box.Left).ThenBy(item => item.Index).ToList();
        var result = new List<PdfTextLine>();
        var row = new List<LayoutItem>();
        var rowBaseline = 0d;
        var rowSize = 0d;
        foreach (var item in sorted)
        {
            if (row.Count > 0 && rowBaseline - item.BaselineY > Math.Max(rowSize, item.Size) * 0.5)
            {
                result.Add(JoinRow(row));
                row.Clear();
            }

            if (row.Count == 0)
            {
                rowBaseline = item.BaselineY;
                rowSize = item.Size;
            }

            row.Add(item);
        }

        if (row.Count > 0)
        {
            result.Add(JoinRow(row));
        }

        return result;
    }

    private static PdfTextLine JoinRow(List<LayoutItem> row)
    {
        if (row.Count == 1)
            return row[0].Line;

        row.Sort((a, b) => a.BaselineX != b.BaselineX ? a.BaselineX.CompareTo(b.BaselineX) : a.Index.CompareTo(b.Index));
        var text = new StringBuilder();
        var words = new List<PdfTextWord>();
        var glyphs = new List<PdfTextGlyph>();
        LayoutItem? previous = null;
        foreach (var item in row)
        {
            if (previous is { } last && item.Box.Left - last.Box.Right > Math.Max(last.Size, item.Size) * 0.15 && text.Length > 0 && !char.IsWhiteSpace(text[^1]) && item.Line.Text is not [' ', ..])
            {
                text.Append(' ');
            }

            text.Append(item.Line.Text);
            words.AddRange(item.Line.Words);
            glyphs.AddRange(item.Line.Glyphs);
            previous = item;
        }

        return new PdfTextLine(text.ToString(), words, glyphs);
    }

    /// <summary>A line of the content in the coordinates of the page as it is displayed.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LayoutItem(int Index, PdfTextLine Line, PdfRectangle Box, double BaselineX, double BaselineY, double Size, bool IsHorizontal);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Interval(double Start, double End);
}
