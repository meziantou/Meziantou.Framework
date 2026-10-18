using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The gap decorations (https://drafts.csswg.org/css-gaps-1/#gap-decorations) of the grid, flex and multi-column containers.</summary>
public sealed class GapDecorationsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem; orphans: 1; widows: 1 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect Rect(double x, double y, double width, double height) => new(x, y, width, height);

    /// <summary>Returns the rectangles of the gap decorations of the fragments of an element, in painting order.</summary>
    private static List<PhysicalRect>[] GetDecorations(Document document, string id = "c")
        => [.. document.GetLayoutEngine().GetFragments(document.GetElementById(id)!).Select(fragment => (GapDecorations.GetDecorations(fragment) ?? []).ConvertAll(decoration => decoration.Rect))];

    private const string Grid = "display: grid; grid-template: 100px 100px / 100px 100px; gap: 10px; width: 210px;";

    private const string SpanningItems = "<div style='grid-column: 1 / 3'></div><div></div><div></div>";

    [Fact]
    public void MultiColumn_RulesAreBetweenTheColumnsWithContent()
    {
        var document = Parse("<div id=c style='width: 320px; columns: 3; column-gap: 10px; column-rule: 2px solid'><div style='height: 300px'></div></div>");
        Assert.Equal([Rect(104, 0, 2, 100), Rect(214, 0, 2, 100)], GetDecorations(document)[0]);

        // "Column rules are only drawn between two columns that both have content", unless rule-visibility-items says otherwise
        document = Parse("<div id=c style='width: 320px; height: 100px; columns: 3; column-fill: auto; column-gap: 10px; column-rule: 2px solid'><div style='height: 150px'></div></div>");
        Assert.Equal([Rect(104, 0, 2, 100)], GetDecorations(document)[0]);
        document = Parse("<div id=c style='width: 320px; height: 100px; columns: 3; column-fill: auto; column-gap: 10px; column-rule: 2px solid; column-rule-visibility-items: all'><div style='height: 150px'></div></div>");
        Assert.Equal([Rect(104, 0, 2, 100), Rect(214, 0, 2, 100)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Grid_RulesBreakAtTheSpanningItems()
    {
        // The column rule starts after the item that spans the two columns; the row rule continues through the intersection
        var document = Parse($"<div id=c style='{Grid} rule: 4px solid'>{SpanningItems}</div>");
        Assert.Equal([Rect(103, 110, 4, 100), Rect(0, 103, 210, 4)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Grid_RuleBreak()
    {
        // intersection: the row rule breaks at the intersection that is not flanked by spanning items
        var document = Parse($"<div id=c style='{Grid} rule: 4px solid; rule-break: intersection'>{SpanningItems}</div>");
        Assert.Equal([Rect(103, 110, 4, 100), Rect(0, 103, 100, 4), Rect(110, 103, 100, 4)], GetDecorations(document)[0]);

        // none: one decoration from one end of the gap to the other, behind the items
        document = Parse($"<div id=c style='{Grid} rule: 4px solid; rule-break: none'>{SpanningItems}</div>");
        Assert.Equal([Rect(103, 0, 4, 210), Rect(0, 103, 210, 4)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Grid_VisibilityItemsAndInsets()
    {
        // The cell after the second row of the column gap is empty: the rule is only painted between the items of the first row
        var style = Grid + " column-rule: 2px solid; column-rule-visibility-items: between;";
        var document = Parse($"<div id=c style='{style}'><div></div><div></div><div></div></div>");
        Assert.Equal([Rect(104, 0, 2, 100)], GetDecorations(document)[0]);

        // The start of the rule is a cap (the edge of the content), its end a junction: the percentages resolve against the crossing gap
        document = Parse($"<div id=c style='{style} column-rule-inset-cap-start: 10px; column-rule-inset-junction-end: 50%'><div></div><div></div><div></div></div>");
        Assert.Equal([Rect(104, 10, 2, 85)], GetDecorations(document)[0]);

        // overlap-join extends the rule to the far edge of the crossing rule
        document = Parse($"<div id=c style='{Grid} rule: 2px solid; rule-break: intersection; column-rule-inset-junction: overlap-join'><div></div><div></div><div></div><div></div></div>");
        Assert.Equal([Rect(104, 0, 2, 106), Rect(104, 104, 2, 106), Rect(0, 104, 100, 2), Rect(110, 104, 100, 2)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Lists_AreAssignedToTheGaps()
    {
        // The leading value, the values of the auto repeater, then the trailing value
        var document = Parse("""
            <div id=c style='display: grid; grid-template: 50px / repeat(4, 50px); gap: 10px; width: 230px; column-rule-style: solid; column-rule-width: 2px, repeat(auto, 4px), 6px'>
              <div></div><div></div><div></div><div></div>
            </div>
            """);
        Assert.Equal([Rect(54, 0, 2, 50), Rect(113, 0, 4, 50), Rect(172, 0, 6, 50)], GetDecorations(document)[0]);
    }

    [Fact]
    public void RuleOverlap_ColumnOverRow()
    {
        var document = Parse($"<div id=c style='{Grid} rule: 4px solid; rule-overlap: column-over-row'>{SpanningItems}</div>");
        Assert.Equal([Rect(0, 103, 210, 4), Rect(103, 110, 4, 100)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Flex_GapsOfTheItemsAndOfTheLines()
    {
        const string Items = "<div style='width: 100px; height: 50px'></div><div style='width: 100px; height: 50px'></div><div style='width: 100px; height: 50px'></div>";

        // The gap between the items is confined to its line; the gap between the lines runs across the container (break: none)
        var document = Parse($"<div id=c style='display: flex; flex-wrap: wrap; width: 210px; gap: 10px; rule: 2px solid'>{Items}</div>");
        Assert.Equal([Rect(104, 0, 2, 50), Rect(0, 54, 210, 2)], GetDecorations(document)[0]);

        // intersection: the rule of the gap between the lines breaks where the gap of the first line abuts it
        document = Parse($"<div id=c style='display: flex; flex-wrap: wrap; width: 210px; gap: 10px; rule: 2px solid; row-rule-break: intersection'>{Items}</div>");
        Assert.Equal([Rect(104, 0, 2, 50), Rect(0, 54, 100, 2), Rect(110, 54, 100, 2)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Flex_ValuesAreAssignedInThePlacementOrder()
    {
        var document = Parse("""
            <div id=c style='display: flex; flex-direction: row-reverse; width: 170px; gap: 10px; column-rule-style: solid; column-rule-width: 2px, 4px'>
              <div style='width: 50px; height: 20px'></div><div style='width: 50px; height: 20px'></div><div style='width: 50px; height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(53, 0, 4, 20), Rect(114, 0, 2, 20)], GetDecorations(document)[0]);
    }

    [Fact]
    public void GridLanes_GuttersOfTheTracksAndOfTheItems()
    {
        // The gutter before the second item of the second track is confined to its track
        var document = Parse("""
            <div id=c style='display: grid-lanes; grid-template-columns: 100px 100px; gap: 10px; width: 210px; rule: 2px solid; flow-tolerance: 0'>
              <div style='height: 50px'></div><div style='height: 30px'></div><div style='height: 20px'></div>
            </div>
            """);
        Assert.Equal([Rect(104, 0, 2, 60), Rect(110, 34, 100, 2)], GetDecorations(document)[0]);
    }

    [Fact]
    public void Fragmentation_TheGapAtABreakDisappears()
    {
        // The break is in the row gap: the second row starts the second column, no row rule is painted, and the column rule runs to
        // the end of the first fragment
        var document = Parse("""
            <div style='columns: 2; column-fill: auto; column-gap: 10px; width: 210px; height: 85px'>
              <div id=c style='display: grid; grid-template: 80px 80px / 45px 45px; gap: 10px; rule: 2px solid'>
                <div></div><div></div><div id=item></div><div></div>
              </div>
            </div>
            """);
        var rect = document.GetElementById("item")!.GetBoundingClientRect();
        Assert.Equal((110d, 0d), (rect.X, rect.Y));
        var decorations = GetDecorations(document);
        Assert.Equal(2, decorations.Length);
        Assert.Equal([Rect(49, 0, 2, 85)], decorations[0]);
        Assert.Equal([Rect(49, 0, 2, 80)], decorations[1]);
    }

    [Fact]
    public void Fragmentation_TheGapsFollowTheItemsOfTheLinesOfAColumnFlexContainer()
    {
        // The two lines of the column container are parallel flows: the second item of the first line is monolithic and pushed
        // to the second column, where the gap before it disappears; the gap between the items of the second line stays in the
        // first column, and the gap after the pushed item follows it in the second column
        var document = Parse("""
            <div style='columns: 2; column-fill: auto; column-gap: 10px; width: 210px; height: 100px'>
              <div id=c style='display: flex; flex-direction: column; flex-wrap: wrap; height: 160px; gap: 10px; row-rule: 2px solid'>
                <div style='width: 40px; height: 60px'></div><div id=b style='width: 40px; height: 60px; contain: size'></div><div id=d style='width: 40px; height: 20px'></div>
                <div style='width: 40px; height: 30px'></div><div style='width: 40px; height: 30px'></div>
              </div>
            </div>
            """);
        var b = document.GetElementById("b")!.GetBoundingClientRect();
        var d = document.GetElementById("d")!.GetBoundingClientRect();
        Assert.Equal((110d, 0d, 60d), (b.X, b.Y, b.Height));
        Assert.Equal((110d, 70d, 20d), (d.X, d.Y, d.Height));
        var decorations = GetDecorations(document);
        Assert.Equal(2, decorations.Length);
        Assert.Equal([Rect(55, 34, 45, 2)], decorations[0]);
        Assert.Equal([Rect(0, 64, 45, 2)], decorations[1]);
    }
}
