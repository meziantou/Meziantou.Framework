using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>Unit tests of the pure-buffer model used by every golden comparison.</summary>
public sealed class PixelBufferTests
{
    [Fact]
    public void LayoutsDescribeExactByteLayouts()
    {
        Assert.Equal(4, RawPixelLayout.Rgba8.BytesPerPixel);
        Assert.Equal(8, RawPixelLayout.Rgba16Le.BytesPerPixel);
        Assert.Equal(3, RawPixelLayout.Rgba8.AlphaChannel);
        Assert.Equal(-1, RawPixelLayout.Gray16Le.AlphaChannel);
        Assert.Equal(1, RawPixelLayout.GrayAlpha8.AlphaChannel);
        Assert.Equal(257, RawPixelLayout.Rgba16Le.ScaleFrom8Bit);
        Assert.Equal(7 * 4 * 3, RawPixelLayout.Rgba8.GetByteLength(7, 3));
        Assert.Same(RawPixelLayout.Gray16Le, RawPixelLayout.Parse("gray16le"));
        Assert.False(RawPixelLayout.TryParse("bgra8", out _));
    }

    [Fact]
    public void SetRow16WritesLittleEndianWhateverThePlatform()
    {
        var builder = new RawPixelBufferBuilder(2, 1, RawPixelLayout.Rgba16Le);
        builder.SetRow16(0, [0x0102, 0x0304, 0x0506, 0xFFFF, 0, 1, 0x8000, 0x7FFF]);
        var buffer = builder.Build();
        Assert.Equal([0x02, 0x01, 0x04, 0x03, 0x06, 0x05, 0xFF, 0xFF, 0x00, 0x00, 0x01, 0x00, 0x00, 0x80, 0xFF, 0x7F], buffer.ToArray());
        Assert.Equal(0x8000, buffer.GetSample(1, 0, 2));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => new RawPixelBufferBuilder(1, 1, RawPixelLayout.Rgba8).SetRow16(0, [1, 2, 3, 4]));
    }

    [Fact]
    public void StridedRowsCopyOnlyVisibleBytes()
    {
        byte[] data = [1, 2, 3, 99, 4, 5, 6, 99, 7, 8, 9];
        var buffer = RawPixelBuffer.Create(1, 3, RawPixelLayout.Rgb8, data, stride: 4);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], buffer.ToArray());
        Assert.Throws<ArgumentException>(() => RawPixelBuffer.Create(1, 3, RawPixelLayout.Rgb8, data.AsSpan(0, 10), stride: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => RawPixelBuffer.Create(1, 3, RawPixelLayout.Rgb8, data, stride: 2));
    }

    [Fact]
    public void GeometricTransformsAreExact()
    {
        // 3x2 gray: 0 1 2 / 3 4 5
        var buffer = RawPixelBuffer.Create(3, 2, RawPixelLayout.Gray8, [0, 1, 2, 3, 4, 5]);
        Assert.Equal([3, 4, 5, 0, 1, 2], buffer.FlipVertical().ToArray());
        Assert.Equal([2, 1, 0, 5, 4, 3], buffer.FlipHorizontal().ToArray());
        Assert.Equal([5, 4, 3, 2, 1, 0], buffer.Rotate180().ToArray());
        var clockwise = buffer.Rotate90Clockwise();
        Assert.Equal((2, 3), (clockwise.Width, clockwise.Height));
        Assert.Equal([3, 0, 4, 1, 5, 2], clockwise.ToArray());
        Assert.Equal([2, 5, 1, 4, 0, 3], buffer.Rotate90CounterClockwise().ToArray());
        Assert.Equal([0, 3, 1, 4, 2, 5], buffer.Transpose().ToArray());
        var transversed = buffer.Transverse();
        Assert.Equal((2, 3), (transversed.Width, transversed.Height));
        Assert.Equal([5, 2, 4, 1, 3, 0], transversed.ToArray());

        var cropped = buffer.Crop(1, 0, 2, 2);
        Assert.Equal((2, 2), (cropped.Width, cropped.Height));
        Assert.Equal([1, 2, 4, 5], cropped.ToArray());
        Assert.Equal([5], buffer.Crop(2, 1, 1, 1).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Crop(2, 0, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Crop(0, 0, 0, 1));
    }

    [Fact]
    public void ChannelHelpersAreExact()
    {
        var buffer = RawPixelBuffer.Create(1, 1, RawPixelLayout.Rgba8, [10, 20, 30, 0]);
        Assert.Equal([30, 20, 10, 0], buffer.SwapChannels(0, 2).ToArray());
        Assert.Equal([10, 20, 30, 255], buffer.WithChannel(3, 255).ToArray());
        Assert.Equal([0, 0, 0, 0], buffer.WithTransparentColorsCleared().ToArray());
    }

    [Theory]
    [InlineData("3/30", 1, 10)]
    [InlineData("0/0", 0, 0)]
    [InlineData("0/7", 0, 1)]
    [InlineData("7/1000", 7, 1000)]
    [InlineData("65535/1000", 13107, 200)]
    public void RationalDurationsAreNormalized(string text, long numerator, long denominator)
    {
        if (denominator == 0)
        {
            Assert.False(RationalDuration.TryParse(text, out _));
            return;
        }

        var duration = RationalDuration.Parse(text);
        Assert.Equal((numerator, denominator), (duration.Numerator, duration.Denominator));
        Assert.Equal(RationalDuration.Create(numerator * 3, denominator * 3), duration);
    }

    [Fact]
    public void DefaultRationalDurationIsZero()
    {
        Assert.Equal(RationalDuration.Zero, default);
        Assert.Equal("0/1", default(RationalDuration).ToString());
        Assert.False(RationalDuration.TryParse("-1/2", out _));
        Assert.False(RationalDuration.TryParse("1/-2", out _));
        Assert.False(RationalDuration.TryParse("0.1", out _));
    }

    [Fact]
    public void ToleranceAppliesToColorButNeverToAlpha()
    {
        var expected = RawPixelBuffer.Create(2, 1, RawPixelLayout.Rgba8, [100, 100, 100, 200, 50, 50, 50, 255]);
        var policy = ComparisonPolicy.Tolerance(3, 2, "Unit-test policy: color within 3, mean within 2, alpha exact.");
        Assert.True(PixelBufferComparer.Matches(expected, RawPixelBuffer.Create(2, 1, RawPixelLayout.Rgba8, [103, 97, 100, 200, 50, 52, 50, 255]), policy));
        Assert.False(PixelBufferComparer.Matches(expected, RawPixelBuffer.Create(2, 1, RawPixelLayout.Rgba8, [104, 100, 100, 200, 50, 50, 50, 255]), policy));
        Assert.False(PixelBufferComparer.Matches(expected, RawPixelBuffer.Create(2, 1, RawPixelLayout.Rgba8, [100, 100, 100, 201, 50, 50, 50, 255]), policy));

        var result = PixelBufferComparer.Compare(expected, RawPixelBuffer.Create(2, 1, RawPixelLayout.Rgba8, [103, 97, 100, 200, 50, 52, 50, 255]), policy);
        Assert.True(result.IsMatch);
        Assert.Equal(3, result.MaxAbsoluteError);
        Assert.Equal(8.0 / 6.0, result.MeanAbsoluteError, 1e-9);
        Assert.Equal([3, 3, 0, 0], result.MaxErrorPerChannel);
    }

    [Fact]
    public void ToleranceIsScaledFor16BitLayouts()
    {
        var policy = ComparisonPolicy.Tolerance(2, 1, "Unit-test policy: 8-bit-scale tolerance applied to 16-bit samples.");
        var expected = RawPixelBuffer.Create(1, 1, RawPixelLayout.Gray16Le, [0x00, 0x80]);
        Assert.True(PixelBufferComparer.Matches(expected, RawPixelBuffer.Create(1, 1, RawPixelLayout.Gray16Le, [0x01, 0x80]), ComparisonPolicy.Exact) is false);
        Assert.True(PixelBufferComparer.Matches(expected, RawPixelBuffer.Create(1, 1, RawPixelLayout.Gray16Le, [0x02, 0x80]), ComparisonPolicy.Tolerance(1, 2, "Unit-test policy: 257 sixteen-bit units correspond to one 8-bit step.")));
        Assert.Equal(514, policy.GetMaxAbsoluteError(RawPixelLayout.Gray16Le));
    }

    [Fact]
    public void PolicyDefinitionsAreValidated()
    {
        Assert.Same(ComparisonPolicy.Exact, ComparisonPolicy.FromDefinition(new ComparisonPolicyDefinition { Mode = "exact" }));
        Assert.NotEmpty(ComparisonPolicy.Validate(new ComparisonPolicyDefinition { Mode = "exact", MaxAbsoluteError = 1 }));
        Assert.NotEmpty(ComparisonPolicy.Validate(new ComparisonPolicyDefinition { Mode = "tolerance", MaxAbsoluteError = 2, MaxMeanAbsoluteError = 0.5 }));
        Assert.NotEmpty(ComparisonPolicy.Validate(new ComparisonPolicyDefinition { Mode = "approximate" }));
    }

    [Fact]
    public void ExpectedErrorAssertionsCheckTypeAndProperties()
    {
        var corpus = GoldenCorpus.Default;
        var invalid = corpus.Get("invalid/png/bad-ihdr-crc");
        GoldenAssert.FailsAsExpected(invalid, () => throw new InvalidImageContentException("bad CRC", ImageFormat.Png));
        Assert.Contains("failed with Meziantou.Framework.Imaging.UnknownImageFormatException", Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FailsAsExpected(invalid, () => throw new UnknownImageFormatException())).Message, StringComparison.Ordinal);
        Assert.Contains("Format = Png, actual Gif", Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FailsAsExpected(invalid, () => throw new InvalidImageContentException("bad", ImageFormat.Gif))).Message, StringComparison.Ordinal);
        Assert.Contains("but the operation succeeded", Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FailsAsExpected(invalid, () => { })).Message, StringComparison.Ordinal);

        var limit = corpus.Get("limit/gif/frames-over-limit");
        Assert.Equal(3, limit.Entry.DecodeOptions!.Limits!["MaxFrames"]);
        GoldenAssert.FailsAsExpected(limit, () => throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, 3, 4));
        Assert.Contains("Kind = Frames, actual Width", Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FailsAsExpected(limit, () => throw new ImageResourceLimitException(ImageResourceLimitKind.Width, 3, 4))).Message, StringComparison.Ordinal);

        // Not-yet-implemented codecs are reported clearly instead of passing
