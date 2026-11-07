using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Verifies the pinned libheif tools (downloaded from meziantou/prebuilt) and the helpers that read what <c>heif-dec</c>
/// and <c>heif-info</c> write, without the library under test.
/// </summary>
public sealed class LibHeifToolTests
{
    [Fact]
    public async Task LibHeifReportsThePinnedVersionAndOnlyHevcCodecs()
    {
        var heif = await RequiredTools.LibHeifAsync();

        // The three tools report the version the package declares (checked for each of them when they are located)
        Assert.Equal(PrebuiltTools.HeifEnc.Version, heif.Version);
        Assert.Equal(PrebuiltTools.HeifDec.Version, heif.Version);
        Assert.Equal(PrebuiltTools.HeifInfo.Version, heif.Version);

        // The build has one encoder and one decoder, both for HEVC: a file of another codec cannot be written or read by mistake
        var encoder = Assert.Single(heif.Encoders);
        Assert.StartsWith("HEIC: x265 = ", encoder, StringComparison.Ordinal);
        Assert.Contains(PrebuiltTools.X265.Version, encoder, StringComparison.Ordinal);
        var decoder = Assert.Single(heif.Decoders);
        Assert.StartsWith("HEIC: libde265 = ", decoder, StringComparison.Ordinal);
        Assert.Contains(PrebuiltTools.Dec265.Version, decoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// The plumbing of the HEIC reference: raw samples that FFmpeg wrote as a PNG file come back, bit for bit, from a lossless
    /// encode by heif-enc decoded by heif-dec to a PNG file and read by <see cref="HeifDecImage"/>. At 10 and 12 bits,
    /// heif-enc takes the most significant bits of the 16-bit samples and heif-dec writes them back in the most significant
    /// bits. Distinct values per channel and pixel detect channel swaps, byte swaps and row-order errors. The 5x3 image is
    /// padded by heif-enc to the 64x64 picture that x265 codes, and cropped by a clean aperture.
    /// </summary>
    [Theory]
    [InlineData("rgba", 4, 8)]
    [InlineData("rgb24", 3, 8)]
    [InlineData("gray", 1, 8)]
    [InlineData("rgb48le", 3, 10)]
    [InlineData("rgba64le", 4, 10)]
    [InlineData("gray16le", 1, 10)]
    [InlineData("rgb48le", 3, 12)]
    [InlineData("rgba64le", 4, 12)]
    [InlineData("gray16le", 1, 12)]
    public async Task HeifDecOutputIsReadExactly(string pixelFormat, int channels, int depth)
    {
        const int Width = 5;
        const int Height = 3;
        var heif = await RequiredTools.LibHeifAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LibHeifToolTests));
        var name = string.Create(CultureInfo.InvariantCulture, $"pattern-{Width}x{Height}-{pixelFormat}-{depth}");
        var rawPath = directory / (name + ".raw");
        var pngPath = directory / (name + ".png");
        var heicPath = directory / (name + ".heic");

        // 16-bit samples whose low bits are not zero: heif-enc must drop them
        var bytesPerSample = depth > 8 ? 2 : 1;
        var samples = new int[Width * Height * channels];
        var raw = new byte[samples.Length * bytesPerSample];
        for (var i = 0; i < samples.Length; i++)
        {
            if (depth > 8)
            {
                samples[i] = ((i * 2731) + 1117) & 0xFFFF;
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(i * 2), (ushort)samples[i]);
            }
            else
            {
                samples[i] = (byte)((i * 37) + 11);
                raw[i] = (byte)samples[i];
            }
        }

