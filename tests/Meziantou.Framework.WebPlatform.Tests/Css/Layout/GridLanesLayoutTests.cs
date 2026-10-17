using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The grid lanes layout (https://drafts.csswg.org/css-grid-3/).</summary>
public sealed class GridLanesLayoutTests : IDisposable
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
    public void ColumnLanes_ItemsArePlacedInTheShortestTrack()
    {
        var document = Parse("""
            <div id=g style="display: grid-lanes; width: 300px; grid-template-columns: repeat(3, 1fr); gap: 10px 0; flow-tolerance: 0">
              <div id=a style="height: 50px"></div>
              <div id=b style="height: 20px"></div>
              <div id=c style="height: 30px"></div>
              <div id=d style="height: 10px"></div>
              <div id=e style="height: 10px"></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 50), GetRect(document, "a"));
        Assert.Equal(Rect(100, 0, 100, 20), GetRect(document, "b"));
        Assert.Equal(Rect(200, 0, 100, 30), GetRect(document, "c"));

        // The running positions (with the gap) are 60, 30 and 40: the second track is the shortest, then the third one
        Assert.Equal(Rect(100, 30, 100, 10), GetRect(document, "d"));
        Assert.Equal(Rect(200, 40, 100, 10), GetRect(document, "e"));

        // The block size of the container is the stacking range; the resolved value of the stacking axis is the computed value
        Assert.Equal(Rect(0, 0, 300, 50), GetRect(document, "g"));
        Assert.Equal("none", GetComputed(document, "g", "grid-template-rows"));
        Assert.Equal("100px 100px 100px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void FlowTolerance_TiesAreResolvedByTheCursor()
    {
        // flow-tolerance: normal is 1em (10px): the tracks at 20px and 25px are tied, the item goes after the previous one
        var html = """
            <div style="display: grid-lanes; width: 200px; grid-template-columns: 100px 100px; flow-tolerance: TOLERANCE">
              <div style="height: 25px"></div>
              <div style="height: 20px"></div>
              <div id=c style="height: 10px"></div>
            </div>
            """;
        Assert.Equal(Rect(0, 25, 100, 10), GetRect(Parse(html.Replace("TOLERANCE", "normal", StringComparison.Ordinal)), "c"));
        Assert.Equal(Rect(100, 20, 100, 10), GetRect(Parse(html.Replace("TOLERANCE", "0", StringComparison.Ordinal)), "c"));
    }

    [Fact]
    public void AutoPlacedItems_ContributeToEveryTrack()
    {
        // https://drafts.csswg.org/css-grid-3/#track-sizing: the auto-placed item contributes to both auto tracks, the explicitly
        // placed one only to its track
        var document = Parse("""
            <div id=g style="display: grid-lanes; grid-template-columns: auto auto; justify-content: start">
              <div style="width: 40px; height: 10px"></div>
              <div style="grid-column: 2; width: 70px; height: 10px"></div>
            </div>
            """);
        Assert.Equal("40px 70px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void SpanningItem_IsPlacedAtTheLargestRunningPosition()
    {
        var document = Parse("""
            <div style="display: grid-lanes; width: 300px; grid-template-columns: repeat(3, 100px); flow-tolerance: 0">
              <div style="height: 10px"></div>
              <div style="height: 30px"></div>
              <div id=c style="grid-column: 3; height: 20px"></div>
              <div id=d style="grid-column: span 2; height: 10px"></div>
            </div>
            """);
        Assert.Equal(Rect(200, 0, 100, 20), GetRect(document, "c"));

        // Lines 1-3 give 30px, lines 2-4 give 30px: the first one
        Assert.Equal(Rect(0, 30, 200, 10), GetRect(document, "d"));
    }

    [Fact]
    public void RowLanes_StackInTheInlineAxis()
    {
        var document = Parse("""
            <div id=g style="display: inline-grid-lanes; grid-template-rows: 20px 20px; gap: 0 5px; flow-tolerance: 0">
              <div id=a style="width: 30px"></div>
              <div id=b style="width: 10px"></div>
              <div id=c style="width: 10px"></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 30, 20), GetRect(document, "a"));
        Assert.Equal(Rect(0, 20, 10, 20), GetRect(document, "b"));
        Assert.Equal(Rect(15, 20, 10, 20), GetRect(document, "c"));

        // The inline size of the container is its stacking range
        Assert.Equal(30, GetRect(document, "g").Width);
    }

    [Fact]
    public void DensePacking_FillsTheOpeningsLeftBySpanningItems()
    {
        var document = Parse("""
            <div style="display: grid-lanes; grid-template-columns: repeat(2, 50px); grid-lanes-pack: dense; flow-tolerance: 0">
              <div style="grid-column: 2; height: 40px"></div>
              <div id=b style="grid-column: span 2; height: 10px"></div>
              <div id=c style="height: 20px"></div>
            </div>
            """);
        Assert.Equal(Rect(0, 40, 100, 10), GetRect(document, "b"));

        // The opening of the first track before the spanning item
        Assert.Equal(Rect(0, 0, 50, 20), GetRect(document, "c"));
    }

    [Fact]
    public void FillReverseAndTrackReverse()
    {
        var document = Parse("""
            <div style="display: grid-lanes; width: 100px; height: 100px; grid-template-columns: 50px 50px; grid-lanes-direction: column fill-reverse track-reverse; flow-tolerance: 0">
              <div id=a style="height: 30px"></div>
              <div id=b style="height: 20px"></div>
            </div>
            """);

        // The first item is in the last track, at the end of the stacking axis
        Assert.Equal(Rect(50, 70, 50, 30), GetRect(document, "a"));
        Assert.Equal(Rect(0, 80, 50, 20), GetRect(document, "b"));
    }

    [Fact]
    public void StackingAxisAlignment_StretchesTheLastItems()
    {
        // https://drafts.csswg.org/css-grid-3/#stacking-axis-self-alignment: the alignment container of the last item of a track
        // extends to the end of the stacking range
        var document = Parse("""
            <div style="display: grid-lanes; grid-template-columns: 50px 50px 50px; align-items: stretch; flow-tolerance: 0">
              <div style="height: 60px"></div>
              <div id=b></div>
              <div id=c style="align-self: end; height: 10px"></div>
            </div>
            """);
        Assert.Equal(Rect(50, 0, 50, 60), GetRect(document, "b"));
        Assert.Equal(Rect(100, 50, 50, 10), GetRect(document, "c"));
    }

    [Fact]
    public void AbsolutelyPositionedChild_StackingAxisLinesAreTheStackingRange()
    {
        var document = Parse("""
            <div style="display: grid-lanes; position: relative; grid-template-columns: 50px 50px; padding: 5px">
              <div style="height: 30px"></div>
              <div id=a style="position: absolute; grid-column: 2; grid-row: 1 / 2; top: 0; bottom: 0; width: 10px"></div>
            </div>
            """);
        Assert.Equal(Rect(55, 5, 10, 30), GetRect(document, "a"));
    }

    [Fact]
    public void IntrinsicAutoRepeat_UsesTheHypotheticalSizesOfTheTracks()
    {
        // https://drafts.csswg.org/css-grid-3/#repeat-intrinsic: the auto tracks are 30px (the item), 300px fit 10 repetitions
        var document = Parse("<div id=g style='display: grid-lanes; width: 300px; grid-template-columns: repeat(auto-fill, auto)'><div style='width: 30px; height: 10px'></div></div>");
        Assert.Equal(string.Join(' ', Enumerable.Repeat("30px", 10)), GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void Shorthand_SetsTheTemplateOfTheGridAxisAndTheDirection()
    {
        var document = Parse("<div id=g style='grid-lanes: \"a b\" 10px 20px row'></div>");
        var style = ((HtmlElement)document.GetElementById("g")!).Style;
        Assert.Equal("none", style.GetPropertyValue("grid-template-columns"));
        Assert.Equal("10px 20px", style.GetPropertyValue("grid-template-rows"));
        Assert.Equal("\"a\" \"b\"", style.GetPropertyValue("grid-template-areas"));
        Assert.Equal("row", style.GetPropertyValue("grid-lanes-direction"));
        Assert.Equal("\"a b\" 10px 20px row", style.GetPropertyValue("grid-lanes"));
    }

    [Fact]
    public void AspectRatio_TheContentsAreAlignedInTheSizeGrownToThem()
    {
        // WPT css/css-grid/grid-lanes/alignment/grid-lanes-content-distribution-001.html: the block size transferred through the
        // aspect ratio (25px) grows to the contents (100px), which are centered in that size
        var document = Parse("""
            <div id=g style='display: grid-lanes; grid-template-rows: auto; width: 100px; aspect-ratio: 2; max-width: 50px; align-content: center'>
              <div id=a style='width: 100px; height: 100px'></div>
            </div>
            """);
        Assert.Equal(100, GetRect(document, "g").Height);
        Assert.Equal(0, GetRect(document, "a").Y);
    }

    [Fact]
    public void GridLanesSubgrid_UsesTheTracksOfItsParentGrid()
    {
        // The grid lanes container is a subgrid in its grid axis: its items are placed in the columns 2 and 3 of the grid (60px
        // and 30px after a gap of 10px), with the margin and the padding of the subgrid at its edges
        var document = Parse("""
            <div id=g style="display: inline-grid; grid-template-columns: 40px 60px 30px; column-gap: 10px">
              <div style="height: 10px"></div>
              <div id=s style="display: grid-lanes; grid-template-columns: subgrid; grid-column: 2 / span 2; margin-left: 4px; padding-right: 6px; row-gap: 5px; flow-tolerance: 0">
                <div id=a style="height: 30px"></div>
                <div id=b style="height: 10px"></div>
                <div id=c style="height: 10px"></div>
              </div>
            </div>
            """);
        Assert.Equal(Rect(54, 0, 96, 30), GetRect(document, "s"));
        Assert.Equal(Rect(54, 0, 56, 30), GetRect(document, "a"));
        Assert.Equal(Rect(120, 0, 24, 10), GetRect(document, "b"));

        // The running positions are 35 and 15: the third item goes below the second one
        Assert.Equal(Rect(120, 15, 24, 10), GetRect(document, "c"));
        Assert.Equal("subgrid [] [] []", GetComputed(document, "s", "grid-template-columns"));
        Assert.Equal("none", GetComputed(document, "s", "grid-template-rows"));
    }

    [Fact]
    public void GridLanesSubgrid_AutoPlacedItemsContributeToEveryTrackTheyCouldBePlacedIn()
    {
        // The items of the grid lanes subgrid have an automatic position: each one sizes the two columns the subgrid spans, with
        // the largest extra margin of the edges of the subgrid (its padding of 8px at the start edge)
        var document = Parse("""
            <div id=g style="display: inline-grid; grid-template-columns: auto auto auto">
              <div id=one style="width: 10px; height: 10px"></div>
              <div id=s style="display: grid-lanes; grid-template-columns: subgrid; grid-column: 2 / span 2; padding-left: 8px">
                <div id=a style="width: 30px; height: 10px"></div>
                <div id=b style="width: 20px; height: 10px"></div>
              </div>
            </div>
            """);
        Assert.Equal("10px 38px 38px", GetComputed(document, "g", "grid-template-columns"));
        Assert.Equal(Rect(18, 0, 30, 10), GetRect(document, "a"));
        Assert.Equal(Rect(48, 0, 20, 10), GetRect(document, "b"));
    }

    [Fact]
    public void GridLanesSubgrid_IsStandaloneWithContainment()
    {
        // A grid lanes container that establishes an independent formatting context is not a subgrid: `subgrid` is `none`
        var document = Parse("""
            <div style="display: inline-grid; grid-template-columns: 40px 60px">
              <div id=s style="display: grid-lanes; grid-template-columns: subgrid; grid-column: 1 / span 2; contain: layout">
                <div id=a style="height: 10px"></div>
                <div id=b style="height: 10px"></div>
              </div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 10), GetRect(document, "a"));
        Assert.Equal(Rect(0, 10, 100, 10), GetRect(document, "b"));
    }

    [Fact]
    public void AutoPlacedSubgrid_ItsTracksContributeTheirGutters()
    {
        // The empty subgrid has an automatic position: the largest contribution of its tracks (half the difference of the gutters
        // on both sides of its middle track: (50 - 10) / 2 * 2) sizes every track of the grid lanes container
        var document = Parse("""
            <div id=g style="display: inline-grid-lanes; grid-template-columns: auto auto auto; column-gap: 10px">
              <div style="display: grid; grid-template-columns: subgrid; grid-column: span 3; column-gap: 50px; min-height: 10px"></div>
            </div>
            """);
        Assert.Equal("40px 40px 40px", GetComputed(document, "g", "grid-template-columns"));
    }

    [Fact]
    public void AspectRatioItem_TransfersTheStretchFitBlockSizeToItsColumn()
    {
        // The item has a preferred aspect ratio and a normal alignment: its block size is not stretched, but the size of its
        // row is transferred through the ratio to its contribution to the column, whose size is not known yet
        var document = Parse("""
            <div id=g style="display: inline-grid; grid-template-rows: 100px">
              <div id=a style="aspect-ratio: 1"></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 100), GetRect(document, "g"));
        Assert.Equal(Rect(0, 0, 100, 100), GetRect(document, "a"));
    }

    [Fact]
    public void ColumnLanes_TheFirstBaselineOfATrackIsTheOneOfItsFirstItemWithABaseline()
    {
        // The first item of the first track has no baseline: the baseline of the track is the one of its second item (its text at
        // 20px, baseline at 28px), which is higher than the baseline of the first item of the second track (38px): the text
        // beside the container (baseline at 8px) is moved down by 20px
        var document = Parse("""
            <div style='display: flex; align-items: baseline'>
              <div id=t>x</div>
              <div style='display: inline-grid-lanes; grid-template-columns: 50px 50px; flow-tolerance: 0'>
                <div style='height: 20px'></div>
                <div style='padding-top: 30px'>b</div>
                <div>a</div>
              </div>
            </div>
            """);
        Assert.Equal(20, GetRect(document, "t").Y);
    }

    [Fact]
    public void RowLanes_TheIntrinsicInlineSizeLaysOutTheItemsInInfiniteRows()
    {
        // The inline size is found before the rows are sized: the orthogonal item is laid out in an infinite row (one line, 10px
        // wide); the min-content row (10px) then wraps it on two lines (20px wide), which overflow the container
        var document = Parse("""
            <div id=g style='display: inline-grid-lanes; grid-template-rows: min-content; height: min-content'>
              <div id=a style='writing-mode: vertical-rl'>X X</div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 10, 10), GetRect(document, "g"));
        Assert.Equal(Rect(0, 0, 20, 10), GetRect(document, "a"));
    }

    [Fact]
    public void RowLanes_TheIntrinsicInlineSizeUsesTheRepetitionsOfTheMaximumBlockSize()
    {
        // repeat(auto-fill) gives 3 rows in the 35px of max-height (https://drafts.csswg.org/css-grid-2/#auto-repeat), also when
        // the inline size of the container is computed: the 6 items are placed in 2 columns
        var document = Parse("""
            <div id=g style='display: inline-grid-lanes; max-height: 35px; grid-template-rows: repeat(auto-fill, 10px)'>
              <div style='width: 10px'></div><div style='width: 10px'></div><div style='width: 10px'></div>
              <div style='width: 10px'></div><div style='width: 10px'></div><div style='width: 10px'></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 20, 30), GetRect(document, "g"));
    }

    [Fact]
    public void TrackReverse_TheTracksThatOverflowTheStartSideAreScrollable()
    {
        // The columns (120px) overflow the start side of the scroll container (its scroll origin is on the end side): the items
        // aligned at the end of their columns do not reach the start of the first column, which is in the scrollable overflow
        var document = Parse("""
            <div id=g style='display: grid-lanes; width: 100px; height: 50px; overflow: hidden; grid-template-columns: 40px 40px 40px;
                             grid-lanes-direction: column track-reverse; justify-items: end'>
              <div style='width: 10px; height: 10px'></div><div style='width: 10px; height: 10px'></div><div style='width: 10px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(120, document.GetElementById("g")!.ScrollWidth);
    }
}
