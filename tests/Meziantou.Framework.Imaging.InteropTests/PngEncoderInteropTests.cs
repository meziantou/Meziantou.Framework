using System.IO.Compression;
using System.Text.Json;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Static PNG encoder output decoded by the pinned FFmpeg: every storage type with every filter setting, every
/// compression level and Adam7 (seeded random samples, so 16-bit low bits and alpha are exercised), every raw reference of
/// the golden corpus, large streamed images, and metadata. Pixels are compared exactly with FFmpeg's decoding in the
/// matching raw layout; the color type and bit depth are checked independently with ffprobe (decoded pixel format) and the
/// harness chunk reader (<see cref="ReferencePng"/>), metadata with ffprobe (text tags, ICC side data) and the harness
/// container walker (<see cref="EncodedFieldInspector"/>). Indexed output (palettes of every bit depth, with and without
/// alpha values, from every storage type) is decoded by FFmpeg to RGBA and compared with the pixels expanded exactly to
/// 8 bits. Round trips through the library decoder are never used here.
/// </summary>
public sealed class PngEncoderInteropTests
{
    private static readonly PixelFormat[] AllFormats = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];

    public static TheoryData<PixelFormat, PngFilter, bool> FilterCases()
    {
        var data = new TheoryData<PixelFormat, PngFilter, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var filter in Enum.GetValues<PngFilter>())
            {
                data.Add(format, filter, false);
                data.Add(format, filter, true);
            }
        }

        return data;
    }

    public static TheoryData<PixelFormat, CompressionLevel, bool> CompressionCases()
    {
        var data = new TheoryData<PixelFormat, CompressionLevel, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var level in Enum.GetValues<CompressionLevel>())
            {
                data.Add(format, level, false);
                data.Add(format, level, true);
            }
        }

        return data;
    }

    public static TheoryData<string, string> CorpusCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task EveryStorageTypeAndFilterDecodesExactlyWithFFmpeg(PixelFormat format, PngFilter filter, bool interlaced)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateRandom(format, 23, 17, seed: ((int)format * 13) + (int)filter);
        var path = await EncodeToFileAsync(image, new PngEncoder { Filter = filter, Interlaced = interlaced }, $"{format}-{filter}-{(interlaced ? "adam7" : "progressive")}");
        await AssertFFmpegDecodesExactlyAsync(ffmpeg, path, image);
        await AssertProbedPixelFormatAsync(ffmpeg, path, format, interlaced);
    }

    [Theory]
    [MemberData(nameof(CompressionCases))]
    public async Task EveryCompressionLevelDecodesExactlyWithFFmpeg(PixelFormat format, CompressionLevel level, bool interlaced)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateRandom(format, 31, 9, seed: ((int)format * 5) + (int)level);
        var path = await EncodeToFileAsync(image, new PngEncoder { CompressionLevel = level, Interlaced = interlaced }, $"{format}-{level}-{(interlaced ? "adam7" : "progressive")}");
        await AssertFFmpegDecodesExactlyAsync(ffmpeg, path, image);
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public async Task EncodedCorpusReferencesDecodeExactlyWithFFmpeg(string id, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var stills = EncoderSources.GetStills(fixture, pixelFormat);
        for (var i = 0; i < stills.Count; i++)
        {
            var (name, expected) = stills[i];
            using var image = EncoderSources.CreateImage(expected, pixelFormat);

            // Alternate settings across frames so that the corpus covers interlacing and both filter strategies
            var encoder = new PngEncoder { Interlaced = i % 2 == 1, Filter = i % 3 == 2 ? PngFilter.Paeth : PngFilter.Adaptive };
            var path = await EncodeToFileAsync(image, encoder, $"{id.Replace('/', '-')}-{format}-{name.Replace(' ', '-')}");
            var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, GetRawFormat(pixelFormat), expected.Width, expected.Height, XunitCancellationToken));
            var result = PixelBufferComparer.Compare(expected, RawPixelBuffer.Create(expected.Width, expected.Height, expected.Layout, decoded), ComparisonPolicy.Exact, $"{id} {name} encoded as {pixelFormat}, decoded by ffmpeg {ffmpeg.Version}");
            Assert.True(result.IsMatch, result.Describe());
        }
    }

    public static TheoryData<PixelFormat, int, int, bool> IndexedCases()
    {
        // The number of colors and the bit depth of the palette indexes
        (int Colors, int BitDepth)[] palettes = [(2, 1), (4, 2), (16, 4), (200, 8)];
        var data = new TheoryData<PixelFormat, int, int, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var (colors, bitDepth) in palettes)
            {
                data.Add(format, colors, bitDepth, false);
                data.Add(format, colors, bitDepth, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IndexedCases))]
    public async Task IndexedOutputDecodesExactlyWithFFmpeg(PixelFormat format, int colors, int bitDepth, bool interlaced)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateWithColors(format, 23, 17, colors, seed: (int)format);
        var path = await EncodeToFileAsync(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = interlaced }, $"indexed-{format}-{colors}-{(interlaced ? "adam7" : "progressive")}");
        await AssertFFmpegDecodesIndexedExactlyAsync(ffmpeg, path, ImageSnapshots.CaptureFrame(image.Frames[0]), $"{format} with {colors} colors");

        // FFmpeg decodes every palette bit depth to 8-bit indexes; the harness reader checks the IHDR fields directly
        using var probe = await ffmpeg.ProbeAsync(path, XunitCancellationToken);
        var stream = probe.RootElement.GetProperty("streams")[0];
        Assert.Equal("png", stream.GetProperty("codec_name").GetString());
        Assert.Equal("pal8", stream.GetProperty("pix_fmt").GetString());
        var reference = ReferencePng.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.Equal((3, bitDepth, interlaced ? 1 : 0), (reference.ColorType, reference.BitDepth, reference.InterlaceMethod));
    }

    public static TheoryData<PixelFormat, int, bool> MinimumSizeCases()
    {
        var data = new TheoryData<PixelFormat, int, bool>();
        foreach (var format in AllFormats)
        {
            foreach (var colors in new[] { 2, 4, 16, 300 })
            {
                data.Add(format, colors, false);
                data.Add(format, colors, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MinimumSizeCases))]
    public async Task MinimumSizeOutputDecodesExactlyWithFFmpeg(PixelFormat format, int colors, bool interlaced)
    {
        // Palettes, grayscale, color keys and reduced bit depths: FFmpeg decodes them, compared exactly in 16-bit RGBA
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateWithColors(format, 23, 17, colors, seed: (int)format + colors);
        await AssertMinimumSizeDecodesExactlyAsync(ffmpeg, image, $"minimum-size-{format}-{colors}-{(interlaced ? "adam7" : "progressive")}", interlaced);
    }

    [Theory]
    [MemberData(nameof(MinimumSizeCases))]
    public async Task LayoutOptimizationOutputDecodesExactlyWithFFmpeg(PixelFormat format, int colors, bool interlaced)
    {
        // The layout of the analysis alone: palettes with tRNS, grayscale of 1 to 16 bits, RGB without alpha
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateWithColors(format, 23, 17, colors, seed: (int)format + colors);
        await AssertOptimizedDecodesExactlyAsync(ffmpeg, image, new PngEncoder { Optimization = PngOptimization.Layout, Interlaced = interlaced }, $"layout-{format}-{colors}-{(interlaced ? "adam7" : "progressive")}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task MinimumSizeGrayLevelsDecodeExactlyWithFFmpeg(int bitDepth)
    {
        // Gray levels of 1, 2 or 4 bits, with a color key on one of them: sub-byte grayscale and its tRNS
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var step = 255 / ((1 << bitDepth) - 1);
        var pixels = new Rgba32[29 * 13];
        for (var i = 0; i < pixels.Length; i++)
        {
            var level = (byte)(i * 7 % (1 << bitDepth) * step);
            pixels[i] = i % 11 == 0 ? new Rgba32(0, 0, 0, 0) : new Rgba32(level, level, level, (byte)(level == 0 ? 0 : 255));
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, 29, 13);
        await AssertMinimumSizeDecodesExactlyAsync(ffmpeg, image, $"minimum-size-gray-{bitDepth}", interlaced: false);
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public async Task IndexedCorpusReferencesDecodeExactlyWithFFmpeg(string id, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var stills = EncoderSources.GetStills(fixture, pixelFormat);
        for (var i = 0; i < stills.Count; i++)
        {
            // The references a palette cannot hold exactly are rejected (conformance tests): nothing to decode
            var (name, expected) = stills[i];
            if (!IndexedPngExpectation.Create(expected).IsStorable)
                continue;

            using var image = EncoderSources.CreateImage(expected, pixelFormat);
            var encoder = new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = i % 2 == 1, Filter = i % 3 == 2 ? PngFilter.Paeth : PngFilter.Adaptive };
            var path = await EncodeToFileAsync(image, encoder, $"indexed-{id.Replace('/', '-')}-{format}-{name.Replace(' ', '-')}");
            await AssertFFmpegDecodesIndexedExactlyAsync(ffmpeg, path, expected, $"{id} {name} encoded as indexed {pixelFormat}");
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba64, false)]
    [InlineData(PixelFormat.Rgb24, true)]
    public async Task LargeStreamedImagesDecodeExactlyWithFFmpeg(PixelFormat format, bool interlaced)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateRandom(format, 640, 360, seed: 77);
        var name = $"large-{format}-{(interlaced ? "adam7" : "progressive")}";
        var path = InteropSettings.GetArtifactsDirectory(nameof(PngEncoderInteropTests)) / (name + ".png");

        // Asynchronous, non-seekable output: written in one pass in many IDAT chunks
        using (var stream = new TestOutputStream { ForbidSynchronousWrites = true })
        {
            await image.SaveAsync(stream, new PngEncoder { CompressionLevel = CompressionLevel.NoCompression, Interlaced = interlaced }, XunitCancellationToken);
            await File.WriteAllBytesAsync(path, stream.ToArray(), XunitCancellationToken);
        }

        Assert.HasCountGreaterThan(20, ReferencePng.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken)).GetChunks("IDAT"));
        await AssertFFmpegDecodesExactlyAsync(ffmpeg, path, image);
    }

    [Fact]
    public async Task MetadataIsReadByFFmpegAndTheIndependentWalker()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateRandom(PixelFormat.Rgba32, 8, 6, seed: 3);
        var icc = CreateIccProfile();
        var longText = string.Concat(Enumerable.Repeat("compressible text ", 200));
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(icc));
        image.Metadata.Resolution = new ImageResolution(300, 300);
        image.Metadata.Orientation = ExifOrientation.LeftBottom;
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8.ToArray()));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Plain ASCII title"));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Copyright", "© café")); // Latin-1 tEXt (FFmpeg exports it lossily: walker only)
        image.Metadata.TextEntries.Add(new ImageTextEntry("Description", longText));
        image.Metadata.TextEntries.Add(new ImageTextEntry("Author", "中文 été"));
        var path = await EncodeToFileAsync(image, new PngEncoder(), "metadata");

        // Pixels and structure: FFmpeg decodes the file with every ancillary chunk present
        await AssertFFmpegDecodesExactlyAsync(ffmpeg, path, image);
        using var probe = await ffmpeg.ProbeAsync(path, XunitCancellationToken);
        var frame = probe.RootElement.GetProperty("frames")[0];
        var tags = frame.GetProperty("tags");
        Assert.Equal("Plain ASCII title", tags.GetProperty("Title").GetString());
        Assert.Equal(longText, tags.GetProperty("Description").GetString()); // zTXt, inflated by FFmpeg
        Assert.Contains(frame.GetProperty("side_data_list").EnumerateArray(), item => item.GetProperty("side_data_type").GetString() == "ICC profile");
        Assert.Equal("1:1", frame.GetProperty("sample_aspect_ratio").GetString()); // pHYs with equal densities

        // Every field, independently of FFmpeg's metadata export
        var fields = EncodedFieldInspector.Inspect("png", await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.Equal(icc, fields.Icc!.Value.ToArray());
        Assert.Equal(11811, fields.Resolution!.X);
        Assert.Equal(11811, fields.Resolution.Y);
        Assert.NotNull(fields.Exif);
        Assert.NotNull(fields.Xmp);
        Assert.Equal(["Title: Plain ASCII title", "Copyright: © café", "Description: " + longText, "Author: 中文 été"], fields.Text.Select(entry => entry.ToString()));
        Assert.Equal(["IHDR", "iCCP", "pHYs", "eXIf", "iTXt", "tEXt", "tEXt", "zTXt", "iTXt", "IDAT", "IEND"], ReferencePng.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken)).ChunkTypes);
    }

    internal static string GetRawFormat(PixelFormat format) => format switch
    {
        PixelFormat.Gray8 => "gray",
        PixelFormat.Gray16 => "gray16le",
        PixelFormat.Rgb24 => "rgb24",
        PixelFormat.Rgba64 => "rgba64le",
        _ => "rgba",
    };

    private static async Task<FullPath> EncodeToFileAsync(Image image, PngEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(PngEncoderInteropTests)) / (name + ".png");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    private static async Task AssertFFmpegDecodesExactlyAsync(FFmpegTool ffmpeg, FullPath path, Image image)
    {
        var expected = ImageSnapshots.CaptureFrame(image.Frames[0]);
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, GetRawFormat(image.PixelFormat), image.Width, image.Height, XunitCancellationToken));
        var result = PixelBufferComparer.Compare(expected, RawPixelBuffer.Create(image.Width, image.Height, expected.Layout, decoded), ComparisonPolicy.Exact, $"{path.Name} decoded by ffmpeg {ffmpeg.Version}");
        Assert.True(result.IsMatch, result.Describe());
    }

    private static Task AssertMinimumSizeDecodesExactlyAsync(FFmpegTool ffmpeg, Image image, string name, bool interlaced)
        => AssertOptimizedDecodesExactlyAsync(ffmpeg, image, new PngEncoder { Optimization = PngOptimization.MinimumSize, Interlaced = interlaced }, name);

    private static async Task AssertOptimizedDecodesExactlyAsync(FFmpegTool ffmpeg, Image image, PngEncoder encoder, string name)
    {
        var path = await EncodeToFileAsync(image, encoder, name);
        var reference = OptimizedPngOutputVerifier.VerifyStill(await File.ReadAllBytesAsync(path, XunitCancellationToken), image, name);
        var (rawFormat, layout) = GetNativeRawFormat(reference);
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, rawFormat, image.Width, image.Height, XunitCancellationToken));
        var expected = ReferencePng.ToRgba16(ImageSnapshots.CaptureFrame(image.Frames[0]));
        var result = PixelBufferComparer.Compare(expected, ReferencePng.ToRgba16(RawPixelBuffer.Create(image.Width, image.Height, layout, decoded)), ComparisonPolicy.Exact, $"{name} decoded by ffmpeg {ffmpeg.Version} as {rawFormat}");
        Assert.True(result.IsMatch, result.Describe());
    }

    /// <summary>
    /// Gets the raw layout FFmpeg decodes a PNG to without converting its samples (its pixel format conversions to wider
    /// layouts are not exact): gray, gray and alpha (a gray color key), RGB, or RGBA (an RGB color key, a palette).
    /// </summary>
    /// <param name="reference">The parsed file.</param>
    /// <returns>The FFmpeg pixel format and the matching harness layout.</returns>
    internal static (string RawFormat, RawPixelLayout Layout) GetNativeRawFormat(ReferencePng reference)
    {
        var key = reference.ChunkTypes.Contains("tRNS") && reference.ColorType is 0 or 2;
        return (reference.ColorType, reference.BitDepth == 16, key) switch
        {
            (0, false, false) => ("gray", RawPixelLayout.Gray8),
            (0, true, false) => ("gray16le", RawPixelLayout.Gray16Le),
            (0, false, true) or (4, false, _) => ("ya8", RawPixelLayout.GrayAlpha8),
            (0, true, true) or (4, true, _) => ("ya16le", RawPixelLayout.GrayAlpha16Le),
            (2, false, false) => ("rgb24", RawPixelLayout.Rgb8),
            (2, false, true) or (3, _, _) or (6, false, _) => ("rgba", RawPixelLayout.Rgba8),
            _ => ("rgba64le", RawPixelLayout.Rgba16Le),
        };
    }

    private static async Task AssertFFmpegDecodesIndexedExactlyAsync(FFmpegTool ffmpeg, FullPath path, RawPixelBuffer source, string context)
    {
        // The palette entries carry their alpha: FFmpeg expands the indexes to straight RGBA
        var expected = IndexedPngExpectation.ToRgba8(source);
        var decoded = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "rgba", source.Width, source.Height, XunitCancellationToken));
        var result = PixelBufferComparer.Compare(expected, RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, decoded), ComparisonPolicy.Exact, $"{context}, decoded by ffmpeg {ffmpeg.Version}");
        Assert.True(result.IsMatch, result.Describe());
    }

    /// <summary>
    /// Creates an image that has exactly <paramref name="colors"/> distinct colors, in runs: gray levels for the grayscale
    /// pixel formats, otherwise colors that are fully transparent, partially transparent or opaque when the pixel format
    /// has alpha. The samples of the 16-bit pixel formats are multiples of 257.
    /// </summary>
    internal static Image CreateWithColors(PixelFormat format, int width, int height, int colors, int seed)
    {
        var hasAlpha = format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64;
        var isGray = format is PixelFormat.Gray8 or PixelFormat.Gray16;
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        var data = new byte[width * height * bytesPerPixel];
        for (var i = 0; i < width * height; i++)
        {
            // Every color once, then runs of two to four pixels; multiplying by an odd number keeps the red samples distinct
            var entry = i < colors ? i : ((i / ((i % 3) + 2)) + seed) % colors;
            var red = (byte)((entry + seed) * 151);
            var green = isGray ? red : (byte)(entry * 89);
            var blue = isGray ? red : (byte)(255 - (entry * 3));
            var alpha = !hasAlpha ? byte.MaxValue : (entry % 4) switch
            {
                0 => (byte)0,
                1 => (byte)(1 + (entry % 254)),
                _ => byte.MaxValue,
            };

            var pixel = data.AsSpan(i * bytesPerPixel, bytesPerPixel);
            switch (format)
            {
                case PixelFormat.Gray8:
                    pixel[0] = red;
                    break;
                case PixelFormat.Gray16:
                    BitConverter.TryWriteBytes(pixel, (ushort)(red * 257));
                    break;
                case PixelFormat.Rgb24:
                    (pixel[0], pixel[1], pixel[2]) = (red, green, blue);
                    break;
                case PixelFormat.Rgba32:
                    (pixel[0], pixel[1], pixel[2], pixel[3]) = (red, green, blue, alpha);
                    break;
                case PixelFormat.Bgra32:
                    (pixel[0], pixel[1], pixel[2], pixel[3]) = (blue, green, red, alpha);
                    break;
                default:
                    BitConverter.TryWriteBytes(pixel, (ushort)(red * 257));
                    BitConverter.TryWriteBytes(pixel[2..], (ushort)(green * 257));
                    BitConverter.TryWriteBytes(pixel[4..], (ushort)(blue * 257));
                    BitConverter.TryWriteBytes(pixel[6..], (ushort)(alpha * 257));
                    break;
            }
        }

        return format switch
        {
            PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(data, width, height),
            PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(data, width, height),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(data, width, height),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(data, width, height),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(data, width, height),
            _ => Image.ImportPixelBytes<Gray16>(data, width, height),
        };
    }

    private static async Task AssertProbedPixelFormatAsync(FFmpegTool ffmpeg, FullPath path, PixelFormat format, bool interlaced)
    {
        // FFmpeg reports the PNG color type and bit depth as its decoded pixel format
        using var probe = await ffmpeg.ProbeAsync(path, XunitCancellationToken);
        var stream = probe.RootElement.GetProperty("streams")[0];
        Assert.Equal("png", stream.GetProperty("codec_name").GetString());
        var expected = format switch
        {
            PixelFormat.Gray8 => "gray",
            PixelFormat.Gray16 => "gray16be",
            PixelFormat.Rgb24 => "rgb24",
            PixelFormat.Rgba64 => "rgba64be",
            _ => "rgba",
        };

        Assert.Equal(expected, stream.GetProperty("pix_fmt").GetString());

        // The harness reader checks the IHDR fields directly
        var reference = ReferencePng.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.Equal(interlaced ? 1 : 0, reference.InterlaceMethod);
        Assert.Equal(format is PixelFormat.Rgba64 or PixelFormat.Gray16 ? 16 : 8, reference.BitDepth);
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image CreateRandom(PixelFormat format, int width, int height, int seed)
    {
        var data = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
        new Random(seed).NextBytes(data);
        return format switch
        {
            PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(data, width, height),
            PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(data, width, height),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(data, width, height),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(data, width, height),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(data, width, height),
            _ => Image.ImportPixelBytes<Gray16>(data, width, height),
        };
    }

    private static byte[] CreateIccProfile()
    {
        // A minimal structurally valid RGB display profile: 128-byte header, an empty tag table
        var data = new byte[132];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
        "mntr"u8.CopyTo(data.AsSpan(12));
        "RGB "u8.CopyTo(data.AsSpan(16));
        "XYZ "u8.CopyTo(data.AsSpan(20));
        "acsp"u8.CopyTo(data.AsSpan(36));
        return data;
    }
}
