using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The line clamping (https://drafts.csswg.org/css-overflow-4/#line-clamp): the clamp point, the size of the line-clamp container, the hidden content and the block overflow ellipsis.</summary>
public sealed class LineClampTests : IDisposable
{
    private const string Ellipsis = "…";

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

    private static BoxFragment GetFragment(Document document, string id) => (BoxFragment)document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0];

    private static List<LineBoxFragment> GetLines(BoxFragment fragment) => [.. fragment.Children.OfType<LineBoxFragment>()];

    /// <summary>Returns the texts drawn by the display list.</summary>
    private static List<string> GetDrawnTexts(Document document)
        => [.. document.GetDisplayList()!.Dump().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("draw-glyphs", StringComparison.Ordinal)).Select(line => line.Split('"')[1])];

    [Fact]
    public void MaxLines_HidesTheLinesAfterTheClampPoint()
    {
        var document = Parse("<div id=c style='line-clamp: 2'>aa<br>bb<br>cc</div>");
        var fragment = GetFragment(document, "c");
        Assert.Equal(20, fragment.Size.Height);
        var lines = GetLines(fragment);
        Assert.Equal(3, lines.Count);
        Assert.False(lines[0].IsHiddenForPaint);
        Assert.False(lines[1].IsHiddenForPaint);
        Assert.True(lines[2].IsHiddenForPaint);
        Assert.NotNull(lines[1].BlockEllipsis);
        Assert.Equal(["aa", "bb", Ellipsis], GetDrawnTexts(document));
    }

    [Theory]
    [InlineData("line-clamp: 3")]
    [InlineData("line-clamp: 4")]
    [InlineData("line-clamp: none")]
    [InlineData("max-lines: 2")]
    [InlineData("-webkit-line-clamp: 2")]
    [InlineData("display: flow-root; -webkit-box-orient: vertical; -webkit-line-clamp: 2")]
    [InlineData("display: -webkit-box; -webkit-line-clamp: 2")]
    public void NotClamped(string style)
    {
        // Not more lines than the maximum, or not a line-clamp container (continue is auto, -webkit-legacy needs a vertical -webkit-box)
        var document = Parse("<div id=c style='" + style + "'><div>aa<br>bb<br>cc</div></div>");
        Assert.Equal(30, GetFragment(document, "c").Size.Height);
        Assert.DoesNotContain(Ellipsis, GetDrawnTexts(document));
    }

    [Fact]
    public void WebkitLineClamp_OnAVerticalWebkitBox()
    {
        var document = Parse("<div id=c style='display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2'>aa<br>bb<br>cc</div>");
        Assert.Equal("flow-root", document.DefaultView!.GetComputedStyle(document.GetElementById("c")!).GetPropertyValue("display"));
        Assert.Equal(20, GetFragment(document, "c").Size.Height);
        Assert.Equal(["aa", "bb", Ellipsis], GetDrawnTexts(document));
    }

    [Fact]
    public void LinesOfDescendants_AreCounted()
    {
        // The clamp point is in a descendant in the same block formatting context: it ends at the clamp point with its padding
        // and border, the boxes after it are hidden
        var document = Parse("<div id=c style='line-clamp: 2'>aa<div id=inner style='padding-bottom: 5px'>bb<br>cc</div><div id=after>dd</div></div>");
        Assert.Equal(15, GetFragment(document, "inner").Size.Height);
        Assert.Equal(25, GetFragment(document, "c").Size.Height);
        Assert.True(GetFragment(document, "after").IsHiddenForPaint);
        Assert.Equal(["aa", "bb", Ellipsis], GetDrawnTexts(document));
    }

    [Fact]
    public void IndependentFormattingContexts_AreSkipped()
    {
        var document = Parse("<div id=c style='line-clamp: 2'>aa<div style='display: flow-root'>bb<br>cc</div>dd<br>ee</div>");
        Assert.Equal(40, GetFragment(document, "c").Size.Height);
        Assert.Equal(["aa", "bb", "cc", "dd", Ellipsis], GetDrawnTexts(document));
    }

    [Fact]
    public void BlockEllipsis_DisplacesTheContentAtTheLastSoftWrapOpportunity()
    {
        // "aaa bbb" and the ellipsis do not fit in 70px: "bbb" is displaced, the ellipsis follows "aaa" (the space is removed)
        var document = Parse("<div id=c style='line-clamp: 1; width: 70px'>aaa bbb<br>ccc</div>");
        var line = GetLines(GetFragment(document, "c"))[0];
        Assert.Equal(30, line.BlockEllipsis!.LineLeft);
        Assert.Equal(["aaa", Ellipsis], GetDrawnTexts(document));
    }

    [Fact]
    public void BlockEllipsis_DisplacesTheWholeLine()
    {
        // No soft wrap opportunity fits with the ellipsis: the line keeps the strut of its root inline box
        var document = Parse("<div id=c style='line-clamp: 1; width: 50px'>aaaaa<br>ccc</div>");
        var fragment = GetFragment(document, "c");
        Assert.Equal(10, fragment.Size.Height);
        Assert.Equal(0, GetLines(fragment)[0].BlockEllipsis!.LineLeft);
        Assert.Equal([Ellipsis], GetDrawnTexts(document));
    }

    [Fact]
    public void BlockEllipsis_String()
    {
        var document = Parse("<div id=c style='line-clamp: 1 \"[more]\"'>aa<br>bb</div>");
        Assert.Equal(["aa", "[more]"], GetDrawnTexts(document));
    }

    [Fact]
    public void BlockEllipsis_None()
    {
        var document = Parse("<div id=c style='line-clamp: 1 no-ellipsis'>aa<br>bb</div>");
        Assert.Equal(10, GetFragment(document, "c").Size.Height);
        Assert.Equal(["aa"], GetDrawnTexts(document));
    }

    [Fact]
    public void BlockEllipsis_RightToLeft()
    {
        // The ellipsis is at the end of the line: on its left in a right-to-left paragraph
        var document = Parse("<div id=c style='line-clamp: 1; width: 100px; direction: rtl'>aa<br>bb</div>");
        var line = GetLines(GetFragment(document, "c"))[0];
        Assert.Equal(70, line.BlockEllipsis!.LineLeft);
    }

    [Fact]
    public void WebkitLineClamp_TruncatesTheLastLine()
    {
        // As in Blink, the content of the last line that fits without the ellipsis but not with it is truncated (not displaced)
        var document = Parse("<div id=c style='display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 1; width: 50px; overflow: hidden'>aaaaa<br>bb</div>");
        var line = GetLines(GetFragment(document, "c"))[0];
        Assert.True(line.IsTruncatedByBlockEllipsis);
        Assert.Contains(Ellipsis, GetDrawnTexts(document));
    }

    [Fact]
    public void WebkitLineClamp_DisplacesTheContentWiderThanTheLine()
    {
        // The content that does not fit without the ellipsis either is displaced, as with line-clamp (WPT
        // css/css-overflow/line-clamp/webkit-line-clamp-035.html)
        var document = Parse("<div id=c style='display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; width: 50px; overflow: hidden'>aa<br>aaaaaaaa<br>bb</div>");
        var line = GetLines(GetFragment(document, "c"))[1];
        Assert.False(line.IsTruncatedByBlockEllipsis);
        Assert.DoesNotContain("aaaaaaaa", GetDrawnTexts(document));
        Assert.Contains(Ellipsis, GetDrawnTexts(document));
    }

    [Theory]
    [InlineData("max-height: 30px", 30)]
    [InlineData("max-height: 35px", 30)]
    [InlineData("height: 35px", 35)]
    [InlineData("max-height: 5px", 0)]
    [InlineData("max-height: 100px", 50)]
    public void Auto_ClampsByTheBlockSize(string style, double height)
    {
        var document = Parse("<div id=c style='line-clamp: auto; " + style + "'>aa<br>bb<br>cc<br>dd<br>ee</div>");
        Assert.Equal(height, GetFragment(document, "c").Size.Height);
    }

    [Fact]
    public void Auto_AndMaxLines_TheEarliestClampPoint()
    {
        var document = Parse("<div id=c style='line-clamp: 4 auto; max-height: 30px'>aa<br>bb<br>cc<br>dd<br>ee</div>");
        Assert.Equal(30, GetFragment(document, "c").Size.Height);
        document = Parse("<div id=d style='line-clamp: 2 auto; max-height: 30px'>aa<br>bb<br>cc<br>dd<br>ee</div>");
        Assert.Equal(20, GetFragment(document, "d").Size.Height);
    }

    [Fact]
    public void ContinueCollapse_ClampsByTheBlockSizeWithoutEllipsis()
    {
        var document = Parse("<div id=c style='continue: collapse; max-height: 20px'>aa<br>bb<br>cc</div>");
        Assert.Equal(20, GetFragment(document, "c").Size.Height);
        Assert.Equal(["aa", "bb"], GetDrawnTexts(document));
    }

    [Fact]
    public void AbsolutelyPositionedBoxes_HiddenWhenTheirContainingBlockIsAfterTheClampPoint()
    {
        var document = Parse(
            "<div id=c style='line-clamp: 1'>aa<div style='position: absolute' id=shown>x</div>" +
            "<div style='position: relative'>bb<div style='position: absolute' id=hidden>y</div></div></div>");
        Assert.False(GetFragment(document, "shown").IsHiddenForPaint);
        Assert.Equal(["aa", Ellipsis, "x"], GetDrawnTexts(document));
    }

    [Fact]
    public void HiddenContent_IsNotScrollableOverflow()
    {
        var document = Parse("<div id=s style='overflow: auto; height: 20px'><div style='line-clamp: 2'>aa<br>bb<br>cc<br>dd</div></div>");
        Assert.Equal(20, document.GetElementById("s")!.ScrollHeight);
    }

    [Fact]
    public void Floats_ClippedToTheContentEdge()
    {
        var document = Parse("<div id=c style='line-clamp: 1'><div style='float: left; width: 10px; height: 50px; background: green'></div>aa<br>bb</div>");
        Assert.Equal(10, GetFragment(document, "c").Size.Height);
        Assert.Contains(document.GetDisplayList()!.Dump().Split('\n'), line => line.Trim().StartsWith("clip-rect (-10000000, -10000000, 20000000x10000010)", StringComparison.Ordinal));
    }

    [Fact]
    public void Relayout_WhenTheMaximumNumberOfLinesChanges()
    {
        var document = Parse("<div id=c style='line-clamp: 1'>aa<br>bb<br>cc</div>");
        Assert.Equal(10, GetFragment(document, "c").Size.Height);
        document.GetElementById("c")!.SetAttribute("style", "line-clamp: 2");
        Assert.Equal(20, GetFragment(document, "c").Size.Height);
    }
}
