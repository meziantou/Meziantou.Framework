using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

public sealed class BlockFormattingContextTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0 }</style>" + html);

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => GetRect(document, document.GetElementById(id)!);

    private static PhysicalRect GetRect(Document document, Element element) => document.GetLayoutEngine().GetBorderBoxRect(element) ?? throw new InvalidOperationException("No box");

    private static BoxFragment GetFragment(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0];

    [Fact]
    public void Siblings_MarginsCollapse()
    {
        var document = Parse("""
            <div id=a style='height: 10px; margin-bottom: 20px'></div>
            <div id=b style='height: 10px; margin-top: 30px'></div>
            <div id=c style='height: 10px; margin-top: 10px'></div>
            """);
        Assert.Equal(0, GetRect(document, "a").Y);
        Assert.Equal(40, GetRect(document, "b").Y);
        Assert.Equal(60, GetRect(document, "c").Y);
        Assert.Equal(70, GetRect(document, document.DocumentElement!).Height);
    }

    [Theory]
    [InlineData("<center><div id=t style='width: 100px; height: 10px'></div></center>", 350)]
    [InlineData("<div align=center><div id=t style='width: 100px; height: 10px'></div></div>", 350)]
    [InlineData("<div align=MIDDLE><p><div id=t style='width: 100px; height: 10px'></div></div>", 350)]
    [InlineData("<div align=right><div id=t style='width: 100px; height: 10px'></div></div>", 700)]
    [InlineData("<div align=right><div id=t style='width: 100px; height: 10px; margin-right: 20px'></div></div>", 680)]
    [InlineData("<div align=left dir=rtl><div id=t style='width: 100px; height: 10px'></div></div>", 0)]
    [InlineData("<div align=justify dir=rtl><div id=t style='width: 100px; height: 10px'></div></div>", 0)]
    [InlineData("<div align=center><div align=left><div id=t style='width: 100px; height: 10px'></div></div></div>", 0)]
    [InlineData("<div align=center><div id=t align=left style='width: 100px; height: 10px'></div></div>", 350)]
    [InlineData("<div align=center><div id=t style='width: 100px; height: 10px; margin-left: auto'></div></div>", 700)]
    [InlineData("<div align=center><div id=t style='width: 900px; height: 10px'></div></div>", 0)]
    [InlineData("<div align=center><fieldset style='margin: 0; padding: 0; border: 0'><legend id=t style='padding: 0; width: 100px'>x</legend></fieldset></div>", 0)]
    [InlineData("<table style='border-spacing: 0; width: 100%'><tr><td align=center style='padding: 0'><div id=t style='width: 100px; height: 10px'></div></table>", 350)]
    [InlineData("<center><table style='border-spacing: 0; width: 100%'><tr><td style='padding: 0'><div id=t style='width: 100px; height: 10px'></div></table></center>", 0)] // The alignment does not cross a table (as in the browsers)
    [InlineData("<div align=right><table style='border-spacing: 0; width: 100%'><tr><td style='padding: 0'><table id=t style='width: 100px'></table></table></div>", 0)]
    public void AlignDescendants(string html, double expectedX)
    {
        // https://html.spec.whatwg.org/multipage/rendering.html#align-descendants: the center element and the align attribute
        // align the block-level descendants whose inline margins are not auto
        var document = Parse(html);
        Assert.Equal(expectedX, GetRect(document, "t").X);
    }

    [Theory]
    [InlineData(20, -5, 25)]
    [InlineData(-5, 20, 25)]
    [InlineData(-10, -5, 0)]
    [InlineData(-5, -10, 0)]
    [InlineData(0, -20, -10)]
    public void Siblings_NegativeMargins(int bottom, int top, int expected)
    {
        // The largest negative margin is deducted from the largest positive margin
        var document = Parse($"""
            <div style='height: 10px; margin-bottom: {bottom}px'></div>
            <div id=b style='height: 10px; margin-top: {top}px'></div>
            """);
        Assert.Equal(expected, GetRect(document, "b").Y);
    }

    [Fact]
    public void Siblings_MixedPositiveAndNegative()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 30px'></div>
            <div id=empty style='margin-top: -10px; margin-bottom: 15px'></div>
            <div id=b style='height: 10px; margin-top: -25px'></div>
            """);

        // max(30, 15) + min(-10, -25) = 5
        Assert.Equal(15, GetRect(document, "b").Y);
    }

    [Fact]
    public void ParentAndFirstChild_MarginsCollapse()
    {
        var document = Parse("""
            <div id=parent style='margin-top: 10px'>
              <div id=child style='margin-top: 30px; height: 10px'></div>
            </div>
            """);

        // The margins collapse through the parent and the body: the body and the parent start at 30
        Assert.Equal(new PhysicalRect(0, 30, 800, 10), GetRect(document, "parent"));
        Assert.Equal(new PhysicalRect(0, 30, 800, 10), GetRect(document, "child"));
        Assert.Equal(new PhysicalRect(0, 30, 800, 10), GetRect(document, document.Body!));

        // The root element does not collapse its margins with its children
        Assert.Equal(new PhysicalRect(0, 0, 800, 40), GetRect(document, document.DocumentElement!));

        var parent = GetFragment(document, "parent");
        Assert.Equal(new PhysicalOffset(0, 0), parent.Children[0].Offset);
        Assert.Equal(30, parent.CollapsedMargins.Start.Sum);
        Assert.False(parent.CollapsedMargins.CollapsedThrough);
        Assert.Equal(10, parent.Margin.Top);
    }

    [Theory]
    [InlineData("border-top: 1px solid", 10, 41, 51)]
    [InlineData("padding-top: 2px", 10, 42, 52)]
    [InlineData("display: flow-root", 10, 40, 50)]
    [InlineData("overflow: hidden", 10, 40, 50)]
    [InlineData("overflow: clip", 30, 30, 40)]
    [InlineData("contain: layout", 10, 40, 50)]
    [InlineData("column-count: 2", 10, 40, 50)]
    [InlineData("align-content: center", 10, 40, 50)]
    [InlineData("display: flex", 10, 40, 50)]
    public void ParentAndFirstChild_Separated(string style, int parentY, int childY, int bottom)
    {
        var document = Parse($"""
            <div id=parent style='margin-top: 10px; {style}'>
              <div id=child style='margin-top: 30px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(parentY, GetRect(document, "parent").Y);
        Assert.Equal(childY, GetRect(document, "child").Y);
        Assert.Equal(bottom, GetRect(document, "parent").Bottom);
    }

    [Fact]
    public void ParentAndLastChild_MarginsCollapse()
    {
        var document = Parse("""
            <div id=parent><div style='height: 10px; margin-bottom: 20px'></div></div>
            <div id=next style='height: 5px; margin-top: 15px'></div>
            """);
        Assert.Equal(10, GetRect(document, "parent").Height);
        Assert.Equal(30, GetRect(document, "next").Y);
        Assert.Equal(20, GetFragment(document, "parent").CollapsedMargins.End.Sum);
    }

    [Theory]
    [InlineData("height: 50px", 50, 50)]
    [InlineData("min-height: 50px", 50, 50)]
    [InlineData("min-height: 20px", 20, 20)]
    [InlineData("min-height: 5px", 10, 30)]
    [InlineData("min-height: 5px; max-height: 40px", 10, 30)]
    [InlineData("max-height: 5px", 5, 25)]
    [InlineData("min-height: 0", 10, 30)]
    [InlineData("padding-bottom: 1px", 31, 31)]
    [InlineData("border-bottom: 2px solid", 32, 32)]
    [InlineData("display: flow-root", 30, 30)]
    public void ParentAndLastChild_Separated(string style, int parentHeight, int nextY)
    {
        // "The bottom margin of an in-flow block box with a 'height' of 'auto' collapses with its last in-flow block-level child's
        // bottom margin, if the box has no bottom padding, and the box has no bottom border". As in the browsers, the margin of
        // the child is discarded when the minimum height makes the box taller than its contents
        var document = Parse($"""
            <div id=parent style='{style}'><div style='height: 10px; margin-bottom: 20px'></div></div>
            <div id=next style='height: 5px'></div>
            """);
        Assert.Equal(parentHeight, GetRect(document, "parent").Height);
        Assert.Equal(nextY, GetRect(document, "next").Y);
    }

    [Fact]
    public void EmptyBlock_MarginsCollapseThrough()
    {
        var document = Parse("""
            <div id=a style='height: 10px; margin-bottom: 10px'></div>
            <div id=empty style='margin-top: 20px; margin-bottom: 30px'></div>
            <div id=b style='height: 10px; margin-top: 5px'></div>
            """);

        // The top border edge of the empty box is where it would be with a non-zero bottom border
        Assert.Equal(new PhysicalRect(0, 30, 800, 0), GetRect(document, "empty"));
        Assert.Equal(40, GetRect(document, "b").Y);
        Assert.True(GetFragment(document, "empty").CollapsedMargins.CollapsedThrough);
    }

    [Theory]
    [InlineData("height: 0", true)]
    [InlineData("height: 1px; max-height: 0", true)]
    [InlineData("min-height: 1px", false)]
    [InlineData("height: 1px", false)]
    [InlineData("padding-top: 1px", false)]
    [InlineData("border-bottom: 1px solid", false)]
    [InlineData("display: flow-root", false)]
    [InlineData("overflow: hidden", false)]
    public void EmptyBlock_CollapsesThroughWhenItsUsedHeightIsZero(string style, bool collapsesThrough)
    {
        // https://github.com/w3c/csswg-drafts/issues/8919: the used height is checked
        var document = Parse($"""
            <div style='height: 10px; margin-bottom: 10px'></div>
            <div id=empty style='margin: 20px 0 30px; {style}'></div>
            <div id=b style='height: 10px'></div>
            """);
        var empty = GetRect(document, "empty");
        var b = GetRect(document, "b").Y;
        if (collapsesThrough)
        {
            Assert.Equal(30, empty.Y);
            Assert.Equal(40, b);
        }
        else
        {
            Assert.Equal(30, empty.Y);
            Assert.Equal(empty.Bottom + 30, b);
        }
    }

    [Fact]
    public void EmptyBlocks_Nested()
    {
        var document = Parse("""
            <div style='height: 10px'></div>
            <div id=parent style='margin-top: 5px'>
              <div id=e1 style='margin: 20px 0'></div>
              <div id=e2 style='margin: 10px 0 -5px'></div>
            </div>
            <div id=next style='height: 5px; margin-top: 15px'></div>
            """);

        // All the margins collapse together: max(5, 20, 10, 15) + min(-5) = 15. The top border edge of the parent is where it
        // would be with a bottom border: after the margins of its children, which collapse with its top margin
        Assert.Equal(new PhysicalRect(0, 25, 800, 0), GetRect(document, "parent"));
        Assert.Equal(25, GetRect(document, "e1").Y);
        Assert.Equal(25, GetRect(document, "e2").Y);
        Assert.Equal(25, GetRect(document, "next").Y);
        Assert.True(GetFragment(document, "parent").CollapsedMargins.CollapsedThrough);
    }

    [Fact]
    public void EmptyFirstChild_CollapsesWithParentAndNextSibling()
    {
        var document = Parse("""
            <div id=parent style='margin-top: 5px'>
              <div id=empty style='margin: 10px 0 25px'></div>
              <div id=child style='height: 10px; margin-top: 15px'></div>
            </div>
            """);

        // The margins of the empty child collapse with the parent's margin and with the next sibling's margin
        Assert.Equal(25, GetRect(document, "parent").Y);
        Assert.Equal(25, GetRect(document, "empty").Y);
        Assert.Equal(25, GetRect(document, "child").Y);
        Assert.Equal(10, GetRect(document, "parent").Height);
    }

    [Fact]
    public void EmptyChildren_AfterBorder_CollapseWithTheNextSibling()
    {
        var document = Parse("""
            <div id=parent style='border-top: 1px solid'><div id=empty style='margin: 20px 0'></div></div>
            <div id=next style='height: 5px; margin-top: 10px'></div>
            """);

        // The margins of the empty child collapse with the block-end margin of the parent and the next sibling's
        Assert.Equal(1, GetRect(document, "parent").Height);
        Assert.Equal(21, GetRect(document, "empty").Y);
        Assert.Equal(21, GetRect(document, "next").Y);
    }

    [Fact]
    public void SkippedContents_MarginsCollapseWithSiblings()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 10px'></div>
            <details id=details style='margin: 20px 0'><summary style='display: block; height: 10px'></summary><div style='height: 50px; margin: 30px 0'></div></details>
            <div id=hidden style='content-visibility: hidden; margin: 5px 0'><div style='height: 50px'></div></div>
            <div id=next style='height: 5px'></div>
            """);
        Assert.Equal(new PhysicalRect(0, 30, 800, 10), GetRect(document, "details"));
        Assert.Equal(new PhysicalRect(0, 60, 800, 0), GetRect(document, "hidden"));
        Assert.Equal(65, GetRect(document, "next").Y);
    }

    [Fact]
    public void LineBoxes_SeparateTheMargins()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 5px'></div>
            <div id=text style='margin-top: 20px; font: 10px/10px Ahem'>X</div>
            <div id=parent style='font: 10px/10px Ahem'>X<div id=child style='margin-top: 20px; height: 5px'></div></div>
            <div id=whitespace style='margin: 20px 0'> <span> </span> </div>
            <div id=after style='height: 5px; margin-top: 10px'></div>
            """);
        Assert.Equal(new PhysicalRect(0, 30, 800, 10), GetRect(document, "text"));

        // The anonymous block of the text has a line box: the child is placed after it
        Assert.Equal(40, GetRect(document, "parent").Y);
        Assert.Equal(70, GetRect(document, "child").Y);
        Assert.Equal(35, GetRect(document, "parent").Height);

        // A block without line box (collapsible white space and an empty inline box) collapses through
        Assert.Equal(95, GetRect(document, "whitespace").Y);
        Assert.Equal(95, GetRect(document, "after").Y);
    }

    [Fact]
    public void BlockFormattingContextRoot_MarginsCollapseWithSiblingsAndParent()
    {
        var document = Parse("""
            <div id=parent style='margin-top: 10px'>
              <div id=root style='display: flow-root; margin-top: 30px; margin-bottom: 15px'><div style='height: 5px; margin: 7px 0'></div></div>
            </div>
            <div id=next style='height: 5px; margin-top: 10px'></div>
            """);

        // Its own margins collapse with the parent's and the siblings', the ones of its children stay inside
        Assert.Equal(30, GetRect(document, "parent").Y);
        Assert.Equal(new PhysicalRect(0, 30, 800, 19), GetRect(document, "root"));
        Assert.Equal(19, GetRect(document, "parent").Height);
        Assert.Equal(64, GetRect(document, "next").Y);
    }

    [Fact]
    public void BlockFormattingContextRoot_EmptyDoesNotCollapseThrough()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 10px'></div>
            <div id=root style='display: flow-root; margin: 20px 0'></div>
            <div id=b style='height: 10px; margin-top: 5px'></div>
            """);
        Assert.Equal(30, GetRect(document, "root").Y);
        Assert.Equal(50, GetRect(document, "b").Y);
    }

    [Fact]
    public void ReplacedAndTables_MarginsCollapseWithSiblings()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 10px'></div>
            <canvas id=canvas style='display: block; margin: 20px 0; width: 10px; height: 10px'></canvas>
            <div id=b style='height: 10px; margin-top: 5px'></div>
            """);
        Assert.Equal(30, GetRect(document, "canvas").Y);
        Assert.Equal(60, GetRect(document, "b").Y);
    }

    [Fact]
    public void Clear_WithoutFloats_HasNoEffect()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 10px'></div>
            <div id=cleared style='clear: both; margin-top: 20px; height: 10px'></div>
            """);
        Assert.Equal(30, GetRect(document, "cleared").Y);
        Assert.Null(GetFragment(document, "cleared").CollapsedMargins.Clearance);
    }

    [Fact]
    public void RootAndBody_Margins()
    {
        var document = Load("""
            <!DOCTYPE html><style>html { margin-top: 10px } body { margin-top: 20px }</style>
            <div id=child style='margin-top: 30px; height: 5px'></div>
            """);

        // The margins of the root element do not collapse; the body's margin collapses with the child's
        // The block-end margin of the body (8px) is inside the root
        Assert.Equal(new PhysicalRect(0, 10, 800, 43), GetRect(document, document.DocumentElement!));
        Assert.Equal(new PhysicalRect(8, 40, 784, 5), GetRect(document, document.Body!));
        Assert.Equal(40, GetRect(document, "child").Y);
    }

    [Fact]
    public void Body_DefaultMargins()
    {
        var document = Load("<!DOCTYPE html><div id=child style='margin-top: 20px; height: 10px'></div><div id=last style='height: 10px; margin-bottom: 5px'></div>");
        Assert.Equal(new PhysicalRect(8, 20, 784, 20), GetRect(document, document.Body!));
        Assert.Equal(new PhysicalRect(8, 20, 784, 10), GetRect(document, "child"));

        // The bottom margin of the body collapses with the last child's: the root is a block formatting context root
        Assert.Equal(48, GetRect(document, document.DocumentElement!).Height);
    }

    [Fact]
    public void Body_OverflowPropagatedToTheViewport_CollapsesWithChildren()
    {
        var document = Parse("<style>body { overflow: hidden }</style><div id=child style='margin-top: 20px; height: 5px'></div>");
        Assert.Equal(20, GetRect(document, document.Body!).Y);

        // When the root is not overflow: visible, the body is a scroll container
        document = Parse("<style>html, body { overflow: hidden }</style><div id=child style='margin-top: 20px; height: 5px'></div>");
        Assert.Equal(0, GetRect(document, document.Body!).Y);
        Assert.Equal(20, GetRect(document, "child").Y);
    }

    [Fact]
    public void WritingModes_MarginsCollapseInTheBlockAxis()
    {
        var document = Parse("""
            <div style='writing-mode: vertical-lr; height: 100px'>
              <div id=a style='width: 10px; margin-right: 20px'></div>
              <div id=b style='width: 10px; margin-left: 30px'></div>
              <div id=c style='width: 10px; margin-left: 5px; writing-mode: horizontal-tb'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 10, 100), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(40, 0, 10, 100), GetRect(document, "b"));

        // An orthogonal box establishes a new formatting context: its margins still collapse with its siblings'
        Assert.Equal(55, GetRect(document, "c").X);
    }

    [Fact]
    public void Zoom_MarginsAreZoomed()
    {
        var document = Parse("""
            <div style='zoom: 2'>
              <div id=a style='height: 10px; margin-bottom: 10px'></div>
              <div id=b style='height: 10px; margin-top: 5px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 40, 800, 20), GetRect(document, "b"));
    }

    [Fact]
    public void PercentageMargins_ResolveAgainstTheInlineSize()
    {
        var document = Parse("""
            <div style='width: 200px; border-top: 1px solid'>
              <div id=child style='margin-top: 10%; padding-top: 5%; height: 5px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 21, 200, 15), GetRect(document, "child"));
    }

    [Fact]
    public void Sizing_BoxSizingMinMaxAndPercentages()
    {
        var document = Parse("""
            <div style='height: 200px'>
              <div id=borderbox style='box-sizing: border-box; height: 10px; padding: 10px'></div>
              <div id=percent style='height: 25%; min-height: 60px; max-height: 40px'></div>
              <div id=maxpercent style='height: 100px; max-height: 10%'></div>
              <div id=auto style='max-height: 5px'><div style='height: 30px'></div></div>
              <div id=blockautomargins style='margin: auto; height: 5px'></div>
            </div>
            """);
        Assert.Equal(20, GetRect(document, "borderbox").Height);

        // The minimum wins over the maximum
        Assert.Equal(60, GetRect(document, "percent").Height);
        Assert.Equal(20, GetRect(document, "maxpercent").Height);
        Assert.Equal(5, GetRect(document, "auto").Height);

        // The auto margins of the block axis are 0
        Assert.Equal(new PhysicalRect(0, 105, 800, 5), GetRect(document, "blockautomargins"));
    }

    [Fact]
    public void AbsolutelyPositioned_StaticPosition()
    {
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 20px'></div>
            <div id=abs style='position: absolute; width: 10px; height: 10px'></div>
            <div id=b style='height: 5px; margin-top: 10px'></div>
            """);
        Assert.Equal(30, GetRect(document, "abs").Y);
        Assert.Equal(30, GetRect(document, "b").Y);
    }

    [Fact]
    public void Quirks_PercentageHeightCalculation()
    {
        var document = Load("""
            <style>body { margin: 8px }</style>
            <div id=a style='height: 50%'></div>
            <div><div id=b style='height: 25%'></div></div>
            <div style='height: 100px'><div><div id=c style='height: 50%'></div></div></div>
            <div style='position: absolute'><div id=d style='height: 50%'></div></div>
            <div style='display: flow-root'><div id=e style='height: calc(50% + 1px)'></div></div>
            """);
        Assert.Equal(DocumentMode.Quirks, document.Mode);

        // The body fills the html element, which fills the viewport: 600 - 16
        Assert.Equal(292, GetRect(document, "a").Height);
        Assert.Equal(146, GetRect(document, "b").Height);
        Assert.Equal(50, GetRect(document, "c").Height);

        // An absolutely positioned containing block stops the search, and calc() does not use the quirk (it behaves as auto)
        Assert.Equal(0, GetRect(document, "d").Height);
        Assert.Equal(0, GetRect(document, "e").Height);
    }

    [Fact]
    public void Quirks_HtmlAndBodyFillTheViewport()
    {
        var document = Load("<style>html { margin: 10px; padding: 5px } body { margin: 20px; padding: 3px }</style><div id=d style='height: 10px'></div>");
        Assert.Equal(new PhysicalRect(10, 10, 780, 580), GetRect(document, document.DocumentElement!));

        // The content box of the html element (570) minus the margins of the body
        Assert.Equal(new PhysicalRect(35, 35, 730, 530), GetRect(document, document.Body!));

        // A taller content wins
        document = Load("<div id=d style='height: 1000px'></div>");
        Assert.Equal(1016, GetRect(document, document.DocumentElement!).Height);
        Assert.Equal(1000, GetRect(document, document.Body!).Height);

        // Not in no-quirks mode
        document = Load("<!DOCTYPE html><div id=d style='height: 10px'></div>");
        Assert.Equal(10, GetRect(document, document.Body!).Height);
    }

    [Fact]
    public void Quirks_FloatingOrPositionedBody_DoesNotFill()
    {
        var document = Load("<style>body { position: absolute }</style><div style='height: 10px'></div>");
        Assert.Equal(10, GetRect(document, document.Body!).Height);
    }

    [Fact]
    public void Quirks_MarginCollapsingQuirks()
    {
        var document = Load("""
            <body>
            <p id=first>text</p>
            <p id=second>text</p>
            <table><tr><td><p id=cell>text</p></td><td> <h1 id=blank></h1> </td></tr></table>
            </body>
            """);
        var window = document.DefaultView!;
        string MarginTop(string id) => window.GetComputedStyle(document.GetElementById(id)!).MarginTop;
        string MarginBottom(string id) => window.GetComputedStyle(document.GetElementById(id)!).MarginBottom;

        // https://html.spec.whatwg.org/multipage/rendering.html#margin-collapsing-quirks
        Assert.Equal("0px", MarginTop("first"));
        Assert.Equal("16px", MarginBottom("first"));
        Assert.Equal("16px", MarginTop("second"));
        Assert.Equal("0px", MarginTop("cell"));
        Assert.Equal("0px", MarginBottom("cell"));
        Assert.Equal("0px", MarginTop("blank"));
        Assert.Equal("0px", MarginBottom("blank"));
        Assert.Equal(8, GetRect(document, "first").Y);

        // The rule depends on the siblings
        document.Body!.InsertBefore(document.CreateTextNode("text"), document.GetElementById("first"));
        Assert.Equal("16px", MarginTop("first"));

        // Author rules win, and the quirk only applies in quirks mode
        document = Load("<style>p { margin-top: 5px }</style><p id=first>text</p>");
        Assert.Equal("5px", document.DefaultView!.GetComputedStyle(document.GetElementById("first")!).MarginTop);
        document = Load("<!DOCTYPE html><p id=first>text</p>");
        Assert.Equal("16px", document.DefaultView!.GetComputedStyle(document.GetElementById("first")!).MarginTop);
        Assert.Equal(16, GetRect(document, "first").Y);
    }

    [Fact]
    public void NegativeMargins_ContentsBlockSizeIsNotNegative()
    {
        // WPT css/CSS2/positioning/top-007.xht: the contents of the parent end before its content box, whose block size is 0
        var document = Parse("""
            <div id=a style='border-top: 3px solid'>
              <div style='border-top: 3px solid; margin-top: -99px'></div>
            </div>
            <div id=b style='height: 10px'></div>
            """);
        Assert.Equal(3, GetRect(document, "a").Height);
        Assert.Equal(3, GetRect(document, "b").Y);
    }

    [Fact]
    public void AspectRatio_ChildDoesNotCollapseThrough()
    {
        // WPT css/css-sizing/aspect-ratio/block-aspect-ratio-with-margin-collapsing-001.html
        var document = Parse("""
            <div style='width: 100px; height: 25px; margin-bottom: -50px'></div>
            <div id=b style='width: 100px'>
              <div id=c style='margin-top: 50px; margin-bottom: 100px; aspect-ratio: 2/1'></div>
            </div>
            <div id=d style='width: 100px; height: 25px; margin-top: -100px'></div>
            """);
        Assert.Equal(new PhysicalRect(0, 25, 100, 50), GetRect(document, "b"));
        Assert.Equal(new PhysicalRect(0, 25, 100, 50), GetRect(document, "c"));
        Assert.Equal(75, GetRect(document, "d").Y);
    }

    [Fact]
    public void StretchMinimumHeight_ChildDoesNotCollapseThrough()
    {
        // WPT css/css-sizing/stretch/block-height-009.html: the child does not collapse through, its stretch-fit size ignores its
        // block-end margin (adjoining to the margin of its parent)
        var document = Parse("""
            <div id=p style='height: 20px; margin-bottom: 16px'></div>
            <div style='height: 100px'>
              <div id=c style='width: 100px; min-height: stretch; margin-bottom: 50px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 36, 100, 100), GetRect(document, "c"));
    }

    [Fact]
    public void ExclusionSpace_Clearance()
    {
        // The clearance is computed from the floats of the exclusion space
        var state = new BlockFormattingContextState();
        Assert.True(state.Exclusions.IsEmpty);
        Assert.Null(state.ComputeClearance(ClearSides.Both, MarginStrut.FromMargin(10)));

        state.Exclusions.Add(FloatSide.LineLeft, new LogicalRect(new LogicalOffset(0, 0), new LogicalSize(100, 50)));
        state.Exclusions.Add(FloatSide.LineRight, new LogicalRect(new LogicalOffset(300, 0), new LogicalSize(100, 80)));
        Assert.Equal(50, state.Exclusions.GetClearanceOffset(ClearSides.LineLeft));
        Assert.Equal(80, state.Exclusions.GetClearanceOffset(ClearSides.Both));
        Assert.Null(state.ComputeClearance(ClearSides.None, default));

        // Hypothetical position 20 (max(10, 20)), not past the float: clearance = 50 - (10 + 20)
        state.AdjoinMargin(10);
        Assert.Equal(20, state.ComputeClearance(ClearSides.LineLeft, MarginStrut.FromMargin(20)));

        // Already past the floats
        state.CollapseMargins();
        state.AdvanceBlockPosition(90);
        Assert.Null(state.ComputeClearance(ClearSides.Both, default));

        // The logical values are resolved with the direction of the containing block
        Assert.Equal(ClearSides.LineRight, ExclusionSpace.GetClearSides(ClearKind.InlineStart, new WritingDirection(WritingMode.HorizontalTb, Direction.Rtl)));
        Assert.Equal(ClearSides.LineRight, ExclusionSpace.GetClearSides(ClearKind.InlineEnd, WritingDirection.HorizontalLtr));
    }

    [Fact]
    public void MarginStrut_Collapsing()
    {
        var strut = MarginStrut.FromMargin(10).Append(-5).Append(20).Append(-15);
        Assert.Equal(20, strut.Positive);
        Assert.Equal(-15, strut.Negative);
        Assert.Equal(5, strut.Sum);
        Assert.Equal(-7, MarginStrut.FromMargin(-3).Append(MarginStrut.FromMargin(-7)).Sum);
        Assert.True(default(MarginStrut).IsZero);
    }

    [Fact]
    public void Invalidation_MarginChange()
    {
        var document = Parse("<div id=a style='height: 10px'></div><div id=b style='height: 10px'></div>");
        Assert.Equal(10, GetRect(document, "b").Y);
        ((HtmlElement)document.GetElementById("a")!).Style.SetProperty("margin-bottom", "15px");
        Assert.Equal(25, GetRect(document, "b").Y);
    }

    [Fact]
    public void AnonymousBlock_PercentageHeightRefersToTheNonAnonymousAncestor()
    {
        // https://drafts.csswg.org/css2/#anonymous-block-level: "anonymous block boxes are ignored when resolving percentage
        // values that would refer to it"
        var document = Parse("""
            <div style='height: 200px'><div id=float style='float: left; height: 50%; width: 10px'></div>text<div></div></div>
            """);
        Assert.Equal(100, GetRect(document, "float").Height);
    }

    [Fact]
    public void Quirks_HtmlAndBodyFillTheViewport_VerticalWritingMode()
    {
        // The quirks apply to the size in the block flow direction: the width
        var document = Load("<style>html { writing-mode: vertical-lr; margin: 10px } body { margin: 20px }</style><div style='width: 10px'></div>");
        Assert.Equal(780, GetRect(document, document.DocumentElement!).Width);
        Assert.Equal(740, GetRect(document, document.Body!).Width);
    }

    [Fact]
    public void Quirks_PercentageHeightCalculation_StopsAtFlexItemsAndCells()
    {
        // The main size of an item of a column flex container whose main size is indefinite is not a height for the quirk (the
        // flex container stops the search), and a cell gives no basis to the descendants of its children
        var document = Load("""
            <style>body { margin: 0 }</style>
            <div style='height: 80px'><div style='display: flex; flex-direction: column'><div><div id=a style='height: 75%; min-height: 10px'></div><div style='height: 50px'></div></div></div></div>
            <div style='display: table-cell; height: 100px'><div><div id=b style='height: 100%'></div></div></div>
            """);
        Assert.Equal(10, GetRect(document, "a").Height);
        Assert.Equal(0, GetRect(document, "b").Height);
    }

    [Fact]
    public void Quirks_StretchResolvesAgainstTheBodyThatFillsTheViewport()
    {
        // The second child is an inline-block in an anonymous block box
        var document = Load("<style>body { margin: 0 }</style><div style='height: 0'></div><span id=a style='display: inline-block; vertical-align: top; width: 10px; height: stretch'></span>");
        Assert.Equal(600, GetRect(document, "a").Height);

        // Not in an element that does not fill the viewport
        document = Load("<style>body { margin: 0 }</style><div><div id=a style='height: stretch'></div></div>");
        Assert.Equal(0, GetRect(document, "a").Height);
    }

    // https://drafts.csswg.org/css-box-4/#margin-trim

    [Theory]
    [InlineData("none", 30, 80)]
    [InlineData("block-start", 0, 50)]
    [InlineData("block-end", 30, 60)]
    [InlineData("block", 0, 30)]
    [InlineData("block-start block-end", 0, 30)]
    public void MarginTrim_TrimsTheMarginsOfTheFirstAndLastChildren(string value, int childY, int height)
    {
        // The container establishes a block formatting context: the margins of its children do not collapse with its own margins
        var document = Parse($"""
            <div id=container style='margin-trim: {value}; display: flow-root'>
              <div id=first style='height: 10px; margin: 30px 0 5px'></div>
              <div id=last style='height: 10px; margin: 10px 0 20px'></div>
            </div>
            """);
        Assert.Equal(childY, GetRect(document, "first").Y);
        Assert.Equal(childY + 20, GetRect(document, "last").Y);
        Assert.Equal(height, GetRect(document, "container").Height);
    }

    [Fact]
    public void MarginTrim_KeepsTheMarginsOfTheContainer()
    {
        // The margins of the children would collapse with the ones of the container: they are trimmed, not the ones of the container
        var document = Parse("""
            <div id=before style='height: 10px'></div>
            <div id=container style='margin-trim: block; margin: 10px 0'>
              <div id=child style='height: 10px; margin: 40px 0'></div>
            </div>
            <div id=after style='height: 10px'></div>
            """);
        Assert.Equal(20, GetRect(document, "container").Y);
        Assert.Equal(20, GetRect(document, "child").Y);
        Assert.Equal(10, GetRect(document, "container").Height);
        Assert.Equal(40, GetRect(document, "after").Y);
    }

    [Fact]
    public void MarginTrim_BlockStart_TrimsTheMarginsThatCollapseWithTheMarginOfTheFirstChild()
    {
        // The margins of the boxes that collapse through and the margins of the first children of the first child are adjacent
        // to the block-start edge; the margins after the first content are not
        var document = Parse("""
            <div id=container style='margin-trim: block-start; display: flow-root'>
              <div style='margin: 100px 0 200px'></div>
              <div id=wrapper style='margin-top: 20px'>
                <div style='margin: 30px 0'></div>
                <div id=nested style='height: 10px; margin: 40px 0 15px'></div>
                <div id=second style='height: 10px; margin-top: 25px'></div>
              </div>
            </div>
            """);
        Assert.Equal(0, GetRect(document, "wrapper").Y);
        Assert.Equal(0, GetRect(document, "nested").Y);
        Assert.Equal(35, GetRect(document, "second").Y);
        Assert.Equal(45, GetRect(document, "container").Height);
    }

    [Fact]
    public void MarginTrim_BlockStart_ABorderProtectsTheNestedMargins()
    {
        var document = Parse("""
            <div style='margin-trim: block-start; display: flow-root'>
              <div id=wrapper style='margin-top: 20px; border-top: 1px solid'>
                <div id=nested style='height: 10px; margin-top: 40px'></div>
              </div>
            </div>
            """);
        Assert.Equal(0, GetRect(document, "wrapper").Y);
        Assert.Equal(41, GetRect(document, "nested").Y);
    }

    [Fact]
    public void MarginTrim_BlockEnd_TrimsTheMarginsThatCollapseWithTheMarginOfTheLastChild()
    {
        // The block-end margins of the last children of the last child, and both margins of the boxes that collapse through
        // after the last content: these boxes are at the block-end edge of the content
        var document = Parse("""
            <div id=container style='margin-trim: block-end; display: flow-root'>
              <div id=first style='height: 10px; margin-bottom: 5px'></div>
              <div id=wrapper style='margin-bottom: 20px'>
                <div id=nested style='height: 10px; margin: 15px 0 40px'></div>
                <div id=empty1 style='margin: 30px 0'></div>
              </div>
              <div id=empty2 style='margin: 100px 0 200px'><div id=empty3 style='margin: 50px 0'></div></div>
            </div>
            <div id=after style='height: 10px'></div>
            """);
        Assert.Equal(25, GetRect(document, "nested").Y);
        Assert.Equal(35, GetRect(document, "empty1").Y);
        Assert.Equal(35, GetRect(document, "empty2").Y);
        Assert.Equal(35, GetRect(document, "empty3").Y);
        Assert.Equal(35, GetRect(document, "container").Height);
        Assert.Equal(35, GetRect(document, "after").Y);
    }

    [Fact]
    public void MarginTrim_BlockEnd_ABorderOrABlockSizeProtectsTheNestedMargins()
    {
        var document = Parse("""
            <div id=a style='margin-trim: block-end; display: flow-root'>
              <div style='margin-bottom: 20px; border-bottom: 1px solid'>
                <div style='height: 10px; margin-bottom: 40px'></div>
              </div>
            </div>
            <div id=b style='margin-trim: block-end; display: flow-root'>
              <div style='margin-bottom: 20px; height: 30px'>
                <div style='height: 10px; margin-bottom: 40px'></div>
              </div>
            </div>
            """);
        Assert.Equal(51, GetRect(document, "a").Height);
        Assert.Equal(30, GetRect(document, "b").Height);
    }

    [Fact]
    public void MarginTrim_TrimsAllTheMarginsOfTheChildrenThatCollapseThrough()
    {
        // All the children collapse through: their margins are adjacent to both edges, and the margins of the container are kept
        foreach (var value in (ReadOnlySpan<string>)["block-start", "block-end"])
        {
            var document = Parse($"""
                <div id=before style='height: 10px'></div>
                <div style='margin-trim: {value}; margin-top: 7px'>
                  <div style='margin: 100px 0 200px'></div>
                  <div style='margin: 50px 0'><div style='margin: 300px 0'></div></div>
                </div>
                <div id=after style='height: 10px'></div>
                """);
            Assert.Equal(17, GetRect(document, "after").Y);
        }
    }

    [Fact]
    public void MarginTrim_DoesNotTrimTheMarginsOfTheFloatsAndOfTheInlineLevelBoxes()
    {
        var document = Parse("""
            <div id=container style='margin-trim: block; display: flow-root; width: 200px'>
              <div id=float style='float: left; width: 10px; height: 10px; margin: 5px 0'></div>
              <span id=inline style='display: inline-block; width: 10px; height: 10px; margin: 7px 0'></span>
            </div>
            """);
        Assert.Equal(5, GetRect(document, "float").Y);
        Assert.Equal(7, GetRect(document, "inline").Y);
    }

    [Fact]
    public void MarginTrim_TrimsTheMarginsOfTheReplacedBoxesAndOfTheTables()
    {
        var document = Parse("""
            <div id=a style='margin-trim: block; display: flow-root'>
              <svg width=10 height=10 style='display: block; margin: 20px 0'></svg>
            </div>
            <div id=b style='margin-trim: block; display: flow-root'>
              <table id=table style='margin: 20px 0; border-spacing: 0'><tr><td style='padding: 0; height: 10px'></td></tr></table>
            </div>
            """);
        Assert.Equal(10, GetRect(document, "a").Height);
        Assert.Equal(10, GetRect(document, "b").Height);
        Assert.Equal(10, GetRect(document, "table").Y);
    }

    [Fact]
    public void MarginTrim_DoesNotApplyToTheFlexAndGridContainers()
    {
        // The trimming of the margins of the flex and grid items was removed from the specification (https://github.com/w3c/csswg-drafts/issues/13731)
        var document = Parse("""
            <div id=flex style='margin-trim: block; display: flex; flex-direction: column'><div style='height: 10px; margin: 20px 0'></div></div>
            <div id=grid style='margin-trim: block; display: grid'><div style='height: 10px; margin: 20px 0'></div></div>
            """);
        Assert.Equal(50, GetRect(document, "flex").Height);
        Assert.Equal(50, GetRect(document, "grid").Height);
    }

    [Fact]
    public void MarginTrim_TheResolvedValuesOfTheTrimmedMarginsAreNotChanged()
    {
        var document = Parse("""
            <div style='margin-trim: block; display: flow-root; width: 200px'>
              <div id=child style='height: 10px; margin: 10% 0 30px'></div>
            </div>
            """);
        var fragment = GetFragment(document, "child");
        Assert.Equal(0, fragment.Margin.Top);
        Assert.Equal(0, fragment.Margin.Bottom);
        Assert.Equal(20, fragment.UntrimmedMargin!.Value.Top);
        var style = document.DefaultView!.GetComputedStyle(document.GetElementById("child")!);
        Assert.Equal("20px", style.GetPropertyValue("margin-top"));
        Assert.Equal("30px", style.GetPropertyValue("margin-bottom"));
    }

    [Fact]
    public void MarginTrim_VerticalWritingMode()
    {
        var document = Parse("""
            <div id=container style='margin-trim: block; writing-mode: vertical-rl; display: flow-root; width: max-content; height: 50px'>
              <div id=first style='width: 10px; margin: 0 30px 0 5px'></div>
              <div id=last style='width: 10px; margin: 0 10px 0 20px'></div>
            </div>
            """);
        Assert.Equal(30, GetRect(document, "container").Width);
        Assert.Equal(20, GetRect(document, "first").X);
        Assert.Equal(0, GetRect(document, "last").X);
    }

    [Fact]
    public void MarginTrim_Invalidation()
    {
        var document = Parse("""
            <div id=container style='display: flow-root'><div style='height: 10px; margin: 20px 0'></div></div>
            """);
        Assert.Equal(50, GetRect(document, "container").Height);
        ((HtmlElement)document.GetElementById("container")!).Style.SetProperty("margin-trim", "block");
        Assert.Equal(10, GetRect(document, "container").Height);
        ((HtmlElement)document.GetElementById("container")!).Style.SetProperty("margin-trim", "block-start");
        Assert.Equal(30, GetRect(document, "container").Height);
        Assert.Equal("block-start", document.DefaultView!.GetComputedStyle(document.GetElementById("container")!).GetPropertyValue("margin-trim"));
    }

    [Theory]
    [InlineData("block-start block-end", "block")]
    [InlineData("block-end block-start", "block")]
    [InlineData("block-end", "block-end")]
    [InlineData("inline", "")]
    [InlineData("block inline", "")]
    public void MarginTrim_Serialization(string value, string expected)
    {
        var document = Parse("<div id=a></div>");
        var style = ((HtmlElement)document.GetElementById("a")!).Style;
        style.SetProperty("margin-trim", value);
        Assert.Equal(expected, style.GetPropertyValue("margin-trim"));
    }

    [Theory]
    [InlineData("height: 60px", 20, 60, 100)]
    [InlineData("height: 60px; block-step-align: start", 0, 60, 100)]
    [InlineData("height: 60px; block-step-align: end", 40, 60, 100)]
    [InlineData("height: 60px; align-self: end", 40, 60, 100)]
    [InlineData("height: 60px; block-step-round: down", -5, 60, 50)]
    [InlineData("height: 70px; block-step-round: nearest", -10, 70, 50)]
    [InlineData("height: 80px; block-step-round: nearest", 10, 80, 100)]
    [InlineData("height: 50px", 0, 50, 50)]
    [InlineData("height: 0", 25, 0, 50)]
    [InlineData("height: 30px; margin-top: 15px", 17.5, 30, 50)]
    [InlineData("height: 60px; block-step-insert: padding-box", 0, 100, 100)]
    [InlineData("height: 60px; block-step-insert: content-box", 0, 100, 100)]
    [InlineData("height: 60px; block-step-insert: padding-box; padding-top: 10px; box-sizing: border-box", 0, 100, 100)]
    public void BlockStep_RoundsTheOuterBlockSize(string style, double expectedY, double expectedHeight, double expectedContainerHeight)
    {
        // https://drafts.csswg.org/css-rhythm/#block-step-size: the outer block size is rounded to a multiple of the step unit,
        // the space is inserted in the margins (centered by default), the paddings or the content box
        var document = Parse($"<div id=c style='display: flow-root'><div id=t style='block-step-size: 50px; {style}'></div></div>");
        var rect = GetRect(document, "t");
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(expectedHeight, rect.Height);
        Assert.Equal(expectedContainerHeight, GetRect(document, "c").Height);
    }

    [Theory]
    [InlineData("block-step-insert: padding-box", 20)]
    [InlineData("block-step-insert: content-box", 20)]
    [InlineData("block-step-insert: content-box; block-step-align: start", 0)]
    [InlineData("block-step-insert: content-box; block-step-align: end", 40)]
    public void BlockStep_InsertedInsideTheBorder_AlignsTheContents(string style, double expectedChildY)
    {
        var document = Parse($"<div id=t style='block-step-size: 50px; {style}'><div id=child style='height: 60px'></div></div>");
        Assert.Equal(100, GetRect(document, "t").Height);
        Assert.Equal(expectedChildY, GetRect(document, "child").Y);
    }

    [Fact]
    public void BlockStep_MarginsCollapseWithTheSiblings()
    {
        // Only the own margins of the box are rounded; the margins with the inserted space collapse with the adjoining margins
        var document = Parse("""
            <div style='height: 10px; margin-bottom: 30px'></div>
            <div id=t style='block-step-size: 50px; height: 30px'></div>
            <div id=next style='height: 10px; margin-top: 5px'></div>
            """);
        Assert.Equal(40, GetRect(document, "t").Y);
        Assert.Equal(80, GetRect(document, "next").Y);
    }

    [Fact]
    public void BlockStep_EstablishesAnIndependentFormattingContext()
    {
        var document = Parse("""
            <div style='float: left; width: 50px; height: 100px'></div>
            <div id=t style='block-step-size: 1px; height: 100px'></div>
            """);
        Assert.Equal(50, GetRect(document, "t").X);
        Assert.Equal(750, GetRect(document, "t").Width);
    }

    [Fact]
    public void BlockStep_DoesNotApplyToInlineLevelAndFlexItems()
    {
        var document = Parse("""
            <div id=a style='display: inline-block; block-step-size: 50px; height: 60px; vertical-align: top'></div>
            <div style='display: flex'><div id=b style='block-step-size: 50px; height: 60px'></div></div>
            """);
        Assert.Equal(0, GetRect(document, "a").Y);
        Assert.Equal(60, GetRect(document, "b").Y);
        Assert.Equal(60, GetRect(document, "b").Height);
    }

    [Fact]
    public void BlockStep_ResolvedMarginsAreTheUsedValues()
    {
        var document = Parse("<div style='display: flow-root'><div id=t style='block-step-size: 50px; height: 60px; margin-top: 4px'></div></div>");
        var style = document.DefaultView!.GetComputedStyle(document.GetElementById("t")!);
        Assert.Equal("22px", style.MarginTop);
        Assert.Equal("18px", style.MarginBottom);
    }
}
