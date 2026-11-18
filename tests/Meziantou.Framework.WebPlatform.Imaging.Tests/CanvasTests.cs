using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The canvas 2D context (#379) with the Imaging backend: the drawing, the pixels, the text, the serialization, the painting.</summary>
public sealed class CanvasTests : IDisposable
{
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    private (BrowsingContext Context, HtmlCanvasElement Canvas, CanvasRenderingContext2D Context2D) Create(int width = 100, int height = 50, CanvasRenderingContext2DSettings? settings = null, RenderingBackendKind backend = RenderingBackendKind.Imaging)
    {
        var context = _pages.Load($"<canvas id=c width={width} height={height}></canvas>", backend: backend);
        var canvas = (HtmlCanvasElement)context.Document!.GetElementById("c")!;
        var context2D = (CanvasRenderingContext2D)canvas.GetContext("2d", settings)!;
        return (context, canvas, context2D);
    }

    private static byte[] GetPixel(CanvasRenderingContext2DBase context, int x, int y) => context.GetImageData(x, y, 1, 1).Data.ToArray();

    [Fact]
    public void GetContext_WithoutBackend_ReturnsNull()
    {
        var context = _pages.Load("<canvas id=c></canvas>", backend: RenderingBackendKind.None);
        var canvas = (HtmlCanvasElement)context.Document!.GetElementById("c")!;
        Assert.Null(canvas.GetContext("2d"));

        // The bitmap renderer does not need a backend, and the canvas can still be serialized
        Assert.IsType<ImageBitmapRenderingContext>(canvas.GetContext("bitmaprenderer"));
        Assert.StartsWith("data:image/png;base64,", canvas.ToDataUrl(), StringComparison.Ordinal);
    }

    [Fact]
    public void GetContext_ReturnsTheSameContext()
    {
        var (_, canvas, context) = Create();
        Assert.Same(context, canvas.GetContext("2d"));
        Assert.Null(canvas.GetContext("bitmaprenderer"));
        Assert.Same(canvas, context.Canvas);
        Assert.True(context.GetContextAttributes().Alpha);
    }

    [Fact]
    public void Float16_PutGetPreservesHalfBitsAndDirtyRectangles()
    {
        var (_, canvas, context) = Create(3, 2, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        Assert.Equal(CanvasColorType.Float16, context.GetContextAttributes().ColorType);
        Assert.Same(context, canvas.GetContext("2d", new CanvasRenderingContext2DSettings()));

        var settings = new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 };
        var input = new ImageData(2, 1, settings);
        Half[] values = [(Half)0.1234, Half.Epsilon, (Half)1.5, Half.One, (Half)(-0.25), (Half)0.501, (Half)0.0001, (Half)0.333];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        context.PutImageData(input, 0, 0);
        Assert.Equal(input.Data.ToArray(), context.GetImageData(0, 0, 2, 1, settings).Data.ToArray());
        Assert.Equal(ImageDataPixelFormat.RgbaUnorm8, context.GetImageData(0, 0, 1, 1).PixelFormat);
        Assert.Equal([31, 0, 255, 255], GetPixel(context, 0, 0));

        context.PutImageData(input, 1, 1, 1, 0, 1, 1);
        var output = context.GetImageData(-1, 1, 4, 1, settings);
        Assert.Equal(new byte[24], output.Data.Span[..24].ToArray());
        Assert.Equal(input.Data.Span[8..].ToArray(), output.Data.Span[24..].ToArray());

        canvas.Width = 3;
        Assert.Equal(CanvasColorType.Float16, context.GetContextAttributes().ColorType);
        Assert.Equal(new byte[8], context.GetImageData(0, 0, 1, 1, settings).Data.ToArray());
    }

    [Fact]
    public void Float16_DrawAndSnapshotPreserveMoreThanEightBits()
    {
        var (_, canvas, context) = Create(2, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        var settings = new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 };
        var input = new ImageData(1, 1, settings);
        Half[] values = [(Half)0.1234, (Half)0.501, (Half)0.333, Half.One];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        context.PutImageData(input, 0, 0);
        context.DrawImage(canvas, 1, 0);
        Assert.Equal(input.Data.ToArray(), context.GetImageData(1, 0, 1, 1, settings).Data.ToArray());

        context.FillStyle = "rgba(255, 0, 0, 0.5)";
        context.FillRect(0, 0, 1, 1);
        var output = context.GetImageData(0, 0, 1, 1, settings);
        var actual = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(output.Data.Span));
        Assert.InRange((double)actual[0], 0.561, 0.563);
        Assert.InRange((double)actual[1], 0.250, 0.251);
        Assert.Equal(Half.One, actual[3]);
        context.ClearRect(1, 0, 1, 1);
        Assert.Equal(new byte[8], context.GetImageData(1, 0, 1, 1, settings).Data.ToArray());
    }

    [Fact]
    public async Task Float16_ImageBitmapsPreserveFormatAcrossCropResizeAndTransfer()
    {
        var (browsingContext, canvas, context) = Create(2, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        var settings = new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 };
        var input = new ImageData(1, 1, settings);
        Half[] values = [(Half)0.1234, (Half)0.501, (Half)0.333, Half.One];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        var task = canvas.OwnerDocument!.DefaultView!.CreateImageBitmap(input, new ImageBitmapOptions { ResizeWidth = 2, ResizeQuality = ResizeQuality.Pixelated });
        browsingContext.EventLoop.RunUntilIdle();
        var bitmap = await task;
        Assert.Equal(ImageDataPixelFormat.RgbaFloat16, bitmap.Bitmap!.PixelFormat);
        Assert.Equal(input.Data.ToArray(), bitmap.Bitmap.PixelBytes.AsSpan(8, 8).ToArray());
        context.DrawImage(bitmap, 0, 0);
        Assert.Equal(input.Data.ToArray(), context.GetImageData(1, 0, 1, 1, settings).Data.ToArray());

        var croppedTask = canvas.OwnerDocument.DefaultView.CreateImageBitmap(canvas, 1, 0, 1, 1);
        browsingContext.EventLoop.RunUntilIdle();
        var cropped = await croppedTask;
        Assert.Equal(input.Data.ToArray(), cropped.Bitmap!.PixelBytes);

        var formatted = ImageBitmapFactory.Format(cropped.Bitmap, null, new ImageBitmapOptions { ResizeWidth = 2, ResizeQuality = ResizeQuality.High, ImageOrientation = ImageBitmapOrientation.FlipY })!;
        Assert.Equal(input.Data.ToArray(), formatted.PixelBytes.AsSpan(8, 8).ToArray());

        var sourceCanvas = (HtmlCanvasElement)canvas.OwnerDocument.CreateElement("canvas");
        sourceCanvas.Width = 1;
        sourceCanvas.Height = 1;
        var offscreen = sourceCanvas.TransferControlToOffscreen();
        var offscreenContext = (OffscreenCanvasRenderingContext2D)offscreen.GetContext(OffscreenRenderingContextId.TwoD, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 })!;
        offscreenContext.PutImageData(input, 0, 0);
        var transferred = offscreen.TransferToImageBitmap();
        Assert.Equal(input.Data.ToArray(), transferred.Bitmap!.PixelBytes);
        context.DrawImage(transferred, 0, 0);
        Assert.Equal(input.Data.ToArray(), context.GetImageData(0, 0, 1, 1, settings).Data.ToArray());
        Assert.Equal(new byte[8], offscreenContext.GetImageData(0, 0, 1, 1, settings).Data.ToArray());
    }

    [Fact]
    public void Float16_WideGamutPaintAndReadbackDoNotClipColors()
    {
        var (_, _, context) = Create(1, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        context.FillStyle = "color(display-p3 1 0 0)";
        context.FillRect(0, 0, 1, 1);
        var output = context.GetImageData(0, 0, 1, 1, new ImageDataSettings { ColorSpace = PredefinedColorSpace.DisplayP3, PixelFormat = ImageDataPixelFormat.RgbaFloat16 });
        var values = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(output.Data.Span));
        Assert.InRange((double)values[0], 0.999, 1.001);
        Assert.InRange((double)values[1], -0.001, 0.001);
        Assert.InRange((double)values[2], -0.001, 0.001);
    }

    [Fact]
    public void Float16_ImagesAndPatternsCanBeDrawnOnUnorm8Canvases()
    {
        var (_, targetCanvas, target) = Create(2, 1);
        var canvas = (HtmlCanvasElement)targetCanvas.OwnerDocument!.CreateElement("canvas");
        canvas.Width = 1;
        canvas.Height = 1;
        var context = (CanvasRenderingContext2D)canvas.GetContext("2d", new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 })!;
        var input = new ImageData(1, 1, new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 });
        Half[] values = [(Half)2, (Half)(-1), (Half)0.501, Half.One];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        context.PutImageData(input, 0, 0);

        target.DrawImage(canvas, 0, 0);
        target.FillStyle = target.CreatePattern(canvas, "repeat")!;
        target.FillRect(1, 0, 1, 1);
        Assert.Equal([255, 0, 128, 255], GetPixel(target, 0, 0));
        Assert.Equal([255, 0, 128, 255], GetPixel(target, 1, 0));
        Assert.Equal(CanvasColorType.Unorm8, target.GetContextAttributes().ColorType);
    }

    [Fact]
    public async Task Float16_PngImageBitmapRoundTripPreservesAGrayscaleRamp()
    {
        var (browsingContext, canvas, context) = Create(1024, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        for (var i = 0; i < 1024; i++)
        {
            var value = i / 1023.0;
            context.FillStyle = FormattableString.Invariant($"color(srgb {value} {value} {value})");
            context.FillRect(i, 0, 1, 1);
        }

        var png = Convert.FromBase64String(canvas.ToDataUrl()["data:image/png;base64,".Length..]);
        var bitmapTask = canvas.OwnerDocument!.DefaultView!.CreateImageBitmap(new Blob([png]));
        browsingContext.EventLoop.RunUntilIdle();
        var bitmap = await bitmapTask;
        Assert.Equal(ImageDataPixelFormat.RgbaFloat16, bitmap.Bitmap!.PixelFormat);
        context.ClearRect(0, 0, 1024, 1);
        context.DrawImage(bitmap, 0, 0);
        var output = context.GetImageData(0, 0, 1024, 1, new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 });
        var samples = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(output.Data.Span));
        var distinct = new HashSet<Half>();
        for (var i = 0; i < samples.Length; i += 4)
        {
            distinct.Add(samples[i]);
        }

        Assert.True(distinct.Count > 1000, $"The PNG round trip retained only {distinct.Count} distinct grayscale samples.");
    }

    [Theory]
    [InlineData(ImageSmoothingQuality.Low)]
    [InlineData(ImageSmoothingQuality.Medium)]
    [InlineData(ImageSmoothingQuality.High)]
    public void Float16_DrawImagePreservesHighlightsAndSubnormalSamples(ImageSmoothingQuality quality)
    {
        var (_, canvas, context) = Create(2, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        var input = new ImageData(1, 1, new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 });
        Half[] values = [(Half)2, (Half)(-1), Half.Epsilon, Half.One];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        context.PutImageData(input, 0, 0);
        context.ImageSmoothingQuality = quality;
        context.DrawImage(canvas, 0, 0, 1, 1, 1, 0, 1, 1);
        Assert.Equal(input.Data.ToArray(), context.GetImageData(1, 0, 1, 1, new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 }).Data.ToArray());
    }

    [Fact]
    public void Float16_PngSerializationUsesSixteenBitSamples()
    {
        var (_, canvas, context) = Create(1, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        var input = new ImageData(1, 1, new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 });
        Half[] values = [(Half)0.1234, (Half)0.501, (Half)0.333, Half.One];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        context.PutImageData(input, 0, 0);
        var png = Convert.FromBase64String(canvas.ToDataUrl()["data:image/png;base64,".Length..]);
        Assert.Equal(16, png[24]);
        using var image = Image.Load<Rgba64>(png);
        var pixels = new Rgba64[1];
        image.Frames[0].CopyPixelDataTo(pixels);
        Assert.InRange(pixels[0].R / 65535.0, (double)values[0] - (1.0 / 65535), (double)values[0] + (1.0 / 65535));
        Assert.Equal(ushort.MaxValue, pixels[0].A);
    }

    [Fact]
    public void Float16_OpaqueCanvasPreservesColorPrecision()
    {
        var (_, _, context) = Create(1, 1, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16, Alpha = false });
        var settings = new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16 };
        Assert.False(context.GetContextAttributes().Alpha);
        var input = new ImageData(1, 1, settings);
        Half[] values = [(Half)0.1234, (Half)0.501, (Half)0.333, (Half)0.5];
        var bytes = unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
        bytes.CopyTo(input.Data.Span);
        context.PutImageData(input, 0, 0);
        var output = context.GetImageData(0, 0, 1, 1, settings);
        var actual = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(output.Data.Span));
        Assert.Equal((Half)((float)values[0] * 0.5f), actual[0]);
        Assert.Equal(Half.One, actual[3]);
        context.Reset();
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 0, 0));
    }

    [Fact]
    public void FillRect_GetImageData()
    {
        var (_, _, context) = Create();
        context.FillStyle = "#f00";
        Assert.Equal("#ff0000", context.FillStyle);
        context.FillRect(0, 0, 50, 50);
        context.FillStyle = "rgba(0, 255, 0, 0.5)";
        Assert.Equal("rgba(0, 255, 0, 0.5)", context.FillStyle);
        context.FillRect(50, 0, 50, 50);

        Assert.Equal([255, 0, 0, 255], GetPixel(context, 25, 25));
        Assert.Equal([0, 255, 0, 128], GetPixel(context, 75, 25));

        // The pixels outside of the canvas are transparent black
        var data = context.GetImageData(-1, 0, 2, 1);
        Assert.Equal([0, 0, 0, 0, 255, 0, 0, 255], data.Data.ToArray());

        context.ClearRect(0, 0, 10, 10);
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 5, 5));
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => context.GetImageData(0, 0, 0, 1)).Name);
    }

    [Fact]
    public void Paths_FillStrokeAndClip()
    {
        var (_, _, context) = Create();

        // A full circle (an end angle 2π after the start angle) clips the next drawing
        context.Arc(50, 25, 20, 0, 2 * Math.PI);
        Assert.True(context.IsPointInPath(50, 25));
        Assert.False(context.IsPointInPath(5, 5));
        context.Clip();
        context.FillStyle = "#00f";
        context.FillRect(0, 0, 100, 50);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 50, 25));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 5, 5));

        // The stroke is in the coordinates of the current transformation matrix
        var (_, _, other) = Create();
        other.Scale(2, 2);
        other.LineWidth = 4;
        other.BeginPath();
        other.MoveTo(0, 10);
        other.LineTo(50, 10);
        other.Stroke();
        Assert.True(other.IsPointInStroke(50, 23));
        Assert.False(other.IsPointInStroke(50, 30));
        Assert.Equal([0, 0, 0, 255], GetPixel(other, 50, 20));
        Assert.Equal([0, 0, 0, 0], GetPixel(other, 50, 30));

        var path = new Path2D("M 0 0 L 100 0 L 100 50 Z");
        other.ResetTransform();
        Assert.True(other.IsPointInPath(path, 90, 10, CanvasFillRule.EvenOdd));
        Assert.False(other.IsPointInPath(path, 10, 40));
    }

    [Fact]
    public void Styles_GradientsPatternsAndShadows()
    {
        var (_, _, context) = Create();
        var gradient = context.CreateLinearGradient(0, 0, 100, 0);
        gradient.AddColorStop(0, "#f00");
        gradient.AddColorStop(1, "#00f");
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => gradient.AddColorStop(0.5, "not a color")).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => gradient.AddColorStop(2, "red")).Name);
        context.FillStyle = gradient;
        Assert.Same(gradient, context.FillStyle);
        context.FillRect(0, 0, 100, 50);
        var pixel = GetPixel(context, 50, 25);
        Assert.InRange(pixel[0], 120, 135);
        Assert.InRange(pixel[2], 120, 135);

        // A pattern of another canvas (of the same document: the same backend), and a shadow offset by 50px (not affected by the transform)
        var sourceCanvas = (HtmlCanvasElement)context.Canvas.OwnerDocument!.CreateElement("canvas");
        sourceCanvas.Width = 10;
        sourceCanvas.Height = 10;
        var source = (CanvasRenderingContext2D)sourceCanvas.GetContext("2d")!;
        source.FillStyle = "#0f0";
        source.FillRect(0, 0, 10, 10);
        var target = (CanvasRenderingContext2D)((HtmlCanvasElement)context.Canvas.OwnerDocument.CreateElement("canvas")).GetContext("2d")!;
        target.FillStyle = target.CreatePattern(sourceCanvas, "repeat")!;
        target.ShadowColor = "#00f";
        target.ShadowOffsetX = 50;
        target.Scale(0.5, 1);
        target.FillRect(0, 0, 100, 20);
        Assert.Equal([0, 255, 0, 255], GetPixel(target, 25, 10));
        Assert.Equal([0, 0, 255, 255], GetPixel(target, 75, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(target, 25, 40));
    }

    [Fact]
    public void Compositing_CopyClearsTheClipRegion()
    {
        var (_, _, context) = Create();
        context.FillStyle = "#f00";
        context.FillRect(0, 0, 100, 50);
        context.GlobalCompositeOperation = "copy";
        context.GlobalCompositeOperation = "not an operator";
        Assert.Equal("copy", context.GlobalCompositeOperation);
        context.FillStyle = "#0f0";
        context.FillRect(0, 0, 50, 50);
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 25, 25));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 75, 25));
    }

    [Fact]
    public void TextClusters_PositionsWithAlignmentAndBaseline()
    {
#pragma warning disable WEBPLATEXP0024
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        context.TextBaseline = CanvasTextBaseline.Top;
        var metrics = context.MeasureText("ABC");
        var clusters = metrics.GetTextClusters();
        Assert.Equal(3, clusters.Count);
        Assert.Equal(20, clusters[1].X);
        Assert.Equal(0, clusters[1].Y);
        Assert.Equal(CanvasTextAlign.Start, clusters[1].Align);

        // The options give the alignment point and the baseline of the clusters
        var centered = metrics.GetTextClusters(1, 2, new TextClusterOptions { Align = CanvasTextAlign.Center, Baseline = CanvasTextBaseline.Alphabetic });
        Assert.Single(centered);
        Assert.Equal(30, centered[0].X);
        Assert.Equal(16, centered[0].Y);

        // The cluster is drawn with the font of the measurement, at the point plus its position
        context.Font = "40px serif";
        context.FillTextCluster(clusters[1], 10, 0);
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 40, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 25, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 55, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 40, 25));

        // The options replace the position, the alignment and the baseline of the cluster
        context.FillTextCluster(clusters[0], 100, 40, new TextClusterOptions { Align = CanvasTextAlign.Right, Baseline = CanvasTextBaseline.Bottom, X = 0, Y = 0 });
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 90, 30));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 105, 30));
#pragma warning restore WEBPLATEXP0024
    }

    [Fact]
    public void TextClusters_SelectionCaretAndBoundingBox()
    {
#pragma warning disable WEBPLATEXP0024
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        context.TextAlign = CanvasTextAlign.Right;
        var metrics = context.MeasureText("ABC");

        Assert.Equal(0u, metrics.GetIndexFromOffset(-100));
        Assert.Equal(1u, metrics.GetIndexFromOffset(-45));
        Assert.Equal(1u, metrics.GetIndexFromOffset(-40));
        Assert.Equal(3u, metrics.GetIndexFromOffset(10));

        var rects = metrics.GetSelectionRects(1, 3);
        Assert.Single(rects);
        Assert.Equal(-40, rects[0].X);
        Assert.Equal(40, rects[0].Width);
        Assert.Equal(-16, rects[0].Y);
        Assert.Equal(20, rects[0].Height);

        // A collapsed range is the caret position of its end
        var caret = metrics.GetSelectionRects(2, 1);
        Assert.Single(caret);
        Assert.Equal(-40, caret[0].X);
        Assert.Equal(0, caret[0].Width);

        var box = metrics.GetActualBoundingBox(0, 1);
        Assert.Equal(-60, box.X);
        Assert.Equal(20, box.Width);
        Assert.Equal(-16, box.Y);
        Assert.Equal(20, box.Height);

        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => metrics.GetTextClusters(3, 3)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => metrics.GetActualBoundingBox(0, 4)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => metrics.GetSelectionRects(4, 0)).Name);
        Assert.Single(metrics.GetSelectionRects(3, 3));
