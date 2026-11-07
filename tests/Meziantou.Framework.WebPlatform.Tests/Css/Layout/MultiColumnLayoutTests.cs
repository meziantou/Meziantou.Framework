using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The multi-column layout (https://drafts.csswg.org/css-multicol-1/) and the block fragmentation (https://drafts.csswg.org/css-break-3/).</summary>
public sealed class MultiColumnLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem; orphans: 1; widows: 1 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static List<PhysicalRect> GetClientRects(Document document, string id)
        => [.. document.GetElementById(id)!.GetClientRects().Select(rect => new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height))];

    private static PhysicalRect GetBoundingRect(Document document, string id)
    {
        var rect = document.GetElementById(id)!.GetBoundingClientRect();
        return new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    private static IReadOnlyList<BoxFragment> GetFragments(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!);

    [Fact]
    public void ColumnCountAndWidth_PseudoAlgorithm()
    {
        // https://drafts.csswg.org/css-multicol-1/#pseudo-algorithm: N = floor((U + gap) / (column-width + gap)), W = (U + gap) / N - gap
        var document = Parse("""
            <div style='width: 300px; column-width: 90px; column-gap: 10px'><div id=a style='height: 30px'></div></div>
            <div style='width: 300px; columns: 2; column-gap: 20px'><div id=b style='height: 30px'></div></div>
            <div style='width: 300px; columns: 5 100px; column-gap: 0'><div id=c style='height: 30px'></div></div>
            """);
        Assert.Equal(Rect(0, 0, 93.328125, 10), GetClientRects(document, "a")[0]);
        Assert.Equal(140, GetClientRects(document, "b")[0].Width);
        Assert.Equal(100, GetClientRects(document, "c")[0].Width);
    }

    [Fact]
    public void ColumnGap_NormalIsOneEm()
    {
        var document = Parse("<div style='width: 210px; columns: 2'><div style='height: 20px'></div><div id=a style='height: 20px'></div></div>");
        Assert.Equal(Rect(110, 0, 100, 20), GetBoundingRect(document, "a"));
    }

    [Fact]
    public void BalancedColumns_LinesAreDistributed()
    {
        // 4 lines of 10px in 2 columns: the columns are 20px tall
        var document = Parse("<div id=mc style='width: 210px; columns: 2; column-gap: 10px'><span id=s>XXXXXXXXXX XXXXXXXXXX XXXXXXXXXX XXXXXXXXXX</span></div>");
        Assert.Equal(20, Get(document, "mc").OffsetHeight);
        Assert.Equal([Rect(0, 0, 100, 10), Rect(0, 10, 100, 10), Rect(110, 0, 100, 10), Rect(110, 10, 100, 10)], GetClientRects(document, "s"));
    }

    [Fact]
    public void BalancedColumns_BlockIsSplitIntoOneFragmentPerColumn()
    {
        var document = Parse("<div id=mc style='width: 320px; columns: 3; column-gap: 10px'><div id=a style='height: 300px'></div></div>");
        Assert.Equal(100, Get(document, "mc").OffsetHeight);
        Assert.Equal([Rect(0, 0, 100, 100), Rect(110, 0, 100, 100), Rect(220, 0, 100, 100)], GetClientRects(document, "a"));

        var fragments = GetFragments(document, "a");
        Assert.Equal(3, fragments.Count);
        Assert.True(fragments[0].IsFirstFragment);
        Assert.False(fragments[0].IsLastFragment);
        Assert.False(fragments[2].IsFirstFragment);
        Assert.True(fragments[2].IsLastFragment);

        // offsetTop and offsetLeft come from the first fragment, offsetWidth and offsetHeight from the union of the fragments
        var a = Get(document, "a");
        Assert.Equal(0, a.OffsetTop);
        Assert.Equal(320, a.OffsetWidth);
        Assert.Equal(100, a.OffsetHeight);

        // getComputedStyle returns the size of the box before the fragmentation
        Assert.Equal("300px", document.DefaultView!.GetComputedStyle(a).GetPropertyValue("height"));
    }

    [Fact]
    public void BalancedColumns_MonolithicContentGivesTheMinimumHeight()
    {
        // https://drafts.csswg.org/css-break-3/#monolithic: a box with size containment is not fragmented: the columns are stretched
        // until the content fits in 2 columns
        var document = Parse("""
            <div id=mc style='width: 200px; columns: 2; column-gap: 0'>
              <div id=a style='height: 10px'></div><div id=b style='contain: size; height: 60px'></div><div id=c style='height: 10px'></div>
            </div>
            """);
        Assert.Equal(70, Get(document, "mc").OffsetHeight);
        Assert.Equal([Rect(0, 10, 100, 60)], GetClientRects(document, "b"));
        Assert.Equal(Rect(100, 0, 100, 10), GetBoundingRect(document, "c"));
    }

    [Fact]
    public void BalancedColumns_BoxesThatAvoidBreaksStretchTheColumns()
    {
        // The boxes with break-inside: avoid have a padding: the content before their block-end padding fits in a column of 160px,
        // but not their padding; the columns are stretched until the boxes fit in 2 columns (200px: 40 + 10 + 60, then 90 + 10 + 100)
        var document = Parse("""
            <div id=mc style='width: 210px; columns: 2; column-gap: 10px'>
              <div id=a style='height: 30px; padding: 5px 0; margin-bottom: 10px; break-inside: avoid'></div>
              <div id=b style='height: 50px; padding: 5px 0; margin-bottom: 10px; break-inside: avoid'></div>
              <div id=c style='height: 80px; padding: 5px 0; margin-bottom: 10px; break-inside: avoid'></div>
              <div id=d style='height: 90px; padding: 5px 0; break-inside: avoid'></div>
            </div>
            """);
        Assert.Equal(200, Get(document, "mc").OffsetHeight);
        Assert.Equal(Rect(0, 50, 100, 60), GetBoundingRect(document, "b"));
        Assert.Equal(Rect(110, 0, 100, 90), GetBoundingRect(document, "c"));
        Assert.Equal(Rect(110, 100, 100, 100), GetBoundingRect(document, "d"));
    }

    [Fact]
    public void IntrinsicSizes_ColumnCountWithAColumnWidthNarrowerThanTheContent()
    {
        // https://drafts.csswg.org/css-sizing-3/#multicol-intrinsic: the max-content inline size is the column count times the larger
        // of the column width and the largest min-content contribution of the contents (120px), plus the gaps
        var document = Parse("""
            <div id=mc style='display: inline-block; columns: 2 100px; column-gap: 10px'>
              <div style='width: 120px; height: 10px'></div><div style='width: 120px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(250, Get(document, "mc").OffsetWidth);
    }

    [Fact]
    public void ColumnFillAuto_FillsTheColumnsSequentially()
    {
        var document = Parse("""
            <div id=mc style='width: 320px; height: 100px; columns: 3; column-gap: 10px; column-fill: auto'>
              <div id=a style='height: 60px'></div><div id=b style='height: 60px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 60), GetBoundingRect(document, "a"));
        Assert.Equal([Rect(0, 60, 100, 40), Rect(110, 0, 100, 20)], GetClientRects(document, "b"));
    }

    [Fact]
    public void OverflowColumns_AreCreatedInTheInlineDirection()
    {
        var document = Parse("<div style='width: 210px; height: 50px; columns: 2; column-gap: 10px; column-fill: auto'><div id=a style='height: 200px'></div></div>");
        Assert.Equal([Rect(0, 0, 100, 50), Rect(110, 0, 100, 50), Rect(220, 0, 100, 50), Rect(330, 0, 100, 50)], GetClientRects(document, "a"));
    }

    [Fact]
    public void ForcedBreaks()
    {
        // https://drafts.csswg.org/css-break-3/#break-between: break-before/break-after: column (propagated from the first child)
        var document = Parse("""
            <div id=mc style='width: 320px; columns: 3; column-gap: 10px'>
              <div id=a style='height: 10px; break-after: column'></div>
              <div><div id=b style='height: 10px; break-before: column'></div></div>
              <div id=c style='height: 10px; break-before: page'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 10), GetBoundingRect(document, "a"));
        Assert.Equal(Rect(110, 0, 100, 10), GetBoundingRect(document, "b"));

        // The page breaks do not apply to the columns
        Assert.Equal(Rect(220, 0, 100, 10), GetBoundingRect(document, "c"));
    }

    [Fact]
    public void BreakInsideAvoid_MovesTheBoxToTheNextColumn()
    {
        var document = Parse("""
            <div style='width: 210px; height: 100px; columns: 2; column-gap: 10px; column-fill: auto'>
              <div style='height: 70px'></div>
              <div id=a style='break-inside: avoid'><div style='height: 20px'></div><div style='height: 20px'></div></div>
            </div>
            """);
        Assert.Equal([Rect(110, 0, 100, 40)], GetClientRects(document, "a"));
    }

    [Fact]
    public void OrphansAndWidows()
    {
        // 4 lines, the column has room for 3: widows: 2 moves the third line to the next column
        var document = Parse("""
            <div style='width: 210px; height: 30px; columns: 2; column-gap: 10px; column-fill: auto'>
              <div id=a style='widows: 2'><span id=s>XXXXXXX XXXXXXX XXXXXXX XXXXXXX</span></div>
            </div>
            """);
        var rects = GetClientRects(document, "s");
        Assert.Equal(4, rects.Count);
        Assert.Equal(Rect(0, 10, 70, 10), rects[1]);
        Assert.Equal(Rect(110, 0, 70, 10), rects[2]);
    }

    [Fact]
    public void ColumnSpanAll_SpansTheColumnsBetweenTheRows()
    {
        var document = Parse("""
            <div id=mc style='width: 210px; columns: 2; column-gap: 10px'>
              <div id=a style='height: 40px'></div>
              <div id=spanner style='column-span: all; height: 10px'></div>
              <div id=b style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 100, 20), Rect(110, 0, 100, 20)], GetClientRects(document, "a"));
        Assert.Equal(Rect(0, 20, 210, 10), GetBoundingRect(document, "spanner"));
        Assert.Equal([Rect(0, 30, 100, 10), Rect(110, 30, 100, 10)], GetClientRects(document, "b"));
        Assert.Equal(40, Get(document, "mc").OffsetHeight);
    }

    [Fact]
    public void IntrinsicSize_IsTheColumnCountTimesTheContent()
    {
        var document = Parse("<div id=mc style='float: left; columns: 2; column-gap: 10px'><div style='width: 20px; height: 10px'></div></div>");
        Assert.Equal(50, Get(document, "mc").OffsetWidth);
    }

    [Fact]
    public void VerticalWritingMode()
    {
        // The columns progress downwards, the block axis goes from right to left
        var document = Parse("<div style='writing-mode: vertical-rl; width: 50px; height: 210px; columns: 2; column-gap: 10px; column-fill: auto'><div id=a style='width: 70px'></div></div>");
        Assert.Equal([Rect(0, 0, 50, 100), Rect(30, 110, 20, 100)], GetClientRects(document, "a"));
    }

    [Fact]
    public void BoxDecorationBreakSlice_TheEdgesAreOnTheFirstAndLastFragments()
    {
        var document = Parse("<div style='width: 210px; columns: 2; column-gap: 10px'><div id=a style='border: 5px solid; height: 30px'></div></div>");
        var fragments = GetFragments(document, "a");
        Assert.Equal(2, fragments.Count);
        Assert.Equal(5, fragments[0].Border.Top);
        Assert.Equal(0, fragments[0].Border.Bottom);
        Assert.Equal(0, fragments[1].Border.Top);
        Assert.Equal(5, fragments[1].Border.Bottom);
    }

    [Fact]
    public void BoxDecorationBreakClone_EveryFragmentHasTheEdges()
    {
        var document = Parse("<div style='width: 210px; height: 30px; columns: 2; column-gap: 10px; column-fill: auto'><div id=a style='border: 5px solid; box-decoration-break: clone'><div style='height: 15px'></div><div style='height: 15px'></div></div></div>");
        var fragments = GetFragments(document, "a");
        Assert.Equal(2, fragments.Count);
        Assert.Equal(5, fragments[0].Border.Top);
        Assert.Equal(5, fragments[0].Border.Bottom);
        Assert.Equal(5, fragments[1].Border.Top);
        Assert.Equal(5, fragments[1].Border.Bottom);
    }

    [Fact]
    public void BoxDecorationBreakClone_EdgesTallerThanTheColumns()
    {
        // The repeated borders (10px) do not fit in the columns (1px at least): each fragment has 1px of the content of the box
        // and overflows its column; no fragment has the borders only
        var document = Parse("<div style='width: 320px; height: 0; columns: 3; column-gap: 10px; column-fill: auto'><div id=a style='height: 2px; border: 5px solid; box-decoration-break: clone'></div></div>");
        var fragments = GetFragments(document, "a");
        Assert.Equal(2, fragments.Count);
        Assert.All(fragments, fragment => Assert.Equal(11, fragment.Size.Height));
        Assert.All(fragments, fragment => Assert.Equal(new Sides<double>(5, 5, 5, 5), fragment.Border));
    }

    [Fact]
    public void OverflowClip_OverflowingContentIsNotFragmented()
    {
        var document = Parse("""
            <div style='width: 320px; height: 100px; columns: 3; column-gap: 10px; column-fill: auto'>
              <div style='overflow: clip; height: 150px'><div style='height: 150px'></div><div id=a style='height: 100px'></div></div>
            </div>
            """);
        Assert.Equal([Rect(110, 50, 100, 100)], GetClientRects(document, "a"));
    }

    [Fact]
    public void AlignContent_AlignsTheColumns()
    {
        var document = Parse("<div style='width: 210px; height: 100px; columns: 2; column-gap: 10px; align-content: end'><div id=a style='height: 40px'></div></div>");
        Assert.Equal([Rect(0, 80, 100, 20), Rect(110, 80, 100, 20)], GetClientRects(document, "a"));
    }

    [Fact]
    public void ColumnHeight_WrapsTheColumnsInRows()
    {
        // https://drafts.csswg.org/css-multicol-2/#ch: column-height fixes the block size of the rows, column-wrap: auto wraps the
        // columns; the content of the last row is balanced (column-fill: balance), and the row keeps its block size
        var document = Parse("<div id=mc style='width: 210px; columns: 2; column-gap: 10px; column-height: 50px; row-gap: 10px'><div id=a style='height: 150px'></div></div>");
        Assert.Equal([Rect(0, 0, 100, 50), Rect(110, 0, 100, 50), Rect(0, 60, 100, 25), Rect(110, 60, 100, 25)], GetClientRects(document, "a"));
        Assert.Equal(110, Get(document, "mc").OffsetHeight);

        document = Parse("<div id=mc style='width: 210px; columns: 2; column-gap: 10px; column-height: 50px; row-gap: 10px; column-fill: auto'><div id=a style='height: 150px'></div></div>");
        Assert.Equal([Rect(0, 0, 100, 50), Rect(110, 0, 100, 50), Rect(0, 60, 100, 50)], GetClientRects(document, "a"));
        Assert.Equal(110, Get(document, "mc").OffsetHeight);
    }

    [Fact]
    public void AbsolutelyPositionedBox_IsFragmented()
    {
        var document = Parse("""
            <div style='position: relative; width: 210px; height: 100px; columns: 2; column-gap: 10px; column-fill: auto'>
              <div style='height: 80px'></div>
              <div style='position: relative; height: 20px'><div id=a style='position: absolute; top: 0; width: 10px; height: 40px'></div></div>
            </div>
            """);
        Assert.Equal([Rect(0, 80, 10, 20), Rect(110, 0, 10, 20)], GetClientRects(document, "a"));
    }

    [Fact]
    public void ColumnSpannerInANestedBlock_IsNotDuplicated()
    {
        var document = Parse("""
            <style>
              .relpos { position: relative; }
              .columns { column-count: 2; column-fill: auto; column-gap: 10px; width: 210px; height: 100px; }
              .t { position: absolute; width: 10px; height: 10px; }
            </style>
            <div class="columns relpos"><div style="height: 40px"></div><div class=relpos><div style="column-span: all"><div class=relpos><div id=t1 class=t></div></div></div></div></div>
            """);

        // The block that contains the spanner is not sliced: the spanner is placed by the multi-column container only
        Assert.Single(Get(document, "t1").GetClientRects());
    }

    [Fact]
    public void Spanner_InsideABlock_MarginsDoNotCollapseWithTheBlock()
    {
        // WPT css/css-multicol/multicol-span-all-007.html: the margins of a spanner do not collapse with the ones of its parent
        var document = Parse("""
            <div style='columns: 2; column-gap: 0; width: 200px'>
              <div style='height: 10px'></div>
              <div id=wrapper>
                <div id=spanner style='column-span: all; height: 10px; margin: 20px 0'></div>
              </div>
              <div id=last style='height: 10px'></div>
            </div>
            """);
        // The first row is balanced (5px), the spanner is 20px below it
        Assert.Equal(Rect(0, 25, 200, 10), document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById("spanner")!));
        Assert.Equal(55, document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById("last")!)!.Value.Y);
    }

    [Fact]
    public void Spanner_MarginsCollapseWithTheAdjacentSpannerOnly()
    {
        // https://drafts.csswg.org/css-multicol-1/#spanning-columns: "margins of two adjacent spanners collapse with each other";
        // the margins of the content of the columns do not collapse with the ones of a spanner (they are kept after it)
        var document = Parse("""
            <div id=mc style='width: 200px; columns: 2; column-gap: 0'>
              <div style='height: 40px'></div>
              <div id=s1 style='column-span: all; height: 10px; margin-bottom: 20px'></div>
              <div id=s2 style='column-span: all; height: 10px; margin: 30px 0 10px'></div>
              <div id=b style='height: 40px; margin-top: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 20, 200, 10), GetBoundingRect(document, "s1"));
        Assert.Equal(Rect(0, 60, 200, 10), GetBoundingRect(document, "s2"));
        Assert.Equal([Rect(0, 90, 100, 15), Rect(100, 80, 100, 25)], GetClientRects(document, "b"));
        Assert.Equal(105, Get(document, "mc").OffsetHeight);
    }

    [Fact]
    public void Spanner_MarginCollapsesWithTheMarginOfItsParent()
    {
        // WPT css/css-multicol/multicol-span-all-margin-nested-002.xht: the margin of a spanner that is the first content of its
        // parent collapses with the margin of the parent, not with the margin of the spanner before the parent
        var document = Parse("""
            <div style='width: 200px; columns: 2; column-gap: 0'>
              <div style='height: 40px'></div>
              <div id=s1 style='column-span: all; height: 10px; margin-bottom: 20px'></div>
              <div style='margin-top: 30px'><div id=s2 style='column-span: all; height: 10px; margin-top: 20px'></div></div>
            </div>
            """);
        Assert.Equal(Rect(0, 20, 200, 10), GetBoundingRect(document, "s1"));
        Assert.Equal(Rect(0, 80, 200, 10), GetBoundingRect(document, "s2"));
    }

    [Fact]
    public void Spanner_IsBelowTheFloatsAndTheOverflowingContentBeforeIt()
    {
        // The floats and the content that overflows a box before a spanner are in the row of columns before the spanner
        var document = Parse("""
            <div style='width: 200px; columns: 2; column-gap: 0'>
              <div id=f style='float: left; width: 50px; height: 60px'></div>
              <div id=s style='column-span: all; height: 10px'></div>
            </div>
            <div style='width: 200px; columns: 2; column-gap: 0'>
              <div style='height: 10px'><div id=a style='height: 80px'></div></div>
              <div id=b style='height: 10px'></div>
              <div id=s2 style='column-span: all; height: 10px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 30), Rect(100, 0, 50, 30)], GetClientRects(document, "f"));
        Assert.Equal(Rect(0, 30, 200, 10), GetBoundingRect(document, "s"));
        Assert.Equal([Rect(0, 40, 100, 40), Rect(100, 40, 100, 40)], GetClientRects(document, "a"));
        Assert.Equal(Rect(0, 50, 100, 10), GetBoundingRect(document, "b"));
        Assert.Equal(Rect(0, 80, 200, 10), GetBoundingRect(document, "s2"));
    }

    [Fact]
    public void Spanner_InterruptsTheBoxThatContainsIt()
    {
        // WPT css/css-multicol/multicol-span-all-children-height-004a.html: a box with a definite block size continues after the
        // spanner it contains, with the block size that remains
        var document = Parse("""
            <div style='width: 200px; columns: 2; column-gap: 0'>
              <div id=box style='height: 90px'>
                <div style='height: 40px'></div>
                <div id=s style='column-span: all; height: 10px'></div>
                <div id=b style='height: 40px'></div>
              </div>
              <div id=c style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 100, 20), Rect(100, 0, 100, 20), Rect(0, 30, 100, 35), Rect(100, 30, 100, 15)], GetClientRects(document, "box"));
        Assert.Equal(Rect(0, 20, 200, 10), GetBoundingRect(document, "s"));
        Assert.Equal([Rect(0, 30, 100, 35), Rect(100, 30, 100, 5)], GetClientRects(document, "b"));
        Assert.Equal([Rect(100, 45, 100, 20)], GetClientRects(document, "c"));

        // WPT css/css-multicol/multicol-span-all-children-height-009.html: the content after a box that ends before the spanner it
        // contains is after the spanner
        document = Parse("""
            <div style='width: 200px; columns: 2; column-gap: 0'>
              <div style='height: 10px'>
                <div style='height: 40px'></div>
                <div id=s style='column-span: all; height: 10px'></div>
              </div>
              <div id=c style='height: 20px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 20, 200, 10), GetBoundingRect(document, "s"));
        Assert.Equal([Rect(0, 30, 100, 10), Rect(100, 30, 100, 10)], GetClientRects(document, "c"));
    }

    [Fact]
    public void Spanner_ContentAfterItWithANegativeMarginIsInTheNextRow()
    {
        // WPT css/css-multicol/multicol-span-all-015.html: the margin of the content after a spanner is kept: a negative margin
        // moves the box above its row of columns
        var document = Parse("""
            <div id=mc style='width: 200px; columns: 2; column-gap: 0'>
              <div style='height: 30px'></div>
              <div id=s style='column-span: all; height: 10px'></div>
              <div id=b style='height: 60px; margin-top: -20px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 15, 200, 10), GetBoundingRect(document, "s"));
        Assert.Equal([Rect(0, 5, 100, 40), Rect(100, 25, 100, 20)], GetClientRects(document, "b"));
        Assert.Equal(45, Get(document, "mc").OffsetHeight);
    }

    [Fact]
    public void Spanner_InABoxWithATransform_IsNotASpanner()
    {
        // WPT css/css-multicol/multicol-span-all-010.html: as in Blink, no spanner in a box that is a containing block for the fixed positioned boxes
        var document = Parse("<div style='width: 200px; columns: 2; column-gap: 0'><div style='transform: scale(1)'><div id=s style='column-span: all; height: 40px'></div></div></div>");
        Assert.Equal([Rect(0, 0, 100, 20), Rect(100, 0, 100, 20)], GetClientRects(document, "s"));
    }

    [Fact]
    public void IntrinsicSize_SpannersAndColumnWidth()
    {
        // The spanners are not in a column: the container is at least as wide as they are; the max-content size of a column is its column-width
        var document = Parse("""
            <div id=mc style='width: fit-content; columns: 3; column-gap: 0'>
              <div style='width: 20px; height: 10px'></div>
              <div style='column-span: all'><div style='width: 100px; height: 10px'></div></div>
            </div>
            <div id=mc2 style='width: max-content; column-width: 80px; column-gap: 0'><div style='width: 100px; height: 10px'></div></div>
            """);
        Assert.Equal(100, Get(document, "mc").OffsetWidth);
        Assert.Equal(80, Get(document, "mc2").OffsetWidth);
    }

    [Fact]
    public void ColumnFillAuto_WithAMaximumBlockSize()
    {
        // WPT css/css-multicol/columnfill-auto-max-height-003.html: the columns are filled up to the maximum block size, and the
        // container is as tall as its content
        var document = Parse("""
            <div id=mc style='width: 200px; columns: 2; column-gap: 0; column-fill: auto; max-height: 100px'><div id=a style='height: 60px'></div></div>
            <div id=mc2 style='width: 200px; columns: 2; column-gap: 0; column-fill: auto; max-height: 100px'><div id=b style='height: 150px'></div></div>
            """);
        Assert.Equal([Rect(0, 0, 100, 60)], GetClientRects(document, "a"));
        Assert.Equal(60, Get(document, "mc").OffsetHeight);
        Assert.Equal([Rect(0, 60, 100, 100), Rect(100, 60, 100, 50)], GetClientRects(document, "b"));
        Assert.Equal(100, Get(document, "mc2").OffsetHeight);
    }

    [Fact]
    public void BalancedColumns_TheMarginsAtTheBreaksAreTruncated()
    {
        // https://drafts.csswg.org/css-break-3/#break-margins: a column may end in the margins between two boxes (the content of
        // the columns is 20 + 60, 60 and 60px)
        var document = Parse("""
            <div id=mc style='width: 300px; columns: 3; column-gap: 0'>
              <div id=a style='height: 60px; margin-top: 20px'></div>
              <div id=b style='height: 60px; margin-top: 20px'></div>
              <div id=c style='height: 60px; margin-top: 20px'></div>
            </div>
            """);
        Assert.Equal(80, Get(document, "mc").OffsetHeight);
        Assert.Equal(Rect(0, 20, 100, 60), GetBoundingRect(document, "a"));
        Assert.Equal(Rect(100, 0, 100, 60), GetBoundingRect(document, "b"));
        Assert.Equal(Rect(200, 0, 100, 60), GetBoundingRect(document, "c"));
    }

    [Fact]
    public void ColumnHeight_SpannersAreInTheRows()
    {
        // WPT css/css-multicol/column-height-018.html: the rows are at fixed positions; a spanner that does not fit in what remains
        // of its row is in the next row, and the content after it is in the next row too
        var document = Parse("""
            <div id=mc style='width: 200px; columns: 2; column-gap: 0; column-height: 20px; row-gap: 10px'>
              <div id=a style='height: 20px'></div>
              <div id=s style='column-span: all; height: 25px'></div>
              <div id=b style='height: 60px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 100, 10), Rect(100, 0, 100, 10)], GetClientRects(document, "a"));
        Assert.Equal(Rect(0, 30, 200, 25), GetBoundingRect(document, "s"));
        Assert.Equal([Rect(0, 60, 100, 20), Rect(100, 60, 100, 20), Rect(0, 90, 100, 10), Rect(100, 90, 100, 10)], GetClientRects(document, "b"));
        Assert.Equal(110, Get(document, "mc").OffsetHeight);

        // WPT css/css-multicol/column-height-020.html: a spanner that fits is after the content of its row, and the content after
        // it fills what remains of the row
        document = Parse("""
            <div style='width: 200px; columns: 2; column-gap: 0; column-height: 20px; row-gap: 10px'>
              <div id=a style='height: 50px'></div>
              <div id=s style='column-span: all; height: 10px'></div>
              <div id=b style='height: 70px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 100, 20), Rect(100, 0, 100, 20), Rect(0, 30, 100, 5), Rect(100, 30, 100, 5)], GetClientRects(document, "a"));
        Assert.Equal(Rect(0, 35, 200, 10), GetBoundingRect(document, "s"));
        Assert.Equal([Rect(0, 45, 100, 5), Rect(100, 45, 100, 5), Rect(0, 60, 100, 20), Rect(100, 60, 100, 20), Rect(0, 90, 100, 10), Rect(100, 90, 100, 10)], GetClientRects(document, "b"));
    }

    [Fact]
    public void ColumnWrap_TheForcedBreaksWrapTheColumnsOfAContainerWithoutBlockSize()
    {
        // WPT css/css-multicol/column-wrap-no-constraints-002.html: each row is as tall as its content
        var document = Parse("""
            <div id=mc style='width: 200px; columns: 2; column-gap: 0; column-wrap: wrap; row-gap: 10px'>
              <div id=a style='height: 20px; break-after: column'></div>
              <div id=b style='height: 30px; break-after: column'></div>
              <div id=c style='height: 50px; break-after: column'></div>
              <div id=d style='height: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 20), GetBoundingRect(document, "a"));
        Assert.Equal(Rect(100, 0, 100, 30), GetBoundingRect(document, "b"));
        Assert.Equal(Rect(0, 40, 100, 50), GetBoundingRect(document, "c"));
        Assert.Equal(Rect(100, 40, 100, 10), GetBoundingRect(document, "d"));
        Assert.Equal(90, Get(document, "mc").OffsetHeight);
    }

    [Fact]
    public void NestedMulticol_IsFragmentedByTheOuterColumns()
    {
        // https://drafts.csswg.org/css-multicol-1/#nested-multicol: the row of the inner container is as tall as what remains of the
        // outer column (2 x 40px), and its content continues in a row of the next outer column (balanced: 2 x 60px)
        var document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='height: 60px'></div>
              <div id=inner style='columns: 2; column-gap: 0'><div id=b style='height: 200px'></div></div>
              <div id=c style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 60, 100, 40), Rect(100, 0, 100, 60)], GetClientRects(document, "inner"));
        Assert.Equal([Rect(0, 60, 50, 40), Rect(50, 60, 50, 40), Rect(100, 0, 50, 60), Rect(150, 0, 50, 60)], GetClientRects(document, "b"));
        Assert.Equal([Rect(100, 60, 100, 20)], GetClientRects(document, "c"));
    }

    [Fact]
    public void NestedMulticol_TheOuterColumnsAreBalancedWithTheInnerRows()
    {
        // WPT css/css-multicol/multicol-fill-balance-026.html: the inner columns fill the outer columns (5 x 2 columns of 100px)
        var document = Parse("""
            <div id=outer style='width: 200px; columns: 5; column-gap: 0'>
              <div id=inner style='columns: 2; column-gap: 0; column-fill: auto'><div id=b style='height: 1000px'></div></div>
            </div>
            """);
        Assert.Equal(100, Get(document, "outer").OffsetHeight);
        Assert.Equal(5, GetClientRects(document, "inner").Count);
        Assert.Equal(10, GetClientRects(document, "b").Count);
        Assert.Equal(Rect(180, 0, 20, 100), GetClientRects(document, "b")[9]);
    }

    [Fact]
    public void NestedMulticol_SpannersAreFragmentedByTheOuterColumns()
    {
        // WPT css/css-multicol/spanner-fragmentation-000.html
        var document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div id=inner style='columns: 2; column-gap: 0'>
                <div id=a style='height: 40px'></div>
                <div id=s1 style='column-span: all; height: 60px'></div>
                <div id=s2 style='column-span: all; height: 60px'></div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 20), Rect(50, 0, 50, 20)], GetClientRects(document, "a"));
        Assert.Equal([Rect(0, 20, 100, 60)], GetClientRects(document, "s1"));
        Assert.Equal([Rect(0, 80, 100, 20), Rect(100, 0, 100, 40)], GetClientRects(document, "s2"));
        Assert.Equal([Rect(0, 0, 100, 100), Rect(100, 0, 100, 40)], GetClientRects(document, "inner"));

        // WPT css/css-multicol/spanner-fragmentation-010.html: a forced break before a spanner breaks the outer columns
        document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='height: 50px'></div>
              <div style='columns: 2; column-gap: 0'>
                <div id=b style='height: 100px'></div>
                <div id=s style='column-span: all; height: 100px; break-before: column'></div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(0, 50, 50, 50), Rect(50, 50, 50, 50)], GetClientRects(document, "b"));
        Assert.Equal([Rect(100, 0, 100, 100)], GetClientRects(document, "s"));
    }

    [Fact]
    public void NestedMulticol_DefiniteBlockSizeContinuesInTheOuterColumns()
    {
        // The content box of the inner container (150px) is in two outer columns; its content is balanced in the first one
        var document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div id=inner style='columns: 2; column-gap: 0; height: 150px; border-bottom: 10px solid'><div id=a style='height: 40px'></div></div>
              <div id=c style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 100, 100), Rect(100, 0, 100, 60)], GetClientRects(document, "inner"));
        Assert.Equal([Rect(0, 0, 50, 20), Rect(50, 0, 50, 20)], GetClientRects(document, "a"));
        Assert.Equal([Rect(100, 60, 100, 20)], GetClientRects(document, "c"));
    }

    [Fact]
    public void NestedMulticol_ARowThatViolatesARuleIsMovedToTheNextOuterColumn()
    {
        // WPT css/css-multicol/multicol-nested-007.html: the two lines cannot be in two columns (orphans, widows) and do not fit in
        // one column of the 60px that remain in the first outer column
        var document = Parse("""
            <div style='width: 200px; height: 160px; columns: 2; column-gap: 0; column-fill: auto; line-height: 50px; orphans: 2; widows: 2'>
              <div style='height: 100px'></div>
              <div id=inner style='columns: 2; column-gap: 0; column-fill: auto'><span id=l1>x</span><br><span id=l2>x</span></div>
            </div>
            """);
        Assert.Equal([Rect(100, 0, 100, 100)], GetClientRects(document, "inner"));
        Assert.Equal(Rect(100, 20, 10, 10), GetBoundingRect(document, "l1"));
        Assert.Equal(Rect(100, 70, 10, 10), GetBoundingRect(document, "l2"));
    }

    [Fact]
    public void NestedMulticol_AbsolutelyPositionedBoxesAreInTheSequenceOfTheInnerColumns()
    {
        // WPT css/css-break/out-of-flow-in-multicolumn-021.html: the box starts 250px after the start of its containing block, in
        // the third inner column. The inner columns are as tall as the outer column (the container has no in-flow content: its
        // block size is 0), and the columns created for the box are in new rows in the next outer columns
        var document = Parse("""
            <div id=outer style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div id=inner style='columns: 2; column-gap: 0; column-fill: auto'>
                <div style='position: relative'><div id=a style='position: absolute; top: 250px; width: 50px; height: 300px'></div></div>
              </div>
              <div id=c style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(100, 50, 50, 50), Rect(150, 0, 50, 100), Rect(200, 0, 50, 100), Rect(250, 0, 50, 50)], GetClientRects(document, "a"));
        Assert.Equal(0, Get(document, "inner").OffsetHeight);
        Assert.Equal([Rect(0, 0, 100, 20)], GetClientRects(document, "c"));

        // The boxes use the columns of the in-flow content first: the last row has room for one more column
        document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='height: 40px'></div>
              <div id=inner style='columns: 2; column-gap: 0; column-fill: auto'>
                <div style='position: relative; height: 30px'><div id=a style='position: absolute; top: 0; width: 50px; height: 200px'></div></div>
              </div>
            </div>
            """);
        // (the row of the second outer column is in an empty fragment of the container, which has ended)
        Assert.Equal([Rect(0, 40, 100, 30), Rect(100, 0, 100, 0)], GetClientRects(document, "inner"));
        Assert.Equal([Rect(0, 40, 50, 60), Rect(50, 40, 50, 60), Rect(100, 0, 50, 80)], GetClientRects(document, "a"));
    }

    [Fact]
    public void NestedMulticol_AbsolutelyPositionedBoxesContinueAfterASpanner()
    {
        // WPT css/css-break/out-of-flow-in-multicolumn-117.html: the row before the spanner is balanced with the box (2 x 20px),
        // which uses all its columns
        var document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='height: 40px'></div>
              <div id=inner style='columns: 2; column-gap: 0; column-fill: auto'>
                <div id=r style='position: relative; height: 20px'><div id=a style='position: absolute; top: 0; width: 50px; height: 40px'></div></div>
                <div id=s style='column-span: all; height: 40px'></div>
              </div>
            </div>
            """);
        Assert.Equal(Rect(0, 40, 50, 20), GetClientRects(document, "r")[0]);
        Assert.Equal([Rect(0, 40, 50, 20), Rect(50, 40, 50, 20)], GetClientRects(document, "a"));
        Assert.Equal([Rect(0, 60, 100, 40)], GetClientRects(document, "s"));
    }

    [Fact]
    public void NestedMulticol_AbsolutelyPositionedChildrenOfTheContainerAreFragmentedByTheOuterColumns()
    {
        // WPT css/css-break/out-of-flow-in-multicolumn-047.html: the containing block of the box is the nested container, not
        // a box of its columns
        var document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div id=inner style='position: relative; columns: 2; column-gap: 0; column-fill: auto'>
                <div id=a style='position: absolute; top: 50px; width: 100px; height: 100px'></div>
                <div id=b style='height: 150px'></div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "b"));
        Assert.Equal([Rect(0, 50, 100, 50), Rect(100, 0, 100, 50)], GetClientRects(document, "a"));
    }

    [Fact]
    public void NestedMulticol_StaticPositionOfABoxWhoseContainingBlockIsOutsideOfTheContainer()
    {
        // WPT css/css-break/out-of-flow-in-multicolumn-040.html: the static position is in the third inner column, which is in
        // the second outer column (the container is laid out in the outer strip with two balanced columns of 125px)
        var document = Parse("""
            <div style='width: 200px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='position: relative'>
                <div style='columns: 2; column-gap: 0; column-fill: auto'>
                  <div style='height: 250px'></div>
                  <div id=a style='position: absolute; width: 10px; height: 20px'></div>
                </div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(100, 50, 10, 20)], GetClientRects(document, "a"));
    }

    [Fact]
    public void AbsolutelyPositionedBox_StretchedInAContainingBlockInterruptedByASpanner()
    {
        // WPT css/css-break/out-of-flow-in-multicolumn-018.html: the box is as tall as the fragments of its containing block
        // (360px), which the spanner interrupts
        var document = Parse("""
            <div style='width: 100px; height: 200px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='position: relative; height: 360px'>
                <div style='height: 180px'></div>
                <div style='column-span: all; height: 20px'></div>
                <div id=a style='position: absolute; top: 0; bottom: 0; width: 50px'></div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 90), Rect(50, 0, 50, 90), Rect(0, 110, 50, 90), Rect(50, 110, 50, 90)], GetClientRects(document, "a"));
    }

    [Fact]
    public void AbsolutelyPositionedBox_StaticPositionInTheContentsThatOverflowItsContainingBlock()
    {
        // WPT css/css-break/out-of-flow-in-multicolumn-120.html: the static position follows the breaks of the contents of the
        // containing block (a parallel flow: its block size is specified), 10px after the start of the second column
        var document = Parse("""
            <div style='width: 100px; height: 100px; columns: 2; column-gap: 0; column-fill: auto'>
              <div style='position: relative; height: 50px'>
                <div style='height: 60px'></div>
                <div style='height: 50px'></div>
                <div id=a style='position: absolute; width: 50px; height: 90px'></div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(50, 10, 50, 90)], GetClientRects(document, "a"));
    }

    [Fact]
    public void MarginTrim_TrimsTheMarginsOfTheSpannersAtTheEdgesOfTheContainer()
    {
        // https://drafts.csswg.org/css-box-4/#margin-trim applies to the multi-column containers: the block-start margin of the
        // first spanner and the block-end margin of the last one, when no content of the columns separates them from the edges
        var document = Parse("""
            <div id=a style='columns: 2; column-gap: 0; width: 100px; margin-trim: block'>
              <div id=s1 style='column-span: all; height: 10px; margin: 20px 0'></div>
              <div id=s2 style='column-span: all; height: 10px; margin: 20px 0'></div>
            </div>
            <div id=b style='columns: 2; column-gap: 0; width: 100px; margin-trim: block'>
              <div style='height: 40px'></div>
              <div id=s3 style='column-span: all; height: 10px; margin: 20px 0'></div>
            </div>
            <div id=c style='columns: 2; column-gap: 0; width: 100px; margin-trim: block'>
              <div id=content style='height: 40px; margin: 6px 0'></div>
            </div>
            """);
        Assert.Equal(0, GetBoundingRect(document, "s1").Y);
        Assert.Equal(30, GetBoundingRect(document, "s2").Y);
        Assert.Equal(40, GetBoundingRect(document, "a").Height);

        // The margin of the spanner after the content of the columns is not at the block-start edge
        var b = GetBoundingRect(document, "b");
        Assert.Equal(b.Y + 40, GetBoundingRect(document, "s3").Y);
        Assert.Equal(50, b.Height);

        // The margins of the content of the columns
        Assert.Equal(20, GetBoundingRect(document, "c").Height);
    }

    [Fact]
    public void TextBoxTrim_TrimsTheFirstAndLastLinesOfEachColumn()
    {
        // https://drafts.csswg.org/css-inline-3/#text-box-trim: "each column of a multi-column container". The lines are 20px
        // tall with a half-leading of 5px: 4 lines take 20 * 4 - 10 = 70px in a column, where 3 untrimmed lines would fit
        var document = Parse("""
            <div id=m style='columns: 2; column-gap: 0; column-fill: auto; width: 100px; height: 70px; line-height: 2; text-box-trim: trim-both'>
              <span id=a>x</span><br><span>x</span><br><span>x</span><br><span id=d>x</span><br><span id=e>x</span><br><span id=f>x</span>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 10, 10), GetBoundingRect(document, "a"));
        Assert.Equal(Rect(0, 60, 10, 10), GetBoundingRect(document, "d"));
        Assert.Equal(Rect(50, 0, 10, 10), GetBoundingRect(document, "e"));
        Assert.Equal(Rect(50, 20, 10, 10), GetBoundingRect(document, "f"));
    }

    [Fact]
    public void TextBoxTrim_OfADescendantIsNotAppliedAtTheColumnBreaks()
    {
        // A box with its own text-box-trim on a side trims that side in its first (last) fragment only: here the block-start
        // side of the lines that start the next columns is not trimmed, the block-end side of the last line of a column is
        // (the text-box-trim of the multi-column container)
        var document = Parse("""
            <div style='columns: 2; column-gap: 0; column-fill: auto; width: 100px; height: 65px; line-height: 2; text-box-trim: trim-both'>
              <div style='text-box-trim: trim-start'><span id=a>x</span><br><span>x</span><br><span id=c>x</span><br><span id=d>x</span><br><span id=e>x</span></div>
            </div>
            """);
        Assert.Equal(0, GetBoundingRect(document, "a").Y);
        Assert.Equal(Rect(0, 40, 10, 10), GetBoundingRect(document, "c"));
        Assert.Equal(Rect(50, 5, 10, 10), GetBoundingRect(document, "d"));

        // With box-decoration-break: clone, each fragment of the box is trimmed
        document = Parse("""
            <div style='columns: 2; column-gap: 0; column-fill: auto; width: 100px; height: 50px; line-height: 2'>
              <div style='text-box-trim: trim-both; box-decoration-break: clone'><span id=a>x</span><br><span>x</span><br><span id=c>x</span><br><span id=d>x</span><br><span id=e>x</span></div>
            </div>
            """);
        Assert.Equal(Rect(0, 40, 10, 10), GetBoundingRect(document, "c"));
        Assert.Equal(Rect(50, 0, 10, 10), GetBoundingRect(document, "d"));
    }

    [Fact]
    public void TextBoxTrim_OnlyTrimsTheColumnsAdjacentToTheEdgesOfTheContainer()
    {
        // https://github.com/w3c/csswg-drafts/issues/11363: "Apply text-box-trim to a column box or spanner iff it is adjacent to
        // the relevant edge". The first row of columns is trimmed on its block-start side, the last one on its block-end side
        var document = Parse("""
            <div id=m style='columns: 2; column-gap: 0; width: 100px; line-height: 2; text-box-trim: trim-both'>
              <span id=a>x</span>
              <div id=s style='column-span: all'>x</div>
              <span id=c>x</span><br><span id=d>x</span>
              <div id=t style='column-span: all'>x</div>
              <span id=e>x</span>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 10, 10), GetBoundingRect(document, "a"));
        Assert.Equal(15, GetBoundingRect(document, "s").Y);

        // The columns between the two spanners are not trimmed
        Assert.Equal(Rect(0, 40, 10, 10), GetBoundingRect(document, "c"));
        Assert.Equal(Rect(50, 40, 10, 10), GetBoundingRect(document, "d"));
        Assert.Equal(55, GetBoundingRect(document, "t").Y);
        Assert.Equal(Rect(0, 80, 10, 10), GetBoundingRect(document, "e"));
        Assert.Equal(90, GetBoundingRect(document, "m").Height);
    }

    [Fact]
    public void ContinueDiscard_DiscardsTheOverflowColumns()
    {
        // https://drafts.csswg.org/css-overflow-4/#valdef-continue-discard: the content after the first region break (the end of
        // the last column) is not rendered: the overflow columns, the spanners and the rows after them
        var document = Parse("""
            <div id=multicol style='width: 300px; columns: 3; gap: 0; continue: discard'>
              <div id=a style='height: 10px; break-after: column'></div>
              <div id=b style='height: 10px; break-after: column'></div>
              <div id=c style='height: 10px; break-after: column'></div>
              <div id=d style='height: 20px'></div>
              <div id=spanner style='column-span: all; height: 10px'></div>
              <div id=e style='height: 10px'></div>
            </div>
            """);
        Assert.Equal(10, GetBoundingRect(document, "multicol").Height);
        Assert.Equal(Rect(200, 0, 100, 10), GetBoundingRect(document, "c"));
        Assert.Empty(GetFragments(document, "d"));
        Assert.Empty(GetFragments(document, "spanner"));
        Assert.Empty(GetFragments(document, "e"));
        Assert.Equal(300, Get(document, "multicol").ScrollWidth);
    }

    [Fact]
    public void ContinueDiscard_BlockEllipsisOnTheLastLineBeforeTheRegionBreak()
    {
        var document = Parse("<div id=multicol style='width: 200px; height: 20px; columns: 2; gap: 0; continue: discard; block-ellipsis: ellipsis'>a<br>b<br>c<br>d<br>e</div>");
        var lines = new List<LineBoxFragment>();
        Collect(GetFragments(document, "multicol")[0]);
        Assert.Equal(4, lines.Count);
        Assert.Null(lines[2].BlockEllipsis);
        Assert.NotNull(lines[3].BlockEllipsis);

        void Collect(Fragment fragment)
        {
            foreach (var child in fragment.Children)
            {
                if (child is LineBoxFragment line)
                {
                    lines.Add(line);
                }
                else
                {
                    Collect(child);
                }
            }
        }
    }

    [Fact]
    public void OverflowColumns_ContributeTheirColumnBoxesToTheScrollableOverflow()
    {
        // As in Blink: the scroll container of a multi-column container whose content overflows in overflow columns can scroll to
        // the end of the last column box, not only to the end of its content
        var document = Parse("""
            <div id=scroller style='width: 200px; overflow: hidden'>
              <div style='width: 200px; height: 10px; columns: 2; gap: 0; column-fill: auto'>
                <div style='width: 10px; height: 30px'></div>
              </div>
            </div>
            """);
        Assert.Equal(300, Get(document, "scroller").ScrollWidth);
    }
}
