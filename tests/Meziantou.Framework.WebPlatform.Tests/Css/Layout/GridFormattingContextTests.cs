using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The grid layout (https://drafts.csswg.org/css-grid-2/).</summary>
public sealed class GridFormattingContextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static string GetComputed(Document document, string id, string property) => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).GetPropertyValue(property);

    [Fact]
    public void FixedAndFlexibleTracks()
    {
        var document = Parse("<div id=g style='display: grid; width: 400px; grid-template-columns: 100px 1fr 2fr; grid-template-rows: 30px'><div id=a></div><div id=b></div><div id=c></div></div>");
        Assert.Equal(Rect(0, 0, 100, 30), GetRect(document, "a"));
        Assert.Equal(Rect(100, 0, 100, 30), GetRect(document, "b"));
        Assert.Equal(Rect(200, 0, 200, 30), GetRect(document, "c"));
        Assert.Equal("100px 100px 200px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void AutoTracks_AreSizedByTheirItemsAndStretched()
    {
        // The auto tracks get the max-content contributions of the items, then share the free space (justify-content: normal)
        var document = Parse("<div id=g style='display: grid; width: 200px; grid-template-columns: auto auto'><div id=a>XX</div><div id=b>XXXX</div></div>");
        Assert.Equal("90px 110px", GetComputed(document, "g", "grid-template-columns"));
        Assert.Equal(Rect(90, 0, 110, 10), GetRect(document, "b"));
    }

    [Fact]
    public void IntrinsicTracks_SpanningItems()
    {
        // https://drafts.csswg.org/css-grid-2/#algo-spanning-items: the item spanning both min-content tracks distributes its
        // min-content contribution equally
        var document = Parse("<div id=g style='display: grid; grid-template-columns: min-content min-content; justify-content: start'><div style='grid-column: span 2'>XXXX XXXX</div></div>");
        Assert.Equal("20px 20px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void FitContentTrack_IsClampedByItsArgument()
    {
        var document = Parse("<div id=g style='display: grid; width: 300px; grid-template-columns: fit-content(50px) 1fr'><div>XXX XXX XXX</div><div></div></div>");
        Assert.Equal("50px 250px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void Gaps_AreBetweenTheTracks()
    {
        var document = Parse("<div style='display: grid; width: 230px; grid-template-columns: 100px 100px; grid-auto-rows: 20px; gap: 5px 30px'><div></div><div></div><div id=c></div></div>");
        Assert.Equal(Rect(0, 25, 100, 20), GetRect(document, "c"));
    }

    [Fact]
    public void Placement_LineNumbersNamesAndAreas()
    {
        var document = Parse("""
            <div style='display: grid; grid-template-columns: [a] 10px [b] 20px [c] 30px; grid-template-rows: 10px 10px; grid-template-areas: "x x y" "z z z"'>
              <div id=n style='grid-column: -2; grid-row: 1'></div>
              <div id=named style='grid-column: b / c; grid-row: 2'></div>
              <div id=area style='grid-area: y'></div>
            </div>
            """);
        Assert.Equal(Rect(30, 0, 30, 10), GetRect(document, "n"));
        Assert.Equal(Rect(10, 10, 20, 10), GetRect(document, "named"));
        Assert.Equal(Rect(30, 0, 30, 10), GetRect(document, "area"));
    }

    [Fact]
    public void AutoPlacement_SparseAndDense()
    {
        const string Items = "<div id=a style='grid-column: span 2'></div><div id=b style='grid-column: span 3'></div><div id=c></div>";
        var sparse = Parse("<div style='display: grid; grid-template-columns: repeat(3, 10px); grid-auto-rows: 10px'>" + Items + "</div>");
        Assert.Equal(Rect(0, 20, 10, 10), GetRect(sparse, "c"));

        var dense = Parse("<div style='display: grid; grid-template-columns: repeat(3, 10px); grid-auto-rows: 10px; grid-auto-flow: dense'>" + Items + "</div>");
        Assert.Equal(Rect(20, 0, 10, 10), GetRect(dense, "c"));
    }

    [Fact]
    public void ImplicitTracks_BeforeTheExplicitGrid()
    {
        // A negative line beyond the explicit grid creates implicit tracks before it, sized by grid-auto-columns
        var document = Parse("<div id=g style='display: grid; grid-template-columns: 50px; grid-auto-columns: 20px; justify-content: start'><div id=a style='grid-column: -3'></div><div id=b style='grid-column: 1'></div></div>");
        Assert.Equal(Rect(0, 0, 20, 0), GetRect(document, "a"));
        Assert.Equal(Rect(20, 0, 50, 0), GetRect(document, "b"));
        Assert.Equal("20px 50px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void AutoFill_And_AutoFit()
    {
        var fill = Parse("<div id=g style='display: grid; width: 230px; grid-template-columns: repeat(auto-fill, 50px); gap: 10px'><div></div></div>");
        Assert.Equal("50px 50px 50px 50px", GetComputed(fill, "g", "grid-template-columns"));

        var fit = Parse("<div id=g style='display: grid; width: 230px; grid-template-columns: repeat(auto-fit, 50px); gap: 10px'><div></div></div>");
        Assert.Equal("50px 0px 0px 0px", GetComputed(fit, "g", "grid-template-columns"));
    }

    [Fact]
    public void SelfAlignment_AndAutoMargins()
    {
        var document = Parse("""
            <div style='display: grid; grid-template-columns: 100px; grid-auto-rows: 50px'>
              <div id=a style='justify-self: end; align-self: center'>XX</div>
              <div id=b style='margin: auto'>XX</div>
              <div id=c>XX</div>
            </div>
            """);
        Assert.Equal(Rect(80, 20, 20, 10), GetRect(document, "a"));
        Assert.Equal(Rect(40, 70, 20, 10), GetRect(document, "b"));
        Assert.Equal(Rect(0, 100, 100, 50), GetRect(document, "c"));
    }

    [Fact]
    public void ContentAlignment_DistributesTheFreeSpace()
    {
        var document = Parse("<div style='display: grid; width: 100px; height: 100px; grid-template: 20px 20px / 20px 20px; justify-content: space-between; align-content: center'><div></div><div id=b></div></div>");
        Assert.Equal(Rect(80, 30, 20, 20), GetRect(document, "b"));
    }

    [Fact]
    public void BaselineAlignment_InARow()
    {
        var document = Parse("<div style='display: grid; grid-template-columns: 50px 50px; align-items: baseline'><div id=a style='font-size: 20px'>X</div><div id=b>X</div></div>");

        // Ahem: the baseline is at 0.8em
        Assert.Equal(0, GetRect(document, "a").Y);
        Assert.Equal(8, GetRect(document, "b").Y);
    }

    [Fact]
    public void IntrinsicSize_OfAnInlineGrid()
    {
        var document = Parse("<div id=g style='display: inline-grid; grid-template-columns: auto 30px; column-gap: 5px'><div>XXX X</div></div>");
        Assert.Equal(85, GetRect(document, "g").Width);
    }

    [Fact]
    public void PercentageRows_IndefiniteHeight()
    {
        // The percentages are treated as auto to find the height of the grid, then resolved against it
        var document = Parse("<div id=g style='display: grid; grid-template-rows: 50%'><div>X<br>X</div></div>");
        Assert.Equal(20, GetRect(document, "g").Height);
        Assert.Equal("10px", GetComputed(document, "g", "grid-template-rows"));
    }

    [Fact]
    public void AbsolutelyPositioned_GridAreaIsTheContainingBlock()
    {
        var document = Parse("""
            <div style='display: grid; position: relative; width: 300px; height: 100px; padding: 10px; grid-template-columns: 100px 100px 100px; grid-template-rows: 100px'>
              <div id=a style='position: absolute; grid-column: 2 / 3; inset: 0'></div>
              <div id=b style='position: absolute; grid-column: 3; inset: 0 0 auto 0; height: 10px'></div>
              <div id=c style='position: absolute; grid-column: auto / 2; inset: 0'></div>
            </div>
            """);
        // The auto lines are the padding edges of the container
        Assert.Equal(Rect(110, 0, 100, 120), GetRect(document, "a"));
        Assert.Equal(Rect(210, 0, 110, 10), GetRect(document, "b"));
        Assert.Equal(Rect(0, 0, 110, 120), GetRect(document, "c"));
    }

    [Fact]
    public void SizeContainment_SizesTheTracksWithoutTheItems()
    {
        // https://drafts.csswg.org/css-contain-2/#containment-size: "as if the element had no content", but the explicit tracks remain
        var document = Parse("<div id=g style='display: inline-grid; contain: size; grid-template: 20px 15px / 70px 30px; gap: 10px'><div>XXXXXXXXXXXXXXXXXXXX</div><div></div><div></div><div></div><div></div></div>");
        Assert.Equal(Rect(0, 0, 110, 45), GetRect(document, "g"));
    }

    [Fact]
    public void Order_ChangesThePlacementAndThePaintingOrder()
    {
        var document = Parse("<div style='display: grid; grid-template-columns: 10px 10px'><div id=a style='order: 2'></div><div id=b></div></div>");
        Assert.Equal(10, GetRect(document, "a").X);
        Assert.Equal(0, GetRect(document, "b").X);
    }

    [Fact]
    public void Subgrid_ItemsSizeTheParentTracks()
    {
        var document = Parse("""
            <div id=g style='display: grid; grid-template-columns: auto auto; justify-content: start'>
              <div style='grid-column: span 2; display: grid; grid-template-columns: subgrid; padding-left: 5px'>
                <div id=a>XXX</div><div id=b>X</div>
              </div>
              <div id=c>X</div><div id=d>XXXXX</div>
            </div>
            """);

        // The first column fits the 30px item of the subgrid plus the padding of the subgrid (an extra margin of the item at its
        // start edge); the second column fits the 50px item of the parent
        Assert.Equal("35px 50px", GetComputed(document, "g", "grid-template-columns"));
        Assert.Equal(Rect(5, 0, 30, 10), GetRect(document, "a"));
        Assert.Equal(Rect(35, 0, 50, 10), GetRect(document, "b"));
        Assert.Equal(Rect(35, 10, 50, 10), GetRect(document, "d"));
    }

    [Fact]
    public void Subgrid_ResolvedTrackList()
    {
        var document = Parse("<div style='display: grid; grid-template-columns: [x] 10px 10px 10px'><div id=s style='grid-column: span 3; display: grid; grid-template-columns: subgrid [] [a]'></div></div>");
        Assert.Equal("subgrid [] [a] [] []", GetComputed(document, "s", "grid-template-columns"));
    }

    [Fact]
    public void Subgrid_InheritsTheLineNames()
    {
        var document = Parse("""
            <div style='display: grid; grid-template-columns: [a] 10px [b] 10px [c] 10px'>
              <div style='grid-column: 2 / 4; display: grid; grid-template-columns: subgrid'>
                <div id=i style='grid-column: b / c'></div>
              </div>
            </div>
            """);
        Assert.Equal(10, GetRect(document, "i").X);
        Assert.Equal(10, GetRect(document, "i").Width);
    }

    [Fact]
    public void Subgrid_EmptyEdgeTracksGetTheMarginsBordersAndPaddingsOfTheSubgrid()
    {
        // https://drafts.csswg.org/css-grid-2/#subgrid-edge-placeholders: WPT css/css-grid/subgrid/subgrid-no-items-on-edges-001.html
        // (an empty subgrid), css/css-grid/subgrid/grid-gap-002.html (the edge track of a subgrid whose items do not reach its edge)
        var document = Parse("""
            <div id=g style='display: inline-grid; grid-template-columns: repeat(3, auto)'>
              <div style='display: grid; grid-column: 1 / -1; grid-template-columns: subgrid; padding: 0 7px 0 13px'></div>
            </div>
            <div id=h style='display: inline-grid; grid-template-columns: repeat(3, auto)'>
              <div style='display: grid; grid-column: 1 / -1; grid-template-columns: subgrid; padding-right: 40px'>
                <div id=i style='width: 20px; height: 10px'></div>
              </div>
            </div>
            """);
        Assert.Equal(20, GetRect(document, "g").Width);
        Assert.Equal(60, GetRect(document, "h").Width);
        Assert.Equal(20, GetRect(document, "i").Width);
    }

    [Fact]
    public void Subgrid_EachEdgeTakesSpaceInItsEdgeTrack()
    {
        // WPT css/css-grid/subgrid/orthogonal-writing-mode-002.html: the edges of an empty subgrid are not distributed over the
        // tracks it spans
        var document = Parse("""
            <div style='display: inline-grid; grid-template-rows: repeat(3, auto); grid-template-columns: 50px'>
              <div id=a style='grid-area: 1 / 1'></div><div id=b style='grid-area: 2 / 1'></div><div id=c style='grid-area: 3 / 1'></div>
              <div style='display: grid; grid-area: 1 / 1 / -1 / 1; grid-template-rows: subgrid; padding: 4px 0 30px'></div>
            </div>
            """);
        Assert.Equal(4, GetRect(document, "a").Height);
        Assert.Equal(0, GetRect(document, "b").Height);
        Assert.Equal(30, GetRect(document, "c").Height);
    }

    [Fact]
    public void Subgrid_EdgeTrackIsNotSmallerThanTheEdgeWhenAnItemHasANegativeExtraMargin()
    {
        // WPT css/css-grid/subgrid/item-percentage-height-001.html: the gutters of the subgrid are 20px smaller than the ones of
        // its parent (an extra margin of -10px for its items beside the gutter): the first row still contains the 5px padding
        // of the subgrid, so the rows are 5px and 100px before the remaining 135px are shared
        var document = Parse("""
            <div style='display: grid; grid: auto auto / auto; row-gap: 60px; height: 300px'>
              <div style='display: grid; grid: subgrid / auto; grid-row: span 2; row-gap: 40px; padding: 5px 10px 10px'>
                <div id=a style='align-self: start; height: 100%; width: 100px'></div>
                <div id=b style='align-self: start; height: 100px; width: 100px'></div>
              </div>
            </div>
            """);
        Assert.Equal(Rect(10, 5, 100, 77.5), GetRect(document, "a"));
        Assert.Equal(Rect(10, 122.5, 100, 100), GetRect(document, "b"));
    }

    [Fact]
    public void Subgrid_EdgeLinesAreTheContentEdgesForTheAbsolutelyPositionedBoxes()
    {
        // WPT css/css-grid/subgrid/abs-pos-002.html
        var document = Parse("""
            <div style='display: grid; grid-template-columns: repeat(4, 30px)'>
              <div style='position: relative; display: grid; grid-column: 1 / -1; grid-template-columns: subgrid; padding: 0 20px 0 10px; height: 20px'>
                <div id=a style='position: absolute; inset: 0; grid-column: 1 / -1'></div>
                <div id=b style='position: absolute; inset: 0; grid-column: 2 / 3'></div>
                <div id=c style='position: absolute; inset: 0; grid-column: auto / 1'></div>
              </div>
            </div>
            """);
        Assert.Equal(Rect(10, 0, 90, 20), GetRect(document, "a"));
        Assert.Equal(Rect(30, 0, 30, 20), GetRect(document, "b"));
        Assert.Equal(Rect(0, 0, 10, 20), GetRect(document, "c"));
    }

    [Fact]
    public void Subgrid_ItemsAreMeasuredWithTheStandaloneAxisSizedFirst()
    {
        // WPT css/css-grid/subgrid/orthogonal-writing-mode-001.html: the standalone columns of the vertical subgrid (the block
        // axis of the grid) are sized as if its grid area were infinite before its item is measured for the columns of the grid:
        // the text is on one line. WPT css/css-grid/subgrid/standalone-axis-size-011.html: the rows of the subgrid stretched in a
        // fixed row are known, its orthogonal item wraps its text in them
        var document = Parse("""
            <div id=g style='display: inline-grid; grid-template-columns: auto auto'>
              <div style='writing-mode: vertical-rl; display: grid; grid-template-rows: subgrid; grid-column: 1 / -1'>A B</div>
            </div>
            <div id=h style='display: inline-grid; grid-template: 20px / auto'>
              <div style='display: grid; grid-template-columns: subgrid'>
                <div style='writing-mode: vertical-rl'>A B C D</div>
              </div>
            </div>
            """);
        Assert.Equal(10, GetRect(document, "g").Width);
        Assert.Equal(40, GetRect(document, "h").Width);
    }

    [Fact]
    public void ScrollableOverflow_IncludesTheGridAndThePaddingAfterIt()
    {
        // https://drafts.csswg.org/css-grid-2/#overflow: "the grid is also included in a grid container's scrollable overflow
        // region" (WPT css/css-grid/grid-overflow-001.html): the 140px of columns, between the 10px paddings
        var document = Parse("<div id=g style='display: grid; overflow: hidden; width: 100px; height: 100px; padding: 10px; grid-template: 110px 10px / 120px 20px'><div></div></div>");
        var grid = document.GetElementById("g")!;
        Assert.Equal(160, grid.ScrollWidth);
        Assert.Equal(140, grid.ScrollHeight);
    }

    [Fact]
    public void ResolvedValue_GridTemplateUsesTheComputedTrackLists()
    {
        // WPT css/css-grid/grid-layout-properties.html
        var document = Parse("""
            <div id=g style='display: grid; width: 150px; grid-template: [a] auto [b] auto [c] / [d] auto [e] auto [f]'><div>X</div></div>
            <div id=h style='display: grid; width: 150px; grid-template: "a b" 100px / 50px'><div>X</div></div>
            <div id=i style='display: grid; width: 150px'><div>X</div></div>
            """);
        Assert.Equal("[a] auto [b] auto [c] / [d] auto [e] auto [f]", GetComputed(document, "g", "grid-template"));
        Assert.Equal("\"a b\" 100px / 50px", GetComputed(document, "h", "grid-template"));
        Assert.Equal("10px / 150px", GetComputed(document, "i", "grid-template"));
    }

    [Fact]
    public void Subgrid_NormalGapIsTheGapOfTheParentSubgrid()
    {
        // WPT css/css-grid/subgrid/subgrid-no-items-on-edges-002.html: the gap of a nested subgrid is the one of its parent subgrid,
        // which the items get as extra margins (the tracks are the ones of the root grid, without gap)
        var document = Parse("""
            <div id=g style='display: inline-grid; grid-template-columns: repeat(2, auto)'>
              <div style='display: grid; grid-column: span 2; grid-template-columns: subgrid; column-gap: 10px'>
                <div style='display: grid; grid-column: span 2; grid-template-columns: subgrid'>
                  <div style='width: 20px; height: 10px'></div>
                  <div id=b style='width: 20px; height: 10px'></div>
                </div>
              </div>
            </div>
            """);
        Assert.Equal(50, GetRect(document, "g").Width);
        Assert.Equal(30, GetRect(document, "b").X);
    }

    [Fact]
    public void Subgrid_DefinitePlacementsAreClampedBeforeTheAutoPlacement()
    {
        // WPT css/css-grid/subgrid/repeat-auto-fill-001.html: an item placed after the last line of a subgridded axis is clamped
        // to its last track, and the automatic placement of the rows sees it there
        var document = Parse("""
            <div style='display: grid; grid-template-columns: repeat(4, 20px)'>
              <div style='display: grid; grid-column: 1 / span 2; grid-template-columns: subgrid; grid-auto-rows: 10px'>
                <div id=a style='grid-column: 2'></div>
                <div id=b style='grid-column: 5'></div>
              </div>
            </div>
            """);
        Assert.Equal(Rect(20, 0, 20, 10), GetRect(document, "a"));
        Assert.Equal(Rect(20, 10, 20, 10), GetRect(document, "b"));
    }

    [Fact]
    public void Subgrid_SizeContainerIsNotASubgrid()
    {
        // WPT css/css-grid/subgrid/independent-formatting-context.html
        var document = Parse("""
            <div style='display: grid; grid-template-rows: 0px 100px; width: 100px'>
              <div style='display: grid; grid-template-rows: subgrid; grid-row: 1 / 3; container-type: inline-size'>
                <div id=a style='grid-row: 1; height: 100px'></div>
              </div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "a").Height);
    }

    [Theory]
    [InlineData("aspect-ratio: 1 / 1; min-height: 60px", 100)]
    [InlineData("min-width: 60px", 100)]
    [InlineData("max-width: 120px; width: max-content", 100)]
    public void AutoRepeat_IntrinsicSizeUsesTheMinimumAndMaximumSizes(string style, double width)
    {
        // https://drafts.csswg.org/css-grid-2/#auto-repeat: WPT css/css-grid/grid-definition/grid-auto-repeat-aspect-ratio-001.html,
        // grid-auto-repeat-dynamic-001.html, grid-auto-repeat-max-size-002.html
        var document = Parse($"<div id=g style='display: inline-grid; grid-template-columns: repeat(auto-fill, 50px); {style}'></div>");
        Assert.Equal(width, GetRect(document, "g").Width);
    }

    [Fact]
    public void FlexibleRows_FractionalFlexSumIsResolvedAgainInTheResultingBlockSize()
    {
        // WPT css/css-grid/layout-algorithm/flex-sizing-rows-indefinite-height-002.html
        var document = Parse("""
            <div id=g style='display: grid; width: 100px; grid-template-rows: repeat(2, minmax(0, 0.4fr))'>
              <div id=a style='grid-row: 2; height: 100px'></div>
            </div>
            """);
        Assert.Equal(80, GetRect(document, "g").Height);
        Assert.Equal(32, GetRect(document, "a").Y);
    }

    [Fact]
    public void Rows_IgnoreTheMaximumBlockSizeOfTheContainer()
    {
        // WPT css/css-grid/layout-algorithm/grid-track-ignores-max-size-001.html
        var document = Parse("<div id=g style='display: grid; width: 100px; max-height: 10px'><div id=a style='min-height: 0'><div style='height: 100px'></div></div></div>");
        Assert.Equal(10, GetRect(document, "g").Height);
        Assert.Equal(100, GetRect(document, "a").Height);
    }

    [Fact]
    public void ColumnContributions_UseTheStretchedBlockSizeOfTheItems()
    {
        // WPT css/css-grid/grid-items/grid-item-inline-contribution-003.html: the columns are sized again when the contribution of
        // an item changed with the sizes of the rows (a replaced element with a percentage block size in a stretched item)
        var document = Parse("""
            <div style='display: grid; width: 0; grid-template: auto / auto auto'>
              <div id=a><canvas width=10 height=10 style='height: 100%; display: block'></canvas></div>
              <div><div style='height: 100px'></div></div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "a").Width);
    }

    [Fact]
    public void ReplacedItem_NormalAlignmentDoesNotStretch()
    {
        // WPT css/css-grid/alignment/grid-item-no-aspect-ratio-stretch-1.html: a replaced item without natural size has the default object size
        var document = Parse("""
            <div style='display: grid; width: 350px; height: 250px'><svg id=a></svg></div>
            <div style='display: grid; width: 350px; height: 250px'><svg id=b style='align-self: stretch; justify-self: stretch'></svg></div>
            """);
        Assert.Equal(Rect(0, 0, 300, 150), GetRect(document, "a"));
        Assert.Equal(Rect(0, 250, 350, 250), GetRect(document, "b"));
    }

    [Fact]
    public void AbsolutelyPositionedChild_LineOneOfAGridWithoutTrack()
    {
        // WPT css/css-grid/abspos/positioned-grid-items-022.html: a grid without track has one line, at its content edge
        var document = Parse("""
            <div style='display: grid; position: relative; width: 100px; height: 100px; padding: 5px'>
              <div id=a style='position: absolute; inset: 0; grid-area: 1 / 1 / auto / auto'></div>
              <div id=b style='position: absolute; inset: 0; grid-area: auto / auto / 1 / 1'></div>
            </div>
            """);
        Assert.Equal(Rect(5, 5, 105, 105), GetRect(document, "a"));
        Assert.Equal(Rect(0, 0, 5, 5), GetRect(document, "b"));
    }

    [Fact]
    public void FlexibleSpanningItem_MinContentSizeKeepsTheMinimumContribution()
    {
        // WPT css/css-grid/grid-flex-spanning-items-001.html
        var document = Parse("""
            <div id=g style='display: grid; grid-template-columns: 1fr 30px; width: min-content'>
              <div style='grid-column: 1 / span 2'><div style='width: 300px; height: 50px'></div></div>
            </div>
            """);
        Assert.Equal(30, GetRect(document, "g").Width);
    }

    [Fact]
    public void FitContentTrack_GrowsBeyondItsArgumentForASpanningItem()
    {
        // The spanning item needs 120px in the fit-content() row beside the fixed row: the row reaches its argument (100px), is then
        // treated as fixed, and as the only affected track it gets the rest (https://drafts.csswg.org/css-grid-2/#extra-space)
        var document = Parse("""
            <div id=g style='display: grid; grid-template-rows: fit-content(100px) 80px; grid-template-columns: 50px 50px'>
              <div style='height: 15px'></div>
              <div style='grid-row: 1 / span 2; height: 200px'></div>
            </div>
            """);
        Assert.Equal("120px 80px", GetComputed(document, "g", "grid-template-rows"));
    }
}
