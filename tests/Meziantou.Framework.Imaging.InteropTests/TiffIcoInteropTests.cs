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
/// <item><description>every option combination of the TIFF encoder decodes exactly in FFmpeg;</description></item>
/// <item><description>TIFFs written by FFmpeg decode exactly in the library;</description></item>
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
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate })
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
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate })
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
    [InlineData("raw")]
    [InlineData("deflate")]
    [InlineData("lzw")]
    public async Task TiffFilesWrittenByFFmpegAreDecodedOrRejectedExplicitly(string algorithm)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateRgba(Width, Height);
        var source = await SaveAsync(image, new PngEncoder(), $"ffmpeg-source-{algorithm}.png");
        var path = InteropSettings.GetArtifactsDirectory(nameof(TiffIcoInteropTests)) / $"ffmpeg-{algorithm}.tif";
        var result = await ffmpeg.RunAsync(["-y", "-i", source, "-c:v", "tiff", "-compression_algo", algorithm, "-pix_fmt", "rgba", path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ffmpeg {ffmpeg.Version} failed to write a {algorithm} TIFF: {result.StandardError}");

        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(data));
        if (algorithm == "lzw")
        {
            // LZW is recognized and rejected explicitly, never decoded as garbage
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
            Assert.Equal("Compression: 5", exception.Feature);
            return;
        }

        using var decoded = Image.Load(data);
        Assert.Equal(new Size(Width, Height), decoded.Size);
        var expected = ImageSnapshots.CaptureFrame(image.Frames[0]);
        var actual = ImageSnapshots.CaptureFrame(decoded.Frames[0]);
        var comparison = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, $"ffmpeg {algorithm} TIFF decoded by the library");
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

    private static async Task<IReadOnlyList<byte[]>> DecodeWithImageIOAsync(string swift, string script, FullPath path, int expectedCount, int width, int height)
    {
        var result = await ProcessRunner.RunAsync(swift, [script, path], XunitCancellationToken);
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
