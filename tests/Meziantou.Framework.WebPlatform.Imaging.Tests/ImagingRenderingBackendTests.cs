using System.Numerics;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>The services of the Imaging backend: the surfaces, the rasterization of the display list operations, the image decoding.</summary>
public sealed class ImagingRenderingBackendTests
{
    private static readonly DisplayColor Red = DisplayColor.FromRgb(255, 0, 0);
    private static readonly DisplayColor Blue = DisplayColor.FromRgb(0, 0, 255);

    private readonly ImagingRenderingBackend _backend = new();

    private RenderingSurface CreateSurface(int width = 20, int height = 20, RenderingSurfaceOptions? options = null)
        => _backend.CreateSurface(width, height, options ?? RenderingSurfaceOptions.Default) ?? throw new InvalidOperationException("No surface");

    private static DisplayList List(params DisplayItem[] items) => new(items, new PhysicalSize(20, 20));

    private static byte[] ReadPixel(RenderingSurface surface, int x, int y)
    {
        var pixel = new byte[4];
        surface.ReadPixels(x, y, 1, 1, pixel);
        return pixel;
    }

    [Fact]
    public void Surface_InitialPixels()
    {
        using var transparent = CreateSurface();
        Assert.Equal([0, 0, 0, 0], ReadPixel(transparent, 5, 5));

        using var opaque = CreateSurface(options: new RenderingSurfaceOptions(HasAlpha: false));
        Assert.Equal([0, 0, 0, 255], ReadPixel(opaque, 5, 5));

        Assert.Null(_backend.CreateSurface(0, 10, RenderingSurfaceOptions.Default));
        Assert.Null(_backend.CreateSurface(100_000, 100_000, RenderingSurfaceOptions.Default));
    }

