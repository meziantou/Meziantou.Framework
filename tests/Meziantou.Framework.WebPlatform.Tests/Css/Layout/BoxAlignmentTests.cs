using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The box alignment (https://drafts.csswg.org/css-align-3/) of the block containers, block-level boxes, absolutely positioned boxes and table cells, measured with Ahem.</summary>
public sealed class BoxAlignmentTests : IDisposable
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

    // ------------------------------------------------------------------------------------------------------------------
    // Shared resolution
    // ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Start", "ltr", "Start")]
    [InlineData("Start", "rtl", "Start")]
    [InlineData("End", "ltr", "End")]
    [InlineData("Left", "ltr", "Start")]
    [InlineData("Left", "rtl", "End")]
    [InlineData("Right", "rtl", "Start")]
    [InlineData("FlexEnd", "ltr", "End")]
    [InlineData("SelfStart", "ltr", "Start")]
    [InlineData("Center", "ltr", "Center")]
    [InlineData("Normal", "ltr", "Stretch")]
    public void ResolveSelfAlignment_InlineAxis(string keyword, string direction, string expected)
    {
        var container = new WritingDirection(WritingMode.HorizontalTb, direction == "rtl" ? Direction.Rtl : Direction.Ltr);
        var alignment = BoxAlignment.ResolveSelfAlignment(Enum.Parse<AlignmentKeyword>(keyword), AlignmentAxis.Inline(container), container, AxisAlignment.Stretch);
        Assert.Equal(Enum.Parse<AxisAlignment>(expected), alignment);
    }

    [Fact]
    public void ResolveSelfAlignment_SelfStartUsesTheWritingModeOfTheSubject()
    {
        // A vertical-rl rtl subject in the vertical block axis of a horizontal container: its inline-start side is the bottom
        var axis = AlignmentAxis.Block(WritingDirection.HorizontalLtr);
        var subject = new WritingDirection(WritingMode.VerticalRl, Direction.Rtl);
        Assert.Equal(AxisAlignment.End, BoxAlignment.ResolveSelfAlignment(AlignmentKeyword.SelfStart, axis, subject, AxisAlignment.Start));
        Assert.Equal(AxisAlignment.Start, BoxAlignment.ResolveSelfAlignment(AlignmentKeyword.SelfEnd, axis, subject, AxisAlignment.Start));
    }

    [Fact]
    public void ResolveSelfAlignment_LeftInAVerticalInlineAxisIsTheLineLeftSide()
    {
        var container = new WritingDirection(WritingMode.VerticalRl, Direction.Rtl);
        var axis = AlignmentAxis.Inline(container);
        Assert.Equal(AxisAlignment.End, BoxAlignment.ResolveSelfAlignment(AlignmentKeyword.Left, axis, container, AxisAlignment.Start));
        Assert.Equal(AxisAlignment.Start, BoxAlignment.ResolveSelfAlignment(AlignmentKeyword.Right, axis, container, AxisAlignment.Start));
    }

    [Theory]
    [InlineData("SpaceBetween", 30, 4, 0, 10)]
    [InlineData("SpaceBetween", 30, 1, 0, 0)]
    [InlineData("SpaceBetween", -30, 4, 0, 0)]
    [InlineData("SpaceAround", 40, 4, 5, 10)]
    [InlineData("SpaceAround", -40, 4, 0, 0)]
    [InlineData("SpaceEvenly", 40, 3, 10, 10)]
    [InlineData("Center", -40, 3, -20, 0)]
    [InlineData("End", 40, 3, 40, 0)]
    [InlineData("Stretch", 40, 3, 0, 0)]
    public void Distribute(string alignment, double freeSpace, int count, double offset, double between)
        => Assert.Equal((offset, between), BoxAlignment.Distribute(Enum.Parse<AxisAlignment>(alignment), freeSpace, count, safe: false));

    [Fact]
    public void Distribute_SafeStartAlignsAnOverflow()
        => Assert.Equal((0d, 0d), BoxAlignment.Distribute(AxisAlignment.End, -40, 1, safe: true));

    // ------------------------------------------------------------------------------------------------------------------
    // Block containers: align-content
    // ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("start", 0)]
    [InlineData("center", 40)]
    [InlineData("end", 80)]
    [InlineData("flex-end", 80)]
    [InlineData("space-between", 0)]
    [InlineData("space-around", 40)]
    [InlineData("space-evenly", 40)]
    [InlineData("baseline", 0)]
    [InlineData("last baseline", 80)]
    [InlineData("normal", 0)]
    public void AlignContent_BlockContainer(string value, double expected)
    {
        var document = Parse($"<div id=c style='height: 100px; align-content: {value}'><div id=a style='height: 20px'></div></div>");
        Assert.Equal(expected, GetRect(document, "a").Y);
    }

    [Fact]
    public void AlignContent_InlineContent()
    {
        var document = Parse("<div style='height: 100px; align-content: end'><span id=a>X</span></div>");
        Assert.Equal(90, GetRect(document, "a").Y);
    }

    [Theory]
    [InlineData("center", 0)]
    [InlineData("safe center", 0)]
    [InlineData("unsafe center", -10)]
    [InlineData("unsafe end", -20)]
    public void AlignContent_OverflowIsSafeByDefault(string value, double expected)
    {
        var document = Parse($"<div style='height: 20px; align-content: {value}'><div id=a style='height: 40px'></div></div>");
        Assert.Equal(expected, GetRect(document, "a").Y);
    }

    [Fact]
    public void AlignContent_EstablishesAnIndependentFormattingContext()
    {
        // The margins of the child do not collapse with the ones of the container, the floats are aligned with the contents
        var document = Parse("""
            <div id=c style='height: 100px; align-content: end'>
              <div id=f style='float: left; width: 10px; height: 30px'></div>
              <div id=a style='margin-top: 10px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(0, GetRect(document, "c").Y);
        Assert.Equal(70, GetRect(document, "f").Y);
        Assert.Equal(80, GetRect(document, "a").Y);
    }

    [Fact]
    public void AlignContent_MovesTheStaticPositionOfAbsolutelyPositionedBoxes()
    {
        var document = Parse("""
            <div style='position: relative; height: 100px; align-content: center'>
              <div style='height: 20px'></div>
              <div id=abs style='position: absolute; height: 10px; width: 10px'></div>
            </div>
            """);
        Assert.Equal(60, GetRect(document, "abs").Y);
    }

    [Fact]
    public void AlignContent_TableCell()
    {
        var document = Parse("""
            <table style='border-spacing: 0'><tr>
              <td style='padding: 0; height: 50px; align-content: end'><div id=a style='height: 10px'></div></td>
              <td style='padding: 0; align-content: unsafe center'><div id=b style='height: 10px'></div></td>
            </tr></table>
            """);
        Assert.Equal(40, GetRect(document, "a").Y);
        Assert.Equal(20, GetRect(document, "b").Y);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Block-level boxes: justify-self
    // ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("start", "ltr", 0)]
    [InlineData("center", "ltr", 350)]
    [InlineData("end", "ltr", 700)]
    [InlineData("end", "rtl", 0)]
    [InlineData("left", "rtl", 0)]
    [InlineData("right", "ltr", 700)]
    [InlineData("flex-end", "ltr", 700)]
    [InlineData("self-end", "ltr", 700)]
    [InlineData("normal", "ltr", 0)]
    [InlineData("normal", "rtl", 700)]
    public void JustifySelf_BlockLevelBox(string value, string direction, double expected)
    {
        var document = Parse($"<div dir={direction}><div id=a style='width: 100px; height: 10px; justify-self: {value}'></div></div>");
        Assert.Equal(expected, GetRect(document, "a").X);
    }

    [Fact]
    public void JustifySelf_AutoSizeIsFitContent()
    {
        var document = Parse("<div id=a style='justify-self: center'>XXX</div><div id=b style='justify-self: stretch'>XXX</div>");
        Assert.Equal(new PhysicalRect(385, 0, 30, 10), GetRect(document, "a"));
        Assert.Equal(800, GetRect(document, "b").Width);
    }

    [Fact]
    public void JustifySelf_AutoUsesTheJustifyItemsOfTheParent()
    {
        var document = Parse("<div style='justify-items: end'><div id=a style='width: 100px; height: 10px'></div><div id=b style='width: 100px; height: 10px; justify-self: start'></div></div>");
        Assert.Equal(700, GetRect(document, "a").X);
        Assert.Equal(0, GetRect(document, "b").X);
    }

    [Fact]
    public void JustifySelf_AutoMarginsTakePrecedence()
    {
        var document = Parse("<div id=a style='width: 100px; height: 10px; margin-left: auto; justify-self: start'></div>");
        Assert.Equal(700, GetRect(document, "a").X);
    }

    [Theory]
    [InlineData("end", -100)]
    [InlineData("safe end", 0)]
    public void JustifySelf_Overflow(string value, double expected)
    {
        var document = Parse($"<div style='width: 100px'><div id=a style='width: 200px; height: 10px; justify-self: {value}'></div></div>");
        Assert.Equal(expected, GetRect(document, "a").X);
    }

    [Fact]
    public void JustifySelf_OrthogonalBox()
    {
        var document = Parse("""
            <div style='width: 100px'>
              <div id=a style='writing-mode: vertical-rl; justify-self: center'><div style='width: 20px; height: 20px'></div></div>
              <div id=b style='writing-mode: vertical-rl; justify-self: stretch'><div style='width: 20px; height: 20px'></div></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(40, 0, 20, 20), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(0, 20, 100, 20), GetRect(document, "b"));
    }

    [Fact]
    public void JustifySelf_Table()
    {
        var document = Parse("<table id=t style='justify-self: end; border-spacing: 0'><tr><td style='padding: 0'><div style='width: 50px; height: 10px'></div></td></tr></table>");
        Assert.Equal(750, GetRect(document, "t").X);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Absolutely positioned boxes
    // ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("normal", 0, 100)]
    [InlineData("start", 0, 20)]
    [InlineData("center", 40, 20)]
    [InlineData("end", 80, 20)]
    [InlineData("stretch", 0, 100)]
    [InlineData("baseline", 0, 20)]
    [InlineData("last baseline", 80, 20)]
    public void Abspos_JustifySelf(string value, double x, double width)
    {
        var document = Parse($"<div style='position: relative; width: 100px; height: 100px'><div id=a style='position: absolute; inset: 0; justify-self: {value}'><div style='width: 20px; height: 20px'></div></div></div>");
        var rect = GetRect(document, "a");
        Assert.Equal(x, rect.X);
        Assert.Equal(width, rect.Width);
    }

    [Theory]
    [InlineData("center", "top: 20px; bottom: 10px", 20, 45)]
    [InlineData("center", "top: 20px; bottom: 10px", 80, 15)]
    [InlineData("center", "top: 20px; bottom: 10px", 95, 5)]
    [InlineData("center", "top: 20px; bottom: 10px", 120, 0)]
    [InlineData("unsafe center", "top: 20px; bottom: 10px", 120, -5)]
    [InlineData("safe end", "top: 20px; bottom: 10px", 120, 20)]
    public void Abspos_DefaultOverflowAlignment(string value, string insets, double height, double expectedY)
    {
        var document = Parse($"<div style='position: relative; width: 100px; height: 100px'><div id=a style='position: absolute; {insets}; align-self: {value}'><div style='width: 20px; height: {height}px'></div></div></div>");
        Assert.Equal(expectedY, GetRect(document, "a").Y);
    }

    [Fact]
    public void Abspos_DefaultOverflowAlignmentExtendsIntoTheScrollableOverflow()
    {
        var document = Parse("<div id=c style='position: relative; overflow: scroll; scrollbar-width: none; width: 100px; height: 100px'><div style='position: absolute; inset: 50px; width: 150px; height: 150px; place-self: start'></div></div>");
        var container = document.GetElementById("c")!;
        Assert.Equal(200, container.ScrollWidth);
        Assert.Equal(200, container.ScrollHeight);
    }

    [Fact]
    public void Abspos_StretchAppliesToReplacedElementsAndTables()
    {
        var document = Parse("""
            <div style='position: relative; width: 40px; height: 40px'>
              <canvas id=c width=10 height=10 style='position: absolute; inset: 5px; place-self: stretch'></canvas>
              <div id=t style='display: table; position: absolute; inset: 5px; align-self: stretch'><div style='width: 10px; height: 10px'></div></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(5, 5, 30, 30), GetRect(document, "c"));
        Assert.Equal(new PhysicalRect(5, 5, 10, 30), GetRect(document, "t"));
    }

    [Fact]
    public void Abspos_StaticPositionInBlockLayoutIsAligned()
    {
        var document = Parse("""
            <div style='position: relative; width: 100px'>
              <div style='width: 80px; justify-items: center'>
                <div id=a style='position: absolute; width: 20px; height: 10px'></div>
                <div id=b style='position: absolute; width: 20px; height: 10px; justify-self: end'></div>
              </div>
            </div>
            """);
        Assert.Equal(30, GetRect(document, "a").X);
        Assert.Equal(60, GetRect(document, "b").X);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Baselines
    // ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void InlineBlockBaseline_OnlyBlockAxisScrollContainersAreSynthesized()
    {
        var document = Parse("""
            <div style='line-height: 100px'>
              <div id=x style='display: inline-block; width: 50px; height: 50px; overflow-x: scroll; overflow-y: clip; line-height: 10px'>X</div>
              <div id=y style='display: inline-block; width: 50px; height: 50px; overflow-x: clip; overflow-y: scroll; line-height: 10px'>X</div>
            </div>
            """);

        // The baseline of #x is its first line (8px from its top), the one of #y its bottom margin edge
        Assert.Equal(GetRect(document, "x").Y + 8, GetRect(document, "y").Bottom);
    }
}
