using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the pages of another inline size (the content of a page whose page area has another inline size is laid out
/// again in it, from the break): the input is decoded to the sizes of the first page and of the next ones, a container of rows
/// (a table without a header, with a repeated header or with a header too tall to be repeated, a column flex container, a grid
/// with a fixed or an automatic number of columns, a row flex container that wraps, a block, a table row whose rows are its cells,
/// a single-line row flex container whose rows are its items) and its rows (their block sizes,
/// whether they avoid breaks, a paragraph); the second target has rows whose items are aligned in them or span the next rows, whose first pages may not follow their order. Each row is in a page, the first pages of the rows follow their order, each word of
/// the paragraphs is laid out once, and the rectangles of the fragments are finite. The third target has monolithic content that
/// the breaks slice (see <see cref="PagedInlineSizeSliced"/>).
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private const int PagedInlineSizeMaxRows = 12;
    private const double PagedInlineSizeTolerance = 0.01;

    /// <summary>The containers of the rows: the opening markup, the markup of a row (its id, its style and its content), the closing markup.</summary>
    private static readonly (string Start, string Row, string End)[] PagedInlineSizeContainers =
    [
        ("<table style='width: 100%; border-spacing: 0'><tbody>", "<tr id={0} style='{1}'><td>{2}</td></tr>", "</tbody></table>"),
        ("<table style='width: 100%; border-spacing: 0'><thead><tr style='height: 15px'><td></td></tr></thead><tbody>", "<tr id={0} style='{1}'><td>{2}</td></tr>", "</tbody></table>"),
        ("<div style='display: flex; flex-direction: column'>", "<div id={0} style='{1}'>{2}</div>", "</div>"),
        ("<div style='display: grid; grid-template-columns: 1fr 1fr'>", "<div id={0} style='{1}'>{2}</div>", "</div>"),
        ("<div style='display: grid; grid-template-columns: repeat(auto-fill, 90px)'>", "<div id={0} style='{1}'>{2}</div>", "</div>"),
        ("<div style='display: flex; flex-wrap: wrap'>", "<div id={0} style='width: 90px; {1}'>{2}</div>", "</div>"),
        ("<div>", "<div id={0} style='{1}'>{2}</div>", "</div>"),

        // (a header that avoids breaks and is not repeated, when it is taller than a quarter of the page)
        ("<table style='width: 100%; border-spacing: 0'><thead style='break-inside: avoid'><tr style='height: 60px'><td></td></tr></thead><tbody>", "<tr id={0} style='{1}'><td>{2}</td></tr>", "</tbody></table>"),

        // (the items of one row, side by side: the cells of a table row, the items of a single-line flex container)
        ("<table style='width: 100%; border-spacing: 0'><tr>", "<td id={0} style='{1}'>{2}</td>", "</tr></table>"),
        ("<div style='display: flex'>", "<div id={0} style='flex: 1 1 0; {1}'>{2}</div>", "</div>"),
    ];

    /// <summary>
    /// The containers of the target of the rows whose items are aligned in them or span the next rows: the items of a flex
    /// container or of a grid aligned in their row (every second or third item), the items of a grid that span two rows (every
    /// third item), and the cells of a table that span two rows.
    /// </summary>
    private static readonly (string Start, string Row, string End)[] PagedInlineSizeRowContainers =
    [
        ("<style>#c > :nth-child(2n) { align-self: center }</style><div id=c style='display: flex'>", "<div id={0} style='flex: 1 1 0; min-width: 0; {1}'>{2}</div>", "</div>"),
        ("<style>#c > :nth-child(2n) { align-self: end } #c > :nth-child(3n) { grid-row: span 2 }</style><div id=c style='display: grid; grid-template-columns: 1fr 1fr'>", "<div id={0} style='{1}'>{2}</div>", "</div>"),
        ("<style>#c > :nth-child(3n) { align-self: flex-end }</style><div id=c style='display: flex; flex-wrap: wrap'>", "<div id={0} style='width: 90px; {1}'>{2}</div>", "</div>"),
        ("<style>#c > * { align-self: center }</style><div id=c style='display: grid; grid-template-columns: repeat(auto-fill, 90px)'>", "<div id={0} style='{1}'>{2}</div>", "</div>"),
        ("<table style='width: 100%; border-spacing: 0'>", "<tr><td id={0} rowspan=2 style='{1}'>{2}</td><td></td></tr>", "</table>"),
    ];

    /// <summary>Decodes the input to a document whose first page has another inline size than the next ones, and checks its pages.</summary>
    public static void PagedInlineSize(ReadOnlySpan<byte> data) => PagedInlineSize(data, PagedInlineSizeContainers, rowsFollowTheirOrder: true);

    /// <summary>Decodes the input to a document whose first page has another inline size than the next ones, with rows whose items are aligned in them or span the next rows, and checks its pages.</summary>
    public static void PagedInlineSizeRows(ReadOnlySpan<byte> data) => PagedInlineSize(data, PagedInlineSizeRowContainers, rowsFollowTheirOrder: false);

    private static void PagedInlineSize(ReadOnlySpan<byte> data, (string Start, string Row, string End)[] containers, bool rowsFollowTheirOrder)
    {
        var position = 0;
        byte Next(ReadOnlySpan<byte> input) => position < input.Length ? input[position++] : (byte)0;

        // (the sizes of the pages are multiples of 5px, from 25px to 520px)
        var firstWidth = ((Next(data) % 100) + 5) * 5;
        var width = ((Next(data) % 100) + 5) * 5;
        var height = ((Next(data) % 100) + 5) * 5;
        var (start, row, end) = containers[Next(data) % containers.Length];
        var rowCount = (Next(data) % PagedInlineSizeMaxRows) + 1;
        var markup = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<!DOCTYPE html><style>body {{ margin: 0; font: 10px/1 Ahem }} @page {{ size: {width}px {height}px; margin: 0 }} @page :first {{ size: {firstWidth}px {height}px }}</style>"));
        markup.Append(start);
        var words = 0;
        for (var i = 0; i < rowCount; i++)
        {
            var flags = Next(data);
            var style = string.Create(CultureInfo.InvariantCulture, $"height: {Next(data) % 200}px{((flags & 1) != 0 ? "; break-inside: avoid" : "")}");
            var wordCount = (flags & 2) != 0 ? Next(data) % 40 : 0;
            if (wordCount > 0)
            {
                // (the row grows with its paragraph)
                style = (flags & 1) != 0 ? "break-inside: avoid" : "";
            }

            words += wordCount;
            var content = string.Join(' ', Enumerable.Repeat("XXXX", wordCount));
            markup.Append(string.Format(CultureInfo.InvariantCulture, row, "r" + i.ToString(CultureInfo.InvariantCulture), style, content));
        }

        markup.Append(end);

        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => markup.ToString());
        context.EventLoop.RunUntilIdle();
        using var session = document.BeginPrint(new PrintOptions { PageWidth = width, PageHeight = height });
        var pages = session.GetPages();
        FuzzAssert.True(pages.Count > 0, "the document has a page");

        // The first page of each row, and the words of the text fragments
        var firstPages = new int?[rowCount];
        var laidOutWords = 0;
        foreach (var page in pages)
        {
            Visit(page.Content, page.Index, top: 0);
        }

        var previous = 0;
        for (var i = 0; i < rowCount; i++)
        {
            FuzzAssert.True(firstPages[i] is not null, string.Create(CultureInfo.InvariantCulture, $"the row {i} is in a page: {markup}"));
            // (an item aligned in its row may start in a page after the one of the next item)
            FuzzAssert.True(!rowsFollowTheirOrder || firstPages[i] >= previous, string.Create(CultureInfo.InvariantCulture, $"the row {i} starts in a page after the one of the previous row: {markup}"));
            previous = firstPages[i]!.Value;
        }

        FuzzAssert.Equal(words, laidOutWords, $"the number of words laid out in the pages: {markup}");

        void Visit(Fragment fragment, int index, double top)
        {
            FuzzAssert.True(double.IsFinite(fragment.Offset.X) && double.IsFinite(fragment.Offset.Y) && double.IsFinite(fragment.Size.Width) && double.IsFinite(fragment.Size.Height), "the rectangle of a fragment is finite");
            if (fragment is BoxFragment { Box: { Element: { } element, PseudoElement: null } } && element.Id is ['r', .. var number] && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var rowIndex) && rowIndex < rowCount)
            {
                firstPages[rowIndex] ??= index;
            }

            // A line that does not fit and cannot be pushed to the next page (only the block-start padding of its box precedes it
            // in the page) is sliced, as the monolithic content: its rest, above the top of the next page, is not another line
            top += fragment.Offset.Y;
            if (fragment is TextFragment text && top >= -PagedInlineSizeTolerance)
            {
                laidOutWords += text.Run.Data[text.Start..text.End].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }

            foreach (var child in fragment.ChildrenSpan)
            {
                Visit(child, index, top);
            }
        }
    }

    /// <summary>The seeds: the scenarios of the unit tests (rows of 100px on a first page of 200x300 then pages of 400x300, a paragraph in a column flex container, an automatic grid, a flex container that wraps).</summary>
    private static IEnumerable<FuzzSeed> GetPagedInlineSizeSeeds()
    {
        byte[][] scenarios =
        [
            // Tables, a column flex container, a grid: 5 rows of 100px
            [35, 75, 55, 0, 4, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100],
            [35, 75, 55, 1, 3, 1, 100, 1, 100, 1, 100, 1, 100],
            [35, 75, 55, 2, 4, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100],
            [35, 75, 55, 3, 9, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100],

            // A paragraph broken in a column flex container, in a block
            [35, 75, 55, 2, 1, 0, 100, 2, 0, 39],
            [35, 75, 55, 6, 0, 2, 0, 39],

            // A grid whose columns depend on its inline size keeps its layout
            [35, 75, 55, 4, 7, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100],

            // A row flex container that wraps, broken between its flex lines (the items after the break start a new flex line),
            // with paragraphs, and broken inside a flex line (it keeps its layout)
            [35, 75, 55, 5, 7, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100, 0, 100],
            [35, 75, 55, 5, 7, 2, 0, 20, 0, 100, 2, 0, 39, 0, 100, 0, 100, 3, 0, 10, 0, 100, 0, 100],
            [35, 75, 55, 5, 3, 0, 199, 0, 199, 0, 199, 0, 100],

            // A row that avoids breaks and does not fit below a header that is not repeated starts the next page
            [55, 55, 8, 7, 1, 3, 0, 28, 2, 0, 2],

            // Rows broken between the lines of the paragraphs of their items, side by side (pages of 100px): a grid row, the
            // cells of a table row, the items of a single-line flex container, one of them ending before the break
            [35, 75, 15, 3, 1, 2, 0, 39, 2, 0, 30],
            [35, 75, 15, 8, 2, 2, 0, 39, 2, 0, 30, 2, 0, 12],
            [35, 75, 15, 9, 2, 2, 0, 39, 2, 0, 30, 2, 0, 12],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("paged-inline-size-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    /// <summary>
    /// The seeds of the target of the aligned rows: the scenarios of the unit tests (pages of 100px whose first one is 200px wide and the next ones 400px wide, rows broken between the lines
    /// of the paragraphs of their items, an item aligned in the row, a cell that spans the next row).
    /// </summary>
    private static IEnumerable<FuzzSeed> GetPagedInlineSizeRowSeeds()
    {
        byte[][] scenarios =
        [
            // A flex row and a grid row whose second item is aligned in the row, a flex line whose third item is aligned in it
            [35, 75, 15, 0, 1, 2, 0, 39, 2, 0, 30],
            [35, 75, 15, 1, 1, 2, 0, 39, 2, 0, 30],
            [35, 75, 15, 2, 2, 2, 0, 39, 2, 0, 30, 2, 0, 20],

            // A grid whose columns depend on its inline size, whose items are centered in their rows
            [35, 75, 15, 3, 6, 0, 100, 2, 0, 39, 0, 100, 0, 100, 2, 0, 20, 0, 100, 0, 100],

            // The cells of a table that span two rows
            [35, 75, 15, 4, 2, 2, 0, 39, 2, 0, 39, 2, 0, 4],

            // An item aligned after the break
            [35, 75, 15, 1, 1, 2, 0, 39, 0, 100],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("paged-inline-size-rows-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    /// <summary>
    /// Decodes the input to a document whose first page has another inline size than the next ones, with monolithic content
    /// that the breaks may slice (a box with size containment of a fixed or percentage width, centered or not, an SVG image, an
    /// inline-block in a paragraph, between words; a monolithic item beside a paragraph in a grid row, a flex row, a table row)
    /// between paragraphs, and checks its pages: each item is in a page, the first pages of the items follow their order, each
    /// word is laid out once, and the parts of a sliced item continue each other (the same size, and the same inline position
    /// out of a row, from the end of the previous page).
    /// </summary>
    public static void PagedInlineSizeSliced(ReadOnlySpan<byte> data)
    {
        var position = 0;
        byte Next(ReadOnlySpan<byte> input) => position < input.Length ? input[position++] : (byte)0;

        var firstWidth = ((Next(data) % 100) + 5) * 5;
        var width = ((Next(data) % 100) + 5) * 5;
        var height = ((Next(data) % 100) + 5) * 5;
        var itemCount = (Next(data) % PagedInlineSizeMaxRows) + 1;
        var markup = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<!DOCTYPE html><style>body {{ margin: 0; font: 10px/1 Ahem }} @page {{ size: {width}px {height}px; margin: 0 }} @page :first {{ size: {firstWidth}px {height}px }}</style>"));
        var words = 0;
        var isMonolithic = new bool[itemCount];
        var isInRow = new bool[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            var kind = Next(data);
            var size = Next(data);
            var extent = Next(data) * 3;
            var id = "r" + i.ToString(CultureInfo.InvariantCulture);
            var wordCount = Next(data) % 20;
            var text = string.Join(' ', Enumerable.Repeat("XXXX", wordCount));
            words += wordCount;
            isMonolithic[i] = (kind % 8) != 0;
            isInRow[i] = (kind % 8) >= 5;
            markup.Append((kind % 8) switch
            {
                0 => string.Create(CultureInfo.InvariantCulture, $"<p id={id} style='margin: {size % 30}px 0'>{text}</p>"),
                1 => string.Create(CultureInfo.InvariantCulture, $"<div id={id} style='contain: size; width: {size}px; height: {extent}px{((kind & 8) != 0 ? "; margin: 0 auto" : "")}'></div><p style='margin: 0'>{text}</p>"),
                2 => string.Create(CultureInfo.InvariantCulture, $"<div id={id} style='contain: size; width: {size % 101}%; height: {extent}px'></div><p style='margin: 0'>{text}</p>"),
                3 => string.Create(CultureInfo.InvariantCulture, $"<svg id={id} width={size} height={extent} style='display: block'></svg><p style='margin: 0'>{text}</p>"),
                4 => string.Create(CultureInfo.InvariantCulture, $"<p style='margin: 0{((kind & 8) != 0 ? "; text-align: center" : "")}'>{((kind & 16) != 0 ? text : "")}<span id={id} style='display: inline-block; width: {size}px; height: {extent}px; vertical-align: top'></span>{((kind & 16) != 0 ? "" : text)}</p>"),
                5 => string.Create(CultureInfo.InvariantCulture, $"<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div>{text}</div><div id={id} style='contain: size; width: {size}px; height: {extent}px'></div></div>"),
                6 => string.Create(CultureInfo.InvariantCulture, $"<div style='display: flex; align-items: flex-start'><div style='flex: 1 1 0; min-width: 0'>{text}</div><div id={id} style='flex: none; contain: size; width: {size}px; height: {extent}px'></div></div>"),
                _ => string.Create(CultureInfo.InvariantCulture, $"<table style='width: 100%; border-spacing: 0'><tr><td style='padding: 0'>{text}</td><td style='padding: {size % 30}px 0 0'><div id={id} style='contain: size; width: {size}px; height: {extent}px'></div></td></tr></table>"),
            });
        }

        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => markup.ToString());
        context.EventLoop.RunUntilIdle();
        using var session = document.BeginPrint(new PrintOptions { PageWidth = width, PageHeight = height });
        var pages = session.GetPages();
        FuzzAssert.True(pages.Count > 0, "the document has a page");

        // The fragments of the items by page (their rectangles in the page), and the words of the text fragments that start in their
        // page (the part of a sliced line after the end of the page is in the next page)
        var fragments = new List<(int Page, double X, double Y, double Width, double Height)>[itemCount];
        var laidOutWords = 0;
        foreach (var page in pages)
        {
            Visit(page.Content, page.Index, x: 0, y: 0);
        }

        var previous = 0;
        for (var i = 0; i < itemCount; i++)
        {
            FuzzAssert.True(fragments[i] is { Count: > 0 }, string.Create(CultureInfo.InvariantCulture, $"the item {i} is in a page: {markup}"));
            FuzzAssert.True(fragments[i][0].Page >= previous, string.Create(CultureInfo.InvariantCulture, $"the item {i} starts in a page after the one of the previous item: {markup}"));
            previous = fragments[i][0].Page;
            if (!isMonolithic[i])
                continue;

            // (the part of a sliced item in a page continues the part in the previous page; an item without block size is not
            // sliced, it may be at the end of a page and at the start of the next one)
            for (var j = 1; j < fragments[i].Count && fragments[i][0].Height > PagedInlineSizeTolerance; j++)
            {
                var (page, x, y, fragmentWidth, fragmentHeight) = fragments[i][j];
                var before = fragments[i][j - 1];
                FuzzAssert.True(page == before.Page + 1 && (isInRow[i] || Math.Abs(x - before.X) <= PagedInlineSizeTolerance) && Math.Abs(fragmentWidth - before.Width) <= PagedInlineSizeTolerance
                    && Math.Abs(fragmentHeight - before.Height) <= PagedInlineSizeTolerance && Math.Abs(y - (before.Y - height)) <= PagedInlineSizeTolerance,
                    string.Create(CultureInfo.InvariantCulture, $"the part of the item {i} in the page {page} ({x}, {y}, {fragmentWidth}x{fragmentHeight}) continues the part in the previous page ({before.X}, {before.Y}, {before.Width}x{before.Height}): {markup}"));
            }
        }

        FuzzAssert.Equal(words, laidOutWords, $"the number of words laid out in the pages: {markup}");

        void Visit(Fragment fragment, int index, double x, double y)
        {
            FuzzAssert.True(double.IsFinite(fragment.Offset.X) && double.IsFinite(fragment.Offset.Y) && double.IsFinite(fragment.Size.Width) && double.IsFinite(fragment.Size.Height), "the rectangle of a fragment is finite");
            x += fragment.Offset.X;
            y += fragment.Offset.Y;
            if (fragment is BoxFragment { Box: { Element: { } element, PseudoElement: null } } && element.Id is ['r', .. var number] && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var itemIndex) && itemIndex < itemCount)
            {
                (fragments[itemIndex] ??= []).Add((index, x, y, fragment.Size.Width, fragment.Size.Height));
            }

            if (fragment is TextFragment text && y >= -PagedInlineSizeTolerance && y < height - PagedInlineSizeTolerance)
            {
                laidOutWords += text.Run.Data[text.Start..text.End].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }

            foreach (var child in fragment.ChildrenSpan)
            {
                Visit(child, index, x, y);
            }
        }
    }

    /// <summary>
    /// The seeds of the target of the sliced content: the scenarios of the unit tests (pages of 300px whose first one is 200px
    /// wide and the next ones 400px wide, content of 400px or 700px that the break slices, followed by a paragraph or beside one
    /// in a row).
    /// </summary>
    private static IEnumerable<FuzzSeed> GetPagedInlineSizeSlicedSeeds()
    {
        byte[][] scenarios =
        [
            // A box with size containment, 150px wide (400px tall, 699px tall), 50% wide, centered; an SVG image
            [35, 75, 55, 0, 1, 150, 133, 4],
            [35, 75, 55, 0, 1, 150, 233, 4],
            [35, 75, 55, 0, 2, 50, 133, 4],
            [35, 75, 55, 0, 9, 150, 133, 4],
            [35, 75, 55, 0, 3, 150, 133, 4],

            // An inline-block alone in its line box, centered, followed and preceded by words
            [35, 75, 55, 0, 4, 150, 133, 0],
            [35, 75, 55, 0, 12, 150, 133, 0],
            [35, 75, 55, 0, 4, 170, 133, 2],
            [35, 75, 55, 0, 20, 150, 133, 2],

            // A monolithic item beside a paragraph in a grid row, a flex row, a table row
            [35, 75, 55, 0, 5, 80, 133, 19],
            [35, 75, 55, 0, 6, 80, 133, 19],
            [35, 75, 55, 0, 7, 80, 133, 19],

            // Paragraphs around a sliced box
            [35, 75, 55, 2, 0, 0, 0, 19, 1, 150, 133, 19, 0, 10, 0, 19],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("paged-inline-size-sliced-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }

    /// <summary>The tokens of the target: a row of 100px, a row that avoids breaks, a row with a paragraph.</summary>
    private static string[] PagedInlineSizeTokens => ["\u0000d", "\u0001d", "\u0002\u0000'"];
}
