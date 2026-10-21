using System.IO.Compression;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// ImageData, ImageBitmap, createImageBitmap() and ImageBitmapRenderingContext (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html),
/// and the metadata of the images (orientation, animation loops). The decoding of the images by a rendering backend is tested
/// by the tests of the Imaging backend.
/// </summary>
public sealed class ImageBitmapTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    /// <summary>Runs a script and returns the value it assigns to <c>document.title</c> (after the event loop is idle).</summary>
    private string Run(string script)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => "<canvas id=c width=3 height=2></canvas><script>" + script + "</script>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void ImageData_Constructors()
    {
        var data = new ImageData(2, 3);
        Assert.Equal((2u, 3u), (data.Width, data.Height));
        Assert.Equal(24, data.Data.Length);
        Assert.Equal(PredefinedColorSpace.Srgb, data.ColorSpace);
        Assert.Equal(ImageDataPixelFormat.RgbaUnorm8, data.PixelFormat);

        // The bytes are not copied
        var bytes = new byte[16];
        var view = new ImageData(bytes, 2);
        Assert.Equal(2u, view.Height);
        view.Data.Span[0] = 42;
        Assert.Equal(42, bytes[0]);

        var float16 = new ImageData(1, 1, new ImageDataSettings { PixelFormat = ImageDataPixelFormat.RgbaFloat16, ColorSpace = PredefinedColorSpace.DisplayP3 });
        Assert.Equal(8, float16.Data.Length);
        Assert.Equal(PredefinedColorSpace.DisplayP3, float16.ColorSpace);

        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => new ImageData(0, 1)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => new ImageData(uint.MaxValue, uint.MaxValue)).Name);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => new ImageData(new byte[3], 1)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => new ImageData(new byte[16], 3)).Name);
        Assert.Equal("IndexSizeError", Assert.Throws<DomException>(() => new ImageData(new byte[16], 4, 3)).Name);
    }

    [Fact]
    public void ImageData_Script()
    {
        var result = Run("""
            const array = new Uint8ClampedArray([1, 2, 3, 4, 5, 6, 7, 8]);
            const data = new ImageData(array, 1);
            const empty = new ImageData(2, 1);
            empty.data[0] = 255;
            const errors = [];
            for (const create of [() => new ImageData(0, 1), () => new ImageData(new Uint8ClampedArray(3), 1), () => new ImageData(new Uint8Array(100), 25), () => new ImageData(new Uint8ClampedArray(0))]) {
              try { create(); errors.push('no error'); } catch (e) { errors.push(e.name); }
            }
            const clone = structuredClone(data);
            document.title = [data.data === array, data.data === data.data, data.width, data.height, empty.data === empty.data, empty.data[0], data.colorSpace, data.pixelFormat,
              errors.join(' '), clone instanceof ImageData, clone.data !== array, clone.data.join(' ')].join();
            """);
        Assert.Equal("true,true,1,2,true,255,srgb,rgba-unorm8,IndexSizeError InvalidStateError IndexSizeError TypeError,true,true,1 2 3 4 5 6 7 8", result);
    }

    [Fact]
    public void CreateImageBitmap_Script()
    {
        var result = Run("""
            const results = [];
            const data = new ImageData(new Uint8ClampedArray([255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0]), 2);
            const pending = createImageBitmap(data);
            results.push(pending instanceof Promise);
            (async () => {
              const bitmap = await createImageBitmap(data);
              results.push(bitmap instanceof ImageBitmap, bitmap.width, bitmap.height);
              const cropped = await createImageBitmap(bitmap, -1, 0, 2, 1, { resizeWidth: 4, resizeQuality: 'pixelated' });
              results.push(cropped.width, cropped.height);
              const canvas = await createImageBitmap(document.getElementById('c'));
              results.push(canvas.width, canvas.height);
              bitmap.close();
              results.push(bitmap.width);
              for (const args of [[bitmap], [data, 0, 0, 0, 1], [data, { resizeWidth: 0 }], [new Image()], [document.createElement('video')], [new Blob(['x'])]]) {
                try { await createImageBitmap(...args); results.push('no error'); } catch (e) { results.push(e.name); }
              }
              try { await createImageBitmap(null); } catch (e) { results.push(e.name); }
              document.title = results.join();
            })();
            """);
        Assert.Equal("true,true,2,2,4,2,3,2,0,InvalidStateError,RangeError,InvalidStateError,InvalidStateError,InvalidStateError,InvalidStateError,TypeError", result);
    }

    [Fact]
    public void BitmapRenderer_Script()
    {
        var result = Run("""
            const canvas = document.getElementById('c');
            const context = canvas.getContext('bitmaprenderer', { alpha: false });
            const results = [context instanceof ImageBitmapRenderingContext, context === canvas.getContext('bitmaprenderer'), canvas.getContext('2d'), context.canvas === canvas];
            (async () => {
              const bitmap = await createImageBitmap(new ImageData(1, 1));
              context.transferFromImageBitmap(bitmap);
              results.push(bitmap.width);
              try { context.transferFromImageBitmap(bitmap); } catch (e) { results.push(e.name); }
              context.transferFromImageBitmap(null);
              const snapshot = await createImageBitmap(canvas);
              results.push(snapshot.width);
              document.title = results.join();
            })();
            """);
        Assert.Equal("true,true,,true,0,InvalidStateError,3", result);
    }

    [Fact]
    public void ImageBitmap_IsTransferable()
    {
        var result = Run("""
            (async () => {
              const bitmap = await createImageBitmap(new ImageData(2, 1));
              const clone = structuredClone(bitmap);
              const transferred = structuredClone(bitmap, { transfer: [bitmap] });
              const results = [clone.width, transferred.width, bitmap.width];
              try { structuredClone(bitmap); } catch (e) { results.push(e.name); }
              document.title = results.join();
            })();
            """);
        Assert.Equal("2,2,0,DataCloneError", result);
    }

    [Fact]
    public void CreateImageBitmap_ImageWithoutBackendCannotBeDecoded()
    {
        // A 2x3 PNG: only its header is read without rendering backend
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = new BytesLoader(png) });
        var document = context.Navigate("https://example.com/", _ => "<img src=a.png>");
        _contexts.EventLoop.RunUntilIdle();
        var image = (HtmlImageElement)document.QuerySelector("img")!;
        Assert.Equal(2u, image.NaturalWidth);
        var task = document.DefaultView!.CreateImageBitmap(image);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("InvalidStateError", Assert.IsType<DomException>(task.Exception?.InnerException).Name);
    }

    [Fact]
    public void Format_CropsResizesFlipsAndPremultiplies()
    {
        // 2x2: red, green / blue, half-transparent white
        var input = new BitmapPixels(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 128]);

        // The parts of the source rectangle outside of the input are transparent black
        var cropped = ImageBitmapFactory.Format(input, (1, -1, 2, 2), new ImageBitmapOptions())!;
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0], cropped.Rgba);

        // A negative size extends the rectangle to the left and to the top
        var negative = ImageBitmapFactory.Format(input, (2, 1, -1, -1), new ImageBitmapOptions())!;
        Assert.Equal([0, 255, 0, 255], negative.Rgba);

        var flipped = ImageBitmapFactory.Format(input, sourceRect: null, new ImageBitmapOptions { ImageOrientation = ImageBitmapOrientation.FlipY })!;
        Assert.Equal([0, 0, 255, 255, 255, 255, 255, 128, 255, 0, 0, 255, 0, 255, 0, 255], flipped.Rgba);

        // The resize height alone keeps the aspect ratio; pixelated is the nearest pixel
        var resized = ImageBitmapFactory.Format(input, sourceRect: null, new ImageBitmapOptions { ResizeHeight = 4, ResizeQuality = ResizeQuality.Pixelated })!;
        Assert.Equal((4, 4), (resized.Width, resized.Height));
        Assert.Equal([255, 0, 0, 255], resized.Rgba[..4]);
        Assert.Equal([255, 0, 0, 255], resized.Rgba[4..8]);
        Assert.Equal([0, 255, 0, 255], resized.Rgba[8..12]);

        // The bilinear interpolation of the colors is premultiplied: a transparent pixel does not darken its neighbor
        var halfTransparent = new BitmapPixels(2, 1, [255, 255, 255, 255, 0, 0, 0, 0]);
        var averaged = ImageBitmapFactory.Format(halfTransparent, sourceRect: null, new ImageBitmapOptions { ResizeWidth = 1, ResizeQuality = ResizeQuality.High })!;
        Assert.Equal([255, 255, 255, 128], averaged.Rgba);

        // The premultiplication reduces the precision of the colors of the transparent pixels
        var premultiplied = ImageBitmapFactory.Format(new BitmapPixels(1, 1, [101, 0, 0, 3]), sourceRect: null, new ImageBitmapOptions { PremultiplyAlpha = PremultiplyAlpha.Premultiply })!;
        Assert.Equal([85, 0, 0, 3], premultiplied.Rgba);

        Assert.Null(ImageBitmapFactory.Format(input, (0, 0, 100_000, 100_000), new ImageBitmapOptions()));
    }

    [Fact]
    public void Metadata_ExifDensityCorrectedSize()
    {
        // The image file directory: XResolution and YResolution (rationals), ResolutionUnit (inch) and the pointer to the Exif
        // directory, which has PixelXDimension and PixelYDimension
        static byte[] Tiff(uint resolutionX, uint resolutionY, ushort unit, uint width, uint height) =>
        [
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0,
            4, 0,
            0x1A, 0x01, 5, 0, 1, 0, 0, 0, 62, 0, 0, 0,
            0x1B, 0x01, 5, 0, 1, 0, 0, 0, 70, 0, 0, 0,
            0x28, 0x01, 3, 0, 1, 0, 0, 0, (byte)unit, 0, 0, 0,
            0x69, 0x87, 4, 0, 1, 0, 0, 0, 78, 0, 0, 0,
            0, 0, 0, 0,
            (byte)resolutionX, 0, 0, 0, 1, 0, 0, 0,
            (byte)resolutionY, 0, 0, 0, 1, 0, 0, 0,
            2, 0,
            0x02, 0xA0, 4, 0, 1, 0, 0, 0, (byte)width, 0, 0, 0,
            0x03, 0xA0, 4, 0, 1, 0, 0, 0, (byte)height, 0, 0, 0,
            0, 0, 0, 0,
        ];

        static byte[] Jpeg(byte[] tiff) => [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(2 + 6 + tiff.Length), .. "Exif\0\0"u8, .. tiff, 0xFF, 0xC0, 0, 17, 8, 0, 50, 0, 100, 3];

        // A 100x50 image at 144 pixels per inch is 50x25 CSS pixels; at 36 pixels per inch, 200x100
        Assert.Equal((50u, 25u), ImageMetadata.GetDensityCorrectedSize(Jpeg(Tiff(144, 144, 2, 50, 25)), 100, 50));
        Assert.Equal((200u, 100u), ImageMetadata.GetDensityCorrectedSize(Jpeg(Tiff(36, 36, 2, 200, 100)), 100, 50));
        Assert.Equal((50u, 100u), ImageMetadata.GetDensityCorrectedSize(Jpeg(Tiff(144, 36, 2, 50, 100)), 100, 50));
        Assert.True(ImageHeaders.TryGetImageInfo(Jpeg(Tiff(144, 144, 2, 50, 25)), "image/jpeg", out var corrected));
        Assert.Equal((50.0, 25.0), (corrected.Width, corrected.Height));

        // The dimensions do not match the resolution, or the unit is not the inch: the size of the encoded pixels is kept
        Assert.Null(ImageMetadata.GetDensityCorrectedSize(Jpeg(Tiff(144, 144, 2, 60, 25)), 100, 50));
        Assert.Null(ImageMetadata.GetDensityCorrectedSize(Jpeg(Tiff(144, 144, 3, 50, 25)), 100, 50));
        Assert.True(ImageHeaders.TryGetImageInfo(Jpeg(Tiff(144, 144, 3, 50, 25)), "image/jpeg", out var physical));
        Assert.Equal((100.0, 50.0), (physical.Width, physical.Height));
    }

    [Fact]
    public void Metadata_ExifOrientation()
    {
        byte[] tiff = [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0];

        // JPEG: an APP1 segment before the start of frame
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE1, 0, (byte)(2 + 6 + tiff.Length), .. "Exif\0\0"u8, .. tiff, 0xFF, 0xC0, 0, 17, 8, 0, 2, 0, 3, 3];
        Assert.Equal(ExifOrientation.LeftBottom, ImageMetadata.GetExifOrientation(jpeg));
        Assert.True(ImageHeaders.TryGetImageInfo(jpeg, "image/jpeg", out var jpegSize));
        Assert.Equal((2.0, 3.0, true), (jpegSize.Width, jpegSize.Height, jpegSize.OrientationSwapsAxes));

        // PNG: the eXIf chunk, or the EXIF raw profile of a compressed text chunk, before the image data
        var header = Chunk("IHDR", [0, 0, 0, 3, 0, 0, 0, 2, 8, 6, 0, 0, 0]);
        byte[] png = [.. ImageMetadata.PngSignature, .. header, .. Chunk("eXIf", tiff), .. Chunk("IDAT", [])];
        Assert.Equal(ExifOrientation.LeftBottom, ImageMetadata.GetExifOrientation(png));
        Assert.True(ImageHeaders.TryGetImageInfo(png, "image/png", out var pngSize));
        Assert.Equal((2.0, 3.0), (pngSize.Width, pngSize.Height));

        byte[] late = [.. ImageMetadata.PngSignature, .. header, .. Chunk("IDAT", []), .. Chunk("eXIf", tiff)];
        Assert.Equal(ExifOrientation.TopLeft, ImageMetadata.GetExifOrientation(late));

        var profile = "\nexif\n" + tiff.Length + "\n" + Convert.ToHexString([.. "Exif\0\0"u8, .. tiff]).ToLowerInvariant() + "\n";
        byte[] text = [.. "Raw profile type exif"u8, 0, 0, .. Deflate(System.Text.Encoding.ASCII.GetBytes(profile))];
        byte[] rawProfile = [.. ImageMetadata.PngSignature, .. header, .. Chunk("zTXt", text), .. Chunk("IDAT", [])];
        Assert.Equal(ExifOrientation.LeftBottom, ImageMetadata.GetExifOrientation(rawProfile));

        // A PNG whose first chunk is not its header is corrupted
        Assert.False(ImageHeaders.TryGetImageInfo([.. ImageMetadata.PngSignature, .. Chunk("IDAT", []), .. header], "image/png", out _));
    }

    [Fact]
    public void Metadata_AnimationRepetitionCount()
    {
        // GIF: the NETSCAPE2.0 application extension (0: forever), played once without it
        byte[] screen = [.. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0, 0, 0, 255, 255, 255];
        byte[] netscape = [0x21, 0xFF, 11, .. "NETSCAPE2.0"u8, 3, 1, 2, 0, 0];
        byte[] image = [0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 0x01, 0, 0x3B];
        Assert.Equal(2, ImageMetadata.GetAnimationRepetitionCount([.. screen, .. netscape, .. image]));
        netscape[16] = 0;
        Assert.Equal(-1, ImageMetadata.GetAnimationRepetitionCount([.. screen, .. netscape, .. image]));
        Assert.Equal(0, ImageMetadata.GetAnimationRepetitionCount([.. screen, .. image]));

        // WebP: the loop count of the ANIM chunk (0: forever, n: played n times)
        byte[] webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8X"u8, 10, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. "ANIM"u8, 6, 0, 0, 0, 0, 0, 0, 0, 3, 0];
        Assert.Equal(2, ImageMetadata.GetAnimationRepetitionCount(webp));
        Assert.Null(ImageMetadata.GetAnimationRepetitionCount([.. ImageMetadata.PngSignature]));
    }

    [Fact]
    public void Apng_IsSplitIntoFrames()
    {
        var header = Chunk("IHDR", [0, 0, 0, 2, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        byte[] apng = [.. ImageMetadata.PngSignature, .. header, .. Chunk("acTL", [0, 0, 0, 2, 0, 0, 0, 0]),
            .. Chunk("fcTL", FrameControl(0, 2, 1, 0, 0)), .. Chunk("IDAT", [1, 2, 3]),
            .. Chunk("fcTL", FrameControl(1, 1, 1, 1, 0)), .. Chunk("fdAT", [0, 0, 0, 2, 4, 5]), .. Chunk("IEND", [])];
        var image = ApngImage.TryParse(apng);
        Assert.NotNull(image);
        Assert.Equal(-1, image.RepetitionCount);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal((1, 0, 1, 1, 50.0), (image.Frames[1].X, image.Frames[1].Y, image.Frames[1].Width, image.Frames[1].Height, image.Frames[1].Duration));

        // The PNG image of a frame: its header has the size of the frame, its data the one of the fdAT chunk
        var frame = image.Frames[1].Png;
        Assert.Equal([0, 0, 0, 1, 0, 0, 0, 1], frame[16..24]);
        Assert.Equal([0, 0, 0, 2, .. "IDAT"u8, 4, 5], frame[33..43]);

        // A still PNG image and an animation whose sequence numbers are wrong are not animated
        Assert.Null(ApngImage.TryParse([.. ImageMetadata.PngSignature, .. header, .. Chunk("IDAT", [1]), .. Chunk("IEND", [])]));
        apng[^19] = 9;
        Assert.Null(ApngImage.TryParse(apng));

        static byte[] FrameControl(byte sequence, byte width, byte height, byte x, byte y) => [0, 0, 0, sequence, 0, 0, 0, width, 0, 0, 0, height, 0, 0, 0, x, 0, 0, 0, y, 0, 1, 0, 20, 0, 0];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var crc = System.IO.Hashing.Crc32.HashToUInt32([.. typeBytes, .. data]);
        return [(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length, .. typeBytes, .. data, (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc];
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private sealed class BytesLoader(byte[] content) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(new DocumentResource(content) { Headers = [new("Content-Type", "image/png")] });
    }
}
