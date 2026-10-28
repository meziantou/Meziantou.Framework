using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// TIFF/BigTIFF and ICO/CUR interoperability with independent readers and writers: FFmpeg's libavcodec
/// <c>tiff</c> codec, and Apple ImageIO (<c>CGImageSource</c>), which reads multi-page TIFF documents and icon
/// directories natively.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>every lossless compression, byte order and layout of the TIFF encoder (the bilevel layout with the CCITT compressions, the palette layout) decodes exactly in FFmpeg;</description></item>
/// <item><description>the 32-bit floating-point RGB and RGBA pages of the TIFF encoder, with the floating-point predictor, decode exactly in FFmpeg;</description></item>
/// <item><description>TIFFs written by FFmpeg decode exactly in the library: every compression it writes, palette, bilevel and deep pages, and YCbCr pages with every chroma subsampling (from the samples FFmpeg reads back);</description></item>
/// <item><description>Apple ImageIO decodes the bilevel pages exactly and the JPEG pages like the library does;</description></item>
/// <item><description>Apple ImageIO sees the pages of a multi-page document, and the representations of an icon, as separate images with their own pixels;</description></item>
/// <item><description>a cursor written by the library keeps the hotspot Apple ImageIO reports.</description></item>
/// </list>
/// </remarks>
public sealed class TiffIcoInteropTests
{
    private const int Width = 13;
    private const int Height = 9;