    [Fact]
    public void Surface_ReadWritePixels()
    {
        using var surface = CreateSurface();
        surface.WritePixels(2, 3, 2, 1, [255, 0, 0, 255, 0, 0, 255, 128]);
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 2, 3));
        Assert.Equal([0, 0, 255, 128], ReadPixel(surface, 3, 3));

        // The pixels outside of the surface are transparent black
        var pixels = new byte[8];
        pixels.AsSpan().Fill(7);
        surface.ReadPixels(-1, 3, 2, 1, pixels);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0], pixels);

        surface.Clear(Blue);
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 2, 3));

        using var snapshot = surface.CreateSnapshot();
        surface.Clear(DisplayColor.Transparent);
        var snapshotPixels = new byte[20 * 20 * 4];
        snapshot.ReadPixels(snapshotPixels);
        Assert.Equal([0, 0, 255, 255], snapshotPixels.AsSpan(0, 4).ToArray());
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 0, 0));
    }

    [Fact]
    public void Draw_RectsClipsAndLayers()
    {
        using var surface = CreateSurface();
        surface.Draw(List(
            new DrawRectItem(new PhysicalRect(0, 0, 10, 10), new DisplayPaint(Red)),
            SaveItem.Instance,
            new ClipRectItem(new PhysicalRect(10, 0, 5, 20)),
            new DrawPaintItem(new DisplayPaint(Blue)),
            RestoreItem.Instance,
            new SaveLayerItem(0.5, BlendMode.Normal, Filters: null),
            new DrawRectItem(new PhysicalRect(0, 10, 10, 10), new DisplayPaint(Red)),
            new DrawRectItem(new PhysicalRect(0, 10, 5, 10), new DisplayPaint(Blue)),
            RestoreItem.Instance));

        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 5, 5));
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 12, 18));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 17, 5));

        // The layer is composited as a group: the blue rect replaces the red one below it in the layer
        Assert.Equal([0, 0, 255, 128], ReadPixel(surface, 2, 15), PixelComparer);
        Assert.Equal([255, 0, 0, 128], ReadPixel(surface, 7, 15), PixelComparer);
    }

    [Fact]
    public void Draw_LayerInClip_DrawingNothingInTheClip()
    {
        // A group whose drawings are all outside of its clip (or that draws nothing) changes no pixel
        using var surface = CreateSurface();
        surface.Draw(List(
            SaveItem.Instance,
            new ClipRectItem(new PhysicalRect(0, 0, 10, 10), AntiAlias: true),
            new SaveLayerItem(0.5, BlendMode.Normal, Filters: null),
            new DrawRectItem(new PhysicalRect(0, 12, 10, 5), new DisplayPaint(Red)),
            RestoreItem.Instance,
            new SaveLayerItem(1, BlendMode.Normal, Filters: null),
            RestoreItem.Instance,
            RestoreItem.Instance,
            new DrawRectItem(new PhysicalRect(15, 15, 5, 5), new DisplayPaint(Blue))));

        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 5, 14));
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 17, 17));
    }

    [Fact]
    public void Draw_ShadowOfADrawingFarOutsideOfTheSurface()
    {
        // The drawing API draws the items of the filtered group twice (on the layer, and on the window of a shadow cast from
        // beyond its margin)
        using var surface = CreateSurface(width: 100, height: 100);
        surface.Draw(List(
            new SaveLayerItem(1, BlendMode.Normal, [new DropShadowFilter(new PhysicalOffset(1000, 1000), 0, Blue)]),
            new DrawRectItem(new PhysicalRect(-1000, -1000, 50, 50), new DisplayPaint(Red)),
            RestoreItem.Instance,
            new DrawRectItem(new PhysicalRect(80, 80, 10, 10), new DisplayPaint(Red))));

        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 25, 25));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 60, 60));
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 85, 85));
    }

    [Fact]
    public void Draw_ScopeDrawnAgainInNestedScopes()
    {
        // The filtered group is drawn twice by the drawing API (the window of the far shadow), inside a transform, a clip that is
        // not antialiased and a layer: each scope draws its items once, and the items after them are drawn
        using var surface = CreateSurface(width: 100, height: 100);
        surface.Draw(List(
            SaveItem.Instance,
            new TransformItem(DisplayMatrix.Translation(10, 10)),
            new ClipRectItem(new PhysicalRect(-10, -10, 100, 100)),
            new SaveLayerItem(0.5, BlendMode.Normal, Filters: null),
            new SaveLayerItem(1, BlendMode.Normal, [new DropShadowFilter(new PhysicalOffset(1000, 1000), 0, Blue)]),
            new DrawRectItem(new PhysicalRect(-1000, -1000, 20, 20), new DisplayPaint(Red)),
            RestoreItem.Instance,
            new DrawRectItem(new PhysicalRect(40, 0, 10, 10), new DisplayPaint(Red)),
            RestoreItem.Instance,
            new DrawRectItem(new PhysicalRect(60, 0, 10, 10), new DisplayPaint(Blue)),
            RestoreItem.Instance,
            new DrawRectItem(new PhysicalRect(80, 80, 10, 10), new DisplayPaint(Red))));

        Assert.Equal([0, 0, 255, 128], ReadPixel(surface, 15, 15), PixelComparer); // the shadow, in the layer at half opacity
        Assert.Equal([255, 0, 0, 128], ReadPixel(surface, 55, 15), PixelComparer); // after the filtered group, in the layer
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 75, 15)); // after the layer, in the transform
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 85, 85)); // after the scopes, without the transform
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 95, 15));
    }

    [Fact]
    public void Draw_Transforms()
    {
        using var surface = CreateSurface();

        // A 3D matrix (column-major) whose only effect is a translation by (10, 5)
        double[] translate3d = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 5, 0, 1];
        surface.Draw(List(
            SaveItem.Instance,
            new TransformItem(DisplayMatrix.FromMatrix4x4(translate3d) with { Matrix3d = translate3d }),
            new DrawRectItem(new PhysicalRect(0, 0, 2, 2), new DisplayPaint(Red)),
            RestoreItem.Instance,
            new TransformItem(DisplayMatrix.Scale(2, 2)),
            new DrawRectItem(new PhysicalRect(0, 5, 2, 2), new DisplayPaint(Blue))));

        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 11, 6));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 1, 1));
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 3, 13));

        // A perspective: the points far from the viewer are closer to the origin
        double[] perspective = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -0.05, 0, 0, 0, 1];
        surface.Clear(DisplayColor.Transparent);
        surface.Draw(List(
            new TransformItem(new DisplayMatrix(1, 0, 0, 1, 0, 0) { Matrix3d = perspective }),
            new DrawRectItem(new PhysicalRect(0, 0, 10, 10), new DisplayPaint(Red))));
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 5, 5));
    }

    [Fact]
    public void Draw_StrokesPathsAndRings()
    {
        using var surface = CreateSurface();
        var path = new DisplayPath { FillRule = FillRule.EvenOdd };
        path.AddRect(new PhysicalRect(0, 0, 10, 10));
        path.AddRect(new PhysicalRect(2, 2, 6, 6));
        var corner = (10d, 10d);
        surface.Draw(List(
            new DrawPathItem(path, new DisplayPaint(Red)),
            new DrawLineItem(new PhysicalOffset(10, 15), new PhysicalOffset(20, 15), DisplayPaint.ForStroke(Blue, new StrokeStyle(2))),
            new DrawRingItem(new RoundedRect(new PhysicalRect(10, 0, 10, 10), default), new RoundedRect(new PhysicalRect(12, 2, 6, 6), default), new DisplayPaint(Blue)),
            new DrawRoundedRectItem(new RoundedRect(new PhysicalRect(0, 10, 10, 10), new BorderRadii(corner, corner, corner, corner)), new DisplayPaint(Red))));

        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 1, 1));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 5, 5));
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 15, 15));
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 11, 5));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 15, 5));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 0, 10)); // outside of the rounded corner
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 5, 15));
    }

    [Fact]
    public void Draw_Gradients()
    {
        using var surface = CreateSurface(width: 100, height: 10);
        GradientStop[] stops = [new(0, Red), new(1, Blue)];
        surface.Draw(List(new DrawRectItem(new PhysicalRect(0, 0, 100, 10), new DisplayPaint(DisplayColor.Black) { Shader = new LinearGradientShader(new PhysicalOffset(0, 0), new PhysicalOffset(100, 0), stops) })));

        var start = ReadPixel(surface, 0, 5);
        var middle = ReadPixel(surface, 50, 5);
        Assert.True(start[0] > 250 && start[2] < 5);
        Assert.InRange(middle[0], 120, 135);
        Assert.InRange(middle[2], 120, 135);
    }

    [Fact]
    public void GradientStops_InterpolatedPremultiplied()
    {
        // From transparent to red: the color stays red (premultiplied interpolation), only the alpha changes
        var gradient = new LinearGradientShader(default, new PhysicalOffset(10, 0), [new(0, DisplayColor.Transparent), new(1, Red)]);
        Assert.Equal([255, 0, 0, 140], DrawGradient(gradient)[5], PixelComparer);

        // Not premultiplied: the color of the transparent stop (black) is interpolated too
        Assert.Equal([140, 0, 0, 140], DrawGradient(gradient with { InterpolatesPremultiplied = false })[5], PixelComparer);

        // The offsets are clamped and monotonic (a hard stop at 0.5), and a single stop is a solid color
        var hardStop = DrawGradient(new LinearGradientShader(default, new PhysicalOffset(10, 0), [new(0.5, Red), new(0.2, Blue), new(2, Blue)]));
        Assert.Equal([255, 0, 0, 255], hardStop[4]);
        Assert.Equal([0, 0, 255, 255], hardStop[5]);
        Assert.All(DrawGradient(new LinearGradientShader(default, new PhysicalOffset(10, 0), [new(0.5, Red)])), color => Assert.Equal([255, 0, 0, 255], color));

        byte[][] DrawGradient(LinearGradientShader shader)
        {
            using var surface = CreateSurface(width: 10, height: 1);
            surface.Draw(List(new DrawRectItem(new PhysicalRect(0, 0, 10, 1), new DisplayPaint(DisplayColor.Black) { Shader = shader })));
            return [.. Enumerable.Range(0, 10).Select(x => ReadPixel(surface, x, 0))];
        }
    }

    [Fact]
    public void Draw_Filters()
    {
        using var surface = CreateSurface();

        // grayscale(1) of red
        float[] grayscale = [0.2126f, 0.7152f, 0.0722f, 0, 0, 0.2126f, 0.7152f, 0.0722f, 0, 0, 0.2126f, 0.7152f, 0.0722f, 0, 0, 0, 0, 0, 1, 0];
        surface.Draw(List(
            new SaveLayerItem(1, BlendMode.Normal, [new ColorMatrixFilter(grayscale)]),
            new DrawRectItem(new PhysicalRect(0, 0, 10, 10), new DisplayPaint(Red)),
            RestoreItem.Instance,
            new SaveLayerItem(1, BlendMode.Normal, [new DropShadowFilter(new PhysicalOffset(5, 5), 0, Blue)]),
            new DrawRectItem(new PhysicalRect(10, 10, 5, 5), new DisplayPaint(Red)),
            RestoreItem.Instance));

        Assert.Equal([54, 54, 54, 255], ReadPixel(surface, 5, 5), PixelComparer);
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 12, 12));
        Assert.Equal([0, 0, 255, 255], ReadPixel(surface, 17, 17));
    }

    [Fact]
    public void Draw_BlendModes()
    {
        using var surface = CreateSurface();
        surface.Draw(List(
            new DrawRectItem(new PhysicalRect(0, 0, 10, 10), new DisplayPaint(DisplayColor.FromRgb(255, 255, 0))),
            new DrawRectItem(new PhysicalRect(0, 0, 10, 10), new DisplayPaint(DisplayColor.FromRgb(0, 255, 255)) { BlendMode = BlendMode.Multiply }),
            new DrawRectItem(new PhysicalRect(10, 0, 10, 10), new DisplayPaint(Red)),
            new DrawRectItem(new PhysicalRect(10, 0, 10, 10), new DisplayPaint(Blue) { BlendMode = BlendMode.DestinationOver })));

        Assert.Equal([0, 255, 0, 255], ReadPixel(surface, 5, 5));
        Assert.Equal([255, 0, 0, 255], ReadPixel(surface, 15, 5));
    }

    [Fact]
    public void DecodeImage()
    {
        var png = TestPages.CreatePng(2, 1, Colors.Red, new Rgba32(0, 0, 255, 128));
        using var image = _backend.DecodeImage(png, "image/png", applyOrientation: true);
        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        var pixels = new byte[8];
        image.ReadPixels(pixels);
        Assert.Equal([255, 0, 0, 255, 0, 0, 255, 128], pixels, PixelComparer);

        // The format is sniffed; the SVG images and the data that is not an image are not decoded
        using var sniffed = _backend.DecodeImage(png, mimeType: null, applyOrientation: true);
        Assert.NotNull(sniffed);
        Assert.Null(_backend.DecodeImage("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>"u8.ToArray(), "image/svg+xml", applyOrientation: true));
        Assert.Null(_backend.DecodeImage("not an image"u8.ToArray(), "image/png", applyOrientation: true));
    }

    [Fact]
    public void DecodeImage_ExifOrientation()
    {
        // A 2x1 JPEG (red, blue) with the EXIF orientation 6: rotated 90 degrees clockwise, it is 1x2 (red above blue)
        var jpeg = CreateJpegWithOrientation(6);
        using (var oriented = _backend.DecodeImage(jpeg, "image/jpeg", applyOrientation: true))
        {
            Assert.NotNull(oriented);
            Assert.Equal(1 * 8, oriented.Width);
            Assert.Equal(2 * 8, oriented.Height);
            var pixels = new byte[oriented.Width * oriented.Height * 4];
            oriented.ReadPixels(pixels);
            Assert.True(pixels[0] > 200 && pixels[2] < 60, "red at the top");
            var last = pixels.Length - 4;
            Assert.True(pixels[last] < 60 && pixels[last + 2] > 200, "blue at the bottom");
        }

        using var raw = _backend.DecodeImage(jpeg, "image/jpeg", applyOrientation: false);
        Assert.NotNull(raw);
        Assert.Equal(2 * 8, raw.Width);
        Assert.Equal(1 * 8, raw.Height);
    }

    [Fact]
    public void EncodeImage_Jpeg()
    {
        // A 16x16 bitmap: the left half is opaque red, the right half is transparent green
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < 16 * 16; i++)
        {
            byte[] color = i % 16 < 8 ? [255, 0, 0, 255] : [0, 255, 0, 0];
            color.CopyTo(pixels, i * 4);
        }

        var jpeg = _backend.EncodeImage(16, 16, pixels, "image/jpeg", quality: null);
        Assert.NotNull(jpeg);
        Assert.Equal([0xFF, 0xD8, 0xFF], jpeg[..3]);
        using (var decoded = Image.Load<Rgba32>(jpeg))
        {
            // A JPEG image has no alpha channel: the pixels are composited over opaque black
            Assert.Equal((16, 16), (decoded.Width, decoded.Height));
            var red = decoded.GetPixel(2, 8);
            Assert.True(red is { R: > 230, G: < 30, B: < 30, A: 255 }, $"The pixel (2, 8) is {red}");
            var black = decoded.GetPixel(13, 8);
            Assert.True(black is { R: < 30, G: < 30, B: < 30, A: 255 }, $"The pixel (13, 8) is {black}");
        }

        // The chroma is subsampled (4:2:0): the sampling factors of the first component (the luma) of the frame header are 2x2
        var frameHeader = jpeg.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0xC0]);
        Assert.True(frameHeader > 0);
        Assert.Equal(0x22, jpeg[frameHeader + 11]);

        // The default quality is 0.92, also for a quality out of the range
        Assert.Equal(jpeg, _backend.EncodeImage(16, 16, pixels, "image/jpeg", quality: 0.92));
        Assert.Equal(jpeg, _backend.EncodeImage(16, 16, pixels, "image/jpeg", quality: 2));
        Assert.Equal(jpeg, _backend.EncodeImage(16, 16, pixels, "image/jpeg", quality: -0.5));
        Assert.Equal(jpeg, _backend.EncodeImage(16, 16, pixels, "image/jpeg", quality: double.NaN));

        // A lower quality gives a smaller file
        var noise = CreateNoise(32, 32, opaque: true);
        var low = _backend.EncodeImage(32, 32, noise, "image/jpeg", quality: 0);
        var medium = _backend.EncodeImage(32, 32, noise, "image/jpeg", quality: 0.5);
        var high = _backend.EncodeImage(32, 32, noise, "image/jpeg", quality: 1);
        Assert.NotNull(low);
        Assert.NotNull(medium);
        Assert.NotNull(high);
        Assert.True(low.Length < medium.Length && medium.Length < high.Length, $"{low.Length} < {medium.Length} < {high.Length}");
    }

    [Fact]
    public void EncodeImage_WebP()
    {
        // The quality 1 is lossless: the pixels are the same, also the transparent and the translucent ones
        var pixels = CreateNoise(16, 16, opaque: false);
        var lossless = _backend.EncodeImage(16, 16, pixels, "image/webp", quality: 1);
        Assert.NotNull(lossless);
        Assert.True(lossless.AsSpan(0, 4).SequenceEqual("RIFF"u8) && lossless.AsSpan(8, 4).SequenceEqual("WEBP"u8));
        Assert.True(lossless.AsSpan().IndexOf("VP8L"u8) > 0);
        using (var decoded = Image.Load<Rgba32>(lossless))
        {
            Assert.Equal((16, 16), (decoded.Width, decoded.Height));
            Assert.Equal(pixels, ScreenshotAssert.GetPixels(decoded));
        }

        // The default quality is 0.8 (lossy), also for a quality out of the range
        var lossy = _backend.EncodeImage(16, 16, pixels, "image/webp", quality: null);
        Assert.NotNull(lossy);
        Assert.True(lossy.AsSpan().IndexOf("VP8 "u8) > 0);
        Assert.Equal(lossy, _backend.EncodeImage(16, 16, pixels, "image/webp", quality: 0.8));
        Assert.Equal(lossy, _backend.EncodeImage(16, 16, pixels, "image/webp", quality: 1.5));
        Assert.Equal(lossy, _backend.EncodeImage(16, 16, pixels, "image/webp", quality: -1));
        using (var decoded = Image.Load<Rgba32>(lossy))
        {
            // The colors are approximated, the alpha channel is not
            Assert.Equal((16, 16), (decoded.Width, decoded.Height));
            var decodedPixels = ScreenshotAssert.GetPixels(decoded);
            Assert.NotEqual(pixels, decodedPixels);
            for (var i = 3; i < pixels.Length; i += 4)
            {
                Assert.Equal(pixels[i], decodedPixels[i]);
            }
        }

        var low = _backend.EncodeImage(16, 16, pixels, "image/webp", quality: 0);
        Assert.NotNull(low);
        Assert.True(low.Length < lossy.Length, $"{low.Length} < {lossy.Length}");
    }

    [Fact]
    public void EncodeImage_OtherTypesAndSizes()
    {
        // The library encodes the PNG images itself; the other types are not supported
        byte[] pixel = [255, 0, 0, 255];
        Assert.Null(_backend.EncodeImage(1, 1, pixel, "image/png", quality: null));
        Assert.Null(_backend.EncodeImage(1, 1, pixel, "image/gif", quality: null));
        Assert.Null(_backend.EncodeImage(1, 1, pixel, "image/avif", quality: 0.5));
        Assert.Null(_backend.EncodeImage(0, 1, pixel, "image/jpeg", quality: null));
        Assert.Null(_backend.EncodeImage(2, 1, pixel, "image/jpeg", quality: null));
        Assert.NotNull(_backend.EncodeImage(1, 1, pixel, "image/jpeg", quality: null));
        Assert.NotNull(_backend.EncodeImage(1, 1, pixel, "image/webp", quality: null));

        // An image larger than the largest image of the format is not encoded (16,383 pixels per side for a lossy WebP image, 65,535 for JPEG)
        Assert.Null(_backend.EncodeImage(16384, 1, new byte[16384 * 4], "image/webp", quality: null));
        Assert.NotNull(_backend.EncodeImage(16384, 1, new byte[16384 * 4], "image/jpeg", quality: null));
        Assert.Null(_backend.EncodeImage(65536, 1, new byte[65536 * 4], "image/jpeg", quality: null));
    }

    /// <summary>Creates pixels (RGBA) that do not compress well, the same at each run; when they are not opaque, some are transparent and some are translucent.</summary>
    private static byte[] CreateNoise(int width, int height, bool opaque)
    {
        var pixels = new byte[width * height * 4];
        var seed = 2463534242u;
        for (var i = 0; i < pixels.Length; i++)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            pixels[i] = (byte)(seed >> 8);
            if (i % 4 == 3)
            {
                pixels[i] = opaque ? (byte)255 : (i / 4 % 3) switch { 0 => (byte)0, 1 => pixels[i], _ => (byte)255 };
            }
        }

        return pixels;
    }

    /// <summary>A font of the tests of the glyph outlines of the core library (tests/Meziantou.Framework.WebPlatform.Tests/Css/Fonts/Resources/Outlines).</summary>
    /// <summary>Loads a font of the test data (fonts of WPT: COLR-palettes-test-font.ttf, and a subset of Noto Color Emoji under the OFL).</summary>
    private static Css.Fonts.FontFile LoadTestFont(string name, [System.Runtime.CompilerServices.CallerFilePath] string path = "")
    {
        var file = Path.Combine(Path.GetDirectoryName(path)!, "TestData", name);
        return Css.Fonts.FontFile.Load(File.ReadAllBytes(file)) ?? throw new InvalidOperationException(name + " is invalid");
    }

    private static HashSet<(byte R, byte G, byte B)> ReadOpaqueColors(RenderingSurface surface)
    {
        var pixels = new byte[surface.Width * surface.Height * 4];
        surface.ReadPixels(0, 0, surface.Width, surface.Height, pixels);
        var colors = new HashSet<(byte, byte, byte)>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 255)
            {
                colors.Add((pixels[i], pixels[i + 1], pixels[i + 2]));
            }
        }

        return colors;
    }

    [Fact]
    public void Draw_ColorGlyphLayers()
    {
        // The glyph 5 of the font has 2 layers (COLR version 0): a glyph in black and a glyph in red (the first palette); the
        // glyphs of the font are the ones of Ahem (not antialiased)
        var file = LoadTestFont("COLR-palettes-test-font.ttf");
        using var surface = CreateSurface(width: 40, height: 40);
        var run = new GlyphRun(new DisplayFont(file, 0, 20, [], SyntheticBold: false, SyntheticObliqueAngle: 0), [5], [new PhysicalOffset(10, 30)], "A");
        surface.Draw(List(new DrawGlyphsItem(run, new DisplayPaint(Blue))));

        var colors = ReadOpaqueColors(surface);
        Assert.Contains(((byte)255, (byte)0, (byte)0), colors);
        Assert.Contains(((byte)0, (byte)0, (byte)0), colors);
        Assert.DoesNotContain(((byte)0, (byte)0, (byte)255), colors);

        // A glyph without layers is painted in the color of the text
        using var plain = CreateSurface(width: 40, height: 40);
        plain.Draw(List(new DrawGlyphsItem(run with { Glyphs = [81] }, new DisplayPaint(Blue))));
        Assert.Equal([((byte)0, (byte)0, (byte)255)], ReadOpaqueColors(plain));
    }

    [Fact]
    public void Draw_BitmapGlyphs()
    {
        // The glyphs 2 to 6 of the font are PNG images of 136x128 pixels for 109 pixels per em, whose top is 101 pixels above
        // the baseline (CBDT): at 32px, an image of about 40x38 pixels from 30 pixels above the baseline
        var file = LoadTestFont("NotoColorEmoji-subset.ttf");
        using var surface = CreateSurface(width: 60, height: 60);
        var run = new GlyphRun(new DisplayFont(file, 0, 32, [], SyntheticBold: false, SyntheticObliqueAngle: 0), [2], [new PhysicalOffset(10, 40)], "\U0001F600");
        surface.Draw(List(new DrawGlyphsItem(run, new DisplayPaint(Blue))));

        // The image has its own colors (more than the color of the text), in its rectangle only
        Assert.True(ReadOpaqueColors(surface).Count > 8);
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 5, 30));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 30, 5));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 55, 30));
        Assert.Equal([0, 0, 0, 0], ReadPixel(surface, 30, 52));
        var alpha = ReadPixel(surface, 30, 28)[3];
        Assert.True(alpha > 128);

        // The alpha of the color of the text applies to the image
        using var translucent = CreateSurface(width: 60, height: 60);
        translucent.Draw(List(new DrawGlyphsItem(run, new DisplayPaint(Blue with { A = 0.5f }))));
        Assert.InRange(ReadPixel(translucent, 30, 28)[3], (alpha / 2) - 2, (alpha / 2) + 2);
    }

    [Fact]
    public void Fonts_AreCached()
    {
        var font = Css.Fonts.BundledFonts.Ahem.Font;
        var displayFont = new DisplayFont(font.File, font.FaceIndex, 10, [], SyntheticBold: false, SyntheticObliqueAngle: 0);
        var imagingFont = _backend.Fonts.Get(displayFont);
        Assert.NotNull(imagingFont);
        Assert.Same(imagingFont, _backend.Fonts.Get(displayFont with { Size = 20 }));
        Assert.Equal("Ahem", imagingFont.FamilyName);
        Assert.Null(_backend.Fonts.Get(displayFont with { FaceIndex = 1 }));
    }

    private static readonly Func<byte[], byte[], bool> PixelComparer = (expected, actual) => expected.Length == actual.Length && expected.Zip(actual).All(pair => Math.Abs(pair.First - pair.Second) <= 2);

    /// <summary>Encodes a 16x8 JPEG (the left half red, the right half blue) with an EXIF APP1 segment that sets its orientation.</summary>
    internal static byte[] CreateJpegWithOrientation(ushort orientation)
    {
        var pixels = new byte[16 * 8 * 4];
        for (var i = 0; i < 16 * 8; i++)
        {
            var color = i % 16 < 8 ? Colors.Red : Colors.Blue;
            pixels[i * 4] = color.R;
            pixels[(i * 4) + 1] = color.G;
            pixels[(i * 4) + 2] = color.B;
            pixels[(i * 4) + 3] = color.A;
        }

        using var bitmap = Image.ImportPixelBytes<Rgba32>(pixels, 16, 8, 16 * 4);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new Meziantou.Framework.Imaging.Formats.JpegEncoder { Quality = 100 });
        var jpeg = stream.ToArray();

        // APP1 "Exif\0\0" + a big-endian TIFF header + an IFD with the Orientation tag (0x0112, SHORT, 1 value)
        byte[] tiff = [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0, 0, 0, 0, 0];
        byte[] exifHeader = [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0];
        var length = 2 + exifHeader.Length + tiff.Length;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exifHeader, .. tiff];
        return [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)];
    }
}