        await File.WriteAllBytesAsync(rawPath, raw, XunitCancellationToken);
        var pngPixelFormat = depth > 8 ? pixelFormat.Replace("le", "be", StringComparison.Ordinal) : pixelFormat;
        var encode = await ffmpeg.RunAsync(["-y", "-f", "rawvideo", "-pixel_format", pixelFormat, "-video_size", string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}"), "-i", rawPath, "-frames:v", "1", "-pix_fmt", pngPixelFormat, pngPath], XunitCancellationToken);
        Assert.Equal(0, encode.ExitCode, message: encode.StandardError);
        string[] depthOptions = depth > 8 ? ["-b", depth.ToString(CultureInfo.InvariantCulture)] : [];
        await heif.EncodeAsync(["-L", .. depthOptions, pngPath], heicPath, XunitCancellationToken);

        var info = await heif.GetInfoAsync(heicPath, XunitCancellationToken);
        Assert.Equal("image/heic", info.MimeType);
        Assert.Equal((Width, Height, depth), (info.Width, info.Height, info.BitDepth));
        Assert.Equal(channels == 1 ? "monochrome" : "RGB", info.ColorSpace);
        Assert.Equal(channels == 4, info.HasAlpha);
        Assert.False(info.IsPremultiplied);
        Assert.Equal<(int, int, int, int)?>((0, 0, 64 - Width, 64 - Height), info.Crop);
        Assert.Null(info.Tiles);
        Assert.Empty(info.Tracks);

        var decoded = await heif.DecodeAsync(heicPath, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal((Width, Height), (decoded.Width, decoded.Height));
        Assert.Equal(depth > 8 ? 16 : 8, decoded.FileBitDepth);
        Assert.Equal(channels == 4, decoded.HasAlpha);
        var maximum = (1 << depth) - 1;
        var pixels = decoded.GetPixels(depth);
        for (var pixel = 0; pixel < Width * Height; pixel++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var source = (channels, channel) switch
                {
                    (_, 3) => channels == 4 ? samples[(pixel * 4) + 3] : -1,
                    (1, _) => samples[pixel],
                    _ => samples[(pixel * channels) + channel],
                };

                // The sample of the image: the most significant bits of the source
                var expected = source < 0 ? maximum : depth > 8 ? source >> (16 - depth) : source;
                Assert.Equal(expected, decoded.GetSample(pixel % Width, pixel / Width, channel, depth), message: $"{name}: pixel {pixel} channel {channel}.");
                Assert.Equal((int)(((expected * 65535L) + (maximum / 2)) / maximum), pixels.GetSample(pixel % Width, pixel / Width, channel), message: $"{name}: pixel {pixel} channel {channel}, scaled to 16 bits.");
            }
        }
    }

