using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The positioned layout (relative, absolute, fixed and sticky positioning, the top layer), measured with Ahem.</summary>
public sealed class PositionedLayoutTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    private static BoxFragment GetFragment(Document document, string id) => document.GetLayoutEngine().GetFragments(document.GetElementById(id)!)[0];

    private static string GetComputedValue(Document document, string id, string property) => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).GetPropertyValue(property);

    [Fact]
    public void Relative_OffsetsTheBoxWithoutAffectingTheFlow()
    {
        var document = Parse("""
            <div id=a style='position: relative; top: 10px; left: 20px; height: 10px'></div>
            <div id=b style='height: 10px'></div>
            """);
        Assert.Equal(new PhysicalRect(20, 10, 800, 10), GetRect(document, "a"));
        Assert.Equal(10, GetRect(document, "b").Y);
        Assert.Equal(new PhysicalOffset(20, 10), GetFragment(document, "a").RelativeOffset);
    }

    [Theory]
    [InlineData("left: 10px; right: 30px", "ltr", 10)]
    [InlineData("left: 10px; right: 30px", "rtl", -30)]
    [InlineData("right: 30px", "ltr", -30)]
    [InlineData("left: auto; right: auto", "ltr", 0)]
    [InlineData("left: 10%", "ltr", 80)]
    public void Relative_HorizontalOffset(string insets, string direction, double expected)
    {
        var document = Parse($"<div dir={direction}><div id=a style='position: relative; {insets}; width: 100px; height: 10px'></div></div>");
        var expectedX = (direction == "rtl" ? 700 : 0) + expected;
        Assert.Equal(expectedX, GetRect(document, "a").X);
    }

    [Fact]
    public void Relative_PercentageOfIndefiniteHeightIsAuto()
    {
        var document = Parse("""
            <div id=auto><div id=a style='position: relative; top: 50%; height: 10px'></div></div>
            <div style='height: 100px'><div id=b style='position: relative; top: 50%; height: 10px'></div></div>
            """);
        Assert.Equal(0, GetRect(document, "a").Y);
        Assert.Equal(60, GetRect(document, "b").Y);
    }

    [Fact]
    public void Relative_InlineBoxMovesItsContent()
    {
        var document = Parse("<div>XX<span id=s style='position: relative; left: 5px; top: 3px'>YY</span></div>");
        var rect = GetRect(document, "s");
        Assert.Equal(25, rect.X);
        Assert.Equal(3, rect.Y);
    }

    [Fact]
    public void Relative_SplitInlineMovesTheBlocksInside()
    {
        // https://drafts.csswg.org/css2/#anonymous-block-level: the relative offset of the inline box affects the block inside it
        var document = Parse("<div><span style='position: relative; top: 7px; left: 3px'>X<div id=d style='height: 10px'></div>Y</span></div>");
        Assert.Equal(new PhysicalRect(3, 17, 800, 10), GetRect(document, "d"));
    }

    [Fact]
    public void Absolute_BothInsetsStretch()
    {
        var document = Parse("""
            <div style='position: relative; width: 200px; height: 100px'>
              <div id=a style='position: absolute; left: 10px; right: 20px; top: 5px; bottom: 15px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(10, 5, 170, 80), GetRect(document, "a"));
    }

    [Fact]
    public void Absolute_ContainingBlockIsThePaddingBox()
    {
        var document = Parse("""
            <div id=cb style='position: relative; margin: 10px; border: 5px solid; padding: 20px; width: 100px; height: 100px'>
              <div id=a style='position: absolute; top: 0; left: 0; width: 100%; height: 50%'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(15, 15, 140, 70), GetRect(document, "a"));

        // The fragment of the box is a child of the fragment of its containing block
        Assert.Same(GetFragment(document, "cb"), GetFragment(document, "a").Parent);
    }

    [Fact]
    public void Absolute_StaticPositionInBlockFlow()
    {
        var document = Parse("""
            <div style='position: relative; padding: 5px'>
              <div style='height: 30px'></div>
              <div id=a style='position: absolute; width: 10px; height: 10px; margin-left: 4px'></div>
              <div id=b style='height: 10px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(9, 35, 10, 10), GetRect(document, "a"));
        Assert.Equal(35, GetRect(document, "b").Y);
    }

    [Fact]
    public void Absolute_StaticPositionInRightToLeftContainer()
    {
        var document = Parse("<div dir=rtl style='position: relative; width: 200px'><div id=a style='position: absolute; width: 50px; height: 10px'></div></div>");
        Assert.Equal(150, GetRect(document, "a").X);
    }

    [Fact]
    public void Absolute_StaticPositionInInlineContent()
    {
        // An inline-level box is at its position on the line; a block-level box is at the start of the next line
        var document = Parse("""
            <div style='position: relative'>XX<span id=a style='position: absolute'>Y</span>Z</div>
            <div style='position: relative'>XX<div id=b style='position: absolute'>Y</div>Z</div>
            """);
        Assert.Equal(new PhysicalRect(20, 0, 10, 10), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(0, 20, 10, 10), GetRect(document, "b"));
    }

    [Theory]
    [InlineData("left: 0; right: 0; width: 100px; margin: 0 auto", 100)]
    [InlineData("left: 0; right: 0; width: 100px; margin-left: auto", 200)]
    [InlineData("left: 10px; right: 10px; width: 100px", 10)]
    [InlineData("right: 10px; width: 50px", 240)]
    [InlineData("left: 0; right: 0; width: 400px; margin: 0 auto", 0)]
    public void Absolute_HorizontalEquation(string style, double expectedX)
    {
        var document = Parse($"<div style='position: relative; width: 300px; height: 100px'><div id=a style='position: absolute; height: 10px; {style}'></div></div>");
        Assert.Equal(expectedX, GetRect(document, "a").X);
    }

    [Theory]
    [InlineData("bottom: 0; height: 20px", 80)]
    [InlineData("top: 0; bottom: 0; height: 20px; margin: auto 0", 40)]
    [InlineData("top: 0; bottom: 0; height: 120px; margin: auto 0", -10)]
    public void Absolute_VerticalEquation(string style, double expectedY)
    {
        var document = Parse($"<div style='position: relative; width: 300px; height: 100px'><div id=a style='position: absolute; width: 10px; {style}'></div></div>");
        Assert.Equal(expectedY, GetRect(document, "a").Y);
    }

    [Fact]
    public void Absolute_ShrinkToFitWithInlineContent()
    {
        var document = Parse("""
            <div style='position: relative; width: 300px'>
              <div id=a style='position: absolute; left: 50px'>XXX XX</div>
              <div id=b style='position: absolute; left: 280px'>XXX XX</div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(50, 0, 60, 10), GetRect(document, "a"));

        // The available space is the inset-modified containing block: the lines wrap
        Assert.Equal(new PhysicalRect(280, 0, 30, 20), GetRect(document, "b"));
    }

    [Fact]
    public void Absolute_AspectRatio()
    {
        var document = Parse("""
            <div style='position: relative; width: 300px; height: 100px'>
              <div id=a style='position: absolute; width: 100px; aspect-ratio: 2'></div>
              <div id=b style='position: absolute; inset: 0; aspect-ratio: 1'></div>
              <div id=c style='position: absolute; left: 0; top: 0; bottom: 0; aspect-ratio: 1'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 100, 50), GetRect(document, "a"));

        // Both axes have an auto size: the block axis is ratio-dependent, unless only the inline axis has an auto inset
        Assert.Equal(new PhysicalRect(0, 0, 300, 300), GetRect(document, "b"));
        Assert.Equal(new PhysicalRect(0, 0, 100, 100), GetRect(document, "c"));
    }

    [Fact]
    public void Absolute_InitialContainingBlock()
    {
        var document = Load("<!DOCTYPE html><html style='margin: 10px'><body style='margin: 8px'><div id=a style='position: absolute; top: 0; left: 0; right: 0; bottom: 0'></div>");
        Assert.Equal(new PhysicalRect(0, 0, 800, 600), GetRect(document, "a"));
        Assert.Same(document.GetLayoutEngine().EnsureLayout(), GetFragment(document, "a").Parent);
    }

    [Fact]
    public void Absolute_InlineContainingBlock()
    {
        var document = Parse("""
            <div>XX<span id=cb style='position: relative; padding: 0 2px'>YY<b id=a style='position: absolute; left: 0; top: 0; right: 0; bottom: 0'></b></span></div>
            """);
        var containingBlock = GetRect(document, "cb");
        Assert.Equal(new PhysicalRect(20, 0, 24, 10), containingBlock);
        Assert.Equal(new PhysicalRect(20, 0, 24, 10), GetRect(document, "a"));
    }

    [Fact]
    public void Absolute_InlineContainingBlockSplitAroundBlocks()
    {
        var document = Parse("""
            <div style='width: 200px'>
              <span id=cb style='position: relative'>XX<div style='height: 10px'></div>YYY<i id=a style='position: absolute; inset: 0'></i></span>
            </div>
            """);

        // From the start edges of the first fragment to the end edges of the last one
        Assert.Equal(new PhysicalRect(0, 0, 30, 30), GetRect(document, "a"));
    }

    [Fact]
    public void Fixed_ViewportUnlessTransformedAncestor()
    {
        var document = Parse("""
            <div style='height: 50px'></div>
            <div style='position: relative; margin-left: 30px; height: 100px'>
              <div id=a style='position: fixed; top: 0; left: 0; width: 10px; height: 10px'></div>
            </div>
            <div id=t style='transform: translateX(0); margin-left: 40px; height: 100px'>
              <div id=b style='position: fixed; top: 0; left: 0; width: 10px; height: 10px'></div>
              <div id=c style='position: absolute; top: 0; left: 0; width: 10px; height: 10px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 10, 10), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(40, 150, 10, 10), GetRect(document, "b"));
        Assert.Equal(new PhysicalRect(40, 150, 10, 10), GetRect(document, "c"));
    }

    [Fact]
    public void Fixed_InsideAbsolute()
    {
        var document = Parse("""
            <div style='position: absolute; top: 100px; left: 100px'>
              <div id=a style='position: fixed; width: 10px; height: 10px'></div>
              <div id=b style='position: fixed; bottom: 0; right: 0; width: 10px; height: 10px'></div>
            </div>
            """);

        // The static position is in the absolutely positioned box
        Assert.Equal(new PhysicalRect(100, 100, 10, 10), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(790, 590, 10, 10), GetRect(document, "b"));
    }

    [Fact]
    public void Absolute_ContributesToTheScrollableOverflowOfItsContainingBlock()
    {
        var document = Parse("""
            <div id=cb style='position: relative; width: 100px; height: 100px'>
              <div style='overflow: hidden; width: 50px; height: 50px'>
                <div style='position: absolute; left: 150px; top: 0; width: 10px; height: 10px'></div>
              </div>
            </div>
            """);
        Assert.Equal(160, GetFragment(document, "cb").ScrollableOverflow.Right);
    }

    [Fact]
    public void Sticky_ShiftedIntoTheViewport()
    {
        var document = Parse("""
            <div style='height: 50px'>
              <div id=a style='position: sticky; top: 10px; height: 45px'></div>
            </div>
            <div style='height: 100px'>
              <div id=b style='position: sticky; top: 10px; height: 20px'></div>
            </div>
            """);

        // Shifted by 10px, limited by the containing block (5px)
        Assert.Equal(5, GetRect(document, "a").Y);
        Assert.Equal(new PhysicalOffset(0, 5), GetFragment(document, "a").StickyOffset);
        Assert.Equal(50, GetRect(document, "b").Y);
        var constraints = GetFragment(document, "b").StickyConstraints!;
        Assert.Null(constraints.VerticalScrollContainer);

        // Scrolled by 60px: the box stays 10px under the top of the viewport
        Assert.Equal(new PhysicalOffset(0, 20), constraints.ComputeOffset(new PhysicalOffset(0, 60)));
    }

    [Fact]
    public void Sticky_InScrollContainer()
    {
        var document = Parse("""
            <div style='height: 30px'></div>
            <div id=s style='overflow: hidden; height: 100px; padding-top: 5px'>
              <div style='height: 200px'></div>
              <div id=a style='position: sticky; bottom: 10px; height: 10px'></div>
            </div>
            """);

        // The bottom of the box is 10px above the bottom of the content box of the scroll container (135 - 10)
        Assert.Equal(115, GetRect(document, "a").Y);
        Assert.Equal("10px", GetComputedValue(document, "a", "bottom"));
        Assert.Equal("auto", GetComputedValue(document, "a", "top"));
    }

    [Fact]
    public void Sticky_OverconstrainedStartEdgeWins()
    {
        // The sticky view rectangle (20px to 100px) is smaller than the box: the bottom edge moves the box up, then the top edge
        // moves it down again to 20px below the top of the scrollport
        var document = Parse("""
            <div id=s style='overflow: auto; height: 100px; position: relative'>
              <div style='height: 200px'></div>
              <div id=a style='position: sticky; top: 20px; bottom: 0; height: 200px'></div>
              <div style='height: 200px'></div>
            </div>
            """);
        Assert.Equal(20, ((HtmlElement)document.GetElementById("a")!).OffsetTop);
    }

    [Fact]
    public void ResolvedInsets()
    {
        var document = Parse("""
            <div style='position: relative; width: 200px; height: 100px'>
              <div id=a style='position: absolute; top: 10%; left: 20px; width: 50px; height: 10px'></div>
              <div id=r style='position: relative; bottom: 5px'></div>
            </div>
            """);
        Assert.Equal("10px", GetComputedValue(document, "a", "top"));
        Assert.Equal("80px", GetComputedValue(document, "a", "bottom"));
        Assert.Equal("130px", GetComputedValue(document, "a", "right"));
        Assert.Equal("-5px", GetComputedValue(document, "r", "top"));
        Assert.Equal("0px", GetComputedValue(document, "r", "left"));
    }

    [Fact]
    public void TopLayer_ModalDialogIsCenteredInTheViewport()
    {
        var document = Parse("<div style='position: relative; transform: scale(1)'><dialog id=d style='padding: 0; border: 0; width: 100px; height: 50px'>X</dialog></div>");
        var dialog = (HtmlDialogElement)document.GetElementById("d")!;
        dialog.ShowModal();
        Assert.Equal(new PhysicalRect(350, 275, 100, 50), GetRect(document, "d"));

        var root = document.GetLayoutEngine().EnsureLayout()!;
        var fragment = GetFragment(document, "d");
        Assert.Same(root, fragment.Parent);
        Assert.True(fragment.Box.IsInTopLayer);
        Assert.True(fragment.EstablishesStackingContext);

        // The ::backdrop is before the dialog, it fills the viewport
        var backdrop = document.GetLayoutEngine().GetFragments(dialog, "backdrop")[0];
        Assert.Equal(new PhysicalRect(0, 0, 800, 600), backdrop.GetRectInRoot());
        var children = root.Children;
        Assert.Equal(children.Count - 2, IndexOf(children, backdrop));

        dialog.Close();
        Assert.Empty(document.GetLayoutEngine().GetFragments(dialog));

        static int IndexOf(IReadOnlyList<Fragment> list, Fragment fragment)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], fragment))
                    return i;
            }

            return -1;
        }
    }

    [Fact]
    public void StackingContexts()
    {
        var document = Parse("""
            <div id=a style='position: relative; z-index: 2'></div>
            <div id=b style='position: relative'></div>
            <div id=c style='z-index: 3'></div>
            <div id=d style='opacity: 0.5'></div>
            <div style='display: flex'><div id=e style='z-index: 1'></div></div>
            """);
        Assert.Equal(2, GetFragment(document, "a").ZIndex);
        Assert.True(GetFragment(document, "a").EstablishesStackingContext);
        Assert.Null(GetFragment(document, "b").ZIndex);
        Assert.False(GetFragment(document, "b").EstablishesStackingContext);
        Assert.Null(GetFragment(document, "c").ZIndex);
        Assert.True(GetFragment(document, "d").EstablishesStackingContext);
        Assert.Equal(1, GetFragment(document, "e").ZIndex);
    }

    [Fact]
    public void StackingContexts_AnimatedPropertiesActAsWillChange()
    {
        // https://drafts.csswg.org/web-animations-1/#side-effects-section: also in the delay phase of the animation
        var document = Parse("""
            <style>@keyframes scale { from, to { scale: 1 } } @keyframes color { from, to { color: red } }</style>
            <div id=a style='animation: scale 100s 100s'></div>
            <div id=b style='animation: color 100s 100s'></div>
            <div id=c></div>
            """);
        Assert.True(GetFragment(document, "a").EstablishesStackingContext);
        Assert.False(GetFragment(document, "b").EstablishesStackingContext);
        Assert.False(GetFragment(document, "c").EstablishesStackingContext);
    }

    [Theory]
    [InlineData("div", "will-change: z-index; position: relative", true)]
    [InlineData("div", "will-change: z-index", false)]
    [InlineData("div", "will-change: position", true)]
    [InlineData("div", "will-change: offset-path", true)]
    [InlineData("div", "will-change: transform-style", true)]
    [InlineData("div", "will-change: -webkit-perspective", true)]
    [InlineData("span", "will-change: filter", true)]
    [InlineData("span", "will-change: transform", false)]
    public void WillChange_StackingContext(string element, string style, bool expected)
    {
        // https://drafts.csswg.org/css-will-change/#will-change: a property whose non-initial values create a stacking context
        // creates one (z-index only on the boxes it applies to, the transforms not on the inline boxes)
        var context = _contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><{element} id=a style='{style}'>text</{element}>");
        var fragment = document.GetLayoutEngine().GetFragments(document.GetElementById("a")!)[0];
        Assert.Equal(expected, fragment.EstablishesStackingContext);
    }

    [Fact]
    public void StaticPosition_BlockLevelBoxInInlineContent_DoesNotAvoidTheFloats()
    {
        // The static position is at the edge of the content box, below the line of the content before the box
        var document = Parse("""
            <div style='line-height: 20px'>x<div id=a style='position: absolute; width: 20px; height: 20px'></div><div style='float: left; width: 50px; height: 50px'></div></div>
            """);
        Assert.Equal(new PhysicalRect(0, 20, 20, 20), GetRect(document, "a"));
    }

    [Theory]
    [InlineData("border-left: 100px solid; margin-left: -100px", 100)]
    [InlineData("border-right: 100px solid", 0)]
    public void StaticPosition_BlockLevelBoxAfterTheStartEdgeOfAnInlineBox(string style, int expectedY)
    {
        // A line box with the start margin, border or padding of an inline box is not a zero-height line box: the box is below it
        var document = Parse($"""
            <div style='width: 100px'><span style='line-height: 100px; {style}'><div id=a style='position: absolute; width: 10px; height: 10px'></div>X</span></div>
            """);
        Assert.Equal(expectedY, GetRect(document, "a").Y);
    }

    [Theory]
    [InlineData("ltr", "left", "left", 50)]
    [InlineData("ltr", "center", "left", 75)]
    [InlineData("ltr", "right", "left", 100)]
    [InlineData("ltr", "center", "right", 25)]
    [InlineData("rtl", "right", "left", 50)]
    [InlineData("rtl", "center", "left", 25)]
    [InlineData("rtl", "left", "left", 0)]
    [InlineData("rtl", "left", "right", -50)]
    public void StaticPosition_InlineLevelBoxAmongBlocksIsOnAnEmptyLine(string direction, string textAlign, string floatSide, int expectedX)
    {
        // The box would be on a line box next to the float, aligned by text-align: its inline-start edge (the right edge in a
        // right-to-left block container) is where the content of that empty line box would be
        var document = Parse($"""
            <div style='position: relative; width: 100px; direction: {direction}; text-align: {textAlign}'>
              <div style='height: 50px'></div>
              <div style='float: {floatSide}; width: 50px; height: 50px'></div>
              <div id=a style='display: inline; position: absolute; width: 50px; height: 50px'></div>
            </div>
            """);
        Assert.Equal(new PhysicalRect(expectedX, 50, 50, 50), GetRect(document, "a"));
    }

    [Fact]
    public void StaticPosition_InlineLevelBoxUsesTheDirectionOfItsBidiLevel()
    {
        // The left-to-right text of a right-to-left block container: the box is in a left-to-right run, its left edge is at
        // its position in the run
        var document = Parse("<div style='position: relative; direction: rtl; width: 100px'>XXX<div id=a style='display: inline; position: absolute'>XX</div>XX</div>");
        Assert.Equal(new PhysicalRect(80, 0, 20, 10), GetRect(document, "a"));
    }

    [Fact]
    public void StaticPosition_StaysOnTheLineWithTheStartOfItsInlineBox()
    {
        // The line breaks after the absolutely positioned box: the inline box that contains it starts on the first line, and
        // forms a containing block from there
        var document = Parse("""
            <div style='width: 50px'>X <span style='position: relative'><div id=a style='position: absolute; left: 0; top: 0; width: 5px; height: 5px'></div>XXXX XX</span></div>
            """);
        Assert.Equal(new PhysicalRect(10, 0, 5, 5), GetRect(document, "a"));
    }

    [Fact]
    public void InlineContainingBlock_UsesTheFragmentsOfTheFirstAndLastLines()
    {
        // Right-to-left block container: the start edge is the right edge of the fragments of the first line (the bidirectional
        // reordering splits the inline box in two fragments there), the end edge the left edge of the fragment of the last line
        var document = Parse("""
            <div style='direction: rtl; width: 80px'>XX <span style='position: relative'>XXXX<br>XXXXXXXX<br>XXXXX<div id=a style='position: absolute; inset: 0'></div></span></div>
            """);
        Assert.Equal(new PhysicalRect(30, 0, 50, 30), GetRect(document, "a"));
    }

    [Fact]
    public void InlineContainingBlock_InMultiColumnContainerIsFragmentedWithTheColumns()
    {
        // The containing block is formed by the fragments of the inline box before the fragmentation (from the first line to
        // the last one, 60px in the block axis): the box is fragmented in the two columns
        var document = Parse("""
            <div style='columns: 2; column-gap: 0; column-fill: auto; width: 160px; height: 30px'>XX <span style='position: relative'>XXXX<br>XXXXXXXX<br>XXXXX<br>XXXXXXXX<br>XXXXX<br>XXXXX<div id=a style='position: absolute; inset: 0'></div></span></div>
            """);
        var fragments = document.GetLayoutEngine().GetFragments(document.GetElementById("a")!);
        Assert.Equal(2, fragments.Count);
        Assert.Equal(new PhysicalRect(30, 0, 20, 30), new PhysicalRect(fragments[0].GetOffsetInRoot(), fragments[0].Size));
        Assert.Equal(new PhysicalRect(110, 0, 20, 30), new PhysicalRect(fragments[1].GetOffsetInRoot(), fragments[1].Size));
    }

    [Fact]
    public void StaticPosition_FollowsTheAlignmentOfTheContentsOfATableCell()
    {
        var document = Parse("""
            <div style='display: table-cell; position: relative; vertical-align: middle; width: 100px; height: 100px'><div id=a style='position: absolute; width: 10px; height: 10px'></div></div>
            """);
        Assert.Equal(new PhysicalRect(0, 50, 10, 10), GetRect(document, "a"));
    }

    [Fact]
    public void Absolute_FormControlsStretchInTheInsetModifiedContainingBlock()
    {
        var document = Parse("""
            <div style='position: relative; width: 300px; height: 100px'>
              <input id=a style='position: absolute; inset: 10px; margin: 0; box-sizing: border-box'>
              <img id=b style='position: absolute; inset: 10px'>
            </div>
            """);
        Assert.Equal(new PhysicalRect(10, 10, 280, 80), GetRect(document, "a"));
        Assert.Equal(new PhysicalSize(0, 0), GetRect(document, "b").Size);
    }

    [Theory]
    [InlineData("<tbody style='height: 10px'><tr id=a style='position: relative; top: 100%'>", 10)]
    [InlineData("<tbody><tr id=a style='position: relative; top: 100%'>", 0)]
    [InlineData("<tbody><tr style='height: 10px'><td id=a style='position: relative; top: 100%; padding: 0'>", 10)]
    public void Relative_PercentageInsetsOfTablePartsUseTheSpecifiedBlockSize(string markup, int expectedY)
    {
        // The basis is the specified height of the parent part when it is a length, not its used height (100px)
        var document = Parse($"<table style='border-spacing: 0'>{markup}<td style='padding: 0'><div style='width: 100px; height: 100px'></div>");
        Assert.Equal(expectedY, GetRect(document, "a").Y);
    }

    [Fact]
    public void Sticky_AutoMarginIsNotAMarginOfTheBox()
    {
        // The box is aligned at the end of the flex container by its auto margin, and moves into the scrollport
        var document = Parse("""
            <div style='overflow: hidden; width: 100px; height: 100px'>
              <div style='display: flex; flex-direction: column; height: 500px'>
                <div id=a style='position: sticky; bottom: 0; margin-top: auto; height: 100px'></div>
              </div>
            </div>
            """);
        Assert.Equal(0, GetRect(document, "a").Y);
    }

    [Fact]
    public void Sticky_InlineBoxMovesInItsBlockContainerWithTheBoxesItContains()
    {
        // The containing block is the block container, not the anonymous block box around the inline content; the absolutely
        // positioned box whose containing block is the inline box moves with it
        var document = Parse("""
            <div style='overflow: hidden; height: 100px'>
              <div style='height: 200px'>
                <div style='height: 5px'></div>
                <span id=a style='position: sticky; top: 30px'>X<div id=b style='position: absolute; top: 0; left: 0; width: 5px; height: 5px'></div></span>
              </div>
            </div>
            """);
        Assert.Equal(30, GetRect(document, "a").Y);
        Assert.Equal(30, GetRect(document, "b").Y);
    }

    [Fact]
    public void Sticky_InFixedPositionedBoxDoesNotMoveWithTheViewportScroll()
    {
        var document = Parse("""
            <div style='position: fixed; top: 100px; height: 2000px'><div id=a style='position: sticky; top: 0; width: 10px; height: 10px'></div></div>
            <div style='height: 5000px'></div>
            """);
        _ = document.DefaultView!.ScrollTo(0, 2000);
        Assert.Equal(PhysicalOffset.Zero, GetFragment(document, "a").StickyOffset);
    }

    [Fact]
    public void PaintOrder_AbsolutelyPositionedChildOfFlexContainerIsOrderedAsOrderZero()
    {
        // The positioned flex item follows the absolutely positioned box in tree order: it is painted above it
        var document = Parse("""
            <div style='display: flex; position: relative; width: 100px; height: 100px'>
              <div id=abs style='position: absolute; inset: 0'></div>
              <div id=item style='position: relative; width: 100px; height: 100px'></div>
            </div>
            <div style='display: flex; position: relative; width: 100px; height: 100px'>
              <div id=item2 style='position: relative; order: 1; width: 100px; height: 100px'></div>
              <div id=abs2 style='position: absolute; inset: 0'></div>
            </div>
            """);
        Assert.Same(document.GetElementById("item"), document.ElementFromPoint(50, 50));
        Assert.Same(document.GetElementById("item2"), document.ElementFromPoint(50, 150));
    }

    [Theory]
    [InlineData("-2147483649", int.MinValue)]
    [InlineData("2147483648", int.MaxValue)]
    [InlineData("5", 5)]
    public void ZIndex_IsClampedToTheRangeOfTheIntegers(string value, int expected)
    {
        var document = Parse($"<div id=a style='position: absolute; z-index: {value}'></div>");
        Assert.Equal(expected, GetFragment(document, "a").Box.Style.Layout.Insets.ZIndex);
    }

    [Fact]
    public void BlockInInline_IsPaintedInTheStackingContextOfTheInlineBox()
    {
        // The block split out of the inline box is in its stacking context: it is above the box with the lower z-index
        var document = Parse("""
            <div id=red style='position: relative; z-index: 1; width: 100px; height: 100px'></div>
            <span style='position: relative; z-index: 2; top: -100px'> <div id=green style='width: 100px; height: 100px'></div> </span>
            """);
        Assert.Same(document.GetElementById("green"), document.ElementFromPoint(50, 50));
    }

    [Fact]
    public void AspectRatio_TheAxisWithAnAutomaticSizeIsRatioDependent()
    {
        // https://drafts.csswg.org/css-position-3/#abspos-auto-size: the automatic size of the ratio-dependent axis is not the stretch-fit size
        var document = Parse("""
            <div style='position: relative; width: 100px; height: 500px'><div id=a style='position: absolute; inset: 0; aspect-ratio: 1; width: 100px'></div></div>
            <div style='position: relative; width: 500px; height: 100px'><div id=b style='position: absolute; inset: 0; aspect-ratio: 1; height: 100%'></div></div>
            """);
        Assert.Equal(100, GetRect(document, "a").Height);
        Assert.Equal(100, GetRect(document, "b").Width);
    }

    [Fact]
    public void Table_FitContentSizeIsLimitedByTheContainingBlock()
    {
        // The inset-modified containing block is 200px wide (as in Blink and WebKit, the table is not wider than its containing block)
        var document = Parse("<div style='position: relative; width: 100px; height: 100px; margin-left: 100px'><table id=t style='position: absolute; left: -100px; border-spacing: 0'><td style='padding: 0'>X X X X X X X X X</td></table></div>");
        Assert.Equal(100, GetRect(document, "t").Width);
    }

    [Fact]
    public void StaticPosition_ItemsAlignmentOfTheParent()
    {
        // Only the justify-items of a block container parent is used: its align-items does not apply to its block-level
        // children, and the ones of an inline box apply to nothing
        var document = Parse("""
            <div style='position: relative; width: 100px; height: 100px'><div style='width: 80px; height: 80px; align-items: center; justify-items: center'><div id=a style='position: absolute; width: 20px; height: 20px'></div></div></div>
            <div style='position: relative; width: 100px; height: 100px'><span style='align-items: center; justify-items: center'>X<div id=b style='position: absolute; width: 20px; height: 20px'></div></span></div>
            """);
        Assert.Equal(new PhysicalRect(30, 0, 20, 20), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(0, 110, 20, 20), GetRect(document, "b"));
    }
}
