using System.Globalization;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Layout;

/// <summary>
/// Lays out the blocks of a flow document in pages, then writes them: the commands of each page, in marked content of
/// their structure elements or as artifacts, the links, and the outline. A table of contents needs the pages of the
/// headings: the layout runs again until they are stable.
/// </summary>
internal sealed class FlowLayout
{
    // The passes of a document with a table of contents: its entries change the pages of the headings that follow it
    private const int MaxPasses = 4;

    // The nesting of the blocks: lists in lists, tables in cells
    private const int MaxDepth = 32;

    // The page number of the table of contents is reserved this width
    private const double PageNumberWidth = 36;

    private static readonly double[] HeadingScales = [2, 1.6, 1.35, 1.2, 1.1, 1];

    private readonly PdfFlowOptions _options;
    private readonly IReadOnlyList<PlacedHeading> _previousHeadings;

    private FlowLayout(PdfFlowOptions options, IReadOnlyList<PlacedHeading> previousHeadings)
    {
        _options = options;
        _previousHeadings = previousHeadings;
        Root = new StructNode("Document", parent: null);
        Pages = new PageArea(options);
    }

    public StructNode Root { get; }

    public PageArea Pages { get; }

    public List<PlacedHeading> Headings { get; } = [];

    public static PdfDocument Build(PdfFlowDocument flow)
    {
        var options = flow.Options;
        var hasTableOfContents = flow.Blocks.Any(block => block is TableOfContentsBlock);
        IReadOnlyList<PlacedHeading> headings = [];
        FlowLayout layout;
        var pass = 0;
        while (true)
        {
            layout = new FlowLayout(options, headings);
            layout.LayoutBlocks(layout.Pages, flow.Blocks, layout.Root, depth: 0);
            pass++;
            if (!hasTableOfContents || pass >= MaxPasses || layout.Headings.SequenceEqual(headings))
                break;

            headings = layout.Headings;
        }

        return layout.Write();
    }

