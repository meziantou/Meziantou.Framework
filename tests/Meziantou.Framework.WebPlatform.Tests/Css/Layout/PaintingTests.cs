using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Rendering;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

/// <summary>The painting into a display list (the painting order of CSS 2 Appendix E, the stacking contexts, the clips), checked with the dumps of the display lists.</summary>
public sealed class PaintingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();

    public void Dispose() => _contexts.Dispose();

    /// <summary>Parses a no-quirks document whose body has no margin and the 10px/1 Ahem font, in a 800x600 viewport.</summary>
    private Document Parse(string html) => Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem }</style>" + html);

    private Document Load(string html, RenderingBackend? backend = null, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions { TimeProvider = _clock, Viewport = new ViewportOptions { Width = 800, Height = 600 }, DocumentLoader = new PngLoader(), RenderingBackend = backend });
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Serves a PNG of WxH pixels for the URLs ending with <c>/WxH.png</c> (only the header is valid), and a 404 for the other ones.</summary>
    private sealed class PngLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            var name = request.Url[(request.Url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            if (name.EndsWith(".png", StringComparison.Ordinal) && name[..^4].Split('x') is [var w, var h])
            {
                byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), uint.Parse(w, CultureInfo.InvariantCulture));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), uint.Parse(h, CultureInfo.InvariantCulture));
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(png) { Headers = [new("Content-Type", "image/png")] });
            }

            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] });
        }
    }

    /// <summary>A rendering backend that records the calls (the services are implemented by the backend packages).</summary>
    private sealed class FakeBackend : RenderingBackend
    {
        public List<string> Decoded { get; } = [];

        public int DecodedFrames { get; set; }

        internal override RenderingSurface? CreateSurface(int width, int height, RenderingSurfaceOptions options) => null;

        internal override RenderingImageDecoder? CreateImageDecoder(ReadOnlyMemory<byte> data, string? mimeType)
        {
            Decoded.Add(mimeType + " " + data.Length.ToString(CultureInfo.InvariantCulture));
            return new FakeDecoder(this);
        }

        private sealed class FakeDecoder(FakeBackend backend) : RenderingImageDecoder
        {
            public override int Width => 1;

            public override int Height => 1;

            public override int FrameCount => 1;

            public override int RepetitionCount => 0;

            public override double GetFrameDuration(int index) => 0;

            public override RenderingImage? DecodeFrame(int index, ExifOrientation orientation, bool convertToSrgb)
            {
                backend.DecodedFrames++;
                return new FakeImage();
            }

            protected override void Dispose(bool disposing)
            {
            }
        }

        private sealed class FakeImage : RenderingImage
        {
            public override int Width => 1;

            public override int Height => 1;

            public override void ReadPixels(Span<byte> destination) => destination.Clear();

            protected override void Dispose(bool disposing)
            {
            }
        }
    }

    /// <summary>Runs the rendering updates (the CSS images are fetched by the update of the rendering).</summary>
    private void UpdateRendering()
    {
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(20));
            _contexts.EventLoop.RunUntilIdle();
        }
    }

    private static string Dump(Document document) => document.GetDisplayList()!.Dump();

    /// <summary>Returns the ids of the elements painted by the drawing operations, in painting order (consecutive duplicates removed).</summary>
    private static List<string> GetPaintedIds(Document document)
    {
        var result = new List<string>();
        foreach (var item in document.GetDisplayList()!.Items)
        {
            if (item.Kind is DisplayItemKind.Save or DisplayItemKind.SaveLayer or DisplayItemKind.Restore or DisplayItemKind.Transform or DisplayItemKind.ClipRect or DisplayItemKind.ClipRoundedRect or DisplayItemKind.ClipPath)
                continue;

            var element = item.Source as Element ?? (item.Source as Text)?.ParentElement;
            if (element?.GetAttributeValue("id") is { Length: > 0 } id && (result.Count == 0 || result[^1] != id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    [Fact]
    public void FormControls_NativeFrame()
    {
        // The text fields, the textarea elements and the drop-down boxes with the native appearance have a frame along their
        // border box; a devolved widget (here an author border, even of the user agent value) and a drop-down box with
        // appearance: menulist-button do not
        var document = Parse("<input id=a style='width: 50px'><input id=b style='width: 50px; border-width: 0'><select id=c><option>x</select><select id=d style='appearance: menulist-button'><option>x</select>");
        var dump = Dump(document);
        Assert.Contains("draw-ring (0, 0, 50x10) radii=2,2,2,2 - (1, 1, 48x8) radii=1,1,1,1 fill #767676  [input#a]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("fill #767676  [input#b]", dump, StringComparison.Ordinal);
        Assert.Contains("fill #767676  [select#c]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("fill #767676  [select#d]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void FormControls_Switch()
    {
        // A checkbox with a switch attribute is a track with a thumb at its inline-start side, at its inline-end side when it is
        // checked (indeterminate does not apply)
        var document = Parse("<input id=a type=checkbox switch style='margin: 0'><input id=b type=checkbox switch checked style='margin: 0'>");
        ((HtmlInputElement)document.GetElementById("a")!).Indeterminate = true;
        var dump = Dump(document);
        Assert.Contains("draw-rounded-rect (2, 2, 9x9", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rounded-rect (41, 2, 9x9", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void NoBackend_ByDefault()
    {
        var document = Parse("<canvas id=c></canvas>");
        Assert.Null(document.BrowsingContext!.Options.RenderingBackend);
        Assert.Null(((HtmlCanvasElement)document.GetElementById("c")!).GetContext("2d"));
        Assert.Null(document.RenderingBackend);
    }

    [Fact]
    public void Dump_BackgroundsBordersAndText()
    {
        var document = Parse("""
            <div id=a style='background: rgb(255, 0, 0); border: 2px solid rgb(0, 0, 255); width: 50px; height: 20px'>ab</div>
            """);
        Assert.Equal("""
            draw-paint fill #ffffff
            save
              save
                save
                  draw-rect (0, 0, 54x24) fill #ff0000  [div#a]
                  draw-ring (0, 0, 54x24) - (2, 2, 50x20) fill #0000ff  [div#a]
                  draw-glyphs "ab" Ahem 10px at (2, 10) glyphs=2 fill #000000  [#text]
                restore
              restore
            restore

            """, Dump(document), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void CanvasBackground_FromBody()
    {
        var document = Parse("<style>body { background: rgb(0, 128, 0) }</style><div style='height: 10px'></div>");
        var dump = Dump(document);
        Assert.Contains("draw-paint fill #008000  [body]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("draw-rect", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void CanvasBackground_RootWins()
    {
        var document = Parse("<style>html { background: rgb(0, 0, 255) } body { background: rgb(0, 128, 0); height: 10px }</style>");
        var dump = Dump(document);
        Assert.Contains("draw-paint fill #0000ff  [html]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 0, 800x10) fill #008000  [body]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void PaintingOrder_ZIndex()
    {
        var document = Parse("""
            <style>div { width: 10px; height: 10px; background: rgb(0, 128, 0) }</style>
            <div id=pos style='position: relative; z-index: 2'></div>
            <div id=neg style='position: relative; z-index: -1'></div>
            <div id=block></div>
            <div id=auto style='position: relative'></div>
            <div id=zero style='position: relative; z-index: 0'></div>
            <div id=one style='position: relative; z-index: 1'></div>
            """);
        Assert.Equal(["neg", "block", "auto", "zero", "one", "pos"], GetPaintedIds(document));
    }

    [Fact]
    public void PaintingOrder_BlocksFloatsInlinesPositioned()
    {
        var document = Parse("""
            <style>.box { width: 10px; height: 10px; background: rgb(0, 128, 0) }</style>
            <div id=container><span id=text>x</span><div id=abs class=box style='position: absolute; top: 0'></div><div id=float class=box style='float: left'></div><div id=inline-block class=box style='display: inline-block'><div id=inner class=box style='float: left'></div></div><div id=block class=box></div></div>
            """);
        Assert.Equal(["block", "float", "text", "inline-block", "inner", "abs"], GetPaintedIds(document));
    }

    [Fact]
    public void PaintingOrder_TopLayerIsLast()
    {
        var document = Parse("""
            <style>div { width: 10px; height: 10px; background: rgb(0, 128, 0) }</style>
            <dialog id=dialog style='background: rgb(0, 0, 255)'>x</dialog>
            <div id=high style='position: relative; z-index: 100'></div>
            """);
        ((HtmlDialogElement)document.GetElementById("dialog")!).ShowModal();
        var ids = GetPaintedIds(document);
        Assert.Equal("dialog", ids[^1]);
        Assert.Contains("high", ids);
        Assert.True(ids.IndexOf("high") < ids.IndexOf("dialog"));
    }

    [Fact]
    public void StackingContext_OpacityTransformAndFilter()
    {
        var document = Parse("""
            <div id=a style='opacity: 0.5; transform: scale(2); transform-origin: 0 0; filter: grayscale(1) blur(2px); width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>
            """);
        var dump = Dump(document);
        Assert.Contains("transform matrix(2, 0, 0, 2, 0, 0)", dump, StringComparison.Ordinal);
        Assert.Contains("save-layer opacity=0.5 filters=color-matrix(", dump, StringComparison.Ordinal);
        Assert.Contains(" blur(2)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 0, 10x10) fill #ff0000  [div#a]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Clips_OverflowHiddenClipsDescendantsNotAbsoluteBoxesOfOuterContainingBlock()
    {
        var document = Parse("""
            <style>.box { width: 100px; height: 100px; background: rgb(0, 128, 0) }</style>
            <div id=clip style='overflow: hidden; width: 50px; height: 50px'>
              <div id=inside class=box></div>
              <div id=escape class=box style='position: absolute; top: 0; left: 0'></div>
            </div>
            """);
        Assert.Equal("""
            draw-paint fill #ffffff
            save
              save
                save
                  save
                    clip-rect (0, 0, 50x50)
                    draw-rect (0, 0, 100x100) fill #008000  [div#inside]
                  restore
                  draw-rect (0, 0, 100x100) fill #008000  [div#escape]
                restore
              restore
            restore

            """, Dump(document), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Clips_RoundedOverflowClip()
    {
        var document = Parse("""
            <div style='overflow: hidden; width: 50px; height: 50px; border-radius: 10px; border: 2px solid transparent'>
              <div id=inside style='height: 100px; background: rgb(0, 128, 0)'></div>
            </div>
            """);
        Assert.Contains("clip-rounded-rect (2, 2, 50x50) radii=8,8,8,8", Dump(document), StringComparison.Ordinal);
    }

    // Not in parallel with the other tests: a change of a style sheet through the CSSOM by another test (the process-wide
    // CssStyleVersion) lays the document out again, which paints it again between the reads of the display list
    [Fact(DisableParallelization = true)]
    public void Scrolling_TranslatesTheContentsAndInvalidatesThePainting()
    {
        var document = Parse("""
            <div id=scroller style='overflow: scroll; width: 50px; height: 50px'>
              <div id=inside style='height: 100px; background: rgb(0, 128, 0)'></div>
            </div>
            """);
        var engine = document.GetLayoutEngine();
        Assert.Contains("draw-rect (0, 0, 35x100) fill #008000  [div#inside]", Dump(document), StringComparison.Ordinal);
        var count = engine.PaintCount;
        Assert.Same(document.GetDisplayList(), document.GetDisplayList());
        Assert.Equal(count, engine.PaintCount);

        document.GetElementById("scroller")!.ScrollTop = 20;
        Assert.Contains("draw-rect (0, -20, 35x100) fill #008000  [div#inside]", Dump(document), StringComparison.Ordinal);
        Assert.Equal(count + 1, engine.PaintCount);
    }

    [Fact]
    public void Invalidation_StyleChangeRepaints()
    {
        var document = Parse("<div id=a style='height: 10px; background: rgb(255, 0, 0)'></div>");
        Assert.Contains("fill #ff0000", Dump(document), StringComparison.Ordinal);
        ((HtmlElement)document.GetElementById("a")!).Style.SetProperty("background-color", "rgb(0, 0, 255)");
        Assert.Contains("fill #0000ff", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void ViewportScroll_TranslatesAllButFixedBoxes()
    {
        var document = Parse("""
            <div style='height: 2000px'></div>
            <div id=fixed style='position: fixed; top: 0; width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>
            """);
        _ = document.DefaultView!.ScrollTo(0, 100);
        var dump = Dump(document);
        Assert.Contains("transform translate(0, -100)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 100, 10x10) fill #ff0000  [div#fixed]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_RightToLeftAndPositions()
    {
        var document = Parse("<div style='direction: rtl; unicode-bidi: bidi-override; width: 100px'>abc</div><div>x<span style='color: rgb(255, 0, 0)'>yz</span></div>");
        var dump = Dump(document);

        // The glyphs of the right-to-left run are painted from the left: the first one is the glyph of "c"
        Assert.Contains("draw-glyphs \"abc\" Ahem 10px at (70, 8) glyphs=3 fill #000000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"yz\" Ahem 10px at (10, 18) glyphs=2 fill #ff0000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_VerticalIsRotated()
    {
        var document = Parse("<div style='writing-mode: vertical-rl; height: 100px'>ab</div>");
        var dump = Dump(document);
        // The alphabetic baseline is 3px left of the central baseline (Ahem: ascent 8px, descent 2px)
        Assert.Contains("transform matrix(0, 1, -1, 0, 2, 0)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ab\" Ahem 10px at (0, 0) glyphs=2", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_VerticalUpright()
    {
        var document = Parse("<div style='writing-mode: vertical-rl; text-orientation: upright; height: 100px'>ab</div>");
        var dump = Dump(document);
        Assert.DoesNotContain("transform", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ab\" Ahem 10px at (0, 8) glyphs=2", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_VisibilityHidden()
    {
        var document = Parse("<div style='visibility: hidden'>a<span style='visibility: visible'>b</span></div>");
        var dump = Dump(document);
        Assert.DoesNotContain("\"a\"", dump, StringComparison.Ordinal);
        Assert.Contains("\"b\"", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Borders_StylesPerSide()
    {
        var document = Parse("<div id=a style='width: 10px; height: 10px; border-style: solid dashed; border-width: 2px; border-color: rgb(255, 0, 0) rgb(0, 0, 255)'></div>");
        var dump = Dump(document);
        Assert.Contains("clip-path M0 0L14 0L12 2L2 2Z", dump, StringComparison.Ordinal);
        Assert.Contains("draw-line (13, 0) -> (13, 14) stroke #0000ff width=2 dashes=6,2", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Outline_PaintedAtTheEndOfTheStackingContext()
    {
        var document = Parse("""
            <div id=a style='outline: 2px solid rgb(255, 0, 0); outline-offset: 1px; width: 10px; height: 10px'></div>
            <div id=b style='height: 10px; background: rgb(0, 128, 0)'></div>
            """);
        Assert.Equal(["b", "a"], GetPaintedIds(document));
        Assert.Contains("draw-ring (-3, -3, 16x16) - (-1, -1, 12x12) fill #ff0000  [div#a]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Svg_ShapesWithSolidPaint()
    {
        var document = Parse("""
            <svg width=100 height=100 style='display: block'><rect x=10 y=20 width=30 height=40 fill='rgb(255, 0, 0)' stroke='rgb(0, 0, 255)' stroke-width=2 /></svg>
            """);
        var dump = Dump(document);
        Assert.Contains("draw-path M10 20L40 20L40 60L10 60Z fill #ff0000  [rect]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-path M10 20L40 20L40 60L10 60Z stroke #0000ff width=2  [rect]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnRules()
    {
        var document = Parse("<div style='column-count: 2; column-gap: 10px; column-rule: 2px solid rgb(255, 0, 0); width: 110px'><div style='height: 10px'>a</div><div style='height: 10px'>b</div></div>");
        Assert.Contains("draw-rect (54, 0, 2x10) fill #ff0000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Text_RightToLeftGlyphsFromTheLineLeft()
    {
        // The glyphs of a right-to-left run are painted from the left end of the line, as the fragments of a line are
        var document = Parse("<div style='width: 100px'><bdo dir=rtl>ab</bdo></div>");
        var run = Assert.Single(document.GetDisplayList()!.Items.ToArray().OfType<DrawGlyphsItem>()).Run;
        Assert.Equal([0, 10], run.Positions.Select(position => position.X));
        Assert.Equal("ab", run.Text);
    }

    [Fact]
    public void ColumnRules_PaintedAsTheSideOfABorder()
    {
        // A rule and the same line painted as the only side of a border are the same operations in the same clip: a
        // dotted line drawn from its top end
        var rule = Dump(Parse("<div style='column-count: 2; column-gap: 10px; column-rule: 10px dotted rgb(255, 0, 0); width: 110px'><div style='height: 110px'></div><div style='height: 110px'></div></div>"));
        var border = Dump(Parse("<div style='margin-left: 50px; width: 0; height: 110px; border-left: 10px dotted rgb(255, 0, 0)'></div>"));
        Assert.Contains("draw-line (55, 0) -> (55, 110) stroke #ff0000 width=10 dashes=0,20 cap=round", rule, StringComparison.Ordinal);
        Assert.Contains("draw-line (55, 0) -> (55, 110) stroke #ff0000 width=10 dashes=0,20 cap=round", border, StringComparison.Ordinal);
        Assert.Equal(GetClipBefore(rule), GetClipBefore(border));
        Assert.StartsWith("clip-rect (50, 0, 10x110)", GetClipBefore(rule), StringComparison.Ordinal);

        static string GetClipBefore(string dump)
        {
            var lines = dump.Split('\n');
            var index = Array.FindIndex(lines, line => line.Contains("draw-line", StringComparison.Ordinal));
            return lines[index - 1].Trim();
        }
    }

    [Theory]
    [InlineData(10, "(0, 0, 10x20) - (3, 0, 7x20)", "(7, 0, 3x20) - (10, 0, 0x20)")] // 3 + 4 + 3
    [InlineData(5, "(0, 0, 5x20) - (2, 0, 3x20)", "(3, 0, 2x20) - (5, 0, 0x20)")] // 2 + 1 + 2
    [InlineData(4, "(0, 0, 4x20) - (1, 0, 3x20)", "(3, 0, 1x20) - (4, 0, 0x20)")] // 1 + 2 + 1
    public void Borders_DoubleLinesAreWholePixels(int width, string outerLine, string innerLine)
    {
        var dump = Dump(Parse($"<div style='width: 0; height: 20px; border-left: {width}px double rgb(255, 0, 0)'></div>"));
        Assert.Contains("draw-ring " + outerLine + " fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring " + innerLine + " fill #ff0000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Borders_GrooveHalvesAreWholePixels()
    {
        // The half at the top or at the left of a side is the larger one: 3px + 2px for the top and the bottom sides of 5px
        var top = Dump(Parse("<div style='width: 20px; height: 0; border-top: 5px groove rgb(255, 0, 0)'></div>"));
        Assert.Contains("draw-ring (0, 0, 20x5) - (0, 3, 20x2)", top, StringComparison.Ordinal);
        var bottom = Dump(Parse("<div style='width: 20px; height: 0; border-bottom: 5px groove rgb(255, 0, 0)'></div>"));
        Assert.Contains("draw-ring (0, 0, 20x5) - (0, 0, 20x3)", bottom, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_ObjectFitAndSampling()
    {
        var document = Parse("<img id=i src=20x10.png style='width: 40px; height: 40px; object-fit: contain; image-rendering: pixelated; display: block'>");
        Assert.Contains("draw-image https://example.com/20x10.png (0, 0, 20x10) -> (0, 10, 40x20) pixelated  [img#i]", Dump(document), StringComparison.Ordinal);

        // Without backend, the encoded bytes are not kept
        var image = ((HtmlImageElement)document.GetElementById("i")!).GetPaintImage()!;
        Assert.True(image.Data.IsEmpty);
    }

    [Fact]
    public void Image_DecodedByTheBackendOnce()
    {
        var backend = new FakeBackend();
        var document = Load("<!DOCTYPE html><img id=i src=20x10.png>", backend);
        Assert.Same(backend, document.RenderingBackend);
        var image = ((HtmlImageElement)document.GetElementById("i")!).GetPaintImage()!;
        Assert.False(image.Data.IsEmpty);

        // The image is opened by the decoder of the backend when it is loaded (an image it cannot read is broken), and its pixels
        // are decoded when they are drawn
        Assert.Equal(["image/png 29"], backend.Decoded);
        Assert.Equal(0, backend.DecodedFrames);
        var decoded = image.GetDecodedImage(backend);
        Assert.NotNull(decoded);
        Assert.Same(decoded, image.GetDecodedImage(backend));
        Assert.Equal(["image/png 29"], backend.Decoded);
        Assert.Equal(1, backend.DecodedFrames);
    }

    [Fact]
    public void Image_OverflowingObjectFitIsClipped()
    {
        var document = Parse("<img id=i src=20x10.png style='width: 40px; height: 40px; object-fit: cover; display: block'>");
        var dump = Dump(document);
        Assert.Contains("clip-rect (0, 0, 40x40)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-image https://example.com/20x10.png (0, 0, 20x10) -> (-20, 0, 80x40)  [img#i]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayList_WithTransform()
    {
        var document = Parse("<div style='height: 10px; background: rgb(255, 0, 0)'></div>");
        var scaled = document.GetDisplayList()!.WithTransform(DisplayMatrix.Scale(2, 2));
        Assert.StartsWith("save\n  transform matrix(2, 0, 0, 2, 0, 0)\n  draw-paint", scaled.Dump(), StringComparison.Ordinal);
    }

    [Fact]
    public void Math_FractionBar()
    {
        var document = Parse("<math><mfrac><mn>1</mn><mn>2</mn></mfrac></math>");
        Assert.Contains("draw-rect", Dump(document), StringComparison.Ordinal);
    }
    [Fact]
    public void PixelSnapping_BoxesAreSnappedToDevicePixels()
    {
        // The layout keeps the fractional positions (the CSSOM View), the painted rectangles are on whole pixels
        var document = Parse("<div style='height: 10.3px'></div><div id=a style='width: 10.4px; height: 10.6px; background: rgb(255, 0, 0)'></div>");
        Assert.Equal(10.3, document.GetElementById("a")!.GetBoundingClientRect().Y, 3);
        Assert.Contains("draw-rect (0, 10, 10x11) fill #ff0000  [div#a]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void NormalLineHeight_FontMetricsAreRoundedToWholePixels()
    {
        // Tinos 16px: ascent 14.26, descent 3.46, line gap 0.68 are rounded to 14 + 3 + 1, as in Blink
        var document = Load("<!DOCTYPE html><style>body { margin: 0; font: 16px serif }</style><div id=a>Text</div>");
        Assert.Equal(18, document.GetElementById("a")!.GetBoundingClientRect().Height);
    }

    [Fact]
    public void Gradients_LinearRadialConic()
    {
        var document = Parse("""
            <div id=a style='width: 10px; height: 10px; background: linear-gradient(to right, rgb(255, 0, 0), rgb(0, 0, 255))'></div>
            <div id=b style='width: 10px; height: 10px; background: radial-gradient(rgb(255, 0, 0), rgb(0, 0, 255))'></div>
            <div id=c style='width: 10px; height: 10px; background: conic-gradient(rgb(255, 0, 0), rgb(0, 0, 255))'></div>
            <div id=d style='width: 10px; height: 10px; background: repeating-linear-gradient(rgb(255, 0, 0) 0 2px, rgb(0, 0, 255) 2px 4px)'></div>
            """);
        var items = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().ToArray();
        var linear = Assert.IsType<LinearGradientShader>(items[0].Paint.Shader);
        Assert.Equal(0, linear.Start.X, 6);
        Assert.Equal(5, linear.Start.Y, 6);
        Assert.Equal(10, linear.End.X, 6);
        Assert.Equal(5, linear.End.Y, 6);
        Assert.IsType<RadialGradientShader>(items[1].Paint.Shader);
        Assert.IsType<ConicGradientShader>(items[2].Paint.Shader);
        var repeating = Assert.IsType<LinearGradientShader>(items[3].Paint.Shader);
        Assert.Equal(SpreadMode.Repeat, repeating.Spread);
        Assert.Equal(4, repeating.End.Y, 6);
    }

    [Fact]
    public void Gradients_InterpolationColorSpaceIsExpandedIntoSrgbStops()
    {
        var document = Parse("<div style='width: 10px; height: 10px; background: linear-gradient(in oklch, rgb(255, 0, 0), rgb(0, 0, 255))'></div>");
        var gradient = Assert.IsType<LinearGradientShader>(document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single().Paint.Shader);
        Assert.True(gradient.Stops.Length > 2);
        Assert.Equal("#ff0000", gradient.Stops[0].Color.ToString());
        Assert.Equal(1, gradient.Stops[^1].Offset);
    }

    [Fact]
    public void Gradients_MissingComponentTakesTheValueOfTheOtherStop()
    {
        // https://drafts.csswg.org/css-color-4/#interpolation-missing: the stop in the middle has the red of the stop of each
        // of its sides
        var document = Parse("<div style='width: 10px; height: 10px; background: linear-gradient(to right in srgb, color(srgb 1 0 0), color(srgb none 0 1), color(srgb 0 0 0))'></div>");
        var gradient = Assert.IsType<LinearGradientShader>(document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single().Paint.Shader);
        Assert.Equal(["#ff0000", "#ff00ff", "#0000ff", "#000000"], gradient.Stops.Select(stop => stop.Color.ToString()));
        Assert.Equal([0, 0.5, 0.5, 1], gradient.Stops.Select(stop => stop.Offset));
    }

    [Fact]
    public void Gradients_CurrentColorInColorFunctions()
    {
        var document = Parse("<div style='width: 10px; height: 10px; color: rgb(0, 128, 0); background: linear-gradient(color-mix(in srgb, currentcolor, red 0%), rgb(from currentcolor r g b))'></div>");
        var gradient = Assert.IsType<LinearGradientShader>(document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single().Paint.Shader);
        Assert.All(gradient.Stops, stop => Assert.Equal("#008000", stop.Color.ToString()));
    }

    [Fact]
    public void Gradients_RadialSizeFollowedByPosition()
    {
        var document = Parse("<div style='width: 100px; height: 100px; background: radial-gradient(circle 20px at 0px 50px, rgb(255, 0, 0), rgb(0, 0, 255))'></div>");
        var gradient = Assert.IsType<RadialGradientShader>(document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single().Paint.Shader);
        Assert.Equal(new PhysicalOffset(0, 50), gradient.EndCenter);
        Assert.Equal(20, gradient.EndRadius);
    }

    [Fact]
    public void CrossFade_OfColorsIsTheirPremultipliedMix()
    {
        // https://drafts.csswg.org/css-images-4/#cross-fade-function: the omitted percentage is what the other one leaves of 100%
        var document = Parse("<div style='width: 10px; height: 10px; background-image: cross-fade(rgb(255, 0, 0) 25%, rgb(0, 0, 255))'></div>");
        var item = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single();
        Assert.Equal(new DisplayColor(0.25f, 0, 0.75f, 1), item.Paint.Color);
    }

    [Fact]
    public void BackgroundImage_NotRepeatedIsSnappedToTheDevicePixels()
    {
        // The image is positioned in the positioning area of the layout (10.4 + 20.3 = 30.7), then its edges are rounded
        var document = Load("<!DOCTYPE html><style>body { margin: 0 }</style><div id=a style='margin-left: 10.4px; width: 50px; height: 30px; background: url(20x10.png) 20.3px 0 no-repeat'></div>", new FakeBackend());
        UpdateRendering();
        var item = document.GetDisplayList()!.Items.ToArray().OfType<DrawImageItem>().Single();
        Assert.Equal(new PhysicalRect(31, 0, 20, 10), item.DestinationRect);
    }

    [Fact]
    public void BackgroundImage_TilesWithTheImageShader()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 }</style><div id=a style='width: 50px; height: 30px; background: url(20x10.png) 5px 0 repeat-x'></div>", new FakeBackend());
        UpdateRendering();
        var item = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single(item => item.Paint.Shader is ImageShader);
        var shader = (ImageShader)item.Paint.Shader!;
        Assert.Equal(SpreadMode.Repeat, shader.SpreadX);
        Assert.Equal(SpreadMode.Decal, shader.SpreadY);
        Assert.Equal(new PhysicalRect(0, 0, 50, 10), item.Rect);
        Assert.Equal(DisplayMatrix.Translation(5, 0), shader.LocalTransform);
    }

    [Fact]
    public void BoxShadow_OuterAndInset()
    {
        var document = Parse("<div style='margin: 20px; width: 10px; height: 10px; box-shadow: 2px 3px 4px rgb(255, 0, 0), inset 1px 1px rgb(0, 0, 255)'></div>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (22, 23, 10x10) fill #ff0000 blur=2", dump, StringComparison.Ordinal);
        Assert.Contains("draw-path", dump, StringComparison.Ordinal);
        Assert.Contains("fill #0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_PropagatedFromTheDecoratingBox()
    {
        var document = Parse("<div style='text-decoration: underline line-through rgb(255, 0, 0)'>ab <span style='color: rgb(0, 128, 0)'>cd</span></div>");
        var dump = Dump(document);

        // The decorations of the div are drawn under both texts in the color of the div, the line-through after the glyphs
        Assert.Equal(4, dump.Split("fill #ff0000  [#text]").Length - 1);
        Assert.True(dump.IndexOf("draw-glyphs \"ab\"", StringComparison.Ordinal) < dump.LastIndexOf("fill #ff0000  [#text]", StringComparison.Ordinal));
    }

    [Fact]
    public void Selection_HighlightBackgroundAndColor()
    {
        var document = Parse("<style>::selection { background: rgb(0, 0, 255); color: rgb(255, 255, 0) }</style><div id=a>abcd</div>");
        var text = document.GetElementById("a")!.FirstChild!;
        var range = document.CreateRange();
        range.SetStart(text, 1);
        range.SetEnd(text, 3);
        document.GetSelection()!.AddRange(range);
        var dump = Dump(document);
        Assert.Contains("draw-rect (10, 0, 20x10) fill #0000ff  [#text]", dump, StringComparison.Ordinal);

        // The glyphs are painted once: the selected ones in the color of the selection, the other ones in the color of the text
        Assert.Contains("draw-glyphs \"a\" Ahem 10px at (0, 8) glyphs=1 fill #000000  [#text]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"bc\" Ahem 10px at (10, 8) glyphs=2 fill #ffff00  [#text]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"d\" Ahem 10px at (30, 8) glyphs=1 fill #000000  [#text]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("glyphs=4", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("clip-rect", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlights_CurrentColorIsTheColorOfTheLayerBelow()
    {
        var document = Parse("""
            <style>
              ::highlight(below) { color: rgb(255, 255, 0) }
              ::highlight(above) { background-color: currentcolor; text-decoration: underline }
            </style>
            <div id=a style='color: rgb(0, 255, 0)'>abcdef</div>
            """);
        var text = document.GetElementById("a")!.FirstChild!;
        var below = document.CreateRange();
        below.SetStart(text, 0);
        below.SetEnd(text, 2);
        document.Highlights.Set("below", new Meziantou.Framework.WebPlatform.Css.Om.Highlight(below) { Priority = 0 });
        var above = document.CreateRange();
        above.SetStart(text, 1);
        above.SetEnd(text, 4);
        document.Highlights.Set("above", new Meziantou.Framework.WebPlatform.Css.Om.Highlight(above) { Priority = 1 });
        var dump = Dump(document);

        // The background of the highlight above is yellow over the highlight below, and has the color of the text after it
        Assert.Contains("draw-rect (10, 0, 10x10) fill #ffff00  [#text]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (20, 0, 20x10) fill #00ff00  [#text]", dump, StringComparison.Ordinal);

        // The highlight above paints the glyphs it covers over its underline, in the color of the text below it
        Assert.Contains("draw-glyphs \"a\" Ahem 10px at (0, 8) glyphs=1 fill #ffff00", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"b\" Ahem 10px at (10, 8) glyphs=1 fill #ffff00", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"cd\" Ahem 10px at (20, 8) glyphs=2 fill #00ff00", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlights_TextShadow()
    {
        var document = Parse("""
            <style>
              ::highlight(below) { background-color: rgb(255, 255, 0); text-shadow: 1px 2px rgb(255, 0, 0) }
              ::highlight(above) { color: rgb(0, 0, 255); text-shadow: 3px 4px, -5px -6px 2px rgb(0, 128, 0) }
            </style>
            <div id=a style='color: rgb(0, 255, 0); text-shadow: 7px 8px rgb(128, 128, 128)'>abcdef</div>
            """);
        var text = document.GetElementById("a")!.FirstChild!;
        var below = document.CreateRange();
        below.SetStart(text, 1);
        below.SetEnd(text, 3);
        document.Highlights.Set("below", new Meziantou.Framework.WebPlatform.Css.Om.Highlight(below) { Priority = 0 });
        var above = document.CreateRange();
        above.SetStart(text, 2);
        above.SetEnd(text, 5);
        document.Highlights.Set("above", new Meziantou.Framework.WebPlatform.Css.Om.Highlight(above) { Priority = 1 });
        var dump = Dump(document).ReplaceLineEndings("\n");

        // The shadow of the originating text is painted for all the text; then the background of each highlight and over it
        // the shadows of its characters (the first shadow on top, currentcolor being the color of the text of the highlight);
        // then the text
        var originating = dump.IndexOf("draw-glyphs \"abcdef\" Ahem 10px at (0, 8) glyphs=6 fill #808080", StringComparison.Ordinal);
        var background = dump.IndexOf("draw-rect (10, 0, 20x10) fill #ffff00", StringComparison.Ordinal);
        var belowShadow = dump.IndexOf("draw-glyphs \"bc\" Ahem 10px at (10, 8) glyphs=2 fill #ff0000", StringComparison.Ordinal);
        var aboveBlurred = dump.IndexOf("draw-glyphs \"cde\" Ahem 10px at (20, 8) glyphs=3 fill #008000 blur=1", StringComparison.Ordinal);
        var aboveShadow = dump.IndexOf("draw-glyphs \"cde\" Ahem 10px at (20, 8) glyphs=3 fill #0000ff", StringComparison.Ordinal);
        var glyphs = dump.LastIndexOf("draw-glyphs \"cde\" Ahem 10px at (20, 8) glyphs=3 fill #0000ff", StringComparison.Ordinal);
        Assert.True(originating >= 0 && originating < background, dump);
        Assert.True(background < belowShadow && belowShadow < aboveBlurred && aboveBlurred < aboveShadow && aboveShadow < glyphs, dump);
        Assert.Contains("transform translate(1, 2)", dump, StringComparison.Ordinal);
        Assert.Contains("transform translate(-5, -6)", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_PairedDefaults()
    {
        // A selection without color nor background-color has the highlight color; a transparent background is kept
        var document = Parse("<style>#b::selection { background-color: transparent }</style><div id=a>ab</div><div id=b>cd</div>");
        var range = document.CreateRange();
        range.SelectNodeContents(document.Body!);
        document.GetSelection()!.AddRange(range);
        var dump = Dump(document);
        Assert.Contains("draw-rect (0, 0, 20x10) fill", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("draw-rect (0, 10, 20x10) fill", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_RecolorsTheDecorationsOfTheText()
    {
        var document = Parse("<style>::selection { color: rgb(0, 128, 0); background: transparent }</style><div id=a style='color: rgb(255, 0, 0); text-decoration: underline'>abcd</div>");
        var text = document.GetElementById("a")!.FirstChild!;
        var range = document.CreateRange();
        range.SetStart(text, 1);
        range.SetEnd(text, 3);
        document.GetSelection()!.AddRange(range);
        var dump = Dump(document);

        // The underline is painted in the color of the text, then in the color of the selection over the selected characters
        Assert.Contains("clip-rect (10,", dump, StringComparison.Ordinal);
        Assert.Contains("fill #008000  [#text]", dump, StringComparison.Ordinal);
        Assert.True(dump.Split("fill #008000").Length - 1 >= 2);
    }

    [Fact]
    public void FirstLine_Background()
    {
        var document = Parse("<style>#a::first-line { background: rgb(255, 255, 0) }</style><div id=a style='line-height: 20px'>ab<br>cd</div>");
        var dump = Dump(document);

        // The content area of the root inline box (10px) along the content of the first line
        Assert.Contains("draw-rect (0, 5, 20x10) fill #ffff00", dump, StringComparison.Ordinal);
        Assert.Equal(1, dump.Split("fill #ffff00").Length - 1);
    }

    [Fact]
    public void OutsideMarker_PaintedBeforeTheContentOfTheListItem()
    {
        var document = Parse("<ul style='margin: 0'><li id=a style='list-style-type: \"m\"'>X</li></ul>");
        var dump = Dump(document);
        Assert.True(dump.IndexOf("draw-glyphs \"m\"", StringComparison.Ordinal) < dump.IndexOf("draw-glyphs \"X\"", StringComparison.Ordinal));
    }

    [Fact]
    public void EmphasisMarks_NotDrawnForSeparatorsAndPunctuation()
    {
        // No mark for the space, the comma and the ideographic full stop; # is a punctuation character that has a mark
        var document = Parse("<div style='text-emphasis: filled circle; line-height: 3'>a b,c#\U00003002d</div>");
        var dump = Dump(document);
        Assert.Contains("glyphs=8 fill #000000", dump, StringComparison.Ordinal);
        Assert.Contains("5px at (2.845, 9) glyphs=5 fill #000000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_OverlineGrowsAwayFromTheText()
    {
        // The text is from 10 to 20: the overline is above its ascent
        var document = Parse("<div style='text-decoration: overline 3px; text-decoration-skip-ink: none; padding-top: 10px'>ab</div>");
        Assert.Contains("draw-rect (0, 7, 20x3) fill #000000  [#text]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_SkipInk()
    {
        // The gaps of the neighboring glyphs are merged (one hole in the clip, not an even-odd overlap), also for the sideways
        // glyphs of vertical text
        var document = Parse("<div style='text-decoration: underline 2px; text-underline-offset: -4px'>abc</div>");
        var dump = Dump(document);
        Assert.Contains("clip-path M-2 -3L32 -3L32 13L-2 13ZM-2 -3L32 -3L32 13L-2 13Z evenodd\n        draw-rect (0, 4, 30x2) fill #000000", dump, StringComparison.Ordinal);

        document = Parse("<div style='writing-mode: vertical-rl; text-orientation: sideways; text-decoration: underline 2px; text-underline-offset: -4px'>abc</div>");
        dump = Dump(document);
        Assert.Contains("clip-path M-3 -2L13 -2L13 32L-3 32ZM-3 -2L13 -2L13 32L-3 32Z evenodd\n        draw-rect (4, 0, 2x30) fill #000000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_OnALineScaledByTextFit()
    {
        // The decorations of the block container are drawn with the used font size of the line (the ascent of 20px: 16px),
        // also the ones propagated to the text of an inline box
        var document = Parse("<div style='width: 120px; text-fit: grow; text-decoration: overline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none'>ABC<span>DEF</span></div>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (0, -1, 60x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (60, -1, 60x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ABC\" Ahem 20px at (0, 16)", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_SkipInk_GlyphsOfTheAdjacentFragments()
    {
        // The gap around the descender of the "p" of the nested box (2px on each side) continues along the character before it
        // (U+00C9 of Ahem, which has no ink under the baseline)
        var document = Parse("<div style='text-decoration: underline 2px'>&#xC9;<span>p</span></div>");
        var dump = Dump(document);
        Assert.Contains("clip-path M-2 2.33L12 2.33L12 18.33L-2 18.33ZM8 2.33L12 2.33L12 18.33L8 18.33Z evenodd\n        draw-rect (0, 9, 10x2) fill #000000", dump, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("text-decoration-inset: 5px", "(25, 9, 30x1)")] // trimmed at both ends of "cdef" (20 to 60)
    [InlineData("text-decoration-inset: -5px", "(15, 9, 50x1)")] // extended at both ends
    [InlineData("text-decoration-inset: 10px -10px", "(30, 9, 40x1)")] // the start inset, then the end inset
    [InlineData("text-decoration-inset: 25% 50%", "(30, 9, 10x1)")] // percentages of the decorated text
    [InlineData("text-decoration-inset: 30px 10px", null)] // nothing is left
    [InlineData("text-decoration-inset: 60px -70px", "(80, 9, 50x1)")] // the whole line is after the text
    [InlineData("text-decoration-inset: auto", "(21, 9, 38x1)")] // half the thickness, 1px at least
    [InlineData("direction: rtl; text-decoration-inset: 10px 0px", "(20, 9, 30x1)")] // the start is on the right
    public void TextDecorations_Inset(string declaration, string? expected)
    {
        var document = Parse($"<div>ab<span style='text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; {declaration}'>cdef</span>gh</div>");
        var dump = Dump(document);
        if (expected is null)
        {
            Assert.DoesNotContain("fill #ff0000", dump, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"draw-rect {expected} fill #ff0000", dump, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TextDecorations_Inset_FragmentsOfTheDecoratingBox()
    {
        // The underline of the span is drawn by its text fragments: the trimmed part of the first one is not drawn, and the
        // nested inline box does not interrupt the insets
        var document = Parse("<div><span style='text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; text-decoration-inset: 15px 5px'>a<b>bc</b>d</span></div>");
        var dump = Dump(document);
        Assert.DoesNotContain("draw-rect (0, 9,", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (15, 9, 15x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (30, 9, 5x1) fill #ff0000", dump, StringComparison.Ordinal);

        // slice: the lines of the box are one run (150px): the start inset is consumed by the first line (50px) and continues on
        // the second one, the end inset applies to the last line; a negative inset only extends the first or the last line
        document = Parse("<div style='width: 50px'><span style='text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; text-decoration-inset: 40% 10px'>abcde fghij klmno</span></div>");
        dump = Dump(document);
        Assert.DoesNotContain("draw-rect (0, 9,", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (10, 19, 40x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 29, 40x1) fill #ff0000", dump, StringComparison.Ordinal);

        document = Parse("<div style='width: 50px'><span style='text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; text-decoration-inset: -5px'>abcde fghij</span></div>");
        dump = Dump(document);
        Assert.Contains("draw-rect (-5, 9, 55x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 19, 55x1) fill #ff0000", dump, StringComparison.Ordinal);

        // clone: the insets apply to each line, and the percentages refer to the decorated text of the line
        document = Parse("<div style='width: 50px'><span style='text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; text-decoration-inset: 20% -5px; box-decoration-break: clone'>abcde fgh</span></div>");
        dump = Dump(document);
        Assert.Contains("draw-rect (10, 9, 45x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (6, 19, 29x1) fill #ff0000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_Inset_BlockContainer()
    {
        // The decorations of a block container are propagated to the anonymous inline box of each block container: the insets
        // are measured from the ends of the text of its lines (not from the edges of the block), in each block separately
        var document = Parse("<div style='width: 100px; text-indent: 20px; text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; text-decoration-inset: 5px -5px'><p style='margin: 0'>abc def ghijk</p><p style='margin: 0'>lm</p></div>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (25, 9, 65x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 19, 55x1) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (25, 29, 20x1) fill #ff0000", dump, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "", -1, 10)] // The underline on the left, the overline on the right
    [InlineData("lang=zh", "", -1, 10)]
    [InlineData("lang=ja", "", 10, -1)] // auto: on the right for Japanese, Korean and Mongolian; the overline switches sides
    [InlineData("lang=ko-KR", "", 10, -1)]
    [InlineData("lang=ja", "text-underline-position: left", -1, 10)]
    [InlineData("", "text-underline-position: right", 10, -1)]
    public void TextDecorations_UnderlinePositionOfVerticalText(string attribute, string declaration, int underline, int overline)
    {
        var document = Parse($"<div {attribute} style='writing-mode: vertical-rl; text-orientation: upright; text-decoration: underline rgb(255, 0, 0) 1px; text-decoration-skip-ink: none; {declaration}'><span style='text-decoration: overline rgb(0, 0, 255) 1px'>ab</span></div>");
        var dump = Dump(document);
        Assert.Contains($"draw-rect ({underline.ToString(CultureInfo.InvariantCulture)}, 0, 1x20) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains($"draw-rect ({overline.ToString(CultureInfo.InvariantCulture)}, 0, 1x20) fill #0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_UnderlineOffsetFromThePositionOfTheFont()
    {
        // The underline position of Ahem is 1px under the baseline (8, then 18): the offset of from-font is added to it
        var document = Parse("<div style='text-decoration: underline 1px; text-underline-position: from-font; text-underline-offset: 3px; text-decoration-skip-ink: none'>ab</div><div style='text-decoration: underline 1px; text-underline-position: from-font; text-decoration-skip-ink: none'>ab</div>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (0, 12, 20x1) fill #000000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 19, 20x1) fill #000000", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDecorations_PropagatedToTheBlockContainerOfTheLine()
    {
        // The underline of the block is drawn with the font of the block container of the text (10px: 1px thick, 1px under the baseline)
        var document = Parse("<div style='text-decoration: underline; font-size: 40px; text-decoration-skip-ink: none'><p style='font-size: 10px; margin: 0'>ab</p></div>");
        Assert.Contains("draw-rect (0, 9, 20x1) fill #000000  [#text]", Dump(document), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "draw-rect (20, 9, 30x1)")] // start end (the initial value): not under the spacers at the ends of the line
    [InlineData("text-decoration-skip-spaces: none", "draw-rect (0, 9, 70x1)")]
    [InlineData("text-decoration-skip-spaces: start", "draw-rect (20, 9, 50x1)")]
    [InlineData("text-decoration-skip-spaces: end", "draw-rect (0, 9, 50x1)")]
    [InlineData("direction: rtl; text-decoration-skip-spaces: start", "draw-rect (730, 9, 50x1)")]
    public void TextDecorations_SkipSpacesAtTheEndsOfTheLine(string declaration, string expected)
    {
        // Two preserved spaces, "a b" and a space and a no-break space; the spans do not change the ends of the line
        var document = Parse($"<div style='white-space: pre; text-decoration: underline; text-decoration-skip-ink: none; {declaration}'>  <span>a</span> <span>b</span> &nbsp;</div>");
        var dump = Dump(document);
        Assert.Contains(expected, MergeRects(dump), StringComparison.Ordinal);

        // The rectangles of the consecutive fragments of the line, as one rectangle
        static string MergeRects(string dump)
        {
            double? start = null;
            var end = 0d;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(dump, @"draw-rect \((?<x>[\d.]+), 9, (?<w>[\d.]+)x1\)", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)))
            {
                var x = double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture);
                start = Math.Min(start ?? x, x);
                end = Math.Max(end, x + double.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture));
            }

            return start is { } value ? string.Create(CultureInfo.InvariantCulture, $"draw-rect ({value}, 9, {end - value}x1)") : "";
        }
    }

    [Fact]
    public void TextDecorations_SkipAllSpaces()
    {
        // all: the spaces between the words are not decorated either (holes in the clip of the line)
        var document = Parse("<div style='white-space: pre; text-decoration: line-through; text-decoration-skip-spaces: all'> ab cd </div>");
        var dump = Dump(document);
        Assert.Contains("L30 ", dump, StringComparison.Ordinal);
        Assert.Contains("L40 ", dump, StringComparison.Ordinal);
        Assert.Contains("evenodd\n        draw-rect (10, ", dump, StringComparison.Ordinal);
        Assert.Contains("50x1) fill #000000", dump, StringComparison.Ordinal);

        // A line of spacers has no decoration
        document = Parse("<div style='text-decoration: underline'>&nbsp;&nbsp;</div>");
        Assert.DoesNotContain("draw-rect", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Highlights_RangesNameAndStroke()
    {
        var document = Parse("""
            <style>
              ::highlight(\*) { background-color: rgb(0, 0, 255) }
              ::highlight(wavy) { text-decoration: wavy underline }
              ::selection { background-color: rgb(255, 0, 0) }
            </style>
            <div id=a>abcdef</div>
            <div id=b style='user-select: none'><span>abcdef</span></div>
            <div id=c style='-webkit-text-stroke: 1px rgb(0, 128, 0)'>abcdef</div>
            """);
        var text = document.GetElementById("a")!.FirstChild!;
        var first = document.CreateRange();
        first.SetStart(text, 1);
        first.SetEnd(text, 3);
        var second = document.CreateRange();
        second.SetStart(text, 2);
        second.SetEnd(text, 4);
        var invalid = new StaticRange(new StaticRangeInit { StartContainer = text, StartOffset = 5, EndContainer = text, EndOffset = 100 });
        document.Highlights.Set("*", new Meziantou.Framework.WebPlatform.Css.Om.Highlight(first, second, invalid));
        var stroked = document.CreateRange();
        stroked.SetStart(document.GetElementById("c")!.FirstChild!, 2);
        stroked.SetEnd(document.GetElementById("c")!.FirstChild!, 4);
        document.Highlights.Set("wavy", new Meziantou.Framework.WebPlatform.Css.Om.Highlight(stroked));
        var selection = document.CreateRange();
        selection.SelectNodeContents(document.GetElementById("b")!);
        document.GetSelection()!.AddRange(selection);
        var dump = Dump(document);

        // The overlapping ranges of the highlight named "*" are painted once, its invalid static range is ignored
        Assert.Contains("draw-rect (10, 0, 30x10) fill #0000ff  [#text]", dump, StringComparison.Ordinal);
        Assert.Equal(1, dump.Split("fill #0000ff").Length - 1);

        // The selection does not highlight the text that cannot be selected
        Assert.DoesNotContain("fill #ff0000", dump, StringComparison.Ordinal);

        // The wavy line starts at the first highlighted character; the highlight paints its glyphs over the line, without the
        // stroke of the text
        Assert.Contains("draw-path M20 30.83Q", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ab\" Ahem 10px at (0, 28) glyphs=2 stroke #008000 width=1", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ef\" Ahem 10px at (40, 28) glyphs=2 stroke #008000 width=1", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("draw-glyphs \"cd\" Ahem 10px at (20, 28) glyphs=2 stroke", dump, StringComparison.Ordinal);
        Assert.True(dump.IndexOf("draw-path M20 30.83Q", StringComparison.Ordinal) < dump.IndexOf("draw-glyphs \"cd\" Ahem 10px at (20, 28) glyphs=2 fill #000000", StringComparison.Ordinal));
    }

    [Fact]
    public void TargetText_PaintedWithTheMarkColors()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } #b::target-text { background-color: rgb(0, 0, 255) }</style><div id=a>ab cd</div><div id=b>ef gh</div>", url: "https://example.com/#:~:text=cd&text=gh");
        Assert.Equal("https://example.com/#", document.Url);
        Assert.Equal(2, document.TargetTextRanges!.Count);
        var dump = Dump(document);
        Assert.Contains("draw-rect (30, 0, 20x10) fill #ffff00", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (30, 10, 20x10) fill #0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlights_GlyphsPaintedOnceInTheColorOfTheTopmostHighlight()
    {
        var document = Parse("""
            <style>
              ::highlight(low) { color: rgb(255, 0, 0) }
              ::highlight(high) { color: rgb(0, 0, 255) }
              ::highlight(underlined) { text-decoration: underline }
            </style>
            <div id=a>abcdef</div>
            """);
        var text = document.GetElementById("a")!.FirstChild!;
        AddHighlight("low", 1, 4, priority: 0);
        AddHighlight("high", 3, 5, priority: 1);
        AddHighlight("underlined", 0, 6, priority: 2);
        var dump = Dump(document);

        // A highlight without color keeps the colors of the highlights below it
        Assert.Contains("draw-glyphs \"a\" Ahem 10px at (0, 8) glyphs=1 fill #000000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"bc\" Ahem 10px at (10, 8) glyphs=2 fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"de\" Ahem 10px at (30, 8) glyphs=2 fill #0000ff", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"f\" Ahem 10px at (50, 8) glyphs=1 fill #000000", dump, StringComparison.Ordinal);
        Assert.Equal(4, dump.Split("draw-glyphs").Length - 1);

        void AddHighlight(string name, int start, int end, int priority)
        {
            var range = document.CreateRange();
            range.SetStart(text, (uint)start);
            range.SetEnd(text, (uint)end);
            var highlight = new Meziantou.Framework.WebPlatform.Css.Om.Highlight(range) { Priority = priority };
            document.Highlights.Set(name, highlight);
        }
    }

    [Fact]
    public void Borders_JoinsOfTheSides()
    {
        var document = Parse("<div id=a style='width: 20px; height: 20px; border: 5px solid; border-color: rgb(255, 0, 0) rgb(255, 0, 0) rgb(0, 0, 255) rgb(0, 0, 255, 0.5)'></div>");
        var dump = Dump(document);

        // Top: the left side is translucent (an antialiased diagonal), the right side has the same color (a diagonal that is
        // not antialiased: each pixel is painted by one side)
        Assert.Contains("clip-path M0 0L30 0L30 5L5 5Z\n        clip-path M0 0L30 0L25 5L0 5Z aliased\n        draw-ring (0, 0, 30x30) - (5, 5, 20x20) fill #ff0000  [div#a]", dump, StringComparison.Ordinal);

        // Right: the bottom side is opaque and painted after it, over the whole corner
        Assert.Contains("clip-path M30 0L30 30L25 30L25 5Z aliased\n        draw-ring (0, 0, 30x30) - (5, 5, 20x20) fill #ff0000  [div#a]", dump, StringComparison.Ordinal);

        // Bottom: antialiased diagonals over the right side and next to the translucent left side
        Assert.Contains("clip-path M30 30L0 30L5 25L25 25Z\n        draw-ring (0, 0, 30x30) - (5, 5, 20x20) fill #0000ff  [div#a]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsedBorders_PaintedByTheTable()
    {
        var document = Parse("<table style='border-collapse: collapse'><tr><td id=a style='border: 2px solid rgb(255, 0, 0); padding: 0; width: 10px; height: 10px'></td><td style='border: 4px solid rgb(0, 0, 255); padding: 0; width: 10px; height: 10px'></td></tr></table>");
        var dump = Dump(document);

        // The wider border wins the edge between the cells
        Assert.Contains("fill #0000ff  [td]", dump, StringComparison.Ordinal);
        Assert.Contains("fill #ff0000  [td#a]", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("draw-ring", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void TableParts_BackgroundsArePaintedInTheCells()
    {
        var document = Parse("<table style='border-spacing: 2px'><tr id=r style='background: rgb(255, 0, 0)'><td style='padding: 0; width: 10px; height: 10px'></td><td style='padding: 0; width: 10px; height: 10px'></td></tr></table>");
        var dump = Dump(document);
        Assert.Equal(2, dump.Split("fill #ff0000  [tr#r]").Length - 1);
    }

    [Fact]
    public void TableParts_PositionedRowGroupPaintsItsBackgroundInItsLayer()
    {
        // The background of the positioned row group is painted with it, above the positioned box that precedes it in tree order
        var document = Parse("""
            <div style='position: relative'>
              <div id=below style='position: absolute; width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>
              <table style='border-spacing: 0'><tbody id=g style='position: relative; background: rgb(0, 128, 0)'><tr style='background: rgb(0, 0, 255)'><td style='padding: 0; width: 10px; height: 10px'></td></tr></tbody></table>
            </div>
            """);
        var dump = Dump(document);
        var below = dump.IndexOf("fill #ff0000", StringComparison.Ordinal);
        var group = dump.IndexOf("fill #008000", StringComparison.Ordinal);
        var row = dump.IndexOf("fill #0000ff", StringComparison.Ordinal);
        Assert.True(below >= 0 && below < group && group < row, dump);
        Assert.Equal(1, dump.Split("fill #008000").Length - 1);
    }

    [Fact]
    public void FormControls_CheckboxAndTextField()
    {
        var document = Parse("<input id=c type=checkbox checked><input id=t value=ab>");
        var dump = Dump(document);
        Assert.Contains("[input#c]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ab\"", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void BorderImage_NineRegions()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 }</style><div style='width: 20px; height: 20px; border: 5px solid; border-image: url(20x10.png) 2 round'></div>", new FakeBackend());
        UpdateRendering();
        var tiles = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Where(item => item.Paint.Shader is TiledShader).ToArray();
        Assert.Equal(8, tiles.Length);
    }

    [Fact]
    public void BackgroundClipText()
    {
        var document = Parse("<div style='background: rgb(255, 0, 0); background-clip: text; color: transparent'>ab</div>");
        var dump = Dump(document);
        Assert.Contains("save-layer", dump, StringComparison.Ordinal);
        Assert.Contains("blend=destinationin", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxShadow_SpreadRadii_ACircleStaysACircle()
    {
        // https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius: the corners cover the box (coverage 1), so the
        // radii grow by the spread distance
        var document = Parse("<div style='margin: 20px; width: 2px; height: 2px; border-radius: 50%; box-shadow: 0 0 0 10px rgb(255, 0, 0)'></div>");
        Assert.Contains("draw-rounded-rect (10, 10, 22x22) radii=11,11,11,11 fill #ff0000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BoxShadow_SpreadRadii_ASmallRadiusGrowsLessThanTheSpread()
    {
        // coverage = 2 * 10 / 100, radius = 10 + 50 * (1 - (1 - 10 / 50)^3 * (1 - coverage^3))
        var document = Parse("<div style='margin: 60px; width: 100px; height: 100px; border-radius: 10px; box-shadow: 0 0 0 50px rgb(255, 0, 0)'></div>");
        Assert.Contains("draw-rounded-rect (10, 10, 200x200) radii=34.605,34.605,34.605,34.605 fill #ff0000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BoxShadow_ClippedInsideOfAnOpaqueBackground()
    {
        // The shadow is clipped out of the border box; one pixel inside of it when the opaque background hides the shadow
        var opaque = Dump(Parse("<div style='margin: 20px; width: 20px; height: 20px; background: rgb(0, 128, 0); box-shadow: 0 0 0 5px rgb(255, 0, 0)'></div>"));
        Assert.Contains("ZM21 21L39 21L39 39L21 39Z evenodd", opaque, StringComparison.Ordinal);
        var translucent = Dump(Parse("<div style='margin: 20px; width: 20px; height: 20px; background: rgb(0 128 0 / 0.5); box-shadow: 0 0 0 5px rgb(255, 0, 0)'></div>"));
        Assert.Contains("ZM20 20L40 20L40 40L20 40Z evenodd", translucent, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxShadow_InlineBoxSlicedAtTheLineBreaks()
    {
        var document = Parse("<div style='width: 30px'><span style='box-shadow: 2px 2px rgb(255, 0, 0)'>ab cd</span></div>");
        var dump = Dump(document);

        // The first fragment (20px wide) has no shadow after its right edge: its box continues to the right and is clipped there
        Assert.Contains("clip-rect (-10000000, -10000000, 10000020x20000010)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (2, 2, 25x10) fill #ff0000", dump, StringComparison.Ordinal);

        // The last fragment has no shadow before its left edge
        Assert.Contains("clip-rect (0, -9999990, 10000020x20000010)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (-3, 12, 25x10) fill #ff0000", dump, StringComparison.Ordinal);
        Assert.Equal(2, dump.Split("clip-rect").Length - 1);
    }

    [Fact]
    public void BoxShadow_TableRow()
    {
        var document = Parse("<div style='display: table'><div style='display: table-row; background: rgb(0, 128, 0); box-shadow: 0 0 0 5px rgb(255, 0, 0)'><div style='display: table-cell; width: 20px; height: 20px'></div></div></div>");
        var dump = Dump(document);

        // The shadow of the row is painted below its background (which is painted in its cells), out of its border box
        var shadow = dump.IndexOf("draw-rect (-5, -5, 30x30) fill #ff0000", StringComparison.Ordinal);
        Assert.True(shadow >= 0);
        Assert.True(shadow < dump.IndexOf("fill #008000", StringComparison.Ordinal));
        Assert.Contains("ZM0 0L20 0L20 20L0 20Z evenodd", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundAttachmentLocal_PaintedInThePaddingBox()
    {
        // The scrollable overflow area does not include the borders: border-box is treated as padding-box
        var document = Parse("<div style='width: 100px; height: 100px; padding: 10px; border: 5px dashed; overflow: hidden; background: rgb(0, 128, 0) local border-box'><div style='height: 500px'></div></div>");
        Assert.Contains("draw-rect (5, 5, 120x120) fill #008000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundAttachmentLocal_ContentBoxOfTheScrollableOverflowArea()
    {
        var document = Parse("<div style='width: 100px; height: 100px; padding: 10px; border: 5px solid; overflow: hidden; background: rgb(0, 128, 0) local content-box'><div style='height: 500px'></div></div>");
        var dump = Dump(document);

        // The padding box, clipped to the content box of the scrolled contents (500px high)
        var clip = dump.IndexOf("clip-rect (15, 15, 100x500)", StringComparison.Ordinal);
        Assert.True(clip >= 0);
        Assert.True(clip < dump.IndexOf("draw-rect (5, 5, 120x120) fill #008000", StringComparison.Ordinal));
    }

    [Fact]
    public void BackgroundClipText_MaskIncludesTheFloatsTheAtomicInlinesAndTheDecorations()
    {
        var document = Parse("<div style='background: rgb(255, 0, 0); background-clip: text; color: transparent; text-decoration: underline'>ab<span style='float: left'>cd</span><span style='display: inline-block'>ef</span><span style='position: absolute'>gh</span></div>");
        var dump = Dump(document);
        var mask = dump[dump.IndexOf("blend=destinationin", StringComparison.Ordinal)..dump.IndexOf("fill #00000000", StringComparison.Ordinal)];
        Assert.Contains("draw-glyphs \"ab\"", mask, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"cd\"", mask, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ef\"", mask, StringComparison.Ordinal);
        Assert.Contains("draw-rect (20, 9, 20x1) fill #000000", mask, StringComparison.Ordinal);

        // The absolutely positioned descendants are not in the mask
        Assert.DoesNotContain("draw-glyphs \"gh\"", mask, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundClipText_LayersBlendBeforeTheClip()
    {
        // The color and the layers clipped to the text are one group: the layer blends with the color inside of it
        var document = Parse("<div style='background: linear-gradient(rgb(255, 0, 0), rgb(255, 0, 0)), rgb(255, 255, 0); background-blend-mode: difference; background-clip: text; color: transparent'>ab</div>");
        var dump = Dump(document);
        var color = dump.IndexOf("fill #ffff00", StringComparison.Ordinal);
        var layer = dump.IndexOf("fill linear-gradient blend=difference", StringComparison.Ordinal);
        var mask = dump.IndexOf("blend=destinationin", StringComparison.Ordinal);
        Assert.True(color >= 0 && color < layer && layer < mask);
        Assert.Equal(1, dump.Split("blend=destinationin").Length - 1);
    }

    [Fact]
    public void BackgroundClipText_LayerAboveAnotherPaintingAreaIsBlendedAsAGroup()
    {
        var document = Parse("<div style='background: linear-gradient(rgb(255, 0, 0), rgb(255, 0, 0)), linear-gradient(rgb(0, 0, 255), rgb(0, 0, 255)); background-blend-mode: difference, normal; background-clip: text, border-box; color: transparent'>ab</div>");
        Assert.Contains("save-layer blend=difference", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundClipBorderArea_Color()
    {
        var document = Parse("<div style='width: 20px; height: 20px; border: 5px solid transparent; background: rgb(0, 128, 0) border-area'></div>");
        Assert.Contains("draw-path M0 0L30 0L30 30L0 30ZM5 5L25 5L25 25L5 25Z evenodd fill #008000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundClipBorderAreaText_MaskIsTheUnionOfTheBordersAndTheText()
    {
        var document = Parse("<div style='width: 20px; height: 20px; border: 5px solid transparent; background: rgb(0, 128, 0); background-clip: border-area text; color: transparent'>ab</div>");
        var dump = Dump(document);
        var mask = dump[dump.IndexOf("blend=destinationin", StringComparison.Ordinal)..];
        Assert.Contains("draw-path M0 0L30 0L30 30L0 30ZM5 5L25 5L25 25L5 25Z evenodd fill #000000", mask, StringComparison.Ordinal);
        Assert.Contains("draw-glyphs \"ab\" Ahem 10px at (5, 13) glyphs=2 fill #000000", mask, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("contents")]
    public void CanvasBackground_NotPropagatedFromABodyWithoutBox(string display)
    {
        var document = Parse("<style>body { background: rgb(255, 0, 0); display: " + display + " }</style>x");
        Assert.DoesNotContain("#ff0000", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void FirstLine_BackgroundImage()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0; font: 10px/1 Ahem } #a::first-line { background: url(20x10.png) no-repeat }</style><div id=a style='line-height: 20px'>ab<br>cd</div>", new FakeBackend());
        UpdateRendering();

        // Positioned in the content area of the root inline box of the first line
        Assert.Contains("draw-image https://example.com/20x10.png (0, 0, 20x10) -> (0, 5, 20x10)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void FirstLine_BackgroundOfAnInlineBoxUsesTheFirstLineStyle()
    {
        var document = Parse("<style>#a { color: rgb(255, 0, 0) } #a::first-line { color: rgb(0, 128, 0) } span { background: currentcolor }</style><div id=a>ab <span>cd</span><br><span>ef</span></div>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (30, 0, 20x10) fill #008000  [span]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 10, 20x10) fill #ff0000  [span]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstLine_BorderAndOutlineColorsOfAnInlineBoxUseTheFirstLineStyle()
    {
        var document = Parse("<style>#a { color: rgb(255, 0, 0) } #a::first-line { color: rgb(0, 128, 0) } span { border-left: 4px solid; outline: 2px solid }</style><div id=a><span>cd</span><br><span>ef</span></div>");
        var dump = Dump(document);
        Assert.Contains("draw-ring (0, 0, 24x10) - (4, 0, 20x10) fill #008000  [span]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring (0, 10, 24x10) - (4, 10, 20x10) fill #ff0000  [span]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring (-2, -2, 28x14) - (0, 0, 24x10) fill #008000  [span]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-ring (-2, 8, 28x14) - (0, 10, 24x10) fill #ff0000  [span]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstLine_PropertiesThatAreNotInheritedInheritFromTheElement()
    {
        // "the fragment of a child that occurs on the first line inherits any standard inherited properties from the ::first-line
        // pseudo-element. For all other properties, inheritance is from the non-pseudo parent"
        var document = Parse("<style>#a { background: rgb(0, 128, 0) } #a::first-line { background-color: rgb(255, 0, 0) } span { background-color: inherit }</style><div id=a><span>ab</span></div>");
        Assert.Contains("draw-rect (0, 0, 20x10) fill #008000  [span]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundImage_RowOfTilesSnappedInTheAxisThatDoesNotRepeat()
    {
        var document = Load("<!DOCTYPE html><style>body { margin: 0 }</style><div id=a style='width: 50px; height: 30px; background: url(1x10.png) 0.25px 0 repeat-y'></div>", new FakeBackend());
        UpdateRendering();
        var item = document.GetDisplayList()!.Items.ToArray().OfType<DrawRectItem>().Single(item => item.Paint.Shader is ImageShader);
        Assert.Equal(new PhysicalRect(0, 0, 1, 30), item.Rect);
        Assert.Equal(DisplayMatrix.Translation(0, 0), ((ImageShader)item.Paint.Shader!).LocalTransform);
    }

    [Fact]
    public void Background_StopsInTheMiddleOfOpaqueRoundedBorders()
    {
        var solid = Dump(Parse("<div style='width: 20px; height: 20px; border: 4px solid rgb(0, 0, 255); border-radius: 10px; background: rgb(0, 128, 0)'></div>"));
        Assert.Contains("draw-rounded-rect (2, 2, 24x24) radii=8,8,8,8 fill #008000", solid, StringComparison.Ordinal);

        // The background shows between the dashes
        var dashed = Dump(Parse("<div style='width: 20px; height: 20px; border: 4px dashed rgb(0, 0, 255); border-radius: 10px; background: rgb(0, 128, 0)'></div>"));
        Assert.Contains("draw-rounded-rect (0, 0, 28x28) radii=10,10,10,10 fill #008000", dashed, StringComparison.Ordinal);
    }

    [Fact]
    public void BorderImage_RegionLinesOnDevicePixels()
    {
        // The widths (10px 10px 20px 15px) are reduced by 2/3 to fit the 20px area: 6.67px, 6.67px, 13.33px, 10px
        var document = Load("<!DOCTYPE html><style>body { margin: 0 }</style><div style='width: 0; height: 0; border: 10px solid; border-image: url(30x30.png) 10; border-image-width: 10px 10px 20px 15px'></div>", new FakeBackend());
        UpdateRendering();
        var dump = Dump(document);
        Assert.Contains("draw-rect (0, 0, 10x7)", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (13, 7, 7x13)", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Opacity_FragmentsOfAnInlineBoxAreOneGroup()
    {
        var document = Parse("<div style='width: 20px'><span style='opacity: 0.5; background: rgb(0, 0, 255)'>ab cd</span></div>");
        var dump = Dump(document);
        Assert.Equal(1, dump.Split("save-layer").Length - 1);
        Assert.Contains("draw-rect (0, 0, 20x10) fill #0000ff", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 10, 20x10) fill #0000ff", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void Opacity_InlineBoxAppliesToItsFloats()
    {
        var document = Parse("<div><span style='opacity: 0.5'><div id=f style='float: left; width: 10px; height: 10px; background: rgb(255, 0, 0)'></div>ab</span></div>");
        var dump = Dump(document);
        var layer = dump.IndexOf("save-layer opacity=0.5", StringComparison.Ordinal);
        Assert.True(layer >= 0);
        Assert.True(layer < dump.IndexOf("fill #ff0000  [div#f]", StringComparison.Ordinal));
        Assert.Equal(1, dump.Split("[div#f]").Length - 1);
    }

    [Fact]
    public void TableParts_BackgroundIsPaintedInTheWholeSpanningCell()
    {
        // The cell spans two rows: the background of its row is painted in its whole border box
        var document = Parse("<table style='border-spacing: 0'><tr id=r style='background: rgb(255, 0, 0)'><td rowspan=2 style='padding: 0; width: 10px; height: 30px'></td><td style='padding: 0; width: 10px; height: 10px'></td></tr><tr><td style='padding: 0; width: 10px; height: 10px'></td></tr></table>");
        var dump = Dump(document);
        Assert.Contains("(0, 0, 10x30) fill #ff0000  [tr#r]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsedBorders_PaintedAfterTheBlocksOfTheCells()
    {
        // The collapsed borders cover the border of the block of the cell, and the collapsed borders of the nested table
        var document = Parse("<table style='border-collapse: collapse'><tr><td style='border: 5px solid rgb(0, 128, 0); padding: 0'><div style='border: 5px solid rgb(255, 0, 0); margin: -5px'></div><table style='border-collapse: collapse'><tr><td style='border: 5px solid rgb(0, 0, 255); padding: 0'></td></tr></table></td></tr></table>");
        var dump = Dump(document);
        var block = dump.LastIndexOf("#ff0000", StringComparison.Ordinal);
        var nested = dump.LastIndexOf("#0000ff", StringComparison.Ordinal);
        var borders = dump.IndexOf("#008000", StringComparison.Ordinal);
        Assert.True(block >= 0 && nested >= 0 && block < borders && nested < borders);
    }

    [Fact]
    public void CollapsedBorders_OfACellThatSpansColumnsWithoutOtherCell()
    {
        // The second column has no cell of its own: the top and bottom borders of the spanning cell are painted
        var document = Parse("<table style='border-collapse: collapse'><col style='width: 50px'><col style='width: 50px'><tr><td colspan=2 style='border: 10px solid rgb(0, 128, 0); padding: 0; height: 50px'></td></tr></table>");
        var dump = Dump(document);
        Assert.Contains("draw-rect (5, 0, 105x10) fill #008000  [td]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (5, 60, 105x10) fill #008000  [td]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapsedBorders_TheLinesAroundACollapsedTrackAreOneLine()
    {
        // The borders on both sides of the collapsed row (column) are one border, and the cells of the collapsed track do not
        // paint borders: the table is a 20px square
        var document = Parse("""
            <table style='border-collapse: collapse'>
              <col style='border-right: 20px solid rgb(0, 128, 0)'><col style='visibility: collapse'><col style='border-left: 20px solid rgb(0, 128, 0)'>
              <tr style='border-bottom: 20px solid rgb(0, 128, 0)'><td style='padding: 0'></td><td style='padding: 0'></td><td style='padding: 0'></td></tr>
              <tr style='visibility: collapse'><td style='padding: 0; border: 30px solid rgb(255, 0, 0)'></td><td style='padding: 0'></td><td style='padding: 0'></td></tr>
              <tr style='border-top: 20px solid rgb(0, 128, 0)'><td style='padding: 0'></td><td style='padding: 0'></td><td style='padding: 0'></td></tr>
            </table>
            """);
        var dump = Dump(document);
        Assert.DoesNotContain("#ff0000", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 0, 20x20) fill #008000  [col]", dump, StringComparison.Ordinal);
        Assert.Contains("draw-rect (0, 0, 20x20) fill #008000  [tr]", dump, StringComparison.Ordinal);
        Assert.Equal(dump.Split("draw-rect").Length, dump.Split("draw-rect (0, 0, 20x20)").Length);
    }

    [Fact]
    public void Outline_NegativeOffsetDoesNotShrinkTheOutlineBelowTwiceItsWidth()
    {
        // https://drafts.csswg.org/css-ui-4/#outline-offset: the 2px outline of the 10x10 box is a 4x4 square around its center
        var document = Parse("<div id=a style='outline: 2px solid rgb(255, 0, 0); outline-offset: -20px; width: 10px; height: 10px'></div>");
        Assert.Contains("(3, 3, 4x4)", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Outline_InsetIsInsideOfTheBorderBox()
    {
        // https://drafts.csswg.org/css-ui-4/#outline-offset: inset is the opposite of the width of the outline
        var document = Parse("<div id=a style='outline: 2px solid rgb(255, 0, 0); outline-offset: inset; width: 10px; height: 10px'></div>");
        Assert.Contains("draw-ring (0, 0, 10x10) - (2, 2, 6x6) fill #ff0000  [div#a]", Dump(document), StringComparison.Ordinal);
    }

    [Fact]
    public void Outline_PaintedBelowTheFollowingPositionedBoxes()
    {
        // As in Blink and WebKit, the outline of a positioned box is painted with it, below the positioned boxes that follow it
        var document = Parse("""
            <div style='position: relative'>
              <div id=a style='position: absolute; width: 10px; height: 10px; outline: 2px solid rgb(255, 0, 0)'></div>
              <div id=b style='position: absolute; width: 20px; height: 20px; background: rgb(0, 128, 0)'></div>
            </div>
            """);
        var dump = Dump(document);
        var outline = dump.IndexOf("fill #ff0000  [div#a]", StringComparison.Ordinal);
        var background = dump.IndexOf("fill #008000  [div#b]", StringComparison.Ordinal);
        Assert.True(outline >= 0 && background > outline, dump);
    }

    [Fact]
    public void Outline_AutoIsARoundedFocusRingOfTwoColors()
    {
        // The inner ring (2px) in the color of the outline, the outer ring (1px) white, with rounded corners
        var document = Parse("<div id=a style='outline: auto rgb(255, 0, 0); width: 10px; height: 10px'></div>");
        var dump = Dump(document);
        Assert.Contains("(-3, -3, 16x16) radii=3,3,3,3 - (0, 0, 10x10) fill #ffffff  [div#a]", dump, StringComparison.Ordinal);
        Assert.Contains("(-2, -2, 14x14) radii=2,2,2,2 - (0, 0, 10x10) fill #ff0000  [div#a]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void FormControls_AccentColor()
    {
        var document = Parse("<input id=c type=checkbox checked style='accent-color: rgb(255, 0, 0)'><progress id=p value=0.5 style='accent-color: rgba(0, 0, 255, 0.5)'></progress>");
        var dump = Dump(document);
        Assert.Contains("fill #ff0000  [input#c]", dump, StringComparison.Ordinal);

        // A transparent accent color is composed over Canvas (white)
        Assert.Contains("fill #8080ff  [progress#p]", dump, StringComparison.Ordinal);
    }

    [Fact]
    public void FormControls_MeterKeepsItsNativeAppearanceWithRevertedProperties()
    {
        var native = Dump(Parse("<meter id=m value=0.5></meter>"));
        Assert.Contains("[meter#m]", native, StringComparison.Ordinal);
        Assert.Equal(native, Dump(Parse("<style>meter { background-color: red; all: revert; border-top-color: revert; background-repeat: repeat }</style><meter id=m value=0.5></meter>")));
        Assert.DoesNotContain("[meter#m]", Dump(Parse("<style>meter { background-color: transparent }</style><meter id=m value=0.5></meter>")), StringComparison.Ordinal);
    }

    [Fact]
    public void OverflowClipMargin_RoundedCornersAreShapedAsAShadowSpread()
    {
        // The corners of the clip edge 100px beyond the border edge are not circles of 25px + 100px (css-borders-4, shadow shape)
        var document = Parse("<div style='overflow: clip; overflow-clip-margin: 100px; border-radius: 25px; width: 200px; height: 200px; margin: 100px'><div style='width: 10px; height: 10px; background: rgb(255, 0, 0)'></div></div>");
        var dump = Dump(document);
        Assert.Contains("clip-rounded-rect (0, 0, 400x400) radii=", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("radii=125,125,125,125", dump, StringComparison.Ordinal);
    }
}