#pragma warning restore WEBPLATEXP0024
    }

    [Fact]
    public void TextClusters_RightToLeftAndLigatures()
    {
#pragma warning disable WEBPLATEXP0024
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        context.Direction = CanvasDirection.Rtl;
        context.TextAlign = CanvasTextAlign.Left;
        var metrics = context.MeasureText("\u202EABC");

        // The first character of a right-to-left text is on the right (the override has no width)
        var clusters = metrics.GetTextClusters();
        Assert.Equal(4, clusters.Count);
        Assert.Equal(1u, clusters[1].Start);
        Assert.Equal(40, clusters[1].X);
        Assert.Equal(0, clusters[3].X);
        Assert.Equal(1u, metrics.GetIndexFromOffset(59));
        Assert.Equal(2u, metrics.GetIndexFromOffset(45));
        Assert.Equal(4u, metrics.GetIndexFromOffset(-1));

        // A zero-width joiner extends the cluster of the character before it, a null character is not drawn
        var joined = context.MeasureText("A\u200DB\0C").GetTextClusters();
        Assert.Equal(3, joined.Count);
        Assert.Equal(0u, joined[0].Start);
        Assert.Equal(2u, joined[0].End);
        Assert.Equal(4u, joined[1].End);
#pragma warning restore WEBPLATEXP0024
    }

    [Fact]
    public void ShapedText_GlyphRunsAndMetrics()
    {
#pragma warning disable WEBPLATEXP0025
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        context.TextBaseline = CanvasTextBaseline.Top;
        var shaped = context.ShapeText("AB\u202ECD");
        Assert.Equal(80, shaped.Width);

        // The glyph runs are the bidi runs, from left to right
        var runs = shaped.GlyphRuns();
        Assert.Equal(2, runs.Count);
        Assert.Equal(0u, runs[0].Start);
        Assert.Equal(2u, runs[0].End);
        Assert.Equal(2u, runs[1].Start);
        Assert.Equal(5u, runs[1].End);
        Assert.Equal(40, runs[1].X);
        Assert.Equal(40, runs[1].Width);
        Assert.Equal(40, runs[1].GetTextMetrics().Width);

        Assert.Equal(80, shaped.PositionToAdvance(3));
        Assert.Equal(60, shaped.PositionToAdvance(4));
        Assert.Equal(4u, shaped.AdvanceToPosition(61));
        Assert.Equal(2, shaped.SelectionRects(1, 4).Count);
        Assert.Equal(20, shaped.ActualBoundingBox(1, 2).X);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => shaped.PositionToAdvance(6)).Name);

        // The runs are drawn with the font of the shaping, at the alignment point of the shaped text
        context.Font = "40px serif";
        context.FillGlyphRun(runs[1], 10, 0);
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 55, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 45, 10));
        context.StrokeGlyphRuns([runs[0]], 100, 0);
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 150, 10));
        Assert.NotEqual([0, 0, 0, 0], GetPixel(context, 100, 10));