    private PdfFont HeadingFont => _options.HeadingFont ?? (_options.Font is var font && font.PostScriptName == "Helvetica" ? PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold) : _options.Font);

    private void LayoutBlocks(FlowArea area, List<FlowBlock> blocks, StructNode parent, int depth)
    {
        if (depth > MaxDepth)
            throw new ArgumentException("The blocks are nested too deeply", nameof(blocks));

        for (var i = 0; i < blocks.Count; i++)
        {
            var next = i + 1 < blocks.Count ? blocks[i + 1] : null;
            switch (blocks[i])
            {
                case ParagraphBlock paragraph:
                    LayoutParagraph(area, paragraph, parent, next);
                    break;
                case ListBlock list:
                    LayoutList(area, list, parent, depth);
                    break;
                case ImageBlock image:
                    LayoutImage(area, image, parent);
                    break;
                case TableBlock table:
                    LayoutTable(area, table, parent, depth);
                    break;
                case RuleBlock rule:
                    area.EnsureSpace(rule.Width + 8);
                    area.Advance(4);
                    area.Add(new LineCommand(null, area.Left, area.Y, area.Left + area.Width, area.Y, rule.Width, rule.Color));
                    area.Advance(rule.Width + 4);
                    break;
                case SpacerBlock spacer:
                    if (!area.AtTop)
                    {
                        area.Y -= Math.Min(spacer.Height, Math.Max(0, area.Available));
                    }

                    break;
                case PageBreakBlock when area is PageArea pages:
                    if (!pages.AtTop || pages.Pages[^1].Count > 0)
                    {
                        pages.NewPage();
                    }

                    break;
                case ColumnsBlock columns when area is PageArea pages:
                    // Columns that would not hold a line start on the next page
                    if (!pages.AtTop && pages.Available < _options.FontSize * _options.LineHeight * 2)
                    {
                        pages.NewPage();
                    }

                    pages.BeginColumns(columns.Count, columns.Gap);
                    LayoutBlocks(pages, columns.Blocks, parent, depth + 1);
                    pages.EndColumns();
                    break;
                case TableOfContentsBlock tableOfContents:
                    LayoutTableOfContents(area, tableOfContents, parent);
                    break;
            }
        }
    }

    private (PdfFont Font, double Size, PdfColor Color) Resolve(FlowRun run, PdfParagraphStyle? style, int headingLevel)
    {
        var baseStyle = style?.TextStyle;
        var font = run.Style?.Font ?? baseStyle?.Font ?? (headingLevel > 0 ? HeadingFont : _options.Font);
        var size = run.Style?.FontSize ?? baseStyle?.FontSize ?? (headingLevel > 0 ? _options.FontSize * HeadingScales[headingLevel - 1] : _options.FontSize);
        var color = run.Style?.Color ?? baseStyle?.Color ?? _options.TextColor;
        return (font, size, color);
    }

    private void LayoutParagraph(FlowArea area, ParagraphBlock paragraph, StructNode parent, FlowBlock? next)
    {
        var level = paragraph.HeadingLevel;
        var style = paragraph.Style;
        var node = parent.Add(level > 0 ? "H" + level.ToString(CultureInfo.InvariantCulture) : "P");
        var size = Resolve(default, style, level).Size;
        var spaceBefore = style?.SpaceBefore ?? (level > 0 ? size * 0.6 : 0);
        var spaceAfter = style?.SpaceAfter ?? (level > 0 ? size * 0.3 : _options.ParagraphSpacing);
        var indent = Math.Clamp(style?.Indent ?? 0, 0, area.Width * 0.9);
        var lines = FlowText.Break(paragraph.Runs, run => Resolve(run, style, level), area.Width - indent, _options.LineHeight);
        if (!area.AtTop)
        {
            area.Y -= spaceBefore;
        }

        // A heading stays with the first lines of the block that follows it
        if (level > 0 && next is not null)
        {
            area.EnsureSpace(lines.Sum(line => line.Height) + (_options.FontSize * _options.LineHeight * 3));
        }

        var text = string.Concat(paragraph.Runs.Select(run => run.Text));
        var rightToLeft = UnicodeBidi.MayRequireBidi(text) && UnicodeBidi.GetParagraphLevel(text) % 2 == 1;
        var alignment = style?.Alignment ?? (rightToLeft ? PdfTextAlignment.Right : PdfTextAlignment.Left);
        var links = new Dictionary<int, StructNode>();
        var first = true;
        foreach (var line in lines)
        {
            area.EnsureSpace(line.Height);
            if (first && level > 0)
            {
                AddHeading(area, level, text, area.Y);
            }

            first = false;
            var baseline = area.Y - line.Ascent;
            PlaceLine(area, line, node, links, baseline, area.Left + (rightToLeft ? 0 : indent), area.Width - indent, alignment, rightToLeft);
            area.Advance(line.Height);
        }

        area.Y -= Math.Min(spaceAfter, Math.Max(0, area.Available));
    }

    /// <summary>Places the fragments of a line: aligned, in visual order (the runs of a right-to-left paragraph from the right), the words of a justified line spread.</summary>
    private static void PlaceLine(FlowArea area, FlowLine line, StructNode node, Dictionary<int, StructNode> links, double baseline, double left, double width, PdfTextAlignment alignment, bool rightToLeft)
    {
        var fragments = line.Fragments;
        if (alignment == PdfTextAlignment.Justify && !line.EndsParagraph)
        {
            fragments = [.. fragments.SelectMany(SplitWords)];
        }

        var gaps = alignment == PdfTextAlignment.Justify && !line.EndsParagraph ? fragments.Count(fragment => fragment.Text == " ") : 0;
        var extra = gaps > 0 ? Math.Max(0, width - line.Width) / gaps : 0;
        var x = alignment switch
        {
            PdfTextAlignment.Center => left + ((width - line.Width) / 2),
            PdfTextAlignment.Right => left + width - line.Width,
            _ => left,
        };
        var ordered = rightToLeft ? Enumerable.Reverse(fragments) : fragments;
        foreach (var fragment in ordered)
        {
            if (fragment.Text != " " || extra == 0)
            {
                var target = node;
                if (fragment.Link is { } uri)
                {
                    if (!links.TryGetValue(fragment.RunIndex, out var link))
                    {
                        link = node.Add("Link");
                        links.Add(fragment.RunIndex, link);
                    }

                    target = link;
                    var descent = fragment.Size * 0.25;
                    area.Add(new LinkCommand(link, new PdfRectangle(x, baseline - descent, x + fragment.Width, baseline + fragment.Size), uri, -1, fragment.Text));
                }

                area.Add(new TextCommand(target, fragment.Text, fragment.Font, fragment.Size, fragment.Color, x, baseline));
            }

            x += fragment.Width + (fragment.Text == " " ? extra : 0);
        }
    }

    private static IEnumerable<FlowFragment> SplitWords(FlowFragment fragment)
    {
        var parts = fragment.Text.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                yield return fragment with { Text = parts[i], Width = fragment.Font.GetTextWidth(parts[i], fragment.Size) };
            }

            if (i + 1 < parts.Length)
            {
                yield return fragment with { Text = " ", Width = fragment.Font.GetTextWidth(" ", fragment.Size) };
            }
        }
    }

    private void LayoutList(FlowArea area, ListBlock list, StructNode parent, int depth)
    {
        var node = parent.Add("L");
        if (list.Ordered)
        {
            node.Attributes.Add(("List", "ListNumbering", PdfName.Create("Decimal")));
        }
        else
        {
            node.Attributes.Add(("List", "ListNumbering", PdfName.Create("Disc")));
        }

        var font = _options.Font;
        var labelWidth = font.GetTextWidth(list.Ordered ? list.Items.Count.ToString(CultureInfo.InvariantCulture) + ". " : "•  ", _options.FontSize) + 4;
        for (var i = 0; i < list.Items.Count; i++)
        {
            var item = node.Add("LI");
            var label = list.Ordered ? (i + 1).ToString(CultureInfo.InvariantCulture) + "." : "•";
            area.EnsureSpace(_options.FontSize * _options.LineHeight);
            var top = area.Y;
            var labelNode = item.Add("Lbl");
            var body = item.Add("LBody");
            var line = FlowText.Break([new FlowRun(label, null, null)], run => Resolve(run, null, 0), labelWidth, _options.LineHeight)[0];

            // The body is laid out in the space after the label, also in the columns it continues in
            var labelLeft = area.Left;
            var parts = GetParts(area);
            var labelPage = parts is null ? -1 : parts.Count - 1;
            var commandsBefore = parts is null ? 0 : parts[^1].Count;
            area.Indent += labelWidth;
            LayoutBlocks(area, list.Items[i], body, depth + 1);
            area.Indent -= labelWidth;

            // The label is on the first line of the body, which can be in the next column, before it in reading order
            var (bodyPage, bodyIndex, bodyTop, bodyLeft) = FindFirstText(area, body, labelPage, commandsBefore) ?? (labelPage, commandsBefore, top, labelLeft + labelWidth);
            Insert(area, new TextCommand(labelNode, label, line.Fragments[0].Font, line.Fragments[0].Size, _options.TextColor, bodyLeft - labelWidth, bodyTop - line.Ascent), bodyPage, bodyIndex);
        }

        area.Y -= Math.Min(_options.ParagraphSpacing, Math.Max(0, area.Available));
    }

    /// <summary>
    /// Gets where the first line of the body of a list item is, from its first text command: the page, the index of the
    /// command (the label is drawn before it, in reading order), and its top and left (the label is on that line).
    /// </summary>
    private static (int Page, int Index, double Top, double Left)? FindFirstText(FlowArea area, StructNode body, int page, int start)
    {
        if (GetParts(area) is not { } parts || page < 0)
            return null;

        for (var i = page; i < parts.Count; i++)
        {
            if (Find(parts[i], i == page ? start : 0, i) is { } found)
                return found;
        }

        return null;

        (int, int, double, double)? Find(List<DrawCommand> commands, int from, int pageIndex)
        {
            for (var i = from; i < commands.Count; i++)
            {
                if (commands[i] is TextCommand text && text.Node is { } node && IsDescendant(node, body))
                    return (pageIndex, i, text.Y + (text.Font.Ascent * text.Size / 1000), text.X);
            }

            return null;
        }
    }

    /// <summary>Inserts a command before another one of a page the area may have left, such as the label of a list item before the first line of its body.</summary>
    private static void Insert(FlowArea area, DrawCommand command, int page, int index)
    {
        if (GetParts(area) is not { } parts || page < 0 || page >= parts.Count)
        {
            area.Add(command);
            return;
        }

        var commands = parts[page];
        commands.Insert(Math.Clamp(index, 0, commands.Count), command);
    }

    /// <summary>Gets the commands of the parts of an area: its pages, or the parts of a cell.</summary>
    private static List<List<DrawCommand>>? GetParts(FlowArea area) => area switch
    {
        PageArea pages => pages.Pages,
        CellArea cell => cell.Parts,
        _ => null,
    };

    private static bool IsDescendant(StructNode node, StructNode ancestor)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private void LayoutImage(FlowArea area, ImageBlock block, StructNode parent)
    {
        var image = block.Image;
        var width = Math.Min(block.Width ?? Math.Min(image.Width, area.Width), area.Width);
        var height = width * image.Height / Math.Max(1, image.Width);
        if (height > area.FullHeight)
        {
            width *= area.FullHeight / height;
            height = area.FullHeight;
        }

        area.EnsureSpace(height);
        var node = parent.Add("Figure");
        node.AlternateDescription = block.AlternateDescription;
        var rectangle = new PdfRectangle(area.Left, area.Y - height, area.Left + width, area.Y);
        area.Add(new ImageCommand(node, image, rectangle));
        area.Advance(height);
        area.Y -= Math.Min(_options.ParagraphSpacing, Math.Max(0, area.Available));
    }

    private void LayoutTableOfContents(FlowArea area, TableOfContentsBlock block, StructNode parent)
    {
        var node = parent.Add("TOC");
        var titleStyle = new PdfParagraphStyle { TextStyle = new PdfTextStyle { Font = HeadingFont, FontSize = _options.FontSize * HeadingScales[1] }, SpaceAfter = _options.FontSize };
        LayoutParagraph(area, new ParagraphBlock([new FlowRun(block.Title, null, null)], titleStyle, HeadingLevel: 0), node, next: null);
        node.Children[^1].Children.Clear();
        for (var i = 0; i < _previousHeadings.Count; i++)
        {
            var heading = _previousHeadings[i];
            if (heading.Level > 3)
                continue;

            var entry = node.Add("TOCI");
            var link = entry.Add("Link");
            var indent = (heading.Level - 1) * _options.FontSize * 1.5;
            var number = (heading.PageIndex + 1).ToString(CultureInfo.InvariantCulture);
            var lines = FlowText.Break([new FlowRun(heading.Text, null, null)], run => Resolve(run, null, 0), Math.Max(1, area.Width - indent - PageNumberWidth), _options.LineHeight);
            for (var j = 0; j < lines.Count; j++)
            {
                var line = lines[j];
                area.EnsureSpace(line.Height);
                var baseline = area.Y - line.Ascent;
                var x = area.Left + indent;
                foreach (var fragment in line.Fragments)
                {
                    area.Add(new TextCommand(link, fragment.Text, fragment.Font, fragment.Size, fragment.Color, x, baseline));
                    x += fragment.Width;
                }

                if (j == lines.Count - 1)
                {
                    var numberWidth = _options.Font.GetTextWidth(number, _options.FontSize);
                    area.Add(new TextCommand(link, number, _options.Font, _options.FontSize, _options.TextColor, area.Left + area.Width - numberWidth, baseline));
                }

                area.Add(new LinkCommand(link, new PdfRectangle(area.Left + indent, area.Y - line.Height, area.Left + area.Width, area.Y), null, i, heading.Text));
                area.Advance(line.Height);
            }
        }

        area.Y -= Math.Min(_options.ParagraphSpacing, Math.Max(0, area.Available));
    }

    private void LayoutTable(FlowArea area, TableBlock table, StructNode parent, int depth)
    {
        var node = parent.Add("Table");
        var head = table.HeaderRowCount > 0 ? node.Add("THead") : null;
        var body = table.HeaderRowCount < table.RowCount ? node.Add("TBody") : null;
        var rows = new StructNode[table.RowCount];
        for (var r = 0; r < table.RowCount; r++)
        {
            rows[r] = (r < table.HeaderRowCount ? head! : body!).Add("TR");
        }

        // The columns, in proportion to their weights
        var total = table.Weights.Sum();
        var columnLeft = new double[table.Weights.Length + 1];
        for (var c = 0; c < table.Weights.Length; c++)
        {
            columnLeft[c + 1] = columnLeft[c] + (area.Width * table.Weights[c] / total);
        }

        // The content of each cell, laid out in its width
        var padding = table.CellPadding;
        var contents = new List<(TableCell Cell, StructNode Node, CellArea Content)>();
        foreach (var cell in table.Cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column))
        {
            var cellNode = rows[cell.Row].Add(cell.IsHeader ? "TH" : "TD");
            if (cell.IsHeader)
            {
                cellNode.Attributes.Add(("Table", "Scope", PdfName.Create("Column")));
            }

            if (cell.RowSpan > 1)
            {
                cellNode.Attributes.Add(("Table", "RowSpan", PdfInteger.Get(cell.RowSpan)));
            }

            if (cell.ColumnSpan > 1)
            {
                cellNode.Attributes.Add(("Table", "ColSpan", PdfInteger.Get(cell.ColumnSpan)));
            }

            var width = columnLeft[cell.Column + cell.ColumnSpan] - columnLeft[cell.Column] - (2 * padding);
            var content = new CellArea(width, area.FullHeight);
            LayoutBlocks(content, cell.Blocks, cellNode, depth + 1);
            contents.Add((cell, cellNode, content));
        }

        // The heights of the rows: the cells of one row, then the cells that span rows grow the last of them
        var heights = new double[table.RowCount];
        foreach (var (cell, _, content) in contents.Where(c => c.Cell.RowSpan == 1))
        {
            heights[cell.Row] = Math.Max(heights[cell.Row], -content.Y + (2 * padding));
        }

        foreach (var (cell, _, content) in contents.Where(c => c.Cell.RowSpan > 1))
        {
            var spanned = heights.AsSpan(cell.Row, cell.RowSpan);
            var needed = -content.Y + (2 * padding);
            var sum = 0d;
            foreach (var height in spanned)
            {
                sum += height;
            }

            if (needed > sum)
            {
                spanned[^1] += needed - sum;
            }
        }

        // The cells by row, so that each row is read once
        var cellsByRow = new List<(TableCell Cell, StructNode Node, CellArea Content)>[table.RowCount];
        foreach (var item in contents)
        {
            (cellsByRow[item.Cell.Row] ??= []).Add(item);
        }

        // The groups of rows that stay on one page when they fit a column: a row and the rows its cells span
        var groups = new List<(int Start, int End)>();
        for (var r = 0; r < table.RowCount;)
        {
            var end = r + 1;
            for (var i = r; i < end; i++)
            {
                foreach (var (cell, _, _) in cellsByRow[i] ?? [])
                {
                    end = Math.Max(end, cell.Row + cell.RowSpan);
                }
            }

            groups.Add((r, end));
            r = end;
        }

        var headerHeight = Sum(heights, 0, table.HeaderRowCount);
        var page = -1;
        foreach (var (start, end) in groups)
        {
            var groupHeight = Sum(heights, start, end);

            // The height of the group without the space after the content of the cells of its last row, which the bottom
            // of a column can leave out
            var contentHeight = Sum(heights, start, end - 1);
            var rowOffset = 0d;
            for (var row = start; row < end; row++)
            {
                foreach (var (cell, _, content) in cellsByRow[row] ?? [])
                {
                    if (cell.Row + cell.RowSpan == end)
                    {
                        contentHeight = Math.Max(contentHeight, rowOffset + (2 * padding) - content.ContentBottom);
                    }
                }

                rowOffset += heights[row];
            }

            // A group taller than the next column, or than the column it is at the top of, is split between columns
            if (area.CanBreak && contentHeight > area.Available && (area.AtTop || contentHeight > area.PeekBreaks(1).Height))
            {
                LayoutSplitRows(area, table, cellsByRow, heights, columnLeft, start, end, depth);
                page = area.PageIndex;
                continue;
            }

            area.EnsureSpace(groupHeight);

            // The header rows again, at the top of the next page, as an artifact, when the group fits below them: a group
            // that fits a page but not below the header is drawn alone, rather than beyond the margin
            if (page >= 0 && area.PageIndex != page && table.HeaderRowCount > 0 && start >= table.HeaderRowCount && headerHeight + groupHeight <= area.Available)
            {
                DrawRows(area, table, cellsByRow, heights, columnLeft, 0, table.HeaderRowCount, asArtifact: true);
                area.Advance(headerHeight);
            }

            // A group that fits the column only without the space after its content ends at the bottom of the column
            var rowHeights = heights;
            if (area.CanBreak && groupHeight > area.Available)
            {
                rowHeights = (double[])heights.Clone();
                rowHeights[end - 1] -= Math.Min(groupHeight - area.Available, groupHeight - contentHeight);
                groupHeight = Sum(rowHeights, start, end);
            }

            page = area.PageIndex;
            DrawRows(area, table, cellsByRow, rowHeights, columnLeft, start, end, asArtifact: false);
            area.Advance(groupHeight);
        }

        area.Y -= Math.Min(_options.ParagraphSpacing, Math.Max(0, area.Available));
    }

    private static double Sum(double[] values, int start, int end)
    {
        var sum = 0d;
        for (var i = start; i < end; i++)
        {
            sum += values[i];
        }

        return sum;
    }

    /// <summary>Records a heading: its page and place, or, in a cell, its place in the cell until the cell is placed.</summary>
    private void AddHeading(FlowArea area, int level, string text, double top)
    {
        switch (area)
        {
            case CellArea cell:
                cell.Headings.Add((level, text, top, cell.PartIndex));
                break;
            case { PageIndex: >= 0 }:
                Headings.Add(new PlacedHeading(level, text, area.PageIndex, top));
                break;
        }
    }

    /// <summary>Draws rows of a table at the top of the area: the backgrounds and borders as artifacts, and the content of the cells.</summary>
    private void DrawRows(FlowArea area, TableBlock table, List<(TableCell Cell, StructNode Node, CellArea Content)>?[] cellsByRow, double[] heights, double[] columnLeft, int start, int end, bool asArtifact)
    {
        // The tops of the rows of the group
        var rowTop = new double[end - start + 1];
        rowTop[0] = area.Y;
        for (var r = start; r < end; r++)
        {
            rowTop[r - start + 1] = rowTop[r - start] - heights[r];
        }

        for (var row = start; row < end; row++)
        {
            foreach (var (cell, _, content) in cellsByRow[row] ?? [])
            {
                DrawCell(area, table, cell, content, part: 0, new PdfRectangle(area.Left + columnLeft[cell.Column], rowTop[Math.Min(end, cell.Row + cell.RowSpan) - start], area.Left + columnLeft[cell.Column + cell.ColumnSpan], rowTop[cell.Row - start]), asArtifact);
            }
        }
    }

    /// <summary>
    /// Lays out and draws a group of rows taller than a column (a row and the rows its cells span), split between columns
    /// and pages as the browsers do: the content of its cells is laid out again in the parts of the columns, from where
    /// the group starts, below the header rows repeated at the top of the next pages, and each part of a cell is drawn
    /// with its background, its borders and its padding.
    /// </summary>
    private void LayoutSplitRows(FlowArea area, TableBlock table, List<(TableCell Cell, StructNode Node, CellArea Content)>?[] cellsByRow, double[] heights, double[] columnLeft, int start, int end, int depth)
    {
        var padding = table.CellPadding;
        var headerHeight = Sum(heights, 0, table.HeaderRowCount);
        var repeatHeader = table.HeaderRowCount > 0 && start >= table.HeaderRowCount;

        // The parts of the group: the rest of the column, then the next columns, below the header rows at the top of the
        // pages when they leave at least half of the page to the rows
        var parts = new List<(double Height, bool Header)>();
        (double Height, bool Header) GetPart(int index)
        {
            while (parts.Count <= index)
            {
                var (height, newPage) = area.PeekBreaks(parts.Count);
                var header = parts.Count > 0 && newPage && repeatHeader && headerHeight <= height / 2;
                parts.Add((header ? height - headerHeight : height, header));
            }

            return parts[index];
        }

        // An image of a cell is scaled down to fit a part
        var fullHeight = Math.Max(1, Math.Min(GetPart(1).Height, area.FullHeight - (repeatHeader ? headerHeight : 0)) - (2 * padding));

        // The tops of the rows, as a part and a distance from its top: each row starts below the cells that end above it
        var rowTop = new (int Part, double Y)[end - start + 1];
        var placed = new List<(TableCell Cell, CellArea Content)>();
        var ending = new List<(TableCell Cell, CellArea Content)>?[end - start];
        var position = (Part: 0, Y: 0d);
        for (var row = start; row < end; row++)
        {
            var cells = new List<(TableCell Cell, CellArea Content)>();
            foreach (var (cell, node, _) in cellsByRow[row] ?? [])
            {
                // The content of the cell is laid out again: its structure elements too
                node.Children.Clear();
                var top = position;
                var width = columnLeft[cell.Column + cell.ColumnSpan] - columnLeft[cell.Column] - (2 * padding);
                var content = new CellArea(width, fullHeight, part => (part == 0 ? GetPart(top.Part).Height - top.Y : GetPart(top.Part + part).Height) - (2 * padding), atTop: top.Y == 0 && (top.Part > 0 || area.AtTop));
                LayoutBlocks(content, cell.Blocks, node, depth + 1);
                cells.Add((cell, content));
            }

            // A row none of whose cells holds a line in the rest of the part starts in the next part: the rows above it
            // extend to the bottom of the part
            if (cells.TrueForAll(item => item.Content.Parts[0].Count == 0) && cells.Exists(item => item.Content.Parts.Count > 1))
            {
                position = (position.Part + 1, 0);
                foreach (var (_, content) in cells.Where(item => item.Content.Parts.Count > 1))
                {
                    content.Parts.RemoveAt(0);
                    for (var i = 0; i < content.Headings.Count; i++)
                    {
                        content.Headings[i] = content.Headings[i] with { Part = content.Headings[i].Part - 1 };
                    }
                }
            }

            rowTop[row - start] = position;
            placed.AddRange(cells);
            foreach (var item in cells)
            {
                (ending[item.Cell.Row + item.Cell.RowSpan - 1 - start] ??= []).Add(item);
            }

            // The bottom of the row: the lowest of the cells that end in it
            var bottom = position;
            foreach (var (cell, content) in ending[row - start] ?? [])
            {
                var top = rowTop[cell.Row - start];
                var last = content.Parts.Count - 1;
                // The padding of an empty cell at the bottom of a part does not go beyond it
                var cellBottom = (Part: top.Part + last, Y: (last == 0 ? top.Y : 0) + (2 * padding) - content.Y);
                cellBottom.Y = Math.Min(cellBottom.Y, Math.Max(GetPart(cellBottom.Part).Height, cellBottom.Part == top.Part ? top.Y : 0));
                if (cellBottom.Part > bottom.Part || (cellBottom.Part == bottom.Part && cellBottom.Y > bottom.Y))
                {
                    bottom = cellBottom;
                }
            }

            position = bottom;
        }

        rowTop[^1] = position;
        var lastPart = position.Part;
        if (lastPart > 0 && position.Y <= 0)
        {
            lastPart--;
        }

        GetPart(lastPart);
        for (var part = 0; part <= lastPart; part++)
        {
            if (part > 0)
            {
                area.Break();
                if (parts[part].Header)
                {
                    DrawRows(area, table, cellsByRow, heights, columnLeft, 0, table.HeaderRowCount, asArtifact: true);
                    area.Advance(headerHeight);
                }
            }

            var partTop = area.Y;
            foreach (var (cell, content) in placed)
            {
                var top = rowTop[cell.Row - start];
                var bottom = rowTop[cell.Row + cell.RowSpan - start];
                if (part < top.Part || part > bottom.Part)
                    continue;

                var segmentTop = part == top.Part ? top.Y : 0;
                var segmentBottom = part == bottom.Part ? bottom.Y : parts[part].Height;
                if (part > top.Part && segmentBottom <= segmentTop)
                    continue;

                var rectangle = new PdfRectangle(area.Left + columnLeft[cell.Column], partTop - segmentBottom, area.Left + columnLeft[cell.Column + cell.ColumnSpan], partTop - segmentTop);
                DrawCell(area, table, cell, content, part - top.Part, rectangle, asArtifact: false);
            }

            area.Advance(part == position.Part ? position.Y : parts[part].Height);
        }
    }

    private void DrawCell(FlowArea area, TableBlock table, TableCell cell, CellArea content, int part, PdfRectangle rectangle, bool asArtifact)
    {
        if (cell.IsHeader && table.HeaderBackground is { } background)
        {
            area.Add(new FillCommand(null, rectangle, background));
        }

        if (part < content.Parts.Count)
        {
            foreach (var command in content.Parts[part])
            {
                var placed = command.Translate(rectangle.Left + table.CellPadding, rectangle.Top - table.CellPadding);
                area.Add(asArtifact ? placed with { Node = null } : placed);
            }
        }

        // The headings of the cell are where the cell is: the outline and the table of contents link to them
        if (!asArtifact)
        {
            foreach (var (level, text, top, headingPart) in content.Headings)
            {
                if (headingPart == part)
                {
                    AddHeading(area, level, text, top + rectangle.Top - table.CellPadding);
                }
            }
        }

        if (table.BorderWidth > 0)
        {
            area.Add(new LineCommand(null, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Top, table.BorderWidth, table.BorderColor));
            area.Add(new LineCommand(null, rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Bottom, table.BorderWidth, table.BorderColor));
            area.Add(new LineCommand(null, rectangle.Left, rectangle.Bottom, rectangle.Left, rectangle.Top, table.BorderWidth, table.BorderColor));
            area.Add(new LineCommand(null, rectangle.Right, rectangle.Bottom, rectangle.Right, rectangle.Top, table.BorderWidth, table.BorderColor));
        }
    }

    private PdfDocument Write()
    {
        var document = new PdfDocument();
        if (_options.Title is { } title)
        {
            document.Information.Title = title;
        }

        if (_options.Language is { } language)
        {
            document.Language = language;
        }

        var pages = new List<PdfPage>();
        foreach (var _ in Pages.Pages)
        {
            pages.Add(document.Pages.Add(_options.PageSize));
        }

        if (_options.Tagged)
        {
            CreateElements(document.StructureTree.AddElement(Root.Type), Root, depth: 0);
        }

        for (var i = 0; i < pages.Count; i++)
        {
            WritePage(pages, i);
        }

        if (_options.CreateOutline)
        {
            WriteOutline(document, pages);
        }

        return document;
    }

    private static void CreateElements(PdfStructureElement element, StructNode node, int depth)
    {
        node.Element = element;
        if (node.AlternateDescription is { } alternate)
        {
            element.AlternateDescription = alternate;
        }

        foreach (var (owner, name, value) in node.Attributes)
        {
            element.SetAttribute(owner, name, value);
        }

        if (depth > MaxDepth * 8)
            return;

        foreach (var child in node.Children)
        {
            // A link is in the text of its paragraph: it is added to the paragraph when its text is written, so that the
            // kids of the paragraph are in reading order (14.7.2)
            if (child.Type != "Link")
            {
                CreateElements(element.AddElement(child.Type), child, depth + 1);
            }
        }
    }

    /// <summary>Gets the element of a node: a link gets its element the first time its content is written.</summary>
    private static PdfStructureElement? GetElement(StructNode? node)
    {
        if (node is null)
            return null;

        if (node.Element is null && node.Type == "Link" && node.Parent?.Element is { } parent)
        {
            node.Element = parent.AddElement(node.Type);
        }

        return node.Element;
    }

    private void WritePage(List<PdfPage> pages, int index)
    {
        var page = pages[index];
        var builder = page.CreateContentBuilder();
        var tagged = _options.Tagged;
        foreach (var command in Pages.Pages[index])
        {
            if (command is LinkCommand link)
            {
                var annotation = link.Uri is { } uri
                    ? page.Annotations.AddLink(link.Rectangle, uri)
                    : Headings.Count > link.HeadingIndex && link.HeadingIndex >= 0
                        ? page.Annotations.AddLink(link.Rectangle, PdfDestination.Xyz(pages[Headings[link.HeadingIndex].PageIndex], left: null, top: Headings[link.HeadingIndex].Top))
                        : null;
                if (annotation is not null)
                {
                    annotation.Contents = link.Text;
                    if (tagged && GetElement(link.Node) is { } element)
                    {
                        element.AddAnnotation(annotation);
                    }
                }

                continue;
            }

            if (tagged)
            {
                if (GetElement(command.Node) is { } element)
                {
                    builder.BeginStructureElement(element);
                }
                else
                {
                    builder.BeginArtifact(PdfArtifactType.Layout);
                }
            }

            switch (command)
            {
                case TextCommand text:
                    builder.SetFillColor(text.Color).DrawText(text.Text, text.Font, text.Size, text.X, text.Y);
                    break;
                case LineCommand line:
                    builder.SetStrokeColor(line.Color).SetLineWidth(line.Width).MoveTo(line.X1, line.Y1).LineTo(line.X2, line.Y2).Stroke();
                    break;
                case FillCommand fill:
                    builder.SetFillColor(fill.Color).Rectangle(fill.Rectangle).Fill();
                    break;
                case ImageCommand image:
                    builder.DrawImage(image.Image, image.Rectangle.Left, image.Rectangle.Bottom, image.Rectangle.Width, image.Rectangle.Height);
                    if (command.Node?.Element is { } figure)
                    {
                        figure.SetAttribute("Layout", "BBox", image.Rectangle.ToArray());
                    }

                    break;
            }

            if (tagged)
            {
                builder.EndMarkedContent();
            }
        }

        WriteMargins(builder, index, pages.Count, tagged);
        builder.Complete();
    }

    /// <summary>Writes the header and the footer of a page, centered in its margins, as pagination artifacts.</summary>
    private void WriteMargins(PdfContentBuilder builder, int index, int count, bool tagged)
    {
        foreach (var (format, isHeader) in new[] { (_options.Header, true), (_options.Footer, false) })
        {
            if (format?.Invoke(index + 1, count) is not { Length: > 0 } text)
                continue;

            var width = _options.Font.GetTextWidth(text, _options.FontSize * 0.9);
            var x = (_options.PageSize.Width - width) / 2;
            var y = isHeader ? _options.PageSize.Height - (_options.MarginTop / 2) : _options.MarginBottom / 2;
            if (tagged)
            {
                builder.BeginArtifact(PdfArtifactType.Pagination);
            }

            builder.SetFillColor(_options.TextColor).DrawText(text, _options.Font, _options.FontSize * 0.9, x, y);
            if (tagged)
            {
                builder.EndMarkedContent();
            }
        }
    }

    private void WriteOutline(PdfDocument document, List<PdfPage> pages)
    {
        var stack = new List<(int Level, PdfOutlineCollection Children)> { (0, document.Outlines) };
        foreach (var heading in Headings)
        {
            while (stack.Count > 1 && stack[^1].Level >= heading.Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var item = stack[^1].Children.Add(heading.Text, PdfDestination.Xyz(pages[heading.PageIndex], left: null, top: heading.Top));
            stack.Add((heading.Level, item.Children));
        }
    }
}
