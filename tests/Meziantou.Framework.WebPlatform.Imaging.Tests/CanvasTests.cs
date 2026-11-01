using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
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
}
