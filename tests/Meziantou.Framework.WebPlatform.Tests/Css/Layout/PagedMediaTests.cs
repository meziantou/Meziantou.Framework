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
    [InlineData("", 0)]
    [InlineData("border-top: 10px solid; padding-top: 5px; padding-bottom: 7px", 15)]
    public void Pages_MultiColumnContainerContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string style, double edges)
    {
        // The first page is 200px wide, the next one 400px wide: the columns of the container in the second page are 200px wide,
        // and continue with the content after the first page (balanced), without repeating or skipping a box
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px {{300 + edges}}px }</style>
            <div id=m style='columns: 2; column-gap: 0; {{style}}'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal([(0, new PhysicalRect(i / 3 * 100, edges + (i % 3 * 100), 100, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        for (var i = 6; i < 10; i++)
        {
            Assert.Equal([(1, new PhysicalRect((i - 6) / 2 * 200, (i - 6) % 2 * 100, 200, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        Assert.Equal(400, GetPageRects(pages, document.GetElementById("m")!)[1].Rect.Width);
    }

    [Theory]
    [InlineData(0, 10, 4, 0)] // the break is in the row of columns after the spanner
    [InlineData(4, 6, 0, 0)] // the break is after the spanner
    [InlineData(6, 4, 0, 50)] // the break is before the spanner
    public void Pages_MultiColumnContainerWithAColumnSpannerContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(int before, int after, int firstOnSecondPage, double secondPageTop)
    {
        // The first page is 200px wide, the next one 400px wide: the columns after the column spanner in the second page are
        // 200px wide, and continue with the content after the first page (balanced)
        static string Items(string prefix, int count) => string.Concat(Enumerable.Range(0, count).Select(i => $"<div id={prefix}{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='columns: 2; column-gap: 0'>{{Items("b", before)}}<div id=s style='column-span: all; height: 50px'></div>{{Items("a", after)}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal(secondPageTop == 0 ? 0 : 1, GetPageRects(pages, document.GetElementById("s")!).Single().Page);
        var perColumn = (after - firstOnSecondPage) / 2;
        for (var i = firstOnSecondPage; i < after; i++)
        {
            var index = i - firstOnSecondPage;
            Assert.Equal([(1, new PhysicalRect(index / perColumn * 200, secondPageTop + (index % perColumn * 100), 200, 100))], GetPageRects(pages, document.GetElementById("a" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        if (secondPageTop > 0)
        {
            Assert.Equal([(1, new PhysicalRect(0, 0, 400, 50))], GetPageRects(pages, document.GetElementById("s")!));
        }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(200)]
    public void Pages_EmptyColumnsBeforeAColumnSpannerWithoutBlockSizeAreInThePageOfTheSpanner(int firstPageWidth)
    {
        // The columns between two column spanners have no content with a block size and the second spanner has no block size:
        // they end the first page with it, before the content that does not fit there (also when it continues on a page of
        // another inline size)
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: {{firstPageWidth}}px 300px }</style>
            <div style='columns: 2'><div style='column-span: all; height: 201px'></div><div id=e></div><div id=s style='column-span: all'></div><div id=a style='height: 100px; break-inside: avoid'></div><div style='height: 100px; break-inside: avoid'></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([0], GetPageRects(pages, document.GetElementById("e")!).Select(rect => rect.Page));
        Assert.Equal([0], GetPageRects(pages, document.GetElementById("s")!).Select(rect => rect.Page));
        Assert.Equal([1], GetPageRects(pages, document.GetElementById("a")!).Select(rect => rect.Page));
    }

    [Theory]
    [InlineData("<table style='width: 100%; border-spacing: 0'>", "<tr id={0} style='height: 100px'><td></td></tr>", "</table>")]
    [InlineData("<div style='display: flex; flex-direction: column'>", "<div id={0} style='height: 100px'></div>", "</div>")]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; grid-auto-rows: 100px'>", "<div id={0}></div><div></div>", "</div>")]
    public void Pages_RowsInAMultiColumnContainerContinuedOnAPageOfAnotherInlineSizeAreLaidOutInIt(string start, string row, string end)
    {
        // The break at the end of the last column of the first page is between two rows (table rows, flex items, grid rows): the
        // rows after it are laid out again in the columns of the second page (200px wide instead of 100px), balanced
        var rows = string.Concat(Enumerable.Range(0, 10).Select(i => string.Format(CultureInfo.InvariantCulture, row, "r" + i.ToString(CultureInfo.InvariantCulture))));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='columns: 2; column-gap: 0'>{{start}}{{rows}}{{end}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        var width = row.Contains("<div></div>", StringComparison.Ordinal) ? 0.5 : 1;
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal([(0, new PhysicalRect(i / 3 * 100, i % 3 * 100, 100 * width, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        for (var i = 6; i < 10; i++)
        {
            Assert.Equal([(1, new PhysicalRect((i - 6) / 2 * 200, (i - 6) % 2 * 100, 200 * width, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }
    }

    [Theory]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div>{0}</div><div>{1}</div></div>")]
    [InlineData("<table style='width: 100%; border-spacing: 0'><tr><td style='padding: 0; vertical-align: top'>{0}</td><td style='padding: 0; vertical-align: top'>{1}</td></tr></table>")]
    [InlineData("<div style='display: flex; align-items: flex-start'><div style='flex: 1 1 0'>{0}</div><div style='flex: 1 1 0'>{1}</div></div>")]
    public void Pages_RowBrokenInAMultiColumnContainerContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string format)
    {
        // The break at the end of the last column of the first page is inside a row of two items side by side (a grid row, a
        // table row, a flex line): the content of both items continues in the columns of the second page, laid out in their
        // inline size, without repeating or skipping a word
        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div id=m style='columns: 2; column-gap: 0'><div style='height: 300px'></div>{{string.Format(CultureInfo.InvariantCulture, format, Words(60), Words(45))}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal(400, GetPageRects(pages, document.GetElementById("m")!)[1].Rect.Width);
        Assert.Equal(105, pages.Sum(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData("height: 400px")]
    [InlineData("max-height: 400px")]
    public void Pages_MultiColumnContainerWithABlockSizeContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string style)
    {
        // The content box is 400px tall: 300px in the first page, the rest (100px) in the second one, whose columns are 200px wide;
        // the content that does not fit in its columns overflows them in the inline direction, in more columns
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div id=m style='columns: 2; column-gap: 0; {{style}}'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 200, 300)), (1, new PhysicalRect(0, 0, 400, 100))], GetPageRects(pages, document.GetElementById("m")!));
        for (var i = 6; i < 10; i++)
        {
            Assert.Equal([(1, new PhysicalRect((i - 6) * 200, 0, 200, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }
    }

    [Fact]
    public void Pages_MultiColumnContainerNestedInAMultiColumnContainerEndsInTheLastPage()
    {
        // The nested container fills the columns of the outer one in the first two pages: its last item is in the second page,
        // without an empty row of columns (with a sliver of the item) in more pages
        var items = string.Concat(Enumerable.Range(0, 31).Select(i => $"<div id=r{i} style='height: 50px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 }</style>
            <div style='columns: 2; column-gap: 0'><div style='height: 120px'></div><div style='columns: 2; column-gap: 0'>{{items}}</div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        for (var i = 0; i < 31; i++)
        {
            var rects = GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!);
            Assert.Equal(i < 18 ? 0 : 1, Assert.Single(rects).Page);
            Assert.Equal(new PhysicalSize(100, 50), rects[0].Rect.Size);
        }
    }

    [Fact]
    public void Pages_FlexItemPushedFromTheStartOfAColumnOfANestedMultiColumnContainerContinuesInTheNextPage()
    {
        // The paragraph of 30 lines (300px) avoids breaks and does not fit in a column of the first page (294px): it is pushed
        // from the start of the second column to the next page, whose column holds it, and the last item is in the next column.
        // The second column of the first page does not break inside the pushed paragraph (fuzz regression: the next pages laid
        // out the paragraph again from its start, one line per column, until the limit of 10,000 pages)
        var document = Parse($$"""
            <style>@page { size: 200px 300px; margin: 0 }</style>
            <div style='padding: 5px; border: 1px solid'><div style='columns: 2'><div style='display: flex; flex-direction: column'>
            <div id=a style='height: 1px; break-inside: avoid'></div><div id=b style='break-inside: avoid'>{{string.Join(' ', Enumerable.Repeat("XXXX", 30))}}</div><div id=c style='break-inside: avoid'>XXXX</div>
            </div></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal(0, Assert.Single(GetPageRects(pages, document.GetElementById("a")!)).Page);
        var b = Assert.Single(GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal((1, 300d), (b.Page, b.Rect.Height));
        var c = Assert.Single(GetPageRects(pages, document.GetElementById("c")!));
        Assert.Equal(1, c.Page);
        Assert.True(c.Rect.X > b.Rect.X, "the last item is in the next column");
    }

    [Theory]
    [InlineData("<div style='padding-top: 10px'><div style='columns: 2; column-gap: 0'>{0}</div></div>")]
    [InlineData("<div style='columns: 2; column-gap: 0'><div style='columns: 1; column-gap: 0'>{0}</div></div>")]
    [InlineData("<div style='display: flex'><div style='flex: 1; columns: 2; column-gap: 0'>{0}</div></div>")]
    public void Pages_MultiColumnContainerNestedDeeplyInAMultiColumnContainerIsNotSlicedInEmptyRows(string format)
    {
        // The rows of columns of the outer container after the end of its strip, where the nested containers continue, are as
        // tall as the page: each item is in a single fragment in the page, not in slivers of 1px in more pages, nor in a column
        // after the last one of the page
        var items = string.Concat(Enumerable.Range(0, 31).Select(i => $"<div id=r{i} style='height: 50px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 }</style>
            <div style='columns: 2; column-gap: 0'><div style='height: 120px'></div>{{string.Format(CultureInfo.InvariantCulture, format, items)}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.True(pages.Count <= 3);
        for (var i = 0; i < 31; i++)
        {
            var rects = GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!);
            Assert.Equal(new PhysicalSize(100, 50), Assert.Single(rects).Rect.Size);
            Assert.True(rects[0].Rect.X + rects[0].Rect.Width <= 400, $"r{i} is in the page: {rects[0]}");
        }
    }

    [Fact]
    public void Pages_RowsThatAvoidBreaksInAMultiColumnContainerNestedInAMultiColumnContainerAreNotBroken()
    {
        // The paragraphs of the nested container need more columns than its block size in the strip of the outer container: the
        // last row of outer columns is balanced from where the nested container continues, and is tall enough for the rows
        // that avoid breaks (they are not broken in columns of 18px, nor in halves)
        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
        var document = Parse($$"""
            <style>@page { size: 285px 260px; margin: 0 }</style>
            <div style='columns: 2'><div style='height: 40px'></div><div style='columns: 2; column-gap: 0'>
            <div style='break-inside: avoid'>{{Words(31)}}</div><div>{{Words(24)}}</div><div style='height: 38px; break-inside: avoid'></div>
            <div style='break-inside: avoid'>{{Words(21)}}</div><div style='height: 165px'></div><div style='height: 63px'></div><div style='height: 31px'></div>
            <div style='height: 55px'></div><div style='height: 29px; break-inside: avoid'></div><div>{{Words(18)}}</div>
            <div id=a style='height: 73px; break-inside: avoid'></div><div id=b style='height: 163px; break-inside: avoid'></div></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(73, Assert.Single(GetPageRects(pages, document.GetElementById("a")!)).Rect.Height);
        Assert.Equal(163, Assert.Single(GetPageRects(pages, document.GetElementById("b")!)).Rect.Height);
    }

    [Fact]
    public void Pages_StretchedMultiColumnContainerNestedInAMultiColumnContainerKeepsItsBlockSize()
    {
        // The nested container is stretched to the block size of its flex line (the 1500px of its sibling): its block size is a
        // minimum, the fragments of the container follow the ones of its sibling, and its items are in the columns of the pages
        var items = string.Concat(Enumerable.Range(0, 31).Select(i => $"<div id=r{i} style='height: 50px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 }</style>
            <div style='columns: 2; column-gap: 0'><div style='height: 120px'></div><div style='display: flex'><div id=m style='flex: 1; columns: 2; column-gap: 0'>{{items}}</div><div id=s style='width: 10px; height: 1500px'></div></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal(GetPageRects(pages, document.GetElementById("s")!).Select(rect => (rect.Page, rect.Rect.Y, rect.Rect.Height)), GetPageRects(pages, document.GetElementById("m")!).Select(rect => (rect.Page, rect.Rect.Y, rect.Rect.Height)));
        for (var i = 0; i < 31; i++)
        {
            var rects = GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!);
            Assert.Equal(new PhysicalSize(95, 50), Assert.Single(rects).Rect.Size);
            Assert.True(rects[0].Rect.X + rects[0].Rect.Width <= 400, $"r{i} is in the page: {rects[0]}");
        }
    }

    [Theory]
    [InlineData("<div style='position: relative; height: 100px; break-inside: avoid'><div id=p style='position: absolute; top: 10px; left: 0; right: 0; height: 20px'></div></div>", "(1, (200, 10, 200x20))")]
    [InlineData("<div style='columns: 2; column-gap: 0; break-inside: avoid'><div id=p style='height: 200px'></div></div>", "(1, (200, 0, 100x100)) (1, (300, 0, 100x100))")]
    public void Pages_MultiColumnContainerWithPositionedOrMultiColumnContentContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string last, string expected)
    {
        // The columns of the second page (200px wide) continue after the first page; the last box of the container (a box with an
        // absolutely positioned child, a multi-column container whose columns are 100px wide) is in them, laid out in their inline
        // size; the absolutely positioned box at the start of the container is in the first page
        var items = string.Concat(Enumerable.Range(0, 8).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='columns: 2; column-gap: 0'><div style='position: relative'><div style='position: absolute; top: 0; width: 10px; height: 10px'></div></div>{{items}}{{last}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(1, new PhysicalRect(0, 0, 200, 100))], GetPageRects(pages, document.GetElementById("r6")!));
        Assert.Equal([(1, new PhysicalRect(0, 100, 200, 100))], GetPageRects(pages, document.GetElementById("r7")!));
        Assert.Equal(expected, string.Join(' ', GetPageRects(pages, document.GetElementById("p")!)));
    }

    [Theory]
    [InlineData(250, "(0, (0, 250, 20x50)) (1, (0, 0, 20x50))")]
    [InlineData(850, "(2, (0, 250, 20x50)) (3, (0, 0, 20x50))")]
    public void Pages_MultiColumnContainerWithAnAbsolutelyPositionedChildContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(int top, string expected)
    {
        // The absolutely positioned child of the container is placed in the pages from its offset in the container, as with pages
        // of the same inline size (the content box of the container is 300px tall in the first page): the columns of the second
        // page are laid out again in its inline size
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='columns: 2; column-gap: 0; position: relative'><div id=p style='position: absolute; top: {{top}}px; width: 20px; height: 100px'></div>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(expected, string.Join(' ', GetPageRects(pages, document.GetElementById("p")!)));
        Assert.Equal([(1, new PhysicalRect(0, 0, 200, 100))], GetPageRects(pages, document.GetElementById("r6")!));
    }

    [Theory]
    [InlineData("", "", 1, "(0, (0, 200, 20x20))")]
    [InlineData("", "", 7, "(1, (200, 0, 20x20))")]
    [InlineData("<div style='position: relative'>", "</div>", 4, "(0, (100, 200, 20x20))")]
    [InlineData("<div style='position: relative'>", "</div>", 9, "(1, (200, 200, 20x20))")]
    [InlineData("", "", 6, "(1, (0, 100, 20x20))", "position: relative")]
    [InlineData("", "", 4, "(0, (100, 200, 20x20))", "position: relative")]
    public void Pages_MultiColumnContainerWithAnAbsolutelyPositionedBoxAtItsStaticPositionContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string wrapperStart, string wrapperEnd, int after, string expected, string containerStyle = "")
    {
        // The absolutely positioned box (whose containing block is the initial containing block, a positioned ancestor, or the
        // container) is at its static position in the columns, after the given box: in the columns of the first page (once), or in
        // the columns of the second page, laid out again in its inline size
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>" + (i == after ? "<div id=p style='position: absolute; width: 20px; height: 20px'></div>" : "")));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            {{wrapperStart}}<div style='columns: 2; column-gap: 0; {{containerStyle}}'>{{items}}</div>{{wrapperEnd}}
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(expected, string.Join(' ', GetPageRects(pages, document.GetElementById("p")!)));
        Assert.Equal([(1, new PhysicalRect(0, 0, 200, 100))], GetPageRects(pages, document.GetElementById("r6")!));
    }

    [Theory]
    [InlineData("<div style='columns: 2; column-gap: 0; position: relative'>", "</div>")]
    [InlineData("<div style='columns: 2; column-gap: 0'>", "</div>")]
    public void Pages_MultiColumnContainerWithAnAbsolutelyPositionedBoxBrokenByThePageKeepsTheInlineSizeOfThePreviousPage(string start, string end)
    {
        // The absolutely positioned box at its static position (its containing block is the container, or the initial containing
        // block) is broken by the end of the first page: the part of it in the first page is not known in the columns laid out
        // again, so the columns of the second page keep the inline size of the first one, and the box continues there
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>" + (i == 1 ? "<div id=p style='position: absolute; width: 20px; height: 150px'></div>" : "")));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            {{start}}{{items}}{{end}}
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(0, new PhysicalRect(0, 200, 20, 100)), (1, new PhysicalRect(0, 0, 20, 50))], GetPageRects(pages, document.GetElementById("p")!));
        Assert.Equal([(1, new PhysicalRect(0, 0, 100, 100))], GetPageRects(pages, document.GetElementById("r6")!));
    }

    [Theory]
    [InlineData("column-height: 100px", 50)]
    [InlineData("column-height: 100px; column-wrap: wrap", 50)]
    [InlineData("column-height: 130px; column-wrap: wrap; row-gap: 10px", 100)]
    [InlineData("column-height: 130px; column-wrap: wrap; column-fill: auto", 50)]
    [InlineData("column-height: 400px; column-wrap: wrap", 100)]
    public void Pages_MultiColumnContainerWithAColumnHeightContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string style, int itemHeight)
    {
        // The rows of columns of a fixed block size continue in the second page (400px wide) as when the first page is 400px wide
        // too: the first page has the same boxes (their block sizes do not depend on the inline size), and a row broken by the end
        // of the first page continues with the rest of its block size
        var items = string.Concat(Enumerable.Range(0, 24).Select(i => $"<div id=r{i} style='height: {itemHeight}px; break-inside: avoid'></div>"));
        var rects = new List<string>();
        foreach (var firstWidth in new[] { 200, 400 })
        {
            var document = Parse($$"""
                <style>@page { size: 400px 300px; margin: 0 } @page :first { size: {{firstWidth}}px 300px }</style>
                <div style='columns: 2; column-gap: 0; {{style}}'>{{items}}</div>
                """);
            using var session = document.BeginPrint(Options);
            var pages = session.GetPages();
            rects.Add(string.Join(' ', Enumerable.Range(0, 24).Select(i => string.Join(' ', GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!).Where(rect => rect.Page > 0)))));
        }

        Assert.Equal(rects[1], rects[0]);
        Assert.Contains("200x", rects[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_MultiColumnContainerWithMonolithicContentTallerThanAColumnContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt()
    {
        // The monolithic box taller than a column is not sliced by the columns: it overflows its column, and the next column
        // starts after it. The column of the second page continues after the last column of the first page, 200px wide
        var items = string.Concat(Enumerable.Range(0, 4).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='columns: 2; column-gap: 0'><div id=image style='contain: size; height: 400px'></div>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 100, 400))], GetPageRects(pages, document.GetElementById("image")!));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal([(0, new PhysicalRect(100, i * 100, 100, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        Assert.Equal([(1, new PhysicalRect(0, 0, 200, 100))], GetPageRects(pages, document.GetElementById("r3")!));
    }

    [Fact]
    public void Pages_MultiColumnContainerContinuedOnPagesOfSeveralInlineSizesIsLaidOutInThem()
    {
        // The pages are 200px, 300px and 400px wide: the columns of each page continue the columns of the previous one in its
        // own inline size, and the content after the container follows its last row of columns
        var items = string.Concat(Enumerable.Range(0, 14).Select(i => $"<div id=r{i} style='height: 100px; break-inside: avoid'></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :left { size: 300px 300px } @page :first { size: 200px 300px }</style>
            <div style='height: 100px'></div>
            <div id=m style='columns: 2; column-gap: 0'>{{items}}</div>
            <div id=after style='height: 20px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal([(0, new PhysicalRect(i / 2 * 100, 100 + (i % 2 * 100), 100, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        for (var i = 4; i < 10; i++)
        {
            Assert.Equal([(1, new PhysicalRect((i - 4) / 3 * 150, (i - 4) % 3 * 100, 150, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        for (var i = 10; i < 14; i++)
        {
            Assert.Equal([(2, new PhysicalRect((i - 10) / 2 * 200, (i - 10) % 2 * 100, 200, 100))], GetPageRects(pages, document.GetElementById("r" + i.ToString(CultureInfo.InvariantCulture))!));
        }

        Assert.Equal([(2, new PhysicalRect(0, 200, 400, 20))], GetPageRects(pages, document.GetElementById("after")!));
    }

    [Fact]
    public void Pages_TextOfAMultiColumnContainerContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt()
    {
        // The break is between two line boxes of the columns: the line boxes after it are laid out again in the columns of the
        // second page (190px wide instead of 90px, balanced), without repeating or skipping a word
        var words = string.Join(' ', Enumerable.Repeat("XXXXXXXXX", 120));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div id=m style='columns: 2; column-gap: 20px'><p style='margin: 0'>{{words}}</p></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 200, 300)), (1, new PhysicalRect(0, 0, 400, 150))], GetPageRects(pages, document.GetElementById("m")!));
        Assert.Equal(60, CountWords(pages[0].Content));
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

    [Fact]
    public void Pages_CellSpanningTheBrokenRowFromARowBeforeItIsNotLaidOutAgain()
    {
        // The first cell spans the first two rows, and its lines end before the break, which crosses the second row: in the wider
        // page, its lines would end below the start of the rest of the second cell. The rest of the second cell starts after
        // them, and the first cell is not laid out again in the page. No word is lost or repeated.
        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
        var document = Parse($$"""
            <style>@page { size: 285px 220px; margin: 0 } @page :first { size: 200px 220px }</style>
            <table style='width: 100%; border-spacing: 0'>
              <tr><td rowspan=2>{{Words(39)}}</td><td></td></tr>
              <tr><td rowspan=2>{{Words(24)}}</td><td></td></tr>
              <tr><td rowspan=2>{{Words(4)}}</td><td></td></tr>
            </table>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal(60, CountWords(pages[0].Content));
        Assert.Equal(7, CountWords(pages[1].Content));
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

    [Theory]
    [InlineData("flex: 0 1 100px", 50)]
    [InlineData("flex: 0 0 100px", 100)]
    public void Pages_WrappingFlexContainerBrokenInsideALineThatNoLongerFitsKeepsTheLineInAPageOfAnotherInlineSize(string itemStyle, double secondPageItemWidth)
    {
        // The break crosses a flex line of 4 items (80 words each, 2 words of 40px per line, 60 on the first page) in the 400px
        // wide first page: the items do not fit in one line of the 200px wide page area of the second page. As the content of
        // each item continues beside the others, they stay in one line there, shrunk (or overflowing the container)
        var items = string.Concat(Enumerable.Range(0, 4).Select(i => $"<div id=i{i}>{string.Join(' ', Enumerable.Repeat("XXXX", 80))}</div>"));
        var document = Parse($$"""
            <style>@page { size: 200px 300px; margin: 0 } @page :first { size: 400px 300px } #c > div { {{itemStyle}} }</style>
            <div id=c style='display: flex; flex-wrap: wrap'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        for (var i = 0; i < 4; i++)
        {
            var rects = GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!);
            Assert.Equal((0, new PhysicalRect(i * 100, 0, 100, 300)), rects[0]);
            Assert.Equal((1, i * secondPageItemWidth, 0d, secondPageItemWidth), (rects[1].Page, rects[1].Rect.X, rects[1].Rect.Y, rects[1].Rect.Width));
        }

        Assert.Equal(240, CountWords(pages[0].Content));
        Assert.Equal(320, pages.Sum(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData("auto-fill")]
    [InlineData("auto-fit")]
    public void Pages_AutoRepeatGridContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string repeat)
    {
        // The first page is 200px wide (2 columns), the next ones 400px wide (4 columns): the items after the break between two
        // rows start a new row in the page area of the second page; none is repeated or skipped
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=i{i}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { height: 100px }</style>
            <div id=c style='display: grid; grid-template-columns: repeat({{repeat}}, 100px)'>{{items}}</div>
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
        Assert.Equal([0d, 100d, 200d, 300d], rects.Select(rect => rect.Rect.X));
    }

    [Theory]
    [InlineData("", "<div id=h style='grid-row: 1; grid-column: 1 / -1'></div>", "", 2)]
    [InlineData("grid-auto-flow: row dense", "", " style='grid-column: span 2'", 1)]
    public void Pages_AutoRepeatGridPlacedAroundTheBreakContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string gridStyle, string header, string secondItemStyle, int lastRowItems)
    {
        // The columns are repeated (2 columns of 100px in the 200px wide first page, 4 in the 400px wide second page). A header in
        // the first row (a definite row), or the dense packing of an item that spans the two columns (the third item fills the
        // hole before it), places the items before the break out of their order: the items after the break start a new row in the
        // page area of the second page, after the rows of the items before the break, without filling the holes before it
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=i{i}{(i == 1 ? secondItemStyle : "")}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { height: 100px }</style>
            <div id=c style='display: grid; grid-template-columns: repeat(auto-fill, 100px); {{gridStyle}}'>{{header}}{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        var firstPageItems = 10 - 4 - lastRowItems;
        for (var i = 0; i < firstPageItems; i++)
        {
            Assert.Equal(0, Assert.Single(GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!)).Page);
        }

        var rects = Enumerable.Range(firstPageItems, 10 - firstPageItems).Select(i => Assert.Single(GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!))).ToList();
        Assert.Equal([.. Enumerable.Range(0, 10 - firstPageItems).Select(i => (1, new PhysicalRect(i % 4 * 100, i / 4 * 100, 100, 100)))], rects);
    }

    [Fact]
    public void Pages_AutoRepeatGridWithAnItemInADefiniteColumnContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt()
    {
        // The ninth item is in the second column: it is placed by the auto-placement cursor, so the items after the break between
        // two rows start a new row in the page area of the 400px wide second page (4 columns of 100px, 2 in the 200px wide first
        // page). The ninth item, before the cursor after the eighth one, starts the next row, and the tenth one follows it
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=i{i}{(i == 8 ? " style='grid-column: 2'" : "")}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { height: 100px }</style>
            <div id=c style='display: grid; grid-template-columns: repeat(auto-fill, 100px)'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(0, Assert.Single(GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!)).Page);
        }

        var rects = Enumerable.Range(6, 4).Select(i => Assert.Single(GetPageRects(pages, document.GetElementById("i" + i.ToString(CultureInfo.InvariantCulture))!))).ToList();
        Assert.Equal([(1, new PhysicalRect(0, 0, 100, 100)), (1, new PhysicalRect(100, 0, 100, 100)), (1, new PhysicalRect(100, 100, 100, 100)), (1, new PhysicalRect(200, 100, 100, 100))], rects);
    }

    [Theory]
    [InlineData("<table style='width: 100%; border-spacing: 0; table-layout: fixed'><tr>", "<td id={0} style='padding: 0'>{1}</td>", "</tr></table>", 114)]
    [InlineData("<table style='width: 100%; border-spacing: 0; table-layout: fixed'><tr>", "<td id={0} style='padding: 0; vertical-align: bottom'>{1}</td>", "</tr></table>", 110)]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr'>", "<div id={0}>{1}</div>", "</div>", 120)]
    [InlineData("<div style='display: flex'>", "<div id={0} style='flex: 1 1 0'>{1}</div>", "</div>", 120)]
    [InlineData("<div style='display: flex; flex-wrap: wrap'>", "<div id={0} style='width: 50%'>{1}</div>", "</div>", 120)]
    public void Pages_RowContinuedOnAPageOfAnotherInlineSizeIsLaidOutInIt(string start, string item, string end, int firstPageWords)
    {
        // The first page is 200px wide (2 words of 40px per line in each item), the next ones 400px wide (4 words per line): the
        // break crosses the row, and the line boxes of each item after the break start again from the first word that is not on
        // the first page, at the same position. The first item has 80 words (60 on the first page, 5 lines of 4 words after
        // it), the second one 70 (10 words after it, in 3 lines). The cells of a table are aligned in the first page (the
        // second cell, 50px shorter than the row, starts 25px lower with vertical-align: middle, 50px lower with bottom, and its
        // own break is before its line that crosses the break), not in the second one
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            {{start}}{{string.Format(CultureInfo.InvariantCulture, item, "a", Words(80))}}{{string.Format(CultureInfo.InvariantCulture, item, "b", Words(70))}}{{end}}
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 100, 300)), (1, new PhysicalRect(0, 0, 200, 50))], GetPageRects(pages, document.GetElementById("a")!));
        Assert.Equal([(0, new PhysicalRect(100, 0, 100, 300)), (1, new PhysicalRect(200, 0, 200, 50))], GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal([firstPageWords, 150 - firstPageWords], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Fact]
    public void Pages_RowWithAnItemThatEndsBeforeTheBreakStartsAfterItsContent()
    {
        // The second item ends before the break (20 words: 10 lines in the first page), its content takes 5 lines in the 400px
        // wide page area of the second page: the content of the first item after the break starts after it
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { flex: 1 1 0 }</style>
            <div id=c style='display: flex'><div id=a>{{Words(80)}}</div><div id=b>{{Words(20)}}</div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(0, new PhysicalRect(0, 0, 100, 300)), (1, new PhysicalRect(0, 0, 200, 50))], GetPageRects(pages, document.GetElementById("a")!));
        Assert.Equal([80, 20], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Fact]
    public void Pages_RowBrokenBetweenTheBoxesOfItsItemsIsLaidOutInAPageOfAnotherInlineSize()
    {
        // The break is before a box in each item: a2 stays 300px lower than the start of the row, b2 moves up to 150px in the
        // 400px wide page area of the second page (the paragraph before it takes 15 lines instead of 30): b2 is moved down to
        // a2, and both start the second page
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr'>
              <div><div style='height: 300px'></div><div id=a2 style='height: 50px'></div></div>
              <div><p style='margin: 0'>{{string.Join(' ', Enumerable.Repeat("XXXX", 60))}}</p><div id=b2 style='height: 50px'></div></div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal([(1, new PhysicalRect(0, 0, 200, 50))], GetPageRects(pages, document.GetElementById("a2")!));
        Assert.Equal([(1, new PhysicalRect(200, 0, 200, 50))], GetPageRects(pages, document.GetElementById("b2")!));
        Assert.Equal([60, 0], pages.Select(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData("display: flex", "center", 110, 30)]
    [InlineData("display: flex", "end", 100, 50)]
    [InlineData("display: flex; flex-wrap: wrap", "flex-end", 100, 50)]
    [InlineData("display: grid; grid-template-columns: 1fr 1fr", "center", 110, 30)]
    [InlineData("display: grid; grid-template-columns: 1fr 1fr", "end", 100, 50)]
    public void Pages_RowWithAnItemAlignedInItIsLaidOutInAPageOfAnotherInlineSize(string display, string alignment, int firstPageWords, double secondItemHeight)
    {
        // The second item (60 words, 30 lines in the 200px wide first page) is aligned in the row of 40 lines: it starts 5 or 10
        // lines lower, and the break crosses it. In the 400px wide page area of the second page, its content after the break
        // continues at the start of the page, as the one of the first item: as the cells of a table, the items of such a row are
        // not aligned in the next page
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { flex: 1 1 0; min-width: 0 }</style>
            <div id=c style='{{display}}'><div id=a>{{Words(80)}}</div><div id=b style='align-self: {{alignment}}'>{{Words(60)}}</div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal((1, new PhysicalRect(0, 0, 200, 50)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal((1, new PhysicalRect(200, 0, 200, secondItemHeight)), GetPageRects(pages, document.GetElementById("b")!)[^1]);
        Assert.Equal([firstPageWords, 140 - firstPageWords], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Theory]
    [InlineData("display: grid; grid-template-columns: 1fr 1fr; align-items: start", "")]
    [InlineData("display: flex; flex-wrap: wrap; align-items: flex-start", "width: 50%")]
    public void Pages_RowWithAnItemThatEndsBeforeTheBreakIsPrintedOnce(string display, string itemStyle)
    {
        // The second item is not stretched and ends before the break (10 words, 5 lines): it is an item of the row that the break
        // crosses, whose content is in the first page, not a row before the break. In the 400px wide page area of the second
        // page, the content of the first item after the break starts after the end of the second item
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { {{itemStyle}} }</style>
            <div id=c style='{{display}}'><div id=a>{{string.Join(' ', Enumerable.Repeat("XXXX", 80))}}</div><div id=b>{{string.Join(' ', Enumerable.Repeat("XXXX", 10))}}</div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal((1, 200d), GetPageRects(pages, document.GetElementById("a")!) is [_, var (page, rect)] ? (page, rect.Width) : default);
        Assert.Equal([(0, new PhysicalRect(100, 0, 100, 50))], GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal([70, 20], pages.Select(page => CountWords(page.Content)));
    }

    [Fact]
    public void Pages_RowWithAnAlignedItemPushedToTheNextPageKeepsItsLayout()
    {
        // The second item is centered in the row and avoids breaks: it does not fit in the rest of the first page, and is pushed to
        // the second one, so the row keeps the layout of the first page
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { flex: 1 1 0; min-width: 0 }</style>
            <div id=c style='display: flex'><div id=a>{{string.Join(' ', Enumerable.Repeat("XXXX", 80))}}</div><div id=b style='align-self: center; height: 250px; break-inside: avoid'></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, 100d), GetPageRects(pages, document.GetElementById("a")!) is [_, var (page, rect)] ? (page, rect.Width) : default);
        Assert.Equal([(1, new PhysicalRect(100, 0, 100, 250))], GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal(80, pages.Sum(page => CountWords(page.Content)));
    }

    [Fact]
    public void Pages_GridItemAlignedAfterThePageInTheNextRowItSpansDoesNotPushTheNextRow()
    {
        // The first item spans the two rows and is aligned at their end, after the first page: it is placed in the second page,
        // while the item of the second row, which starts in the first page, starts there (each word is on its own line)
        var document = Parse($$"""
            <style>@page { size: 145px 155px; margin: 0 }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr'>
              <div id=a style='align-self: end; grid-area: 1 / 1 / span 2'>{{Words(13)}}</div>
              <div id=b style='grid-area: 1 / 2'>{{Words(3)}}</div>
              <div id=c style='grid-area: 2 / 2'>{{Words(39)}}</div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(3, pages.Count);
        Assert.Equal((0, new PhysicalRect(72.5, 30, 72.5, 125)), GetPageRects(pages, document.GetElementById("c")!)[0]);
        Assert.Equal((1, new PhysicalRect(0, 135, 72.5, 20)), GetPageRects(pages, document.GetElementById("a")!)[0]);
        Assert.Equal([15, 17, 23], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Fact]
    public void Pages_GridRowWithAnItemThatSpansTheNextRowIsLaidOutInAPageOfAnotherInlineSize()
    {
        // The second item spans the two rows (100 words, 50 lines in the 200px wide first page), the break crosses the first row
        // (40 lines): in the 400px wide page area of the second page, the content after the break of both items starts at the
        // start of the page (5 and 10 lines), and the item of the second row follows the first row
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>
              <div id=a>{{Words(80)}}</div><div id=b style='grid-row: span 2'>{{Words(100)}}</div><div id=c>{{Words(4)}}</div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal((1, new PhysicalRect(0, 0, 200, 50)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal((1, new PhysicalRect(200, 0, 200, 100)), GetPageRects(pages, document.GetElementById("b")!)[^1]);
        var c = Assert.Single(GetPageRects(pages, document.GetElementById("c")!));
        Assert.Equal((1, 0d, 200d, 10d), (c.Page, c.Rect.X, c.Rect.Width, c.Rect.Height));
        Assert.True(c.Rect.Y >= 50);
        Assert.Equal([120, 64], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Fact]
    public void Pages_AutoRepeatGridRowWithAnItemThatSpansTheNextRowIsLaidOutInAPageOfAnotherInlineSize()
    {
        // The columns are repeated (2 in the 200px wide first page, 4 in the 400px wide page area of the second page): the second
        // item spans the two rows (100 words, 50 lines), the break crosses the first row (40 lines). In the second page, the
        // content after the break of both items starts at the start of the page (10 and 20 lines in the 100px wide columns), and
        // the items after the row start the row after it, beside the second item (in the first and the third columns)
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: repeat(auto-fill, 100px); align-items: start'>
              <div id=a>{{Words(80)}}</div><div id=b style='grid-row: span 2'>{{Words(100)}}</div><div id=c>{{Words(4)}}</div><div id=d>{{Words(4)}}</div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal((1, new PhysicalRect(0, 0, 100, 100)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal((1, new PhysicalRect(100, 0, 100, 200)), GetPageRects(pages, document.GetElementById("b")!)[^1]);
        var c = Assert.Single(GetPageRects(pages, document.GetElementById("c")!));
        var d = Assert.Single(GetPageRects(pages, document.GetElementById("d")!));
        Assert.Equal((1, 0d, 100d, 20d), (c.Page, c.Rect.X, c.Rect.Width, c.Rect.Height));
        Assert.Equal((1, 200d, c.Rect.Y), (d.Page, d.Rect.X, d.Rect.Y));
        Assert.True(c.Rect.Y >= 100);
        Assert.Equal([120, 68], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Theory]
    [InlineData("<div style='display: grid'><div id=b style='display: grid'><div>{0}</div></div></div>")]
    [InlineData("<div style='display: grid'><div id=b style='display: flex'><div>{0}</div></div></div>")]
    [InlineData("<div style='display: flex'><div id=b style='display: grid'><div>{0}</div></div></div>")]
    [InlineData("<div style='display: flex; flex-direction: column'><div id=b style='display: flex; flex-direction: column'><div>{0}</div></div></div>")]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div>XXXX</div><div>XXXX</div><div id=b style='grid-row: span 2; display: grid; grid-template-rows: subgrid'><div>{0}</div></div><div></div></div>")]
    public void Pages_ItemThatIsAFlexOrGridContainerGrowsWithItsContent(string html)
    {
        // The item is a flex or grid container (a subgrid of the rows of its grid): the lines of its item that the breaks push
        // make it grow, and its container with it, so that the paragraph after the container follows it
        var markup = string.Format(CultureInfo.InvariantCulture, html, string.Join(' ', Enumerable.Repeat("XXXX", 21))) + "<p id=p style='margin: 0'>XXXX</p>";
        var document = Parse("<style>@page { size: 100px 25px; margin: 0 }</style>" + markup);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();

        var b = GetPageRects(pages, document.GetElementById("b")!)[^1];
        var p = GetPageRects(pages, document.GetElementById("p")!)[0];
        Assert.True(p.Page > b.Page || (p.Page == b.Page && p.Rect.Y >= b.Rect.Bottom - 0.01), $"{p} is before the end of {b}");

        // (each word once, in the page where its line starts: a line that starts at the end of a page is sliced by the break)
        Assert.Equal(markup.Split("XXXX").Length - 1, pages.Sum(page => CountWordsStartingInPage(page.Content, 0, 25)));

        static int CountWordsStartingInPage(Fragment fragment, double y, double pageHeight)
        {
            y += fragment.Offset.Y;
            var count = fragment is TextFragment text && y >= -0.01 && y < pageHeight - 0.01 ? text.Run.Data[text.Start..text.End].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length : 0;
            foreach (var child in fragment.ChildrenSpan)
            {
                count += CountWordsStartingInPage(child, y, pageHeight);
            }

            return count;
        }
    }

    [Fact]
    public void Pages_EmptyFlexContainerAtABreakIsBeforeTheBreak()
    {
        // The flex container without block size is at the position of the break before the paragraph pushed by its orphans and
        // widows: it is before the break with its items, as the paragraph without block size after it
        var document = Parse("""
            <style>@page { size: 400px 30px; margin: 0 }</style>
            <p style='margin: 0'>XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX</p>
            <div id=c style='display: flex; align-items: flex-start'><div id=a style='flex: 1 1 0; min-width: 0'></div><div id=b style='flex: none; contain: size; width: 0; height: 0'></div></div>
            <p id=e style='margin: 0'></p>
            <p id=p style='margin: 0'>XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX</p>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();

        Assert.Equal((0, new PhysicalRect(0, 20, 400, 0)), Assert.Single(GetPageRects(pages, document.GetElementById("c")!)));
        Assert.Equal((0, new PhysicalRect(0, 20, 400, 0)), Assert.Single(GetPageRects(pages, document.GetElementById("a")!)));
        Assert.Equal((0, new PhysicalRect(400, 20, 0, 0)), Assert.Single(GetPageRects(pages, document.GetElementById("b")!)));
        Assert.Equal((0, new PhysicalRect(0, 20, 400, 0)), Assert.Single(GetPageRects(pages, document.GetElementById("e")!)));
        Assert.Equal((1, new PhysicalRect(0, 0, 400, 20)), Assert.Single(GetPageRects(pages, document.GetElementById("p")!)));
    }

    [Fact]
    public void Pages_ParagraphAfterASlicedLineBoxThatKeepsItsLayoutFollowsItsRest()
    {
        // The inline-block taller than the page is sliced by the break; in the 400px wide page area of the second page, the line
        // would hold words beside it: its rest keeps the layout of the first page (15px), and the lines of the paragraph after it
        // in the line, which start the strip of the new inline size, follow it. Its two lines do not fit in the rest of the page
        // (orphans and widows): they are pushed to the third page, with the empty paragraph after them
        var document = Parse("""
            <style>@page { size: 400px 30px; margin: 0 } @page :first { size: 200px 30px }</style>
            <p id=p style='margin: 0'><span style='display: inline-block; width: 250px; height: 45px; vertical-align: top'></span>XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX XXXX</p>
            <p id=e style='margin: 0'></p>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();

        Assert.Equal(3, pages.Count);
        Assert.Equal([0, 0, 12], pages.Select(page => CountWords(page.Content)));
        Assert.Equal((2, new PhysicalRect(0, 20, 400, 0)), Assert.Single(GetPageRects(pages, document.GetElementById("e")!)));
    }

    [Fact]
    public void Pages_GridRowWithASubgridIsLaidOutInAPageOfAnotherInlineSize()
    {
        // The second item of the grid is a subgrid of its two rows: its items are in the rows of the grid. The break crosses the
        // first row (40 lines of the first item, 35 of the first item of the subgrid in the 100px wide columns of the 200px wide
        // first page): in the 400px wide page area of the second page, the content after the break of both items starts at the
        // start of the page (5 and 3 lines), and the item of the second row of the subgrid follows the first row
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>
              <div id=a>{{Words(80)}}</div>
              <div style='grid-row: span 2; display: grid; grid-template-rows: subgrid; align-items: start'><div id=b>{{Words(70)}}</div><div id=c>{{Words(4)}}</div></div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        Assert.Equal((1, new PhysicalRect(0, 0, 200, 50)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal((1, new PhysicalRect(200, 0, 200, 30)), GetPageRects(pages, document.GetElementById("b")!)[^1]);
        var c = Assert.Single(GetPageRects(pages, document.GetElementById("c")!));
        Assert.Equal((1, 200d, 200d, 10d), (c.Page, c.Rect.X, c.Rect.Width, c.Rect.Height));
        Assert.True(c.Rect.Y >= 50);
        Assert.Equal([120, 34], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Fact]
    public void Pages_StretchedItemOfASubgridContinuedOnAPageOfAnotherInlineSizeGrowsWithItsLinesPushedByTheBreaks()
    {
        // The third item of the grid is a subgrid of its last two rows, whose item (its text, 7 lines in the 40px wide first
        // column of the 25px wide first page) is stretched in its first row (70px). The pages of 25px push its third, fifth and
        // seventh lines (5px at the end of each page): as the stretched items of a grid whose rows are automatic, the item grows
        // with them, and the subgrid with it: its last line is in the fourth page (the grid keeps the layout of the first page in
        // the 200px wide next ones), before the paragraph after the grid, which is laid out in their inline size from there
        var document = Parse($$"""
            <style>@page { size: 200px 25px; margin: 0 } @page :first { size: 25px 25px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>
              <div></div><div></div>
              <div id=a style='grid-row: span 2; display: grid; grid-template-rows: subgrid'>{{Words(7)}}</div>
              <div style='height: 55px'></div>
            </div>
            <p id=b style='margin: 0'>{{Words(15)}}</p>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(22, pages.Sum(page => CountWords(page.Content)));
        Assert.Equal([25, 25, 25, 10], GetPageRects(pages, document.GetElementById("a")!).Select(rect => rect.Rect.Height));
        Assert.Equal((3, new PhysicalRect(0, 0, 40, 10)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal(3, GetPageRects(pages, document.GetElementById("b")!)[0].Page);

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Theory]
    [InlineData("", new double[] { 25, 25, 25, 10 })]
    [InlineData("grid-template-rows: 0 70px 0", new double[] { 25, 25, 20, 0 })]
    public void Pages_StretchedItemOfASubgridGrowsWithItsContentsPushedByTheBreaksUnlessTheRowsOfItsGridAreFixed(string rows, double[] blockSizes)
    {
        // The rows of a subgrid are sized by its grid: as the stretched items of a grid, an item stretched in the rows of the
        // subgrid (its text) grows with its lines pushed by the breaks when they are automatic, and the subgrid with it, not when
        // they are fixed (as in Blink, its last line overflows it, in the fourth page)
        var document = Parse($$"""
            <style>@page { size: 25px 25px; margin: 0 }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start; {{rows}}'>
              <div></div><div></div>
              <div id=a style='grid-row: span 2; display: grid; grid-template-rows: subgrid'>{{Words(7)}}</div>
              <div style='height: 55px'></div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(blockSizes, GetPageRects(pages, document.GetElementById("a")!).Select(rect => rect.Rect.Height));
        Assert.Equal(7, pages.Sum(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Theory]
    [InlineData("<table style='width: 100%; border-spacing: 0'><tr style='height: 100px'><td></td></tr><tr style='height: 100px'><td></td></tr><tr style='height: 100px'><td></td></tr><tr id=r style='height: 100px'><td></td></tr></table>")]
    [InlineData("<table style='width: 100%; border-spacing: 0'><tbody><tr style='height: 300px'><td></td></tr></tbody><tbody id=r><tr style='height: 100px'><td></td></tr></tbody></table>")]
    [InlineData("<div style='display: flex; flex-direction: column'><div style='height: 100px'></div><div style='height: 100px'></div><div style='height: 100px'></div><div id=r style='height: 100px'></div></div>")]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; grid-auto-rows: 100px'><div></div><div></div><div></div><div></div><div></div><div></div><div id=r></div><div id=m></div></div>")]
    [InlineData("<div style='display: grid; grid-template-columns: subgrid; grid-auto-rows: 100px'><div></div><div></div><div></div><div id=r></div></div>")]
    public void Pages_GridRowWithAnItemBrokenBetweenItsRowsIsLaidOutInAPageOfAnotherInlineSize(string rows)
    {
        // The break is between the rows of the table (or the items of the column flex container, the rows of a grid, of a subgrid
        // of the columns) of the second item, and crosses the paragraph of the first one (160 words, 4 per line in the 200px wide
        // columns of the 400px wide first page, 120 on it). In the 100px wide columns of the 200px wide second page, the first 120
        // words take 60 lines: the rows after the break are moved down to the content after the break of the paragraph (all the
        // items of the first row of a grid), and both start at the start of the page
        var document = Parse($$"""
            <style>@page { size: 200px 300px; margin: 0 } @page :first { size: 400px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div id=a>{{string.Join(' ', Enumerable.Repeat("XXXX", 160))}}</div><div>{{rows}}</div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, new PhysicalRect(0, 0, 100, 200)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        var mate = document.GetElementById("m");
        Assert.Equal([(1, new PhysicalRect(100, 0, mate is null ? 100 : 50, 100))], GetPageRects(pages, document.GetElementById("r")!));
        if (mate is not null)
        {
            Assert.Equal([(1, new PhysicalRect(150, 0, 50, 100))], GetPageRects(pages, mate));
        }

        Assert.Equal([120, 40], pages.Select(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr'><div id=b1>{0}</div><div id=b2>{0}</div></div>")]
    [InlineData("<table style='width: 100%; border-spacing: 0; table-layout: fixed'><tr><td id=b1 style='padding: 0'>{0}</td><td id=b2 style='padding: 0'>{0}</td></tr></table>")]
    [InlineData("<div style='display: flex'><div id=b1 style='flex: 1 1 0'>{0}</div><div id=b2 style='flex: 1 1 0'>{0}</div></div>")]
    public void Pages_GridRowWithARowBrokenInAnItemIsLaidOutInAPageOfAnotherInlineSize(string nestedRow)
    {
        // The second item of the grid row has a row of its own (a grid, a table row, a flex line), which the break crosses: its
        // items are items of the row. In the 400px wide first page, the paragraph of the first item has 4 words per line (120 of
        // 160 words on it), the ones of the nested row 2 (60 of 80); in the 200px wide second page, 2 and 1: the content after
        // the break of all of them starts at the start of the page, in the new inline size
        var words = string.Join(' ', Enumerable.Repeat("XXXX", 80));
        var document = Parse($$"""
            <style>@page { size: 200px 300px; margin: 0 } @page :first { size: 400px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div id=a>{{words}} {{words}}</div><div>{{string.Format(CultureInfo.InvariantCulture, nestedRow, words)}}</div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, new PhysicalRect(0, 0, 100, 200)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal((1, new PhysicalRect(100, 0, 50, 200)), GetPageRects(pages, document.GetElementById("b1")!)[^1]);
        Assert.Equal((1, new PhysicalRect(150, 0, 50, 200)), GetPageRects(pages, document.GetElementById("b2")!)[^1]);
        Assert.Equal([240, 80], pages.Select(page => CountWords(page.Content)));
    }

    [Fact]
    public void Pages_GridRowWithAParagraphOfAColumnFlexItemMovedDownIsLaidOutInAPageOfAnotherInlineSize()
    {
        // The paragraphs of the items are items of column flex containers, after two boxes of 40px: in the 200px wide columns of
        // the 400px wide first page (4 words per line), the break crosses the third line of the first paragraph (9 words), and
        // the second one (8 words, which avoids breaks) ends at it. In the 100px wide columns of the 200px wide second page, the
        // content after the break of the first paragraph is moved down to the end of the second one: the flex item grows with
        // its line boxes (its block size measured in another layout of the strip is not used), and its last word starts the page
        var document = Parse($$"""
            <style>@page { size: 200px 100px; margin: 0 } @page :first { size: 400px 100px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>
              <div style='display: flex; flex-direction: column'><div style='height: 40px'></div><div style='height: 40px'></div><div id=a>{{Words(9)}}</div></div>
              <div style='display: flex; flex-direction: column; break-inside: avoid'><div style='height: 40px'></div><div style='height: 40px'></div><div>{{Words(8)}}</div></div>
            </div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, new PhysicalRect(0, 0, 100, 10)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal([16, 1], pages.Select(page => CountWords(page.Content)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Fact]
    public void Pages_ParagraphsOfNestedGridItemsGrowTheirGridItemsBeforeAPageOfAnotherInlineSize()
    {
        // The grid is broken inside its first row (the first item avoids breaks), so the row keeps the layout of the 400px wide
        // strip of the first page in the 300px wide pages. In the pages of 25px (two lines and 5px), the paragraph of the nested
        // grid of the second item (20 lines, 200px in the strip) grows its grid item and the row, up to the page 9. The second row
        // starts there, 5px before the end of the page (the first line of its paragraph is sliced, its rest is at the start of
        // the page 10, which counts its words again): the page after the break between the rows is laid out again in its inline
        // size (one word per line in the 75px wide columns of the nested grid), and the paragraph after the grid follows the
        // paragraph of the nested grid of the last item
        var document = Parse($$"""
            <style>@page { size: 300px 25px; margin: 0 } @page :first { size: 400px 25px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>
              <div style='height: 100px; break-inside: avoid'><div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div></div><div style='height: 50px'></div></div></div>
              <div><div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div id=c>{{Words(39)}}</div><div style='height: 50px'></div></div></div>
              <div><div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div id=a>{{Words(39)}}</div><div style='height: 50px'></div></div></div>
            </div>
            <p id=b style='margin: 0'>{{Words(2)}}</p>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(82, pages.Sum(page => CountWords(page.Content)));
        Assert.Equal((9, new PhysicalRect(200, 0, 100, 20)), GetPageRects(pages, document.GetElementById("c")!)[^1]);
        Assert.Equal((9, new PhysicalRect(0, 20, 100, 5)), GetPageRects(pages, document.GetElementById("a")!)[0]);
        Assert.Equal((27, new PhysicalRect(0, 0, 75, 10)), GetPageRects(pages, document.GetElementById("a")!)[^1]);
        Assert.Equal((27, new PhysicalRect(0, 10, 300, 10)), Assert.Single(GetPageRects(pages, document.GetElementById("b")!)));

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Theory]
    [InlineData(15, "<div style='display: grid; grid-template-columns: 1fr 1fr'><div></div><div id=i style='contain: size; width: 0; height: 0'></div></div>")]
    [InlineData(5, "<div style='display: grid'><div style='height: 10px'></div><div id=i style='height: 0'></div></div>")]
    public void Pages_GridItemsWithoutBlockSizeAtABreakAfterTheGridAreBeforeIt(int offset, string grid)
    {
        // The second page, 100px wide, starts the content laid out again in its inline size. The first line of the paragraph
        // after the grid fits in it, not its second one: the break is before the paragraph (orphans: 2), after the grid and the
        // empty paragraph that follows it. The items of the grid at the position of the break, without block size (the items of
        // a grid without block size, an item in the last row of a grid), are before it: they are in the second page with their
        // grid and the empty paragraph
        var document = Parse($$"""
            <style>@page { size: 100px 25px; margin: 0 } @page :first { size: 200px 25px }</style>
            <div style='height: 25px'></div>
            <div style='height: {{offset}}px'></div>
            {{grid}}
            <p id=e style='margin: 0'></p>
            <p id=p style='margin: 0'>XXXX XXXX XXXX</p>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(1, 15d, 0d)], GetPageRects(pages, document.GetElementById("i")!).Select(rect => (rect.Page, rect.Rect.Y, rect.Rect.Height)));
        Assert.Equal([(1, new PhysicalRect(0, 15, 100, 0))], GetPageRects(pages, document.GetElementById("e")!));
        Assert.Equal(2, GetPageRects(pages, document.GetElementById("p")!)[0].Page);
        Assert.Equal(3, pages.Sum(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    public void Pages_GridItemThatSpansTheRowBeforeTheBreakIsLaidOutInAPageOfAnotherInlineSize(int firstRowHeight)
    {
        // The paragraph of the first item spans the two rows of the grid (400px tall in the 100px wide columns of the first
        // page): the break crosses its second row (or, after a first row of 300px, is between the rows). It continues beside the
        // second row in the 400px wide page area of the second page (200px wide columns)
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div id=a style='grid-row: span 2'>{{string.Join(' ', Enumerable.Repeat("XXXX", 80))}}</div><div style='height: {{firstRowHeight}}px'></div><div id=b style='height: 100px'></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, 200d), GetPageRects(pages, document.GetElementById("a")!) is [_, var (page, rect)] ? (page, rect.Width) : default);
        Assert.Equal(80, pages.Sum(page => CountWords(page.Content)));
        Assert.Equal(200, GetPageRects(pages, document.GetElementById("b")!)[^1].Rect.Width);
    }

    [Fact]
    public void Pages_RowWithACellThatSpansTheNextRowIsLaidOutInAPageOfAnotherInlineSize()
    {
        // The second cell spans the two rows (100 words, 50 lines in the 200px wide first page), the break crosses the first row
        // (40 lines): in the 400px wide page area of the second page, the content after the break of both cells starts at the
        // start of the page (5 and 10 lines), and the second row follows the first one. The rows share the block size of the
        // second cell (25 lines), which ends with the second row
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } td { padding: 0; vertical-align: top }</style>
            <table style='width: 100%; border-spacing: 0; table-layout: fixed'>
              <tr><td id=a>{{Words(80)}}</td><td id=b rowspan=2>{{Words(100)}}</td></tr>
              <tr><td id=c>{{Words(4)}}</td></tr>
            </table>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal(2, pages.Count);
        var a = GetPageRects(pages, document.GetElementById("a")!)[^1];
        var b = GetPageRects(pages, document.GetElementById("b")!)[^1];
        var c = Assert.Single(GetPageRects(pages, document.GetElementById("c")!));
        Assert.Equal((1, 0d, 0d, 200d), (a.Page, a.Rect.X, a.Rect.Y, a.Rect.Width));
        Assert.Equal((1, 200d, 0d, 200d, 100d), (b.Page, b.Rect.X, b.Rect.Y, b.Rect.Width, Round(b.Rect.Height)));
        Assert.Equal((1, 0d, 200d, 100d), (c.Page, c.Rect.X, c.Rect.Width, Round(c.Rect.Bottom)));
        Assert.Equal(Round(a.Rect.Bottom), Round(c.Rect.Y));
        Assert.Equal([120, 64], pages.Select(page => CountWords(page.Content)));

        static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

        static string Words(int count) => string.Join(' ', Enumerable.Repeat("XXXX", count));
    }

    [Theory]
    [InlineData("display: flex", "")]
    [InlineData("display: grid; grid-template-columns: 1fr 1fr", "")]
    [InlineData("display: flex", "padding-top: 20px")]
    [InlineData("display: grid; grid-template-columns: 1fr 1fr", "padding-top: 20px")]
    public void Pages_RowWithAnItemAlignedAfterTheBreakIsLaidOutInAPageOfAnotherInlineSize(string display, string firstItemStyle)
    {
        // The second item is aligned at the end of the row, after the break (the paragraph of the first item is 400px tall in the
        // first page, 100px wide): it starts the second page whole, beside the rest of the paragraph, which continues in the
        // 400px wide page area (200px wide columns); the items are not aligned there (with a padding before the paragraph, the
        // second item is moved down to its content, see LayoutContext.AnchorPushes)
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { flex: 1 1 0; min-width: 0 }</style>
            <div id=c style='{{display}}'><div id=a style='{{firstItemStyle}}'>{{string.Join(' ', Enumerable.Repeat("XXXX", 80))}}</div><div id=b style='align-self: end; height: 100px'></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, 200d), GetPageRects(pages, document.GetElementById("a")!) is [_, var (page, rect)] ? (page, rect.Width) : default);
        Assert.Equal([(1, new PhysicalRect(200, 0, 200, 100))], GetPageRects(pages, document.GetElementById("b")!));
        Assert.Equal(80, pages.Sum(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData(400, 100)]
    [InlineData(100, 200)]
    public void Pages_RowWithAMonolithicItemIsLaidOutInAPageOfAnotherInlineSizeWhenItEndsBeforeTheBreak(int height, double secondPageWidth)
    {
        // The second item is monolithic (size containment, as an image): when the break crosses it, it is sliced, and as wide as
        // its column it is not the same in another inline size, so the row keeps the layout of the first page; when it ends
        // before the break, the paragraph of the first item continues in the 400px wide page area of the second page
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'><div id=a>{{string.Join(' ', Enumerable.Repeat("XXXX", 80))}}</div><div id=b style='contain: size; height: {{height}}px'></div></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, secondPageWidth), GetPageRects(pages, document.GetElementById("a")!) is [_, var (page, rect)] ? (page, rect.Width) : default);
        Assert.Equal(80, pages.Sum(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>", "<div id=m style='contain: size; width: 80px; height: 400px'></div>", "</div>", 200)]
    [InlineData("<div style='display: flex; align-items: flex-start'>", "<div id=m style='flex: none; contain: size; width: 80px; height: 400px'></div>", "</div>", 320)]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>", "<div style='padding-top: 20px'><div id=m style='contain: size; width: 80px; height: 400px'></div><div style='height: 20px'></div></div>", "</div>", 200)]
    [InlineData("<table style='width: 100%; border-spacing: 0'><tr>", "<td style='padding: 0; width: 80px'><div id=m style='contain: size; width: 80px; height: 400px'></div></td>", "</tr></table>", 320)]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>", "<div id=m style='contain: size; width: 80%; height: 400px'></div>", "</div>", 100)]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>", "<div style='line-height: 0'><span id=m style='display: inline-block; width: 80px; height: 400px; vertical-align: top'></span></div>", "</div>", 200)]
    [InlineData("<div style='display: flex; align-items: flex-start'>", "<div style='flex: none; width: 80px; line-height: 0'><span id=m style='display: inline-block; width: 80px; height: 400px; vertical-align: top'></span></div>", "</div>", 320)]
    [InlineData("<table style='width: 100%; border-spacing: 0'><tr>", "<td style='padding: 0; width: 80px; line-height: 0'><span id=m style='display: inline-block; width: 80px; height: 400px; vertical-align: top'></span></td>", "</tr></table>", 320)]
    [InlineData("<div style='display: grid; grid-template-columns: 1fr 1fr; align-items: start'>", "<div style='line-height: 0'><span id=m style='display: inline-block; width: 80px; height: 400px; vertical-align: top'></span> XXXX</div>", "</div>", 100)]
    public void Pages_RowWithAMonolithicItemSlicedByTheBreakIsLaidOutInAPageOfAnotherInlineSize(string start, string item, string end, double secondPageWidth)
    {
        // The monolithic content of the second item (a box with size containment, as an image, or a line box with an
        // inline-block) is taller than the 300px tall pages: the break slices it. When it has the same size and content in the
        // 400px wide page area of the second page, its rest continues there, the paragraph of the first item continues beside it
        // in that inline size, from the same position; else (80% of its column, a line that holds a word in a wider column) the
        // rest of the row keeps the layout of the first page. The content after the row is laid out in that inline size (50%: 200px)
        var tag = start.StartsWith("<table", StringComparison.Ordinal) ? "td style='padding: 0'" : "div";
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            {{start}}<{{tag}} id=a>{{string.Join(' ', Enumerable.Repeat("XXXX", 80))}}</{{tag.Split(' ')[0]}}>{{item}}{{end}}
            <div id=after style='width: 50%; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((1, secondPageWidth), GetPageRects(pages, document.GetElementById("a")!) is [_, var (page, rect), ..] ? (page, rect.Width) : default);
        Assert.Equal((1, 200d), GetPageRects(pages, document.GetElementById("after")!) is [var (afterPage, afterRect)] ? (afterPage, afterRect.Width) : default);
        Assert.Equal(item.Contains("XXXX", StringComparison.Ordinal) ? 81 : 80, pages.Sum(page => CountWords(page.Content)));
        var sliced = GetPageRects(pages, document.GetElementById("m")!);
        Assert.Equal(2, sliced.Count);
        Assert.Equal((1, sliced[0].Rect.Y - 300, sliced[0].Rect.Width, sliced[0].Rect.Height), (sliced[1].Page, sliced[1].Rect.Y, sliced[1].Rect.Width, sliced[1].Rect.Height));
    }

    [Fact]
    public void Pages_AutoRepeatGridWithAPlacedItemKeepsItsLayout()
    {
        // An item with a definite column is not placed in the order of the items: the grid keeps the layout of the first page
        var items = string.Concat(Enumerable.Range(0, 10).Select(i => $"<div id=i{i}></div>"));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px } #c > div { height: 100px } #i9 { grid-column: 1 }</style>
            <div id=c style='display: grid; grid-template-columns: repeat(auto-fill, 100px)'>{{items}}</div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal([(1, new PhysicalRect(0, 0, 100, 100))], GetPageRects(pages, document.GetElementById("i6")!));
        Assert.Equal([(1, new PhysicalRect(100, 0, 100, 100))], GetPageRects(pages, document.GetElementById("i7")!));
    }

    [Theory]
    [InlineData("<div id=m style='contain: size; width: 150px; height: 400px'></div>", 200)]
    [InlineData("<div id=m style='contain: size; width: 150px; height: 700px'></div>", 200)]
    [InlineData("<svg id=m width=150 height=400 style='display: block'></svg>", 200)]
    [InlineData("<p style='margin: 0; line-height: 0'><span id=m style='display: inline-block; width: 150px; height: 400px; vertical-align: top'></span></p>", 200)]
    [InlineData("<div id=m style='contain: size; width: 50%; height: 400px'></div>", 200)]
    [InlineData("<div id=m style='contain: size; width: 50%; height: 700px'></div>", 200)]
    [InlineData("<div id=m style='contain: size; width: 150px; height: 400px; margin: 0 auto'></div>", 200)]
    [InlineData("<p style='margin: 0; line-height: 0; text-align: center'><span id=m style='display: inline-block; width: 150px; height: 400px; vertical-align: top'></span></p>", 200)]
    [InlineData("<p style='margin: 0; line-height: 0'><span id=m style='display: inline-block; width: 170px; height: 400px; vertical-align: top'></span> XXXX XXXX</p>", 100)]
    [InlineData("<p style='margin: 0; line-height: 0'><span id=m style='display: inline-block; width: 25%; height: 400px; vertical-align: top'></span><span style='display: inline-block; width: calc(150px - 25%); height: 400px; vertical-align: top'></span></p>", 200)]
    [InlineData("<div id=m style='contain: size; box-sizing: border-box; width: 150px; height: 400px; padding-left: 10%'><div style='height: 10px'></div></div>", 200)]
    public void Pages_ContentSlicedByTheBreakContinuesInAPageOfAnotherInlineSize(string content, double width)
    {
        // The monolithic content (a box with size containment, an SVG image, an inline-block in a line box) is taller than the 300px
        // tall pages: the break slices it. The rest of it continues in the 400px wide pages after the first one when it is laid
        // out the same way in them (the same size and inline position, the same line box); else (another size or position, also
        // when only the content of the line box or of the box changes: inline-blocks whose widths change, a child moved by a
        // percentage padding) the rest of it keeps the layout of the first page. The content after it is laid out in them (50%:
        // 200px), unless it starts beside the content before it: the lines of the words after the inline-block have no height,
        // at the end of the sliced line, so the pages keep the layout of the first page (100px)
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            {{content}}
            <div id=b style='width: 50%; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        var sliced = GetPageRects(pages, document.GetElementById("m")!);
        Assert.Equal((0, 0d), (sliced[0].Page, sliced[0].Rect.Y));
        Assert.Equal((pages.Count - 1, width), GetPageRects(pages, document.GetElementById("b")!) is [var (page, rect)] ? (page, rect.Width) : default);
        for (var i = 1; i < sliced.Count; i++)
        {
            // (the rest of the content continues where the previous page ends)
            Assert.Equal(i, sliced[i].Page);
            Assert.Equal(sliced[0].Rect.Height, sliced[i].Rect.Height);
            Assert.Equal(sliced[0].Rect.X, sliced[i].Rect.X);
            Assert.Equal(-300 * i, sliced[i].Rect.Y);
        }

        Assert.Equal(content.Contains("XXXX", StringComparison.Ordinal) ? 2 : 0, pages.Sum(page => CountWords(page.Content)));
    }

    [Theory]
    [InlineData("columns: 2; column-gap: 0", "height: 100px; break-inside: avoid", "<div id=p style='position: absolute; width: 20px; height: 150px'></div>", 10, "(1, (100, 100, 100x100))", "(1, (0, 200, 200x10))")]
    [InlineData("columns: 2; column-gap: 0; position: relative", "height: 100px; break-inside: avoid", "<div style='anchor-name: --k'></div><div id=p style='position: absolute; position-anchor: --k; top: anchor(bottom); width: 20px; height: 10px'></div>", 10, "(1, (100, 100, 100x100))", "(1, (0, 200, 200x10))")]
    [InlineData("columns: 2; column-gap: 0", "height: 100px; break-inside: avoid", "<div style='columns: 2; column-gap: 0'><div style='height: 1000px'></div></div>", 4, "(1, (100, 0, 100x100))", "(1, (0, 150, 200x10))")]
    [InlineData("display: grid; grid-template-columns: repeat(auto-fill, 100px); grid-auto-rows: 100px", "", "<div style='grid-column: 1'></div>", 10, "(1, (0, 100, 100x100))", "(1, (0, 200, 200x10))")]
    [InlineData("display: grid; grid-template-columns: repeat(auto-fill, 100px); grid-template-rows: repeat(5, 100px); grid-auto-flow: column", "", "", 10, "(1, (100, 100, 100x100))", "(1, (0, 200, 200x10))")]
    [InlineData("display: flex; flex-wrap: wrap", "flex: 0 0 100px; height: 200px", "", 4, "(0, (100, 200, 100x100)) (1, (100, 0, 100x100))", "(1, (0, 100, 200x10))")]
    public void Pages_ContentAfterAContainerThatCannotContinueIsLaidOutInAPageOfAnotherInlineSize(string containerStyle, string itemStyle, string extra, int count, string expected, string afterExpected)
    {
        // The first page is 200px wide, the second one 400px wide. The container cannot continue in the second page in its
        // inline size (a multi-column container with an absolutely positioned box broken by the first page, an anchored
        // absolutely positioned box, or a nested multi-column container broken by it; a grid whose columns depend on its width
        // and whose items are not placed in their order; a flex line broken by the page): the rest of it keeps the layout of the
        // first page (its last item is 100px wide), and the content after it is laid out in the page area of the second page
        // (50%: 200px)
        var items = string.Concat(Enumerable.Range(0, count).Select(i => (i == count - 1 ? $"<div id=last style='{itemStyle}'></div>" : $"<div style='{itemStyle}'></div>") + (i == 1 ? extra : "")));
        var document = Parse($$"""
            <style>@page { size: 400px 300px; margin: 0 } @page :first { size: 200px 300px }</style>
            <div style='{{containerStyle}}'>{{items}}</div>
            <div id=after style='width: 50%; height: 10px'></div>
            """);
        using var session = document.BeginPrint(Options);
        var pages = session.GetPages();
        Assert.Equal((expected, afterExpected), (string.Join(' ', GetPageRects(pages, document.GetElementById("last")!)), string.Join(' ', GetPageRects(pages, document.GetElementById("after")!))));
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