#pragma warning restore WEBPLATEXP0025
    }

    [Fact]
    public void ShapedText_ClosingControlIsAtTheStartOfTheNextCharacter()
    {
#pragma warning disable WEBPLATEXP0025
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        var shaped = context.ShapeText("A\u202EB\u202CC");
        Assert.Equal(60, shaped.Width);

        // The closing control is in the run of C, at its left edge: the runs do not overlap
        var runs = shaped.GlyphRuns();
        Assert.Equal(60, runs.Sum(run => run.Width));
        var last = runs[^1];
        Assert.Equal(3u, last.Start);
        Assert.Equal(5u, last.End);
        Assert.Equal(40, last.X);
        Assert.Equal(20, last.Width);

        // Selecting the closing control and C does not select B
        var rect = Assert.Single(shaped.SelectionRects(3, 5));
        Assert.Equal(40, rect.X);
        Assert.Equal(20, rect.Width);
#pragma warning restore WEBPLATEXP0025
    }

    [Fact]
    public void ShapedText_NegativeLetterSpacing()
    {
#pragma warning disable WEBPLATEXP0025
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        context.LetterSpacing = "-30px";
        var shaped = context.ShapeText("ABC");

        // Each character advances by -10px: the caret positions go from right to left
        Assert.Equal(-10, shaped.PositionToAdvance(1));
        Assert.Equal(-20, shaped.PositionToAdvance(2));
        Assert.Equal(1u, shaped.AdvanceToPosition(-10));
        Assert.Equal(2u, shaped.AdvanceToPosition(-19));
        Assert.Equal(0u, shaped.AdvanceToPosition(-4));
        Assert.Equal(0u, shaped.AdvanceToPosition(5));
        Assert.Equal(3u, shaped.AdvanceToPosition(-100));

        // A cluster expanded into several glyphs (the synthesized small capitals of a sharp s) starts at its pen position
        context.Font = "small-caps 20px Ahem";
        var expanded = context.ShapeText("x\u00DF");
        Assert.Equal(0, expanded.PositionToAdvance(0));
        Assert.Equal(expanded.Width, expanded.PositionToAdvance(2));
        Assert.True(expanded.PositionToAdvance(1) > expanded.PositionToAdvance(2));
#pragma warning restore WEBPLATEXP0025
    }

    [Fact]
    public void ShapedText_NullCharactersHaveTheCaretOfTheNextCharacter()
    {
#pragma warning disable WEBPLATEXP0025
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        var shaped = context.ShapeText("\0AB\0C");
        Assert.Equal(60, shaped.Width);
        Assert.Equal(0, shaped.PositionToAdvance(0));
        Assert.Equal(20, shaped.PositionToAdvance(2));
        Assert.Equal(40, shaped.PositionToAdvance(4));
        Assert.Equal(60, shaped.PositionToAdvance(5));
        Assert.Equal(0, Assert.Single(shaped.SelectionRects(0, 0)).X);
#pragma warning restore WEBPLATEXP0025
    }

    [Fact]
    public void StrokeText_TextOfTheGlyphsIsNotRepeated()
    {
        // The glyphs of a stroked text are stroked one by one: the text (e.g. of a PDF) is on the first one only
        var (_, _, context) = Create(200, 50);
        context.Font = "20px Ahem";
        context.StrokeText("ABC", 10, 30);
        var glyphs = context.Bitmap.TakeRecording().Items.ToArray().OfType<Meziantou.Framework.WebPlatform.Rendering.DrawGlyphsItem>().ToList();
        Assert.Equal(3, glyphs.Count);
        Assert.Equal("ABC", string.Concat(glyphs.Select(item => item.Run.Text)));
    }

    [Fact]
    public void ShapedText_LigatureCarets()
    {
#pragma warning disable WEBPLATEXP0025
        // ShapingTest.ttf has a ligature f_i of 13px at 20px, with a caret position at 6px (GDEF)
        var (browsingContext, _, context) = Create(200, 50);
        var face = new Meziantou.Framework.WebPlatform.Css.Om.FontFace("Test", System.IO.File.ReadAllBytes(Path.Combine(TestPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Tests", "Css", "Fonts", "Resources", "ShapingTest.ttf")));
        browsingContext.Document!.Fonts.Add(face);
        browsingContext.EventLoop.RunUntilIdle();
        context.Font = "20px Test";

        var shaped = context.ShapeText("fi");
        Assert.Equal(13, shaped.Width, 1e-9);
        var cluster = Assert.Single(shaped.GlyphClusters());
        Assert.Equal(2u, cluster.End);

        // The caret positions inside the ligature are the ones of the font
        Assert.Equal(6, shaped.PositionToAdvance(1), 1e-9);
        Assert.Equal(1u, shaped.AdvanceToPosition(6.5));
        Assert.Equal(0u, shaped.AdvanceToPosition(2));
        Assert.Equal(2u, shaped.AdvanceToPosition(12));
        Assert.Equal(6, Assert.Single(shaped.SelectionRects(0, 1)).Width, 1e-9);
        Assert.Equal(7, Assert.Single(shaped.SelectionRects(1, 2)).Width, 1e-9);
#pragma warning restore WEBPLATEXP0025
    }

    [Fact]
    public void ShapedText_GlyphRunsAreOneDrawingOperation()
    {
#pragma warning disable WEBPLATEXP0025
        var (_, _, context) = Create(100, 50);
        context.Font = "20px Ahem";
        context.TextBaseline = CanvasTextBaseline.Top;
        var shaped = context.ShapeText("ABC");
        context.FillStyle = "#f00";
        context.FillRect(0, 0, 100, 50);

        // With the copy operator, the clusters drawn together do not erase each other
        context.GlobalCompositeOperation = "copy";
        context.FillStyle = "#0f0";
        context.FillGlyphRuns(shaped.GlyphClusters(), 0, 0);
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 10, 10));
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 50, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 70, 10));
#pragma warning restore WEBPLATEXP0025
    }

    [Fact]
    public void Text_MeasureAndFill()
    {
        var (_, _, context) = Create();
        Assert.Equal("10px sans-serif", context.Font);
        context.Font = "italic bold 20px/3 Ahem, serif";
        Assert.Equal("italic bold 20px Ahem, serif", context.Font);
        context.Font = "inherit";
        Assert.Equal("italic bold 20px Ahem, serif", context.Font);

        // The glyphs of Ahem are 1em squares with a 0.8em ascent
        context.Font = "20px Ahem";
        var metrics = context.MeasureText("AB");
        Assert.Equal(40, metrics.Width);
        Assert.Equal(16, metrics.FontBoundingBoxAscent);
        Assert.Equal(4, metrics.FontBoundingBoxDescent);
        Assert.Equal(16, metrics.ActualBoundingBoxAscent);
        Assert.Equal(0, metrics.ActualBoundingBoxLeft);
        Assert.Equal(40, metrics.ActualBoundingBoxRight);

        context.LetterSpacing = "1EM";
        Assert.Equal("1em", context.LetterSpacing);
        Assert.Equal(80, context.MeasureText("AB").Width);
        context.LetterSpacing = "0px";

        context.TextBaseline = CanvasTextBaseline.Top;
        context.TextAlign = CanvasTextAlign.Center;
        context.FillText("AB", 50, 0);
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 45, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 25, 10));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 45, 30));
    }

    [Fact]
    public void DrawImage_TaintsTheCanvasWithCrossOriginImages()
    {
        var (_, canvas, context) = Create();
        var sourceCanvas = (HtmlCanvasElement)canvas.OwnerDocument!.CreateElement("canvas");
        sourceCanvas.Width = 10;
        sourceCanvas.Height = 10;
        var source = (CanvasRenderingContext2D)sourceCanvas.GetContext("2d")!;
        source.FillStyle = "#00f";
        source.FillRect(0, 0, 10, 10);
        context.DrawImage(sourceCanvas, 0, 0, 20, 20);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 15, 15));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 25, 25));

        // A bitmap that is not origin-clean taints the canvas: its pixels can no longer be read
        var tainted = new ImageBitmap(new BitmapPixels(1, 1, [255, 0, 0, 255], originClean: false));
        context.DrawImage(tainted, 50, 0);
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => context.GetImageData(0, 0, 1, 1)).Name);
        Assert.Equal("SecurityError", Assert.Throws<DomException>(() => canvas.ToDataUrl()).Name);

        // Resizing the canvas resets the bitmap and the origin-clean flag
        canvas.Width = 20;
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 15, 15));
    }

    [Fact]
    public async Task SvgImages_AreDrawnAsVectors()
    {
        // A 10x10 SVG image: a lime left half and a blue right half; an image without natural size
        _pages.Resources["/a.svg"] = ("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='5' height='10' fill='lime'/><rect x='5' width='5' height='10' fill='blue'/></svg>"u8.ToArray(), "image/svg+xml");
        _pages.Resources["/nosize.svg"] = ("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 2 1'><rect width='2' height='1' fill='lime'/></svg>"u8.ToArray(), "image/svg+xml");
        var browsingContext = _pages.Load("<canvas id=c width=100 height=50></canvas><img id=a src='/a.svg'><img id=b src='/nosize.svg'><svg><image id=i href='/a.svg'/><image id=broken href='/missing.png'/></svg>");
        var document = browsingContext.Document!;
        var canvas = (HtmlCanvasElement)document.GetElementById("c")!;
        var context = (CanvasRenderingContext2D)canvas.GetContext("2d")!;

        // Drawn at its natural size, and scaled as vectors (the edge between the halves stays sharp)
        context.DrawImage(document.GetElementById("a")!, 0, 0);
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 2, 5));
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 7, 5));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 12, 5));
        context.DrawImage(document.GetElementById("a")!, 5, 0, 5, 10, 20, 0, 40, 40);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 20, 20));
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 59, 39));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 61, 20));

        // An image without natural dimensions has the concrete object size of its aspect ratio in the canvas (100x50)
        context.ClearRect(0, 0, 100, 50);
        context.DrawImage(document.GetElementById("b")!, 0, 0);
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 99, 49));

        // An SVG image element draws its image; a broken one throws
        context.ClearRect(0, 0, 100, 50);
        context.DrawImage(document.GetElementById("i")!, 0, 0);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 7, 5));
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => context.CreatePattern(document.GetElementById("broken")!, "repeat")).Name);

        // A pattern rasterizes the image at its natural size
        context.FillStyle = context.CreatePattern(document.GetElementById("a")!, "repeat")!;
        context.FillRect(0, 0, 100, 50);
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 12, 5));
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 17, 45));

        // createImageBitmap renders the image at the output size; an image without natural dimensions needs the resize options
        var window = document.DefaultView!;
        var bitmap = window.CreateImageBitmap(document.GetElementById("a")!, new ImageBitmapOptions { ResizeWidth = 40, ResizeHeight = 20 });
        var withoutSize = window.CreateImageBitmap(document.GetElementById("b")!);
        browsingContext.EventLoop.RunUntilIdle();
        var result = await bitmap;
        Assert.Equal((40u, 20u), (result.Width, result.Height));
        Assert.Equal([0, 255, 0, 255], result.Bitmap!.Rgba.AsSpan(19 * 4, 4).ToArray());
        Assert.Equal([0, 0, 255, 255], result.Bitmap!.Rgba.AsSpan(20 * 4, 4).ToArray());
        Assert.Equal("InvalidStateError", Assert.IsType<DomException>(withoutSize.Exception?.InnerException).Name);
    }

    [Fact]
    public void SvgImages_AreDrawnOverTheirShadowWithTheGlobalAlpha()
    {
        _pages.Resources["/a.svg"] = ("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10' fill='lime'/></svg>"u8.ToArray(), "image/svg+xml");
        var browsingContext = _pages.Load("<canvas id=c width=40 height=20></canvas><img id=a src='/a.svg'>");
        var document = browsingContext.Document!;
        var canvas = (HtmlCanvasElement)document.GetElementById("c")!;
        var context = (CanvasRenderingContext2D)canvas.GetContext("2d")!;

        // The image is drawn with the global alpha over its shadow, and the drawings after it are not inside the shadow
        context.GlobalAlpha = 0.5;
        context.ShadowColor = "#00f";
        context.ShadowOffsetX = 10;
        context.DrawImage(document.GetElementById("a")!, 0, 0);
        context.ShadowColor = "transparent";
        context.GlobalAlpha = 1;
        context.FillStyle = "#f00";
        context.FillRect(30, 0, 10, 10);

        Assert.Equal([0, 255, 0, 128], GetPixel(context, 5, 5));
        Assert.Equal([0, 0, 255, 128], GetPixel(context, 15, 5));
        Assert.Equal([255, 0, 0, 255], GetPixel(context, 35, 5));
    }

    [Fact]
    public void PutImageData_ConvertsTheColorSpace()
    {
        var (_, _, context) = Create(settings: new CanvasRenderingContext2DSettings { ColorSpace = PredefinedColorSpace.DisplayP3 });
        Assert.Equal(PredefinedColorSpace.DisplayP3, context.GetContextAttributes().ColorSpace);
        var data = context.CreateImageData(1, 1, new ImageDataSettings { ColorSpace = PredefinedColorSpace.Srgb });
        data.Data.Span[0] = 255;
        data.Data.Span[3] = 255;
        context.PutImageData(data, 0, 0);

        // sRGB red is not the red of Display P3
        var p3 = context.GetImageData(0, 0, 1, 1);
        Assert.Equal(PredefinedColorSpace.DisplayP3, p3.ColorSpace);
        Assert.InRange(p3.Data.Span[0], 230, 240);
        Assert.InRange(p3.Data.Span[1], 45, 55);
        var srgb = context.GetImageData(0, 0, 1, 1, new ImageDataSettings { ColorSpace = PredefinedColorSpace.Srgb });
        Assert.InRange(srgb.Data.Span[0], 253, 255);
        Assert.InRange(srgb.Data.Span[1], 0, 2);
    }

    [Fact]
    public async Task DisplayP3_SerializationAndImageBitmapsKeepTheColorsOutsideOfSrgb()
    {
        var p3 = new CanvasRenderingContext2DSettings { ColorSpace = PredefinedColorSpace.DisplayP3 };
        var (browsingContext, canvas, context) = Create(2, 2, p3);
        context.FillStyle = "color(display-p3 1 0 0)";
        context.FillRect(0, 0, 2, 2);

        // The PNG file is tagged as Display P3 (cICP chunk)
        var png = Convert.FromBase64String(canvas.ToDataUrl()["data:image/png;base64,".Length..]);
        Assert.Equal(new Meziantou.Framework.Imaging.Metadata.CicpColorDescription(12, 13, 0, fullRange: true), Image.Identify(png).Metadata.Cicp);

        var window = canvas.OwnerDocument!.DefaultView!;
        var fromBlob = window.CreateImageBitmap(new Blob([png]));
        var fromCanvas = window.CreateImageBitmap(canvas);
        var fromImageData = window.CreateImageBitmap(context.GetImageData(0, 0, 2, 2));
        browsingContext.EventLoop.RunUntilIdle();
        foreach (var bitmap in (ImageBitmap[])[await fromBlob, await fromCanvas, await fromImageData])
        {
            var (_, _, target) = Create(2, 2, p3);
            target.DrawImage(bitmap, 0, 0);
            var pixel = GetPixel(target, 1, 1);
            Assert.InRange(pixel[0], 254, 255);
            Assert.InRange(pixel[1], 0, 1);
            Assert.InRange(pixel[2], 0, 1);
        }

        // An sRGB canvas of half floats keeps the color outside of its gamut, an sRGB canvas of 8 bits clips it
        var (_, _, half) = Create(2, 2, new CanvasRenderingContext2DSettings { ColorType = CanvasColorType.Float16 });
        half.DrawImage(await fromImageData, 0, 0);
        var halfPixel = half.GetImageData(0, 0, 1, 1, new ImageDataSettings { ColorSpace = PredefinedColorSpace.DisplayP3 }).Data.ToArray();
        Assert.InRange(halfPixel[0], 254, 255);
        Assert.InRange(halfPixel[1], 0, 1);
        var (_, _, srgb) = Create(2, 2);
        srgb.DrawImage(await fromImageData, 0, 0);
        Assert.Equal([234, 51, 35, 255], srgb.GetImageData(0, 0, 1, 1, new ImageDataSettings { ColorSpace = PredefinedColorSpace.DisplayP3 }).Data.ToArray());
    }

    [Fact]
    public async Task Serialization_EncodesThePixels()
    {
        var (context, canvas, context2D) = Create(4, 2);
        context2D.FillStyle = "#0f0";
        context2D.FillRect(0, 0, 2, 2);

        var url = canvas.ToDataUrl();
        Assert.StartsWith("data:image/png;base64,", url, StringComparison.Ordinal);
        using (var bitmap = Image.Load<Rgba32>(Convert.FromBase64String(url["data:image/png;base64,".Length..])))
        {
            Assert.Equal((4, 2), (bitmap.Width, bitmap.Height));
            Assert.Equal(Colors.Lime, bitmap.GetPixel(1, 1));
            Assert.Equal(0, bitmap.GetPixel(3, 1).A);
        }

        // A JPEG image has no alpha channel: the transparent pixels are black
        var jpegUrl = canvas.ToDataUrl("image/JPEG", 0.5);
        Assert.StartsWith("data:image/jpeg;base64,", jpegUrl, StringComparison.Ordinal);
        using (var bitmap = Image.Load<Rgba32>(Convert.FromBase64String(jpegUrl["data:image/jpeg;base64,".Length..])))
        {
            Assert.Equal((4, 2), (bitmap.Width, bitmap.Height));
            var lime = bitmap.GetPixel(0, 0);
            Assert.True(lime is { R: < 120, G: > 150, A: 255 }, $"The pixel (0, 0) is {lime}");
            var black = bitmap.GetPixel(3, 1);
            Assert.True(black is { R: < 100, G: < 100, B: < 100, A: 255 }, $"The pixel (3, 1) is {black}");
        }

        Assert.StartsWith("data:image/png;base64,", canvas.ToDataUrl("image/unknown"), StringComparison.Ordinal);

        // A WebP image of the default quality is lossy (its alpha channel is not), and lossless when its quality is 1
        Blob? blob = null;
        canvas.ToBlob(result => blob = result, "image/webp");
        Blob? losslessBlob = null;
        canvas.ToBlob(result => losslessBlob = result, "image/webp", 1.0);
        context.EventLoop.RunUntilIdle();
        Assert.NotNull(blob);
        Assert.Equal("image/webp", blob.Type);
        using (var bitmap = Image.Load<Rgba32>(await blob.Bytes()))
        {
            Assert.Equal((4, 2), (bitmap.Width, bitmap.Height));
            var lime = bitmap.GetPixel(0, 0);
            Assert.True(lime is { R: < 60, G: > 200, B: < 60, A: 255 }, $"The pixel (0, 0) is {lime}");
            Assert.Equal(0, bitmap.GetPixel(3, 1).A);
        }

        Assert.NotNull(losslessBlob);
        Assert.Equal("image/webp", losslessBlob.Type);
        using (var bitmap = Image.Load<Rgba32>(await losslessBlob.Bytes()))
        {
            Assert.Equal((4, 2), (bitmap.Width, bitmap.Height));
            Assert.Equal(Colors.Lime, bitmap.GetPixel(1, 1));
            Assert.Equal(0, bitmap.GetPixel(3, 1).A);
        }
    }

    [Fact]
    public void Canvas_IsPaintedInThePage()
    {
        // A canvas is rendered (instead of its fallback content) when scripting is enabled
        var context = _pages.Load("<canvas id=c width=20 height=10 style='width: 40px; height: 20px; image-rendering: pixelated'></canvas>", scriptingEnabled: true);
        var canvas = (HtmlCanvasElement)context.Document!.GetElementById("c")!;
        var context2D = (CanvasRenderingContext2D)canvas.GetContext("2d")!;
        context2D.FillStyle = "#f00";
        context2D.FillRect(0, 0, 10, 10);
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Red, screenshot.GetPixel(10, 10));
            Assert.Equal(Colors.White, screenshot.GetPixel(30, 10));
        }

        // The page is painted again when the canvas changes
        context2D.FillStyle = "#00f";
        context2D.FillRect(10, 0, 10, 10);
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Blue, screenshot.GetPixel(30, 10));
        }
    }

    [Fact]
    public void TransferControlToOffscreen_PaintsTheFramesOfTheOffscreenCanvas()
    {
        var context = _pages.Load("<canvas id=c width=20 height=10></canvas>", scriptingEnabled: true);
        var canvas = (HtmlCanvasElement)context.Document!.GetElementById("c")!;
        var offscreen = canvas.TransferControlToOffscreen();
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => canvas.GetContext("2d")).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => canvas.Width = 5).Name);

        var context2D = (OffscreenCanvasRenderingContext2D)offscreen.GetContext(OffscreenRenderingContextId.TwoD)!;
        context2D.FillStyle = "#0f0";
        context2D.FillRect(0, 0, 20, 10);
        offscreen.Width = 30;
        context2D.FillStyle = "#0f0";
        context2D.FillRect(0, 0, 30, 10);
        context.EventLoop.RunUntilIdle();

        // The frame is committed to the placeholder, which takes its size
        Assert.Equal(30u, canvas.Width);
        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Lime, screenshot.GetPixel(25, 5));

        var bitmap = offscreen.TransferToImageBitmap();
        Assert.Equal((30u, 10u), (bitmap.Width, bitmap.Height));
        Assert.Equal([0, 0, 0, 0], GetPixel(context2D, 5, 5));
    }

