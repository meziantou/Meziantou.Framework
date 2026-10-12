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
    [InlineData("lossless", 8, "444", 2, 0)]
    [InlineData("lossless", 10, "400", 5, 0)]
    [InlineData("lossless", 12, "444", 0, 3)]
    [InlineData("lossy", 8, "420", 0, 0)]
    [InlineData("lossy", 8, "420", 5, 0)]
    [InlineData("lossy", 8, "420", 9, 0)]
    [InlineData("lossy", 10, "420", 3, 4)]
    [InlineData("lossy", 12, "422", 4, 0)]
    [InlineData("lossy", 8, "444", 6, 0)]
    [InlineData("lossy", 10, "444", 2, 0)]
    [InlineData("lossy", 8, "400", 7, 2)]
    [InlineData("lossy", 12, "400", 1, 0)]
    public async Task TheReferenceDecodersReconstructTheFramesOfAnImageSequence(string compression, int bitDepth, string format, int effort, int keyFrameInterval)
    {
        const int FrameCount = 6;
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var dav1d = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Dav1d);
        var libaom = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Libaom);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AvifEncoderInteropTests));
        foreach (var (width, height, quality) in new[] { (9, 5, 50), (64, 64, 20), (150, 77, 60), (97, 130, 95) })
        {
            // The images of a sequence are predicted from the image before them: a scene that moves, with a part that changes
            var name = string.Create(CultureInfo.InvariantCulture, $"frames-{compression}-{bitDepth}-{format}-e{effort}-k{keyFrameInterval}-{width}x{height}-q{quality}");
            using var image = CreateMovingImage(width, height, format is "400", bitDepth, seed: width + quality, frame: 0);
            for (var i = 1; i < FrameCount; i++)
            {
                using var next = CreateMovingImage(width, height, format is "400", bitDepth, seed: width + quality, frame: i);
                image.AppendFrame(next.Frames[0]);
            }

            var encoder = new AvifEncoder
            {
                Compression = compression is "lossy" ? AvifCompression.Lossy : AvifCompression.Lossless,
                Quality = quality,
                Effort = effort,
                BitDepth = bitDepth,
                KeyFrameInterval = keyFrameInterval,
                ChromaSubsampling = format switch
                {
                    "444" => AvifChromaSubsampling.Ratio444,
                    "422" => AvifChromaSubsampling.Ratio422,
                    _ => AvifChromaSubsampling.Ratio420,
                },
            };

            var path = await SaveAsync(image, encoder, name);
            var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
            var reader = ReferenceAvif.Parse(data);
            foreach (var (track, kind) in new[] { (reader.ColorTrack, "color"), (reader.AlphaTrack, "alpha") })
            {
                if (track is null)
                    continue;

                // The key frames are the ones of the interval, and the only samples with a sequence header
                Assert.HasCount(FrameCount, track.Samples);
                var samples = track.Samples.Select(sample => data.AsSpan(checked((int)sample.Offset), sample.Size).ToArray()).ToList();
                for (var i = 0; i < FrameCount; i++)
                {
                    var isKeyFrame = i is 0 || (keyFrameInterval > 0 && i % keyFrameInterval is 0);
                    Assert.Equal(isKeyFrame, track.SyncSamples is null || track.SyncSamples.Contains((uint)(i + 1)));
                    Assert.Equal(isKeyFrame, (samples[i][0] >> 3) is 1);
                }

                // The track as a raw stream: each sample is a temporal unit, which starts with a temporal delimiter
                var stream = directory / $"{name}-{kind}.obu";
                await File.WriteAllBytesAsync(stream, [.. samples.SelectMany(sample => (byte[])[0x12, 0x00, .. sample])], XunitCancellationToken);
                var frames = await dav1d.ProbeAsync(stream, XunitCancellationToken);
                Assert.HasCount(FrameCount, frames);
                Assert.Equal((width, height), (frames[0].Width, frames[0].Height));
                var first = await dav1d.DecodeAsync(stream, frames[0].PixelFormat, directory / $"{name}-{kind}-dav1d", XunitCancellationToken);
                var second = await libaom.DecodeAsync(stream, frames[0].PixelFormat, directory / $"{name}-{kind}-libaom", XunitCancellationToken);
                var library = DecodeFramesWithLibrary(samples, name);
                Assert.HasCount(FrameCount, first);
                Assert.HasCount(FrameCount, second);
                Assert.HasCount(FrameCount, library);
                for (var i = 0; i < FrameCount; i++)
                {
                    Assert.True(first[i].AsSpan().SequenceEqual(second[i]), $"{name} {kind}: dav1d and libaom reconstruct different planes for frame {i} ({frames[0].PixelFormat}).");
                    Assert.True(first[i].AsSpan().SequenceEqual(library[i]), $"{name} {kind}: the reference decoders and the library reconstruct different planes for frame {i} ({frames[0].PixelFormat}; first difference at byte {first[i].AsSpan().CommonPrefixLength(library[i])} of {first[i].Length} and {library[i].Length}).");
                }
            }
        }
    }

    [Theory]
    [InlineData("lossless", 8, "444", 0)]
    [InlineData("lossless", 8, "444", 5)]
    [InlineData("lossless", 12, "444", 3)]
    [InlineData("lossless", 10, "400", 7)]
    [InlineData("lossy", 8, "420", 2)]
    [InlineData("lossy", 10, "420", 6)]
    [InlineData("lossy", 8, "422", 4)]
    [InlineData("lossy", 12, "444", 9)]
    [InlineData("lossy", 8, "400", 1)]
    public async Task TheReferenceDecodersReconstructScreenContent(string compression, int bitDepth, string format, int effort)
    {
        // Images of few colors are coded with palettes, in the key frame and in the frames that follow it
        const int FrameCount = 3;
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var libavif = await RequiredTools.LibAvifAsync();
        var dav1d = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Dav1d);
        var libaom = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Libaom);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AvifEncoderInteropTests));
        foreach (var (width, height, quality) in new[] { (24, 17, 50), (64, 64, 80), (150, 77, 30), (90, 131, 95) })
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"screen-{compression}-{bitDepth}-{format}-e{effort}-{width}x{height}-q{quality}");
            using var image = CreateScreenImage(width, height, format is "400", bitDepth, seed: width + quality, frame: 0);
            for (var i = 1; i < FrameCount; i++)
            {
                using var next = CreateScreenImage(width, height, format is "400", bitDepth, seed: width + quality, frame: i);
                image.AppendFrame(next.Frames[0]);
            }

            for (var i = 0; i < FrameCount; i++)
            {
                image.Frames[i].Metadata.Duration = new FrameDuration(1, 5);
            }

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
            var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
            var reader = ReferenceAvif.Parse(data);
            foreach (var (track, kind) in new[] { (reader.ColorTrack, "color"), (reader.AlphaTrack, "alpha") })
            {
                if (track is null)
                    continue;

                var samples = track.Samples.Select(sample => data.AsSpan(checked((int)sample.Offset), sample.Size).ToArray()).ToList();
                var stream = directory / $"{name}-{kind}.obu";
                await File.WriteAllBytesAsync(stream, [.. samples.SelectMany(sample => (byte[])[0x12, 0x00, .. sample])], XunitCancellationToken);
                var frames = await dav1d.ProbeAsync(stream, XunitCancellationToken);
                Assert.HasCount(FrameCount, frames);
                var first = await dav1d.DecodeAsync(stream, frames[0].PixelFormat, directory / $"{name}-{kind}-dav1d", XunitCancellationToken);
                var second = await libaom.DecodeAsync(stream, frames[0].PixelFormat, directory / $"{name}-{kind}-libaom", XunitCancellationToken);
                var library = DecodeFramesWithLibrary(samples, name, out var tools);
                Assert.HasCount(FrameCount, first);
                Assert.HasCount(FrameCount, second);
                for (var i = 0; i < FrameCount; i++)
                {
                    Assert.True(first[i].AsSpan().SequenceEqual(second[i]), $"{name} {kind}: dav1d and libaom reconstruct different planes for frame {i} ({frames[0].PixelFormat}).");
                    Assert.True(first[i].AsSpan().SequenceEqual(library[i]), $"{name} {kind}: the reference decoders and the library reconstruct different planes for frame {i} ({frames[0].PixelFormat}; first difference at byte {first[i].AsSpan().CommonPrefixLength(library[i])} of {first[i].Length} and {library[i].Length}).");
                }

                // The color planes of an image that is large enough for the detection of screen content have palettes
                if (kind is "color" && width >= 90)
                    Assert.True(tools.HasFlag(Av1Tools.Palette), $"{name}: no block of the color planes has a palette ({tools}).");
            }

            if (compression is "lossless" && bitDepth is 8)
            {
                var decoded = await libavif.DecodeAllFramesAsync(path, XunitCancellationToken);
                Assert.HasCount(FrameCount, decoded);
                for (var i = 0; i < FrameCount; i++)
                {
                    AssertSamples(ImageSnapshots.CaptureFrame(image.Frames[i]), decoded[i].Pixels, $"{name}: frame {i} decoded by avifdec {libavif.Version}");
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
    [InlineData(null)]
    [InlineData(1)]
    public async Task ImageSequencesWithoutDurationAreReadByAvifDec(int? totalPlays)
    {
        // libavif rejects an edit that is repeated and has no duration: such a sequence has no edit list
        var libavif = await RequiredTools.LibAvifAsync();
        using var image = CreateImage(20, 12, monochrome: false, bitDepth: 8, seed: 40, transparent: false);
        for (var i = 1; i < 3; i++)
        {
            using var frame = CreateImage(20, 12, monochrome: false, bitDepth: 8, seed: 40 + i, transparent: false);
            image.AppendFrame(frame.Frames[0]);
        }

        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var path = await SaveAsync(image, new AvifEncoder(), $"sequence-without-duration-{totalPlays?.ToString(CultureInfo.InvariantCulture) ?? "forever"}");
        var info = await libavif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((20, 12, 3), (info.Width, info.Height, info.FrameCount));
        Assert.Equal([0L, 0L, 0L], info.Frames.Select(frame => frame.Duration));
        Assert.True(info.HasRepetitionCount);
        Assert.Equal(totalPlays is null, info.IsRepetitionCountUnknown);
        Assert.Equal(totalPlays is null ? null : 0, info.RepetitionCount);

        var decoded = await libavif.DecodeAllFramesAsync(path, XunitCancellationToken);
        Assert.HasCount(3, decoded);
        for (var i = 0; i < 3; i++)
        {
            AssertSamples(ImageSnapshots.CaptureFrame(image.Frames[i]), decoded[i].Pixels, $"frame {i} decoded by avifdec {libavif.Version}");
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task LightLevelsMasteringDisplayAndPixelShapeAreReportedByOtherReaders(bool isSequence)
    {
        var libavif = await RequiredTools.LibAvifAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        using var image = CreateImage(16, 12, monochrome: false, bitDepth: 10, seed: 31, transparent: false);
        if (isSequence)
        {
            using var second = CreateImage(16, 12, monochrome: false, bitDepth: 10, seed: 32, transparent: false);
            image.AppendFrame(second.Frames[0]);
            image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            image.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        }

        // The BT.2020 primaries and D65 (SMPTE ST 2086 units of 0.00002), a display of 0.005 to 1,000 cd/m2
        image.Metadata.Cicp = new CicpColorDescription(9, 16, 9, fullRange: false);
        image.Metadata.ContentLightLevel = new ContentLightLevel(1000, 400);
        image.Metadata.MasteringDisplay = new MasteringDisplayColorVolume(redX: 35400, redY: 14600, greenX: 8500, greenY: 39850, blueX: 6550, blueY: 2300, whitePointX: 15635, whitePointY: 16450, maxLuminance: 10_000_000, minLuminance: 50);
        image.Metadata.PixelAspectRatio = new PixelAspectRatio(4, 3);
        var path = await SaveAsync(image, new AvifEncoder { Compression = AvifCompression.Lossy, BitDepth = 10 }, $"hdr-metadata-{(isSequence ? "sequence" : "still")}");

        // libavif reads the properties of the primary item, which is the first image of a sequence
        var info = await libavif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((1000, 400), info.ContentLightLevel);
        Assert.Equal((4, 3), info.PixelAspectRatio);

        // FFmpeg names the primaries of the mastering display: the order in which the box stores them is the one it reads.
        // It reads the boxes of the sample entry of a track, and for a still image the properties of the item (but not the
        // shape of its pixels; with an alpha item, it attaches them to the wrong stream, whoever wrote the file)
        using var probe = await ffmpeg.ProbeStreamsAsync(path, XunitCancellationToken);
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var stream = isSequence
            ? streams.Single(candidate => candidate.GetProperty("nb_frames").GetString() == "2" && candidate.GetProperty("pix_fmt").GetString() == "yuv420p10le")
            : Assert.Single(streams);
        var sideData = stream.GetProperty("side_data_list").EnumerateArray().ToArray();
        var display = sideData.Single(data => data.GetProperty("side_data_type").GetString() == "Mastering display metadata");
        Assert.Equal("35400/50000", display.GetProperty("red_x").GetString());
        Assert.Equal("14600/50000", display.GetProperty("red_y").GetString());
        Assert.Equal("8500/50000", display.GetProperty("green_x").GetString());
        Assert.Equal("39850/50000", display.GetProperty("green_y").GetString());
        Assert.Equal("6550/50000", display.GetProperty("blue_x").GetString());
        Assert.Equal("2300/50000", display.GetProperty("blue_y").GetString());
        Assert.Equal("15635/50000", display.GetProperty("white_point_x").GetString());
        Assert.Equal("16450/50000", display.GetProperty("white_point_y").GetString());
        Assert.Equal("50/10000", display.GetProperty("min_luminance").GetString());
        Assert.Equal("10000000/10000", display.GetProperty("max_luminance").GetString());

        var lightLevel = sideData.Single(data => data.GetProperty("side_data_type").GetString() == "Content light level metadata");
        Assert.Equal(1000, lightLevel.GetProperty("max_content").GetInt32());
        Assert.Equal(400, lightLevel.GetProperty("max_average").GetInt32());
        if (isSequence)
        {
            Assert.Equal("4:3", stream.GetProperty("sample_aspect_ratio").GetString());

            // The alpha track has the shape of the pixels, and nothing that describes colors
            var alpha = streams.Single(candidate => candidate.GetProperty("nb_frames").GetString() == "2" && candidate.GetProperty("pix_fmt").GetString() != "yuv420p10le");
            Assert.Equal("4:3", alpha.GetProperty("sample_aspect_ratio").GetString());
            Assert.False(alpha.TryGetProperty("side_data_list", out _));
        }
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

    [Theory]
    [InlineData(AvifCompression.Lossless, 0)]
    [InlineData(AvifCompression.Lossless, 2)]
    [InlineData(AvifCompression.Lossy, 0)]
    public async Task ImageSequencesDecodeWithAppleImageIO(AvifCompression compression, int keyFrameInterval)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        const int FrameCount = 5;
        var (swift, script) = RequiredTools.AppleImageIO();
        using var image = CreateMovingImage(72, 40, monochrome: false, bitDepth: 8, seed: 9, frame: 0);
        for (var i = 1; i < FrameCount; i++)
        {
            using var next = CreateMovingImage(72, 40, monochrome: false, bitDepth: 8, seed: 9, frame: i);
            image.AppendFrame(next.Frames[0]);
        }

        for (var i = 0; i < FrameCount; i++)
        {
            image.Frames[i].Metadata.Duration = new FrameDuration(1, 10);
        }

        var path = await SaveAsync(image, new AvifEncoder { Compression = compression, Quality = 95, ChromaSubsampling = AvifChromaSubsampling.Ratio444, KeyFrameInterval = keyFrameInterval }, $"imageio-sequence-{compression}-k{keyFrameInterval}");
        var result = await ProcessRunner.RunAsync(swift, [script, path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed (exit code {result.ExitCode}): {result.StandardError}");

        // The images that are predicted from the image before them are decoded like the key frames
        var header = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1].Split(' ');
        Assert.Equal(["frames", "5", "72", "40", "8"], header);
        var frameLength = 72 * 40 * 4;
        Assert.Equal(FrameCount * frameLength, result.StandardOutput.Length);
        var bound = compression is AvifCompression.Lossless ? 0 : 12;
        for (var i = 0; i < FrameCount; i++)
        {
            var actual = RawPixelBuffer.Create(72, 40, RawPixelLayout.Rgba8, result.StandardOutput.Span.Slice(i * frameLength, frameLength));
            var expected = ImageSnapshots.CaptureFrame(image.Frames[i]);
            for (var y = 0; y < expected.Height; y++)
            {
                for (var x = 0; x < expected.Width; x++)
                {
                    var alpha = expected.GetSample(x, y, 3);
                    Assert.Equal(alpha, actual.GetSample(x, y, 3));
                    for (var channel = 0; alpha is not 0 && channel < 3; channel++)
                    {
                        var difference = Math.Abs(expected.GetSample(x, y, channel) - actual.GetSample(x, y, channel));
                        Assert.True(difference <= bound, $"frame {i}, pixel ({x}, {y}), channel {channel}: {expected.GetSample(x, y, channel)} was decoded as {actual.GetSample(x, y, channel)} by Apple ImageIO");
                    }
                }
            }
        }
    }

    // The planes of the frame that the library decodes from the units of an item, in the layout of FFmpeg's raw video:
    // plane after plane, one byte per sample for 8-bit streams and two little-endian bytes otherwise
    private static byte[] DecodeWithLibrary(byte[] payload, string name) => Assert.Single(DecodeFramesWithLibrary([payload], name));

    private static List<byte[]> DecodeFramesWithLibrary(IReadOnlyList<byte[]> payloads, string name) => DecodeFramesWithLibrary(payloads, name, out _);

    // The same for the samples of a track, which the same decoder decodes one after the other; tools receives the coding
    // tools of the frames
    private static List<byte[]> DecodeFramesWithLibrary(IReadOnlyList<byte[]> payloads, string name, out Av1Tools tools)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        using var decoder = new Av1Decoder(scope);
        var frames = new List<byte[]>();
        tools = Av1Tools.None;
        foreach (var payload in payloads)
        {
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

                frames.Add(output.ToArray());
            }
            finally
            {
                frame.Release();
            }

            tools |= decoder.Tools;
        }

        return frames;
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

    // An image of a sequence: the content of CreateImage, which moves by (3, -2) pixels and a half from an image to the
    // next one (the scene is two times larger), under a square that moves faster and a corner of noise that changes
    private static Image CreateMovingImage(int width, int height, bool monochrome, int bitDepth, int seed, int frame)
    {
        const int Margin = 24;
        var deep = bitDepth > 8;
        var max = deep ? 65535 : 255;
        var sceneWidth = 2 * (width + (2 * Margin));
        var sceneHeight = 2 * (height + (2 * Margin));
        var scene = CreateSamples(sceneWidth, sceneHeight, deep, seed, transparent: true);
        var random = new Random(seed + (1000 * frame));
        var samples = new int[width * height * 4];
        var offsetX = (2 * Margin) + (7 * frame);
        var offsetY = (2 * Margin) - (5 * frame);
        var size = Math.Max(2, Math.Min(width, height) / 4);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    // The average of 2x2 samples of the scene: a motion of half a pixel is a motion of one sample of the scene
                    var sum = 0;
                    for (var i = 0; i < 4; i++)
                    {
                        sum += scene[(((((2 * y) + offsetY + (i >> 1)) * sceneWidth) + (2 * x) + offsetX + (i & 1)) * 4) + channel];
                    }

                    var value = (sum + 2) / 4;
                    var inSquare = x >= (11 * frame) % width && x < ((11 * frame) % width) + size && y >= 2 * frame && y < (2 * frame) + size;
                    if (inSquare)
                        value = channel is 3 ? max : max * (channel + 1) / 4;

                    if (x < width / 5 && y >= height - (height / 5))
                        value = channel is 3 ? max : random.Next(max + 1);

                    samples[(((y * width) + x) * 4) + channel] = value;
                }
            }
        }

        return CreateImage(samples, width, height, monochrome, deep);
    }

    // The content of a screen: areas of a few colors with marks of one pixel, edges that blend two colors in a few steps
    // (more colors than a palette holds), a band that changes from an image to the next one, a corner of noise, and
    // pixels that are transparent or not
    private static Image CreateScreenImage(int width, int height, bool monochrome, int bitDepth, int seed, int frame)
    {
        var deep = bitDepth > 8;
        var max = deep ? 65535 : 255;
        var random = new Random(seed);
        var palette = new int[12][];
        for (var i = 0; i < palette.Length; i++)
        {
            // Colors that survive the reduction of 16-bit samples to the bit depth of the file
            palette[i] = [random.Next(256) * (max / 255), random.Next(256) * (max / 255), random.Next(256) * (max / 255)];
        }

        var samples = new int[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var area = (x / 19) + ((y / 11) * 5) + (y >= height / 2 ? frame : 0);
                var mark = ((x * 5) + (y * 11) + (x * y)) % (4 + (area % 5));
                var color = palette[(area + (mark is 0 ? 3 : 0) + (mark is 1 ? 7 : 0)) % palette.Length];
                var other = palette[(area + 1) % palette.Length];
                var blend = (area % 3) is 2 && mark is 2 ? ((x + (2 * y)) % 11) + 1 : 0;
                var index = ((y * width) + x) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = ((color[channel] / (max / 255) * (12 - blend)) + (other[channel] / (max / 255) * blend)) / 12 * (max / 255);
                    samples[index + channel] = x >= width - 6 && y < 6 ? random.Next(256) * (max / 255) : value;
                }

                samples[index + 3] = (area % 7) is 0 && mark is not 0 ? 0 : max;
            }
        }

        return CreateImage(samples, width, height, monochrome, deep);
    }

    // Smooth content with a few edges and some noise, in 8-bit or 16-bit pixels
    private static Image CreateImage(int width, int height, bool monochrome, int bitDepth, int seed, bool transparent)
    {
        var deep = bitDepth > 8;
        return CreateImage(CreateSamples(width, height, deep, seed, transparent), width, height, monochrome, deep);
    }

    private static int[] CreateSamples(int width, int height, bool deep, int seed, bool transparent)
    {
        var random = new Random(seed);
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

        return samples;
    }

    private static Image CreateImage(int[] samples, int width, int height, bool monochrome, bool deep)
    {
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
