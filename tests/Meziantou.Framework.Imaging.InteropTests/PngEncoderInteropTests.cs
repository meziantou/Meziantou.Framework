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
/// container walker (<see cref="EncodedFieldInspector"/>). Round trips through the library decoder are never used here.
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