#pragma warning disable WEBPLATEXP0019 // The interpolation methods of the gradients are a proposal
    [Fact]
    public void Gradients_AreInterpolatedInTheColorInterpolationMethod()
    {
        var (_, _, context) = Create();

        // The stops that are not legacy sRGB colors are interpolated in Oklab by default, as in the CSS gradients
        var gradient = context.CreateLinearGradient(0, 0, 100, 0);
        gradient.AddColorStop(0, "#f00");
        gradient.AddColorStop(1, "color-mix(in srgb, #0f0, #00f)");
        Assert.Equal("srgb", gradient.ColorInterpolationMethod);
        context.FillStyle = gradient;
        context.FillRect(0, 0, 100, 50);
        var pixel = GetPixel(context, 25, 25);
        Assert.InRange(pixel[0], 209, 215);
        Assert.InRange(pixel[1], 78, 84);
        Assert.InRange(pixel[2], 58, 64);

        // Set explicitly, sRGB is used
        gradient.ColorInterpolationMethod = "srgb";
        gradient.ColorInterpolationMethod = "not a color space";
        Assert.Equal("srgb", gradient.ColorInterpolationMethod);
        context.FillRect(0, 0, 100, 50);
        pixel = GetPixel(context, 25, 25);
        Assert.InRange(pixel[0], 187, 193);
        Assert.InRange(pixel[1], 30, 36);
        Assert.InRange(pixel[2], 30, 36);

        // The hues of the polar color spaces: red to lime through yellow (shorter) or through blue (longer)
        gradient = context.CreateLinearGradient(0, 0, 100, 0);
        gradient.AddColorStop(0, "color(srgb 1 0 0)");
        gradient.AddColorStop(1, "color(srgb 0 1 0)");
        gradient.ColorInterpolationMethod = "hsl";
        Assert.Equal("shorter", gradient.HueInterpolationMethod);
        context.FillStyle = gradient;
        context.FillRect(0, 0, 100, 50);
        Assert.Equal([255, 255, 0, 255], GetPixel(context, 50, 25).Select(value => (byte)(value >= 245 ? 255 : value <= 10 ? 0 : value)).ToArray());

        gradient.HueInterpolationMethod = "longer";
        gradient.HueInterpolationMethod = "not a method";
        Assert.Equal("longer", gradient.HueInterpolationMethod);
        context.FillRect(0, 0, 100, 50);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 50, 25).Select(value => (byte)(value >= 245 ? 255 : value <= 10 ? 0 : value)).ToArray());
    }
