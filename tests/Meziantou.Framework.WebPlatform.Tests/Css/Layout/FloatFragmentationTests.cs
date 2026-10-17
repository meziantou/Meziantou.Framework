using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The fragmentation of the content that is laid out again once its breaks are known: the floats, which are parallel flows
/// (https://drafts.csswg.org/css-break-3/#parallel-flows) that are pushed alone to the next column, the table headers and footers
/// that are repeated in each fragment of their table (https://drafts.csswg.org/css-tables-3/#repeated-headers), the collapsed
/// borders of a fragmented table, and <c>orphans</c> and <c>widows</c> counted in each fragment.
/// </summary>
public sealed class FloatFragmentationTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses the content of a multi-column container of columns of 50px by 100px filled sequentially, at the origin of a 800x600 viewport.</summary>
    private Document Parse(string html, int columns = 2, string multicolStyle = "")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><style>body {{ margin: 0; font: 10px/1 Ahem; orphans: 1; widows: 1 }} table {{ border-spacing: 0 }} td {{ padding: 0 }}</style><div style='columns: {columns}; column-gap: 0; column-fill: auto; width: {columns * 50}px; height: 100px; {multicolStyle}'>{html}</div>");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static List<PhysicalRect> GetClientRects(Document document, string id)
        => [.. document.GetElementById(id)!.GetClientRects().Select(rect => new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height))];

    [Fact]
    public void Float_ThatDoesNotFitIsPushedAlone()
    {
        // The float is not broken (break-inside: avoid) and does not fit in the 40px that remain: it starts the next column. The
        // box after it stays in the first column, where no float narrows it
        var document = Parse("""
            <div style='height: 60px'></div>
            <div id=float style='float: left; break-inside: avoid; width: 20px; height: 50px'></div>
            <div id=after style='display: flow-root; height: 40px'></div>
            """);
        Assert.Equal([Rect(50, 0, 20, 50)], GetClientRects(document, "float"));
        Assert.Equal([Rect(0, 60, 50, 40)], GetClientRects(document, "after"));
    }

    [Fact]
    public void Float_ThatFitsStaysInItsColumn()
    {
        var document = Parse("""
            <div style='height: 60px'></div>
            <div id=float style='float: left; break-inside: avoid; width: 20px; height: 40px'></div>
            <div id=after style='display: flow-root; height: 40px'></div>
            """);
        Assert.Equal([Rect(0, 60, 20, 40)], GetClientRects(document, "float"));
        Assert.Equal([Rect(20, 60, 30, 40)], GetClientRects(document, "after"));
    }

    [Fact]
    public void Float_AtTheStartOfAColumnIsNotPushed()
    {
        // A monolithic float taller than the column would not fit better in the next one
        var document = Parse("<div id=float style='float: left; contain: size; width: 20px; height: 150px'></div>");
        Assert.Equal([Rect(0, 0, 20, 150)], GetClientRects(document, "float"));
    }

    [Fact]
    public void Float_WhoseFirstLineDoesNotFitIsPushed()
    {
        // The lines after the float are laid out without it in the first column; the fourth line is beside it in the second one
        var document = Parse("""
            <div style='height: 70px'></div>
            <div id=float style='float: left; width: 20px; line-height: 40px'><br></div>
            <div id=text style='line-height: 10px'>XXXXX XXXXX XXXXX <span id=last>XXX</span></div>
            """);
        Assert.Equal([Rect(50, 0, 20, 40)], GetClientRects(document, "float"));
        Assert.Equal([Rect(0, 70, 50, 30), Rect(50, 0, 50, 10)], GetClientRects(document, "text"));
        Assert.Equal([Rect(70, 0, 30, 10)], GetClientRects(document, "last"));
    }

    [Fact]
    public void Float_WhoseEndMarginDoesNotFitIsPushed()
    {
        // The block-end margin of a float is not truncated (https://github.com/w3c/csswg-drafts/issues/9191, as in Blink)
        var document = Parse("""
            <div style='height: 60px'></div>
            <div id=float style='float: left; width: 20px; height: 40px; margin-bottom: 1px'></div>
            """);
        Assert.Equal([Rect(50, 0, 20, 40)], GetClientRects(document, "float"));
    }

    [Fact]
    public void Float_FloatsAndClearedBoxesFollowAPushedFloat()
    {
        // The second float is not above the first one, and the box that clears them is below them in the second column
        var document = Parse("""
            <div style='height: 60px'></div>
            <div id=a style='float: left; break-inside: avoid; width: 50px; height: 50px'></div>
            <div id=b style='float: left; width: 50px; height: 10px'></div>
            <div id=clear style='clear: left; height: 10px'></div>
            """);
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 50, 50, 10)], GetClientRects(document, "b"));
        Assert.Equal([Rect(50, 60, 50, 10)], GetClientRects(document, "clear"));
    }

    [Fact]
    public void Float_BoxThatAvoidsFloatsContinuesBelowAPushedFloat()
    {
        // The box starts in the first column, where the float is not; its part in the second column starts below the float
        var document = Parse("""
            <div style='height: 70px'></div>
            <div id=float style='float: left; break-inside: avoid; width: 100%; height: 50px'></div>
            <div id=root style='display: flow-root; height: 130px'></div>
            """, columns: 3);
        Assert.Equal([Rect(50, 0, 50, 50)], GetClientRects(document, "float"));
        Assert.Equal([Rect(0, 70, 50, 30), Rect(50, 50, 50, 50), Rect(100, 0, 50, 50)], GetClientRects(document, "root"));
    }

    [Fact]
    public void Float_IsPushedInBalancedColumns()
    {
        // The columns are balanced as if the float was pushed: 50px tall, the line in the first one and the float in the second one
        var document = Parse("""
            <div id=text style='line-height: 10px'>X</div>
            <div id=float style='float: left; contain: size; width: 20px; height: 50px'></div>
            """, multicolStyle: "column-fill: balance; height: auto");
        Assert.Equal([Rect(50, 0, 20, 50)], GetClientRects(document, "float"));
        Assert.Equal(50, document.QuerySelector("body > div")!.GetBoundingClientRect().Height);
    }

    [Fact]
    public void Orphans_AreCountedInEachFragment()
    {
        // Nine lines in columns of four lines, with orphans: 3 and widows: 3: the second column would break before the last line
        // (one widow); the break before the eighth line leaves three lines in the column, and violates widows only (as in Blink)
        var document = Parse("<div id=text style='line-height: 25px; orphans: 3; widows: 3'>1<br>2<br>3<br>4<br>5<br>6<br>7<br>8<br>9</div>", columns: 3);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100), Rect(100, 0, 50, 50)], GetClientRects(document, "text"));
    }

    [Fact]
    public void TableHeaderAndFooter_AreRepeatedInEachFragment()
    {
        // The header and the footer (20px each, break-inside: avoid) are repeated: each column has 60px of the 120px cell
        var document = Parse("""
            <table id=table style='width: 50px'>
              <thead id=head style='break-inside: avoid'><tr><td><div style='height: 20px'></div></td></tr></thead>
              <tfoot id=foot style='break-inside: avoid'><tr><td><div style='height: 20px'></div></td></tr></tfoot>
              <tbody><tr><td><div id=cell style='height: 120px'></div></td></tr></tbody>
            </table>
            """);
        Assert.Equal([Rect(0, 20, 50, 60), Rect(50, 20, 50, 60)], GetClientRects(document, "cell"));
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100)], GetClientRects(document, "table"));

        // The copies of the header and of the footer are painted: the fragment tree has a header at the start of the second
        // fragment of the table and a footer at the end of the first one
        var fragments = document.GetLayoutEngine().GetFragments(document.GetElementById("table")!);
        Assert.Equal(2, fragments.Count);
    }

    [Fact]
    public void TableHeader_TallerThanAQuarterOfTheColumnIsNotRepeated()
    {
        var document = Parse("""
            <table id=table style='width: 50px'>
              <thead style='break-inside: avoid'><tr><td><div style='height: 30px'></div></td></tr></thead>
              <tbody><tr><td><div id=cell style='height: 120px'></div></td></tr></tbody>
            </table>
            """);
        Assert.Equal([Rect(0, 30, 50, 70), Rect(50, 0, 50, 50)], GetClientRects(document, "cell"));
    }

    [Fact]
    public void TableHeader_WithoutBreakInsideAvoidIsNotRepeated()
    {
        var document = Parse("""
            <table id=table style='width: 50px'>
              <thead><tr><td><div style='height: 20px'></div></td></tr></thead>
              <tbody><tr><td><div id=cell style='height: 120px'></div></td></tr></tbody>
            </table>
            """);
        Assert.Equal([Rect(0, 20, 50, 80), Rect(50, 0, 50, 40)], GetClientRects(document, "cell"));
    }

    [Fact]
    public void FloatAvoider_HasTheInlineSizeOfTheLayoutOpportunityOfEachFragment()
    {
        // The box that avoids the floats (automatic width) is beside the 20px float of the first column, beside the 40px float
        // pushed to the second one, and as wide as the third one
        var document = Parse("""
            <div id=first style='float: right; width: 20px; height: 60px'></div>
            <div id=pushed style='clear: both; float: right; break-inside: avoid; width: 40px; height: 60px'></div>
            <div id=avoider style='display: flow-root; height: 300px'></div>
            """, columns: 3);
        Assert.Equal([Rect(60, 0, 40, 60)], GetClientRects(document, "pushed"));
        Assert.Equal([Rect(0, 0, 30, 100), Rect(50, 0, 10, 100), Rect(100, 0, 50, 100)], GetClientRects(document, "avoider"));
    }

    [Fact]
    public void FloatAvoider_ContentOfEachFragmentIsLaidOutInItsArea()
    {
        // The children of the box in the second column come from its layout beside the float there (the percentage width)
        var document = Parse("""
            <div style='height: 50px'></div>
            <div style='float: left; break-inside: avoid; width: 20px; height: 60px'></div>
            <div style='display: flow-root'>
              <div id=a style='width: 50%; height: 50px'></div>
              <div id=b style='width: 50%; height: 50px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 50, 25, 50)], GetClientRects(document, "a"));
        Assert.Equal([Rect(70, 0, 15, 50)], GetClientRects(document, "b"));
    }

    [Fact]
    public void EndMargin_OfTheContentsThatOverflowALimitedBoxDoesNotCollapseThroughIt()
    {
        // The margin of the last child, in the second column, does not move the box after the box whose block size is limited
        var document = Parse("""
            <div style='max-height: 0'>
              <div style='height: 150px; contain: size'></div>
              <div style='display: flow-root; margin-bottom: 80px'></div>
            </div>
            <div id=after style='height: 20px'></div>
            """);
        Assert.Equal([Rect(0, 0, 50, 20)], GetClientRects(document, "after"));
    }

    [Fact]
    public void AlignContent_IsNotAppliedToABrokenBox()
    {
        // https://drafts.csswg.org/css-align-3/#distribution-block: the alignment of fragmented contents is undefined; the contents
        // of a broken box are not aligned (the box that is not broken is)
        var document = Parse("""
            <div style='height: 150px; align-content: end'><div id=broken style='height: 20px'></div></div>
            <div style='height: 40px; align-content: end'><div id=aligned style='height: 20px'></div></div>
            """);
        Assert.Equal([Rect(0, 0, 50, 20)], GetClientRects(document, "broken"));
        Assert.Equal([Rect(50, 70, 50, 20)], GetClientRects(document, "aligned"));
    }

    [Fact]
    public void BoxWithoutBlockSize_WhoseContentsOverflowStaysInItsColumn()
    {
        // The box without block size ends the content of the column: its contents continue there and break in it
        var document = Parse("""
            <div style='height: 10px'></div>
            <div id=empty style='height: 0'>
              <div id=a style='height: 20px'></div>
              <div id=b style='height: 80px; contain: size'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 10, 50, 20)], GetClientRects(document, "a"));
        Assert.Equal([Rect(50, 0, 50, 80)], GetClientRects(document, "b"));
    }
}
