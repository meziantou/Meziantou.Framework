using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The overflow, the scroll containers, the scrollbars and the scrolling APIs of CSSOM View, measured with Ahem.</summary>
public sealed class ScrollingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport with 15px classic scrollbars.</summary>
    private Document Parse(string html, double scrollbarThickness = 15) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html, scrollbarThickness);

    private Document Load(string html, double scrollbarThickness = 15, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600, ScrollbarThickness = scrollbarThickness } });
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static Element Get(Document document, string id) => document.GetElementById(id)!;

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(Get(document, id)) ?? throw new InvalidOperationException("No box");

    /// <summary>Runs the rendering steps of the next frames (the virtual clock advances by one frame each time).</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    [Fact]
    public void ContainLayout_ScrollHeightIncludesZeroWidthChildren()
    {
        var document = Parse("<div id=target style='contain: layout; height: 0; width: 0; overflow: hidden'><div style='height: 2000px'></div></div>");
        Assert.Equal(2000, Get(document, "target").ScrollHeight);
    }

    [Fact]
    public void ContainLayout_DynamicOverflow()
    {
        var document = Parse("<style>#target { contain: layout; height: 0; width: 0 }</style><div id=target><div style='height: 2000px'></div></div>");
        var target = (HtmlElement)Get(document, "target");
        target.Style.SetProperty("display", "flex");
        target.Style.SetProperty("border", "0");
        target.Style.SetProperty("padding", "0");
        Assert.Equal(2000, target.ScrollHeight);
        target.Style.SetProperty("overflow", "hidden");
        Assert.Equal(2000, target.ScrollHeight);
    }

    [Fact]
    public void Scrollbars_OverflowScroll_TakeSpaceFromTheContentBox()
    {
        var document = Parse("<div id=a style='overflow: scroll; width: 100px; height: 100px; border: 2px solid; padding: 3px'><div id=b></div></div>");
        var a = Get(document, "a");
        Assert.Equal(new PhysicalRect(0, 0, 110, 110), GetRect(document, "a"));
        Assert.Equal((91, 91), (a.ClientWidth, a.ClientHeight));
        Assert.Equal((2, 2), (a.ClientLeft, a.ClientTop));
        Assert.Equal(85, GetRect(document, "b").Width);
    }

    [Fact]
    public void Scrollbars_OverflowAuto_OnlyWhenOverflowing()
    {
        var document = Parse("""
            <div id=a style='overflow: auto; width: 100px; height: 100px'><div id=b style='height: 300px'></div></div>
            <div id=c style='overflow: auto; width: 100px; height: 100px'><div id=d style='height: 50px'></div></div>
            """);
        Assert.Equal((85, 100), (Get(document, "a").ClientWidth, Get(document, "a").ClientHeight));
        Assert.Equal(85, GetRect(document, "b").Width);
        Assert.Equal((100, 100), (Get(document, "c").ClientWidth, Get(document, "c").ClientHeight));
        Assert.Equal(100, GetRect(document, "d").Width);
    }

    [Fact]
    public void Scrollbars_ShrinkToFitAddsTheScrollbar()
    {
        var document = Parse("<div id=a style='display: inline-block; overflow-y: scroll; height: 50px'><span style='display: inline-block; width: 40px; height: 10px'></span></div>");
        Assert.Equal(55, GetRect(document, "a").Width);
        Assert.Equal(40, Get(document, "a").ClientWidth);
    }

    [Fact]
    public void Scrollbars_AutoScrollbarOfADescendantEnlargesTheBoxesSizedFromItsContents()
    {
        // WPT css/css-flexbox/scrollbars-auto.html, css/css-overflow/inline-block-with-scrollable-descendant.html: the document is
        // laid out again when a pass displays scrollbars that the content-based sizes did not assume
        var document = Parse("""
            <div style='display: flex; align-items: flex-start'>
              <div id=a style='flex: none; overflow: auto; max-height: 50px'><div style='width: 30px; height: 200px'></div></div>
            </div>
            <div id=b style='display: inline-block'>
              <div style='overflow: auto; max-height: 50px'><div style='width: 30px; height: 200px'></div></div>
            </div>
            """);
        Assert.Equal(45, GetRect(document, "a").Width);
        Assert.Equal(45, GetRect(document, "b").Width);
    }

    [Fact]
    public void Scrollbars_RightToLeft_VerticalScrollbarOnTheLeft()
    {
        var document = Parse("<div id=a dir=rtl style='overflow: scroll; width: 100px; height: 100px'><div id=b style='height: 10px'></div></div>");
        Assert.Equal(15, Get(document, "a").ClientLeft);
        Assert.Equal(new PhysicalRect(15, 0, 85, 10), GetRect(document, "b"));
    }

    [Fact]
    public void Scrollbars_GutterStable()
    {
        var document = Parse("""
            <div id=a style='overflow: hidden; scrollbar-gutter: stable; width: 100px; height: 100px'></div>
            <div id=b style='overflow: auto; scrollbar-gutter: stable both-edges; width: 100px; height: 100px'></div>
            <div id=c style='overflow: visible; scrollbar-gutter: stable; width: 100px; height: 100px'></div>
            """);
        Assert.Equal(85, Get(document, "a").ClientWidth);
        Assert.Equal(70, Get(document, "b").ClientWidth);
        Assert.Equal(15, Get(document, "b").ClientLeft);
        Assert.Equal(100, Get(document, "c").ClientWidth);
    }

    [Theory]
    [InlineData("auto", 85)]
    [InlineData("thin", 89)]
    [InlineData("none", 100)]
    public void Scrollbars_ScrollbarWidth(string value, int clientWidth)
    {
        var document = Parse($"<div id=a style='overflow: scroll; scrollbar-width: {value}; width: 100px; height: 100px'></div>");
        Assert.Equal(clientWidth, Get(document, "a").ClientWidth);
    }

    [Fact]
    public void Scrollbars_OverlayScrollbarsTakeNoSpace()
    {
        var document = Parse("<div id=a style='overflow: scroll; width: 100px; height: 100px'></div><div style='height: 2000px'></div>", scrollbarThickness: 0);
        Assert.Equal((100, 100), (Get(document, "a").ClientWidth, Get(document, "a").ClientHeight));
        Assert.Equal(800, document.DocumentElement!.ClientWidth);
    }

    [Fact]
    public void Viewport_ScrollbarWhenTheDocumentOverflows()
    {
        var document = Parse("<div id=a style='height: 2000px'></div>");
        Assert.Equal((785, 600), (document.DocumentElement!.ClientWidth, document.DocumentElement.ClientHeight));
        Assert.Equal(785, GetRect(document, "a").Width);
        Assert.Equal(2000, document.DocumentElement.ScrollHeight);
        Assert.Equal(800, document.DefaultView!.InnerWidth);
    }

    [Fact]
    public void Viewport_OverflowPropagatedFromTheBody()
    {
        var document = Parse("<style>body { overflow: hidden }</style><div style='height: 2000px'></div><div id=a style='overflow: hidden; height: 10px'></div>");
        Assert.Equal(800, document.DocumentElement!.ClientWidth);

        // The body is not a scroll container: its overflow is applied to the viewport
        Assert.False(document.GetLayoutEngine().GetFragments(document.Body!)[0].IsScrollContainer);
        Assert.True(document.GetLayoutEngine().GetFragments(Get(document, "a"))[0].IsScrollContainer);
    }

    [Fact]
    public void ScrollableOverflow_PaddingInflation()
    {
        var document = Parse("""
            <style>.s { display: block; width: 100px; height: 100px; overflow: scroll; padding: 10px 5px; line-height: 0 }</style>
            <div class=s id=a><div style='position: relative; width: 0; height: 200px; margin-bottom: 50px; top: -1000px'></div></div>
            <div class=s id=b><div style='position: relative; width: 200px; height: 0; margin-right: 50px; left: -1000px'></div></div>
            <div class=s id=c><div style='width: 0; margin-bottom: 100px'><div style='height: 200px; margin-bottom: -50px'></div></div></div>
            """);
        Assert.Equal(270, Get(document, "a").ScrollHeight);
        Assert.Equal(260, Get(document, "b").ScrollWidth);
        Assert.Equal(270, Get(document, "c").ScrollHeight);
    }

    [Fact]
    public void ScrollableOverflow_UnreachableRegionIsExcluded()
    {
        var document = Parse("""
            <div id=a style='position: relative; overflow: auto; width: 200px; height: 200px'><div style='position: absolute; width: 1000px; height: 1000px; top: -1000px'></div></div>
            <div id=b dir=rtl style='position: relative; overflow: auto; width: 200px; height: 200px'><div style='position: absolute; width: 1000px; height: 100px; left: -500px'></div></div>
            """);
        Assert.Equal((200, 200), (Get(document, "a").ScrollWidth, Get(document, "a").ScrollHeight));
        Assert.Equal(700, Get(document, "b").ScrollWidth);
    }

    [Fact]
    public void ScrollableOverflow_ClipIsPerAxis()
    {
        var document = Parse("""
            <div id=outer style='overflow: hidden; width: 100px; height: 100px'>
              <div style='overflow-x: clip; width: 50px; height: 50px'><div style='width: 300px; height: 300px'></div></div>
            </div>
            <div id=outer2 style='overflow: hidden; width: 100px; height: 100px'>
              <div style='overflow: clip; overflow-clip-margin: 20px; width: 50px; height: 50px'><div style='width: 300px; height: 300px'></div></div>
            </div>
            """);
        Assert.Equal((100, 300), (Get(document, "outer").ScrollWidth, Get(document, "outer").ScrollHeight));
        Assert.Equal((100, 100), (Get(document, "outer2").ScrollWidth, Get(document, "outer2").ScrollHeight));
    }

    [Fact]
    public void ScrollTop_ScrollsTheContents()
    {
        var document = Parse("<div id=a style='overflow: hidden; width: 100px; height: 100px'><div id=b style='width: 300px; height: 300px'></div></div>");
        var a = Get(document, "a");
        a.ScrollTop = 50;
        a.ScrollLeft = 1000;
        Assert.Equal(50, a.ScrollTop);
        Assert.Equal(200, a.ScrollLeft);
        Assert.Equal(new PhysicalRect(-200, -50, 300, 300), GetRect(document, "b"));

        a.ScrollTop = -10;
        Assert.Equal(0, a.ScrollTop);
        a.ScrollTop = double.NaN;
        Assert.Equal(0, a.ScrollTop);
    }

    [Fact]
    public void ScrollPosition_SurvivesAndIsClampedOnRelayout()
    {
        var document = Parse("<div id=a style='overflow: hidden; width: 100px; height: 100px'><div id=b style='height: 300px'></div></div>");
        var a = Get(document, "a");
        a.ScrollTop = 150;
        ((HtmlElement)Get(document, "b")).Style.SetProperty("width", "50px");
        Assert.Equal(150, a.ScrollTop);
        ((HtmlElement)Get(document, "b")).Style.SetProperty("height", "150px");
        Assert.Equal(50, a.ScrollTop);
        ((HtmlElement)a).Style.SetProperty("overflow", "visible");
        Assert.Equal(0, a.ScrollTop);

        // As in the browsers, the scroll position of an element that stops being a scroll container is kept
        ((HtmlElement)a).Style.SetProperty("overflow", "hidden");
        Assert.Equal(50, a.ScrollTop);
    }

    [Fact]
    public void ScrollLeft_RightToLeftIsNegative()
    {
        var document = Parse("<div id=a dir=rtl style='overflow: hidden; width: 100px; height: 100px'><div style='width: 300px; height: 10px'></div></div>");
        var a = Get(document, "a");
        Assert.Equal(0, a.ScrollLeft);
        Assert.Equal(300, a.ScrollWidth);
        a.ScrollLeft = 50;
        Assert.Equal(0, a.ScrollLeft);
        a.ScrollLeft = -500;
        Assert.Equal(-200, a.ScrollLeft);
    }

    [Fact]
    public void ScrollLeft_VerticalRlIsNegative()
    {
        var document = Parse("<div id=a style='writing-mode: vertical-rl; overflow: hidden; width: 100px; height: 100px'><div style='width: 300px; height: 10px'></div></div>");
        var a = Get(document, "a");
        a.ScrollLeft = -1000;
        Assert.Equal(-200, a.ScrollLeft);
    }

    [Fact]
    public void SingleAxisScrollContainer_HiddenClip()
    {
        var document = Parse("<div id=a style='overflow: hidden clip; width: 100px; height: 100px'><div style='width: 300px; height: 300px'></div></div>");
        var a = Get(document, "a");
        a.ScrollTop = 50;
        a.ScrollLeft = 50;
        Assert.Equal((50, 0), (a.ScrollLeft, a.ScrollTop));
    }

    [Fact]
    public void ScrollMethods_ScrollToAndScrollBy()
    {
        var document = Parse("<div id=a style='overflow: scroll; width: 100px; height: 100px'><div style='width: 1000px; height: 1000px'></div></div>");
        var a = Get(document, "a");
        Assert.True(a.ScrollTo(25, 50).IsCompleted);
        Assert.Equal((25, 50), (a.ScrollLeft, a.ScrollTop));
        _ = a.ScrollBy(new ScrollToOptions { Left = 10 });
        Assert.Equal((35, 50), (a.ScrollLeft, a.ScrollTop));
        _ = a.Scroll(new ScrollToOptions { Top = 5 });
        Assert.Equal((35, 5), (a.ScrollLeft, a.ScrollTop));
    }

    [Fact]
    public void Window_ScrollsTheViewport()
    {
        var document = Parse("<div style='height: 2000px; width: 2000px'></div><div id=f style='position: fixed; top: 10px; left: 0; width: 10px; height: 10px'></div><div id=a style='position: absolute; top: 100px; width: 10px; height: 10px'></div>");
        var window = document.DefaultView!;
        _ = window.ScrollTo(30, 40);
        Assert.Equal((30, 40), (window.ScrollX, window.ScrollY));
        Assert.Equal((30, 40), (document.DocumentElement!.ScrollLeft, document.DocumentElement.ScrollTop));
        Assert.Equal(new PhysicalRect(0, 10, 10, 10), GetRect(document, "f"));
        Assert.Equal(new PhysicalRect(-30, 60, 10, 10), GetRect(document, "a"));
        _ = window.ScrollBy(0, 100000);
        Assert.Equal(2000 - 585, window.ScrollY);
        document.DocumentElement.ScrollTop = 7;
        Assert.Equal(7, window.ScrollY);
    }

    [Fact]
    public void ScrollEvents_FiredInTheRenderingSteps()
    {
        var document = Parse("<div id=a style='overflow: scroll; width: 100px; height: 100px'><div style='height: 1000px'></div></div><div style='height: 2000px'></div>");
        var events = new List<string>();
        var a = Get(document, "a");
        a.AddEventListener("scroll", e => events.Add("a:" + e.Type + ":" + e.Bubbles));
        a.AddEventListener("scrollend", e => events.Add("a:" + e.Type));
        document.AddEventListener("scroll", e => events.Add("document:" + e.Type + ":" + e.Bubbles));
        document.DefaultView!.AddEventListener("scroll", e => events.Add("window:" + e.Type));
        a.ScrollTop = 10;
        a.ScrollTop = 20;
        _ = document.DefaultView!.ScrollTo(0, 10);
        Assert.Empty(events);
        RunFrames();
        Assert.Equal(["a:scroll:False", "a:scrollend", "document:scroll:True", "window:scroll"], events);

        // No scroll event when the position does not change
        events.Clear();
        a.ScrollTop = 20;
        RunFrames();
        Assert.Empty(events);
    }

    [Fact]
    public void SmoothScroll_AnimatedOverFrames()
    {
        var document = Parse("<div id=a style='overflow: scroll; scroll-behavior: smooth; width: 100px; height: 100px'><div style='height: 1000px'></div></div>");
        var a = Get(document, "a");
        var task = a.ScrollTo(0, 500);
        Assert.False(task.IsCompleted);
        Assert.Equal(0, a.ScrollTop);
        RunFrames();
        Assert.InRange(a.ScrollTop, 0.001, 499);
        RunFrames(30);
        Assert.Equal(500, a.ScrollTop);
        Assert.True(task.IsCompleted);

        // An instant scroll aborts a smooth scroll and resolves its promise
        task = a.Scroll(new ScrollToOptions { Top = 0, Behavior = ScrollBehavior.Smooth });
        RunFrames();
        var instant = a.Scroll(new ScrollToOptions { Top = 100, Behavior = ScrollBehavior.Instant });
        Assert.True(task.IsCompleted);
        Assert.True(instant.IsCompleted);
        Assert.Equal(100, a.ScrollTop);
    }

    [Fact]
    public void Sticky_OffsetFollowsTheScrollPosition()
    {
        var document = Parse("<div id=s style='overflow: scroll; width: 100px; height: 100px'><div style='height: 50px'></div><div id=t style='position: sticky; top: 0; height: 10px'></div><div style='height: 500px'></div></div>");
        var s = Get(document, "s");
        Assert.Equal(50, GetRect(document, "t").Y);
        s.ScrollTop = 30;
        Assert.Equal(20, GetRect(document, "t").Y);
        s.ScrollTop = 200;
        Assert.Equal(0, GetRect(document, "t").Y);
        s.ScrollTop = 0;
        Assert.Equal(50, GetRect(document, "t").Y);
    }

    [Fact]
    public void ScrollIntoView_AlignsTheElement()
    {
        var document = Parse("""
            <div id=s style='overflow: scroll; width: 100px; height: 100px; scroll-padding-top: 5px'>
              <div style='height: 300px'></div><div id=t style='height: 20px; width: 20px; scroll-margin-top: 3px'></div><div style='height: 500px'></div>
            </div>
            <div style='height: 2000px'></div>
            """);
        var s = Get(document, "s");
        var t = Get(document, "t");
        _ = t.ScrollIntoView();
        Assert.Equal(300 - 5 - 3, s.ScrollTop);
        _ = t.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.End });
        Assert.Equal(320 - 85, s.ScrollTop);
        _ = t.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Center });
        Assert.Equal(308.5 - 45, s.ScrollTop);
        _ = t.ScrollIntoView(new ScrollIntoViewOptions { Block = ScrollLogicalPosition.Nearest });
        Assert.Equal(308.5 - 45, s.ScrollTop);
        Assert.Equal(0, document.DefaultView!.ScrollY);
    }

    [Fact]
    public void ScrollIntoView_ScrollsTheViewport()
    {
        var document = Parse("<div style='height: 1000px'></div><div id=t style='height: 20px'></div><div style='height: 1000px'></div>");
        _ = Get(document, "t").ScrollIntoView();
        Assert.Equal(1000, document.DefaultView!.ScrollY);
        _ = Get(document, "t").ScrollIntoView(alignToTop: false);
        Assert.Equal(1020 - 600, document.DefaultView!.ScrollY);
    }

    [Fact]
    public void FragmentNavigation_ScrollsTheTargetIntoView()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 }</style><div style='height: 1000px'></div><div id=t style='height: 20px'></div><div style='height: 1000px'></div>", url: "https://example.com/#t");
        Assert.Equal(1000, document.DefaultView!.ScrollY);
    }

    [Fact]
    public void ScrollIntoView_InAFixedBoxDoesNotScrollTheViewport()
    {
        var document = Parse("<style>html { width: 200vw; height: 200vh }</style><div id=f style='position: fixed; top: 0; left: -200px; width: 100px; height: 100px; overflow: scroll'><div id=t style='width: 100px; height: 100px'></div><div style='height: 200vh'></div></div>");
        _ = document.DefaultView!.ScrollTo(0, 10);
        _ = Get(document, "t").ScrollIntoView(new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth });
        RunFrames(30);
        Assert.Equal(10, document.DefaultView!.ScrollY);
    }
}
