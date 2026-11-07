using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// The baseline content-alignment of the grid items (https://drafts.csswg.org/css-align-3/#baseline-align-content):
/// <c>align-content: baseline</c> on the items of a row aligns the baselines of their contents, by adding space in their boxes.
/// </summary>
public sealed class GridContentBaselineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><style>body {{ margin: 0; font: 20px/1 Ahem }} .grid {{ display: grid; grid-template-columns: 100px 100px; width: 200px }}</style>{html}");
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    private static PhysicalRect GetRect(Document document, string id)
    {
        var rect = document.GetElementById(id)!.GetBoundingClientRect();
        return new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    [Fact]
    public void FirstBaseline_ContentsOfTheItemsOfARowShareTheirBaseline()
    {
        // The baselines are at 16px (20px font) and 8px (10px font): the contents of the second item are moved down by 8px in
        // its box; both items are stretched in the row
        var document = Parse("""
            <div class=grid>
              <div id=a style='align-content: baseline'><span id=sa>X</span></div>
              <div id=b style='align-content: baseline; font-size: 10px'><span id=sb>X</span></div>
            </div>
            """);
        Assert.Equal(Rect(0, 0, 100, 20), GetRect(document, "a"));
        Assert.Equal(Rect(100, 0, 100, 20), GetRect(document, "b"));
        Assert.Equal(0, GetRect(document, "sa").Y);
        Assert.Equal(8, GetRect(document, "sb").Y);
    }

    [Fact]
    public void FirstBaseline_TheShimIncreasesTheSizeOfTheRow()
    {
        // The item with the small font has a padding: its baseline is the lowest one (30px + 8px); the contents of the other
        // item are moved down by 22px, and the row is as tall as that item with its shim (42px)
        var document = Parse("""
            <div id=grid class=grid>
              <div id=a style='align-content: baseline'><span id=sa>X</span></div>
              <div id=b style='align-content: baseline; font-size: 10px; padding-top: 30px'><span id=sb>X</span></div>
            </div>
            """);
        Assert.Equal(22, GetRect(document, "sa").Y);
        Assert.Equal(30, GetRect(document, "sb").Y);
        Assert.Equal(42, GetRect(document, "grid").Height);
        Assert.Equal(42, GetRect(document, "a").Height);
    }

    [Fact]
    public void LastBaseline_ContentsAreAlignedFromTheEndOfTheRow()
    {
        // The last baselines are 4px (20px font) and 2px (10px font) above the block-end edges: the contents of the second
        // item end 2px before the end of its box
        var document = Parse("""
            <div class=grid>
              <div id=a style='align-content: last baseline'><span id=sa>X</span></div>
              <div id=b style='align-content: last baseline; font-size: 10px'><span id=sb>X</span></div>
            </div>
            """);
        Assert.Equal(Rect(100, 0, 100, 20), GetRect(document, "b"));
        Assert.Equal(0, GetRect(document, "sa").Y);
        Assert.Equal(8, GetRect(document, "sb").Y);
    }

    [Fact]
    public void Baseline_IsNotSharedByAnItemAlignedToTheOtherSide()
    {
        // align-self: end: the block-start edge of the box is not at the start of the row, the item does not participate
        var document = Parse("""
            <div class=grid>
              <div id=a style='align-content: baseline'><span id=sa>X</span></div>
              <div id=b style='align-content: baseline; align-self: end; font-size: 10px'><span id=sb>X</span></div>
            </div>
            """);
        Assert.Equal(Rect(100, 10, 100, 10), GetRect(document, "b"));
        Assert.Equal(10, GetRect(document, "sb").Y);
    }
}
