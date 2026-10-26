using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>
/// CSS Anchor Positioning (https://drafts.csswg.org/css-anchor-position-1/): the anchor functions, position-area, anchor-center,
/// the position options, the scroll compensation and the resolved values.
/// </summary>
public sealed class AnchorPositioningTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            Viewport = new ViewportOptions { Width = 800, Height = 600 },
        });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering steps of the next frames.</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

    [Fact]
    public void LastSuccessfulPositionOption_IsKeptWhenNoOptionFits()
    {
        var document = Parse("""
            <style>
              #container { position: relative; width: 400px; height: 400px; }
              #anchor { position: relative; top: 100px; left: 100px; width: 100px; height: 100px; anchor-name: --a; }
              #anchored { position-anchor: --a; position-try-fallbacks: flip-block; position: absolute; width: 100px; height: 200px; position-area: top center; }
            </style>
            <div id=container><div id=anchor></div><div id=anchored></div></div>
            """);
        var anchored = Get(document, "anchored");
        Assert.Equal(200, anchored.OffsetTop);
        RunFrames(2);
        Get(document, "anchor").Style.SetProperty("top", "150px");
        RunFrames(2);
        Assert.Equal(200, anchored.OffsetTop);
        Get(document, "anchor").Style.SetProperty("top", "200px");
        RunFrames(2);
        Assert.Equal(0, anchored.OffsetTop);
    }

    private static string GetComputedValue(Document document, string id, string property) => document.DefaultView!.GetComputedStyle(Get(document, id)).GetPropertyValue(property);

    private static (double X, double Y, double Width, double Height) GetRect(Document document, string id)
    {
        var rect = Get(document, id).GetBoundingClientRect();
        return (rect.X, rect.Y, rect.Width, rect.Height);
    }

    [Fact]
    public void AnchorSize_OfAnAnchorInANestedMultiColumnContainerIsTheOneOfItsFragmentsInTheOuterColumns()
    {
        // The inner columns are 84px tall in the second outer column, 100px in the third one: the anchor (150px tall from 66px)
        // has 18px + 84px in the second outer column and 48px in the third one, its anchor box is 84px + 48px tall in the flow
        // of the containing block
        var document = Parse("""
            <style>
              .cb { position: relative; border: solid transparent; border-width: 3px 2px 4px 1px; padding: 3px 2px 4px 1px; }
              #outer { columns: 4; column-fill: auto; column-gap: 10px; width: 630px; height: 100px; }
              #inner { columns: 2; column-fill: auto; column-gap: 10px; width: 130px; height: 200px; }
              #anchor { anchor-name: --a; margin-left: 10px; width: 20px; height: 150px; }
              #target { position: absolute; left: anchor(--a left); top: anchor(--a top); width: anchor-size(--a width); height: anchor-size(--a height); }
            </style>
            <div style='height: 10px'></div>
            <div id=outer>
              <div class=cb>
                <div style='height: 110px'></div>
                <div id=inner><div class=cb><div style='height: 60px'></div><div id=anchor></div></div></div>
                <div id=target><div id=corner style='position: absolute; bottom: 0; right: 0; width: 10px; height: 10px'></div></div>
              </div>
            </div>
            """);
        Assert.Equal((414d, 48d, 10d, 10d), GetRect(document, "corner"));
    }

    [Fact]
    public void AnchorFunctions_AlignTheInsetsWithTheEdgesOfTheAnchors()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 400px; }
              #a1 { position: absolute; left: 100px; top: 50px; width: 100px; height: 100px; anchor-name: --a1; }
              #a2 { position: absolute; left: 300px; top: 200px; width: 50px; height: 50px; anchor-name: --a2; }
              #target { position: absolute; left: anchor(--a1 right); top: anchor(--a1 bottom); right: anchor(--a2 left); bottom: calc(anchor(--a2 top) - 10px); }
            </style>
            <div id=cb><div id=a1></div><div id=a2></div><div id=target></div></div>
            """);

        Assert.Equal((200, 150, 100, 60), GetRect(document, "target"));

        // https://drafts.csswg.org/css-anchor-position-1/#interleaving: the insets resolve to lengths
        Assert.Equal("200px", GetComputedValue(document, "target", "left"));
        Assert.Equal("100px", GetComputedValue(document, "target", "right"));
        Assert.Equal("190px", GetComputedValue(document, "target", "bottom"));
    }

    [Fact]
    public void AnchorFunctions_LogicalSidesAndPercentages()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 400px; }
              #a { position: absolute; left: 100px; top: 100px; width: 200px; height: 100px; anchor-name: --a; }
              .t { position: absolute; position-anchor: --a; width: 10px; height: 10px; }
              #start { left: anchor(start); top: anchor(end); }
              #percent { left: anchor(25%); top: anchor(center); }
              #outside { right: anchor(outside); bottom: anchor(inside); }
              #rtl { direction: rtl; }
            </style>
            <div id=cb><div id=a></div><div class=t id=start></div><div class=t id=percent></div><div class=t id=outside></div></div>
            <div id=rtl style="position: relative; width: 400px; height: 400px"><div id=a2 style="position: absolute; left: 100px; width: 200px; height: 50px; anchor-name: --b"></div><div class=t id=rtlstart style="position-anchor: --b; right: anchor(start)"></div></div>
            """);

        Assert.Equal((100, 200, 10, 10), GetRect(document, "start"));
        Assert.Equal((150, 150, 10, 10), GetRect(document, "percent"));
        Assert.Equal((90, 190, 10, 10), GetRect(document, "outside"));

        // start is the right side in a rtl containing block
        Assert.Equal(300 - 10, GetRect(document, "rtlstart").X);
    }

    [Fact]
    public void AnchorFunctions_UnresolvableUseTheirFallbackOrAreInvalid()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 400px; }
              #fallback { position: absolute; left: anchor(--unknown right, 42px); top: anchor(--unknown top); width: anchor-size(--unknown width, 30px); height: 10px; }
              #static { position: relative; left: anchor(--unknown right, 7px); margin-left: anchor-size(width); }
            </style>
            <div id=cb><div id=fallback></div><div id=static></div></div>
            """);

        Assert.Equal((42, 0, 30, 10), GetRect(document, "fallback"));

        // The anchor functions of a box that is not absolutely positioned compute to their fallback, else to the initial value
        Assert.Equal("7px", GetComputedValue(document, "static", "left"));
        Assert.Equal("0px", GetComputedValue(document, "static", "margin-left"));
    }

    [Fact]
    public void AnchorSize_ResolvesTheSizesAndTheMargins()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 400px; }
              #a { width: 120px; height: 30px; anchor-name: --a; }
              #target { position: absolute; position-anchor: --a; width: anchor-size(width); height: calc(anchor-size(height) * 2); min-width: anchor-size(height); margin-left: anchor-size(height); }
            </style>
            <div id=cb><div id=a></div><div id=target></div></div>
            """);

        Assert.Equal((30, 30, 120, 60), GetRect(document, "target"));
        Assert.Equal("120px", GetComputedValue(document, "target", "width"));
        Assert.Equal("30px", GetComputedValue(document, "target", "min-width"));
        Assert.Equal("30px", GetComputedValue(document, "target", "margin-left"));
    }

    [Fact]
    public void AcceptableAnchor_IsLaidOutBeforeThePositionedBox()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 400px; }
              .a { anchor-name: --a; position: absolute; width: 10px; height: 10px; }
              #target { position: absolute; left: anchor(--a right, 1px); width: 10px; height: 10px; }
            </style>
            <div id=cb><div class=a style="left: 20px"></div><div id=target></div><div class=a style="left: 50px"></div></div>
            """);

        // The absolutely positioned anchor that follows the box in tree order is not an acceptable anchor element
        Assert.Equal(30, GetRect(document, "target").X);
    }

    [Fact]
    public void PositionArea_SelectsAnAreaOfTheGridAndAlignsTowardTheAnchor()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 400px; }
              #a { position: absolute; left: 100px; top: 150px; width: 150px; height: 75px; anchor-name: --a; }
              .t { position: absolute; position-anchor: --a; width: 20px; height: 20px; }
              #topleft { position-area: top left; }
              #center { position-area: center; }
              #bottom { position-area: bottom; }
              #stretch { position-area: span-right center; place-self: stretch; width: auto; height: auto; }
            </style>
            <div id=cb><div id=a></div><div class=t id=topleft></div><div class=t id=center></div><div class=t id=bottom></div><div class=t id=stretch></div></div>
            """);

        // Aligned toward the anchor (end of the top left area)
        Assert.Equal((80, 130, 20, 20), GetRect(document, "topleft"));
        Assert.Equal((165, 177.5, 20, 20), GetRect(document, "center"));

        // bottom: the area spans the three columns, aligned with anchor-center
        Assert.Equal((165, 225, 20, 20), GetRect(document, "bottom"));
        Assert.Equal((100, 150, 300, 75), GetRect(document, "stretch"));
    }

    [Fact]
    public void AnchorCenter_CentersOnTheAnchorWithinTheContainingBlock()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 100px; height: 100px; }
              #a { position: relative; left: 40px; top: 40px; width: 50px; height: 50px; anchor-name: --a; }
              .t { position: absolute; position-anchor: --a; justify-self: anchor-center; top: anchor(bottom); height: 10px; }
            </style>
            <div id=cb><div id=a></div><div class=t id=small style="width: 30px"></div><div class=t id=large style="width: 90px"></div></div>
            """);

        Assert.Equal(50, GetRect(document, "small").X);

        // Centered at 65 it would overflow the containing block: it stays in it
        Assert.Equal(10, GetRect(document, "large").X);
    }

    [Fact]
    public void PositionTryFallbacks_UsesTheFirstOptionThatFits()
    {
        var document = Parse("""
            <style>
              @position-try --left { right: anchor(--a left); left: auto; }
              #cb { position: relative; width: 300px; height: 200px; }
              #a { position: absolute; left: 200px; top: 50px; width: 50px; height: 50px; anchor-name: --a; }
              #target { position: absolute; left: anchor(--a right); top: anchor(--a top); width: 80px; height: 20px; position-try-fallbacks: --left, flip-inline; }
            </style>
            <div id=cb><div id=a></div><div id=target></div></div>
            """);

        // The base styles overflow (250 + 80 > 300): the @position-try rule places the box at the left of the anchor
        Assert.Equal((120, 50, 80, 20), GetRect(document, "target"));
        Assert.Equal("120px", GetComputedValue(document, "target", "left"));
    }

    [Fact]
    public void PositionTryFallbacks_TryTacticsFlipTheValues()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 200px; height: 200px; }
              #a { position: absolute; left: 50px; top: 150px; width: 50px; height: 20px; anchor-name: --a; }
              #target { position: absolute; position-anchor: --a; top: anchor(bottom); left: anchor(left); margin-top: 5px; width: 40px; height: 40px; position-try-fallbacks: flip-block; }
              #start { position: absolute; position-anchor: --a; left: 10px; top: anchor(bottom); width: 10px; height: 100px; position-try-fallbacks: flip-start; }
            </style>
            <div id=cb><div id=a></div><div id=target></div><div id=start></div></div>
            """);

        // flip-block: bottom: anchor(top) with margin-bottom: 5px
        Assert.Equal((50, 105, 40, 40), GetRect(document, "target"));
        Assert.Equal("5px", GetComputedValue(document, "target", "margin-bottom"));
        Assert.Equal("0px", GetComputedValue(document, "target", "margin-top"));

        // flip-start swaps top and left, width and height: left: anchor(right); top: 10px
        Assert.Equal((100, 10, 100, 10), GetRect(document, "start"));
    }

    [Fact]
    public void PositionTryOrder_SortsTheOptionsByTheirAvailableSpace()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 300px; height: 300px; }
              #a { position: absolute; left: 100px; top: 200px; width: 100px; height: 20px; anchor-name: --a; }
              #target { position: absolute; position-anchor: --a; position-area: bottom; width: 20px; height: 300px; position-try-fallbacks: flip-block; position-try-order: most-height; }
            </style>
            <div id=cb><div id=a></div><div id=target></div></div>
            """);

        // No option fits: the base option is kept; with most-height, flip-block (200px above the anchor) is tried first, but the
        // base styles are kept when nothing fits
        Assert.Equal(20, GetRect(document, "target").Width);
        Get(document, "target").Style.SetProperty("height", "150px");
        Assert.Equal(50, GetRect(document, "target").Y);
    }

    [Fact]
    public void ScrollCompensation_TheBoxFollowsItsDefaultAnchorWithoutLayout()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 300px; height: 300px; }
              #scroller { width: 200px; height: 200px; overflow: scroll; }
              #a { margin-top: 100px; width: 50px; height: 50px; anchor-name: --a; }
              #spacer { height: 1000px; }
              #target { position: absolute; position-anchor: --a; top: anchor(bottom); left: anchor(left); width: 10px; height: 10px; }
            </style>
            <div id=cb><div id=scroller><div id=a></div><div id=spacer></div></div><div id=target></div></div>
            """);

        Assert.Equal(150, GetRect(document, "target").Y);
        var layouts = document.GetLayoutEngine().LayoutCount;
        Get(document, "scroller").ScrollTop = 30;
        Assert.Equal(120, GetRect(document, "target").Y);
        Assert.Equal(120, Get(document, "target").OffsetTop);
        Assert.Equal(layouts, document.GetLayoutEngine().LayoutCount);
    }

    [Fact]
    public void ScrollCompensation_ThePositionFallbackRespondsToTheScroll()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 200px; height: 200px; }
              #scroller { width: 200px; height: 200px; overflow: scroll; }
              #abspos { position: absolute; width: 50px; height: 50px; position-area: bottom; position-try-fallbacks: flip-block; position-anchor: --a; }
              #anchor { anchor-name: --a; width: 50px; height: 50px; margin: 150px 0 150px 50px; }
            </style>
            <div id="cb"><div id="scroller"><div id="anchor"></div></div><div id="abspos"></div></div>
            """);

        RunFrames();
        Assert.Equal(100, GetRect(document, "abspos").Y);
        Get(document, "scroller").ScrollTop = 101;
        RunFrames();
        Assert.Equal(GetRect(document, "anchor").Y + 50, GetRect(document, "abspos").Y);
    }

    [Fact]
    public void ImplicitAnchor_IsThePopoverSource()
    {
        var document = Parse("""
            <style>
              #button { margin-left: 100px; margin-top: 50px; width: 80px; height: 20px; }
              #popover { margin: 0; padding: 0; border: 0; width: 50px; height: 50px; position-area: bottom span-right; }
            </style>
            <button id=button></button><div id=popover popover></div>
            """);

        var popover = Get(document, "popover");
        popover.ShowPopover(new ShowPopoverOptions { Source = Get(document, "button") });
        Assert.Equal((100, 70, 50, 50), GetRect(document, "popover"));
    }

    [Fact]
    public void PositionVisibility_HidesTheBoxWhenItsAnchorIsClipped()
    {
        var document = Parse("""
            <style>
              #scroller { width: 100px; height: 100px; overflow: hidden; }
              #a { margin-top: 200px; width: 50px; height: 50px; anchor-name: --a; }
              #target { position: absolute; position-anchor: --a; top: 0; left: 200px; width: 50px; height: 50px; }
            </style>
            <div id=scroller><div id=a></div></div><div id=target></div>
            """);

        Assert.NotSame(Get(document, "target"), document.ElementFromPoint(210, 10));
        Get(document, "target").Style.SetProperty("position-visibility", "always");
        Assert.Same(Get(document, "target"), document.ElementFromPoint(210, 10));
    }

    [Fact]
    public void PositionTryRule_IsExposedByTheCssom()
    {
        var document = Parse("<style>@position-try --pf { top: anchor(bottom); color: red; margin: 1px !important; }</style>");
        var rule = Assert.IsType<Meziantou.Framework.WebPlatform.Css.Om.CssPositionTryRule>(document.StyleSheets[1]!.CssRules[0]);
        Assert.Equal("--pf", rule.Name);
        Assert.Equal("@position-try --pf { top: anchor(bottom); }", rule.CssText);
        Assert.Equal("anchor(bottom)", rule.Style.Top);
    }

    [Fact]
    public void AnchorRelevance_KeepsTheContentsWithTheAnchorsOfThePositionOptionsRelevant()
    {
        var document = Parse("""
            <style>
              @position-try --foo { right: anchor(--a2 left); }
              #container { position: relative; height: 10000px; }
              #lock { height: 400px; width: 400px; content-visibility: auto; }
              #anchor { anchor-name: --a1; position: absolute; }
              #anchor2 { anchor-name: --a2; position: absolute; left: 60px; }
              #positioned { position: absolute; right: anchor(--a1 left); position-try: --foo; }
            </style>
            <div id=container><div id=lock><div id=anchor>a</div></div><div id=anchor2>b</div><div id=positioned>c</div></div>
            """);

        RunFrames(2);
        _ = document.DefaultView!.ScrollTo(0, 10000);
        RunFrames(4);

        // #anchor is a target anchor element of an option tried by #positioned (outside of #lock): #lock stays relevant to the user
        Assert.True(Meziantou.Framework.WebPlatform.Css.Layout.ContentRelevancy.GetState(Get(document, "lock"))?.CurrentlyRelevant);
    }

    [Fact]
    public void PositionTryOrder_SortsTheBaseStylesWithTheOptions()
    {
        // The base styles fit above the anchor (50px), but the option below it has the tallest inset-modified containing block
        var document = Parse("""
            <style>
              @position-try --bottom { top: anchor(bottom); bottom: unset; }
              #cb { position: relative; width: 300px; height: 300px; }
              #a { position: absolute; left: 0; top: 50px; width: 100px; height: 50px; anchor-name: --a; }
              #target { position: absolute; position-anchor: --a; bottom: anchor(top); width: 100px; height: 50px; position-try: most-height --bottom; }
            </style>
            <div id=cb><div id=a></div><div id=target></div></div>
            """);

        Assert.Equal(100, GetRect(document, "target").Y);
    }

    [Fact]
    public void PositionVisibility_NoOverflowAppliesWithoutAnchor()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 100px; height: 100px; }
              #target { position: absolute; top: 10px; left: 0; width: 50px; height: 100px; position-visibility: no-overflow; }
            </style>
            <div id=cb><div id=target></div></div>
            """);

        Assert.NotSame(Get(document, "target"), document.ElementFromPoint(10, 50));
        Get(document, "target").Style.SetProperty("height", "50px");
        Assert.Same(Get(document, "target"), document.ElementFromPoint(10, 50));
    }

    [Fact]
    public void PositionVisibility_NoOverflowFollowsTheDefaultScrollShift()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 300px; height: 150px; }
              #scroller { width: 300px; height: 150px; overflow: hidden scroll; scrollbar-width: none; }
              #a { width: 100px; height: 100px; anchor-name: --a; }
              #spacer { height: 300px; }
              #target { position: absolute; position-anchor: --a; top: anchor(bottom); left: 150px; width: 50px; height: 100px; position-visibility: no-overflow; }
            </style>
            <div id=cb><div id=scroller><div id=a></div><div id=spacer></div></div><div id=target></div></div>
            """);

        // The box overflows the bottom of its containing block (100px to 200px in 150px), until the scroll moves it up with its anchor
        Assert.NotSame(Get(document, "target"), document.ElementFromPoint(160, 120));
        Get(document, "scroller").ScrollTop = 50;
        Assert.Equal(50, GetRect(document, "target").Y);
        Assert.Same(Get(document, "target"), document.ElementFromPoint(160, 120));
    }

    [Fact]
    public void PositionVisibility_HidesTheBoxWhenItsAnchorIsHiddenByPositionVisibility()
    {
        var document = Parse("""
            <style>
              #scroller { width: 300px; height: 100px; overflow: hidden scroll; scrollbar-width: none; }
              #a1 { width: 100px; height: 100px; anchor-name: --a1; }
              #spacer { height: 100px; }
              .anchored { position: absolute; top: 0; left: 0; position-visibility: anchor-visible; position-area: bottom; width: 100px; height: 50px; }
              #a2 { anchor-name: --a2; position-anchor: --a1; }
              #target { position-anchor: --a2; }
            </style>
            <div id=scroller><div id=a1></div><div id=spacer></div></div><div id=a2 class=anchored></div><div id=target class=anchored></div>
            """);

        Assert.Same(Get(document, "target"), document.ElementFromPoint(10, 160));

        // #a1 is scrolled out of the scrollport: #a2 is hidden, and so is the box anchored to it
        Get(document, "scroller").ScrollTop = 100;
        RunFrames();
        Assert.NotSame(Get(document, "a2"), document.ElementFromPoint(10, 10));
        Assert.NotSame(Get(document, "target"), document.ElementFromPoint(10, 60));
        Assert.NotSame(Get(document, "target"), document.ElementFromPoint(10, 160));
    }

    [Fact]
    public void PositionArea_OverflowsTheInitialContainingBlockInTheReachableDirections()
    {
        var document = Parse("""
            <style>
              #a { position: absolute; top: 300px; left: 400px; width: 40px; height: 40px; anchor-name: --a; }
              .target { position: absolute; position-anchor: --a; width: 10px; height: 400px; }
              #below { position-area: bottom center; }
              #above { position-area: top center; }
            </style>
            <div id=a></div><div id=below class=target></div><div id=above class=target></div>
            """);

        // The viewport scrolls downward: the box below the anchor stays aligned with it and overflows; the box above the anchor
        // cannot overflow upward (the unreachable scrollable overflow region), it is shifted into the containing block
        Assert.Equal(340, GetRect(document, "below").Y);
        Assert.Equal(0, GetRect(document, "above").Y);
    }

    [Theory]
    [InlineData("", 200)]
    [InlineData("<div style='height: 2000px'></div>", 340)]
    public void PositionArea_FixedBoxOverflowsTheViewportInTheScrollableOverflowArea(string content, double expectedY)
    {
        var document = Parse("""
            <style>
              #a { position: absolute; top: 300px; left: 400px; width: 40px; height: 40px; anchor-name: --a; }
              #target { position: fixed; position-anchor: --a; position-area: bottom center; width: 10px; height: 400px; }
            </style>
            <div id=a></div><div id=target></div>
            """ + content);

        Assert.Equal(expectedY, GetRect(document, "target").Y);
    }

    [Fact]
    public void ScrollIntoView_ScrollsTheViewportForAFixedBoxThatFollowsItsAnchor()
    {
        var document = Parse("""
            <style>
              #a { position: absolute; top: 1000px; left: 0; width: 100px; height: 100px; anchor-name: --a; }
              #target { position: fixed; position-anchor: --a; top: anchor(top); left: 0; width: 50px; height: 50px; }
              #fixed { position: fixed; top: 700px; left: 200px; width: 50px; height: 50px; }
            </style>
            <div id=a></div><div id=target></div><div id=fixed></div><div style='height: 3000px'></div>
            """);

        // A fixed positioned box does not move with the viewport: the viewport is not scrolled for it
        _ = Get(document, "fixed").ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Start });
        Assert.Equal(0, document.DefaultView!.ScrollY);

        // The box that compensates for the scroll of its anchor in the vertical axis is scrolled into view with the document
        _ = Get(document, "target").ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Start });
        Assert.Equal(1000, document.DefaultView!.ScrollY);
        Assert.Equal(0, GetRect(document, "target").Y);
    }

    [Fact]
    public void AnchoredContainerQueries_MatchThePositionOptionAppliedByTheLayout()
    {
        var document = Parse("""
            <style>
              #anchor { anchor-name: --a; margin-top: 100px; width: 100px; height: 100px; }
              #anchored { position: absolute; position-anchor: --a; position-area: top; position-try-fallbacks: flip-block, --right;
                width: 100px; height: 100px; container-type: anchored; }
              @position-try --right { position-area: right; }
              #child { --fallback: none; }
              @container anchored(fallback) { #child { --any: yes; } }
              @container anchored(fallback: flip-block) { #child { --fallback: flip-block; } }
              @container anchored(fallback: bottom) { #child { --area: bottom; } }
              @container anchored(fallback: --right) { #child { --fallback: right; } }
            </style>
            <div id=anchor></div><div id=anchored><div id=child></div></div>
            """);

        Assert.Equal(0, Get(document, "anchored").OffsetTop);
        Assert.Equal("none", GetComputedValue(document, "child", "--fallback"));
        Assert.Equal("", GetComputedValue(document, "child", "--any"));

        // The option chosen by the layout is matched by the styles of the descendants, computed again during the same layout
        Get(document, "anchor").Style.SetProperty("margin-top", "50px");
        Assert.Equal(150, Get(document, "anchored").OffsetTop);
        Assert.Equal("flip-block", GetComputedValue(document, "child", "--fallback"));
        Assert.Equal("yes", GetComputedValue(document, "child", "--any"));

        // A <position-area> query does not match a try tactic
        Assert.Equal("", GetComputedValue(document, "child", "--area"));
        Assert.Equal("anchored", GetComputedValue(document, "anchored", "container-type"));
    }

    [Theory]
    [InlineData("anchored(fallback: --foo flip-inline)", "anchored(fallback: --foo flip-inline)")]
    [InlineData("ANCHORED((fallback: FLIP-X FLIP-Y) or (FALLBACK))", "anchored((fallback: flip-x flip-y) or (fallback))")]
    [InlineData("anchored(fallback: any top)", "anchored(fallback: any top)")]
    [InlineData("anchored(fallback: 1)", "anchored(fallback: 1)")]
    public void AnchoredContainerQueries_Serialization(string condition, string expected)
    {
        var document = Parse("<style>@container " + condition + " { }</style>");
        var rule = (Meziantou.Framework.WebPlatform.Css.Om.CssContainerRule)document.StyleSheets[1]!.CssRules[0];
        Assert.Equal(expected, rule.ConditionText);
    }

    [Fact]
    public void AnchorFunctions_InterpolateAsLengths()
    {
        var document = Parse("""
            <style>
              #a1 { width: 200px; height: 100px; anchor-name: --a1; }
              #a2 { margin-left: 400px; margin-top: 200px; width: 100px; height: 200px; anchor-name: --a2; }
              #target { position: fixed; width: 100px; height: 100px; transition: all 10s -5s linear; }
              #target.before { top: anchor(--a1 top); left: anchor(--a1 right); width: anchor-size(--a1 width); }
              #target.after { top: anchor(--a2 bottom); left: anchor(--a2 left); width: anchor-size(--a2 width); }
            </style>
            <div id=a1></div><div id=a2></div><div id=target class=before></div>
            """);
        var target = Get(document, "target");
        Assert.Equal(200, target.OffsetLeft);

        // The transition interpolates the two anchor functions, which the layout resolves (halfway)
        target.ClassName = "after";
        Assert.Equal((300, 250, 150), (target.OffsetLeft, target.OffsetTop, target.OffsetWidth));
        Assert.Equal("250px", GetComputedValue(document, "target", "top"));
    }

    [Fact]
    public void AnchorFunctions_TransitionWhenTheirResultChanges()
    {
        var document = Parse("""
            <style>
              #anchor { position: absolute; left: 400px; width: 100px; height: 100px; anchor-name: --a; }
              #target { position: absolute; position-anchor: --a; left: anchor(left); width: anchor-size(width); height: 10px; transition: left 10s -5s linear, width 10s -5s linear; }
            </style>
            <div id=anchor></div><div id=target></div>
            """);
        var target = Get(document, "target");
        Assert.Equal((400, 100), (target.OffsetLeft, target.OffsetWidth));

        // The declared values do not change: the resolved values transition (halfway)
        Get(document, "anchor").Style.SetProperty("left", "200px");
        Get(document, "anchor").Style.SetProperty("width", "200px");
        Assert.Equal((300, 150), (target.OffsetLeft, target.OffsetWidth));

        // The anchor moves again: the transition restarts from the current value
        Get(document, "anchor").Style.SetProperty("left", "0px");
        Assert.Equal(150, target.OffsetLeft);
    }

    [Fact]
    public void PositionOption_TransitionsWhenItIsAppliedAndUnapplied()
    {
        var document = Parse("""
            <style>
              #cb { position: relative; width: 400px; height: 250px; }
              #cb.shrink { width: 300px; }
              #target { position: absolute; left: 300px; width: 50px; height: 50px; }
              #target.fallback { position-try-fallbacks: --200; }
              #target.anim { transition: left 1000s steps(2, start); }
              @position-try --200 { left: 200px; }
            </style>
            <div id=cb><div id=target></div></div>
            """);
        var cb = Get(document, "cb");
        var target = Get(document, "target");
        Assert.Equal(300, target.OffsetLeft);
        cb.ClassName = "shrink";
        target.ClassName = "fallback anim";
        Assert.Equal(250, target.OffsetLeft);

        foreach (var animation in target.GetAnimations())
        {
            animation.Cancel();
        }

        target.ClassName = "fallback";
        Assert.Equal(200, target.OffsetLeft);
        cb.ClassName = "";
        target.ClassName = "anim";
        Assert.Equal(250, target.OffsetLeft);
    }
}
