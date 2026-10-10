using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Row conversions checked against the raw references of the golden corpus. Fixtures that carry
/// both a native gray reference and an RGBA reference (written independently by the corpus generator) cross-check the
/// gray/color mappings in both directions; reductions and flattening are compared with explicitly computed rounding
/// expectations. No image codec and no conversion under test is used to produce expected values.
/// </summary>
public sealed class PixelConversionConformanceTests
{
    private static readonly Rgba64 Background = new(0x2040, 0xFFFF, 0x0101);

    public static TheoryData<string> Gray8Fixtures => [.. Gray8FixtureIds];

    public static TheoryData<string> SixteenBitFixtures => [.. SixteenBitFixtureIds];

    public static TheoryData<string> TranslucentFixtures => [.. TranslucentFixtureIds];

    private static IEnumerable<string> Gray8FixtureIds => FixturesWith(RawPixelLayout.Gray8, RawPixelLayout.Rgba8);

    private static IEnumerable<string> SixteenBitFixtureIds => GoldenCorpus.Default.Fixtures.Where(f => f.IsValid && f.Layouts.Contains(RawPixelLayout.Rgba16Le)).Select(f => f.Id);

    private static IEnumerable<string> TranslucentFixtureIds => GoldenCorpus.Default.Fixtures.Where(f => f.IsValid && f.Layouts.Contains(RawPixelLayout.Rgba8) && !IsOpaque(f.GetFrame(0, RawPixelLayout.Rgba8))).Select(f => f.Id);

    [Fact]
    public void CorpusProvidesTheReferencesTheseTestsNeed()
    {
        Assert.Contains("jpeg/gray-baseline-odd", Gray8FixtureIds);
        Assert.Contains("png/gray1-odd-width", Gray8FixtureIds);
        Assert.Contains("png/gray16-low-bit-gradient", FixturesWith(RawPixelLayout.Gray16Le, RawPixelLayout.Rgba16Le));
        Assert.Contains("png/rgba16-low-bit-gradient", SixteenBitFixtureIds);
        Assert.Contains("png/graya8-alpha-ramp", TranslucentFixtureIds);
    }

    [Theory]
    [MemberData(nameof(Gray8Fixtures))]
    public void Gray8ReferenceExpandsToRgbaReference(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var gray = fixture.GetFrame(0, RawPixelLayout.Gray8);
        var rgba = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var source = unsafe(MemoryMarshal.Cast<byte, Gray8>(gray.Span));

        AssertMatches(rgba, unsafe(MemoryMarshal.AsBytes(Convert<Gray8, Rgba32>(source).AsSpan())), id + " Gray8 -> Rgba32");
        AssertMatches(rgba.SwapChannels(0, 2), unsafe(MemoryMarshal.AsBytes(Convert<Gray8, Bgra32>(source).AsSpan())), id + " Gray8 -> Bgra32 (BGRA storage)");
        AssertMatches(DropAlpha(rgba), unsafe(MemoryMarshal.AsBytes(Convert<Gray8, Rgb24>(source).AsSpan())), id + " Gray8 -> Rgb24");

        // 8-bit to 16-bit is the exact full-range expansion of the same reference
        var expected16 = Widen(rgba);
        AssertMatches(expected16, ToLittleEndian(Convert<Gray8, Rgba64>(source)), id + " Gray8 -> Rgba64");
    }

    [Theory]
    [MemberData(nameof(Gray8Fixtures))]
    public void RgbaReferenceOfGrayImageReducesToGrayReference(string id)
    {
        // R = G = B, so Rec. 709 luma (coefficients summing to 1) must reproduce the gray reference exactly
        var fixture = GoldenCorpus.Default.Get(id);
        var gray = fixture.GetFrame(0, RawPixelLayout.Gray8);
        var rgba = unsafe(MemoryMarshal.Cast<byte, Rgba32>(fixture.GetFrame(0, RawPixelLayout.Rgba8).Span));

        AssertMatches(gray, unsafe(MemoryMarshal.AsBytes(Convert<Rgba32, Gray8>(rgba).AsSpan())), id + " Rgba32 -> Gray8");
        var rgb = Convert<Rgba32, Rgb24>(rgba);
        AssertMatches(gray, unsafe(MemoryMarshal.AsBytes(Convert<Rgb24, Gray8>(rgb).AsSpan())), id + " Rgb24 -> Gray8");
        AssertMatches(Widen(gray), ToLittleEndian(Convert<Rgba32, Gray16>(rgba)), id + " Rgba32 -> Gray16");
    }

