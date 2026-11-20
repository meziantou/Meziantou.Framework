using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Finds the layout of pages from what they draw: the blocks of text in reading order (the XY-cut of the text
/// extraction), classified as headings (a larger or bold font than the body text), list items (a bullet or a number
/// at the start of a line) or paragraphs; the grids of rules as tables; the images as figures; and the text repeated
/// at the same place at the top or the bottom of the pages as headers and footers.
/// </summary>
internal static class LayoutAnalyzer
{
    // The share of the height of a page where the headers and the footers are
    private const double MarginRatio = 0.15;

    // A heading is a short block
    private const int MaxHeadingLines = 3;
    private const int MaxHeadingLength = 200;

    // The rules of the tables: the segments a page draws, bounded
    private const int MaxRules = 10_000;

    public static List<PdfLayoutPage> Analyze(IReadOnlyList<PdfPage> pages, PdfLayoutOptions options)
    {
        var data = new List<PageData>();
        foreach (var page in pages)
        {
            var layout = page.ExtractTextLayout(new PdfTextExtractionOptions { ReadingOrder = PdfTextReadingOrder.Layout });
            var tables = options.DetectTables ? FindTables(page) : [];
            data.Add(new PageData(page, layout, page.GetImages(), tables));
        }

        var bodySize = GetBodySize(data);
        var repeated = options.DetectHeadersAndFooters && pages.Count > 1 ? FindRepeatedLines(data) : [];

        // The sizes of the headings, from the largest: the levels
        var headingSizes = new SortedSet<double>(Comparer<double>.Create((a, b) => b.CompareTo(a)));
        foreach (var page in data)
        {
            foreach (var block in page.Layout.Blocks)
            {
                if (IsHeadingCandidate(block.Lines, bodySize, options) is { } size)
                {
                    headingSizes.Add(size);
                }
            }
        }

        var levels = headingSizes.Take(6).Select((size, index) => (size, index)).ToDictionary(item => item.size, item => item.index + 1);
        var result = new List<PdfLayoutPage>();
        foreach (var page in data)
        {
            result.Add(new PdfLayoutPage(page.Page, BuildNodes(page, bodySize, repeated, levels, options)));
        }

        return result;
    }

    private static List<PdfLayoutNode> BuildNodes(PageData page, double bodySize, HashSet<(string Text, int Position)> repeated, Dictionary<double, int> levels, PdfLayoutOptions options)
    {
        var height = page.Page.MediaBox.Height;
        var headers = new List<PdfTextLine>();
        var footers = new List<PdfTextLine>();
        var content = new List<PdfLayoutNode>();
        foreach (var block in page.Layout.Blocks)
        {
            var lines = new List<PdfTextLine>();
            foreach (var line in block.Lines)
            {
                if (page.Tables.Any(table => Contains(new PdfRectangle(table.ColumnBoundaries[0], table.RowBoundaries[^1], table.ColumnBoundaries[^1], table.RowBoundaries[0]), Center(line.Bounds))))
                    continue;

                if (repeated.Contains(GetKey(line)))
                {
                    (line.Bounds.Bottom > height / 2 ? headers : footers).Add(line);
                    continue;
                }

                lines.Add(line);
            }

            if (lines.Count > 0)
            {
                content.AddRange(Classify(lines, bodySize, levels, options));
            }
        }

        // The tables and the figures, before the first block below their top in the same column
        foreach (var table in page.Tables)
        {
            Insert(content, BuildTable(table, page.Layout));
        }

        foreach (var image in page.Images)
        {
            Insert(content, new PdfLayoutNode(PdfLayoutNodeKind.Figure, image.Bounds, "", []) { Image = image, Operations = image.OperationIndex >= 0 ? [image.OperationIndex] : [] });
        }

        var nodes = new List<PdfLayoutNode>();
        if (headers.Count > 0)
        {
            nodes.Add(CreateNode(PdfLayoutNodeKind.Header, headers, []));
        }

        nodes.AddRange(content);
        if (footers.Count > 0)
        {
            nodes.Add(CreateNode(PdfLayoutNodeKind.Footer, footers, []));
        }

        return nodes;
    }

    /// <summary>Classifies the lines of a block: a heading, a list of items, or a paragraph; a list that follows a paragraph in the same block is split from it.</summary>
    private static IEnumerable<PdfLayoutNode> Classify(List<PdfTextLine> lines, double bodySize, Dictionary<double, int> levels, PdfLayoutOptions options)
    {
        if (IsHeadingCandidate(lines, bodySize, options) is { } size && levels.TryGetValue(size, out var level))
        {
            var heading = CreateNode(PdfLayoutNodeKind.Heading, lines, []);
            yield return new PdfLayoutNode(heading.Kind, heading.Bounds, heading.Text, heading.Children) { Level = level, Operations = heading.Operations };
            yield break;
        }

        var paragraph = new List<PdfTextLine>();
        var items = new List<List<PdfTextLine>>();
        foreach (var line in lines)
        {
            if (IsListItemStart(line.Text))
            {
                if (paragraph.Count > 0)
                {
                    yield return CreateNode(PdfLayoutNodeKind.Paragraph, paragraph, []);
                    paragraph = [];
                }

                items.Add([line]);
            }
            else if (items.Count > 0)
            {
                items[^1].Add(line);
            }
            else
            {
                paragraph.Add(line);
            }
        }

        if (paragraph.Count > 0)
        {
            yield return CreateNode(PdfLayoutNodeKind.Paragraph, paragraph, []);
        }

        if (items.Count > 0)
        {
            var children = items.Select(item => CreateNode(PdfLayoutNodeKind.ListItem, item, [])).ToList();
            yield return new PdfLayoutNode(PdfLayoutNodeKind.List, Union(children.Select(child => child.Bounds)), string.Join('\n', children.Select(child => child.Text)), children)
            {
                Operations = [.. children.SelectMany(child => child.Operations)],
            };
        }
    }

    /// <summary>Gets the font size of a block that can be a heading: short, in a font larger than the body, or bold and not smaller; <see langword="null"/> otherwise.</summary>
    private static double? IsHeadingCandidate(IReadOnlyList<PdfTextLine> lines, double bodySize, PdfLayoutOptions options)
    {
        if (lines.Count is 0 or > MaxHeadingLines || lines.Sum(line => line.Text.Length) > MaxHeadingLength || IsListItemStart(lines[0].Text))
            return null;

        var glyphs = lines.SelectMany(line => line.Glyphs).Where(glyph => !string.IsNullOrWhiteSpace(glyph.Text)).ToList();
        if (glyphs.Count == 0)
            return null;

        var size = Math.Round(glyphs.Average(glyph => glyph.FontSize) * 2, MidpointRounding.AwayFromZero) / 2;
        if (size >= bodySize * options.HeadingSizeRatio)
            return size;

        // A bold line of the size of the body text: a heading of the lowest level
        if (lines.Count == 1 && size >= bodySize * 0.95 && glyphs.All(glyph => IsBold(glyph.FontName)) && !lines[0].Text.TrimEnd().EndsWith('.', StringComparison.Ordinal))
            return size - 0.01;

        return null;
    }

    private static bool IsBold(string? fontName) => fontName is not null && (fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Black", StringComparison.OrdinalIgnoreCase) || fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase));

    /// <summary>Indicates whether a line starts with the label of a list item: a bullet, or a number or a letter followed by a period or a parenthesis, then a space.</summary>
    internal static bool IsListItemStart(string text)
    {
        var span = text.AsSpan().TrimStart();
        if (span.Length < 2)
            return false;

        if (span[0] is '•' or '◦' or '▪' or '●' or '–' or '-' or '*' or '‣' or '⁃')
            return span[1] is ' ' or '\t' or '\u00A0';

        var digits = 0;
        while (digits < span.Length && digits < 3 && char.IsAsciiDigit(span[digits]))
        {
            digits++;
        }

        var labelLength = digits > 0 ? digits : char.IsAsciiLetter(span[0]) ? 1 : 0;
        return labelLength > 0 && span.Length > labelLength + 1 && span[labelLength] is '.' or ')' && span[labelLength + 1] is ' ' or '\t' or '\u00A0';
    }

    /// <summary>Gets the font size of most of the glyphs: the size of the body text.</summary>
    private static double GetBodySize(List<PageData> pages)
    {
        var counts = new Dictionary<double, int>();
        foreach (var page in pages)
        {
            foreach (var line in page.Layout.Lines)
            {
                foreach (var glyph in line.Glyphs)
                {
                    var size = Math.Round(glyph.FontSize * 2, MidpointRounding.AwayFromZero) / 2;
                    counts[size] = counts.GetValueOrDefault(size) + 1;
                }
            }
        }

        return counts.Count == 0 ? 12 : counts.MaxBy(item => (item.Value, -item.Key)).Key;
    }

    /// <summary>Finds the lines in the top or bottom margins of the pages that are on several pages at the same place: the numbers in them are ignored (page numbers).</summary>
    private static HashSet<(string Text, int Position)> FindRepeatedLines(List<PageData> pages)
    {
        var pagesByKey = new Dictionary<(string, int), int>();
        foreach (var page in pages)
        {
            var height = page.Page.MediaBox.Height;
            var keys = new HashSet<(string, int)>();
            foreach (var line in page.Layout.Lines)
            {
                if (line.Bounds.Bottom > height * (1 - MarginRatio) || line.Bounds.Top < height * MarginRatio)
                {
                    keys.Add(GetKey(line));
                }
            }

            foreach (var key in keys)
            {
                pagesByKey[key] = pagesByKey.GetValueOrDefault(key) + 1;
            }
        }

        var minimum = Math.Max(2, (pages.Count + 1) / 2);
        return [.. pagesByKey.Where(item => item.Value >= minimum).Select(item => item.Key)];
    }

    private static (string Text, int Position) GetKey(PdfTextLine line)
    {
        var text = new StringBuilder(line.Text.Length);
        foreach (var c in line.Text.Trim())
        {
            text.Append(char.IsAsciiDigit(c) ? '#' : c);
        }

        return (text.ToString(), (int)Math.Round(line.Bounds.Bottom / 3, MidpointRounding.AwayFromZero));
    }

    /// <summary>Finds the tables of a page: the groups of rules that cross each other, which the table extraction reads as a grid.</summary>
    private static List<PdfExtractedTable> FindTables(PdfPage page)
    {
        var rules = PdfPageElements.GetRules(page, PdfTableExtractionOptions.Default.MaxRuleThickness).Take(MaxRules).ToList();
        if (rules.Count < 4)
            return [];

        // The groups of rules whose boxes touch (a union-find over the boxes, grown by the tolerance)
        var tolerance = PdfTableExtractionOptions.Default.Tolerance + 1;
        var boxes = rules.Select(rule => new PdfRectangle(Math.Min(rule.X1, rule.X2) - tolerance, Math.Min(rule.Y1, rule.Y2) - tolerance, Math.Max(rule.X1, rule.X2) + tolerance, Math.Max(rule.Y1, rule.Y2) + tolerance)).ToList();
        var parents = Enumerable.Range(0, boxes.Count).ToArray();
        int Find(int i)
        {
            while (parents[i] != i)
            {
                parents[i] = parents[parents[i]];
                i = parents[i];
            }

            return i;
        }

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (Intersects(boxes[i], boxes[j]))
                {
                    parents[Find(i)] = Find(j);
                }
            }
        }

        var result = new List<PdfExtractedTable>();
        foreach (var group in Enumerable.Range(0, boxes.Count).GroupBy(Find))
        {
            if (group.Count() < 4)
                continue;

            var region = Union(group.Select(index => boxes[index]));
            if (PdfTableExtractor.Extract(page, region).Table is { RowCount: > 0, ColumnCount: > 0 } table)
            {
                result.Add(table);
            }
        }

        return result;
    }

    private static PdfLayoutNode BuildTable(PdfExtractedTable table, PdfTextLayout layout)
    {
        var cells = new List<PdfLayoutNode>();
        for (var row = 0; row < table.RowCount; row++)
        {
            for (var column = 0; column < table.ColumnCount; column++)
            {
                var bounds = new PdfRectangle(table.ColumnBoundaries[column], table.RowBoundaries[row + 1], table.ColumnBoundaries[column + 1], table.RowBoundaries[row]);
                var operations = new HashSet<int>();
                foreach (var line in layout.Lines)
                {
                    foreach (var glyph in line.Glyphs)
                    {
                        if (glyph.OperationIndex >= 0 && Contains(bounds, Center(glyph.Bounds)))
                        {
                            operations.Add(glyph.OperationIndex);
                        }
                    }
                }

                cells.Add(new PdfLayoutNode(PdfLayoutNodeKind.TableCell, bounds, table[row, column], []) { Row = row, Column = column, Operations = operations });
            }
        }

        var tableBounds = new PdfRectangle(table.ColumnBoundaries[0], table.RowBoundaries[^1], table.ColumnBoundaries[^1], table.RowBoundaries[0]);
        return new PdfLayoutNode(PdfLayoutNodeKind.Table, tableBounds, string.Join('\n', table.Rows.Select(row => string.Join('\t', row))), cells)
        {
            Operations = [.. cells.SelectMany(cell => cell.Operations)],
        };
    }

    private static PdfLayoutNode CreateNode(PdfLayoutNodeKind kind, List<PdfTextLine> lines, IReadOnlyList<PdfLayoutNode> children)
    {
        var operations = new HashSet<int>();
        foreach (var glyph in lines.SelectMany(line => line.Glyphs))
        {
            if (glyph.OperationIndex >= 0)
            {
                operations.Add(glyph.OperationIndex);
            }
        }

        return new PdfLayoutNode(kind, Union(lines.Select(line => line.Bounds)), string.Join('\n', lines.Select(line => line.Text)), children) { Operations = operations };
    }

    /// <summary>Inserts a node before the first node whose top is below its top and that overlaps it horizontally, or before the end.</summary>
    private static void Insert(List<PdfLayoutNode> nodes, PdfLayoutNode node)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var other = nodes[i].Bounds;
            if (other.Top < node.Bounds.Top - 1 && other.Left < node.Bounds.Right && other.Right > node.Bounds.Left)
            {
                nodes.Insert(i, node);
                return;
            }
        }

        nodes.Add(node);
    }

    private static PdfRectangle Union(IEnumerable<PdfRectangle> rectangles)
    {
        var (left, bottom, right, top) = (double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var rectangle in rectangles)
        {
            (left, bottom, right, top) = (Math.Min(left, rectangle.Left), Math.Min(bottom, rectangle.Bottom), Math.Max(right, rectangle.Right), Math.Max(top, rectangle.Top));
        }

        return double.IsFinite(left) ? new PdfRectangle(left, bottom, right, top) : default;
    }

    private static (double X, double Y) Center(PdfRectangle bounds) => ((bounds.Left + bounds.Right) / 2, (bounds.Bottom + bounds.Top) / 2);

    private static bool Contains(PdfRectangle region, (double X, double Y) point) => point.X >= region.Left && point.X <= region.Right && point.Y >= region.Bottom && point.Y <= region.Top;

    private static bool Intersects(PdfRectangle a, PdfRectangle b) => a.Left <= b.Right && a.Right >= b.Left && a.Bottom <= b.Top && a.Top >= b.Bottom;

    private sealed record PageData(PdfPage Page, PdfTextLayout Layout, IReadOnlyList<PdfImageOccurrence> Images, List<PdfExtractedTable> Tables);
}
