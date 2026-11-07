using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Proves that the harness turns deliberate corruption into useful failures: changed samples (16-bit low bits included),
/// truncated/oversized buffers, channel swaps, orientation errors, alpha loss, missing frames/posters, timing and loop
/// errors, and manifest/hash/layout mismatches.
/// </summary>
public sealed class HarnessCorruptionDetectionTests
{
    private static readonly GoldenAssertOptions NoPreviews = new() { WritePreviews = false };

    [Fact]
    public void ChangedSampleIsReportedWithFullContext()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var corrupted = reference.WithSample(3, 2, 1, reference.GetSample(3, 2, 1) ^ 0x01);

        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, corrupted, NoPreviews));
        var message = exception.Message;
        Assert.Contains("Fixture 'png/rgba8-corner-markers', frame 0", message, StringComparison.Ordinal);
        Assert.Contains("1 of 80 samples violate the policy", message, StringComparison.Ordinal);
        Assert.Contains("(3,2) G: expected " + reference.GetSample(3, 2, 1).ToString(CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
        Assert.Contains("max error 1 at (3,2) channel G", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedLowBitOf16BitSampleIsReportedWithNumericDetail()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba16-low-bit-gradient");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba16Le);
        var original = reference.GetSample(1, 2, 2);
        var corrupted = reference.WithSample(1, 2, 2, original ^ 0x0001);

        var result = PixelBufferComparer.Compare(reference, corrupted, fixture.Policy, "16-bit");
        Assert.False(result.IsMatch);
        Assert.Equal(1, result.ViolatingSampleCount);
        var mismatch = Assert.Single(result.Mismatches);
        Assert.Equal((1, 2, 'B', original, original ^ 1), (mismatch.X, mismatch.Y, mismatch.ChannelName, mismatch.Expected, mismatch.Actual));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"0x{original:X4}"), result.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void EightBitReductionOf16BitSamplesIsReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/gray16-low-bit-gradient");
        var reference = fixture.GetFrame(0, RawPixelLayout.Gray16Le);
        var builder = new RawPixelBufferBuilder(reference.Width, reference.Height, RawPixelLayout.Gray16Le);
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                builder.SetSample(x, y, 0, (reference.GetSample(x, y, 0) >> 8) * 257);
            }
        }

        var result = PixelBufferComparer.Compare(reference, builder.Build(), ComparisonPolicy.Exact, "gray16");
        Assert.Equal(reference.SampleCount, result.ViolatingSampleCount);
        Assert.Contains(result.Hints, hint => hint.Contains("reduced to 8 bits", StringComparison.Ordinal));
    }

    [Fact]
    public void ByteSwapped16BitSamplesAreReported()
    {
        var reference = GoldenCorpus.Default.Get("png/rgba16-low-bit-gradient").GetFrame(0, RawPixelLayout.Rgba16Le);
        var swapped = reference.ToArray();
        for (var i = 0; i < swapped.Length; i += 2)
        {
            (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
        }

        var result = PixelBufferComparer.CompareBytes(reference, swapped, ComparisonPolicy.Exact);
        Assert.False(result.IsMatch);
        Assert.Contains(result.Hints, hint => hint.Contains("byte-swapped", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, "truncated by 1 bytes")]
    [InlineData(-20, "truncated by 20 bytes")]
    [InlineData(1, "oversized by 1 bytes")]
    [InlineData(20, "oversized by 20 bytes")]
    public void TruncatedOrOversizedBuffersAreReported(int delta, string expected)
    {
        var fixture = GoldenCorpus.Default.Get("png/rgb8-odd-width");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var bytes = new byte[reference.ByteLength + delta];
        reference.Span[..Math.Min(bytes.Length, reference.ByteLength)].CopyTo(bytes);

        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameBytesMatch(fixture, 0, RawPixelLayout.Rgba8, bytes, NoPreviews));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("7x3 rgba8 requires exactly 84 bytes (3 rows of 28 bytes)", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => RawPixelBuffer.Create(7, 3, RawPixelLayout.Rgba8, bytes));
        Assert.Throws<ArgumentException>(() => new RawPixelBufferBuilder(7, 3, RawPixelLayout.Rgba8).SetRow(0, bytes.AsSpan(0, 27)));
    }

    [Theory]
    [InlineData("png/rgba8-corner-markers")]
    [InlineData("jpeg/baseline-420")]
    [InlineData("jpeg/progressive-420-partial-mcu")]
    public void ChannelSwapIsReportedEvenWithTolerance(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var result = PixelBufferComparer.Compare(reference, reference.SwapChannels(0, 2), fixture.Policy, id);
        Assert.False(result.IsMatch);
        Assert.Contains(result.Hints, hint => hint.Contains("R and B swapped", StringComparison.Ordinal));
    }

    [Fact]
    public void OrientationErrorsAreReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        AssertHint(reference, reference.FlipVertical(), "vertically flipped");
        AssertHint(reference, reference.FlipHorizontal(), "horizontally flipped");
        AssertHint(reference, reference.Rotate180(), "rotated 180 degrees");

        var rotated = PixelBufferComparer.Compare(reference, reference.Rotate90Clockwise(), ComparisonPolicy.Exact, "rotated");
        Assert.Contains("Dimension mismatch: expected 5x4, actual 4x5", rotated.StructuralError, StringComparison.Ordinal);
        Assert.Contains(rotated.Hints, hint => hint.Contains("rotated 90 degrees clockwise", StringComparison.Ordinal));

        var square = GoldenCorpus.Default.Get("png/palette8-256-colors").GetFrame(0, RawPixelLayout.Rgba8);
        AssertHint(square, square.Transpose(), "transposed");
    }

    [Fact]
    public void ApplyingExifOrientationIsReportedForTheOrientationFixture()
    {
        // Decoders must not apply EXIF orientation: the oriented (rotated) image does not match, even with the JPEG tolerance
        var fixture = GoldenCorpus.Default.Get("jpeg/exif-orientation-6");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var oriented = reference.Rotate90Clockwise();
        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, oriented, NoPreviews));
        Assert.Contains("Dimension mismatch: expected 8x6, actual 6x8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rotated 90 degrees clockwise", exception.Message, StringComparison.Ordinal);

        var snapshot = GoldenCorpusTests.SnapshotFromReferences(fixture, RawPixelLayout.Rgba8);
        var wrongOrientation = new DecodedImageSnapshot { Frames = snapshot.Frames, HasAnimation = false, Orientation = 1 };
        Assert.Contains("Orientation: expected 6, actual 1", Assert.Throws<GoldenAssertionException>(() => GoldenAssert.ImageMatches(fixture, wrongOrientation, NoPreviews)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlphaLossIsReportedEvenWithTolerance()
    {
        var reference = GoldenCorpus.Default.Get("png/graya8-alpha-ramp").GetFrame(0, RawPixelLayout.Rgba8);
        var tolerance = ComparisonPolicy.Tolerance(ComparisonPolicy.AbsoluteErrorCeiling, ComparisonPolicy.MeanAbsoluteErrorCeiling, "Deliberately loose test policy: proves alpha is never covered by tolerances.");
        var opaque = reference.WithChannel(3, 255);
        var result = PixelBufferComparer.Compare(reference, opaque, tolerance, "alpha");
        Assert.False(result.IsMatch);
        Assert.Contains(result.Failures, failure => failure.Contains("alpha is always compared exactly", StringComparison.Ordinal));
        Assert.Contains(result.Hints, hint => hint.Contains("alpha was lost", StringComparison.Ordinal));

        // A single alpha step is also a violation
        Assert.False(PixelBufferComparer.Matches(reference, reference.WithSample(1, 0, 3, reference.GetSample(1, 0, 3) + 1), tolerance));
    }

    [Fact]
    public void HiddenColorsOfTransparentPixelsAreComparedExactly()
    {
        var reference = GoldenCorpus.Default.Get("png/gray8-trns-key").GetFrame(0, RawPixelLayout.Rgba8);
        var transparent = Enumerable.Range(0, reference.Width).First(x => reference.GetSample(x, 0, 3) == 0);
        var result = PixelBufferComparer.Compare(reference, reference.WithSample(transparent, 0, 0, 0), ComparisonPolicy.Exact, "hidden");
        Assert.False(result.IsMatch);
        Assert.Contains(result.Hints, hint => hint.Contains("fully transparent", StringComparison.Ordinal));
    }

    [Fact]
    public void LossyToleranceAcceptsMeasuredDifferencesButRejectsGrossErrors()
    {
        var fixture = GoldenCorpus.Default.Get("jpeg/baseline-420");
        var policy = fixture.Policy;
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var value = reference.GetSample(5, 5, 0);
        var small = reference.WithSample(5, 5, 0, value > 128 ? value - policy.MaxAbsoluteError : value + policy.MaxAbsoluteError);
        GoldenAssert.FrameMatches(fixture, 0, small, NoPreviews);

        var gross = reference.WithSample(5, 5, 0, value > 128 ? value - policy.MaxAbsoluteError - 1 : value + policy.MaxAbsoluteError + 1);
        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, gross, NoPreviews));
        Assert.Contains("exceed the maximum tolerated error", exception.Message, StringComparison.Ordinal);

        // Systematic small errors everywhere exceed the mean tolerance
        var builder = new RawPixelBufferBuilder(reference.Width, reference.Height, RawPixelLayout.Rgba8);
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    var sample = reference.GetSample(x, y, c);
                    builder.SetSample(x, y, c, c == 3 ? sample : Math.Min(255, sample + 2));
                }
            }
        }

        var shifted = PixelBufferComparer.Compare(reference, builder.Build(), policy, "shifted");
        Assert.Contains(shifted.Failures, failure => failure.Contains("Mean color error", StringComparison.Ordinal));
    }

    [Fact]
    public void ToleranceCannotBeLooserThanTheCeilings()
    {
        Assert.Throws<ArgumentException>(() => ComparisonPolicy.Tolerance(ComparisonPolicy.AbsoluteErrorCeiling + 1, 1, "Measured differences between independent decoders of the input."));
        Assert.Throws<ArgumentException>(() => ComparisonPolicy.Tolerance(2, ComparisonPolicy.MeanAbsoluteErrorCeiling + 0.1, "Measured differences between independent decoders of the input."));
        Assert.Throws<ArgumentException>(() => ComparisonPolicy.Tolerance(2, 0.5, "short"));
    }

    [Fact]
    public void MissingFramesPostersTimingAndLoopErrorsAreReported()
    {
        var fixture = GoldenCorpus.Default.Get("apng/separate-poster");
        var good = GoldenCorpusTests.SnapshotFromReferences(fixture, RawPixelLayout.Rgba8);
        GoldenAssert.ImageMatches(fixture, good, NoPreviews);

        AssertImageFailure(fixture, Clone(good, frames: [good.Frames[0]]), "Frame count: expected 2 displayed frames, actual 1");
        AssertImageFailure(fixture, Clone(good, frames: [new DecodedFrameSnapshot(good.Poster!, RationalDuration.Zero), .. good.Frames]), "Frame count: expected 2 displayed frames, actual 3");
        AssertImageFailure(fixture, Clone(good, poster: null), "a separate poster is expected, but the image has none");
        AssertImageFailure(fixture, Clone(good, frames: [good.Frames[0], good.Frames[1] with { Duration = RationalDuration.Create(1, 4) }]), "Frame 1 duration: expected 1/5 s, actual 1/4 s");
        AssertImageFailure(fixture, Clone(good, totalPlays: null), "Total plays: expected 3, actual infinite");
        AssertImageFailure(fixture, Clone(good, totalPlays: 2), "Total plays: expected 3, actual 2");
        AssertImageFailure(fixture, Clone(good, hasAnimation: false), "animation settings are expected");
        AssertImageFailure(fixture, Clone(good, frames: [good.Frames[1], good.Frames[0]]), "Fixture 'apng/separate-poster', frame 0");

        var still = GoldenCorpus.Default.Get("png/single-pixel");
        var stillSnapshot = GoldenCorpusTests.SnapshotFromReferences(still, RawPixelLayout.Rgba8);
        AssertImageFailure(still, Clone(stillSnapshot, hasAnimation: true), "expected to have no animation settings");
        AssertImageFailure(still, Clone(stillSnapshot, pixelFormat: "Rgba64"), "Pixel format: expected Rgba32, actual Rgba64");
    }

    [Fact]
    public void MissingReferenceLayoutIsReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        var actual = RawPixelBuffer.Create(5, 4, RawPixelLayout.Rgba16Le, new byte[5 * 4 * 8]);
        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, actual, NoPreviews));
        Assert.Contains("has no rgba16le reference (available: rgba8)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceHashMismatchIsReported()
    {
        using var corpus = new CorpusCopy();
        var path = corpus.GetPath("png/rgba8-corner-markers.frame-0.rgba8.raw");
        var bytes = File.ReadAllBytes(path);
        bytes[10] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("png/rgba8-corner-markers.frame-0.rgba8.raw", StringComparison.Ordinal) && error.Contains("sha256", StringComparison.Ordinal));
        Assert.Contains("is invalid", Assert.Throws<InvalidOperationException>(() => GoldenCorpus.Load(corpus.Root)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InputHashMismatchIsReported()
    {
        using var corpus = new CorpusCopy();
        var path = corpus.GetPath("gif/disposal-transparency.gif");
        File.WriteAllBytes(path, [.. File.ReadAllBytes(path), 0x00]);
        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("gif/disposal-transparency.gif", StringComparison.Ordinal) && error.Contains("sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void TruncatedReferenceFileIsReportedBeforeComparison()
    {
        using var corpus = new CorpusCopy();
        const string Relative = "apng/blend-source-over.frame-1.rgba8.raw";
        var path = corpus.GetPath(Relative);
        var truncated = File.ReadAllBytes(path)[..^4];
        File.WriteAllBytes(path, truncated);
        corpus.EditFixture("apng/blend-source-over", fixture => FindBuffer(fixture, Relative)["sha256"] = Convert.ToHexStringLower(SHA256.HashData(truncated)));

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains(Relative, StringComparison.Ordinal) && error.Contains("truncated by 4 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void LayoutInconsistenciesAreReported()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("png/rgb8-odd-width", fixture =>
        {
            var buffer = FindBuffer(fixture, "png/rgb8-odd-width.frame-0.rgba8.raw");
            buffer["rowBytes"] = 21;
            buffer["alpha"] = "none";
        });
        corpus.EditFixture("png/gray8-checkerboard", fixture => FindBuffer(fixture, "png/gray8-checkerboard.frame-0.gray8.raw")["width"] = 8);
        corpus.EditFixture("png/palette4-trns", fixture => FindBuffer(fixture, "png/palette4-trns.frame-0.rgba8.raw")["layout"] = "bgra8");

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("rowBytes 21 does not match width x bytes per pixel = 28", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("alpha 'none' is not valid for layout rgba8", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("does not match the 9x9 canvas", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unknown layout 'bgra8'", StringComparison.Ordinal));
    }

    [Fact]
    public void FrameAndMetadataInconsistenciesAreReported()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("apng/dispose-background-previous", fixture =>
        {
            var expected = fixture["expected"]!.AsObject();
            expected["frameCount"] = 5;
            expected["frames"]![1]!["duration"] = "1/0";
            expected["frames"]![2]!["buffers"]!.AsArray().RemoveAt(0);
            expected["animation"]!["totalPlays"] = 3;
        });
        corpus.EditFixture("gif/ffmpeg-animated", fixture => fixture["expected"]!["animation"]!["totalPlays"] = 2);
        corpus.EditFixture("apng/blend-source-over", fixture => fixture["expected"]!["animation"] = null);

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("frameCount 5 does not match the 4 expected frames", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("duration '1/0'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("frame 2: at least one reference buffer is required", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("APNG num_plays 2 means totalPlays 2, not 3", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("GIF loop field 2 means totalPlays 3", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apng/blend-source-over", StringComparison.Ordinal) && error.Contains("must declare 'animation'", StringComparison.Ordinal));
    }

    [Fact]
    public void LoosePoliciesAndFabricatedPixelsAreRejected()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("png/gray8-checkerboard", fixture => fixture["comparison"] = new JsonObject
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = 12,
            ["maxMeanAbsoluteError"] = 2.0,
            ["justification"] = "A deliberately loose policy on a lossless fixture, used to prove it is rejected.",
        });
        corpus.EditFixture("jpeg/baseline-444", fixture => fixture["comparison"]!["maxAbsoluteError"] = 40);
        corpus.EditFixture("jpeg/baseline-420", fixture => fixture["comparison"]!["maxAbsoluteError"] = 10);
        corpus.EditFixture("invalid/png/bad-ihdr-crc", fixture => fixture["comparison"] = new JsonObject { ["mode"] = "exact" });

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("png/gray8-checkerboard", StringComparison.Ordinal) && error.Contains("showing nonzero differences", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("jpeg/baseline-444", StringComparison.Ordinal) && error.Contains("maxAbsoluteError <= 12", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("jpeg/baseline-420", StringComparison.Ordinal) && error.Contains("looser than the measured disagreement justifies", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("invalid/png/bad-ihdr-crc", StringComparison.Ordinal) && error.Contains("fabricated expected pixels are not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void ToleranceThatCannotDetectChannelSwapsIsRejected()
    {
        // A nearly gray reference: a channel swap changes samples by less than the tolerance, so the policy is useless
        var builder = new RawPixelBufferBuilder(4, 4, RawPixelLayout.Rgba8);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var v = 40 + (x * 40) + (y * 7);
                builder.SetSample(x, y, 0, v + 2);
                builder.SetSample(x, y, 1, v);
                builder.SetSample(x, y, 2, v + 1);
                builder.SetSample(x, y, 3, 255);
            }
        }

        var policy = ComparisonPolicy.Tolerance(4, 2, "Test policy proving that undiscriminating references are reported.");
        var defects = FixtureManifestValidator.FindUndetectableDefects(builder.Build(), policy);
        Assert.Contains("R/B channel swap", defects);
        Assert.Contains("R/G channel swap", defects);
    }

    [Fact]
    public void GzipReferencesUseBoundedDecompression()
    {
        using var corpus = new CorpusCopy();
        const string Relative = "png/palette8-256-colors.frame-0.rgba8.raw";
        var raw = File.ReadAllBytes(corpus.GetPath(Relative));

        // A valid compressed reference is accepted
        var gzip = Gzip(raw);
        File.WriteAllBytes(corpus.GetPath(Relative), gzip);
        corpus.EditFixture("png/palette8-256-colors", fixture =>
        {
            var buffer = FindBuffer(fixture, Relative);
            buffer["compression"] = "gzip";
            buffer["sha256"] = Convert.ToHexStringLower(SHA256.HashData(gzip));
        });
        Assert.Empty(corpus.Validate());

        // A stream that decompresses beyond the declared length is stopped and reported
        var bomb = Gzip([.. raw, .. new byte[100]]);
        File.WriteAllBytes(corpus.GetPath(Relative), bomb);
        corpus.EditFixture("png/palette8-256-colors", fixture => FindBuffer(fixture, Relative)["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bomb)));
        Assert.Contains(corpus.Validate(), error => error.Contains("bounded decompression stopped", StringComparison.Ordinal));

        // A stream that decompresses to fewer bytes is truncated
        var truncated = Gzip(raw[..^1]);
        File.WriteAllBytes(corpus.GetPath(Relative), truncated);
        corpus.EditFixture("png/palette8-256-colors", fixture => FindBuffer(fixture, Relative)["sha256"] = Convert.ToHexStringLower(SHA256.HashData(truncated)));
        Assert.Contains(corpus.Validate(), error => error.Contains("(truncated)", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewsAreWrittenOnFailure()
    {
        var directory = FullPath.GetTempPath() / ("mfi-previews-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = GoldenCorpus.Default.Get("png/rgba16-low-bit-gradient");
            var reference = fixture.GetFrame(0, RawPixelLayout.Rgba16Le);
            var corrupted = reference.WithSample(0, 0, 0, reference.GetSample(0, 0, 0) ^ 1);
            var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FrameMatches(fixture, 0, corrupted, new GoldenAssertOptions { WritePreviews = true, PreviewDirectory = directory }));
            Assert.Contains("Previews (diagnosis only)", exception.Message, StringComparison.Ordinal);

            foreach (var suffix in new[] { ".expected.png", ".actual.png", ".diff.png" })
            {
                var png = File.ReadAllBytes(directory / ("frame-0" + suffix));
                Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
            }

            var report = File.ReadAllText(directory / "frame-0.report.txt");
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"(0,0) R: expected {reference.GetSample(0, 0, 0)} (0x{reference.GetSample(0, 0, 0):X4})"), report, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void AssertHint(RawPixelBuffer reference, RawPixelBuffer actual, string hint)
    {
        var result = PixelBufferComparer.Compare(reference, actual, ComparisonPolicy.Exact, "orientation");
        Assert.False(result.IsMatch);
        Assert.Contains(result.Hints, item => item.Contains(hint, StringComparison.Ordinal));
    }

    private static void AssertImageFailure(GoldenFixture fixture, DecodedImageSnapshot snapshot, string expected)
    {
        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.ImageMatches(fixture, snapshot, NoPreviews));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static DecodedImageSnapshot Clone(DecodedImageSnapshot source, IReadOnlyList<DecodedFrameSnapshot>? frames = null, Optional<RawPixelBuffer?> poster = default, bool? hasAnimation = null, Optional<int?> totalPlays = default, string? pixelFormat = null) => new()
    {
        Frames = frames ?? source.Frames,
        Poster = poster.HasValue ? poster.Value : source.Poster,
        HasAnimation = hasAnimation ?? source.HasAnimation,
        TotalPlays = totalPlays.HasValue ? totalPlays.Value : source.TotalPlays,
        Orientation = source.Orientation,
        PixelFormat = pixelFormat ?? source.PixelFormat,
    };

    private static JsonObject FindBuffer(JsonObject fixture, string path)
    {
        var expected = fixture["expected"]!.AsObject();
        var frames = expected["frames"]!.AsArray().Select(frame => frame!.AsObject());
        if (expected["poster"] is JsonObject poster)
        {
            frames = frames.Append(poster);
        }

        return frames.SelectMany(frame => frame["buffers"]!.AsArray()).Single(buffer => (string?)buffer!["path"] == path)!.AsObject();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private readonly struct Optional<T>
    {
        public Optional(T value)
        {
            Value = value;
            HasValue = true;
        }

        public T Value { get; }

        public bool HasValue { get; }

        public static implicit operator Optional<T>(T value) => new(value);
    }
}
