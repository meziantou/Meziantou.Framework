using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using static Meziantou.Framework.Imaging.Tests.Conformance.LosslessEncoderAssertions;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// AVIF encoding (lossless and lossy) of every raw reference of the golden corpus, verified without external tools. The
/// structure of the output is read by the independent harness reader <see cref="ReferenceAvif"/>. The harness has no AV1
/// decoder: the samples are decoded by the library, whose AV1 decoder the conformance vectors pin to the reference decoders
/// byte for byte, so a lossless output must give back every sample of the reference, and a lossy output an exact alpha
/// plane and a bounded error. avifdec, FFmpeg (dav1d and libaom) and Apple ImageIO decode the same kinds of output in
/// <c>InteropTests/AvifEncoderInteropTests</c>.
/// </summary>
public sealed class AvifEncoderConformanceTests
{
    public static TheoryData<string, string> Cases() => LosslessEncoderAssertions.Cases();

    public static TheoryData<string> AnimatedFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Expected.Animation is not null)];

    [Theory]
    [MemberData(nameof(Cases))]
    public void LosslessEncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var deep = pixelFormat is PixelFormat.Rgba64 or PixelFormat.Gray16;
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);

            // AVIF stores at most 12 bits: a 16-bit reference is rounded to 12 bits, which the test does on its own
            var expected = deep ? RoundTo12Bits(ToRgba16(source)) : ToRgba8(source);
            var hasAlpha = HasTransparency(expected);
            var isGray = pixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;

            // The slowest search on the small references only
            int[] efforts = source.Width * source.Height <= 1024 ? [0, 5, 9] : [0, 5];
            foreach (var effort in efforts)
            {
                var context = $"{id} {name} as {pixelFormat} (effort {effort})";
                var data = Encode(image, new AvifEncoder { Effort = effort, BitDepth = deep ? 12 : null });
                var reader = ReferenceAvif.Parse(data);
                Assert.Equal("avif", reader.MajorBrand);
                Assert.False(reader.IsSequence, context);
                var item = reader.PrimaryItem;
                Assert.NotNull(item);
                Assert.Equal("av01", item.Type);
                Assert.Equal((source.Width, source.Height), item.Size);
                Assert.Equal(deep ? 12 : 8, item.Configuration!.BitDepth);
                Assert.Equal(isGray, item.Configuration.IsMonochrome);
                Assert.Equal((0, 0), isGray ? (0, 0) : (item.Configuration.SubsamplingX, item.Configuration.SubsamplingY));
                Assert.Equal(hasAlpha, reader.AlphaItem is not null);
                Assert.Equal(isGray ? null : 0, isGray ? null : item.Nclx?.MatrixCoefficients);
                Assert.True(item.Nclx!.FullRange, context);

                // Every extent is inside the file, and the items do not overlap
                long end = 0;
                foreach (var entry in reader.Items.OrderBy(entry => entry.Extents[0].Offset))
                {
                    var (offset, length) = Assert.Single(entry.Extents);
                    Assert.True(offset >= end && offset + length <= data.Length, context);
                    end = offset + length;
                }

                Assert.Equal(data.Length, end);
                AssertLibraryReadsBack(data, expected, context);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LossyEncodingHasExactAlphaAndABoundedError(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        if (pixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Rgb24 or PixelFormat.Gray8))
            return; // the other layouts hold the same samples

        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var expected = ToRgba8(source);
            foreach (var (quality, subsampling, floor) in new[] { (50, AvifChromaSubsampling.Ratio444, 20.0), (90, AvifChromaSubsampling.Ratio444, 34.0), (75, AvifChromaSubsampling.Auto, 0.0) })
            {
                var context = $"{id} {name} as {pixelFormat} (quality {quality}, {subsampling})";
                var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality, ChromaSubsampling = subsampling, Effort = quality is 90 ? 5 : 0 });
                var reader = ReferenceAvif.Parse(data);
                var item = reader.PrimaryItem;
                Assert.NotNull(item);
                Assert.Equal(HasTransparency(expected), reader.AlphaItem is not null);
                if (pixelFormat is not PixelFormat.Gray8)
                {
                    Assert.Equal(subsampling is AvifChromaSubsampling.Ratio444 ? (0, 0) : (1, 1), (item.Configuration!.SubsamplingX, item.Configuration.SubsamplingY));
                    Assert.Equal(6, item.Nclx!.MatrixCoefficients);
                }

                using var decoded = Image.Load<Rgba32>(data);
                var actual = ImageSnapshots.CaptureFrame(decoded.Frames[0]);
                double squaredError = 0;
                for (var y = 0; y < expected.Height; y++)
                {
                    for (var x = 0; x < expected.Width; x++)
                    {
                        // Alpha is lossless
                        Assert.Equal(expected.GetSample(x, y, 3), actual.GetSample(x, y, 3));
                        for (var channel = 0; channel < 3; channel++)
                        {
                            double difference = expected.GetSample(x, y, channel) - actual.GetSample(x, y, channel);
                            squaredError += difference * difference;
                        }
                    }
                }

                // Without chroma subsampling the error is the one of the quantizer, whatever the content: the floors are
                // 2 dB below the lowest values measured on the corpus when the encoder was written (22.1 dB at quality 50,
                // on a reference of noise, where the rate-distortion decisions drop most coefficients). Subsampled chroma
                // has no bound for a reference of noise: only the structure and the alpha plane are checked
                var psnr = squaredError is 0 ? 100 : 10 * Math.Log10(255.0 * 255.0 * expected.Width * expected.Height * 3 / squaredError);
                Assert.True(psnr >= floor, string.Create(CultureInfo.InvariantCulture, $"{context}: PSNR {psnr:F2} dB is below {floor} dB"));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AnimatedFixtures))]
    public void AnimationsAreWrittenAsImageSequences(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);

        // 16-bit animations are reduced to 8 bits by the test
        var sourceFormat = fixture.CanonicalLayout == RawPixelLayout.Rgba8 ? PixelFormat.Rgba32 : PixelFormat.Rgba64;
        var frames = EncoderSources.GetStills(fixture, sourceFormat).Where(still => still.Name != "poster").Select(still => ToRgba8(still.Pixels)).ToList();
        if (frames.Count is 1)
            return; // an image sequence of one image is not written: the animation settings would be lost

        using var image = EncoderSources.CreateImage(frames[0], PixelFormat.Rgba32);
        for (var i = 1; i < frames.Count; i++)
        {
            using var frame = EncoderSources.CreateImage(frames[i], PixelFormat.Rgba32);
            image.AppendFrame(frame.Frames[0]);
        }

        var durations = new List<FrameDuration>();
        for (var i = 0; i < frames.Count; i++)
        {
            var duration = fixture.GetDuration(i);
            durations.Add(new FrameDuration(duration.Numerator, duration.Denominator));
            image.Frames[i].Metadata.Duration = durations[i];
        }

        // An image sequence stores its play count as a ratio of durations: a sequence without duration plays once or forever
        var totalPlays = fixture.Expected.Animation!.TotalPlays;
        if (totalPlays > 1 && durations.All(duration => duration.IsZero))
            totalPlays = null;

        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var data = Encode(image, new AvifEncoder { Effort = 0 });
        var reader = ReferenceAvif.Parse(data);
        Assert.True(reader.IsSequence);
        Assert.Equal("avis", reader.MajorBrand);
        var track = reader.ColorTrack;
        Assert.NotNull(track);
        Assert.NotNull(reader.AlphaTrack);
        Assert.HasCount(frames.Count, track.Samples);
        Assert.HasCount(frames.Count, reader.AlphaTrack.Samples);
        Assert.Equal(totalPlays, track.TotalPlays);
        for (var i = 0; i < frames.Count; i++)
        {
            // The duration of a sample over the time scale of the track is the duration of the frame, exactly
            Assert.Equal((Int128)durations[i].Numerator * track.Timescale, (Int128)track.Samples[i].Duration * durations[i].Denominator);
        }

        using var decoded = Image.Load<Rgba32>(data);
        Assert.HasCount(frames.Count, decoded.Frames);
        Assert.Equal(totalPlays, decoded.Animation?.TotalPlays);
        for (var i = 0; i < frames.Count; i++)
        {
            Assert.Equal(durations[i], decoded.Frames[i].Metadata.Duration);
            AssertExact(frames[i], ImageSnapshots.CaptureFrame(decoded.Frames[i]), $"{id} frame {i} decoded by the library");
        }
    }

    private static bool HasTransparency(RawPixelBuffer rgba)
    {
        var max = rgba.Layout.MaxSampleValue;
        for (var y = 0; y < rgba.Height; y++)
        {
            for (var x = 0; x < rgba.Width; x++)
            {
                if (rgba.GetSample(x, y, 3) != max)
                    return true;
            }
        }

        return false;
    }

    // A 16-bit sample v is coded as the 12-bit sample round(v * 4095 / 65535), which is decoded as round(q * 65535 / 4095)
    private static RawPixelBuffer RoundTo12Bits(RawPixelBuffer source)
    {
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, source.Layout);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var channel = 0; channel < source.Layout.ChannelCount; channel++)
                {
                    var coded = ((source.GetSample(x, y, channel) * 4095L) + 32767) / 65535;
                    builder.SetSample(x, y, channel, (int)(((coded * 65535) + 2047) / 4095));
                }
            }
        }

        return builder.Build();
    }
}