    [Fact]
    public void Gray16ReferenceExpandsAndReducesWithoutLosingLowBits()
    {
        var fixture = GoldenCorpus.Default.Get("png/gray16-low-bit-gradient");
        var gray = fixture.GetFrame(0, RawPixelLayout.Gray16Le);
        var rgba = fixture.GetFrame(0, RawPixelLayout.Rgba16Le);
        var grayPixels = FromLittleEndian<Gray16>(gray);

        var expanded = Convert<Gray16, Rgba64>(grayPixels);
        AssertMatches(rgba, ToLittleEndian(expanded), "Gray16 -> Rgba64");
        AssertMatches(gray, ToLittleEndian(Convert<Rgba64, Gray16>(FromLittleEndian<Rgba64>(rgba))), "Rgba64 -> Gray16");

        // Routing through 8 bits would be detected: the reference has distinct low bytes
        var through8 = Convert<Rgba32, Rgba64>(Convert<Rgba64, Rgba32>(expanded));
        Assert.False(PixelBufferComparer.Matches(rgba, RawPixelBuffer.Create(rgba.Width, rgba.Height, RawPixelLayout.Rgba16Le, ToLittleEndian(through8)), fixture.Policy));
    }

    [Theory]
    [MemberData(nameof(SixteenBitFixtures))]
    public void Rgba16ReferenceReducesWithNearestRounding(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba16Le);
        var pixels = FromLittleEndian<Rgba64>(reference);

        // Lossless 16-bit paths reproduce every bit
        AssertMatches(reference, ToLittleEndian(Convert<Rgba64, Rgba64>(pixels)), id + " Rgba64 -> Rgba64");