    public static TheoryData<string, bool, bool> TiffEncoderCases()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw, TiffCompression.PackBits })
        {
            foreach (var bigEndian in new[] { false, true })
            {
                foreach (var bigTiff in new[] { false, true })
                {
                    data.Add(compression.ToString(), bigEndian, bigTiff);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// The classic-TIFF cases: libavcodec's <c>tiff</c> decoder implements TIFF 6.0 only and rejects the magic number 43
    /// of a BigTIFF outright, so those outputs are cross-checked against Apple ImageIO instead
    /// (<see cref="AppleImageIOReadsEveryByteOrderAndOffsetSize"/>).
    /// </summary>
    public static TheoryData<string, bool, bool> FFmpegTiffEncoderCases()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw, TiffCompression.PackBits })
        {
            foreach (var bigEndian in new[] { false, true })
            {
                data.Add(compression.ToString(), bigEndian, false);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FFmpegTiffEncoderCases))]
    public async Task TiffEncoderOutputDecodesExactlyInFFmpeg(string compression, bool bigEndian, bool bigTiff)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateRgba(Width, Height);
        var encoder = new TiffEncoder
        {
            Compression = Enum.Parse<TiffCompression>(compression),
            BigEndian = bigEndian,
            BigTiff = bigTiff,
        };

        var path = await SaveAsync(image, encoder, $"encoder-{compression}-{(bigEndian ? "be" : "le")}-{(bigTiff ? "big" : "classic")}.tif");
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "rgba", Width, Height, XunitCancellationToken));
        AssertSame(ImageSnapshots.CaptureFrame(image.Frames[0]), decoded, $"TIFF {compression} decoded by ffmpeg {ffmpeg.Version}");
    }

    [Theory]
    [MemberData(nameof(TiffEncoderCases))]
    public async Task AppleImageIOReadsEveryByteOrderAndOffsetSize(string compression, bool bigEndian, bool bigTiff)
    {
        var (swift, script) = RequiredTools.AppleImageIO();
        using var image = CreateRgba(Width, Height);
        var encoder = new TiffEncoder
        {
            Compression = Enum.Parse<TiffCompression>(compression),
            BigEndian = bigEndian,
            BigTiff = bigTiff,
        };

        var name = $"imageio-{compression}-{(bigEndian ? "be" : "le")}-{(bigTiff ? "big" : "classic")}.tif";
        var path = await SaveAsync(image, encoder, name);
        var frames = await DecodeWithImageIOAsync(swift, script, path, expectedCount: 1, Width, Height);
        AssertSame(ImageSnapshots.CaptureFrame(image.Frames[0]), frames[0], $"{name} decoded by Apple ImageIO");
    }

    [Theory]
    [InlineData("rgb24", PixelFormat.Rgb24)]
    [InlineData("gray", PixelFormat.Gray8)]
    [InlineData("gray16le", PixelFormat.Gray16)]
    public async Task EveryWrittenSampleLayoutDecodesExactlyInFFmpeg(string rawFormat, PixelFormat pixelFormat)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var rgba = CreateRgba(Width, Height);
        using var image = Convert(rgba, pixelFormat);
        var path = await SaveAsync(image, new TiffEncoder(), $"encoder-{pixelFormat}.tif");
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, rawFormat, Width, Height, XunitCancellationToken));
        var expected = RawPixelBuffer.Create(Width, Height, GetLayout(pixelFormat), ImageSnapshots.CaptureFrame(image.Frames[0]).Span);
        AssertSame(expected, decoded, $"TIFF {pixelFormat} decoded by ffmpeg {ffmpeg.Version}");
    }

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    [InlineData(TiffCompression.CcittModifiedHuffman)]
    [InlineData(TiffCompression.CcittGroup3)]
    [InlineData(TiffCompression.CcittGroup4)]
    public async Task BilevelPagesDecodeExactlyInFFmpeg(TiffCompression compression)
    {
        // Every terminating and make-up code of both colors, in several strips
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateBilevel(BilevelWidth, BilevelHeight);
        var path = await SaveAsync(image, new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Bilevel, RowsPerStrip = 50 }, $"bilevel-{compression}.tif");
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "gray", BilevelWidth, BilevelHeight, XunitCancellationToken));
        AssertSame(ImageSnapshots.CaptureFrame(image.Frames[0]), decoded, $"bilevel TIFF {compression} decoded by ffmpeg {ffmpeg.Version}");
    }

    [Theory]
    [InlineData(TiffCompression.CcittModifiedHuffman)]
    [InlineData(TiffCompression.CcittGroup3)]
    [InlineData(TiffCompression.CcittGroup4)]
    [InlineData(TiffCompression.Lzw)]
    public async Task AppleImageIODecodesBilevelPagesExactly(TiffCompression compression)
    {
        var (swift, script) = RequiredTools.AppleImageIO();
        using var image = CreateBilevel(BilevelWidth, BilevelHeight);
        var name = $"imageio-bilevel-{compression}.tif";
        var path = await SaveAsync(image, new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Bilevel }, name);
        var frames = await DecodeWithImageIOAsync(swift, script, path, expectedCount: 1, BilevelWidth, BilevelHeight, opaque: true);
        using var expected = image.CloneAs<Rgba32>();
        AssertSame(ImageSnapshots.CaptureFrame(expected.Frames[0]), frames[0], $"{name} decoded by Apple ImageIO");
    }

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    [InlineData(TiffCompression.Deflate)]
    public async Task PalettePagesDecodeExactlyInFFmpeg(TiffCompression compression)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var pixels = new Rgb24[LargeWidth * LargeHeight];
        for (var i = 0; i < pixels.Length; i++)
        {
            var color = ((i % LargeWidth) / 7) + ((i / LargeWidth) / 5 * 31);
            pixels[i] = new Rgb24((byte)(color * 3), (byte)(255 - color), (byte)(color * 11));
        }

        using var image = Image.ImportPixelData<Rgb24>(pixels, LargeWidth, LargeHeight);
        var path = await SaveAsync(image, new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Indexed }, $"palette-{compression}.tif");
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "rgb24", LargeWidth, LargeHeight, XunitCancellationToken));
        AssertSame(ImageSnapshots.CaptureFrame(image.Frames[0]), decoded, $"palette TIFF {compression} decoded by ffmpeg {ffmpeg.Version}");
    }

    [Theory]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    public async Task LargeNoisyPagesDecodeExactlyInFFmpeg(TiffCompression compression)
    {
        // Noise fills the LZW table again and again (12-bit codes, clear codes) and makes PackBits literal runs of 128 bytes
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateNoise(LargeWidth, LargeHeight);
        var path = await SaveAsync(image, new TiffEncoder { Compression = compression }, $"noise-{compression}.tif");
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "rgba", LargeWidth, LargeHeight, XunitCancellationToken));
        AssertSame(ImageSnapshots.CaptureFrame(image.Frames[0]), decoded, $"noise TIFF {compression} decoded by ffmpeg {ffmpeg.Version}");
    }

    [Theory]
    [InlineData(JpegChromaSubsampling.Ratio444, false)]
    [InlineData(JpegChromaSubsampling.Ratio444, true)]
    public async Task AppleImageIODecodesJpegPagesLikeTheLibrary(JpegChromaSubsampling subsampling, bool gray)
    {
        // Without chroma subsampling, two JPEG decoders only differ by the rounding of their inverse transforms and color
        // conversions. (ImageIO replicates subsampled chroma where the JPEG decoder of the library interpolates it, like
        // libjpeg: such pages are compared with libtiff when the encoder changes, not here.)
        var (swift, script) = RequiredTools.AppleImageIO();
        using var rgba = CreateSmooth(LargeWidth, LargeHeight);
        using var source = gray ? rgba.CloneAs<Gray8>(Opaque) : (Image)rgba.CloneAs<Rgb24>(Opaque);
        var name = $"imageio-jpeg-{(gray ? "gray" : subsampling.ToString())}.tif";
        var path = await SaveAsync(source, new TiffEncoder { Compression = TiffCompression.Jpeg, JpegChromaSubsampling = subsampling, JpegQuality = 90 }, name);
        var frames = await DecodeWithImageIOAsync(swift, script, path, expectedCount: 1, LargeWidth, LargeHeight, opaque: true);
        using var decoded = Image.Load<Rgba32>(path);
        var policy = ComparisonPolicy.Tolerance(3, 0.75, "Two JPEG decoders of the same datastream: only the rounding of the inverse DCT and of the YCbCr conversion differs.");
        var result = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(decoded.Frames[0]), RawPixelBuffer.Create(LargeWidth, LargeHeight, RawPixelLayout.Rgba8, frames[0]), policy, $"{name} decoded by Apple ImageIO");
        Assert.True(result.IsMatch, result.Describe());
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("deflate")]
    [InlineData("lzw")]
    [InlineData("packbits")]
    public async Task TiffFilesWrittenByFFmpegDecodeExactly(string algorithm)
    {
        // Noise: the LZW strips of FFmpeg use every code size and clear their table
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateNoise(LargeWidth, LargeHeight);
        var path = await WriteWithFFmpegAsync(ffmpeg, image, algorithm, "rgba");
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(data));
        using var decoded = Image.Load(data);
        Assert.Equal(new Size(LargeWidth, LargeHeight), decoded.Size);
        var expected = ImageSnapshots.CaptureFrame(image.Frames[0]);
        var actual = ImageSnapshots.CaptureFrame(decoded.Frames[0]);
        var comparison = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, $"ffmpeg {algorithm} TIFF decoded by the library");
        Assert.True(comparison.IsMatch, comparison.Describe());
    }

    [Theory]
    [InlineData("pal8", "lzw", PixelFormat.Rgb24, ImageColorModel.Indexed)]
    [InlineData("pal8", "packbits", PixelFormat.Rgb24, ImageColorModel.Indexed)]
    [InlineData("monob", "raw", PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData("monob", "lzw", PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData("monob", "packbits", PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData("gray", "lzw", PixelFormat.Gray8, ImageColorModel.Grayscale)]
    [InlineData("ya8", "packbits", PixelFormat.La16, ImageColorModel.GrayscaleAlpha)]
    [InlineData("gray16le", "lzw", PixelFormat.Gray16, ImageColorModel.Grayscale)]
    [InlineData("rgb48le", "lzw", PixelFormat.Rgb48, ImageColorModel.Rgb)]
    [InlineData("rgba64le", "packbits", PixelFormat.Rgba64, ImageColorModel.Rgba)]
    public async Task EverySampleLayoutWrittenByFFmpegDecodesExactly(string pixelFormat, string algorithm, PixelFormat expectedFormat, ImageColorModel expectedModel)
    {
        // The reference is what FFmpeg reads back from the file it wrote, in a raw layout it converts to exactly: 8-bit
        // RGBA for the pages of 8 bits and less, 16-bit RGBA for the others
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateSmooth(LargeWidth, LargeHeight);
        var path = await WriteWithFFmpegAsync(ffmpeg, image, algorithm, pixelFormat);
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        Assert.Equal(expectedModel, Image.Identify(data).ColorModel);
        using var decoded = Image.Load(data);
        Assert.Equal(expectedFormat, decoded.PixelFormat);
        var deep = PixelFormats.GetBitsPerComponent(expectedFormat) == 16;
        var reference = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, deep ? "rgba64le" : "rgba", LargeWidth, LargeHeight, XunitCancellationToken));
        using var converted = deep ? decoded.CloneAs<Rgba64>() : (Image)decoded.CloneAs<Rgba32>();
        AssertSame(ImageSnapshots.CaptureFrame(converted.Frames[0]), reference, $"ffmpeg {pixelFormat} {algorithm} TIFF decoded by the library");
    }

    [Theory]
    [InlineData("yuv444p", 1, 1, "raw")]
    [InlineData("yuv422p", 2, 1, "lzw")]
    [InlineData("yuv420p", 2, 2, "packbits")]
    [InlineData("yuv411p", 4, 1, "deflate")]
    [InlineData("yuv440p", 1, 2, "lzw")]
    [InlineData("yuv410p", 4, 4, "raw")]
    public async Task YCbCrPagesWrittenByFFmpegDecodeFromTheirSamples(string pixelFormat, int horizontal, int vertical, string algorithm)
    {
        // FFmpeg stores its planes as the data units of the page and gives them back untouched as raw video, so the
        // expected pixels are the formula of TIFF 6.0 (section 21) applied in decimal arithmetic to samples an independent
        // reader extracted, with the ReferenceBlackWhite values of the file and each chroma sample replicated
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateSmooth(LargeWidth, LargeHeight);
        var path = await WriteWithFFmpegAsync(ffmpeg, image, algorithm, pixelFormat);
        var planes = await ffmpeg.RunAsync(["-noautorotate", "-i", path, "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"], XunitCancellationToken);
        if (planes.ExitCode != 0)
            Assert.Fail($"ffmpeg {ffmpeg.Version} failed to read back its {pixelFormat} TIFF: {planes.StandardError}");

        var chromaWidth = LargeWidth / horizontal;
        var chromaHeight = LargeHeight / vertical;
        Assert.Equal((LargeWidth * LargeHeight) + (2 * chromaWidth * chromaHeight), planes.StandardOutput.Length);
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var reference = ReadRationals(data, tag: 532);
        Assert.HasCount(6, reference);
        var samples = planes.StandardOutput.Span;

        var expected = new byte[LargeWidth * LargeHeight * 3];
        for (var y = 0; y < LargeHeight; y++)
        {
            for (var x = 0; x < LargeWidth; x++)
            {
                var chroma = ((y / vertical) * chromaWidth) + (x / horizontal);
                var luma = (samples[(y * LargeWidth) + x] - reference[0]) * 255 / (reference[1] - reference[0]);
                var blue = (samples[(LargeWidth * LargeHeight) + chroma] - reference[2]) * 127 / (reference[3] - reference[2]);
                var red = (samples[(LargeWidth * LargeHeight) + (chromaWidth * chromaHeight) + chroma] - reference[4]) * 127 / (reference[5] - reference[4]);
                var r = luma + (red * (2 - (2 * 0.299m)));
                var b = luma + (blue * (2 - (2 * 0.114m)));
                var g = (luma - (0.114m * b) - (0.299m * r)) / 0.587m;
                var offset = ((y * LargeWidth) + x) * 3;
                expected[offset] = (byte)Math.Clamp(Math.Floor(r + 0.5m), 0, 255);
                expected[offset + 1] = (byte)Math.Clamp(Math.Floor(g + 0.5m), 0, 255);
                expected[offset + 2] = (byte)Math.Clamp(Math.Floor(b + 0.5m), 0, 255);
            }
        }

        Assert.Equal(ImageColorModel.YCbCr, Image.Identify(data).ColorModel);
        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
        var comparison = PixelBufferComparer.Compare(RawPixelBuffer.Create(LargeWidth, LargeHeight, RawPixelLayout.Rgb8, expected), ImageSnapshots.CaptureFrame(decoded.Frames[0]), ComparisonPolicy.Exact, $"ffmpeg {pixelFormat} {algorithm} TIFF decoded by the library");
        Assert.True(comparison.IsMatch, comparison.Describe());
    }

    [Fact]
    public async Task AppleImageIOSeesEveryPageOfAMultiPageDocument()
    {
        var (swift, script) = RequiredTools.AppleImageIO();
        using var first = CreateRgba(Width, Height, seed: 1);
        using var second = CreateRgba(Width, Height, seed: 2);
        using var third = CreateRgba(Width, Height, seed: 3);

        var path = InteropSettings.GetArtifactsDirectory(nameof(TiffIcoInteropTests)) / "imageio-document.tif";
        using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
        {
            pages.Add(first);
            pages.Add(second);
            pages.Add(third);
            pages.Save(path, new TiffEncoder());
        }

        var frames = await DecodeWithImageIOAsync(swift, script, path, expectedCount: 3, Width, Height);
        AssertSame(ImageSnapshots.CaptureFrame(first.Frames[0]), frames[0], "page 0 decoded by Apple ImageIO");
        AssertSame(ImageSnapshots.CaptureFrame(second.Frames[0]), frames[1], "page 1 decoded by Apple ImageIO");
        AssertSame(ImageSnapshots.CaptureFrame(third.Frames[0]), frames[2], "page 2 decoded by Apple ImageIO");
    }

    [Theory]
    [InlineData(IconPayloadFormat.Dib)]
    [InlineData(IconPayloadFormat.Png)]
    public async Task AppleImageIOSeesEveryRepresentationOfAnIcon(IconPayloadFormat payloadFormat)
    {
        var (swift, script) = RequiredTools.AppleImageIO();
        using var small = CreateRgba(16, 16, seed: 4);
        using var large = CreateRgba(16, 16, seed: 5);

        var path = InteropSettings.GetArtifactsDirectory(nameof(TiffIcoInteropTests)) / $"imageio-icon-{payloadFormat}.ico";
        using (var icon = ImageCollection.Create(ImageCollectionKind.Representations))
        {
            icon.Add(small);
            icon.Add(large);
            icon.Save(path, new IcoEncoder { PayloadFormat = payloadFormat });
        }

        var frames = await DecodeWithImageIOAsync(swift, script, path, expectedCount: 2, 16, 16);
        AssertSame(ImageSnapshots.CaptureFrame(small.Frames[0]), frames[0], $"{payloadFormat} representation 0 decoded by Apple ImageIO");
        AssertSame(ImageSnapshots.CaptureFrame(large.Frames[0]), frames[1], $"{payloadFormat} representation 1 decoded by Apple ImageIO");
    }

    [Fact]
    public async Task AppleImageIODecodesACursorWrittenByTheLibrary()
    {
        var (swift, script) = RequiredTools.AppleImageIO();
        using var image = CreateRgba(16, 16, seed: 6);
        var path = InteropSettings.GetArtifactsDirectory(nameof(TiffIcoInteropTests)) / "imageio-cursor.cur";
        using (var cursor = ImageCollection.Create(ImageCollectionKind.Representations))
        {
            cursor.Add(image, new Point(5, 6));
            cursor.Save(path, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = IconPayloadFormat.Dib });
        }

        var frames = await DecodeWithImageIOAsync(swift, script, path, expectedCount: 1, 16, 16);
        AssertSame(ImageSnapshots.CaptureFrame(image.Frames[0]), frames[0], "cursor representation decoded by Apple ImageIO");

        // The hotspot survives a round trip through this library's own reader
        using var reloaded = ImageCollection.Load(path);
        Assert.Equal(new Point(5, 6), reloaded[0].Hotspot);
    }

    private static async Task<IReadOnlyList<byte[]>> DecodeWithImageIOAsync(string swift, string script, FullPath path, int expectedCount, int width, int height, bool opaque = false)
    {
        // Pages without alpha are returned in layouts without alpha, which the helper only converts when asked to
        var result = await ProcessRunner.RunAsync(swift, opaque ? [script, "--opaque", path] : [script, path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"Apple ImageIO failed on {path.Name} (exit code {result.ExitCode}): {result.StandardError}");

        var header = result.StandardError.Trim().Split(' ');
        Assert.Equal(
            ["frames", expectedCount.ToString(CultureInfo.InvariantCulture), width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture)],
            header[..4]);

        var frameLength = RawPixelLayout.Rgba8.GetByteLength(width, height);
        Assert.Equal(expectedCount * frameLength, result.StandardOutput.Length);
        var frames = new List<byte[]>(expectedCount);
        for (var i = 0; i < expectedCount; i++)
        {
            frames.Add(result.StandardOutput.Slice(i * frameLength, frameLength).ToArray());
        }

        return frames;
    }

    private static RawPixelLayout GetLayout(PixelFormat format) => format switch
    {
        PixelFormat.Rgb24 => RawPixelLayout.Rgb8,
        PixelFormat.Gray8 => RawPixelLayout.Gray8,
        PixelFormat.Gray16 => RawPixelLayout.Gray16Le,
        _ => RawPixelLayout.Rgba8,
    };

    private static Image Convert(Image<Rgba32> source, PixelFormat format) => format switch
    {
        PixelFormat.Rgb24 => source.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) }),
        PixelFormat.Gray8 => source.CloneAs<Gray8>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) }),
        _ => source.CloneAs<Gray16>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) }),
    };

    private const int BilevelWidth = 2703;
    private const int BilevelHeight = 160;
    private const int LargeWidth = 212;
    private const int LargeHeight = 152;

    private static readonly PixelConversionOptions Opaque = new() { BackgroundColor = new Rgba64(0, 0, 0) };

    /// <summary>
    /// A black and white image whose rows hold runs of every length the CCITT codes distinguish, in both colors: every
    /// length from 1 to 63 pixels (the terminating codes), and every multiple of 64 up to 2,560 (the make-up codes) as the
    /// first run of a row that starts white and of a row that starts black.
    /// </summary>
    private static Image<Gray8> CreateBilevel(int width, int height)
    {
        var pixels = new Gray8[width * height];
        for (var y = 0; y < height; y++)
        {
            var group = y / 4;
            var black = y % 2 == 1;
            var x = 0;
            for (var index = 0; x < width; index++)
            {
                var run = y % 4 < 2 ? 1 + ((index + group) % 63) : 64 * (1 + (group % 40));
                var length = Math.Min(run, width - x);
                pixels.AsSpan((y * width) + x, length).Fill(new Gray8(black ? (byte)0 : (byte)255));
                x += length;
                black = !black;
            }
        }

        return Image.ImportPixelData<Gray8>(pixels, width, height);
    }

    private static Image<Rgba32> CreateNoise(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        var state = 2463534242u;
        for (var i = 0; i < pixels.Length; i++)
        {
            state = (state * 1664525) + 1013904223;
            pixels[i] = new Rgba32((byte)(state >> 24), (byte)(state >> 16), (byte)(state >> 8), 255);
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    /// <summary>Gradients with a few edges: what chroma subsampling and a JPEG transform are made for.</summary>
    private static Image<Rgba32> CreateSmooth(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (x / 53) % 2 == (y / 38) % 2
                    ? new Rgba32((byte)(x + 20), (byte)(y + 40), (byte)((x + y) / 2), 255)
                    : new Rgba32((byte)(250 - y), (byte)(x / 2), (byte)(200 - (x / 2)), 255);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static async Task<FullPath> WriteWithFFmpegAsync(FFmpegTool ffmpeg, Image image, string algorithm, string pixelFormat)
    {
        var directory = InteropSettings.GetArtifactsDirectory(nameof(TiffIcoInteropTests));
        var source = await SaveAsync(image, new PngEncoder(), $"ffmpeg-source-{pixelFormat}-{algorithm}.png");
        var path = directory / $"ffmpeg-{pixelFormat}-{algorithm}.tif";
        var result = await ffmpeg.RunAsync(["-y", "-i", source, "-c:v", "tiff", "-compression_algo", algorithm, "-pix_fmt", pixelFormat, path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ffmpeg {ffmpeg.Version} failed to write a {pixelFormat} {algorithm} TIFF: {result.StandardError}");

        return path;
    }

    /// <summary>Reads the RATIONAL values of a tag of the first directory of a little-endian classic TIFF, independently of the library.</summary>
    private static decimal[] ReadRationals(byte[] file, ushort tag)
    {
        Assert.Equal("II*\0"u8.ToArray(), file[..4]);
        var directory = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory));
        for (var i = 0; i < count; i++)
        {
            var entry = file.AsSpan(directory + 2 + (i * 12), 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != tag)
                continue;

            Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]));
            var values = new decimal[BinaryPrimitives.ReadInt32LittleEndian(entry[4..])];
            var data = file.AsSpan(BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            for (var j = 0; j < values.Length; j++)
            {
                values[j] = (decimal)BinaryPrimitives.ReadUInt32LittleEndian(data[(j * 8)..]) / BinaryPrimitives.ReadUInt32LittleEndian(data[((j * 8) + 4)..]);
            }

            return values;
        }

        return [];
    }

    private static Image<Rgba32> CreateRgba(int width, int height, int seed = 0)
    {
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = i + (seed * 37);
            pixels[i] = new Rgba32((byte)(value * 7), (byte)(value * 13), (byte)(value * 29), 255);
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    public async Task FloatingPointPagesDecodeExactlyInFFmpeg(TiffCompression compression)
    {
        // FFmpeg reads the floating-point RGB and RGBA pages of little-endian classic TIFF files, the floating-point
        // predictor of the Deflate and LZW strips included; it reads no floating-point gray page and outputs no big-endian
        // floats. GDAL 3.13.0 and libtiff 4.7.0 read every layout, byte order and offset size of the encoder exactly.
        float[] values = [0f, -0f, 1f, 0.5f, -0.25f, float.MaxValue, 1e-40f, 65504f, -1234.5678f, float.PositiveInfinity, float.NegativeInfinity, 0.1f, 3.5f];
        var ffmpeg = await RequiredTools.FFmpegAsync();
        foreach (var opaque in new[] { true, false })
        {
            var pixels = new RgbaFloat[Width * Height];
            var expected = new List<byte>();
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new RgbaFloat(values[i % values.Length], values[(i + 4) % values.Length], values[(i + 9) % values.Length], opaque ? 1 : values[((i * 3) + 1) % values.Length]);
                float[] samples = opaque ? [pixels[i].R, pixels[i].G, pixels[i].B] : [pixels[i].R, pixels[i].G, pixels[i].B, pixels[i].A];
                foreach (var sample in samples)
                {
                    expected.AddRange(BitConverter.GetBytes(BitConverter.SingleToUInt32Bits(sample)));
                }
            }

            using var image = Image.ImportPixelData<RgbaFloat>(pixels, Width, Height);
            image.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;
            var path = await SaveAsync(image, new TiffEncoder { Compression = compression, RowsPerStrip = 4 }, $"encoder-float-{compression}-{(opaque ? "rgb" : "rgba")}.tif");
            var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, opaque ? "rgbf32le" : "rgbaf32le", Width, Height, XunitCancellationToken));
            Assert.Equal(expected.ToArray(), decoded);
        }
    }

    private static async Task<FullPath> SaveAsync(Image image, ImageEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(TiffIcoInteropTests)) / name;
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    private static void AssertSame(RawPixelBuffer expected, byte[] decoded, string context)
    {
        var result = PixelBufferComparer.Compare(expected, RawPixelBuffer.Create(expected.Width, expected.Height, expected.Layout, decoded), ComparisonPolicy.Exact, context);
        Assert.True(result.IsMatch, result.Describe());
    }
}