#pragma warning restore WEBPLATEXP0019

#pragma warning disable WEBPLATEXP0017 // The canvas layers are a proposal
    [Fact]
    public void Layers_AreDrawnAsOneImageWithTheStateOfBeginLayer()
    {
        var (_, _, context) = Create(30, 10);
        context.FillStyle = "#00f";
        context.FillRect(0, 0, 30, 10);

        // The global alpha is reset in the layer and applies to the layer: the overlap of the two red rectangles is not darker
        context.GlobalAlpha = 0.5;
        context.BeginLayer();
        Assert.Equal(1, context.GlobalAlpha);
        context.FillStyle = "#f00";
        context.FillRect(0, 0, 20, 10);
        context.FillRect(10, 0, 20, 10);
        context.EndLayer();
        Assert.Equal(0.5, context.GlobalAlpha);

        Assert.Equal([128, 0, 128, 255], GetPixel(context, 5, 5));
        Assert.Equal(GetPixel(context, 5, 5), GetPixel(context, 15, 5));
        Assert.Equal(GetPixel(context, 5, 5), GetPixel(context, 25, 5));
    }

    [Fact]
    public void Layers_FilterResultsAreRoundedToPremultipliedEightBitColors()
    {
        var (_, _, context) = Create(200, 200);
        context.BeginLayer(new BeginLayerOptions { Filter = "blur(10px)" });
        context.FillStyle = "teal";
        context.FillRect(50, 50, 100, 100);
        context.EndLayer();

        Assert.Equal([0, 128, 128, 254], GetPixel(context, 100, 72));
        Assert.Equal([0, 129, 129, 254], GetPixel(context, 100, 73));
    }

    [Fact]
    public void Canvas_TranslucentPixelsArePaintedAsPremultipliedEightBitColors()
    {
        // The browsers hold the premultiplied components of the canvas: (0, 128, 128, 254) is (0, 127, 127, 254) premultiplied,
        // which gives 127 + 1 over white, where the straight color would give 128.498 (rounded to 129)
        var context = _pages.Load("<body style='margin: 0'><canvas id=c width=4 height=4 style='display: block'></canvas>", scriptingEnabled: true);
        var canvas = (HtmlCanvasElement)context.Document!.GetElementById("c")!;
        var context2D = (CanvasRenderingContext2D)canvas.GetContext("2d")!;
        context2D.FillStyle = "rgba(0, 128, 128, 0.996)";
        context2D.FillRect(0, 0, 4, 4);
        Assert.Equal([0, 128, 128, 254], GetPixel(context2D, 1, 1));
        using var screenshot = context.TakeScreenshot();
        Assert.Equal(new Rgba32(1, 128, 128, 255), screenshot.GetPixel(1, 1));
    }

    [Fact]
    public void Layers_ClearRectClearsTheLayer()
    {
        var (_, _, context) = Create(30, 10);
        context.FillStyle = "#00f";
        context.FillRect(0, 0, 30, 10);
        context.BeginLayer();
        context.GlobalCompositeOperation = "copy";
        context.FillStyle = "#f00";
        context.FillRect(0, 0, 20, 10);
        context.ClearRect(0, 0, 10, 10);
        context.EndLayer();

        // The copy operator and clearRect() replace the pixels of the layer, not the ones of the canvas
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));
        Assert.Equal([255, 0, 0, 255], GetPixel(context, 15, 5));
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 25, 5));
    }

    [Fact]
    public void Layers_ClipAndFilterApplyToTheLayer()
    {
        var (_, _, context) = Create(40, 10);

        // The clip region applies when the layer is drawn: the blur spreads the rectangle drawn outside of the clip into it
        context.Rect(20, 0, 20, 10);
        context.Clip();
        context.BeginLayer(new BeginLayerOptions { Filter = "blur(4px)" });
        context.FillStyle = "#000";
        context.FillRect(0, 0, 18, 10);
        context.EndLayer();

        Assert.Equal([0, 0, 0, 0], GetPixel(context, 10, 5));
        Assert.True(GetPixel(context, 21, 5)[3] > 0);

        // An invalid filter and the CanvasFilter objects are no filter
        context.Reset();
        context.BeginLayer(new BeginLayerOptions { Filter = "not a filter" });
        context.FillRect(0, 0, 10, 10);
        context.EndLayer();
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 11, 5));
    }

    [Fact]
    public void Layers_UnmatchedCallsAndPixelAccessThrow()
    {
        var (_, canvas, context) = Create(10, 10);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(context.EndLayer).Name);

        context.Save();
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(context.EndLayer).Name);
        context.Restore();

        context.BeginLayer();
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(context.Restore).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => context.GetImageData(0, 0, 1, 1)).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => context.PutImageData(new ImageData(1, 1), 0, 0)).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => canvas.ToDataUrl()).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => context.CreatePattern(canvas, "repeat")).Name);

        // A save() in the layer is restored before the layer ends
        context.Save();
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(context.EndLayer).Name);
        context.Restore();
        context.EndLayer();
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 0, 0));

        // reset() discards the open layers
        context.BeginLayer();
        context.FillRect(0, 0, 10, 10);
        context.Reset();
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 0, 0));
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(context.EndLayer).Name);
    }

    [Theory]
    [InlineData("source-over")]
    [InlineData("multiply")]
    public void Layers_NestedLayersWithShadowsDoNotCopyTheirContent(string compositeOperation)
    {
        var (_, _, context) = Create(20, 20);

        // Each layer is drawn with a shadow: the layer is added once to its parent (not once for the shadow and once for the
        // layer), so the display list does not double at each level
        for (var i = 0; i < 40; i++)
        {
            context.GlobalCompositeOperation = compositeOperation;
            context.ShadowColor = "#000";
            context.ShadowOffsetX = 1;
            context.BeginLayer();
        }

        context.FillStyle = "#f00";
        context.FillRect(0, 0, 4, 4);
        for (var i = 0; i < 40; i++)
        {
            context.EndLayer();
        }

        Assert.Equal([255, 0, 0, 255], GetPixel(context, 0, 0));
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 4, 0));
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 18, 0));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 18, 6));
    }

    [Fact]
    public void Layers_ShadowIncludesTheDrawingsThatTheFiltersMoveOntoTheCanvas()
    {
        var (_, _, context) = Create(20, 20);
        context.ShadowColor = "#000";
        context.ShadowOffsetX = 1;

        // The rectangle is outside of the canvas: the filter of the layer moves it onto the canvas, then the shadow extends it
        context.BeginLayer(new BeginLayerOptions { Filter = "drop-shadow(-100px 0 0 red)" });
        context.FillStyle = "#00f";
        context.FillRect(100, 0, 4, 4);
        context.EndLayer();

        Assert.Equal([255, 0, 0, 255], GetPixel(context, 0, 0));
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 4, 0));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 5, 0));
    }
