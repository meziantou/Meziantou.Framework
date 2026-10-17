using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The viewport-percentage lengths, the visual viewport and the viewports of the nested browsing contexts, from the layout.</summary>
public sealed class ViewportTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin, in a 800x600 viewport with 15px classic scrollbars.</summary>
    private Document Parse(string html, string rootStyle = "")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600, ScrollbarThickness = 15 } });
        var document = context.Navigate("https://example.com/", _ => $"<!DOCTYPE html><html style='{rootStyle}'><style>body {{ margin: 0 }}</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string GetComputedValue(Document document, string id, string property)
        => document.DefaultView!.GetComputedStyle(document.GetElementById(id)!).GetPropertyValue(property);

    /// <summary>Runs the rendering steps of the next frames (the virtual clock advances by one frame each time).</summary>
    private void RunFrames(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(17));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    [Theory]
    [InlineData("", "800px", "600px")]
    [InlineData("overflow: auto", "800px", "600px")]
    [InlineData("overflow: scroll", "785px", "585px")]
    [InlineData("overflow-y: scroll", "785px", "600px")]
    [InlineData("overflow: hidden; scrollbar-gutter: stable", "785px", "600px")]
    [InlineData("overflow: auto; scrollbar-gutter: stable both-edges", "770px", "600px")]
    [InlineData("overflow: auto; scrollbar-gutter: stable; writing-mode: vertical-rl", "800px", "585px")]
    [InlineData("overflow: scroll; scrollbar-width: thin", "789px", "589px")]
    [InlineData("overflow: scroll; scrollbar-width: none", "800px", "600px")]
    [InlineData("overflow: visible; scrollbar-gutter: stable", "785px", "600px")]
    public void ViewportUnits_SubtractTheUnconditionalScrollbarGutters(string rootStyle, string width, string height)
    {
        var document = Parse("<div id=target style='position: absolute; width: 100vw; height: 100vh'></div>", rootStyle);
        Assert.Equal(width, GetComputedValue(document, "target", "width"));
        Assert.Equal(height, GetComputedValue(document, "target", "height"));

        // The media queries use the whole viewport
        Assert.True(document.DefaultView!.MatchMedia("(width: 800px) and (height: 600px)").Matches);
    }

    [Fact]
    public void ViewportUnits_OverflowPropagatedFromTheBody()
    {
        var document = Parse("<style>body { overflow-y: scroll }</style><div id=target style='width: 100vw'></div>");
        Assert.Equal("785px", GetComputedValue(document, "target", "width"));

        // The overflow of the body is not propagated when the overflow of the root element is not visible
        document.DocumentElement!.SetAttribute("style", "overflow: hidden");
        Assert.Equal("800px", GetComputedValue(document, "target", "width"));
    }

    [Fact]
    public void ViewportUnits_UsedByTheRootElement()
    {
        var document = Parse("<div id=target></div>", "overflow-y: scroll; padding-left: 10vw");
        Assert.Equal("78.5px", document.DefaultView!.GetComputedStyle(document.DocumentElement!).GetPropertyValue("padding-left"));
    }

    [Fact]
    public void ViewportUnits_AutoScrollbarsAreIgnored()
    {
        var document = Parse("<div id=target style='width: 100vw; height: 2000px'></div>");
        Assert.Equal(785, document.DocumentElement!.ClientWidth);
        Assert.Equal("800px", GetComputedValue(document, "target", "width"));
    }

    [Fact]
    public void VisualViewport_SizeExcludesTheScrollbars()
    {
        var document = Parse("<div id=target style='height: 2000px'></div>");
        var window = document.DefaultView!;
        var visualViewport = window.VisualViewport!;
        Assert.Equal((785, 600), (visualViewport.Width, visualViewport.Height));
        Assert.Equal((800, 600), (window.InnerWidth, window.InnerHeight));
        Assert.Equal((0, 0, 1), (visualViewport.OffsetLeft, visualViewport.OffsetTop, visualViewport.Scale));

        _ = window.ScrollTo(0, 300);
        Assert.Equal((0, 300), (visualViewport.PageLeft, visualViewport.PageTop));

        // Reading the size computes the layout
        document.GetElementById("target")!.SetAttribute("style", "width: 2000px; height: 2000px");
        Assert.Equal((785, 585), (visualViewport.Width, visualViewport.Height));
    }

    [Fact]
    public void VisualViewport_ResizeEventWhenTheScrollbarsChange()
    {
        var document = Parse("<div id=target></div>");
        var window = document.DefaultView!;
        var events = new List<string>();
        window.AddEventListener("resize", _ => events.Add("window"));
        window.VisualViewport!.AddEventListener("resize", _ => events.Add("visualViewport"));
        RunFrames();
        Assert.Empty(events);

        // The scrollbar that appears and disappears before the next rendering update still fires the event
        var target = document.GetElementById("target")!;
        target.SetAttribute("style", "height: 2000px");
        Assert.Equal(785, window.VisualViewport!.Width);
        target.RemoveAttribute("style");
        RunFrames();
        Assert.Equal(["visualViewport"], events);

        // No event when the overflow changes without changing the scrollbars
        events.Clear();
        target.SetAttribute("style", "height: 2000px");
        RunFrames();
        Assert.Equal(["visualViewport"], events);
        events.Clear();
        target.SetAttribute("style", "height: 4000px");
        RunFrames();
        Assert.Empty(events);
    }

    [Fact]
    public void NestedViewport_ContentBoxOfTheLaidOutContainer()
    {
        var document = Parse("<div style='width: 500px'><iframe style='width: 100%; height: 300px; padding: 10px; border: 5px solid; box-sizing: border-box' srcdoc='<div id=target style=\"width: 100vw\"></div>'></iframe></div>");
        _contexts.EventLoop.RunUntilIdle();
        var frame = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var frameWindow = frame.ContentWindow!;
        Assert.Equal((470, 270), (frameWindow.InnerWidth, frameWindow.InnerHeight));
        Assert.Equal("470px", GetComputedValue(frame.ContentDocument!, "target", "width"));

        // A change of the layout of the parent document resizes the nested viewport: the resize steps fire the resize events
        var events = new List<string>();
        frameWindow.AddEventListener("resize", _ => events.Add("window"));
        frameWindow.VisualViewport!.AddEventListener("resize", _ => events.Add("visualViewport"));
        ((HtmlElement)frame.ParentElement!).Style.SetProperty("width", "400px");
        RunFrames();
        Assert.Equal(["window", "visualViewport"], events);
        Assert.Equal(370, frameWindow.InnerWidth);
        Assert.Equal("370px", GetComputedValue(frame.ContentDocument!, "target", "width"));
    }

    [Fact]
    public void NestedViewport_ZoomOfTheContainerScalesTheDevicePixelRatio()
    {
        var document = Parse("<iframe style='width: 200px; height: 100px; border: 0; zoom: 2' srcdoc='<p>Hello'></iframe>");
        _contexts.EventLoop.RunUntilIdle();
        var frameWindow = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!;
        Assert.Equal((200, 100, 2d), (frameWindow.InnerWidth, frameWindow.InnerHeight, frameWindow.DevicePixelRatio));
        Assert.Equal(1, document.DefaultView!.DevicePixelRatio);
    }

    [Fact]
    public void Viewport_SingleSegment()
    {
        var document = Parse("<div style='height: 2000px'></div>");
        var segment = Assert.Single(document.DefaultView!.Viewport.Segments!);
        Assert.Equal((0, 0, 800, 600), (segment.X, segment.Y, segment.Width, segment.Height));
    }
}