    /// <summary>
    /// <c>--cut-tiles</c> writes a <c>grid</c> item whose tiles are independent HEVC images, which heif-info and the reader of
    /// the test harness describe alike, and heif-dec assembles the tiles and crops the last row and column.
    /// </summary>
    [Fact]
    public async Task GridsAreWritten()
    {
        const int Width = 150;
        const int Height = 100;
        var heif = await RequiredTools.LibHeifAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LibHeifToolTests));
        var (pngPath, samples) = await WritePatternAsync(directory / "grid-150x100.png", Width, Height, seed: 0);
        var heicPath = directory / "grid-150x100.heic";
        await heif.EncodeAsync(["-L", "--cut-tiles", "64", pngPath], heicPath, XunitCancellationToken);

        var info = await heif.GetInfoAsync(heicPath, XunitCancellationToken);
        Assert.Equal((Width, Height, 8, "RGB"), (info.Width, info.Height, info.BitDepth, info.ColorSpace));
        Assert.Equal<(int, int, int, int)?>((3, 2, 64, 64), info.Tiles);

        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(heicPath, XunitCancellationToken));
        var grid = reader.PrimaryItem;
        Assert.NotNull(grid);
        Assert.Equal("grid", grid.Type);
        Assert.Equal((2, 3, Width, Height), reader.ReadGridDescriptor(grid));
        var tiles = reader.GetGridTiles(grid);
        Assert.Equal(6, tiles.Count);
        Assert.All(tiles, tile => Assert.Equal(("hvc1", (64L, 64L)), (tile.Type, tile.Size)));

        var decoded = await heif.DecodeAsync(heicPath, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal((Width, Height), (decoded.Width, decoded.Height));
        AssertSamples(samples, decoded, "grid");
    }

    /// <summary>
    /// <c>-S</c> writes the numbered files of a directory as the samples of a track of an image sequence, with the duration
    /// and the repetitions that were asked for; <c>heif-dec -S --ignore-editlist</c> returns each image once.
    /// </summary>
    [Fact]
    public async Task SequencesAreWritten()
    {
        const int Width = 64;
        const int Height = 64;
        var heif = await RequiredTools.LibHeifAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LibHeifToolTests)) / "sequence";
        Directory.CreateDirectory(directory);
        var frames = new List<int[]>();
        FullPath first = default;
        for (var i = 0; i < 3; i++)
        {
            var (path, samples) = await WritePatternAsync(directory / string.Create(CultureInfo.InvariantCulture, $"frame-{i + 1:D3}.png"), Width, Height, seed: i);
            frames.Add(samples);
            first = i == 0 ? path : first;
        }

        var heicPath = directory / "sequence.heic";
        await heif.EncodeAsync(["-S", "-L", "--gop-structure", "intra-only", "--timebase", "1000", "--duration", "40", "--repetitions", "3", first], heicPath, XunitCancellationToken);

        var info = await heif.GetInfoAsync(heicPath, XunitCancellationToken);
        Assert.Equal("image/heic-sequence", info.MimeType);
        Assert.Equal<long?>(1000, info.MovieTimeScale);
        Assert.Equal([new HeifInfoTrack(1, "pict", (Width, Height), 3, "hvc1")], info.Tracks);

        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(heicPath, XunitCancellationToken));
        var track = reader.ColorTrack;
        Assert.NotNull(track);
        Assert.Equal((1000u, 3), (track.Timescale, track.Samples.Count));
        Assert.Equal([40u, 40u, 40u], track.Samples.Select(sample => sample.Duration));
        Assert.Equal<int?>(3, track.TotalPlays);

        var decoded = await heif.DecodeSequenceAsync(heicPath, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal(3, decoded.Count);
        for (var i = 0; i < decoded.Count; i++)
        {
            Assert.Equal(40, decoded[i].Duration);
            AssertSamples(frames[i], decoded[i].Image, string.Create(CultureInfo.InvariantCulture, $"sequence frame {i}"));
        }
    }

    /// <summary>
    /// The planes heif-dec writes to a YUV4MPEG2 file for an 8-bit 4:2:0 image are the planes of the HEVC stream of the item,
    /// as the reference decoders decode the stream that <see cref="ReferenceHeif"/> extracts: the reader of the container,
    /// the conversion to a byte stream and the reader of the planes agree with libheif.
    /// </summary>
    [Fact]
    public async Task HeifDecPlanesAreTheDecodedPlanes()
    {
        const int Width = 80;
        const int Height = 64;
        var heif = await RequiredTools.LibHeifAsync();
        var decoders = await RequiredTools.HevcReferenceDecodersAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LibHeifToolTests));
        var (pngPath, _) = await WritePatternAsync(directory / "planes-80x64.png", Width, Height, seed: 5);
        var heicPath = directory / "planes-80x64.heic";
        await heif.EncodeAsync(["-q", "70", pngPath], heicPath, XunitCancellationToken);
        var info = await heif.GetInfoAsync(heicPath, XunitCancellationToken);
        Assert.Equal((Width, Height, 8, "YCbCr, 4:2:0"), (info.Width, info.Height, info.BitDepth, info.ColorSpace));

        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(heicPath, XunitCancellationToken));
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        var streamPath = directory / "planes-80x64.hevc";
        await File.WriteAllBytesAsync(streamPath, item.HevcConfiguration!.ToByteStream(reader.ReadItemData(item)), XunitCancellationToken);
        var planes = await heif.DecodePlanesAsync(heicPath, XunitCancellationToken);
        Assert.Equal((Width, Height), (planes.Width, planes.Height));
        byte[] expected = [.. planes.Y.Span, .. planes.U.Span, .. planes.V.Span];
        foreach (var decoder in decoders.Where(decoder => decoder.Name is HevcReferenceDecoder.FFmpeg or HevcReferenceDecoder.Dec265))
        {
            var result = await decoder.DecodeAsync(streamPath, directory / "planes-80x64", XunitCancellationToken);
            Assert.True(result.Succeeded, $"{decoder.Name}: {result.Error}");
            Assert.True(expected.AsSpan().SequenceEqual(result.Planes.Span), $"The planes of heif-dec are not the planes {decoder.Name} decodes.");
        }
    }

    /// <summary>Writes a PNG file of distinct RGB samples with FFmpeg, and returns the samples.</summary>
    private static async Task<(FullPath Path, int[] Samples)> WritePatternAsync(FullPath path, int width, int height, int seed)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var raw = new byte[width * height * 3];
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = (byte)((i * 37) + (seed * 53) + ((i / 251) * 7) + 11);
        }

        var rawPath = FullPath.FromPath(path + ".raw");
        await File.WriteAllBytesAsync(rawPath, raw, XunitCancellationToken);
        var encode = await ffmpeg.RunAsync(["-y", "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-i", rawPath, "-frames:v", "1", "-pix_fmt", "rgb24", path], XunitCancellationToken);
        Assert.Equal(0, encode.ExitCode, message: encode.StandardError);
        return (path, [.. raw.Select(value => (int)value)]);
    }

    private static void AssertSamples(int[] expected, HeifDecImage actual, string context)
    {
        Assert.Equal(expected.Length, actual.Width * actual.Height * 3);
        for (var i = 0; i < expected.Length; i++)
        {
            var pixel = i / 3;
            Assert.Equal(expected[i], actual.GetSample(pixel % actual.Width, pixel / actual.Width, i % 3, bitDepth: 8), message: $"{context}: pixel {pixel} channel {i % 3}.");
        }
    }
}
