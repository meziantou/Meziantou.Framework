using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The output of the AVIF encoder, read by independent implementations:
/// <list type="bullet">
/// <item><description>lossless output of every corpus reference decodes exactly in avifdec (libavif with dav1d), hidden colors included;</description></item>
/// <item><description>the AV1 streams of lossless and lossy output, in every sample format, are decoded by dav1d and by libaom (through
/// FFmpeg) to the same planes, which are the planes of the decoder of the library: the reconstruction is exactly specified,
/// so any difference is a stream that the encoder got wrong;</description></item>
/// <item><description>image sequences are read by avifdec with their durations and their play count, and every image decodes as intended;</description></item>
/// <item><description>the metadata and the transformations are reported by avifdec;</description></item>
/// <item><description>Apple ImageIO decodes still images on macOS.</description></item>
/// </list>
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class AvifEncoderInteropTests
{
    public static TheoryData<string> EncoderFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => EncoderSources.GetCases(GoldenCorpus.Default).Any(item => item.Fixture.Id == id && item.Format == PixelFormat.Rgba32))];

    [Theory]
    [MemberData(nameof(EncoderFixtures))]
    public async Task LosslessOutputDecodesExactlyInAvifDec(string id)
    {
        var libavif = await RequiredTools.LibAvifAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var (name, source) in EncoderSources.GetStills(fixture, PixelFormat.Rgba32))
        {
            using var image = EncoderSources.CreateImage(source, PixelFormat.Rgba32);
            var path = await SaveAsync(image, new AvifEncoder { Effort = 2 }, $"lossless-{id.Replace('/', '-')}-{name.Replace(' ', '-')}");
            var context = $"{id} {name}";
            var info = await libavif.GetInfoAsync(path, XunitCancellationToken);
            Assert.Equal((source.Width, source.Height, 8, "YUV444", 0, true), (info.Width, info.Height, info.BitDepth, info.Format, info.MatrixCoefficients, info.FullRange));

            // avifdec writes 16-bit samples: an 8-bit sample v is 257 v
            var decoded = await libavif.DecodeAsync(path, XunitCancellationToken);
            AssertSamples(source, decoded.Pixels, context + $" decoded by avifdec {libavif.Version}");
        }
    }

    [Theory]
    [InlineData("lossless", 8, "444", 3)]
    [InlineData("lossless", 10, "444", 5)]
    [InlineData("lossless", 12, "444", 0)]
    [InlineData("lossless", 8, "400", 9)]
    [InlineData("lossless", 12, "400", 4)]
    [InlineData("lossy", 8, "420", 0)]
    [InlineData("lossy", 8, "420", 5)]
    [InlineData("lossy", 8, "420", 9)]
    [InlineData("lossy", 10, "420", 4)]
    [InlineData("lossy", 12, "420", 2)]
    [InlineData("lossy", 8, "422", 6)]
    [InlineData("lossy", 10, "422", 3)]
    [InlineData("lossy", 8, "444", 7)]
    [InlineData("lossy", 12, "444", 1)]
    [InlineData("lossy", 8, "400", 5)]
    [InlineData("lossy", 10, "400", 8)]
    public async Task TheReferenceDecodersReconstructThePlanesOfTheLibrary(string compression, int bitDepth, string format, int effort)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var dav1d = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Dav1d);
        var libaom = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Libaom);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AvifEncoderInteropTests));
        var index = 0;
        foreach (var (width, height) in new[] { (1, 1), (17, 9), (64, 64), (150, 77) })
        {
            foreach (var quality in compression is "lossy" ? new[] { 20, 60, 95 } : [100])
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"planes-{compression}-{bitDepth}-{format}-e{effort}-{width}x{height}-q{quality}");
                using var image = CreateImage(width, height, format is "400", bitDepth, seed: index++, transparent: (index & 1) is 0);
                var encoder = new AvifEncoder
                {
                    Compression = compression is "lossy" ? AvifCompression.Lossy : AvifCompression.Lossless,
                    Quality = quality,
                    Effort = effort,
                    BitDepth = bitDepth,
                    ChromaSubsampling = format switch
                    {
                        "444" => AvifChromaSubsampling.Ratio444,
                        "422" => AvifChromaSubsampling.Ratio422,
                        _ => AvifChromaSubsampling.Ratio420,
                    },
                };

                var path = await SaveAsync(image, encoder, name);
                var reader = ReferenceAvif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
                foreach (var (item, kind) in new[] { (reader.PrimaryItem, "color"), (reader.AlphaItem, "alpha") })
                {
                    if (item is null)
                        continue;

                    // The item as a raw stream: a temporal delimiter, then its units
                    var payload = reader.ReadItemData(item);
                    var stream = directory / $"{name}-{kind}.obu";
                    await File.WriteAllBytesAsync(stream, [0x12, 0x00, .. payload], XunitCancellationToken);
                    var frames = await dav1d.ProbeAsync(stream, XunitCancellationToken);
                    var frame = Assert.Single(frames);
                    Assert.Equal((width, height), (frame.Width, frame.Height));
                    var first = Assert.Single(await dav1d.DecodeAsync(stream, frame.PixelFormat, directory / $"{name}-{kind}-dav1d", XunitCancellationToken));
                    var second = Assert.Single(await libaom.DecodeAsync(stream, frame.PixelFormat, directory / $"{name}-{kind}-libaom", XunitCancellationToken));
                    Assert.True(first.AsSpan().SequenceEqual(second), $"{name} {kind}: dav1d and libaom reconstruct different planes ({frame.PixelFormat}).");
                    var library = DecodeWithLibrary(payload, name);
                    Assert.True(first.AsSpan().SequenceEqual(library), $"{name} {kind}: the reference decoders and the library reconstruct different planes ({frame.PixelFormat}; first difference at byte {first.AsSpan().CommonPrefixLength(library)} of {first.Length} and {library.Length}).");
                }
            }
        }
    }

    [Theory]
    [InlineData(AvifCompression.Lossless, null)]
    [InlineData(AvifCompression.Lossless, 4)]
    [InlineData(AvifCompression.Lossy, 1)]
    public async Task ImageSequencesAreReadByAvifDecWithTheirTiming(AvifCompression compression, int? totalPlays)
    {
        var libavif = await RequiredTools.LibAvifAsync();
        FrameDuration[] durations = [new(1, 10), new(1, 25), new(3, 2), new(0, 1), new(1, 25), new(1, 10)];
        using var image = CreateImage(48, 30, monochrome: false, bitDepth: 8, seed: 100, transparent: true);
        for (var i = 1; i < durations.Length; i++)
        {
            using var frame = CreateImage(48, 30, monochrome: false, bitDepth: 8, seed: 100 + i, transparent: true);
            image.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < durations.Length; i++)
        {
            image.Frames[i].Metadata.Duration = durations[i];
        }

        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var path = await SaveAsync(image, new AvifEncoder { Compression = compression, Quality = 90, Effort = 3 }, $"sequence-{compression}-{totalPlays?.ToString(CultureInfo.InvariantCulture) ?? "forever"}");
        var info = await libavif.GetInfoAsync(path, XunitCancellationToken);

        // The time scale that holds 1/10, 1/25 and 3/2 exactly is 50; the repetition count is the play count minus one
        Assert.Equal((48, 30, durations.Length, 50L), (info.Width, info.Height, info.FrameCount, info.TimeScale));
        Assert.Equal([5L, 2L, 75L, 0L, 2L, 5L], info.Frames.Select(frame => frame.Duration));
        Assert.True(info.HasAlpha);
        Assert.Equal(totalPlays is null ? null : totalPlays - 1, info.RepetitionCount);

        var decoded = await libavif.DecodeAllFramesAsync(path, XunitCancellationToken);
        Assert.HasCount(durations.Length, decoded);
        for (var i = 0; i < durations.Length; i++)
        {
            var expected = ImageSnapshots.CaptureFrame(image.Frames[i]);
            if (compression is AvifCompression.Lossless)
            {
                AssertSamples(expected, decoded[i].Pixels, $"frame {i} decoded by avifdec {libavif.Version}");
            }
            else
            {
                // Alpha is lossless
                for (var y = 0; y < expected.Height; y++)
                {
                    for (var x = 0; x < expected.Width; x++)
                    {
                        Assert.Equal(expected.GetSample(x, y, 3) * 257, decoded[i].Pixels.GetSample(x, y, decoded[i].Pixels.Layout.ChannelCount - 1));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(ExifOrientation.TopLeft, null, null)]
    [InlineData(ExifOrientation.TopRight, null, 1)]
    [InlineData(ExifOrientation.BottomRight, 2, null)]
    [InlineData(ExifOrientation.BottomLeft, null, 0)]
    [InlineData(ExifOrientation.LeftTop, 1, 0)]
    [InlineData(ExifOrientation.RightTop, 3, null)]
    [InlineData(ExifOrientation.RightBottom, 3, 0)]
    [InlineData(ExifOrientation.LeftBottom, 1, null)]
    public async Task MetadataAndTransformationsAreReportedByAvifDec(ExifOrientation orientation, int? rotation, int? mirror)
    {
        var libavif = await RequiredTools.LibAvifAsync();
        using var image = CreateImage(12, 7, monochrome: false, bitDepth: 8, seed: 7, transparent: false);
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray();
        image.Metadata.Orientation = orientation;
        image.Metadata.IccProfile = IccProfile.Srgb;
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Cicp = new CicpColorDescription(1, 13, 1, fullRange: false);
        var path = await SaveAsync(image, new AvifEncoder { Compression = AvifCompression.Lossy, ChromaSubsampling = AvifChromaSubsampling.Ratio422 }, $"metadata-{orientation}");
        var info = await libavif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal(("YUV422", 1, 13, 1, false), (info.Format, info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients, info.FullRange));
        Assert.Equal(IccProfile.Srgb.Data.Length, info.IccProfileLength);
        Assert.Equal(xmp.Length, info.XmpLength);
        Assert.Null(info.ExifLength);

        // irot is a number of anti-clockwise quarter turns, imir an axis: avifdec prints both as they are stored
        Assert.Equal((rotation, mirror), (info.Rotation, info.Mirror));
    }

    [Theory]
    [InlineData(AvifCompression.Lossless, false)]
    [InlineData(AvifCompression.Lossless, true)]
    [InlineData(AvifCompression.Lossy, false)]
    public async Task StillImagesDecodeWithAppleImageIO(AvifCompression compression, bool transparent)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();
        using var image = CreateImage(40, 28, monochrome: false, bitDepth: 8, seed: 21, transparent);
        var path = await SaveAsync(image, new AvifEncoder { Compression = compression, Quality = 95, ChromaSubsampling = AvifChromaSubsampling.Ratio444 }, $"imageio-{compression}-{(transparent ? "alpha" : "opaque")}");
        // ImageIO returns an opaque image without alpha channel, which the helper only accepts with --binary-alpha
        string[] arguments = transparent ? [script, path] : [script, "--binary-alpha", path];
        var result = await ProcessRunner.RunAsync(swift, arguments, XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed (exit code {result.ExitCode}): {result.StandardError}");

        // "frames <count> <width> <height> <bits>" is the last line of the error output: the system frameworks can write
        // diagnostics before it
        var header = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1].Split(' ');
        Assert.Equal(["frames", "1", "40", "28", "8"], header);
        var actual = RawPixelBuffer.Create(40, 28, RawPixelLayout.Rgba8, result.StandardOutput.Span);
        var expected = ImageSnapshots.CaptureFrame(image.Frames[0]);

        // ImageIO returns the colors of an image without color profile as they are stored when the coding is lossless; a
        // lossy image is within the error of its quantizer. It may store another color in fully transparent pixels
        var bound = compression is AvifCompression.Lossless ? 0 : 12;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var alpha = expected.GetSample(x, y, 3);
                Assert.Equal(alpha, actual.GetSample(x, y, 3));
                for (var channel = 0; alpha is not 0 && channel < 3; channel++)
                {
                    var difference = Math.Abs(expected.GetSample(x, y, channel) - actual.GetSample(x, y, channel));
                    Assert.True(difference <= bound, $"pixel ({x}, {y}), channel {channel}: {expected.GetSample(x, y, channel)} was decoded as {actual.GetSample(x, y, channel)} by Apple ImageIO");
                }
            }
        }
    }

    // The planes of the frame that the library decodes from the units of an item, in the layout of FFmpeg's raw video:
    // plane after plane, one byte per sample for 8-bit streams and two little-endian bytes otherwise
    private static byte[] DecodeWithLibrary(byte[] payload, string name)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        using var decoder = new Av1Decoder(scope);
        decoder.SetInput(payload, 0, payload.Length);
        var frame = decoder.DecodeNextOutputFrame(XunitCancellationToken);
        Assert.NotNull(frame);
        try
        {
            using var output = new MemoryStream();
            for (var plane = 0; plane < frame.PlaneCount; plane++)
            {
                var buffer = frame.GetPlane(plane);
                for (var y = 0; y < buffer.Height; y++)
                {
                    foreach (var sample in buffer.GetRow(y))
                    {
                        output.WriteByte((byte)sample);
                        if (frame.Sequence.BitDepth > 8)
                            output.WriteByte((byte)(sample >> 8));
                    }
                }
            }

            return output.ToArray();
        }
        finally
        {
            frame.Release();
        }
    }

    // The samples of an 8-bit RGBA reference against the 16-bit samples that avifdec writes (gray, with or without alpha)
    private static void AssertSamples(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        var channels = actual.Layout.ChannelCount;
        var scale = actual.Layout.MaxSampleValue / 255;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var sample = expected.GetSample(x, y, channel) * scale;
                    if (channel is 3 && channels is 1 or 3)
                    {
                        // An image without alpha plane is opaque
                        Assert.Equal(actual.Layout.MaxSampleValue, sample, message: $"{context}: pixel ({x}, {y}) is not opaque but the image has no alpha plane.");
                        continue;
                    }

                    var actualChannel = channels >= 3 ? channel : (channel is 3 ? 1 : 0);
                    Assert.Equal(sample, actual.GetSample(x, y, actualChannel), message: $"{context}: pixel ({x}, {y}), channel {channel}.");
                }
            }
        }
    }

    private static async Task<FullPath> SaveAsync(Image image, AvifEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory(nameof(AvifEncoderInteropTests)) / (name + ".avif");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

    // Smooth content with a few edges and some noise, in 8-bit or 16-bit pixels
    private static Image CreateImage(int width, int height, bool monochrome, int bitDepth, int seed, bool transparent)
    {
        var random = new Random(seed);
        var deep = bitDepth > 8;
        var max = deep ? 65535 : 255;
        var samples = new int[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var edge = ((x / 13) + (y / 9)) % 2 * max / 5;
                var index = ((y * width) + x) * 4;
                samples[index] = Math.Clamp((int)((max / 2.0) + (max / 3.0 * Math.Sin((x + seed) / 7.0))) + edge + random.Next(-max / 50, (max / 50) + 1), 0, max);
                samples[index + 1] = Math.Clamp((int)((max / 2.0) + (max / 3.0 * Math.Cos((y + seed) / 5.0))) + random.Next(-max / 50, (max / 50) + 1), 0, max);
                samples[index + 2] = Math.Clamp(((x + y) * max / (width + height)) + edge, 0, max);
                samples[index + 3] = transparent && ((x + (2 * y)) % 7) is 0 ? random.Next(max + 1) : max;
            }
        }

        if (monochrome)
        {
            if (deep)
                return Image.ImportPixelData<Gray16>(Enumerable.Range(0, width * height).Select(i => new Gray16((ushort)samples[i * 4])).ToArray(), width, height);

            return Image.ImportPixelData<Gray8>(Enumerable.Range(0, width * height).Select(i => new Gray8((byte)samples[i * 4])).ToArray(), width, height);
        }

        if (deep)
            return Image.ImportPixelData<Rgba64>(Enumerable.Range(0, width * height).Select(i => new Rgba64((ushort)samples[i * 4], (ushort)samples[(i * 4) + 1], (ushort)samples[(i * 4) + 2], (ushort)samples[(i * 4) + 3])).ToArray(), width, height);

        return Image.ImportPixelData<Rgba32>(Enumerable.Range(0, width * height).Select(i => new Rgba32((byte)samples[i * 4], (byte)samples[(i * 4) + 1], (byte)samples[(i * 4) + 2], (byte)samples[(i * 4) + 3])).ToArray(), width, height);
    }
}