        // Expected reduction computed independently: nearest of v / 257 (no ties exist), per sample, alpha included
        var expected = new RawPixelBufferBuilder(reference.Width, reference.Height, RawPixelLayout.Rgba8);
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    expected.SetSample(x, y, c, (int)Math.Round(reference.GetSample(x, y, c) / 257.0, MidpointRounding.AwayFromZero));
                }
            }
        }

        var expected8 = expected.Build();
        AssertMatches(expected8, unsafe(MemoryMarshal.AsBytes(Convert<Rgba64, Rgba32>(pixels).AsSpan())), id + " Rgba64 -> Rgba32");
        AssertMatches(expected8.SwapChannels(0, 2), unsafe(MemoryMarshal.AsBytes(Convert<Rgba64, Bgra32>(pixels).AsSpan())), id + " Rgba64 -> Bgra32");
    }

    [Theory]
    [MemberData(nameof(TranslucentFixtures))]
    public void TranslucentReferenceRequiresExplicitBackgroundToRemoveAlpha(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba32>(reference.Span)).ToArray();

        var rejected = new Rgb24[pixels.Length];
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConverter.ConvertRow<Rgba32, Rgb24>(pixels, rejected));
        Assert.All(rejected, pixel => Assert.Equal(default, pixel));
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create<Rgba32, Gray8>().EnsureConvertible<Rgba32>(pixels));

        // Alpha formats keep straight alpha and hidden colors exactly
        AssertMatches(reference, unsafe(MemoryMarshal.AsBytes(Convert<Rgba64, Rgba32>(Convert<Rgba32, Rgba64>(pixels)).AsSpan())), id + " Rgba32 -> Rgba64 -> Rgba32");
        AssertMatches(Widen(reference), ToLittleEndian(Convert<Rgba32, Rgba64>(pixels)), id + " Rgba32 -> Rgba64");

        // Flattening at 8-bit source precision onto the background reduced to 8 bits:
        // out = round((s * a + bg * (255 - a)) / 255), computed here in exact rational arithmetic
        int[] background = [.. new[] { Background.R, Background.G, Background.B }.Select(v => (int)Math.Round(v / 257.0, MidpointRounding.AwayFromZero))]; // 0x2040 / 257 = 32.1 -> 32, 255, 1
        var expected = new RawPixelBufferBuilder(reference.Width, reference.Height, RawPixelLayout.Rgb8);
        for (var y = 0; y < reference.Height; y++)
        {
            for (var x = 0; x < reference.Width; x++)
            {
                var a = reference.GetSample(x, y, 3);
                for (var c = 0; c < 3; c++)
                {
                    var numerator = (reference.GetSample(x, y, c) * a) + (background[c] * (255 - a));
                    expected.SetSample(x, y, c, (int)Math.Round(numerator / 255m, MidpointRounding.AwayFromZero));
                }
            }
        }

        var options = new PixelConversionOptions { BackgroundColor = Background };
        var flattened = new Rgb24[pixels.Length];
        PixelConversionPlan.Create<Rgba32, Rgb24>(options).ConvertRow<Rgba32, Rgb24>(pixels, flattened);
        AssertMatches(expected.Build(), unsafe(MemoryMarshal.AsBytes(flattened.AsSpan())), id + " Rgba32 -> Rgb24 (flattened)");
    }

    [Fact]
    public void FullyTransparentPixelsOfAlphaRampFlattenToBackground()
    {
        var reference = GoldenCorpus.Default.Get("png/graya8-alpha-ramp").GetFrame(0, RawPixelLayout.Rgba8);
        var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba32>(reference.Span));
        Assert.Contains(pixels.ToArray(), pixel => pixel.A == 0);
        Assert.Contains(pixels.ToArray(), pixel => pixel.A == 255);

        var gray = Convert<Rgba32, Gray8>(pixels, Background);
        var backgroundLuma = (int)Math.Floor((0.2126m * 32) + (0.7152m * 255) + (0.0722m * 1) + 0.5m); // 6.8032 + 182.376 + 0.0722 = 189.2514 -> 189
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].A == 0)
            {
                Assert.Equal(backgroundLuma, gray[i].Value);
            }
        }
    }

    [Fact]
    public void ColorProfilesFromCorpusFollowCompatibilityRules()
    {
        var grayFixture = GoldenCorpus.Default.Get("png/metadata-gray8-icc");
        var grayProfile = new IccProfile(new MetadataBlob(grayFixture.InspectInput().Icc!.Value.ToArray()));
        Assert.Equal(IccProfileColorSpace.Gray, grayProfile.ColorSpace);

        var rgbFixture = GoldenCorpus.Default.Get("png/metadata-rgb8-profiles");
        var rgbProfile = new IccProfile(new MetadataBlob(rgbFixture.InspectInput().Icc!.Value.ToArray()));
        Assert.Equal(IccProfileColorSpace.Rgb, rgbProfile.ColorSpace);

        // Gray pixels labeled by a gray profile: gray targets keep it, color targets reject or discard it
        Assert.Same(grayProfile, PixelConversionPlan.Create<Gray8, Gray16>(sourceProfile: grayProfile).ColorProfile);
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create<Gray8, Rgba32>(sourceProfile: grayProfile));
        Assert.Null(PixelConversionPlan.Create<Gray8, Rgba32>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true }, grayProfile).ColorProfile);

        // Color pixels labeled by an RGB profile: grayscale values can never be labeled with it
        var rgbPixels = unsafe(MemoryMarshal.Cast<byte, Rgba32>(rgbFixture.GetFrame(0, RawPixelLayout.Rgba8).Span));
        var keep = PixelConversionPlan.Create<Rgba32, Rgb24>(sourceProfile: rgbProfile);
        Assert.Same(rgbProfile, keep.ColorProfile);
        keep.ConvertRow<Rgba32, Rgb24>(rgbPixels, new Rgb24[rgbPixels.Length]);
        Assert.Throws<UnsupportedImageFeatureException>(() => PixelConversionPlan.Create<Rgba32, Gray8>(sourceProfile: rgbProfile));
        var discard = PixelConversionPlan.Create<Rgba32, Gray16>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true }, rgbProfile);
        Assert.Null(discard.ColorProfile);
        Assert.True(discard.ColorProfileDiscarded);
    }

    [Fact]
    public void ComparisonDetectsChannelSwapsOfConvertedRows()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        var bgra = unsafe(MemoryMarshal.AsBytes(Convert<Rgba32, Bgra32>(MemoryMarshal.Cast<byte, Rgba32>(reference.Span)).AsSpan())).ToArray();

        // BGRA storage read as RGBA must not match: a missing or doubled swap is detected
        var result = PixelBufferComparer.CompareBytes(reference, bgra, fixture.Policy, "BGRA bytes as RGBA");
        Assert.False(result.IsMatch);
        Assert.True(PixelBufferComparer.CompareBytes(reference.SwapChannels(0, 2), bgra, fixture.Policy).IsMatch);
    }

    [Fact]
    public void ComparisonDetectsByteOrderCorruptionOfSixteenBitRows()
    {
        var fixture = GoldenCorpus.Default.Get("png/rgba16-low-bit-gradient");
        var reference = fixture.GetFrame(0, RawPixelLayout.Rgba16Le);

        // Explicit little-endian decoding reproduces the reference; big-endian interpretation of the same bytes does not
        var pixels = FromLittleEndian<Rgba64>(reference);
        var wrongOrder = new Rgba64[pixels.Length];
        SampleEndianness.ReadPixels<Rgba64>(reference.Span, wrongOrder, bigEndian: true);
        Assert.True(PixelBufferComparer.CompareBytes(reference, ToLittleEndian(pixels), fixture.Policy).IsMatch);
        Assert.False(PixelBufferComparer.CompareBytes(reference, ToLittleEndian(wrongOrder), fixture.Policy).IsMatch);

        // Native-endian struct bytes equal the little-endian reference only on little-endian hosts
        var native = unsafe(MemoryMarshal.AsBytes(pixels.AsSpan())).ToArray();
        Assert.Equal(BitConverter.IsLittleEndian, PixelBufferComparer.CompareBytes(reference, native, fixture.Policy).IsMatch);
    }

    private static IEnumerable<string> FixturesWith(RawPixelLayout native, RawPixelLayout canonical)
        => GoldenCorpus.Default.Fixtures.Where(f => f.IsValid && f.Layouts.Contains(native) && f.Layouts.Contains(canonical)).Select(f => f.Id);

    private static bool IsOpaque(RawPixelBuffer buffer)
    {
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                if (buffer.GetSample(x, y, buffer.Layout.AlphaChannel) != buffer.Layout.MaxSampleValue)
                    return false;
            }
        }

        return true;
    }

    private static TDestination[] Convert<TSource, TDestination>(ReadOnlySpan<TSource> source, Rgba64? background = null)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var result = new TDestination[source.Length];
        PixelConverter.ConvertRow(source, result.AsSpan(), background);
        return result;
    }

    private static TPixel[] FromLittleEndian<TPixel>(RawPixelBuffer buffer)
        where TPixel : unmanaged
    {
        var result = new TPixel[buffer.Width * buffer.Height];
        SampleEndianness.ReadPixels<TPixel>(buffer.Span, result, bigEndian: false);
        return result;
    }

    private static byte[] ToLittleEndian<TPixel>(TPixel[] pixels)
        where TPixel : unmanaged
    {
        var result = new byte[pixels.Length * Marshal.SizeOf<TPixel>()];
        SampleEndianness.WritePixels<TPixel>(pixels, result, bigEndian: false);
        return result;
    }

    /// <summary>The full-range 8-to-16-bit expansion of a reference (<c>v * 257</c>, i.e. the byte repeated).</summary>
    private static RawPixelBuffer Widen(RawPixelBuffer buffer)
    {
        var layout = buffer.Layout == RawPixelLayout.Gray8 ? RawPixelLayout.Gray16Le : RawPixelLayout.Rgba16Le;
        var bytes = new byte[buffer.ByteLength * 2];
        for (var i = 0; i < buffer.ByteLength; i++)
        {
            bytes[2 * i] = buffer.Span[i];
            bytes[(2 * i) + 1] = buffer.Span[i];
        }

        return RawPixelBuffer.Create(buffer.Width, buffer.Height, layout, bytes);
    }

    private static RawPixelBuffer DropAlpha(RawPixelBuffer rgba)
    {
        var builder = new RawPixelBufferBuilder(rgba.Width, rgba.Height, RawPixelLayout.Rgb8);
        for (var y = 0; y < rgba.Height; y++)
        {
            for (var x = 0; x < rgba.Width; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    builder.SetSample(x, y, c, rgba.GetSample(x, y, c));
                }
            }
        }

        return builder.Build();
    }

    private static void AssertMatches(RawPixelBuffer expected, ReadOnlySpan<byte> actual, string context)
    {
        var result = PixelBufferComparer.CompareBytes(expected, actual, ComparisonPolicy.Exact, context);
        Assert.True(result.IsMatch, result.Describe());
    }
}