#pragma warning restore WEBPLATEXP0017

#pragma warning disable WEBPLATEXP0021 // The CanvasFilter objects are a proposal
    [Fact]
    public void CanvasFilter_IsTheFilterOfTheContext()
    {
        var (_, _, context) = Create(30, 10);
        var filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "colorMatrix", ["type"] = "hueRotate", ["values"] = 180 });
        context.Filter = filter;
        Assert.Same(filter, context.Filter);

        // An invalid CSS filter does not replace the CanvasFilter
        context.Filter = "not a filter";
        Assert.Same(filter, context.Filter);

        context.FillStyle = "#f00";
        context.FillRect(0, 0, 10, 10);
        Assert.Equal([0, 109, 109, 255], GetPixel(context, 5, 5));

        context.Filter = "none";
        Assert.Equal("none", context.Filter);
        context.FillRect(10, 0, 10, 10);
        Assert.Equal([255, 0, 0, 255], GetPixel(context, 15, 5));
    }

    [Fact]
    public void CanvasFilter_AppliesThePrimitivesInOrder()
    {
        var (_, _, context) = Create(30, 10);

        // A shadow moved 10 pixels to the right, then the red component of the shape and of the shadow is halved
        context.Filter = new CanvasFilter(new object[]
        {
            new Dictionary<string, object?> { ["name"] = "dropShadow", ["dx"] = 10, ["dy"] = 0, ["stdDeviation"] = 0, ["floodColor"] = "#00f", ["floodOpacity"] = 0.5 },
            new Dictionary<string, object?>
            {
                ["name"] = "componentTransfer",
                ["funcR"] = new Dictionary<string, object?> { ["type"] = "linear", ["slope"] = 0.5, ["intercept"] = 0 },
            },
        });
        context.FillStyle = "#fff";
        context.FillRect(0, 0, 10, 10);

        Assert.Equal([128, 255, 255, 255], GetPixel(context, 5, 5));
        Assert.Equal([0, 0, 255, 128], GetPixel(context, 15, 5));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 25, 5));
    }

    [Fact]
    public void CanvasFilter_ConvertsTheValuesAsChromium()
    {
        var (_, _, context) = Create(40, 10);

        // A number of a pair is a number or a list of two numbers; null is 0 and the strings are converted to numbers
        context.Filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "gaussianBlur", ["stdDeviation"] = new object?[] { "2", null } });
        context.FillStyle = "#000";
        context.FillRect(10, 0, 20, 10);

        // The blur is horizontal only: the top edge of the rectangle is not blurred
        Assert.True(GetPixel(context, 8, 0)[3] > 0);
        Assert.Equal(255, GetPixel(context, 20, 0)[3]);

        // The primitives without a name, or with an unknown name, are ignored
        context.Reset();
        context.Filter = new CanvasFilter(new object[] { new Dictionary<string, object?>(), new Dictionary<string, object?> { ["name"] = "unknown" } });
        context.FillRect(0, 0, 10, 10);
        Assert.Equal([0, 0, 0, 255], GetPixel(context, 5, 5));
    }

    [Theory]
    [InlineData("gaussianBlur", "stdDeviation", null)]
    [InlineData("gaussianBlur", "stdDeviation", "foo")]
    [InlineData("gaussianBlur", "stdDeviation", double.NaN)]
    [InlineData("dropShadow", "dx", double.PositiveInfinity)]
    [InlineData("dropShadow", "floodColor", "not a color")]
    [InlineData("colorMatrix", "values", "foo")]
    [InlineData("colorMatrix", "type", "unknown")]
    [InlineData("turbulence", "baseFrequency", -1)]
    [InlineData("turbulence", "stitchTiles", "unknown")]
    [InlineData("convolveMatrix", "divisor", 2)]
    public void CanvasFilter_InvalidDescriptionsThrow(string name, string key, object? value)
    {
        var description = new Dictionary<string, object?> { ["name"] = name };
        if (value is not null)
        {
            description[key] = value;
        }

        Assert.Throws<TypeErrorException>(() => new CanvasFilter(description));
    }

    [Fact]
    public void CanvasFilter_ColorMatricesApplyInSrgbOnDisplayP3Canvases()
    {
        var (_, _, context) = Create(20, 10, new CanvasRenderingContext2DSettings { ColorSpace = PredefinedColorSpace.DisplayP3 });
        context.Filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "colorMatrix", ["type"] = "saturate", ["values"] = 0 });
        context.FillStyle = "#f00";
        context.FillRect(0, 0, 10, 10);

        // The gray of the sRGB red (0.213 * 255 = 54), read in sRGB
        var pixel = context.GetImageData(5, 5, 1, 1, new ImageDataSettings { ColorSpace = PredefinedColorSpace.Srgb }).Data.ToArray();
        Assert.All(pixel[..3], component => Assert.InRange(component, 53, 55));
    }

    [Fact]
    public void CanvasFilter_CyclicListsDoNotOverflowTheStack()
    {
        var (_, _, context) = Create(20, 10);
        var values = new List<object?>();
        values.Add(values);

        // A list that contains itself is converted once, as Array.prototype.join does: its string is "", which is the number 0
        context.Filter = new CanvasFilter(new object[]
        {
            new Dictionary<string, object?> { ["name"] = "gaussianBlur", ["stdDeviation"] = values },
            new Dictionary<string, object?> { ["name"] = "dropShadow", ["dx"] = values, ["dy"] = 0, ["stdDeviation"] = 0, ["floodColor"] = new object?[] { "red" } },
        });
        context.FillStyle = "#00f";
        context.FillRect(0, 0, 10, 10);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));
        Assert.Equal([0, 0, 0, 0], GetPixel(context, 15, 5));
    }

    [Fact]
    public void CanvasFilter_CoversTheDrawingsThatTheShadowMovesOntoTheCanvas()
    {
        var (_, _, context) = Create(20, 10);
        context.Filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "colorMatrix", ["values"] = new object[] { 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0 } });
        context.ShadowColor = "#00f";
        context.ShadowOffsetX = -100;
        context.FillStyle = "#f00";
        context.FillRect(100, 0, 10, 10);

        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));

        // The same with a primitive of a filter graph
        context.Reset();
        context.Filter = new CanvasFilter(new Dictionary<string, object?>
        {
            ["name"] = "componentTransfer",
            ["funcR"] = new Dictionary<string, object?> { ["type"] = "linear", ["slope"] = 0.5, ["intercept"] = 0 },
        });
        context.ShadowColor = "#00f";
        context.ShadowOffsetX = -100;
        context.FillRect(100, 0, 10, 10);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));

        // The same with a CSS filter
        context.Reset();
        context.Filter = "grayscale(0)";
        context.ShadowColor = "#00f";
        context.ShadowOffsetX = -100;
        context.FillRect(100, 0, 10, 10);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));
    }

    [Fact]
    public void CanvasFilter_ReadsTheListsOnce()
    {
        var (_, _, context) = Create(30, 10);
        context.Filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "dropShadow", ["dx"] = SinglePass(10), ["dy"] = 0, ["stdDeviation"] = 0, ["floodColor"] = "#00f" });
        context.FillStyle = "#f00";
        context.FillRect(0, 0, 10, 10);

        Assert.Equal([0, 0, 255, 255], GetPixel(context, 15, 5));

        static IEnumerable<object?> SinglePass(object value) => new SinglePassEnumerable(value);
    }

    /// <summary>A sequence that can be enumerated once: the next enumerations are empty.</summary>
    private sealed class SinglePassEnumerable(object value) : IEnumerable<object?>
    {
        private bool _enumerated;

        public IEnumerator<object?> GetEnumerator()
        {
            if (_enumerated)
                return Enumerable.Empty<object?>().GetEnumerator();

            _enumerated = true;
            return ((IEnumerable<object?>)[value]).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void CanvasFilter_IsNotADescription()
    {
        Assert.Throws<TypeErrorException>(() => new CanvasFilter(null));
        Assert.Throws<TypeErrorException>(() => new CanvasFilter("blur(2px)"));
        Assert.Throws<TypeErrorException>(() => new CanvasFilter(new Dictionary<string, object?>
        {
            ["name"] = "convolveMatrix",
            ["kernelMatrix"] = new object[] { new object[] { 1, 0 }, new object[] { 0 } },
        }));
    }

    [Fact]
    public void CanvasFilter_ConvolutionAndTurbulence()
    {
        var (_, _, context) = Create(20, 10);

        // An edge detection kernel: the inside of the rectangle is transparent, its edges are not
        context.Filter = new CanvasFilter(new Dictionary<string, object?>
        {
            ["name"] = "convolveMatrix",
            ["kernelMatrix"] = new object[] { new object[] { 0, -1, 0 }, new object[] { -1, 4, -1 }, new object[] { 0, -1, 0 } },
            ["divisor"] = 1,
        });
        context.FillStyle = "#000";
        context.FillRect(2, 2, 16, 6);
        Assert.Equal(0, GetPixel(context, 10, 5)[3]);
        Assert.True(GetPixel(context, 2, 5)[3] > 0);

        // The noise replaces the drawings in the filter region
        context.Reset();
        context.Filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "turbulence", ["baseFrequency"] = 0.2, ["numOctaves"] = 2, ["seed"] = 3 });
        context.FillRect(0, 0, 1, 1);
        var pixels = context.GetImageData(0, 0, 20, 10).Data.ToArray();
        Assert.Contains(pixels, value => value is > 0 and < 255);
    }

    [Fact]
    public void CanvasFilter_TurbulenceIsTheNoiseOfAnSvgFeTurbulenceElement()
    {
        var context = _pages.Load("<svg width=0 height=0><filter id=t><feTurbulence baseFrequency='0.05' numOctaves='2' seed='3'/></filter></svg><canvas id=a width=20 height=10></canvas><canvas id=b width=20 height=10></canvas>", scriptingEnabled: true);
        var filtered = (CanvasRenderingContext2D)((HtmlCanvasElement)context.Document!.GetElementById("a")!).GetContext("2d")!;
        var referenced = (CanvasRenderingContext2D)((HtmlCanvasElement)context.Document!.GetElementById("b")!).GetContext("2d")!;
        filtered.Filter = new CanvasFilter(new Dictionary<string, object?> { ["name"] = "turbulence", ["baseFrequency"] = 0.05, ["numOctaves"] = 2, ["seed"] = 3 });
        filtered.FillRect(0, 0, 1, 1);
        referenced.Filter = "url(#t)";
        referenced.FillRect(0, 0, 1, 1);
        Assert.Equal(referenced.GetImageData(0, 0, 20, 10).Data.ToArray(), filtered.GetImageData(0, 0, 20, 10).Data.ToArray());
    }