#pragma warning disable MA0025 // Simulates a not-yet-implemented member
        Assert.Contains("NotImplementedException", Assert.Throws<GoldenAssertionException>(() => GoldenAssert.FailsAsExpected(corpus.Get("invalid/jpeg/arithmetic-coding"), () => throw new NotImplementedException("not yet implemented"))).Message, StringComparison.Ordinal);
#pragma warning restore MA0025
    }

    [Fact]
    public async Task ExpectedErrorAssertionsSupportAsyncOperations()
    {
        var fixture = GoldenCorpus.Default.Get("invalid/gif/plain-text-extension");
        var exception = await GoldenAssert.FailsAsExpectedAsync(fixture, async () =>
        {
            await Task.Yield();
            throw new UnsupportedImageFeatureException("plain text", ImageFormat.Gif, "plain text extension");
        });
        Assert.IsType<UnsupportedImageFeatureException>(exception);
    }

    [Fact]
    public void CorpusQueriesFilterByFormatKindAndFeature()
    {
        var corpus = GoldenCorpus.Default;
        Assert.Contains("apng/separate-poster", corpus.GetIds(format: "png", feature: "apng.poster=separate"));
        Assert.DoesNotContain(corpus.GetIds(format: "png", kind: FixtureKinds.Valid), id => id.StartsWith("invalid/", StringComparison.Ordinal));
        Assert.All(corpus.GetIds(format: "jpeg", kind: FixtureKinds.Valid), id => Assert.StartsWith("jpeg/", id));
        Assert.Throws<KeyNotFoundException>(() => corpus.Get("png/does-not-exist"));
    }
}
