using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>CSS Containment (https://drafts.csswg.org/css-contain-2/): contain, content-visibility and the last remembered size of contain-intrinsic-size.</summary>
public sealed class ContainmentTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600, ScrollbarThickness = 15 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static HtmlElement Get(Document document, string id) => (HtmlElement)document.GetElementById(id)!;

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
    public void ContentVisibilityHidden_ScrollMetricsOfTheSkippingBox()
    {
        var document = Parse("<div id=outer style='width: 100px; height: 100px; overflow: scroll; content-visibility: hidden'><div style='margin: 25px; width: 250px; height: 250px'></div></div>");
        var outer = Get(document, "outer");
        // The scrollable overflow includes the inline-end margin of the child, as in the browsers (WPT content-visibility-090.html
        // expects 275, all the browsers fail it)
        Assert.Equal(300, outer.ScrollWidth);
        Assert.Equal(300, outer.ScrollHeight);
    }

    [Fact]
    public void ContentVisibilityHidden_GeometryOfTheSkippedContents()
    {
        var document = Parse("<div id=outer style='width: 100px; height: 100px; content-visibility: hidden'><div id=inner style='border: 25px solid; margin: 25px; width: 50px; height: 50px'></div></div>");
        var inner = Get(document, "inner");
        inner.Style.SetProperty("margin-left", "20px");
        Assert.Equal(20, inner.OffsetLeft);
        inner.Style.SetProperty("margin-top", "20px");
        Assert.Equal(20, inner.OffsetTop);
        inner.Style.SetProperty("width", "40px");
        Assert.Equal(90, inner.OffsetWidth);
    }

    [Fact]
    public void ContentVisibilityHidden_GeometryOfTheSkippedContents_BeforeRendering()
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => """
            <!DOCTYPE html><style>body { margin: 0; padding: 0 } #outer { width: 100px; height: 100px; content-visibility: hidden } #inner { border: 25px black solid; margin: 25px; width: 50px; height: 50px }</style>
            <body><div id="outer"><div id="inner"></div></div></body>
            """);
        var inner = Get(document, "inner");
        inner.Style.SetProperty("margin-left", "20px");
        Assert.Equal(20, inner.OffsetLeft);
        inner.Style.SetProperty("margin-top", "20px");
        Assert.Equal(20, inner.OffsetTop);
    }

    [Theory]
    [InlineData("contain-intrinsic-size: auto 40px auto 20px", "contain-intrinsic-width", "auto 40px")]
    [InlineData("contain-intrinsic-size: auto 40px auto 20px", "contain-intrinsic-height", "auto 20px")]
    [InlineData("contain-intrinsic-size: auto 40px auto 20px", "contain-intrinsic-size", "auto 40px auto 20px")]
    [InlineData("contain-intrinsic-size: auto none", "contain-intrinsic-size", "auto none")]
    [InlineData("contain-intrinsic-size: 10px; content-visibility: auto", "contain-intrinsic-size", "auto 10px")]
    [InlineData("content-visibility: auto", "contain-intrinsic-width", "auto none")]
    [InlineData("contain: style layout paint", "contain", "content")]
    [InlineData("contain: paint size layout style", "contain", "strict")]
    [InlineData("contain: paint layout", "contain", "layout paint")]
    public void ComputedValues(string style, string property, string expected)
    {
        var document = Parse($"<div id=target style='{style}'></div>");
        Assert.Equal(expected, document.DefaultView!.GetComputedStyle(Get(document, "target")).GetPropertyValue(property));
    }

    [Fact]
    public void LastRememberedSize_NotRecordedBeforeTheRenderingUpdate()
    {
        var document = Parse("""
            <style>.target { width: max-content; height: max-content; border: 1px solid } .target::before { content: ""; display: block; width: 100px; height: 50px }
            .cis-auto .target { contain-intrinsic-size: auto 40px auto 20px } .skip-contents { content-visibility: hidden }</style>
            <div class=target id=target1></div>
            """);
        document.Body!.ClassList.Add("cis-auto");
        var target = Get(document, "target1");
        target.ClassList.Add("skip-contents");
        Assert.Equal(40, target.ClientWidth);
        Assert.Equal(20, target.ClientHeight);

        // The size is recorded by the rendering update when the element does not skip its contents
        target.ClassList.Remove("skip-contents");
        Assert.Equal(100, target.ClientWidth);
        RunFrames();
        target.ClassList.Add("skip-contents");
        Assert.Equal(100, target.ClientWidth);
        Assert.Equal(50, target.ClientHeight);
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("paint")]
    [InlineData("content")]
    public void LayoutAndPaintContainment_EstablishAnIndependentFormattingContextAndAContainingBlock(string contain)
    {
        // The margins of the child do not collapse with the ones of the box, and the absolutely positioned child is positioned
        // in the box
        var document = Parse($"<div style='height: 30px'></div><div id=box style='contain: {contain}'><div id=child style='margin-top: 20px; height: 10px'></div><div id=abs style='position: absolute; top: 5px'></div></div>");
        Assert.Equal(new PhysicalRect(0, 30, 800, 30), GetRect(document, "box"));
        Assert.Equal(50, GetRect(document, "child").Y);
        Assert.Equal(35, GetRect(document, "abs").Y);
    }

    [Fact]
    public void LayoutContainment_SuppressesTheBaseline()
    {
        // An inline-block without baseline is aligned by the bottom edge of its margin box
        var document = Parse("<div><span style='display: inline-block; height: 50px'>x</span><span id=contained style='display: inline-block; height: 50px; contain: layout'>x</span></div>");
        // The bottom of the contained box is on the baseline of the line, which is the baseline of the text of the other one
        // (8px below its top)
        var normal = document.GetLayoutEngine().GetBorderBoxRect(document.QuerySelector("span")!)!.Value;
        var contained = GetRect(document, "contained");
        Assert.Equal(0, contained.Y);
        Assert.Equal(42, normal.Y);
    }

    [Fact]
    public void LayoutContainment_HasNoEffectOnInlineBoxes()
    {
        var document = Parse("<div style='height: 30px'></div><span style='contain: layout'><div id=abs style='position: absolute; top: 5px'></div></span>");
        Assert.Equal(5, GetRect(document, "abs").Y);
    }

    [Fact]
    public void PaintContainment_ClipsTheHitTesting()
    {
        var document = Parse("<div id=box style='contain: paint; width: 50px; height: 50px'><div id=child style='width: 200px; height: 200px'></div></div>");
        Assert.Same(Get(document, "child"), document.ElementFromPoint(10, 10));
        Assert.NotSame(Get(document, "child"), document.ElementFromPoint(100, 100));
    }

    [Fact]
    public void StyleContainment_ScopesTheCounters()
    {
        // https://drafts.csswg.org/css-contain-2/#containment-style: the example of the specification
        var document = Parse("""
            <style>body { counter-reset: foo } div { counter-increment: foo } div::before { content: counters(foo, ".") }</style>
            <div><div id=contained style="contain: style"><div></div><div></div></div><div><div></div><div></div></div></div>
            """);
        var dump = document.GetLayoutEngine().EnsureLayout() is null ? "" : document.GetLayoutEngine().BoxTree!.Dump();
        var texts = System.Text.RegularExpressions.Regex.Matches(dump, "\"([0-9.]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["1", "2", "2.1", "2.2", "3", "4", "5"], texts);
    }

    [Fact]
    public void StyleContainment_ScopesTheQuotes()
    {
        var document = Parse("""
            <style>q { quotes: "A" "a" "B" "b" } q::before { content: open-quote } q::after { content: none }</style>
            <q></q><div style="contain: style"><q></q></div><q></q>
            """);
        var dump = document.GetLayoutEngine().EnsureLayout() is null ? "" : document.GetLayoutEngine().BoxTree!.Dump();
        var texts = System.Text.RegularExpressions.Regex.Matches(dump, "\"([AaBb])\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["A", "B", "B"], texts);
    }

    [Fact]
    public void ContentVisibilityHidden_SkipsTheContents()
    {
        var document = Parse("<div id=box style='content-visibility: hidden; contain-intrinsic-size: 40px 20px; width: max-content'><div id=child style='width: 100px; height: 100px'></div></div><div id=after></div>");
        Assert.Equal(new PhysicalRect(0, 0, 40, 20), GetRect(document, "box"));
        Assert.Equal(20, GetRect(document, "after").Y);

        // The contents are laid out for the geometry APIs, but they are not hit tested
        Assert.Equal(new PhysicalRect(0, 0, 100, 100), GetRect(document, "child"));
        Assert.Equal(new PhysicalRect(0, 0, 40, 20), GetRect(document, "box"));
        Assert.Same(Get(document, "box"), document.ElementFromPoint(10, 10));
        Assert.Equal("", Get(document, "box").InnerText);
        Assert.Equal("", Get(document, "child").InnerText);
    }

    [Fact]
    public void ContentVisibilityHidden_HasNoEffectOnInlineBoxes()
    {
        var document = Parse("<span id=box style='content-visibility: hidden'><span id=child>x</span></span>");
        Assert.Equal(10, GetRect(document, "box").Width);
        Assert.Equal("x", Get(document, "box").InnerText);
    }

    [Fact]
    public void ContentVisibilityAuto_SkipsTheContentsFarFromTheViewport()
    {
        var document = Parse("<div id=close style='content-visibility: auto'><div style='height: 10px'></div></div><div style='height: 2000px'></div><div id=far style='content-visibility: auto'><div style='height: 10px'></div></div>");
        var events = new List<(string Id, bool Skipped)>();
        foreach (var id in (string[])["close", "far"])
        {
            Get(document, id).AddEventListener("contentvisibilityautostatechange", e => events.Add((((Element)e.Target!).Id, ((ContentVisibilityAutoStateChangeEvent)e).Skipped)));
        }

        RunFrames();
        Assert.Equal(10, GetRect(document, "close").Height);
        Assert.Equal(0, GetRect(document, "far").Height);
        Assert.Equal([("close", false), ("far", true)], events);
        Assert.True(Get(document, "close").FirstElementChild!.CheckVisibility(new CheckVisibilityOptions { ContentVisibilityAuto = true }));
        Assert.False(Get(document, "far").FirstElementChild!.CheckVisibility(new CheckVisibilityOptions { ContentVisibilityAuto = true }));

        // Scrolled into view: the proximity is determined by the next rendering update, the event is fired by the one after
        events.Clear();
        _ = document.DefaultView!.ScrollTo(0, 2000);
        RunFrames(3);
        Assert.Equal(10, GetRect(document, "far").Height);
        Assert.Equal(10, GetRect(document, "close").Height); // the last remembered size (content-visibility: auto implies contain-intrinsic-size: auto)
        Assert.Equal([("close", true), ("far", false)], events.OrderBy(e => e.Id));
    }

    [Fact]
    public void ContentVisibilityAuto_FocusedOrSelectedContentsAreRelevant()
    {
        var document = Parse("<div style='height: 2000px'></div><div id=far style='content-visibility: auto'><div id=child tabindex=0 style='height: 10px'>x</div></div>");
        RunFrames();
        Assert.Equal(0, GetRect(document, "far").Height);

        // The selection and the focus take effect immediately, as in the browsers
        var selection = document.GetSelection()!;
        selection.SelectAllChildren(Get(document, "child"));
        Assert.Equal(10, GetRect(document, "far").Height);
        selection.RemoveAllRanges();
        Assert.Equal(0, GetRect(document, "far").Height);
        Get(document, "child").Focus(new FocusOptions { PreventScroll = true });
        Assert.Equal(10, GetRect(document, "far").Height);
    }

    [Fact]
    public void LayoutContainment_OverflowIsInkOverflow()
    {
        // The overflow of a layout containment box does not extend the scrollable overflow of its ancestors
        var document = Parse("<div id=scroller style='height: 100px; overflow: auto'><div id=box style='contain: layout; height: 50px'><div style='height: 500px'></div></div></div>");
        Assert.Equal(100, Get(document, "scroller").ScrollHeight);
        Assert.Equal(500, Get(document, "box").ScrollHeight);
    }

    [Fact]
    public void SizeContainer_EstablishesAnIndependentFormattingContextWithoutLayoutContainment()
    {
        // https://drafts.csswg.org/css-conditional-5/#container-type: the margins do not collapse, but the absolutely positioned
        // child is not contained, and the overflow is scrollable overflow
        var document = Parse("<div style='height: 30px'></div><div id=box style='container-type: inline-size'><div id=child style='margin-top: 20px; height: 10px'></div><div id=abs style='position: absolute; top: 5px'></div></div>");
        Assert.Equal(50, GetRect(document, "child").Y);
        Assert.Equal(5, GetRect(document, "abs").Y);
    }

    [Fact]
    public void SizeContainment_KeepsTheBaselineOfTheContents()
    {
        // The box is sized as if it were empty, but it is aligned by the baseline of its text (8px below its top), as in the
        // browsers; a size query container gives no baseline
        var document = Parse("""
            <div><span id=a style='display: inline-block; height: 5px; contain: size'>x</span><span id=b style='display: inline-block; height: 5px; width: 5px'></span><span id=c style='display: inline-block; width: 5px; height: 5px; container-type: size'>x</span></div>
            """);
        Assert.Equal(0, GetRect(document, "a").Y);
        Assert.Equal(3, GetRect(document, "b").Y);
        Assert.Equal(3, GetRect(document, "c").Y);
    }

    [Theory]
    [InlineData("size", 0)]
    [InlineData("none", 10)]
    public void Containment_AppliesToTableCaptions(string contain, int expectedHeight)
    {
        var document = Parse($"<div id=caption style='display: table-caption; contain: {contain}'>x</div>");
        Assert.Equal(expectedHeight, GetRect(document, "caption").Height);
    }

    [Fact]
    public void PaintContainment_OfTableCaptionEstablishesAContainingBlock()
    {
        var document = Parse("<div style='height: 30px'></div><div style='display: table-caption; contain: paint'><div id=abs style='position: absolute; top: 5px'></div></div>");
        Assert.Equal(35, GetRect(document, "abs").Y);
    }

    [Theory]
    [InlineData("table-row-group")]
    [InlineData("table-row")]
    public void LayoutContainment_HasNoEffectOnTableRowsAndRowGroups(string display)
    {
        // The containing block of the absolutely positioned box is the positioned ancestor, not the row or the row group
        var document = Parse($"<div style='position: relative; padding: 25px'><div style='display: {display}; contain: layout'><div id=abs style='position: absolute; top: 0; left: 0; width: 10px; height: 10px'></div></div></div>");
        Assert.Equal(new PhysicalRect(0, 0, 10, 10), GetRect(document, "abs"));
    }

    [Theory]
    [InlineData("body { contain: layout; direction: rtl }", 0)]
    [InlineData("html { contain: paint } body { direction: rtl }", 0)]
    [InlineData("body { direction: rtl }", 700)]
    public void Containment_OfHtmlOrBodyDisablesThePropagationOfTheBody(string css, int expectedX)
    {
        // The direction of the body element is not the principal direction of the document: the block of the root element
        // stays on the left
        var document = Parse($"<style>{css} html::before {{ content: ''; display: block; width: 100px; height: 10px }}</style>");
        var before = document.GetLayoutEngine().EnsureLayout()!.Children[0].Children[0];
        Assert.Equal(expectedX, before.Offset.X);
    }

    [Fact]
    public void Containment_OfBodyKeepsItsBackground()
    {
        var document = Parse("<style>body { contain: layout; background: rgb(255, 0, 0); width: 100px; height: 100px }</style>");
        // The background of the body element is painted in its box, not on the canvas
        var dump = document.GetDisplayList()!.Dump();
        Assert.Contains("draw-rect (0, 0, 100x100) fill #ff0000  [body]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("draw-paint fill #ff0000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleContainment_ScopesTheItemsOfAReversedList()
    {
        // The items in the element with style containment are not counted by the reversed list (4 items), and they are
        // numbered by a new counter that is not reversed
        var document = Parse("""
            <style>li::before { content: "<" counter(list-item) ">" }</style>
            <ol reversed><li></li><li></li><div style="contain: style"><li></li><li></li></div><li></li><li></li></ol>
            """);
        var dump = document.GetLayoutEngine().EnsureLayout() is null ? "" : document.GetLayoutEngine().BoxTree!.Dump();
        var texts = System.Text.RegularExpressions.Regex.Matches(dump, "\"<(-?[0-9]+)>\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["4", "3", "1", "2", "2", "1"], texts);
    }

    [Fact]
    public void ContentVisibilityHidden_SkipsTheElementsOfTheTopLayerInItsContents()
    {
        var document = Parse("<div id=box style='content-visibility: hidden; width: 100px; height: 100px'><dialog id=dialog style='margin: 0; left: 0; top: 0; width: 50px; height: 50px'>x</dialog></div>");
        ((HtmlDialogElement)Get(document, "dialog")).ShowModal();
        _contexts.EventLoop.RunUntilIdle();
        Assert.False(document.GetLayoutEngine().GetFragments(Get(document, "dialog"))[0].Box.IsInTopLayer);
        Assert.DoesNotContain("draw-glyphs", document.GetDisplayList()!.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Button_WithoutBaselineIsAlignedByItsContentBox()
    {
        // The bottom edge of the content box is on the baseline of the line (8px below its top): the 3px border is below it
        var document = Parse("<div>x<button id=a style='box-sizing: content-box; border: 3px solid; padding: 0; margin: 0 0 2px; width: 4px; height: 4px; contain: layout'>b</button><button id=b style='box-sizing: content-box; border: 3px solid; padding: 0; margin: 0 0 2px; width: 4px; height: 4px'></button></div>");
        Assert.Equal(1, GetRect(document, "a").Y);
        Assert.Equal(1, GetRect(document, "b").Y);
    }

    [Fact]
    public void SizeContainment_TheFlexibleRowsOfAGridFillItsContainIntrinsicHeight()
    {
        // The block size given by contain-intrinsic-height is known before the rows are sized: the 1fr and 3fr rows share it
        var document = Parse("""
            <div style='display: grid; contain: size; contain-intrinsic-size: 100px 80px; grid-template: 1fr 3fr / 100px'>
              <div id=a></div><div id=b></div>
            </div>
            """);
        Assert.Equal(20, GetRect(document, "a").Height);
        Assert.Equal(60, GetRect(document, "b").Height);
    }
}