#pragma warning disable WEBPLATEXP0017 // The canvas layers are a proposal
    [Fact]
    public void CanvasFilter_IsTheFilterOfALayer()
    {
        var (_, _, context) = Create(30, 10);
        context.FillStyle = "#f00";

        // The description of a CanvasFilter applies to the layer, in the coordinates of its transformation matrix
        context.Translate(10, 0);
        context.BeginLayer(new BeginLayerOptions { Filter = new Dictionary<string, object?> { ["name"] = "dropShadow", ["dx"] = 10, ["dy"] = 0, ["stdDeviation"] = 0, ["floodColor"] = "#00f" } });
        context.FillRect(0, 0, 10, 10);
        context.EndLayer();
        Assert.Equal([255, 0, 0, 255], GetPixel(context, 15, 5));
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 25, 5));

        // An invalid description throws, and does not open the layer
        Assert.Throws<TypeErrorException>(() => context.BeginLayer(new BeginLayerOptions { Filter = new Dictionary<string, object?> { ["name"] = "gaussianBlur" } }));
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(context.EndLayer).Name);
    }
#pragma warning restore WEBPLATEXP0017

    [Fact]
    public void CanvasFilter_FromScripts()
    {
        using var engine = TestScriptEngines.CreateDisposable();
        var page = _pages.Load("""
            <canvas id=c width=20 height=10></canvas>
            <script>
            const ctx = document.getElementById('c').getContext('2d');
            const results = [];
            ctx.filter = new CanvasFilter([{name: 'colorMatrix', type: 'saturate', values: '0'}, {name: 'dropShadow', dx: [10], dy: null, stdDeviation: [0, false], floodColor: 'lime'}]);
            results.push(String(ctx.filter));
            ctx.fillStyle = '#f00';
            ctx.fillRect(0, 0, 10, 10);
            for (const description of [{name: 'gaussianBlur', stdDeviation: [1, 2, 3]}, {name: 'gaussianBlur', stdDeviation: undefined}, 'foo']) {
              try { new CanvasFilter(description); results.push('no error'); } catch (e) { results.push(e.name); }
            }
            document.title = results.join(',');
            </script>
            """, scriptEngine: engine);
        var canvas = (HtmlCanvasElement)page.Document!.GetElementById("c")!;
        var context = (CanvasRenderingContext2D)canvas.GetContext("2d")!;

        Assert.Equal("[object CanvasFilter],TypeError,TypeError,TypeError", page.Document.Title);
        Assert.Equal([54, 54, 54, 255], GetPixel(context, 5, 5));
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 15, 5));
    }
