using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The layout of the fieldset (https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements): the
/// rendered legend over the block-start border, the anonymous fieldset content box that holds the other contents with the
/// paddings, the overflow and the inner display of the fieldset; and the fragmentation of the boxes with
/// <c>box-decoration-break: clone</c> and a specified block size, and of the absolutely positioned boxes whose containing block
/// is a relatively positioned inline box.
/// </summary>
public sealed class FieldsetLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><style>body {{ margin: 0; font: 10px/1 Ahem }} fieldset {{ margin: 0; padding: 0; border: 0 solid; min-inline-size: 0 }} legend {{ padding: 0 }}</style>{html}");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static PhysicalRect GetRect(Document document, string id)
    {
        var rect = document.GetElementById(id)!.GetBoundingClientRect();
        return new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static List<PhysicalRect> GetClientRects(Document document, string id)
        => [.. document.GetElementById(id)!.GetClientRects().Select(rect => new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height))];

    [Fact]
    public void Legend_TallerThanTheBorderTakesTheStartOfTheFieldset()
    {
        // The margin box of the legend (40px) is taller than the border (10px): it starts at the block-start edge of the
        // fieldset, and the contents follow it; the legend is shrink-wrapped between the inline paddings of the fieldset
        var document = Parse("""
            <fieldset id=fieldset style='width: 200px; border-width: 10px; padding: 5px 20px'>
              <legend id=legend style='height: 30px; margin-bottom: 10px'>XXXX</legend>
              <div id=content style='height: 50px'></div>
            </fieldset>
            """);
        Assert.Equal(Rect(30, 0, 40, 30), GetRect(document, "legend"));
        Assert.Equal(Rect(30, 45, 200, 50), GetRect(document, "content"));
        Assert.Equal(Rect(0, 0, 260, 110), GetRect(document, "fieldset"));
    }

    [Fact]
    public void Legend_SmallerThanTheBorderIsCenteredInIt()
    {
        var document = Parse("""
            <fieldset id=fieldset style='width: 200px; border-width: 50px 10px 10px'>
              <legend id=legend style='width: 20px; height: 20px'></legend>
              <div id=content style='height: 50px'></div>
            </fieldset>
            """);
        Assert.Equal(Rect(10, 15, 20, 20), GetRect(document, "legend"));
        Assert.Equal(Rect(10, 50, 200, 50), GetRect(document, "content"));
    }

    [Fact]
    public void Legend_IsAlignedByJustifySelf()
    {
        var document = Parse("""
            <fieldset style='width: 200px'>
              <legend id=center align=center style='width: 20px; height: 20px'></legend>
            </fieldset>
            <fieldset style='width: 200px'>
              <legend id=right style='width: 20px; height: 20px; justify-self: right'></legend>
            </fieldset>
            """);
        Assert.Equal(Rect(90, 0, 20, 20), GetRect(document, "center"));
        Assert.Equal(Rect(180, 20, 20, 20), GetRect(document, "right"));
    }

    [Fact]
    public void BlockSize_IncludesTheLegendThatSpillsOutOfTheBorder()
    {
        // "the space allocated for the rendered legend's margin box that spills out past the border, if any, is expected to be
        // subtracted from the 'block-size'": the contents have the 70px that remain
        var document = Parse("""
            <fieldset id=fieldset style='width: 200px; height: 100px'>
              <legend style='height: 30px'></legend>
              <div id=content style='height: 100%'></div>
            </fieldset>
            """);
        Assert.Equal(Rect(0, 0, 200, 100), GetRect(document, "fieldset"));
        Assert.Equal(Rect(0, 30, 200, 70), GetRect(document, "content"));
    }

    [Fact]
    public void Overflow_AppliesToTheContentBox()
    {
        // The legend is not in the scrolling box of the fieldset: it is not scrolled, and the scrollable overflow starts after it
        var document = Parse("""
            <fieldset id=fieldset style='width: 200px; height: 100px; overflow: hidden'>
              <legend id=legend style='height: 30px; width: 20px'></legend>
              <div id=content style='height: 300px'></div>
            </fieldset>
            """);
        var fieldset = document.GetElementById("fieldset")!;
        Assert.Equal(300, fieldset.ScrollHeight);
        fieldset.ScrollTop = 50;
        Assert.Equal(Rect(0, 0, 20, 30), GetRect(document, "legend"));
        Assert.Equal(Rect(0, -20, 200, 300), GetRect(document, "content"));
    }

    [Fact]
    public void Display_FlexAppliesToTheContentBox()
    {
        var document = Parse("""
            <fieldset style='display: flex; width: 200px'>
              <legend id=legend style='height: 30px; width: 20px'></legend>
              <div id=a style='flex: 1; height: 10px'></div>
              <div id=b style='flex: 1; height: 10px'></div>
            </fieldset>
            """);
        Assert.Equal(Rect(0, 0, 20, 30), GetRect(document, "legend"));
        Assert.Equal(Rect(0, 30, 100, 10), GetRect(document, "a"));
        Assert.Equal(Rect(100, 30, 100, 10), GetRect(document, "b"));
    }

    [Fact]
    public void IntrinsicSize_IsTheGreaterOfTheLegendAndTheContents()
    {
        var document = Parse("""
            <fieldset id=legendWider style='width: fit-content; padding: 0 5px'>
              <legend style='width: 80px; height: 10px'></legend>
              <div style='width: 30px; height: 10px'></div>
            </fieldset>
            <fieldset id=contentWider style='width: fit-content; padding: 0 5px'>
              <legend style='width: 30px; height: 10px'></legend>
              <div style='width: 80px; height: 10px'></div>
            </fieldset>
            """);
        Assert.Equal(90, GetRect(document, "legendWider").Width);
        Assert.Equal(90, GetRect(document, "contentWider").Width);
    }

    [Fact]
    public void FloatingLegend_IsNotARenderedLegend()
    {
        var document = Parse("""
            <fieldset style='width: 200px; border-width: 10px'>
              <legend id=legend style='float: left; width: 20px; height: 20px'></legend>
              <div id=content style='display: flow-root; height: 50px'></div>
            </fieldset>
            """);
        Assert.Equal(Rect(10, 10, 20, 20), GetRect(document, "legend"));
        Assert.Equal(Rect(30, 10, 180, 50), GetRect(document, "content"));
    }

    [Fact]
    public void CloneWithContentBoxBlockSize_AddsTheRepeatedEdges()
    {
        // The content box is 40px tall: 20px in each column, between the borders that are repeated (40px each)
        var document = Parse("""
            <div style='columns: 4; column-fill: auto; column-gap: 0; width: 200px; height: 100px'>
              <div id=box style='box-decoration-break: clone; border: solid; border-width: 40px 0; height: 40px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100)], GetClientRects(document, "box"));
    }

    [Fact]
    public void CloneWithBorderBoxBlockSize_TakesTheRepeatedEdgesFromIt()
    {
        var document = Parse("""
            <div style='columns: 4; column-fill: auto; column-gap: 0; width: 200px; height: 100px'>
              <div id=box style='box-decoration-break: clone; box-sizing: border-box; border: solid; border-width: 10px 0; height: 200px'></div>
            </div>
            """);
        Assert.Equal([Rect(0, 0, 50, 100), Rect(50, 0, 50, 100)], GetClientRects(document, "box"));
    }

    [Fact]
    public void CloneWithBlockSize_ContentsThatOverflowHaveNoRepeatedEdge()
    {
        // The box ends in the first column (15 + 70 + 15): its child is over its block-end border there (it is not a repeated
        // one), and continues at the start of the second column
        var document = Parse("""
            <div style='columns: 2; column-fill: auto; column-gap: 0; width: 100px; height: 100px'>
              <div id=box style='box-decoration-break: clone; border: solid; border-width: 15px 0; height: 70px'>
                <div id=child style='height: 185px'></div>
              </div>
            </div>
            """);
        Assert.Equal([Rect(0, 15, 50, 85), Rect(50, 0, 50, 100)], GetClientRects(document, "child"));
        Assert.Equal(Rect(0, 0, 50, 100), GetClientRects(document, "box")[0]);
    }

    [Fact]
    public void AbsolutelyPositionedBox_InARelativelyPositionedInlineBoxIsFragmentedFromItsStaticOffset()
    {
        // The containing block is an inline box that is relatively positioned: the box is placed in the columns from its
        // position before the relative positioning, and moved with its containing block
        var document = Parse("""
            <div style='columns: 2; column-fill: auto; column-gap: 0; width: 100px; height: 100px'>
              <div>
                <span style='position: relative; top: 150px; left: 10px'>
                  <div style='height: 200px; width: 50px'></div>
                  <div id=abs style='position: absolute; top: 50px; left: 0; width: 50px; height: 150px'></div>
                </span>
              </div>
            </div>
            """);
        Assert.Equal([Rect(10, 200, 50, 50), Rect(60, 150, 50, 100)], GetClientRects(document, "abs"));
    }

    [Fact]
    public void ContentBox_DoesNotFillTheMinimumBlockSize()
    {
        // The block-size: 100% of the anonymous fieldset content box behaves as auto when the block size of the fieldset depends on
        // its contents: justify-content does not center the item in the 200px of min-height (https://github.com/whatwg/html/issues/9123)
        var document = Parse("""
            <fieldset id=f style='display: flex; flex-flow: column; justify-content: center; width: 100px; min-height: 200px'>
              <div id=a style='height: 50px'></div>
            </fieldset>
            """);
        Assert.Equal(Rect(0, 0, 100, 200), GetRect(document, "f"));
        Assert.Equal(Rect(0, 0, 100, 50), GetRect(document, "a"));
    }
}
