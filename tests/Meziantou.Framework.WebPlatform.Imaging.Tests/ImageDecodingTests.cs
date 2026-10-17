using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.Imaging;
using ImagingFormats = Meziantou.Framework.Imaging.Formats;
using ImagingMetadata = Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>
/// The image decoding through the Imaging backend (#378): the formats, the animated images, the orientation, the color
/// profiles, the incomplete and the corrupted images, the bitmaps.
/// </summary>
public sealed class ImageDecodingTests : IDisposable
{
    // A non-looping 2x2 GIF of 3 frames (WPT gif/reset-no-gce.gif): red (200ms), transparent (200ms), green (no graphic control
    // extension). The image data of its frames has an invalid code after the code of their last pixel, instead of the end code.
    private static readonly byte[] ResetGifOfWpt = Convert.FromBase64String("R0lGODlhAgACAPEAAAAAAP8AAAD/AAAA/yH5BAkUAAAALAAAAAACAAIAAAIDjKMAACH5BAkUAAIALAAAAAACAAIAAAIDlKUAACwAAAAAAgACAAACA5SlAAA7");

    private readonly ImagingRenderingBackend _backend = new();
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    /// <summary>Decodes a frame of an image, or returns null when the image cannot be opened or decoded.</summary>
    private (int Width, int Height, byte[] Pixels)? Decode(byte[] data, int frame = 0, ExifOrientation orientation = ExifOrientation.TopLeft, bool convertToSrgb = true)
    {
        using var decoder = _backend.CreateImageDecoder(data, mimeType: null);
        if (decoder is null)
            return null;

        using var image = decoder.DecodeFrame(frame, orientation, convertToSrgb);
        if (image is null)
            return null;

        var pixels = new byte[image.Width * image.Height * 4];
        image.ReadPixels(pixels);
        return (image.Width, image.Height, pixels);
    }

    private static byte[] DecodePixels(RenderingImageDecoder decoder, int frame, ExifOrientation orientation = ExifOrientation.TopLeft)
    {
        using var image = decoder.DecodeFrame(frame, orientation, convertToSrgb: true) ?? throw new InvalidOperationException("Not decoded");
        var pixels = new byte[image.Width * image.Height * 4];
        image.ReadPixels(pixels);
        return pixels;
    }

    [Fact]
    public void Gif_FramesAreComposited()
    {
        // The frames of the image of WPT have data after their last pixel (an invalid code instead of the end code): the
        // decoding stops at the last pixel, as in the browsers
        using var decoder = _backend.CreateImageDecoder(ResetGifOfWpt, "image/gif");
        Assert.NotNull(decoder);
        Assert.Equal(3, decoder.FrameCount);
        Assert.Equal(0, decoder.RepetitionCount);
        Assert.Equal(200, decoder.GetFrameDuration(0));
        Assert.Equal([255, 0, 0, 255], DecodePixels(decoder, 0)[..4]);
        Assert.Equal([0, 0, 0, 0], DecodePixels(decoder, 1)[..4]);

        // The last frame has no graphic control extension: its green is not transparent
        Assert.Equal([0, 255, 0, 255], DecodePixels(decoder, 2)[..4]);
        Assert.Equal([255, 0, 0, 255], DecodePixels(decoder, 0)[..4]);
    }

