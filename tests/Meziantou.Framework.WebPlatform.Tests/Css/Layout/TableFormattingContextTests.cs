using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The table layout (https://drafts.csswg.org/css-tables-3/).</summary>
public sealed class TableFormattingContextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport (no-quirks unless a doctype is given).</summary>
    private Document Parse(string html, string doctype = "<!DOCTYPE html>")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => doctype + "<style>body { margin: 0; font: 10px/1 Ahem } td { padding: 0 } table { border-spacing: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static string GetComputed(Document document, string id, string property) => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).GetPropertyValue(property);

    private static int OffsetWidth(Document document, string id) => ((HtmlElement)document.GetElementById(id)!).OffsetWidth;

    private static int OffsetHeight(Document document, string id) => ((HtmlElement)document.GetElementById(id)!).OffsetHeight;

    [Fact]
    public void AutoLayout_ColumnsGetTheirMaxContentWidth()
    {
        var document = Parse("<table id=t style='border-spacing: 5px'><tr><td id=a>XX</td><td id=b>XXXX</td></tr></table>");
        Assert.Equal(Rect(5, 5, 20, 10), GetRect(document, "a"));
        Assert.Equal(Rect(30, 5, 40, 10), GetRect(document, "b"));
        Assert.Equal(75, OffsetWidth(document, "t"));
        Assert.Equal(20, OffsetHeight(document, "t"));
    }

    [Fact]
    public void AutoLayout_NarrowTable_ColumnsShrinkToTheirMinContentWidth()
    {
        // https://drafts.csswg.org/css-tables-3/#distribution-algorithm: between the min-content and the max-content guesses, the
        // columns grow in proportion to the difference between their max-content and min-content widths
        var document = Parse("<table style='width: 120px'><tr><td id=a>XX XX XX</td><td id=b>XXXXXXXX XX</td></tr></table>");
        var a = GetRect(document, "a");
        var b = GetRect(document, "b");
        Assert.Equal(120, a.Width + b.Width);
        Assert.InRange(a.Width, 20, 40);
        Assert.InRange(b.Width, 80, 100);
    }

    [Fact]
    public void AutoLayout_ExtraWidthGoesToTheAutoColumns()
    {
        var document = Parse("<table style='width: 300px'><tr><td id=a style='width: 100px'>X</td><td id=b>X</td><td id=c>XXX</td></tr></table>");
        Assert.Equal(100, GetRect(document, "a").Width);

        // The auto columns grow in proportion to their max-content widths
        Assert.Equal(50, GetRect(document, "b").Width);
        Assert.Equal(150, GetRect(document, "c").Width);
    }

    [Fact]
    public void AutoLayout_PercentageColumns()
    {
        var document = Parse("<table style='width: 200px'><tr><td id=a style='width: 25%'>X</td><td id=b>X</td></tr></table>");
        Assert.Equal(50, GetRect(document, "a").Width);
        Assert.Equal(150, GetRect(document, "b").Width);
    }

    [Fact]
    public void FixedLayout_UsesTheColumnsAndTheFirstRow()
    {
        var document = Parse("""
            <table style='table-layout: fixed; width: 300px'>
              <col style='width: 50px'>
              <tr><td id=a>XXXXXXXXXXXX</td><td id=b style='width: 100px'></td><td id=c></td><td id=d></td></tr>
              <tr><td>X</td><td style='width: 10px'></td><td></td><td>XXXXXXXXXXXXXXXXXXXXX</td></tr>
            </table>
            """);
        Assert.Equal(50, GetRect(document, "a").Width);
        Assert.Equal(100, GetRect(document, "b").Width);
        Assert.Equal(75, GetRect(document, "c").Width);
        Assert.Equal(75, GetRect(document, "d").Width);
    }

    [Fact]
    public void ColumnAndRowSpans()
    {
        var document = Parse("""
            <table>
              <tr><td id=a colspan=2>XXXXXX</td><td id=b rowspan=2>X</td></tr>
              <tr><td id=c>XX</td><td id=d>XX</td></tr>
            </table>
            """);
        Assert.Equal(Rect(0, 0, 60, 10), GetRect(document, "a"));
        Assert.Equal(Rect(60, 0, 10, 20), GetRect(document, "b"));
        Assert.Equal(Rect(0, 10, 30, 10), GetRect(document, "c"));
        Assert.Equal(Rect(30, 10, 30, 10), GetRect(document, "d"));
    }

    [Fact]
    public void RowSpanZero_SpansTheRestOfTheRowGroup()
    {
        var document = Parse("""
            <table>
              <tbody>
                <tr><td id=a rowspan=0>X</td><td>X</td></tr>
                <tr><td>X</td></tr>
                <tr><td>X</td></tr>
              </tbody>
              <tbody><tr><td id=b>X</td><td>X</td></tr></tbody>
            </table>
            """);
        Assert.Equal(Rect(0, 0, 10, 30), GetRect(document, "a"));
        Assert.Equal(Rect(0, 30, 10, 10), GetRect(document, "b"));
    }

    [Fact]
    public void HeaderAndFooterGroupsAreReordered()
    {
        var document = Parse("<table><tfoot id=f><tr><td>F</td></tr></tfoot><tbody id=b><tr><td>B</td></tr></tbody><thead id=h><tr><td>H</td></tr></thead></table>");
        Assert.Equal(0, GetRect(document, "h").Y);
        Assert.Equal(10, GetRect(document, "b").Y);
        Assert.Equal(20, GetRect(document, "f").Y);
    }

    [Fact]
    public void VerticalAlign_InCells()
    {
        var document = Parse("""
            <table>
              <tr style='height: 50px'>
                <td id=top style='vertical-align: top'><span id=t>X</span></td>
                <td id=middle style='vertical-align: middle'><span id=m>X</span></td>
                <td style='vertical-align: bottom'><span id=b>X</span></td>
                <td style='vertical-align: baseline; font-size: 20px'><span id=big>X</span></td>
                <td style='vertical-align: baseline'><span id=small>X</span></td>
              </tr>
            </table>
            """);
        Assert.Equal(0, GetRect(document, "t").Y);
        Assert.Equal(20, GetRect(document, "m").Y);
        Assert.Equal(40, GetRect(document, "b").Y);

        // The baselines of the baseline-aligned cells are aligned (Ahem: the ascent is 0.8em)
        Assert.Equal(GetRect(document, "big").Y + 16, GetRect(document, "small").Y + 8);
    }

    [Fact]
    public void TableHeight_IsDistributedToTheRows()
    {
        var document = Parse("<table style='height: 100px'><tr id=a><td>X</td></tr><tr id=b><td>XX<br>XX<br>XX</td></tr></table>");

        // The rows grow in proportion to their heights
        Assert.Equal(25, GetRect(document, "a").Height);
        Assert.Equal(75, GetRect(document, "b").Height);
    }

    [Fact]
    public void CollapsingBorders_AreHalvedAndTheWidestWins()
    {
        var document = Parse("""
            <table id=t style='border-collapse: collapse; border: 4px solid'>
              <tr><td id=a style='border: 10px solid'>X</td><td id=b style='border: 2px solid'>X</td></tr>
            </table>
            """);

        // The table border is the half of the widest border of its sides, the border between the cells is the widest one
        var a = GetRect(document, "a");
        var b = GetRect(document, "b");
        Assert.Equal(5, a.X);
        Assert.Equal(5 + 10 + 5, a.Width);
        Assert.Equal(a.Right, b.X);
        Assert.Equal(5 + 10 + 2, b.Width);
        Assert.Equal(5 + 20 + 17 + 2, OffsetWidth(document, "t"));
    }

    [Fact]
    public void Captions_AreInTheTableWrapperBox()
    {
        var document = Parse("""
            <table id=t style='width: 100px; margin-left: 20px'>
              <caption id=top>X</caption>
              <caption id=bottom style='caption-side: bottom'>X</caption>
              <tr><td id=c>X</td></tr>
            </table>
            """);
        Assert.Equal(Rect(20, 0, 100, 10), GetRect(document, "top"));
        Assert.Equal(Rect(20, 10, 100, 10), GetRect(document, "c"));
        Assert.Equal(Rect(20, 20, 100, 10), GetRect(document, "bottom"));

        // The offset and client sizes of a table are the ones of its wrapper box (with its captions), as in the browsers
        Assert.Equal(30, OffsetHeight(document, "t"));
        Assert.Equal(30, ((Element)document.GetElementById("t")!).ClientHeight);
    }

    [Fact]
    public void WrapperAndGridBox_ShareTheStyleOfTheTable()
    {
        // The percentage width applies to the table grid box, relative to the containing block of the wrapper; the margins apply
        // to the wrapper; the borders and paddings to the grid box
        var document = Parse("<div style='width: 400px'><table id=t width=50% style='margin: 10px; border: 5px solid; padding: 3px'><tr><td>X</td></tr></table></div>");
        Assert.Equal(Rect(10, 10, 200, 26), GetRect(document, "t"));
        Assert.Equal("200px", GetComputed(document, "t", "width"));
        Assert.Equal("10px", GetComputed(document, "t", "margin-left"));
        Assert.Equal("3px", GetComputed(document, "t", "padding-left"));
    }

    [Fact]
    public void AutoMargins_CenterTheTable()
    {
        var document = Parse("<div style='width: 400px'><table id=t style='margin: 0 auto'><tr><td>XXXXXXXXXX</td></tr></table></div>");
        Assert.Equal(Rect(150, 0, 100, 10), GetRect(document, "t"));
    }

    [Fact]
    public void VisibilityCollapse_RemovesTheRow()
    {
        var document = Parse("<table id=t><tr><td>X</td></tr><tr style='visibility: collapse'><td>XXXX</td></tr><tr><td id=c>X</td></tr></table>");

        // The collapsed row does not take space; the columns keep its width
        Assert.Equal(20, OffsetHeight(document, "t"));
        Assert.Equal(Rect(0, 10, 40, 10), GetRect(document, "c"));
    }

    [Fact]
    public void ShrinkToFitContainer_UsesTheIntrinsicSizesOfTheTable()
    {
        var document = Parse("<div id=f style='float: left'><table><tr><td>XXX XXX</td><td style='width: 100px'></td></tr></table></div>");
        Assert.Equal(170, GetRect(document, "f").Width);
    }

    [Fact]
    public void InlineTable_IsAlignedByTheBaselineOfItsFirstRow()
    {
        var document = Parse("<div>X<table style='display: inline-table'><tr><td style='padding-top: 20px'><span id=s>X</span></td></tr><tr><td>X</td></tr></table></div>");
        Assert.Equal(20, GetRect(document, "s").Y);
    }

    [Fact]
    public void AbsolutelyPositionedBoxInAPositionedTable_IsRelativeToTheWrapperBox()
    {
        var document = Parse("<table style='position: relative'><caption>X</caption><tr><td>XXXX<div id=a style='position: absolute; top: 0; left: 0; width: 5px; height: 5px'></div></td></tr></table>");
        Assert.Equal(Rect(0, 0, 5, 5), GetRect(document, "a"));
    }

    [Fact]
    public void Quirks_CellHeightIsTheBorderBoxHeight()
    {
        // https://quirks.spec.whatwg.org/#the-table-cell-height-box-sizing-quirk
        var document = Parse("<table><tr><td id=c style='height: 30px; padding: 5px'>X</td></tr></table>", doctype: string.Empty);
        Assert.Equal(30, GetRect(document, "c").Height);
    }

    [Fact]
    public void Baseline_RowOfEmptyCells_IsTheBottomOfTheirContentBox()
    {
        // The baseline of an inline-table is the one of its first row: the bottom content edge of its cells without content
        var document = Parse("""
            <div id=c><span id=t style='display: inline-table'>
              <span style='display: table-row'><span style='display: table-cell; height: 48px'></span></span>
              <span style='display: table-row'><span style='display: table-cell; height: 48px'></span></span>
            </span></div>
            """);
        Assert.Equal(0, GetRect(document, "t").Y);
        Assert.Equal(96, GetRect(document, "t").Height);
        Assert.Equal(96, GetRect(document, "c").Height);
    }

    [Fact]
    public void Baseline_EmptyCellDoesNotMoveTheContentsOfTheOtherCells()
    {
        var document = Parse("""
            <table style='border-spacing: 0'><tr style='vertical-align: baseline'><td style='height: 30px; padding: 0'><span id=s>X</span></td><td style='height: 30px; padding: 0'></td></tr></table>
            """);
        Assert.Equal(0, GetRect(document, "s").Y);
    }

    [Fact]
    public void Overflow_ClipsAtThePaddingBoxOfTheGridBox()
    {
        // The rows are clipped inside the borders of the table: the table grid box clips, the wrapper box (and the captions) do not
        var document = Parse("<table id=t style='overflow: hidden; border: 5px solid; width: 50px; table-layout: fixed'><tr><td style='white-space: nowrap'>XXXXXXXXXX</td></tr></table>");
        var wrapper = document.GetLayoutEngine().GetFragments(document.GetElementById("t")!)[0];
        Assert.Equal((OverflowKind.Visible, OverflowKind.Visible), ScrollContainers.GetUsedOverflow(wrapper.Box));
        var grid = wrapper.Children.OfType<BoxFragment>().Single(child => child.Box.Kind == ContainerBoxKind.TableGrid);
        Assert.Equal((OverflowKind.Clip, OverflowKind.Clip), ScrollContainers.GetUsedOverflow(grid.Box));
        Assert.Equal(new PhysicalRect(5, 5, 40, grid.Size.Height - 10), grid.OverflowClipRect);
    }

    [Fact]
    public void Cell_SpecifiedHeightIsAMinimumWhateverItsOverflow()
    {
        var document = Parse("<table><tr><td id=c style='height: 20px; overflow: hidden'><div style='height: 300px'></div></td></tr></table>");
        Assert.Equal(300, GetRect(document, "c").Height);
    }

    [Fact]
    public void Cell_PercentageHeightOfAScrollContainer_IsZeroInARestrictedCellOnly()
    {
        // https://drafts.csswg.org/css-tables-3/#row-layout: in the first layout of a cell, 0px for a scroll container; as in the
        // browsers, only when the block size of the cell is restricted (here, by the height of the table)
        var document = Parse("""
            <div style='display: table'><div id=a style='overflow: auto; height: 100%'><div style='width: 100px; height: 100px'></div></div></div>
            <div style='display: table; height: 50px'><div id=b style='overflow: auto; height: 100%'><div style='width: 100px; height: 100px'></div></div></div>
            """);
        Assert.Equal(100, GetRect(document, "a").Height);
        Assert.Equal(50, GetRect(document, "b").Height);
    }

    [Fact]
    public void Cell_PercentagePaddings_ResolveAgainstTheColumnsAndTheSpacingBetweenThem()
    {
        // The columns are 110px and 100px wide, separated by 10px: 50% of 220px + 10px
        var document = Parse("<table style='border-spacing: 10px 0'><tr><td style='padding-left: calc(50% + 10px)'><div id=d style='width: 100px; height: 10px'></div></td><td><div style='width: 100px; height: 10px'></div></td></tr></table>");
        Assert.Equal(130, GetRect(document, "d").X);
    }

    [Fact]
    public void Cell_PercentagesOfTheChildren_TableWhoseHeightDoesNotResolve()
    {
        // The percentage height of the table behaves as auto: the cell is not "considered to have its height specified explicitly"
        var document = Parse("<div style='display: table; height: 100%; width: 100px'><div id=a style='height: 50%'><div style='height: 100px'></div></div></div>");
        Assert.Equal(100, GetRect(document, "a").Height);
    }

    [Theory]
    [InlineData("end", 0)]
    [InlineData("unsafe end", -30)]
    [InlineData("unsafe center", -15)]
    public void Cell_AlignContent_OverflowingContents(string alignment, double y)
    {
        // The collapsed row makes the cell (30px) shorter than its contents (60px): only an unsafe alignment moves them
        var document = Parse($"<table><tr style='height: 30px'><td rowspan=2 style='align-content: {alignment}'><div id=a style='width: 10px; height: 60px'></div></td></tr><tr style='visibility: collapse; height: 30px'></tr></table>");
        Assert.Equal(y, GetRect(document, "a").Y);
    }
}
