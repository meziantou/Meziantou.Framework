using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

public sealed class LayoutEngineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    private Document Parse(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 } });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>body { margin: 0 }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs the rendering opportunities of the next 100 ms.</summary>
    private void RunFrames()
    {
        for (var i = 0; i < 6; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1000d / 60));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static PhysicalRect GetRect(Document document, string id) => document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById(id)!) ?? throw new InvalidOperationException("No box");

    [Fact]
    public void NoGeometryRead_NoLayout()
    {
        // The rendering steps run (an animation requests frames) and the styles are read, but nothing reads the geometry
        var document = Parse("<style>@keyframes fade { to { opacity: 0 } } #target { animation: fade 1s }</style><div id=target>text</div>");
        var target = document.GetElementById("target")!;
        Assert.Equal("block", document.DefaultView!.GetComputedStyle(target).Display);
        RunFrames();
        target.AppendChild(document.CreateElement("div"));
        RunFrames();
        Assert.Null(document.LayoutEngineOrNull);
    }

    [Fact]
    public void DeeplyNestedBoxes_DoNotOverflowTheStack()
    {
        // Each level of the layout recursion uses ~20 KB of stack: the layout of the deep boxes and the computation of their
        // overflow continue on a new stack instead of overflowing the 1 MB stack of the thread (the default stack on Windows)
        const int Depth = 300;
        double? leafWidth = null;
        var thread = new Thread(() =>
        {
            using var contexts = new BrowsingContextScope();
            var context = contexts.Create(new BrowsingContextOptions { Viewport = new ViewportOptions { Width = 800, Height = 600 } });
            var html = "<!DOCTYPE html><style>body { margin: 0 } div { container-type: inline-size; padding-left: 1px }</style><div style='width: 400px'>"
                + string.Concat(Enumerable.Repeat("<div>", Depth)) + "<span id=leaf>x</span>";
            var document = context.Navigate("https://example.com/", _ => html);
            contexts.EventLoop.RunUntilIdle();
            leafWidth = document.GetLayoutEngine().GetBorderBoxRect(document.GetElementById("leaf")!.ParentElement!)?.Width;
            _ = document.DocumentElement!.ScrollWidth;
        }, 1024 * 1024);
        thread.Start();
        thread.Join();
        Assert.Equal(400 - Depth + 1, leafWidth);
    }

    [Fact]
    public void EnsureLayout_IsCachedUntilAChange()
    {
        var document = Parse("<div id=target style='height: 10px'></div>");
        var engine = document.GetLayoutEngine();
        Assert.Equal(0, engine.LayoutCount);
        Assert.True(engine.NeedsLayout);
        Assert.NotNull(engine.EnsureLayout());
        Assert.NotNull(engine.EnsureLayout());
        Assert.Equal(1, engine.LayoutCount);
        Assert.False(engine.NeedsLayout);
        Assert.Equal(10, GetRect(document, "target").Height);

        ((HtmlElement)document.GetElementById("target")!).Style.SetProperty("height", "20px");
        Assert.True(engine.NeedsLayout);
        Assert.Equal(20, GetRect(document, "target").Height);
        Assert.Equal(2, engine.LayoutCount);

        engine.SetNeedsLayout();
        Assert.True(engine.NeedsLayout);
        engine.EnsureLayout();
        Assert.Equal(3, engine.LayoutCount);
    }

    [Fact]
    public void LayoutRequested_UpdatedByTheRenderingSteps()
    {
        var document = Parse("<style>@keyframes fade { to { opacity: 0 } } #target { animation: fade 1s }</style><div id=target style='height: 10px'></div>");
        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        Assert.True(engine.IsLayoutRequested);
        var count = engine.LayoutCount;

        ((HtmlElement)document.GetElementById("target")!).Style.SetProperty("height", "30px");
        RunFrames();

        Assert.False(engine.NeedsLayout);
        Assert.True(engine.LayoutCount > count);
        Assert.Equal(30, engine.GetFragments(document.GetElementById("target")!)[0].Size.Height);
    }

    [Fact]
    public void NoBrowsingContext_NoFragments()
    {
        var document = HtmlDocumentParser.ParseDocument("<p id=p>a</p>");
        var engine = document.GetLayoutEngine();
        Assert.Null(engine.EnsureLayout());
        Assert.Empty(engine.GetFragments(document.GetElementById("p")!));
        Assert.Null(engine.GetBorderBoxRect(document.GetElementById("p")!));
    }

    [Fact]
    public void ViewportAndRootElement()
    {
        var document = Parse("<div style='height: 30px'></div>");
        var root = document.GetLayoutEngine().EnsureLayout()!;
        Assert.Equal(ContainerBoxKind.Viewport, root.Box.Kind);
        Assert.Equal(new PhysicalSize(800, 600), root.Size);
        var html = Assert.IsType<BoxFragment>(Assert.Single(root.Children));
        Assert.Same(document.DocumentElement, html.Box.Element);
        Assert.Equal(new PhysicalRect(0, 0, 800, 30), html.Rect);
        Assert.Equal("""
            viewport 0,0 800x600
              box <html> 0,0 800x30
                box <body> 0,0 800x30
                  box <div> 0,0 800x30

            """.ReplaceLineEndings("\n"), document.GetLayoutEngine().Dump());
    }

    [Fact]
    public void Blocks_BoxModel()
    {
        var document = Parse("""
            <div id=a style='width: 100px; height: 50px; margin: 10px; padding: 5px; border: 2px solid'></div>
            <div id=b style='height: 20px; margin-left: 30px'></div>
            """);
        Assert.Equal(new PhysicalRect(10, 10, 114, 64), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(30, 84, 770, 20), GetRect(document, "b"));

        var a = document.GetLayoutEngine().GetFragments(document.GetElementById("a")!)[0];
        // The box is over-constrained: the used right margin fills the containing block (https://drafts.csswg.org/css2/#blockwidth)
        Assert.Equal(new Sides<double>(10, 676, 10, 10), a.Margin);
        Assert.Equal(new PhysicalRect(2, 2, 110, 60), a.PaddingBoxRect);
        Assert.Equal(new PhysicalRect(7, 7, 100, 50), a.ContentBoxRect);
        Assert.Equal(new PhysicalRect(-10, -10, 800, 84), a.MarginBoxRect);
        Assert.Equal(new PhysicalRect(0, 0, 800, 104), document.GetLayoutEngine().GetBorderBoxRect(document.DocumentElement!));
    }

    [Fact]
    public void Blocks_AutoMarginsAndOverConstrained()
    {
        var document = Parse("""
            <div id=center style='width: 200px; margin: 0 auto'></div>
            <div id=right style='width: 200px; margin-left: auto'></div>
            <div dir=rtl><div id=rtl style='width: 100px'></div></div>
            <div id=over style='width: 1000px; margin: 0 auto'></div>
            """);
        Assert.Equal(300, GetRect(document, "center").X);
        Assert.Equal(600, GetRect(document, "right").X);
        Assert.Equal(700, GetRect(document, "rtl").X);
        Assert.Equal(0, GetRect(document, "over").X);

        var centered = document.GetLayoutEngine().GetFragments(document.GetElementById("center")!)[0];
        Assert.Equal(300, centered.Margin.Left);
        Assert.Equal(300, centered.Margin.Right);
    }

    [Fact]
    public void Blocks_SizingProperties()
    {
        var document = Parse("""
            <div style='width: 400px; height: 200px'>
              <div id=percent style='width: 50%; height: 25%'></div>
              <div id=borderbox style='box-sizing: border-box; width: 100px; padding: 10px; border: 5px solid'></div>
              <div id=max style='max-width: 150px; min-height: 12px'></div>
              <div id=min style='width: 10px; min-width: 60px'></div>
              <div id=autoheight><div style='height: 7px'></div><div style='height: 8px; margin: 1px 0'></div></div>
            </div>
            <div id=indefinite style='height: 50%'></div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 200, 50), GetRect(document, "percent"));
        Assert.Equal(new PhysicalRect(0, 50, 100, 30), GetRect(document, "borderbox"));
        Assert.Equal(new PhysicalRect(0, 80, 150, 12), GetRect(document, "max"));
        Assert.Equal(60, GetRect(document, "min").Width);
        // The block-end margin of the last child collapses through the parent (https://drafts.csswg.org/css2/#collapsing-margins)
        Assert.Equal(16, GetRect(document, "autoheight").Height);

        // A percentage of an indefinite height behaves as auto
        Assert.Equal(0, GetRect(document, "indefinite").Height);
    }

    [Fact]
    public void Blocks_Zoom()
    {
        var document = Parse("<div id=target style='zoom: 2; width: 100px; height: 10px; margin-left: 5px'></div>");
        Assert.Equal(new PhysicalRect(10, 0, 200, 20), GetRect(document, "target"));
    }

    [Fact]
    public void DisplayNone_NoFragment()
    {
        var document = Parse("<div id=hidden style='display: none'></div><div id=contents style='display: contents'><div id=p style='height: 5px'></div></div>");
        var engine = document.GetLayoutEngine();
        Assert.Empty(engine.GetFragments(document.GetElementById("hidden")!));
        Assert.Empty(engine.GetFragments(document.GetElementById("contents")!));
        Assert.Equal(new PhysicalRect(0, 0, 800, 5), GetRect(document, "p"));
    }

    [Fact]
    public void VerticalWritingMode()
    {
        var document = Parse("""
            <div id=container style='writing-mode: vertical-rl; width: 100px; height: 200px; padding-right: 4px'>
              <div id=c style='width: 30px'></div><div id=d style='width: 20px; margin-right: 5px'></div>
            </div>
            <div style='writing-mode: vertical-lr; height: 50px'><div id=e style='width: 30px'></div><div id=f style='width: 20px'></div></div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 104, 200), GetRect(document, "container"));
        Assert.Equal(new PhysicalRect(70, 0, 30, 200), GetRect(document, "c"));
        Assert.Equal(new PhysicalRect(45, 0, 20, 200), GetRect(document, "d"));
        Assert.Equal(new PhysicalRect(0, 200, 30, 50), GetRect(document, "e"));
        Assert.Equal(new PhysicalRect(30, 200, 20, 50), GetRect(document, "f"));
    }

    [Fact]
    public void Text_AhemLines()
    {
        var document = Parse("""
            <div id=one style='font: 20px/1 Ahem'>XX  XX</div>
            <div id=lines style='font: 10px/15px Ahem'>X<br>Y<br><br></div>
            <div id=empty style='font: 20px/1 Ahem'> <span> </span> </div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 800, 20), GetRect(document, "one"));
        Assert.Equal(new PhysicalRect(0, 20, 800, 45), GetRect(document, "lines"));
        Assert.Equal(0, GetRect(document, "empty").Height);

        var engine = document.GetLayoutEngine();
        var text = (Text)document.GetElementById("one")!.FirstChild!;
        var fragment = Assert.Single(engine.GetTextFragments(text));
        Assert.Equal(0, fragment.Start);
        Assert.Equal(6, fragment.End);
        Assert.Equal(new PhysicalRect(0, 0, 100, 20), fragment.GetRectInRoot());
        Assert.Equal(16, fragment.Baseline);
        Assert.Equal(5, fragment.GlyphCount);
        var line = Assert.IsType<LineBoxFragment>(fragment.Parent);
        Assert.Equal(new PhysicalSize(800, 20), line.Size);
        Assert.Equal(16, line.Baseline);
        Assert.Equal(16, engine.GetFragments(document.GetElementById("one")!)[0].FirstBaseline);

        // The text fragments of 10px Ahem in 15px lines are centered by the half-leading (the half-leading above the text is
        // floored to whole pixels, as in Blink)
        var y = (Text)document.GetElementById("lines")!.ChildNodes[2];
        Assert.Equal(new PhysicalRect(0, 37, 10, 10), Assert.Single(engine.GetTextFragments(y)).GetRectInRoot());
    }

    [Fact]
    public void AtomicInlines_OnLines()
    {
        // The images are on the baseline: the first line is 30px above the baseline and 4px below (the descent of the strut)
        var document = Parse("""
            <div style='font: 20px/1 Ahem; width: 100px'><img id=a style='width: 60px; height: 30px'><img id=b style='width: 60px; height: 10px'></div>
            """);
        Assert.Equal(new PhysicalRect(0, 0, 60, 30), GetRect(document, "a"));
        Assert.Equal(new PhysicalRect(0, 40, 60, 10), GetRect(document, "b"));
    }

    [Fact]
    public void ReplacedElements_DefaultSize()
    {
        var document = Parse("<video id=video style='display: block'></video><iframe id=frame style='display: block; height: 20px'></iframe>");
        Assert.Equal(new PhysicalRect(0, 0, 300, 150), GetRect(document, "video"));
        Assert.Equal(new PhysicalRect(0, 150, 304, 24), GetRect(document, "frame"));
    }

    [Fact]
    public void Overflow()
    {
        var document = Parse("""
            <div id=visible style='width: 100px; height: 10px'><div style='width: 300px; height: 50px'></div></div>
            <div id=hidden style='width: 100px; height: 10px; overflow: hidden'><div style='width: 300px; height: 50px'></div></div>
            """);
        var engine = document.GetLayoutEngine();
        var visible = engine.GetFragments(document.GetElementById("visible")!)[0];
        Assert.Equal(new PhysicalRect(0, 0, 300, 50), visible.ScrollableOverflow);
        Assert.Equal(new PhysicalRect(0, 0, 300, 50), visible.InkOverflow);

        var hidden = engine.GetFragments(document.GetElementById("hidden")!)[0];
        Assert.True(hidden.ClipsOverflow);
        Assert.Equal(new PhysicalRect(0, 0, 300, 50), hidden.ScrollableOverflow);
        var body = engine.GetFragments(document.Body!)[0];
        Assert.Equal(new PhysicalRect(0, 0, 800, 50), body.ScrollableOverflow);
    }

    [Fact]
    public void SizeContainer_SizeRecorded()
    {
        var document = Parse("<div id=container style='container-type: inline-size; width: 200px; padding: 5px'><div style='height: 10px'></div></div>");
        var engine = document.GetLayoutEngine();
        Assert.True(engine.TryGetContainerSize(document.GetElementById("container")!, out var size, out var hasBlockSize));
        Assert.Equal(200, size.Width);
        Assert.False(hasBlockSize);
    }

    [Fact]
    public void Iframe_LaidOutWithTheSizeOfItsContainer()
    {
        var document = Parse("<iframe style='width: 300px; height: 200px; padding: 10px' srcdoc='<div id=target style=\"height: 50%\"></div>'></iframe>");
        _contexts.EventLoop.RunUntilIdle();
        var frameDocument = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentDocument!;
        var root = frameDocument.GetLayoutEngine().EnsureLayout()!;
        Assert.Equal(new PhysicalSize(300, 200), root.Size);
        Assert.Equal(new PhysicalRect(8, 8, 284, 0), frameDocument.GetLayoutEngine().GetBorderBoxRect(frameDocument.GetElementById("target")!));

        // The viewport of the nested document is the content box of the iframe laid out in the parent document
        Assert.Equal(1, document.LayoutEngineOrNull!.LayoutCount);
    }
}