#pragma warning restore WEBPLATEXP0021

#pragma warning disable WEBPLATEXP0023
    [Fact]
    public void DrawElementImage_DrawsTheCurrentBitmapOfTheNestedCanvases()
    {
        var page = _pages.Load("""
            <canvas id=c width=20 height=10 content=drawable>
              <div drawable id=d style="width: 20px; height: 10px; background: red"><canvas id=n width=10 height=10 style="display: block"></canvas></div>
            </canvas>
            """, scriptingEnabled: true);
        var canvas = (HtmlCanvasElement)page.Document!.GetElementById("c")!;
        var nested = (HtmlCanvasElement)page.Document.GetElementById("n")!;
        var element = page.Document.GetElementById("d")!;
        var context = (CanvasRenderingContext2D)canvas.GetContext("2d")!;
        var events = new List<string>();
        canvas.OnPaint = e => events.Add(string.Join('+', ((CanvasPaintEvent)e).ChangedElements.Select(changed => changed.Id)));

        void Render()
        {
            _pages.Clock.Advance(TimeSpan.FromMilliseconds(20));
            page.EventLoop.RunUntilIdle();
        }

        // (the canvases are rendered when scripting is enabled)
        Render();
        Assert.Equal(["d"], events);

        // The snapshot was recorded before the nested canvas had a context: it draws its current bitmap
        var nestedContext = (CanvasRenderingContext2D)nested.GetContext("2d")!;
        nestedContext.FillStyle = "blue";
        nestedContext.FillRect(0, 0, 10, 10);
        context.DrawElementImage(element, 0, 0);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));
        Assert.Equal([255, 0, 0, 255], GetPixel(context, 15, 5));

        // A change of the bitmap changes the snapshot once
        Render();
        Render();
        Assert.Equal(["d", "d"], events);

        // An element image draws the bitmap of the nested canvas when it was captured
        var image = canvas.CaptureElementImage(element);
        nestedContext.FillStyle = "lime";
        nestedContext.FillRect(0, 0, 10, 10);
        context.DrawElementImage(image, 0, 0);
        Assert.Equal([0, 0, 255, 255], GetPixel(context, 5, 5));
        context.DrawElementImage(element, 0, 0);
        Assert.Equal([0, 255, 0, 255], GetPixel(context, 5, 5));
    }
#pragma warning restore WEBPLATEXP0023
}