    [Fact]
    public void Gif_AnimationFollowsTheClockOfTheEventLoop()
    {
        _pages.Resources["/a.gif"] = (ResetGifOfWpt, "image/gif");
        var context = _pages.Load("<img src='/a.gif' style='width: 20px; height: 20px; image-rendering: pixelated'>");
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Red, screenshot.GetPixel(5, 5));
        }

        // The second frame is transparent, the third one (the last one: the animation is played once) is green
        _pages.Clock.Advance(TimeSpan.FromMilliseconds(250));
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.White, screenshot.GetPixel(5, 5));
        }

        _pages.Clock.Advance(TimeSpan.FromSeconds(10));
        using (var screenshot = context.TakeScreenshot())
        {
            Assert.Equal(Colors.Lime, screenshot.GetPixel(5, 5));
        }
    }

    [Fact]
    public void Apng_FramesAreCompositedWithTheirDisposalAndBlending()
    {
        // A 2x1 canvas: the default image (red, red) is the first frame; the second frame paints its right pixel blue over it,
        // then is disposed to the background; the third frame paints a half-transparent green over the left pixel
        var apng = CreateApng(2, 1, plays: 1,
            new ApngTestFrame(0, 0, 2, 1, [Colors.Red, Colors.Red], 100, ApngDisposeOp.None, ApngBlendOp.Source),
            new ApngTestFrame(1, 0, 1, 1, [Colors.Blue], 100, ApngDisposeOp.Background, ApngBlendOp.Source),
            new ApngTestFrame(0, 0, 1, 1, [new Rgba32(0, 255, 0, 128)], 100, ApngDisposeOp.None, ApngBlendOp.Over));

        using var decoder = _backend.CreateImageDecoder(apng, "image/png");
        Assert.NotNull(decoder);
        Assert.Equal(3, decoder.FrameCount);
        Assert.Equal(0, decoder.RepetitionCount);
        Assert.Equal(100, decoder.GetFrameDuration(1));
        Assert.Equal([255, 0, 0, 255, 255, 0, 0, 255], DecodePixels(decoder, 0));
        Assert.Equal([255, 0, 0, 255, 0, 0, 255, 255], DecodePixels(decoder, 1));
        var last = DecodePixels(decoder, 2);
        Assert.InRange(last[0], 125, 129);
        Assert.InRange(last[1], 126, 130);
        Assert.Equal([0, 0, 0, 0], last[4..]);

        // A frame decoded again after a later one
        Assert.Equal([255, 0, 0, 255, 0, 0, 255, 255], DecodePixels(decoder, 1));
    }

    [Fact]
    public void Apng_InvalidAnimationIsAStillImage()
    {
        var apng = CreateApng(1, 1, plays: 0, new ApngTestFrame(0, 0, 1, 1, [Colors.Red], 100, ApngDisposeOp.None, ApngBlendOp.Source), new ApngTestFrame(0, 0, 1, 1, [Colors.Blue], 100, ApngDisposeOp.None, ApngBlendOp.Source));

        // The number of frames of the animation control chunk does not match the frames: the default image is displayed
        BinaryPrimitives.WriteUInt32BigEndian(apng.AsSpan(8 + 25 + 8), 3);
        FixCrc(apng, 8 + 25);
        using var decoder = _backend.CreateImageDecoder(apng, "image/png");
        Assert.NotNull(decoder);
        Assert.Equal(1, decoder.FrameCount);
        Assert.Equal([255, 0, 0, 255], DecodePixels(decoder, 0));
    }

    [Fact]
    public void Png_ExifOrientation()
    {
        // A 2x1 PNG (red, blue) with an eXIf chunk before its image data (orientation 6: rotated by 90 degrees clockwise)
        var png = InsertPngChunk(TestPages.CreatePng(2, 1, Colors.Red, Colors.Blue), "eXIf", [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0, 0, 0, 0]);
        Assert.Equal(ExifOrientation.RightTop, ImageMetadata.GetExifOrientation(png));
        using var image = _backend.DecodeImage(png, "image/png", applyOrientation: true);
        Assert.NotNull(image);
        Assert.Equal(1, image.Width);
        Assert.Equal(2, image.Height);
        var pixels = new byte[8];
        image.ReadPixels(pixels);
        Assert.Equal([255, 0, 0, 255, 0, 0, 255, 255], pixels);

        // The natural size of an img element is rotated, and image-orientation: none paints the encoded pixels
        _pages.Resources["/a.png"] = (png, "image/png");
        var context = _pages.Load("<img id=a src='/a.png' style='display: block; image-rendering: pixelated; width: 20px'><img id=b src='/a.png' style='display: block; image-orientation: none; image-rendering: pixelated; width: 20px'>");
        var a = (HtmlImageElement)context.Document!.GetElementById("a")!;
        Assert.Equal(1u, a.NaturalWidth);
        Assert.Equal(2u, a.NaturalHeight);
        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Red, screenshot.GetPixel(10, 5));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(10, 35));
        Assert.Equal(Colors.Red, screenshot.GetPixel(5, 45));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(15, 45));
    }

    [Fact]
    public void Png_ChunksOfInvalidTypeAreIgnored()
    {
        var png = InsertPngChunk(TestPages.CreatePng(1, 1, Colors.Lime), "s\u0001IT", [1, 2, 3]);
        using var image = _backend.DecodeImage(png, "image/png", applyOrientation: true);
        Assert.NotNull(image);
        var pixels = new byte[4];
        image.ReadPixels(pixels);
        Assert.Equal([0, 255, 0, 255], pixels);
    }

    [Fact]
    public void Img_HeaderTheBackendCannotReadIsBroken()
    {
        // A PNG signature followed by garbage: the header parser of the library sees a PNG, the decoder of the backend does not
        byte[] corrupted = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. "this is not a PNG image, its header is corrupted"u8];
        _pages.Resources["/a.png"] = (corrupted, "image/png");
        var context = _pages.Load("<img id=a src='/a.png' alt=''>");
        var image = (HtmlImageElement)context.Document!.GetElementById("a")!;
        Assert.True(image.Complete);
        Assert.Equal(0u, image.NaturalWidth);

        // Without backend, only the header is read: the image is available (its header has the type of the first chunk)
        byte[] header = [.. TestPages.CreatePng(1, 1, Colors.Lime).AsSpan(0, 33), .. "and then garbage"u8];
        _pages.Resources["/b.png"] = (header, "image/png");
        var withoutBackend = _pages.Load("<img id=a src='/a.png' alt=''><img id=b src='/b.png' alt=''>", backend: RenderingBackendKind.None);
        Assert.Equal(0u, ((HtmlImageElement)withoutBackend.Document!.GetElementById("a")!).NaturalWidth);
        Assert.Equal(1u, ((HtmlImageElement)withoutBackend.Document!.GetElementById("b")!).NaturalWidth);
    }

    [Fact]
    public void Img_DecodeIsRejectedWhenThePixelsCannotBeDecoded()
    {
        // WPT images/undecodable.png: a valid header (100x50) and an empty compressed stream: the image is available, but its
        // pixels cannot be decoded
        var undecodable = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAGQAAAAyCAYAAACqNX6+AAAAAElEQVQ1rwYeAAAAAElFTkSuQmCC");
        _pages.Resources["/undecodable.png"] = (undecodable, "image/png");
        _pages.Resources["/a.png"] = (TestPages.CreatePng(1, 1, Colors.Lime), "image/png");
        var context = _pages.Load("<img id=a src='/undecodable.png'><img id=b src='/a.png'>");
        var undecodableImage = (HtmlImageElement)context.Document!.GetElementById("a")!;
        Assert.Equal(100u, undecodableImage.NaturalWidth);

        var failed = undecodableImage.Decode();
        var succeeded = ((HtmlImageElement)context.Document.GetElementById("b")!).Decode();
        context.EventLoop.RunUntilIdle();
        var exception = Assert.IsType<DomException>(failed.Exception?.InnerException);
        Assert.Equal("EncodingError", exception.Name);
        Assert.True(succeeded.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CreateImageBitmap_FromImagesAndBlobs()
    {
        var jpeg = ImagingRenderingBackendTests.CreateJpegWithOrientation(6);
        _pages.Resources["/a.jpg"] = (jpeg, "image/jpeg");
        var context = _pages.Load("<img id=a src='/a.jpg'>");
        var window = context.Document!.DefaultView!;
        var image = (HtmlImageElement)context.Document.GetElementById("a")!;

        // The EXIF orientation is applied (16x8 rotated: 8x16), then the bitmap is cropped and flipped
        var fromImage = window.CreateImageBitmap(image);
        var cropped = window.CreateImageBitmap(image, 0, 0, 8, 8, new ImageBitmapOptions { ImageOrientation = ImageBitmapOrientation.FlipY });
        var fromBlob = window.CreateImageBitmap(new Blob([jpeg]), new ImageBitmapOptions { ResizeWidth = 4 });
        var notAnImage = window.CreateImageBitmap(new Blob(["not an image"]));
        Assert.False(fromImage.IsCompleted); // resolved in a task
        context.EventLoop.RunUntilIdle();

        var fromImageBitmap = await fromImage;
        Assert.Equal((8u, 16u), (fromImageBitmap.Width, fromImageBitmap.Height));
        var pixels = fromImageBitmap.Bitmap!.Rgba;
        Assert.True(pixels[0] > 200 && pixels[2] < 60, "red at the top");
        Assert.True(pixels[^4] < 60 && pixels[^2] > 200, "blue at the bottom");

        // The top 8x8 square is red: flipped, it is still red
        var croppedBitmap = await cropped;
        Assert.Equal((8u, 8u), (croppedBitmap.Width, croppedBitmap.Height));
        Assert.True(croppedBitmap.Bitmap!.Rgba[0] > 200);

        // The resize width keeps the aspect ratio
        var fromBlobBitmap = await fromBlob;
        Assert.Equal((4u, 8u), (fromBlobBitmap.Width, fromBlobBitmap.Height));
        Assert.Equal("InvalidStateError", Assert.IsType<DomException>(notAnImage.Exception?.InnerException).Name);
    }

    [Fact]
    public async Task BitmapRenderer_PaintsTheTransferredBitmap()
    {
        // The canvas is a replaced element when scripting is enabled
        var context = _pages.Load("<canvas id=c width=2 height=2 style='display: block; width: 20px; height: 20px; image-rendering: pixelated'></canvas>", scriptingEnabled: true);
        var canvas = (HtmlCanvasElement)context.Document!.GetElementById("c")!;
        var renderer = Assert.IsType<ImageBitmapRenderingContext>(canvas.GetContext("bitmaprenderer"));
        Assert.Same(renderer, canvas.GetContext("bitmaprenderer"));
        Assert.Null(canvas.GetContext("2d"));

        // A 2x2 bitmap: red, lime / blue, transparent
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0];
        var bitmapTask = context.Document.DefaultView!.CreateImageBitmap(new ImageData(rgba, 2));
        context.EventLoop.RunUntilIdle();
        var bitmap = await bitmapTask;
        renderer.TransferFromImageBitmap(bitmap);
        Assert.Equal(0u, bitmap.Width); // detached
        Assert.Throws<DomException>(() => renderer.TransferFromImageBitmap(bitmap));

        using var screenshot = context.TakeScreenshot();
        Assert.Equal(Colors.Red, screenshot.GetPixel(5, 5));
        Assert.Equal(Colors.Lime, screenshot.GetPixel(15, 5));
        Assert.Equal(Colors.Blue, screenshot.GetPixel(5, 15));
        Assert.Equal(Colors.White, screenshot.GetPixel(15, 15));

        // transferFromImageBitmap(null) clears the canvas
        renderer.TransferFromImageBitmap(null);
        using var cleared = context.TakeScreenshot();
        Assert.Equal(Colors.White, cleared.GetPixel(5, 5));
    }

    [Theory]
    [InlineData("png", 0)]
    [InlineData("png-interlaced", 0)]
    [InlineData("jpeg", 4)]
    [InlineData("gif", 0)]
    [InlineData("webp", 0)]
    [InlineData("webp-lossy", 12)]
    [InlineData("avif", 0)]
    [InlineData("avif-lossy", 12)]
    [InlineData("bmp", 0)]
    [InlineData("ico", 0)]
    [InlineData("cur", 0)]
    public void Formats_AreDecoded(string format, int tolerance)
    {
        var data = CreateTestImage(format);
        using var decoder = _backend.CreateImageDecoder(data, mimeType: null);
        Assert.NotNull(decoder);
        Assert.Equal((16, 16), (decoder.Width, decoder.Height));
        Assert.Equal(1, decoder.FrameCount);
        Assert.Equal(0, decoder.RepetitionCount);
        Assert.Equal(0, decoder.GetFrameDuration(0));
        var pixels = DecodePixels(decoder, 0);
        AssertPixel(Colors.Red, pixels, 16, 2, 8, tolerance);
        AssertPixel(Colors.Blue, pixels, 16, 13, 8, tolerance);

        // The image can be decoded again
        Assert.Equal(pixels, DecodePixels(decoder, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.DecodeFrame(1, ExifOrientation.TopLeft, convertToSrgb: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.DecodeFrame(-1, ExifOrientation.TopLeft, convertToSrgb: true));
    }

    [Fact]
    public void Format_IsSniffed()
    {
        // The MIME type is ignored
        var jpeg = CreateTestImage("jpeg");
        using var decoder = _backend.CreateImageDecoder(jpeg, "image/png");
        Assert.NotNull(decoder);
        Assert.NotNull(Decode(jpeg));
    }

    [Fact]
    public void Jpeg_Cmyk_IsConvertedToSrgb()
    {
        // A CMYK JPEG image of WPT (html/canvas/element/manual/wide-gamut-canvas/resources): the color of the
        // cyan ink at 100% (FF000000), converted to sRGB with the CMYK profile of the file
        var data = System.IO.File.ReadAllBytes(Path.Combine(GetTestDataDirectory(), "Generic-CMYK-FF000000.jpg"));
        using var decoder = _backend.CreateImageDecoder(data, mimeType: null);
        Assert.NotNull(decoder);
        var pixels = DecodePixels(decoder, 0);
        var (r, g, b, a) = (pixels[0], pixels[1], pixels[2], pixels[3]);

        // A cyan: no red, much green and blue, opaque
        Assert.Equal(255, a);
        Assert.True(r < 64 && g > 128 && b > 192, $"({r}, {g}, {b})");
        Assert.True(pixels.AsSpan(0, 4).SequenceEqual(pixels.AsSpan(pixels.Length - 4)));
    }

    private static string GetTestDataDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "TestData");

    [Theory]
    [InlineData("qoi")]
    [InlineData("tiff")]
    [InlineData("tga")]
    [InlineData("pnm")]
    [InlineData("ani")]
    public void Formats_ThatAreNotTheOnesOfTheWebAreNotDecoded(string format)
    {
        // The codecs decode these formats, the browsers do not
        var data = CreateTestImage(format);
        using (var image = Image.Load(data))
        {
            Assert.Equal(16, image.Width);
        }

        Assert.Null(_backend.CreateImageDecoder(data, mimeType: null));
        Assert.Null(_backend.DecodeImage(data, mimeType: null, applyOrientation: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Icon_LargestRepresentationIsDecoded(bool cursor)
    {
        using var representations = ImageCollection.Create(ImageCollectionKind.Representations);
        foreach (var (size, color) in ((int, Rgba32)[])[(16, Colors.Red), (32, Colors.Blue), (24, Colors.Lime)])
        {
            using var representation = new Image<Rgba32>(size, size, color);
            representations.Add(representation);
        }

        using var stream = new MemoryStream();
        representations.Save(stream, new ImagingFormats.IcoEncoder { Kind = cursor ? ImagingFormats.IconKind.Cursor : ImagingFormats.IconKind.Icon });
        var decoded = Decode(stream.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal((32, 32), (decoded.Value.Width, decoded.Value.Height));
        AssertPixel(Colors.Blue, decoded.Value.Pixels, 32, 16, 16);
    }

    [Theory]
    [InlineData("gif", null, -1)]
    [InlineData("gif", 1, 0)]
    [InlineData("gif", 3, 2)]
    [InlineData("apng", null, -1)]
    [InlineData("apng", 1, 0)]
    [InlineData("apng", 3, 2)]
    [InlineData("webp", null, -1)]
    [InlineData("webp", 1, 0)]
    [InlineData("webp", 3, 2)]
    [InlineData("avif", null, -1)]
    [InlineData("avif", 1, 0)]
    [InlineData("avif", 3, 2)]
    public void Animation_FramesDurationsAndRepetitions(string format, int? totalPlays, int expectedRepetitions)
    {
        // 3 frames (red, blue, lime) played a number of times: the repetitions do not count the first time
        using var decoder = _backend.CreateImageDecoder(CreateAnimation(format, totalPlays), mimeType: null);
        Assert.NotNull(decoder);
        Assert.Equal((4, 4), (decoder.Width, decoder.Height));
        Assert.Equal(3, decoder.FrameCount);
        Assert.Equal(expectedRepetitions, decoder.RepetitionCount);
        Assert.Equal([100, 200, 30], [decoder.GetFrameDuration(0), decoder.GetFrameDuration(1), decoder.GetFrameDuration(2)]);

        // The frames in order, a frame again, earlier frames, a frame after the next one
        Rgba32[] colors = [Colors.Red, Colors.Blue, Colors.Lime];
        foreach (var index in (int[])[0, 1, 2, 2, 1, 0, 2, 0, 0, 1])
        {
            var pixels = DecodePixels(decoder, index);
            AssertPixel(colors[index], pixels, 4, 0, 0);
            AssertPixel(colors[index], pixels, 4, 3, 3);
        }

        // The orientation is applied to the frames
        using var oriented = decoder.DecodeFrame(1, ExifOrientation.RightTop, convertToSrgb: true);
        Assert.NotNull(oriented);
        Assert.Equal((4, 4), (oriented.Width, oriented.Height));
    }

    [Fact]
    public void Animation_OfOneFrameIsAStillImage()
    {
        using var image = new Image<Rgba32>(4, 4, Colors.Red);
        image.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);
        image.Animation = new ImagingMetadata.AnimationMetadata();
        foreach (var data in (byte[][])[Encode(image, new ImagingFormats.GifEncoder()), Encode(image, new ImagingFormats.WebPEncoder())])
        {
            using var decoder = _backend.CreateImageDecoder(data, mimeType: null);
            Assert.NotNull(decoder);
            Assert.Equal(1, decoder.FrameCount);
            Assert.Equal(0, decoder.RepetitionCount);
            Assert.Equal(0, decoder.GetFrameDuration(0));
            AssertPixel(Colors.Red, DecodePixels(decoder, 0), 4, 1, 1);
        }
    }

    [Fact]
    public void Apng_DefaultImageThatIsNotAFrameIsNotDisplayed()
    {
        // The default image (yellow) is not part of the animation: the first frame is red
        using var decoder = _backend.CreateImageDecoder(CreateAnimation("apng", totalPlays: null, poster: Colors.Yellow), "image/png");
        Assert.NotNull(decoder);
        Assert.Equal(3, decoder.FrameCount);
        Assert.Equal([100, 200, 30], [decoder.GetFrameDuration(0), decoder.GetFrameDuration(1), decoder.GetFrameDuration(2)]);
        AssertPixel(Colors.Red, DecodePixels(decoder, 0), 4, 1, 1);
        AssertPixel(Colors.Lime, DecodePixels(decoder, 2), 4, 1, 1);
        AssertPixel(Colors.Blue, DecodePixels(decoder, 1), 4, 1, 1);
    }

    [Fact]
    public void Apng_FrameControlBeforeTheAnimationControl()
    {
        // As in the browsers, the frame control chunk of the default image may precede the animation control chunk (WPT png/apng/support/024.png)
        var apng = CreateApng(1, 1, plays: 1, new ApngTestFrame(0, 0, 1, 1, [Colors.Red], 100, ApngDisposeOp.None, ApngBlendOp.Source), new ApngTestFrame(0, 0, 1, 1, [Colors.Lime], 200, ApngDisposeOp.None, ApngBlendOp.Source));
        const int AfterHeader = 8 + 25;
        const int AnimationControlLength = 12 + 8;
        const int FrameControlLength = 12 + 26;
        byte[] reordered = [.. apng.AsSpan(0, AfterHeader), .. apng.AsSpan(AfterHeader + AnimationControlLength, FrameControlLength), .. apng.AsSpan(AfterHeader, AnimationControlLength), .. apng.AsSpan(AfterHeader + AnimationControlLength + FrameControlLength)];
        using var decoder = _backend.CreateImageDecoder(reordered, "image/png");
        Assert.NotNull(decoder);
        Assert.Equal(2, decoder.FrameCount);
        Assert.Equal(0, decoder.RepetitionCount);
        Assert.Equal(200, decoder.GetFrameDuration(1));
        Assert.Equal([255, 0, 0, 255], DecodePixels(decoder, 0));
        Assert.Equal([0, 255, 0, 255], DecodePixels(decoder, 1));
    }

    [Theory]
    [InlineData(1, 3, "ABCDEF")]
    [InlineData(2, 3, "CBAFED")]
    [InlineData(3, 3, "FEDCBA")]
    [InlineData(4, 3, "DEFABC")]
    [InlineData(5, 2, "ADBECF")]
    [InlineData(6, 2, "DAEBFC")]
    [InlineData(7, 2, "FCEBDA")]
    [InlineData(8, 2, "CFBEAD")]
    public void Orientation_IsAppliedToThePixels(int orientation, int expectedWidth, string expectedPixels)
    {
        // A 3x2 image whose pixels are A B C / D E F
        Rgba32[] colors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Cyan, Colors.Magenta];
        var png = TestPages.CreatePng(3, 2, colors);
        var decoded = Decode(png, orientation: (ExifOrientation)orientation);
        Assert.NotNull(decoded);
        Assert.Equal((expectedWidth, 6 / expectedWidth), (decoded.Value.Width, decoded.Value.Height));
        for (var i = 0; i < expectedPixels.Length; i++)
        {
            AssertPixel(colors[expectedPixels[i] - 'A'], decoded.Value.Pixels, expectedWidth, i % expectedWidth, i / expectedWidth);
        }
    }

    [Fact]
    public void Orientation_OfTheImageIsNotAppliedByTheDecoder()
    {
        // The orientation is the one the caller gives, not the one of the metadata of the image
        var jpeg = ImagingRenderingBackendTests.CreateJpegWithOrientation(6);
        var decoded = Decode(jpeg);
        Assert.NotNull(decoded);
        Assert.Equal((16, 8), (decoded.Value.Width, decoded.Value.Height));
        var oriented = Decode(jpeg, orientation: ExifOrientation.BottomRight);
        Assert.NotNull(oriented);
        Assert.Equal((16, 8), (oriented.Value.Width, oriented.Value.Height));
        AssertPixel(Colors.Blue, oriented.Value.Pixels, 16, 2, 4, tolerance: 60);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public void ColorProfile_IsConvertedToSrgb(string format)
    {
        // The red and the green primaries of the profile are the green and the red primaries of sRGB: its red is the green of sRGB
        using var image = new Image<Rgba32>(8, 8, Colors.Red);
        image.Metadata.IccProfile = new ImagingMetadata.IccProfile(new ImagingMetadata.MetadataBlob(CreateColorProfile(grayscale: false, gamma: 2.2)));
        var data = format switch
        {
            "png" => Encode(image, new ImagingFormats.PngEncoder()),
            "jpeg" => Encode(image, new ImagingFormats.JpegEncoder { Quality = 100 }),
            _ => Encode(image, new ImagingFormats.WebPEncoder()),
        };

        var converted = Decode(data);
        Assert.NotNull(converted);
        AssertPixel(Colors.Lime, converted.Value.Pixels, 8, 4, 4, tolerance: 3);

        // Without conversion (colorSpaceConversion: "none"), the encoded values are kept
        var encoded = Decode(data, convertToSrgb: false);
        Assert.NotNull(encoded);
        AssertPixel(Colors.Red, encoded.Value.Pixels, 8, 4, 4, tolerance: 3);

        using var decodedImage = _backend.DecodeImage(data, mimeType: null, applyOrientation: true);
        Assert.NotNull(decodedImage);
        var pixels = new byte[8 * 8 * 4];
        decodedImage.ReadPixels(pixels);
        AssertPixel(Colors.Lime, pixels, 8, 0, 0, tolerance: 3);
    }

    [Fact]
    public void ColorProfile_OfSrgbChangesNoPixel()
    {
        // Most of the images that have a profile have the one of sRGB: their pixels are the encoded ones
        using var image = CreateGradient(64, 64);
        image.Metadata.IccProfile = ImagingMetadata.IccProfile.Srgb;
        var data = Encode(image, new ImagingFormats.PngEncoder());
        Assert.True(data.AsSpan().IndexOf("iCCP"u8) > 0);
        var converted = Decode(data);
        Assert.NotNull(converted);
        Assert.Equal(ScreenshotAssert.GetPixels(image), converted.Value.Pixels);
    }

    [Fact]
    public void ColorProfile_OfAGrayscaleImage()
    {
        // The gray of the profile is linear: its middle gray is lighter in sRGB
        using var image = new Image<Gray8>(8, 8, new Gray8(128));
        image.Metadata.IccProfile = new ImagingMetadata.IccProfile(new ImagingMetadata.MetadataBlob(CreateColorProfile(grayscale: true, gamma: 1)));
        foreach (var data in (byte[][])[Encode(image, new ImagingFormats.PngEncoder()), Encode(image, new ImagingFormats.JpegEncoder { Quality = 100 })])
        {
            var converted = Decode(data);
            Assert.NotNull(converted);
            AssertPixel(new Rgba32(188, 188, 188, 255), converted.Value.Pixels, 8, 4, 4, tolerance: 2);
            var encoded = Decode(data, convertToSrgb: false);
            Assert.NotNull(encoded);
            AssertPixel(new Rgba32(128, 128, 128, 255), encoded.Value.Pixels, 8, 4, 4, tolerance: 2);
        }
    }

    [Fact]
    public void ColorProfile_OfSamplesOf16BitsIsConvertedBeforeTheyAreReduced()
    {
        // A linear profile: the dark red 300/65535 is a dark green of sRGB, which is not the one of 1/255, its value at 8 bits
        var profile = new ImagingMetadata.IccProfile(new ImagingMetadata.MetadataBlob(CreateColorProfile(grayscale: false, gamma: 1)));
        var transform = IccColorTransform.Create(profile, ImagingMetadata.IccProfile.Srgb);
        ushort[] wide = [300, 0, 0];
        byte[] narrow = [1, 0, 0];
        transform.Convert(wide, wide);
        transform.Convert(narrow, narrow);
        var expectedGreen = (byte)Math.Round(wide[1] / 257.0, MidpointRounding.AwayFromZero);
        Assert.InRange(expectedGreen, 14, 16);
        Assert.NotEqual(expectedGreen, narrow[1]);

        using var image = new Image<Rgba64>(4, 4, new Rgba64(300, 0, 0, 32768));
        image.Metadata.IccProfile = profile;
        var data = Encode(image, new ImagingFormats.PngEncoder());
        var converted = Decode(data);
        Assert.NotNull(converted);
        AssertPixel(new Rgba32(0, expectedGreen, 0, 128), converted.Value.Pixels, 4, 2, 2);
        var encoded = Decode(data, convertToSrgb: false);
        Assert.NotNull(encoded);
        AssertPixel(new Rgba32(1, 0, 0, 128), encoded.Value.Pixels, 4, 2, 2);
    }

    [Fact]
    public void ColorProfile_ThatIsInvalidIsIgnored()
    {
        // An iCCP chunk whose profile is not a profile, and a profile that is not the one of the color model of the image
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(new byte[200]);
        }

        var png = TestPages.CreatePng(1, 1, Colors.Red);
        var invalid = Decode(InsertPngChunk(png, "iCCP", [.. "bad"u8, 0, 0, .. compressed.ToArray()]));
        Assert.NotNull(invalid);
        Assert.Equal([255, 0, 0, 255], invalid.Value.Pixels);

        using var profile = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(profile, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(CreateColorProfile(grayscale: true, gamma: 1));
        }

        var mismatched = Decode(InsertPngChunk(png, "iCCP", [.. "gray"u8, 0, 0, .. profile.ToArray()]));
        Assert.NotNull(mismatched);
        Assert.Equal([255, 0, 0, 255], mismatched.Value.Pixels);
    }

    [Fact]
    public void Png_GammaAndChromaticitiesAreIgnored()
    {
        // As in the browsers, the colors of a PNG image without ICC profile are sRGB, whatever its gAMA and cHRM chunks
        var png = TestPages.CreatePng(1, 1, Colors.Gray);
        png = InsertPngChunk(png, "cHRM", [0, 0, 0x7A, 0x26, 0, 0, 0x80, 0x84, 0, 0, 0xFA, 0, 0, 0, 0x80, 0xE8, 0, 0, 0x75, 0x30, 0, 0, 0xEA, 0x60, 0, 0, 0x3A, 0x98, 0, 0, 0x17, 0x70]);
        png = InsertPngChunk(png, "gAMA", [0, 1, 0x86, 0xA0]);
        var decoded = Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal([128, 128, 128, 255], decoded.Value.Pixels);
    }

    [Fact]
    public void Png_ChunksWhoseChecksumIsWrong()
    {
        // As libpng, an ancillary chunk whose checksum is wrong is ignored...
        var png = TestPages.CreatePng(1, 1, Colors.Lime);
        var text = InsertPngChunk(png, "tEXt", [.. "Title"u8, 0, .. "x"u8]);
        text[8 + 25 + 12 + 7 - 1] ^= 0xFF;
        var decoded = Decode(text);
        Assert.NotNull(decoded);
        Assert.Equal([0, 255, 0, 255], decoded.Value.Pixels);

        // ... except the ICC profile, which is used
        using var image = new Image<Rgba32>(2, 2, Colors.Red);
        image.Metadata.IccProfile = new ImagingMetadata.IccProfile(new ImagingMetadata.MetadataBlob(CreateColorProfile(grayscale: false, gamma: 2.2)));
        var profile = Encode(image, new ImagingFormats.PngEncoder());
        var chunk = profile.AsSpan().IndexOf("iCCP"u8) - 4;
        Assert.True(chunk > 0);
        profile[chunk + 8 + BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(chunk))] ^= 0xFF;
        Assert.ThrowsAny<ImageException>(() => Image.Identify(profile));
        var converted = Decode(profile);
        Assert.NotNull(converted);
        AssertPixel(Colors.Lime, converted.Value.Pixels, 2, 1, 1, tolerance: 3);

        // A critical chunk whose checksum is wrong is an error: the header cannot be read
        var header = (byte[])png.Clone();
        header[8 + 25 - 1] ^= 0xFF;
        Assert.Null(_backend.CreateImageDecoder(header, "image/png"));
    }

    [Fact]
    public void Png_TransparentColorWithBitsAboveTheBitDepth()
    {
        // The bits of the samples of the tRNS chunk above the 8 bits of the samples of the image are ignored (WPT
        // png/support/trns-high-bits-set.png): the red pixel is transparent, the blue one is not
        var png = PngEncoder.Encode(2, 1, [255, 0, 0, 255, 0, 0, 255, 255], hasAlpha: false);
        var decoded = Decode(InsertPngChunk(png, "tRNS", [0xFF, 0xFF, 0x12, 0x00, 0xAB, 0x00]));
        Assert.NotNull(decoded);
        Assert.Equal(0, decoded.Value.Pixels[3]);
        Assert.Equal([0, 0, 255, 255], decoded.Value.Pixels[4..]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Truncated_PngIsDecodedUpToItsLastRow(bool interlaced)
    {
        using var image = CreateGradient(64, 64);
        var png = Encode(image, new ImagingFormats.PngEncoder { Interlaced = interlaced });
        var complete = Decode(png);
        var truncated = Decode(png[..(png.Length / 2)]);
        Assert.NotNull(complete);
        Assert.NotNull(truncated);
        Assert.Equal((64, 64), (truncated.Value.Width, truncated.Value.Height));

        // The pixels that are decoded are the ones of the image, the other ones are transparent
        var decodedPixels = 0;
        for (var i = 0; i < truncated.Value.Pixels.Length; i += 4)
        {
            var pixel = truncated.Value.Pixels.AsSpan(i, 4);
            if (pixel.SequenceEqual(complete.Value.Pixels.AsSpan(i, 4)))
            {
                decodedPixels++;
            }
            else
            {
                Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(pixel));
            }
        }

        Assert.InRange(decodedPixels, 64, (64 * 64) - 64);
        if (!interlaced)
        {
            // The first rows are decoded
            var rows = CountRows(truncated.Value);
            Assert.Equal(rows * 64, decodedPixels);
            Assert.True(truncated.Value.Pixels.AsSpan(0, rows * 64 * 4).SequenceEqual(complete.Value.Pixels.AsSpan(0, rows * 64 * 4)));
        }
    }

    [Fact]
    public void Truncated_JpegIsDecodedUpToItsLastRows()
    {
        using var image = CreateGradient(256, 256);
        var jpeg = Encode(image, new ImagingFormats.JpegEncoder { Quality = 90 });
        var complete = Decode(jpeg);
        var truncated = Decode(jpeg[..(jpeg.Length / 2)]);
        Assert.NotNull(complete);
        Assert.NotNull(truncated);

        // The first rows are decoded, the other ones are transparent
        var rows = CountRows(truncated.Value);
        Assert.InRange(rows, 16, 240);
        Assert.True(truncated.Value.Pixels.AsSpan(0, (rows - 1) * 256 * 4).SequenceEqual(complete.Value.Pixels.AsSpan(0, (rows - 1) * 256 * 4)));
        Assert.False(truncated.Value.Pixels.AsSpan(rows * 256 * 4).ContainsAnyExcept((byte)0));
    }

    [Fact]
    public void Truncated_BmpIsDecodedUpToItsLastRow()
    {
        // The rows of a BMP image are stored from the bottom
        using var image = CreateGradient(32, 32);
        var bmp = Encode(image, new ImagingFormats.BmpEncoder());
        var complete = Decode(bmp);
        var truncated = Decode(bmp[..(bmp.Length / 2)]);
        Assert.NotNull(complete);
        Assert.NotNull(truncated);
        var rows = CountRows(truncated.Value);
        Assert.InRange(rows, 1, 31);
        Assert.True(truncated.Value.Pixels.AsSpan((32 - rows) * 32 * 4).SequenceEqual(complete.Value.Pixels.AsSpan((32 - rows) * 32 * 4)));
        Assert.False(truncated.Value.Pixels.AsSpan(0, (32 - rows) * 32 * 4).ContainsAnyExcept((byte)0));
    }

    [Fact]
    public void Truncated_TransparentRowsAreDecodedRows()
    {
        // The image is transparent black and its data is not compressed: its first rows are decoded (and transparent)
        using var image = new Image<Rgba32>(64, 64);
        var png = Encode(image, new ImagingFormats.PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression });
        var truncated = Decode(png[..(png.Length / 2)]);
        Assert.NotNull(truncated);
        Assert.False(truncated.Value.Pixels.AsSpan().ContainsAnyExcept((byte)0));

        // An image of which no row can be decoded cannot be decoded (WPT images/undecodable.png: an empty compressed stream)
        var undecodable = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAGQAAAAyCAYAAACqNX6+AAAAAElEQVQ1rwYeAAAAAElFTkSuQmCC");
        using var decoder = _backend.CreateImageDecoder(undecodable, "image/png");
        Assert.NotNull(decoder);
        Assert.Equal((100, 50), (decoder.Width, decoder.Height));
        Assert.Null(decoder.DecodeFrame(0, ExifOrientation.TopLeft, convertToSrgb: true));
        Assert.Null(decoder.DecodeFrame(0, ExifOrientation.RightTop, convertToSrgb: false));
    }

    [Fact]
    public void Truncated_ImageWithoutItsEndIsDecodedEntirely()
    {
        // A PNG image without its IEND chunk, a JPEG image without its end of image marker, a GIF image without its trailer
        using var image = CreateGradient(64, 64);
        var png = Encode(image, new ImagingFormats.PngEncoder());
        Assert.Equal(Decode(png)?.Pixels, Decode(png[..^12])?.Pixels);

        var jpeg = Encode(image, new ImagingFormats.JpegEncoder { Quality = 90 });
        Assert.Equal([0xFF, 0xD9], jpeg[^2..]);
        Assert.Equal(Decode(jpeg)?.Pixels, Decode(jpeg[..^2])?.Pixels);

        var gif = Encode(image, new ImagingFormats.GifEncoder());
        Assert.Equal(0x3B, gif[^1]);
        Assert.Equal(Decode(gif)?.Pixels, Decode(gif[..^1])?.Pixels);
        Assert.NotNull(Decode(gif[..^1]));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("webp-lossy")]
    public void Truncated_FormatsWhoseRowsAreNotDecodedOneByOneAreNotDecoded(string format)
    {
        // A limitation of the codec: it writes the pixels of a GIF or WebP frame when the whole frame is decoded. The image
        // is available (its header is valid), its pixels cannot be decoded.
        using var image = CreateGradient(64, 64);
        var data = format switch
        {
            "gif" => Encode(image, new ImagingFormats.GifEncoder()),
            "webp" => Encode(image, new ImagingFormats.WebPEncoder()),
            _ => Encode(image, new ImagingFormats.WebPEncoder { Compression = ImagingFormats.WebPCompression.Lossy, Quality = 80 }),
        };

        using var decoder = _backend.CreateImageDecoder(data.AsMemory(0, data.Length / 2), mimeType: null);
        Assert.NotNull(decoder);
        Assert.Equal((64, 64), (decoder.Width, decoder.Height));
        Assert.Equal(1, decoder.FrameCount);
        Assert.Null(decoder.DecodeFrame(0, ExifOrientation.TopLeft, convertToSrgb: true));
    }

    [Theory]
    [InlineData("gif", 2)]
    [InlineData("webp", 2)]
    [InlineData("apng", 1)]
    public void Truncated_AnimationHasItsCompleteFrames(string format, int expectedFrames)
    {
        // The last frame is cut: the complete frames of a GIF or WebP animation are played; an animated PNG image whose
        // animation is not complete is displayed as its default image
        var data = CreateAnimation(format, totalPlays: null);
        using var decoder = _backend.CreateImageDecoder(data.AsMemory(0, data.Length - (format == "apng" ? 20 : 6)), mimeType: null);
        Assert.NotNull(decoder);
        Assert.Equal(expectedFrames, decoder.FrameCount);
        Assert.Equal(expectedFrames > 1 ? -1 : 0, decoder.RepetitionCount);
        AssertPixel(Colors.Red, DecodePixels(decoder, 0), 4, 1, 1);
        if (expectedFrames > 1)
        {
            AssertPixel(Colors.Blue, DecodePixels(decoder, 1), 4, 1, 1);
            Assert.Equal(200, decoder.GetFrameDuration(1));
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("bmp")]
    [InlineData("ico")]
    public void Corrupted_HeaderIsNotAnImage(string format)
    {
        // The signature of the format, then data that is not a header
        var data = CreateTestImage(format);
        var signatureLength = format switch { "png" => 8, "jpeg" => 3, "gif" => 6, "webp" => 12, "bmp" => 2, _ => 6 };
        byte[] corrupted = [.. data.AsSpan(0, signatureLength), .. "this is not an image, its header is corrupted"u8];
        Assert.Null(_backend.CreateImageDecoder(corrupted, mimeType: null));
        Assert.Null(_backend.CreateImageDecoder(data.AsSpan(0, signatureLength).ToArray(), mimeType: null));
        Assert.Null(_backend.CreateImageDecoder(data.AsSpan(0, 1).ToArray(), mimeType: null));
        Assert.Null(_backend.DecodeImage(corrupted, mimeType: null, applyOrientation: true));
    }

    [Fact]
    public void Corrupted_DataThatIsNotAnImage()
    {
        Assert.Null(_backend.CreateImageDecoder(ReadOnlyMemory<byte>.Empty, mimeType: null));
        Assert.Null(_backend.CreateImageDecoder("not an image"u8.ToArray(), "image/png"));
        Assert.Null(_backend.CreateImageDecoder("<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'/>"u8.ToArray(), "image/svg+xml"));
        Assert.Null(_backend.CreateImageDecoder(new byte[64], mimeType: null));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("png-interlaced")]
    [InlineData("jpeg")]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("webp-lossy")]
    [InlineData("bmp")]
    [InlineData("ico")]
    [InlineData("apng-animation")]
    [InlineData("gif-animation")]
    [InlineData("webp-animation")]
    public void Corrupted_DataNeverThrows(string format)
    {
        // Bytes of the image are replaced: the image is decoded or not, the decoder does not throw
        var data = format.EndsWith("-animation", StringComparison.Ordinal) ? CreateAnimation(format[..^"-animation".Length], totalPlays: 2) : CreateTestImage(format);
        var seed = 12345u;
        for (var i = 0; i < 300; i++)
        {
            var corrupted = (byte[])data.Clone();
            var count = 1 + (i % 3);
            for (var j = 0; j < count; j++)
            {
                corrupted[Next(corrupted.Length)] = (byte)Next(256);
            }

            if (i % 10 == 9)
            {
                corrupted = corrupted[..Next(corrupted.Length)];
            }

            using var decoder = _backend.CreateImageDecoder(corrupted, mimeType: null);
            if (decoder is null || (long)decoder.Width * decoder.Height > 1_000_000)
                continue;

            for (var frame = Math.Min(decoder.FrameCount, 4) - 1; frame >= 0; frame--)
            {
                _ = decoder.GetFrameDuration(frame);
                using var image = decoder.DecodeFrame(frame, ExifOrientation.RightTop, convertToSrgb: true);
                if (image is not null)
                {
                    Assert.Equal((decoder.Height, decoder.Width), (image.Width, image.Height));
                }
            }
        }

        // The same pseudo-random numbers at each run
        int Next(int maxValue)
        {
            seed = (seed * 1664525) + 1013904223;
            return (int)((seed >> 8) % (uint)maxValue);
        }
    }

    [Fact]
    public void Limits_ImageLargerThanTheLargestSurfaceIsNotDecoded()
    {
        // The header of a PNG image of 60000x60000 pixels, whose data would be a few bytes (a decompression bomb)
        Assert.Null(_backend.CreateImageDecoder(CreatePngOfSize(60000, 60000), "image/png"));
        Assert.Null(_backend.DecodeImage(CreatePngOfSize(60000, 60000), "image/png", applyOrientation: true));
        Assert.Null(_backend.CreateImageDecoder(CreatePngOfSize(16385, 16384), "image/png"));
        Assert.Null(_backend.CreateImageDecoder(CreatePngOfSize(int.MaxValue, int.MaxValue), "image/png"));

        // An image of the size of the largest surface (268,435,456 pixels) is opened; its data here is the one of a pixel
        using var decoder = _backend.CreateImageDecoder(CreatePngOfSize(16384, 16384), "image/png");
        Assert.NotNull(decoder);
        Assert.Equal((16384, 16384), (decoder.Width, decoder.Height));

        static byte[] CreatePngOfSize(int width, int height)
        {
            var png = TestPages.CreatePng(1, 1, Colors.Lime);
            BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
            BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
            FixCrc(png, 8);
            return png;
        }
    }

    [Fact]
    public void Decoder_Disposed()
    {
        var decoder = _backend.CreateImageDecoder(CreateAnimation("gif", totalPlays: null), mimeType: null);
        Assert.NotNull(decoder);
        AssertPixel(Colors.Blue, DecodePixels(decoder, 1), 4, 1, 1);
        decoder.Dispose();
        decoder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => decoder.DecodeFrame(0, ExifOrientation.TopLeft, convertToSrgb: true));
    }

    /// <summary>Asserts the color of a pixel of pixels of 4 bytes (RGBA), with a tolerance per component.</summary>
    private static void AssertPixel(Rgba32 expected, byte[] pixels, int width, int x, int y, int tolerance = 0)
    {
        var offset = ((y * width) + x) * 4;
        var actual = new Rgba32(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
        var matches = Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance;
        Assert.True(matches, $"The pixel ({x}, {y}) is {actual} instead of {expected}");
    }

    /// <summary>Returns the number of rows that have a pixel that is not transparent.</summary>
    private static int CountRows((int Width, int Height, byte[] Pixels) image)
    {
        var rows = 0;
        for (var y = 0; y < image.Height; y++)
        {
            if (image.Pixels.AsSpan(y * image.Width * 4, image.Width * 4).ContainsAnyExcept((byte)0))
            {
                rows++;
            }
        }

        return rows;
    }

    /// <summary>Creates an image whose pixels are given by a function.</summary>
    private static Image<Rgba32> CreateImage(int width, int height, Func<int, int, Rgba32> color)
    {
        var image = new Image<Rgba32>(width, height);
        var frame = image.Frames[0];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frame[x, y] = color(x, y);
            }
        }

        return image;
    }

    /// <summary>Creates an opaque image whose rows and columns all differ.</summary>
    private static Image<Rgba32> CreateGradient(int width, int height)
        => CreateImage(width, height, static (x, y) => new Rgba32((byte)(64 + (x * 3 % 192)), (byte)(64 + (y * 3 % 192)), (byte)(((x * 7) + (y * 13)) % 256), 255));

    private static byte[] Encode(Image image, ImagingFormats.ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Encodes a 16x16 image (the left half red, the right half blue) in a format, with the encoders of the codecs.</summary>
    private static byte[] CreateTestImage(string format)
    {
        using var image = CreateImage(16, 16, static (x, _) => x < 8 ? Colors.Red : Colors.Blue);
        return format switch
        {
            "png" => Encode(image, new ImagingFormats.PngEncoder()),
            "png-interlaced" => Encode(image, new ImagingFormats.PngEncoder { Interlaced = true }),
            "jpeg" => Encode(image, new ImagingFormats.JpegEncoder { Quality = 95 }),
            "gif" => Encode(image, new ImagingFormats.GifEncoder()),
            "webp" => Encode(image, new ImagingFormats.WebPEncoder { Compression = ImagingFormats.WebPCompression.Lossless }),
            "webp-lossy" => Encode(image, new ImagingFormats.WebPEncoder { Compression = ImagingFormats.WebPCompression.Lossy, Quality = 90 }),
            "avif" => Encode(image, new ImagingFormats.AvifEncoder { Compression = ImagingFormats.AvifCompression.Lossless }),
            "avif-lossy" => Encode(image, new ImagingFormats.AvifEncoder { Compression = ImagingFormats.AvifCompression.Lossy, Quality = 90 }),
            "bmp" => Encode(image, new ImagingFormats.BmpEncoder()),
            "ico" => Encode(image, new ImagingFormats.IcoEncoder()),
            "cur" => Encode(image, new ImagingFormats.IcoEncoder { Kind = ImagingFormats.IconKind.Cursor }),
            "qoi" => Encode(image, new ImagingFormats.QoiEncoder()),
            "tiff" => Encode(image, new ImagingFormats.TiffEncoder()),
            "tga" => Encode(image, new ImagingFormats.TgaEncoder()),
            "pnm" => Encode(image, new ImagingFormats.PnmEncoder()),
            "ani" => Encode(image, new ImagingFormats.AniEncoder()),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>
    /// Encodes a 4x4 animation of 3 frames (red for 100ms, blue for 200ms, lime for 30ms) as a GIF, an animated PNG or an
    /// animated WebP image, with the encoders of the codecs.
    /// </summary>
    /// <param name="format">The format: <c>gif</c>, <c>apng</c> or <c>webp</c>.</param>
    /// <param name="totalPlays">The number of times the animation is played, or null when it loops forever.</param>
    /// <param name="poster">The color of the default image of an animated PNG image that is not a frame of the animation, or null.</param>
    private static byte[] CreateAnimation(string format, int? totalPlays, Rgba32? poster = null)
    {
        using var animation = new Image<Rgba32>(4, 4, Colors.Red);
        animation.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);
        AddFrame(animation, Colors.Blue, 200);
        AddFrame(animation, Colors.Lime, 30);
        animation.Animation!.TotalPlays = totalPlays;
        if (poster is { } posterColor)
        {
            using var posterImage = new Image<Rgba32>(4, 4, posterColor);
            animation.SetPosterFrame(posterImage.Frames[0]);
        }

        return format switch
        {
            "gif" => Encode(animation, new ImagingFormats.GifEncoder()),
            "apng" => Encode(animation, new ImagingFormats.PngEncoder()),
            "webp" => Encode(animation, new ImagingFormats.WebPEncoder()),
            "avif" => Encode(animation, new ImagingFormats.AvifEncoder { Compression = ImagingFormats.AvifCompression.Lossless }),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        static void AddFrame(Image<Rgba32> animation, Rgba32 color, int duration)
        {
            var frame = animation.AppendFrame();
            frame.Metadata.Duration = FrameDuration.FromMilliseconds(duration);
            frame.ProcessPixelRows(color, static (pixels, color) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    pixels.GetRowSpan(y).Fill(color);
                }
            });
        }
    }

    /// <summary>
    /// Creates a minimal ICC profile (version 2, a display profile based on a matrix and tone curves): an RGB profile whose
    /// primaries are the ones of sRGB with the red and the green ones swapped, or a grayscale profile.
    /// </summary>
    private static byte[] CreateColorProfile(bool grayscale, double gamma)
    {
        // The primaries of sRGB adapted to the D50 white point of the profile connection space
        var red = CreateXyz(0.4360, 0.2225, 0.0139);
        var green = CreateXyz(0.3851, 0.7169, 0.0971);
        var blue = CreateXyz(0.1431, 0.0606, 0.7141);

        // A curve of one entry is a gamma (u8Fixed8Number)
        var curve = new byte[16];
        "curv"u8.CopyTo(curve);
        BinaryPrimitives.WriteUInt32BigEndian(curve.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(curve.AsSpan(12), (ushort)Math.Round(gamma * 256, MidpointRounding.AwayFromZero));

        var description = new byte[100];
        "desc"u8.CopyTo(description);
        BinaryPrimitives.WriteUInt32BigEndian(description.AsSpan(8), 5);
        "test"u8.CopyTo(description.AsSpan(12));
        var copyright = new byte[16];
        "text"u8.CopyTo(copyright);
        "none"u8.CopyTo(copyright.AsSpan(8));

        List<(string Signature, byte[] Data)> tags = [("desc", description), ("cprt", copyright), ("wtpt", CreateXyz(0.9642, 1, 0.8249))];
        if (grayscale)
        {
            tags.Add(("kTRC", curve));
        }
        else
        {
            tags.AddRange([("rXYZ", green), ("gXYZ", red), ("bXYZ", blue), ("rTRC", curve), ("gTRC", curve), ("bTRC", curve)]);
        }

        // The header (128 bytes), the tag table, the data of the tags
        var profile = new byte[128 + 4 + (12 * tags.Count) + tags.Sum(static tag => tag.Data.Length)];
        BinaryPrimitives.WriteInt32BigEndian(profile, profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(8), 0x02100000);
        "mntr"u8.CopyTo(profile.AsSpan(12));
        (grayscale ? "GRAY"u8 : "RGB "u8).CopyTo(profile.AsSpan(16));
        "XYZ "u8.CopyTo(profile.AsSpan(20));
        BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(24), 2020);
        BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16BigEndian(profile.AsSpan(28), 1);
        "acsp"u8.CopyTo(profile.AsSpan(36));
        CreateXyz(0.9642, 1, 0.8249).AsSpan(8).CopyTo(profile.AsSpan(68));
        BinaryPrimitives.WriteInt32BigEndian(profile.AsSpan(128), tags.Count);
        var offset = 128 + 4 + (12 * tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var entry = profile.AsSpan(132 + (12 * i));
            System.Text.Encoding.ASCII.GetBytes(tags[i].Signature, entry);
            BinaryPrimitives.WriteInt32BigEndian(entry[4..], offset);
            BinaryPrimitives.WriteInt32BigEndian(entry[8..], tags[i].Data.Length);
            tags[i].Data.CopyTo(profile, offset);
            offset += tags[i].Data.Length;
        }

        return profile;

        // An XYZType: three s15Fixed16Number
        static byte[] CreateXyz(double x, double y, double z)
        {
            var data = new byte[20];
            "XYZ "u8.CopyTo(data);
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), (int)Math.Round(x * 65536, MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), (int)Math.Round(y * 65536, MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), (int)Math.Round(z * 65536, MidpointRounding.AwayFromZero));
            return data;
        }
    }

    /// <summary>A frame of an APNG image built by <see cref="CreateApng"/>.</summary>
    private sealed record ApngTestFrame(int X, int Y, int Width, int Height, Rgba32[] Pixels, ushort DelayMilliseconds, ApngDisposeOp DisposeOp, ApngBlendOp BlendOp);

    /// <summary>Creates an animated PNG image whose first frame is the default image (the frames are encoded as RGBA).</summary>
    private static byte[] CreateApng(int width, int height, uint plays, params ApngTestFrame[] frames)
    {
        var output = new List<byte>(ImageMetadata.PngSignature.ToArray());
        byte[]? header = null;
        var sequence = 0u;
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            var (frameHeader, data) = ReadPng(TestPages.CreatePng(frame.Width, frame.Height, frame.Pixels));
            if (header is null)
            {
                header = frameHeader;
                BinaryPrimitives.WriteInt32BigEndian(header, width);
                BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
                WriteChunk(output, "IHDR", header);
                var animationControl = new byte[8];
                BinaryPrimitives.WriteInt32BigEndian(animationControl, frames.Length);
                BinaryPrimitives.WriteUInt32BigEndian(animationControl.AsSpan(4), plays);
                WriteChunk(output, "acTL", animationControl);
            }

            var control = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(control, sequence++);
            BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(4), frame.Width);
            BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(8), frame.Height);
            BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(12), frame.X);
            BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(16), frame.Y);
            BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20), frame.DelayMilliseconds);
            BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22), 1000);
            control[24] = (byte)frame.DisposeOp;
            control[25] = (byte)frame.BlendOp;
            WriteChunk(output, "fcTL", control);
            if (i == 0)
            {
                WriteChunk(output, "IDAT", data);
            }
            else
            {
                var frameData = new byte[4 + data.Length];
                BinaryPrimitives.WriteUInt32BigEndian(frameData, sequence++);
                data.CopyTo(frameData, 4);
                WriteChunk(output, "fdAT", frameData);
            }
        }

        WriteChunk(output, "IEND", []);
        return [.. output];
    }

    /// <summary>Returns the header and the concatenated image data of a PNG image.</summary>
    private static (byte[] Header, byte[] Data) ReadPng(byte[] png)
    {
        byte[]? header = null;
        var data = new List<byte>();
        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IHDR")
            {
                header = png.AsSpan(offset + 8, length).ToArray();
            }
            else if (type == "IDAT")
            {
                data.AddRange(png.AsSpan(offset + 8, length));
            }

            offset += 12 + length;
        }

        return (header!, [.. data]);
    }

    /// <summary>Inserts a chunk after the header of a PNG image.</summary>
    private static byte[] InsertPngChunk(byte[] png, string type, byte[] data)
    {
        var chunk = new List<byte>();
        WriteChunk(chunk, type, data);
        const int AfterHeader = 8 + 25;
        return [.. png.AsSpan(0, AfterHeader), .. chunk, .. png.AsSpan(AfterHeader)];
    }

    private static void WriteChunk(List<byte> output, string type, ReadOnlySpan<byte> data)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.AddRange(buffer);
        var typeAndData = new byte[4 + data.Length];
        System.Text.Encoding.Latin1.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData.AsSpan(4));
        output.AddRange(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(typeAndData));
        output.AddRange(buffer);
    }

    /// <summary>Recomputes the CRC of the chunk at an offset.</summary>
    private static void FixCrc(byte[] png, int offset)
    {
        var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(offset + 8 + length), Crc32(png.AsSpan(offset + 4, 4 + length)));
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
