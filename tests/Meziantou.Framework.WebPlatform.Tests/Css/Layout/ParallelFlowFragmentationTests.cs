using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The fragmentation of the parallel flows (https://drafts.csswg.org/css-break-3/#parallel-flows): the items of the flex
/// containers (https://drafts.csswg.org/css-flexbox-1/#pagination), of the grids (https://drafts.csswg.org/css-grid-2/#pagination)
/// and the cells of the table rows break independently in the columns of a multi-column container.
/// </summary>
public sealed class ParallelFlowFragmentationTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses the content of a multi-column container of two columns of 50px by 100px filled sequentially, at the origin of a 800x600 viewport.</summary>
    private Document Parse(string html, string multicolStyle = "")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><style>body {{ margin: 0; font: 10px/1 Ahem; orphans: 1; widows: 1 }} table {{ border-spacing: 0 }} td {{ padding: 0 }}</style><div style='columns: 2; column-gap: 0; column-fill: auto; width: 100px; height: 100px; {multicolStyle}'>{html}</div>");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static List<PhysicalRect> GetClientRects(Document document, string id)
        => [.. document.GetElementById(id)!.GetClientRects().Select(rect => new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height))];

    [Fact]
    public void FlexRow_ItemsBreakIndependently()
    {
        // The second child of the first item does not fit: it is pushed to the next column, while the second item breaks at the end of the column
        var document = Parse("""
            <div id=flex style='display: flex'>
              <div id=a style='width: 25px'><div id=a1 style='contain: size; height: 80px'></div><div id=a2 style='contain: size; height: 30px'></div></div>
              <div id=b style='width: 25px; height: 200px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 25, 80)], GetClientRects(document, "a1"));
        Assert.Equal([Rect(50, 0, 25, 30)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(25, 0, 25, 100), Rect(75, 0, 25, 100)], GetClientRects(document, "b"));

        // The fragment of the first item extends to the end of the column, and the item is stretched to the line in the next one
        Assert.Equal([Rect(0, 0, 25, 100), Rect(50, 0, 25, 100)], GetClientRects(document, "a"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100)], GetClientRects(document, "flex"));
    }

    [Fact]
    public void FlexRow_ItemThatGrowsPushesTheContentAfterTheLine()
    {
        // The item is 110px tall; its second child is pushed to the next column, so the item ends 30px after the break and the
        // line grows: the block after the container follows it
        var document = Parse("""
            <div id=flex style='display: flex'>
              <div id=a style='width: 50px'><div style='contain: size; height: 80px'></div><div id=a2 style='contain: size; height: 30px'></div></div>
            </div>
            <div id=after style='height: 20px'></div>
            """);
        Assert.Equal([Rect(50, 0, 50, 30)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 30)], GetClientRects(document, "a"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 30)], GetClientRects(document, "flex"));
        Assert.Equal([Rect(50, 30, 50, 20)], GetClientRects(document, "after"));
    }

    [Fact]
    public void FlexRow_ItemWithDefiniteBlockSizeDoesNotGrow()
    {
        // The contents of the item are pushed, but its block size is specified: it ends 10px after the break, and its contents overflow it
        var document = Parse("""
            <div id=flex style='display: flex'>
              <div id=a style='width: 50px; height: 110px'><div style='contain: size; height: 80px'></div><div id=a2 style='contain: size; height: 30px'></div></div>
            </div>
            <div id=after style='height: 20px'></div>
            """);
        Assert.Equal([Rect(50, 0, 50, 30)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 10)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 10, 50, 20)], GetClientRects(document, "after"));
    }

    [Fact]
    public void FlexRow_LineIsPushedWhenAnItemAvoidsBreaks()
    {
        // A break inside the line would break the second item: the break before the container violates no rule
        var document = Parse("""
            <div style='height: 20px'></div>
            <div id=flex style='display: flex'>
              <div style='width: 25px; height: 100px'></div>
              <div id=b style='width: 25px; break-inside: avoid'></div>
            </div>
            """);
        Assert.Equal([Rect(50, 0, 50, 100)], GetClientRects(document, "flex"));
        Assert.Equal([Rect(75, 0, 25, 100)], GetClientRects(document, "b"));
    }

    [Fact]
    public void FlexRow_MonolithicItemIsPushedWithoutItsLine()
    {
        // The second item is aligned at the end of the line and does not fit in the first column: it is pushed to the next one,
        // the first item is broken at the end of the column
        var document = Parse("""
            <div id=flex style='display: flex; height: 150px'>
              <div id=a style='width: 25px'></div>
              <div id=b style='width: 25px; height: 80px; contain: size; align-self: end'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 25, 100), Rect(50, 0, 25, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(75, 0, 25, 80)], GetClientRects(document, "b"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "flex"));
    }

    [Fact]
    public void FlexColumn_GapDisappearsAtTheBreakButNotTheMargins()
    {
        // The gap that coincides with the break disappears; the margin of the item after the break is preserved
        var document = Parse("""
            <div style='display: flex; flex-direction: column; gap: 10px'>
              <div style='height: 95px'></div>
              <div id=b style='height: 20px'></div>
              <div id=c style='height: 20px; margin-top: 5px'></div>
            </div>
            """);
        Assert.Equal([Rect(50, 0, 50, 20)], GetClientRects(document, "b"));
        Assert.Equal([Rect(50, 35, 50, 20)], GetClientRects(document, "c"));
    }

    [Fact]
    public void FlexColumn_ForcedBreakInsideAnItemWithDefiniteBlockSize()
    {
        // The item fills the first column (its block size is specified); its contents after the forced break and the next item are in the next column
        var document = Parse("""
            <div style='display: flex; flex-direction: column'>
              <div id=a style='height: 100px'><div style='height: 50px'></div><div id=a2 style='height: 10px; break-before: column'></div></div>
              <div id=b style='height: 50px; margin-top: 10px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 0)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 10)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(50, 10, 50, 50)], GetClientRects(document, "b"));
    }

    [Fact]
    public void Grid_RowGrowsWithItsItemAndShiftsTheNextRows()
    {
        // The forced break in the first item pushes its second child to the next column: the row ends after it, and the second row follows
        var document = Parse("""
            <div id=grid style='display: grid'>
              <div id=a><div style='height: 30px'></div><div id=a2 style='height: 50px; break-before: column'></div></div>
              <div id=b style='height: 40px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(50, 50, 50, 40)], GetClientRects(document, "b"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 90)], GetClientRects(document, "grid"));
    }

    [Fact]
    public void Grid_ItemsOfARowBreakIndependently()
    {
        var document = Parse("""
            <div style='display: grid; grid-template-columns: 25px 25px'>
              <div id=a><div style='contain: size; height: 70px'></div><div id=a2 style='contain: size; height: 60px'></div></div>
              <div id=b style='height: 130px'></div>
            </div>
            """);
        Assert.Equal([Rect(50, 0, 25, 60)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(25, 0, 25, 100), Rect(75, 0, 25, 30)], GetClientRects(document, "b"));
    }

    [Fact]
    public void Table_CellsOfARowBreakIndependently()
    {
        // The forced break in the cell of the first row pushes its second child to the next column, where the second row follows it
        var document = Parse("""
            <table id=table>
              <tr><td><div style='height: 10px; width: 50px'></div><div id=a2 style='height: 10px; break-before: column'></div></td></tr>
              <tr><td id=b><div style='height: 90px'></div></td></tr>
            </table>
            """);
        Assert.Equal([Rect(50, 0, 50, 10)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(50, 10, 50, 90)], GetClientRects(document, "b"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100)], GetClientRects(document, "table"));
    }

    [Fact]
    public void ForcedBreak_IsHonoredWhenTheContentFitsInTheColumn()
    {
        var document = Parse("<div style='height: 20px'></div><div id=b style='height: 20px; break-before: column'></div>");
        Assert.Equal([Rect(50, 0, 50, 20)], GetClientRects(document, "b"));
    }

    [Fact]
    public void BreakBeforeAvoid_IsNotBypassedByTheEndOfThePreviousBox()
    {
        // The break between the second and third boxes is avoided: the columns break between the first and second ones
        var document = Parse("""
            <div style='height: 50px'></div>
            <div id=b style='height: 50px'></div>
            <div id=c style='height: 60px; break-before: avoid'></div>
            """);
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "b"));
        Assert.Equal([Rect(50, 50, 50, 50), Rect(100, 0, 50, 10)], GetClientRects(document, "c"));
    }

    [Fact]
    public void AbsolutelyPositionedChild_IsPlacedInTheExtendedFragmentOfItsContainingBlock()
    {
        // The second line is pushed to the next column; the first fragment of the container extends to the end of the column, and
        // contains the absolutely positioned box
        var document = Parse("""
            <div id=flex style='display: flex; flex-wrap: wrap; position: relative'>
              <div style='width: 50px; height: 20px'></div>
              <div style='width: 50px; height: 100px; break-inside: avoid'></div>
              <div id=abs style='position: absolute; top: 20px; width: 50px; height: 80px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 20, 50, 80)], GetClientRects(document, "abs"));
    }

    [Fact]
    public void DefiniteBox_ContentsThatOverflowAreAParallelFlow()
    {
        // The contents of the box overflow it and are broken at the end of the column; the block after the box follows its
        // block-end edge in the first column (its margin is truncated: it starts the second column)
        var document = Parse("""
            <div id=box style='height: 70px'><div id=tall style='height: 150px'></div></div>
            <div id=after style='margin-top: 60px; height: 50px'></div>
            """);
        Assert.Equal([Rect(0, 0, 50, 70), Rect(50, 0, 50, 0)], GetClientRects(document, "box"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "tall"));
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "after"));
    }

    [Fact]
    public void DefiniteBox_UsesTheBlockSizeOfTheFragmentainersItIsBrokenBy()
    {
        // The second child is pushed to the next column: the box still uses the 60px that remain in the first column, and has
        // 90px left in the second one (not the 120px that follow the break in the strip)
        var document = Parse("""
            <div style='height: 40px'></div>
            <div id=box style='height: 150px'><div style='contain: size; height: 30px'></div><div id=b style='contain: size; height: 50px'></div></div>
            <div id=after style='height: 10px'></div>
            """);
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "b"));
        Assert.Equal([Rect(0, 40, 50, 60), Rect(50, 0, 50, 90)], GetClientRects(document, "box"));
        Assert.Equal([Rect(50, 90, 50, 10)], GetClientRects(document, "after"));
    }

    [Fact]
    public void DefiniteBox_ThatFitsKeepsItsFirstMonolithicContent()
    {
        // https://github.com/w3c/csswg-drafts/issues/6056: the box fits in the first column, so its first monolithic child is
        // placed with it (it overflows the column) instead of pushing the box; the second child is in the next column
        var document = Parse("""
            <div style='height: 50px'></div>
            <div id=box style='height: 25px'><div id=a style='contain: size; height: 80px'></div><div id=b style='contain: size; height: 40px'></div></div>
            """);
        Assert.Equal([Rect(0, 50, 50, 80)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 40)], GetClientRects(document, "b"));
        Assert.Equal(Rect(0, 50, 50, 25), GetClientRects(document, "box")[0]);
    }

    [Fact]
    public void TrailingMarginOfTheLastChild_IsTruncatedAtTheBreak()
    {
        // The margin of the child crosses the end of the column: the box ends there, and the next block starts the second column
        var document = Parse("""
            <div id=root style='display: flow-root'><div style='height: 70px; margin-bottom: 50px'></div></div>
            <div id=after style='height: 20px'></div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100)], GetClientRects(document, "root"));
        Assert.Equal([Rect(50, 0, 50, 20)], GetClientRects(document, "after"));
    }

    [Fact]
    public void BoxWithoutBlockSize_AtTheEndOfAColumnStaysInIt()
    {
        var document = Parse("""
            <div style='height: 100px'></div>
            <div id=empty></div>
            <div id=after style='contain: size; height: 20px'></div>
            """);
        Assert.Equal([Rect(0, 100, 50, 0)], GetClientRects(document, "empty"));
        Assert.Equal([Rect(50, 0, 50, 20)], GetClientRects(document, "after"));
    }

    [Fact]
    public void ForcedBreak_AfterABoxWithoutBlockSizeAtTheStartIsTaken()
    {
        // A class A break point: the break is taken although the first column has no content...
        var document = Parse("<div style='break-after: column'></div><div id=b style='height: 20px'></div>");
        Assert.Equal([Rect(50, 0, 50, 20)], GetClientRects(document, "b"));

        // ... but not before the first child of a box
        document = Parse("<div><div id=b style='height: 20px; break-before: column'></div></div>");
        Assert.Equal([Rect(0, 0, 50, 20)], GetClientRects(document, "b"));
    }

    [Fact]
    public void NegativeMargin_AfterABrokenSiblingStaysInItsFragmentainer()
    {
        // The second box follows the end of the first one, which is in the second column: it is there, above the column
        var document = Parse("""
            <div style='height: 120px'></div>
            <div id=b style='margin-top: -50px; height: 20px'></div>
            """);
        Assert.Equal([Rect(50, -30, 50, 20)], GetClientRects(document, "b"));
    }

    [Fact]
    public void AbsolutelyPositionedBox_IsPlacedInTheSequenceOfTheColumns()
    {
        // The box is 250px below its containing block: in the third column (a new column), and it continues in a fourth one
        var document = Parse("""
            <div style='position: relative'><div id=abs style='position: absolute; top: 250px; width: 50px; height: 100px'></div></div>
            """);
        Assert.Equal([Rect(100, 50, 50, 50), Rect(150, 0, 50, 50)], GetClientRects(document, "abs"));
    }

    [Fact]
    public void AbsolutelyPositionedBox_DoesNotChangeTheBreaksOfTheInFlowContent()
    {
        var document = Parse("""
            <div style='position: relative'>
              <div id=a style='height: 80px'></div>
              <div id=abs style='position: absolute; top: 0; width: 50px; height: 150px'><div style='height: 90px'></div><div id=inner style='contain: size; height: 20px'></div></div>
              <div id=b style='contain: size; height: 40px'></div>
            </div>
            """);

        // The in-flow content breaks before its second box; the box breaks at the end of the first column, with its own content
        Assert.Equal([Rect(50, 0, 50, 40)], GetClientRects(document, "b"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "abs"));
        Assert.Equal([Rect(50, 0, 50, 20)], GetClientRects(document, "inner"));
    }

    [Fact]
    public void AbsolutelyPositionedBox_AtTheBlockEndOfABrokenContainingBlock()
    {
        // The containing block is 100px tall in the first column (its second child is pushed) and 40px in the second one: the
        // box is at the end of these fragments stitched together
        var document = Parse("""
            <div style='position: relative'>
              <div style='contain: size; height: 70px'></div>
              <div style='contain: size; height: 40px'></div>
              <div id=abs style='position: absolute; bottom: 0; width: 50px; height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(50, 20, 50, 20)], GetClientRects(document, "abs"));
    }

    [Fact]
    public void AbsolutelyPositionedBoxes_AreContentOfTheBalancedColumns()
    {
        // The box of 200px is balanced in the two columns, which have no in-flow content
        var document = Parse("<div style='position: relative'><div id=abs style='position: absolute; width: 50px; height: 200px'></div></div>", multicolStyle: "column-fill: balance; height: auto");
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100)], GetClientRects(document, "abs"));
    }

    [Fact]
    public void ColumnFillAuto_WithoutBlockSizeFillsTheFirstColumn()
    {
        var document = Parse("<div id=a style='height: 150px'></div>", multicolStyle: "height: auto");
        Assert.Equal([Rect(0, 0, 50, 150)], GetClientRects(document, "a"));
    }

    [Fact]
    public void FlexColumn_ItemsAlignedToTheEndBreakInTheSpaceBeforeThem()
    {
        // The space before the items of a container with a definite block size breaks anywhere; the container uses the block
        // size of the columns
        var document = Parse("""
            <div id=flex style='display: flex; flex-direction: column; justify-content: flex-end; height: 150px'>
              <div id=item style='contain: size; height: 30px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "flex"));
        Assert.Equal([Rect(50, 20, 50, 30)], GetClientRects(document, "item"));
    }

    [Fact]
    public void BoxThatStartsAfterTheEndOfTheColumnIsPushed()
    {
        // The block-start margin of the first box does not fit in the column: as in Blink, the box is pushed to the next column
        // and the margin is truncated
        var document = Parse("<div id=a style='margin-top: 150px; height: 50px'></div>");
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "a"));
    }

    [Fact]
    public void FlexColumn_MarginAfterAForcedBreakIsPreserved()
    {
        // The margin after the forced break fills the second column: the item, which starts at its end, is pushed to the third one
        var document = Parse("""
            <div style='display: flex; flex-direction: column'>
              <div id=a style='height: 50px'></div>
              <div id=b style='height: 50px; margin-top: 100px; break-before: column'></div>
              <div id=c style='height: 20px; margin-top: 10px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(100, 0, 50, 50)], GetClientRects(document, "b"));
        Assert.Equal([Rect(100, 60, 50, 20)], GetClientRects(document, "c"));
    }

    [Fact]
    public void FlexColumn_ForcedBreakBeforeAnItemThatOverlapsThePreviousOne()
    {
        // The second item overlaps the first one (a negative margin): after the forced break, it starts the column after the one
        // where the first item ends, at the offset its margin gives it
        var document = Parse("""
            <div style='display: flex; flex-direction: column'>
              <div id=a style='height: 150px'></div>
              <div id=b style='height: 100px; margin-top: -150px; break-before: column'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(100, -150, 50, 100)], GetClientRects(document, "b"));
    }

    [Fact]
    public void FlexColumn_GapAtAForcedBreakDisappears()
    {
        var document = Parse("""
            <div style='display: flex; flex-direction: column; row-gap: 10px'>
              <div id=a style='height: 100px'></div>
              <div id=b style='height: 90px; break-before: column'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 90)], GetClientRects(document, "b"));
    }

    [Fact]
    public void FlexColumn_ItemEncompassesItsContents()
    {
        // The item is 10px tall in the container (its flex basis); it cannot shrink and its block size is automatic: as in Blink,
        // it is as tall as its contents once fragmented
        var document = Parse("""
            <div style='display: flex; flex-direction: column'>
              <div id=a style='flex-basis: 10px; min-height: 10px'><div style='contain: size; height: 80px'></div><div id=a2 style='contain: size; height: 60px'></div></div>
            </div>
            <div id=after style='height: 10px'></div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 60)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 60)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(50, 60, 50, 10)], GetClientRects(document, "after"));
    }

    [Fact]
    public void FlexColumnMultiLine_BreakValuesOfTheFirstItemsOfTheLinesArePropagated()
    {
        // The forced break before the first item of the second line is a forced break before the container
        var document = Parse("""
            <div style='height: 50px'></div>
            <div id=flex style='display: flex; flex-direction: column; flex-wrap: wrap; height: 50px'>
              <div style='width: 10px; height: 50px'></div>
              <div id=b style='width: 10px; height: 50px; break-before: column'></div>
            </div>
            """);
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "flex"));
        Assert.Equal([Rect(75, 0, 10, 50)], GetClientRects(document, "b"));
    }

    [Fact]
    public void FlexColumnMultiLine_ForcedBreakBeforeTheFirstItemOfALineSeparatedByItsMargin()
    {
        // The first item of the second line is separated from the start of the line by its margin: the forced break is taken,
        // the margin fills the second column, and the item is pushed to the third one
        var document = Parse("""
            <div style='display: flex; flex-direction: column; flex-wrap: wrap; height: 150px'>
              <div id=a style='width: 10px; height: 150px'></div>
              <div id=b style='width: 10px; height: 50px; margin-top: 100px; break-before: column'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 10, 100), Rect(50, 0, 10, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(125, 0, 10, 50)], GetClientRects(document, "b"));
    }

    [Fact]
    public void Grid_RowWithAFixedSizeDoesNotGrow()
    {
        // The row has a fixed track sizing function: as in Blink, the stretched item does not grow when its contents are pushed
        // by the break; they overflow it in the next column
        var document = Parse("""
            <div id=grid style='display: grid; grid-template-rows: 150px'>
              <div id=a><div style='contain: size; height: 80px'></div><div id=a2 style='contain: size; height: 80px'></div></div>
            </div>
            <div id=after style='height: 10px'></div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 80)], GetClientRects(document, "a2"));
        Assert.Equal([Rect(50, 50, 50, 10)], GetClientRects(document, "after"));
    }

    [Fact]
    public void Grid_RowAfterASpannedGapThatDisappearsStartsAtTheBreak()
    {
        // The column ends in the gap between the two rows, which the first item spans across: the gap disappears, and, as in
        // Blink, the second row starts at the break (its stretched item grows by the rest of the gap)
        var document = Parse("""
            <div style='display: grid; grid-template-columns: 25px 25px; grid-template-rows: 90px 30px; row-gap: 20px'>
              <div style='grid-row: 1 / 3'></div>
              <div id=b style='grid-column: 2; grid-row: 2'></div>
            </div>
            """);
        Assert.Equal([Rect(75, 0, 25, 40)], GetClientRects(document, "b"));
    }

    [Fact]
    public void Grid_SubgridIsShorterWhenAGapOfItsRowsDisappears()
    {
        // The gap between the second and the third rows of the subgrid disappears at the break: its third row starts the second
        // column, and, as in Blink, the subgrid ends with it; the item of the parent in the third row starts at the break
        var document = Parse("""
            <div style='display: grid; grid-template-columns: 25px 25px; grid-template-rows: 40px 40px 40px; row-gap: 20px'>
              <div id=subgrid style='display: grid; grid-template-rows: subgrid; grid-row: 1 / 4'><div id=s3 style='grid-row: 3'></div></div>
              <div id=c style='grid-column: 2; grid-row: 3'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 25, 100), Rect(50, 0, 25, 40)], GetClientRects(document, "subgrid"));
        Assert.Equal([Rect(50, 0, 25, 40)], GetClientRects(document, "s3"));
        Assert.Equal([Rect(75, 0, 25, 60)], GetClientRects(document, "c"));
    }

    [Fact]
    public void GridLanes_PushedItemIsPlacedAgainInTheNextFragmentainer()
    {
        // The third item cannot be broken and does not fit in the first column: it is pushed, and its track is closed (the fourth
        // item is placed in the other track); the pushed item is placed again in the next column, in its first track
        var document = Parse("""
            <div style='display: grid-lanes; grid-template-columns: 25px 25px'>
              <div id=a style='height: 60px'></div>
              <div id=b style='height: 30px'></div>
              <div id=c style='height: 80px; break-inside: avoid'></div>
              <div id=d style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 60, 25, 20)], GetClientRects(document, "d"));
        Assert.Equal([Rect(50, 0, 25, 80)], GetClientRects(document, "c"));
    }

    [Fact]
    public void GridLanes_BrokenItemClosesItsTrack()
    {
        // The third item is broken at the end of the first column: the rest of it starts its track in the next column, where the
        // fourth item, which cannot be broken, is placed in the other track
        var document = Parse("""
            <div style='display: grid-lanes; grid-template-columns: 25px 25px'>
              <div id=a style='height: 90px'></div>
              <div id=b style='height: 40px'></div>
              <div id=c style='height: 70px'></div>
              <div id=d style='height: 20px; break-inside: avoid'></div>
            </div>
            """);
        Assert.Equal([Rect(25, 40, 25, 60), Rect(75, 0, 25, 10)], GetClientRects(document, "c"));
        Assert.Equal([Rect(50, 0, 25, 20)], GetClientRects(document, "d"));
    }

    [Fact]
    public void Grid_ForcedBreakBetweenTwoRowsBesideAMonolithicItemThatOverflows()
    {
        var document = Parse("""
            <div style='display: grid; grid-template-rows: 20px 50px'>
              <div style='contain: size; height: 150px'></div>
              <div id=b style='break-before: column; height: 50px'></div>
            </div>
            """);
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "b"));
    }

    [Fact]
    public void Grid_ItemWithANegativeMarginStartsWithItsRow()
    {
        // The item starts 10px before its row, which starts the second column: it is not broken by the end of the first column
        var document = Parse("""
            <div style='display: grid; grid-template-rows: 100px 50px'>
              <div></div>
              <div id=b style='margin-top: -10px; height: 60px'></div>
            </div>
            """);
        Assert.Equal([Rect(50, -10, 50, 60)], GetClientRects(document, "b"));
    }

    [Fact]
    public void Grid_ItemOffsetInItsRowIsNotPushedWhenItCanBeBroken()
    {
        // The item aligned to the end of its row is broken between its line boxes (which violates orphans and widows): as in
        // Blink, a break before the first content of a row is not a possible break
        var document = Parse("""
            <div style='display: grid; grid-template-columns: 25px 25px; orphans: 2; widows: 2; line-height: 0'>
              <div><div style='display: inline-block; width: 25px; height: 80px'></div><div style='display: inline-block; width: 25px; height: 50px'></div></div>
              <div id=b style='align-self: end'><div id=b1 style='display: inline-block; width: 25px; height: 60px'></div><div id=b2 style='display: inline-block; width: 25px; height: 60px'></div></div>
            </div>
            """);
        Assert.Equal([Rect(25, 10, 25, 60)], GetClientRects(document, "b1"));
        Assert.Equal([Rect(75, 0, 25, 60)], GetClientRects(document, "b2"));
    }

    [Fact]
    public void FlexRow_ItemWithADefiniteBlockSizeBreaksBesideAMonolithicItemThatOverflows()
    {
        // The monolithic item overflows the first column; the item with a specified block size is broken at the end of the
        // column, and the item whose block size follows the line ends with the monolithic item
        var document = Parse("""
            <div style='display: flex; flex-wrap: wrap; height: 240px'>
              <div id=a style='width: 20px; height: 120px'></div>
              <div id=b style='width: 20px; height: 120px; contain: size'></div>
              <div id=c style='width: 50px; height: 120px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 20, 100), Rect(50, 0, 20, 20)], GetClientRects(document, "a"));
        Assert.Equal([Rect(20, 0, 20, 120)], GetClientRects(document, "b"));
        Assert.Equal([Rect(50, 20, 50, 80), Rect(100, 0, 50, 40)], GetClientRects(document, "c"));
    }
}
