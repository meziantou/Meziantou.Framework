using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.WebP;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// WebP encoding (lossless and lossy) of every raw reference of the golden corpus, verified without external tools by
/// the independent harness reader <see cref="ReferenceWebP"/> (strict container rules, its own bit-by-bit VP8L and ALPH
/// decoder): lossless output must reproduce every 8-bit sample, including the colors of fully transparent pixels, at every
/// effort; lossy output must have the expected structure, an exact alpha plane, and a bounded reconstruction error (decoded
/// by the library: the reference reader does not decode VP8 color). The reader itself is validated against the libwebp
/// encoded corpus inputs. dwebp, anim_dump and FFmpeg decode the same kinds of output in <c>InteropTests/WebPEncoderInteropTests</c>.
/// </summary>
public sealed class WebPEncoderConformanceTests
{
    private static readonly WebPEncoder[] LosslessEncoders =
    [
        new WebPEncoder { Effort = 0, AllowBitDepthReduction = true },
        new WebPEncoder { Effort = 3, AllowBitDepthReduction = true },
        new WebPEncoder { AllowBitDepthReduction = true },
        new WebPEncoder { Effort = 9, AllowBitDepthReduction = true },
    ];

    private static readonly WebPEncoder[] LossyEncoders =
    [
        new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 50, Effort = 0, AllowBitDepthReduction = true },
        new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 75, AllowBitDepthReduction = true },
        new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 95, Effort = 9, AllowBitDepthReduction = true },
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    public static TheoryData<string> AnimatedFixtures => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Expected.Animation is not null)];

    public static TheoryData<string> WebPFixtures => [.. GoldenCorpus.Default.GetIds(format: "webp", kind: FixtureKinds.Valid).Where(id => !GoldenCorpus.Default.Get(id).Entry.HasFeature("webp.chunk=unknown"))];

    [Fact]
    public void ReferenceDistanceMapMatchesTheLibraryTable()
    {
        // Two independent transcriptions of the specification table: a typo in either is caught here
        for (var code = 1; code <= 120; code++)
        {
            Assert.Equal(ReferenceVp8L.DistanceMapOffsets[code - 1], Vp8LDecoder.GetDistanceMapEntry(code));
        }
    }

    [Theory]
    [MemberData(nameof(WebPFixtures))]
    public void ReferenceReaderDecodesTheLibWebPCorpus(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var reader = ReferenceWebP.Parse(fixture.ReadInput());
        Assert.Equal((fixture.Expected.Width, fixture.Expected.Height), (reader.Width, reader.Height));
        Assert.Equal(fixture.Expected.FrameCount, reader.Frames.Count);
        if (fixture.Expected.Animation is not null)
        {
            // No compositor: every frame rectangle must decode (lossless) or carry a decodable alpha plane (lossy)
            for (var i = 0; i < reader.Frames.Count; i++)
            {
                Assert.HasCount(reader.Frames[i].Width * reader.Frames[i].Height, reader.DecodeAlpha(i));
            }

            return;
        }

        var expected = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        if (reader.Frames[0].IsLossless)
        {
            var actual = reader.DecodeFrame(0);
            if (!reader.GetLosslessAlphaHint(0))
            {
                actual = RawPixelBuffer.Create(actual.Width, actual.Height, RawPixelLayout.Rgba8, SetOpaque(actual.Span.ToArray()));
            }

            AssertExact(expected, actual, id);
        }
        else
        {
            Assert.Equal(GetAlpha(expected), reader.DecodeAlpha(0));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LosslessEncodingPreservesEverySample(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var expected = ToRgba8(source);
            var hasAlpha = !GetAlpha(expected).All(alpha => alpha == byte.MaxValue);
            foreach (var encoder in LosslessEncoders)
            {
                var context = $"{id} {name} as {pixelFormat} (effort {encoder.Effort})";
                var data = Encode(image, encoder);
                var reader = ReferenceWebP.Parse(data);
                Assert.False(reader.IsExtended, $"{context}: a still image without metadata uses the simple layout.");
                var frame = Assert.Single(reader.Frames);
                Assert.True(frame.IsLossless, context);
                Assert.Equal(hasAlpha, reader.GetLosslessAlphaHint(0));
                AssertExact(expected, reader.DecodeFrame(0), context);

                // The library reads its output back to the same samples
                using var decoded = Image.Load<Rgba32>(data);
                AssertExact(expected, ImageSnapshots.CaptureFrame(decoded.Frames[0]), context + " decoded by the library");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LossyEncodingHasTheExpectedStructureAndExactAlpha(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        if (pixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Rgb24 or PixelFormat.Rgba64))
            return; // the other layouts hold the same samples

        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var expected = ToRgba8(source);
            var alpha = GetAlpha(expected);
            var hasAlpha = !alpha.All(value => value == byte.MaxValue);
            foreach (var encoder in LossyEncoders)
            {
                var context = $"{id} {name} as {pixelFormat} (quality {encoder.Quality}, effort {encoder.Effort})";
                var data = Encode(image, encoder);
                var reader = ReferenceWebP.Parse(data);
                var frame = Assert.Single(reader.Frames);
                Assert.False(frame.IsLossless, context);
                Assert.Equal(hasAlpha, reader.IsExtended);
                Assert.Equal(hasAlpha, frame.Alpha is not null);
                Assert.Equal(hasAlpha, reader.HasAlphaFlag);
                Assert.Equal(alpha, reader.DecodeAlpha(0)); // alpha is always lossless

                using var decoded = Image.Load<Rgba32>(data);
                var actual = ImageSnapshots.CaptureFrame(decoded.Frames[0]);
                Assert.Equal(alpha, GetAlpha(actual));
                var (psnr, mean) = MeasureVisible(expected, actual);
                // Luma PSNR floors just below the minimum measured on the corpus. Tiny saturated pixel-art frames next to
                // transparent pixels are the worst cases (22.5-25 dB at qualities 75 and 95; libwebp 1.3.2 cwebp -exact scores
                // the same within 1 dB on them); photographic content (JPEG references, lossy and detailed WebP fixtures)
                // measured at least 23.8, 29.6 and 40.7 dB at qualities 50, 75 and 95.
                var photographic = fixture.Entry.Format == "jpeg" || id.StartsWith("webp/lossy", StringComparison.Ordinal) || id == "webp/lossless-detail-transforms";
                var minimum = (encoder.Quality, photographic) switch
                {
                    (>= 95, true) => 40.0,
                    (>= 75, true) => 29.0,
                    (_, true) => 23.0,
                    (>= 95, false) => 23.0,
                    (>= 75, false) => 22.0,
                    _ => 19.0,
                };
                Assert.True(psnr >= minimum, string.Create(CultureInfo.InvariantCulture, $"{context}: PSNR {psnr:F2} dB (mean error {mean:F2}) is below {minimum} dB."));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AnimatedFixtures))]
    public void AnimationsAreWrittenAsFullCanvasFrames(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        // 16-bit animations are reduced to 8 bits by the test (the encoder input is Rgba32 either way)
        var sourceFormat = fixture.CanonicalLayout == RawPixelLayout.Rgba8 ? PixelFormat.Rgba32 : PixelFormat.Rgba64;
        var frames = EncoderSources.GetStills(fixture, sourceFormat).Where(still => still.Name != "poster").Select(still => ToRgba8(still.Pixels)).ToList();

        using var image = EncoderSources.CreateImage(frames[0], PixelFormat.Rgba32);
        for (var i = 1; i < frames.Count; i++)
        {
            using var frame = EncoderSources.CreateImage(frames[i], PixelFormat.Rgba32);
            image.AppendFrame(frame.Frames[0]);
        }

        var durations = new List<RationalDuration>();
        for (var i = 0; i < frames.Count; i++)
        {
            var duration = fixture.GetDuration(i);
            durations.Add(duration);
            image.Frames[i].Metadata.Duration = new FrameDuration(duration.Numerator, duration.Denominator);
        }

        var totalPlays = fixture.Expected.Animation!.TotalPlays is > ushort.MaxValue ? null : fixture.Expected.Animation.TotalPlays;
        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };

        foreach (var encoder in new[] { new WebPEncoder(), new WebPEncoder { Effort = 0 } })
        {
            var data = Encode(image, encoder);
            var reader = ReferenceWebP.Parse(data);
            Assert.True(reader.IsExtended);
            Assert.NotNull(reader.Animation);
            Assert.Equal(totalPlays ?? 0, reader.Animation.LoopCount);
            Assert.Equal(0u, reader.Animation.BackgroundColor);
            Assert.Equal(frames.Count, reader.Frames.Count);
            Assert.Equal(frames.Any(frame => GetAlpha(frame).Any(alpha => alpha != byte.MaxValue)), reader.HasAlphaFlag);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = reader.Frames[i];
                Assert.Equal((0, 0, image.Width, image.Height, false, false), (frame.X, frame.Y, frame.Width, frame.Height, frame.AlphaBlend, frame.DisposeToBackground));

                // Milliseconds rounded to nearest, ties up (the default FrameDurationRounding.RoundToNearest)
                var milliseconds = ((durations[i].Numerator * 1000) + (durations[i].Denominator / 2)) / durations[i].Denominator;
                Assert.Equal(milliseconds, frame.DurationMilliseconds);
                AssertExact(frames[i], reader.DecodeFrame(i), $"{id} frame {i}");
            }

            using var decoded = Image.Load<Rgba32>(data);
            Assert.Equal(frames.Count, decoded.Frames.Count);
            for (var i = 0; i < frames.Count; i++)
            {
                AssertExact(frames[i], ImageSnapshots.CaptureFrame(decoded.Frames[i]), $"{id} frame {i} decoded by the library");
            }
        }
    }

    private static byte[] Encode(Image image, WebPEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Straight 8-bit RGBA of a reference in any layout (gray replicated, 16-bit samples rounded with <c>(v * 255 + 32767) / 65535</c>).</summary>
    private static RawPixelBuffer ToRgba8(RawPixelBuffer source)
    {
        var result = new byte[source.Width * source.Height * 4];
        var channels = source.Layout.ChannelCount;
        var sixteen = source.Layout.BytesPerSample == 2;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var offset = 4 * ((y * source.Width) + x);
                for (var c = 0; c < 4; c++)
                {
                    int value;
                    if (channels >= 3)
                    {
                        value = c < channels ? source.GetSample(x, y, c) : (sixteen ? 65535 : 255);
                    }
                    else
                    {
                        // Gray (and gray + alpha) layouts
                        value = c < 3 ? source.GetSample(x, y, 0) : channels == 2 ? source.GetSample(x, y, 1) : (sixteen ? 65535 : 255);
                    }

                    result[offset + c] = sixteen ? (byte)(((value * 255) + 32767) / 65535) : (byte)value;
                }
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba8, result);
    }

    private static byte[] GetAlpha(RawPixelBuffer rgba)
    {
        var span = rgba.Span;
        var alpha = new byte[span.Length / 4];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = span[(4 * i) + 3];
        }

        return alpha;
    }

    private static byte[] SetOpaque(byte[] rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = byte.MaxValue;
        }

        return rgba;
    }

    private static void AssertExact(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        Assert.True(result.IsMatch, result.Describe());
    }

    /// <summary>
    /// PSNR of the BT.601 luma of visible (non-transparent) pixels, and the mean absolute error of their color samples.
    /// Luma is the fidelity criterion: 4:2:0 chroma subsampling legitimately averages the chroma of 2x2 blocks (pure
    /// saturated pixel art loses its per-pixel colors in every VP8 encoder), while the luma plane is coded at full resolution.
    /// </summary>
    private static (double Psnr, double Mean) MeasureVisible(RawPixelBuffer expected, RawPixelBuffer actual)
    {
        var e = expected.Span;
        var a = actual.Span;
        double squared = 0;
        double absolute = 0;
        var pixels = 0;
        for (var i = 0; i < e.Length; i += 4)
        {
            if (e[i + 3] == 0)
                continue;

            var difference = Luma(e, i) - Luma(a, i);
            squared += difference * difference;
            for (var c = 0; c < 3; c++)
            {
                absolute += Math.Abs(e[i + c] - a[i + c]);
            }

            pixels++;
        }

        if (pixels == 0 || squared == 0)
            return (double.PositiveInfinity, 0);

        return (10 * Math.Log10(255.0 * 255.0 * pixels / squared), absolute / (3 * pixels));

        static double Luma(ReadOnlySpan<byte> rgba, int offset) => (0.299 * rgba[offset]) + (0.587 * rgba[offset + 1]) + (0.114 * rgba[offset + 2]);
    }
}
