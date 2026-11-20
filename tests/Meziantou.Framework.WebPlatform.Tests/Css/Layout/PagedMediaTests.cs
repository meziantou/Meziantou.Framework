using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The paginated layout of the printed documents (https://drafts.csswg.org/css-page-3/, see <see cref="PrintSession"/>).</summary>
public sealed class PagedMediaTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    /// <summary>Pages of 400x300 with margins of 50px: the page areas are 300x200.</summary>
    private static readonly PrintOptions Options = new() { PageWidth = 400, PageHeight = 300, MarginTop = 50, MarginRight = 50, MarginBottom = 50, MarginLeft = 50 };

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a document whose body has no margin and the 10px/1 Ahem font.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Returns the rectangles of the fragments of an element in the pages: the page index and the rectangle in the page area.</summary>
    private static List<(int Page, PhysicalRect Rect)> GetPageRects(IReadOnlyList<LayoutPage> pages, Element element)
    {
        var result = new List<(int, PhysicalRect)>();
        foreach (var page in pages)
        {
            Collect(page.Content, PhysicalOffset.Zero, page.Index);
        }

        return result;

        void Collect(Fragment fragment, PhysicalOffset origin, int index)
        {
            var offset = origin + fragment.Offset;
            if (fragment is BoxFragment { Box: { Element: var owner, PseudoElement: null } } && ReferenceEquals(owner, element))
            {
                result.Add((index, new PhysicalRect(offset, fragment.Size)));
            }

            foreach (var child in fragment.ChildrenSpan)
            {
                Collect(child, offset, index);
            }
        }
    }

    [Fact]
    public void Pages_ContentIsFragmentedIntoPageAreas()
    {
        var document = Parse("<div id=a style='height: 500px'></div>");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal(new PhysicalRect(50, 50, 300, 200), pages[0].Geometry.PageArea);
        Assert.Equal([(0, new PhysicalRect(0, 0, 300, 200)), (1, new PhysicalRect(0, 0, 300, 200)), (2, new PhysicalRect(0, 0, 300, 100))], GetPageRects(pages, document.GetElementById("a")!));
    }

    [Fact]
    public void Pages_PrintMediaTypeAndViewportUnits()
    {
        // The media queries match print; the viewport-relative lengths refer to the page area of the first page
        var document = Parse("<style>@media print { #a { width: 50vw; height: 25vh } }</style><div id=a></div>");
        using (var session = document.BeginPrint(Options))
        {
            var pages = session.GetPages();
            Assert.Equal([(0, new PhysicalRect(0, 0, 150, 50))], GetPageRects(pages, document.GetElementById("a")!));
        }

        // The screen is not styled again: the element has no size
        Assert.Equal(0, document.GetElementById("a")!.GetBoundingClientRect().Height);
    }

    [Fact]
    public void Pages_ForcedBreaksAndPageRules()
    {
        // @page size and margins, :first; break-before: page
        var document = Parse("""
            <style>
              @page { size: 200px 100px; margin: 10px }
              @page :first { margin-top: 20px; margin-bottom: 20px }
            </style>
            <div id=a style='height: 10px'></div>
            <div id=b style='height: 10px; break-before: page'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal(new PhysicalSize(200, 100), pages[0].Geometry.PageSize);
        Assert.Equal(new PhysicalRect(10, 20, 180, 60), pages[0].Geometry.PageArea);
        Assert.Equal(new PhysicalRect(10, 10, 180, 80), pages[1].Geometry.PageArea);
        Assert.Equal([(1, new PhysicalRect(0, 0, 180, 10))], GetPageRects(pages, document.GetElementById("b")!));
    }

    [Fact]
    public void Pages_NamedPagesForceBreaks()
    {
        // https://drafts.csswg.org/css-page-3/#using-named-pages: a change of page name forces a page break
        var document = Parse("""
            <style>@page wide { size: 400px 200px }</style>
            <div style='height: 10px'></div>
            <div id=a style='page: wide; height: 10px'></div>
            <div style='page: wide; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(["", "wide"], pages.Select(page => page.Name));
        Assert.Equal(new PhysicalSize(400, 200), pages[1].Geometry.PageSize);
        Assert.Equal(1, Assert.Single(GetPageRects(pages, document.GetElementById("a")!)).Page);
    }

    [Fact]
    public void Pages_ForcedBreaksAtTheStartOfAPageAreIgnored()
    {
        // As in Gecko, the forced breaks after boxes without block size at the start of a page do not start another page (the
        // page would only contain these boxes)
        var document = Parse("""
            <div style='height: 10px'></div>
            <div style='page: b; height: 0'></div>
            <div style='page: c; height: 0; break-after: page'></div>
            <div id=a style='page: d; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(1, new PhysicalRect(0, 0, 300, 10))], GetPageRects(pages, document.GetElementById("a")!));
    }

    [Fact]
    public void Pages_FloatBeforeForcedBreakStaysInItsPage()
    {
        // As in Blink, a float whose margin box crosses a forced break stays in the page of its position in the content, and the
        // content after the break is not laid out beside it
        var document = Parse("""
            <div id=a style='float: right; width: 10px; height: 10px; margin-top: 100px'></div>
            <div style='height: 10px'></div>
            <div style='break-before: page'><div id=b style='float: right; width: 10px; height: 10px'></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(290, 100, 10, 10))], GetPageRects(pages, document.GetElementById("a")!));
        Assert.Equal([(1, new PhysicalRect(290, 0, 10, 10))], GetPageRects(pages, document.GetElementById("b")!));
    }

    [Fact]
    public void Pages_StaticPositionBeforeForcedBreak()
    {
        // As in Blink, an absolutely positioned box whose static position is in the block-start margin preserved after a forced
        // break, before the box after the break in the content, is in the page before the break
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 20px'></div>
            <div id=a style='position: absolute; width: 10px; height: 10px'></div>
            <div style='break-before: page; margin-top: 20px; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 30, 10, 10))], GetPageRects(pages, document.GetElementById("a")!));
    }

    [Fact]
    public void Pages_PageMarginSafety()
    {
        // https://drafts.csswg.org/css-page-3/#page-margin-safety: clamp keeps the margins at least the safe printable inset,
        // add adds the inset to them; without the descriptor, the inset does not change the margins
        var document = Parse("""
            <style>
              @page { margin: 10px 30px 0 0 }
              @page :first { page-margin-safety: clamp }
              @page wide { page-margin-safety: add }
            </style>
            <div style='height: 10px'></div>
            <div style='page: wide; height: 10px'></div>
            <div style='page: plain; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options with { SafePrintableInset = 20 });
        var pages = session.GetPages();
        Assert.Equal(new PhysicalRect(20, 20, 350, 260), pages[0].Geometry.PageArea);
        Assert.Equal(new PhysicalRect(20, 30, 330, 250), pages[1].Geometry.PageArea);
        Assert.Equal(new PhysicalRect(0, 10, 370, 290), pages[2].Geometry.PageArea);
    }

    [Fact]
    public void Pages_BreakToRightPageInsertsBlankPage()
    {
        // The first page is a right page: a break to a right page after it inserts a blank left page
        var document = Parse("<div style='height: 10px'></div><div id=a style='height: 10px; break-before: right'></div>");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([false, true, false], pages.Select(page => page.IsBlank));
        Assert.Equal([false, true, false], pages.Select(page => page.IsLeft));
        Assert.Equal(2, Assert.Single(GetPageRects(pages, document.GetElementById("a")!)).Page);
    }

    [Fact]
    public void Pages_FixedPositionedBoxesAreRepeated()
    {
        var document = Parse("<div style='height: 300px'></div><div id=a style='position: fixed; right: 0; bottom: 0; width: 10px; height: 10px'></div>");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(290, 190, 10, 10)), (1, new PhysicalRect(290, 190, 10, 10))], GetPageRects(pages, document.GetElementById("a")!));
    }

    [Fact]
    public void Pages_MonolithicContentIsSliced()
    {
        // A monolithic box taller than a page is sliced: the next page shows the rest of it, and the content after it follows it
        var document = Parse("<div id=a style='contain: size; height: 300px'></div><div id=b style='height: 10px'></div>");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 0, 300, 300)), (1, new PhysicalRect(0, -200, 300, 300))], GetPageRects(pages, document.GetElementById("a")!));
        Assert.Equal([(1, new PhysicalRect(0, 100, 300, 10))], GetPageRects(pages, document.GetElementById("b")!));
    }

    [Fact]
    public void Pages_MonolithicContentAfterBordersIsSliced()
    {
        // As in Blink, the break after the block-start border of a box that starts the page is not a possible break: the
        // monolithic content that does not fit stays in the page and is sliced
        var document = Parse("<div style='border-top: 5px solid'><div id=a style='contain: size; height: 300px'></div></div>");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 5, 300, 300)), (1, new PhysicalRect(0, -195, 300, 300))], GetPageRects(pages, document.GetElementById("a")!));
    }

    [Fact]
    public void Pages_GridLanesItemsArePlacedAgainInTheNextPage()
    {
        // The third item cannot be broken and does not fit in the first page: it is pushed, and its track is closed (the fourth
        // item is placed in the other track); the pushed item is placed again in the next page, in its first track
        var document = Parse("""
            <div style='display: grid-lanes; grid-template-columns: 25px 25px'>
              <div id=a style='height: 150px'></div>
              <div id=b style='height: 120px'></div>
              <div id=c style='height: 100px; break-inside: avoid-page'></div>
              <div id=d style='height: 20px'></div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 150, 25, 20))], GetPageRects(pages, document.GetElementById("d")!));
        Assert.Equal([(1, new PhysicalRect(0, 0, 25, 100))], GetPageRects(pages, document.GetElementById("c")!));
    }

    [Fact]
    public void Pages_GridLanesItemsHaveThePageBreaks()
    {
        // In the pages, break-before: page forces a break before an item of a grid lanes container and break-before: column does not
        var document = Parse("""
            <div style='display: grid-lanes; grid-template-columns: 25px'>
              <div style='height: 150px'></div>
              <div id=b style='height: 30px; break-before: column'></div>
              <div id=c style='height: 30px; break-before: page'></div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 150, 25, 30))], GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal([(1, new PhysicalRect(0, 0, 25, 30))], GetPageRects(pages, document.GetElementById("c")!));
    }

    [Fact]
    public void Pages_MarginBoxesWithPageCounters()
    {
        var document = Parse("""
            <style>@page { @bottom-center { content: "Page " counter(page) " of " counter(pages) } }</style>
            <div style='height: 10px'></div><div style='height: 10px; break-before: page'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        var box = Assert.Single(pages[1].MarginBoxes!);
        var text = Assert.IsType<TextRun>(box.Box.FirstChild);
        Assert.Equal("Page 2 of 2", text.Data);

        // The bottom-center box is the only box of the bottom edge: it fills the edge between the corners, in the bottom margin
        Assert.Equal(new PhysicalRect(50, 250, 300, 50), new PhysicalRect(box.Offset, box.Size));
    }

    [Fact]
    public void Pages_PageBoxSizedByWidthAndHeight()
    {
        // The automatic margins center the page box in the page sheet; without automatic margin, the page sheet is resized to
        // contain the page box and its margins
        var document = Parse("""
            <style>
              @page { size: 400px 300px; width: 200px; height: 100px; margin: auto }
              @page small { width: 200px; height: 100px; margin: 10px }
            </style>
            <div style='height: 10px'></div><div style='height: 10px; page: small'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal(new PhysicalSize(400, 300), pages[0].Geometry.PageSize);
        Assert.Equal(new PhysicalRect(100, 100, 200, 100), pages[0].Geometry.PageArea);
        Assert.Equal(new PhysicalSize(220, 120), pages[1].Geometry.PageSize);
        Assert.Equal(new PhysicalRect(10, 10, 200, 100), pages[1].Geometry.PageArea);
    }

    [Fact]
    public void Pages_PageContextCounters()
    {
        // The page context inherits the counters of the root element; a counter reset by a page is a counter of that page only
        var document = Parse("""
            <style>
              html { counter-reset: foo 10 }
              @page { counter-increment: foo; @top-center { content: counter(foo) "/" counter(page) } }
              @page :first { counter-reset: foo 1 }
            </style>
            <div style='height: 10px'></div><div style='height: 10px; break-before: page'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(["2/1", "11/2"], pages.Select(page => Assert.IsType<TextRun>(Assert.Single(page.MarginBoxes!).Box.FirstChild).Data));
    }

    [Fact]
    public void Pages_PageOrientationRotatesThePrintedPage()
    {
        var document = Parse("<style>@page { size: 400px 300px; page-orientation: rotate-right }</style><div style='height: 10px'></div>");
        using var session = document.BeginPrint(Options);
        var page = Assert.Single(session.Paint());
        Assert.Equal(new PhysicalSize(300, 400), page.Size);
    }

    [Fact]
    public void Pages_ContentOverflowingMonolithicBoxAddsPages()
    {
        // The content that overflows a monolithic box continues in new pages
        var document = Parse("<div id=a style='contain: size; height: 100px'><div style='height: 500px'></div></div>");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 300, 100)), (1, new PhysicalRect(0, -200, 300, 100)), (2, new PhysicalRect(0, -400, 300, 100))], GetPageRects(pages, document.GetElementById("a")!));
    }

    [Fact]
    public void Pages_ContentOfPagesOfAnotherInlineSizeIsLaidOutInThem()
    {
        // The content of the named page is laid out in its page area (500px wide): its percentages and automatic widths resolve
        // against it; the content after it, in the default page area again
        var document = Parse("""
            <style>@page { size: 400px 300px; margin: 0 } @page wide { size: 500px 300px }</style>
            <div id=a style='width: 50%; height: 10px'></div>
            <div id=b style='page: wide; width: 50%; height: 10px'></div>
            <div id=c style='height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 200, 10))], GetPageRects(pages, document.GetElementById("a")!));
        Assert.Equal([(1, new PhysicalRect(0, 0, 250, 10))], GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal([(2, new PhysicalRect(0, 0, 400, 10))], GetPageRects(pages, document.GetElementById("c")!));
    }

    [Theory]
    [InlineData("<table id=c style='width: 100%; border-spacing: 0'><tr id=r0 style='height: 100px'><td></td></tr><tr id=r1 style='height: 100px'><td></td></tr><tr id=r2 style='height: 100px'><td></td></tr><tr id=r3 style='height: 100px'><td></td></tr><tr id=r4 style='height: 100px'><td></td></tr></table>")]
    [InlineData("<div id=c style='display: flex; flex-direction: column'><div id=r0 style='height: 100px'></div><div id=r1 style='height: 100px'></div><div id=r2 style='height: 100px'></div><div id=r3 style='height: 100px'></div><div id=r4 style='height: 100px'></div></div>")]
    [InlineData("<div id=c style='display: grid; grid-auto-rows: 100px'><div id=r0></div><div id=r1></div><div id=r2></div><div id=r3></div><div id=r4></div></div>")]
    public void Pages_ContainerContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string html)
    {
        // The first page is 200px wide, the next ones 400px wide: the rows (table rows, flex items, grid items) after the break
        // are laid out again in the page area of the second page, and none is repeated or skipped
        var document = Parse($"<style>@page {{ size: 400px 300px; margin: 0 }} @page :first {{ size: 200px 300px }}</style>{html}");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal([(0, new PhysicalRect(0, i * 100, 200, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        for (var i = 3; i < 5; i++)
        {
            Assert.Equal([(1, new PhysicalRect(0, (i - 3) * 100, 400, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }
    }

    [Fact]
    public void Pages_TableWithRepeatedHeaderContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt()
    {
        // The header is repeated on the second page, in the table laid out again in its page area (the rows are not broken: the
        // break is between two rows)
        var document = Parse("""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } tr { break-inside: avoid }</style>
            <table style='width: 100%; border-spacing: 0'>
              <thead><tr id=h style='height: 20px'><td></td></tr></thead>
              <tbody><tr id=r0 style='height: 100px'><td></td></tr><tr id=r1 style='height: 100px'><td></td></tr><tr id=r2 style='height: 100px'><td></td></tr><tr id=r3 style='height: 100px'><td></td></tr></tbody>
            </table>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 200, 20)), (1, new PhysicalRect(0, 0, 400, 20))], GetPageRects(pages, document.GetElementById("h")!));
        Assert.Equal([(0, new PhysicalRect(0, 120, 200, 100))], GetPageRects(pages, document.GetElementById("r1")!));
        Assert.Equal([(1, new PhysicalRect(0, 20, 400, 100))], GetPageRects(pages, document.GetElementById("r2")!));
        Assert.Equal([(1, new PhysicalRect(0, 120, 400, 100))], GetPageRects(pages, document.GetElementById("r3")!));
    }

    [Fact]
    public void Pages_TableWithOversizedHeaderContinuedOnAPageOfAnotherInlineSizeDoesNotRepeatIt()
    {
        // The header avoids breaks (it can be repeated) but is taller than a quarter of the page where the table starts: it is not
        // repeated, also in the pages laid out again in another inline size
        var document = Parse("""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } tr { break-inside: avoid }</style>
            <table style='width: 100%; border-spacing: 0'>
              <thead style='break-inside: avoid'><tr id=h style='height: 100px'><td></td></tr></thead>
              <tbody><tr id=r0 style='height: 100px'><td></td></tr><tr id=r1 style='height: 100px'><td></td></tr><tr id=r2 style='height: 100px'><td></td></tr><tr id=r3 style='height: 100px'><td></td></tr><tr id=r4 style='height: 100px'><td></td></tr><tr id=r5 style='height: 100px'><td></td></tr><tr id=r6 style='height: 100px'><td></td></tr><tr id=r7 style='height: 100px'><td></td></tr></tbody>
            </table>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 200, 100))], GetPageRects(pages, document.GetElementById("h")!));
        Assert.Equal([(0, new PhysicalRect(0, 200, 200, 100))], GetPageRects(pages, document.GetElementById("r1")!));
        Assert.Equal([(1, new PhysicalRect(0, 0, 400, 100))], GetPageRects(pages, document.GetElementById("r2")!));
        Assert.Equal([(1, new PhysicalRect(0, 200, 400, 100))], GetPageRects(pages, document.GetElementById("r4")!));
        Assert.Equal([(2, new PhysicalRect(0, 0, 400, 100))], GetPageRects(pages, document.GetElementById("r5")!));
        Assert.Equal([(2, new PhysicalRect(0, 200, 400, 100))], GetPageRects(pages, document.GetElementById("r7")!));
    }

    [Fact]
    public void Pages_FlexItemContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt()
    {
        // The break is inside the only item of a column flex container: its line boxes after the break are laid out again in
        // the page area of the second page (2 words of 90px per line on the first page, 4 on the second one), without repeating
        // or skipping a word
        var words = string.Join(' ', Enumerable.Repeat("XXXXXXXXX", 100));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: flex; flex-direction: column'><div id=before style='height: 100px'></div><p id=p style='margin: 0'>{{words}}</p></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 100, 200, 200)), (1, new PhysicalRect(0, 0, 400, 150))], GetPageRects(pages, document.GetElementById("p")!));

        Assert.Equal(40, CountWords(pages[0].Content));
        Assert.Equal(60, CountWords(pages[1].Content));
    }

    [Theory]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr'><div style='break-inside: avoid'>{0}</div><div>{1}</div><div>{2}</div><div style='height: 42px'></div></div>", 36, 3, 37)] // the first item continues after the end of its row
    [InlineData("<div style='display: flex; flex-direction: column'><div>{0}</div><div>{1}</div><div style='break-inside: avoid'>{2}</div></div>", 19, 34, 7)] // the break of the second item is before the break of the page
    [InlineData("<table style='width: 100%; border-spacing: 0'><thead><tr style='height: 15px'><td></td></tr></thead><tbody><tr><td>{0}</td></tr><tr style='height: 56px'><td></td></tr><tr><td>{1}</td></tr><tr style='height: 171px'><td></td></tr><tr><td>{2}</td></tr></tbody></table>", 20, 26, 5)] // the cell of the third row continues after the end of its row
    public void Pages_RowsContinuedAfterTheBreakAreNotLaidOutAgain(string format, int words0, int words1, int words2)
    {
        // The content of a row continues in the page after the break (a parallel flow breaks apart from the flow of its
        // container): the page is laid out in the inline size of the strip, until a page that starts with the content after the
        // break. No word is lost or repeated.
        var html = string.Format(CultureInfo.InvariantCulture, format, Words(words0), Words(words1), Words(words2));
        var document = Parse($"<style>@page {{ size: 400px 75px; margin: 0 }} @page :first {{ size: 115px 75px }}</style>{html}");
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(words0 + words1 + words2, pages.Sum(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    /// <summary>Returns the number of words of the text fragments of a fragment and of its descendants.</summary>
    private static int CountWords(Fragment fragment)
    {
        var count = fragment is TextFragment text ? text.Run.Data[text.Start..text.End].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length : 0;
        foreach (var child in fragment.ChildrenSpan)
        {
            count += CountWords(child);
        }

        return count;
    }

    [Fact]
    public void Pages_GridWhoseColumnsDependOnItsInlineSizeKeepsItsLayout()
    {
        // The number of columns of the grid depends on its inline size: its items would not be in the same rows in the page area
        // of the second page, so the grid keeps the layout of the first page
        var items = string.Concat(Enumerable.Range(0, 8).Select(i => $"<div id=i{i}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: repeat(auto-fill, 100px); grid-auto-rows: 100px'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(100, 200, 100, 100))], GetPageRects(pages, document.GetElementById("i5")!));
        Assert.Equal([(1, new PhysicalRect(0, 0, 100, 100))], GetPageRects(pages, document.GetElementById("i6")!));
        Assert.Equal([(1, new PhysicalRect(100, 0, 100, 100))], GetPageRects(pages, document.GetElementById("i7")!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("flex-direction: row-reverse")]
    [InlineData("justify-content: center")]
    public void Pages_WrappingFlexContainerContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string style)
    {
        // The first page is 200px wide (2 items per flex line), the next ones 400px wide: the items after the break between two
        // flex lines start a new flex line in the page area of the second page, where 4 items fit per line; none is repeated or
        // skipped
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=i{i}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { flex: 0 0 100px; height: 100px }</style>
            <div id=c style='display: flex; flex-wrap: wrap; {{style}}'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(0, Assert.Single(GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!)).Page);
        }

        var rects = Enumerable.Range(6, 4).Select(i => Assert.Single(GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!))).ToList();
        Assert.All(rects, rect => Assert.Equal((1, 0d, 100d, 100d), (rect.Page, rect.Rect.Y, rect.Rect.Width, rect.Rect.Height)));
        Assert.Equal([0d, 100d, 200d, 300d], rects.Select(rect => rect.Rect.X).Order());
    }

    [Fact]
    public void Pages_WrappingFlexContainerBrokenInsideALineKeepsItsLayout()
    {
        // The break is inside a flex line (its items are side by side): the items after it would not be in the same lines in the
        // page area of the second page, so the flex container keeps the layout of the first page
        var items = string.Concat(Enumerable.Range(0, 4).Select(i => $"<div id=i{i}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { flex: 0 0 100px; height: 200px }</style>
            <div id=c style='display: flex; flex-wrap: wrap'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 200, 100, 100)), (1, new PhysicalRect(0, 0, 100, 100))], GetPageRects(pages, document.GetElementById("i2")!));
    }

    [Fact]
    public void Pages_ParagraphContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt()
    {
        // The first page is 200px wide (2 words of 90px per line: 29 lines after the border and the padding), the next ones 400px
        // wide: the line boxes of the paragraph after the break start again from the first word that is not on the first page,
        // in lines of 4 words (the 42 words left make 11 lines; the fragment has no block-start edges)
        var words = string.Join(' ', Enumerable.Repeat("XXXXXXXXX", 100));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <p id=p style='margin: 0; border-top: 5px solid; padding-top: 5px'>{{words}}</p>
            <div id=after style='height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 200, 300)), (1, new PhysicalRect(0, 0, 400, 110))], GetPageRects(pages, document.GetElementById("p")!));
        Assert.Equal([(1, new PhysicalRect(0, 110, 400, 10))], GetPageRects(pages, document.GetElementById("after")!));

        // (no word is repeated or skipped at the break: the text fragments of the pages follow each other in the text)
        var ranges = new List<(int Page, int Start, int End)>();
        foreach (var page in pages)
        {
            CollectText(page.Content, page.Index);
        }

        Assert.Equal(58, ranges.Count(range => range.Page == 0));
        Assert.Equal(42, ranges.Count(range => range.Page == 1));
        for (var i = 0; i < ranges.Count; i++)
        {
            Assert.Equal(i * 10, ranges[i].Start);
            Assert.Equal((i * 10) + 9, ranges[i].End);
        }

        void CollectText(Fragment fragment, int page)
        {
            if (fragment is TextFragment text)
            {
                // (one fragment per word: the spaces of the lines are collapsed or hang at their end)
                foreach (var (start, end) in SplitWords(text))
                {
                    ranges.Add((page, start, end));
                }
            }

            foreach (var child in fragment.ChildrenSpan)
            {
                CollectText(child, page);
            }
        }

        static IEnumerable<(int Start, int End)> SplitWords(TextFragment text)
        {
            var data = text.Run.Data;
            for (var start = text.Start; start < text.End;)
            {
                while (start < text.End && data[start] == ' ')
                {
                    start++;
                }

                var end = start;
                while (end < text.End && data[end] != ' ')
                {
                    end++;
                }

                if (end > start)
                {
                    yield return (start, end);
                }

                start = end;
            }
        }
    }

    [Fact]
    public void Pages_TableHeaderAndFooterAreRepeated()
    {
        // https://drafts.csswg.org/css-tables-3/#repeated-headers: in the pages, the header and the footer groups are repeated
        // whatever their break-inside (as in Gecko) when they are not taller than a quarter of the page area
        var document = Parse("""
            <table style='border-spacing: 0'>
              <thead><tr><td style='padding: 0'><div style='width: 100px; height: 20px'></div></td></tr></thead>
              <tfoot><tr><td style='padding: 0'><div style='height: 20px'></div></td></tr></tfoot>
              <tbody><tr><td style='padding: 0'><div id=cell style='height: 320px'></div></td></tr></tbody>
            </table>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 20, 100, 160)), (1, new PhysicalRect(0, 20, 100, 160))], GetPageRects(pages, document.GetElementById("cell")!));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(15)]
    public void Pages_AvoidBreakRowAfterTallHeaderIsNotSliced(int headerHeight)
    {
        // A header that is not repeated (taller than a quarter of the page) and leaves 5px at the bottom of the page is separated
        // from the row after it: the row, which avoids breaks and does not fit, is pushed whole to the next page, instead of placing
        // a slice of it there that the next page repeats. A repeated header is not separated from the row: the row is broken.
        // Either way, each word is printed once
        var words = string.Join(' ', Enumerable.Repeat("XXXX", 28));
        var document = Parse($$"""
            <table style='width: 100%; border-spacing: 0'>
              <thead style='break-inside: avoid'><tr style='height: {{headerHeight}}px'><td></td></tr></thead>
              <tbody><tr id=r0 style='break-inside: avoid'><td>{{words}}</td></tr><tr id=r1><td>XXXX XXXX</td></tr></tbody>
            </table>
            """);
        using var session = document.BeginPrint(new PrintOptions { PageWidth = 300, PageHeight = 65, MarginTop = 0, MarginRight = 0, MarginBottom = 0, MarginLeft = 0 });
        var pages = session.GetPages();
        if (headerHeight * 4 > 65)
        {
            Assert.Equal([(1, new PhysicalRect(0, 0, 300, 52))], GetPageRects(pages, document.GetElementById("r0")!));
        }

        var count = 0;
        foreach (var page in pages)
        {
            Count(page.Content);
        }

        Assert.Equal(30, count);

        void Count(Fragment fragment)
        {
            if (fragment is TextFragment text)
            {
                count += text.Run.Data[text.Start..text.End].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }

            foreach (var child in fragment.ChildrenSpan)
            {
                Count(child);
            }
        }
    }
}
