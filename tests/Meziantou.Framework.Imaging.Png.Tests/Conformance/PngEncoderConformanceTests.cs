using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Static PNG encoding of every raw reference of the golden corpus (frames and posters as still images, in every
/// pixel format that holds them exactly: 8/16-bit RGBA, BGRA, RGB, gray 8/16), with fixed and adaptive filters, every
/// compression level and Adam7. The output is decoded by the independent harness reader (<see cref="ReferencePng"/>) and
/// compared exactly with the same reference; the IHDR color type and bit depth are checked against the pixel format.
/// The indexed layout encodes the same references: the harness decides from the reference alone whether a palette can hold
/// it exactly (<see cref="IndexedPngExpectation"/>), and then which palette, alpha values and bit depth the file must have;
/// a reference that cannot be stored must be rejected before any output. The reader itself is checked against the paletted
/// files of the corpus, which other software wrote.
/// Tool-free: FFmpeg decodes the same outputs in <c>InteropTests/PngEncoderInteropTests</c>.
/// </summary>
public sealed class PngEncoderConformanceTests
{
    private static readonly PngEncoder[] Encoders =
    [
        new PngEncoder(),
        new PngEncoder { Interlaced = true },
        new PngEncoder { Filter = PngFilter.None, CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression },
        new PngEncoder { Filter = PngFilter.Paeth, CompressionLevel = System.IO.Compression.CompressionLevel.SmallestSize, Interlaced = true },
        new PngEncoder { Filter = PngFilter.Sub, CompressionLevel = System.IO.Compression.CompressionLevel.Fastest },
        new PngEncoder { Filter = PngFilter.Up, Interlaced = true },
        new PngEncoder { Filter = PngFilter.Average },
    ];

    private static readonly PngEncoder[] IndexedEncoders =
    [
        new PngEncoder { PixelLayout = PngPixelLayout.Indexed },
        new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = true, Filter = PngFilter.Paeth, CompressionLevel = System.IO.Compression.CompressionLevel.SmallestSize },
        new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Filter = PngFilter.Sub, CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression },
    ];

    private static readonly PngEncoder[] MinimumSizeEncoders =
    [
        new PngEncoder { Optimization = PngOptimization.MinimumSize },
        new PngEncoder { Optimization = PngOptimization.MinimumSize, Interlaced = true, Filter = PngFilter.Paeth },
    ];

    public static TheoryData<string> PaletteFixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Valid, feature: "png.colorType=3").Where(id => id.StartsWith("png/", StringComparison.Ordinal))];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Fact]
    public void CorpusCoversEveryStorageType()
    {
        var formats = EncoderSources.GetCases(GoldenCorpus.Default).Select(item => item.Format).ToHashSet();
        Assert.HasCount(6, formats);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodedCorpusReferencesDecodeIndependently(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, expected) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(expected, pixelFormat);
            foreach (var encoder in Encoders)
            {
                using var stream = new MemoryStream();
                image.Save(stream, encoder);
                var reference = ReferencePng.Parse(stream.ToArray());
                var (colorType, bitDepth) = pixelFormat switch
                {
                    PixelFormat.Gray8 => (0, 8),
                    PixelFormat.Gray16 => (0, 16),
                    PixelFormat.Rgb24 => (2, 8),
                    PixelFormat.Rgba64 => (6, 16),
                    _ => (6, 8),
                };

                var context = $"{id} {name} as {pixelFormat} (filter {encoder.Filter}, {encoder.CompressionLevel}, interlaced {encoder.Interlaced})";
                Assert.True(reference.ColorType == colorType && reference.BitDepth == bitDepth, $"{context}: color type {reference.ColorType}, bit depth {reference.BitDepth}.");
                Assert.Equal(encoder.Interlaced ? 1 : 0, reference.InterlaceMethod);
                var result = PixelBufferComparer.Compare(expected, reference.DecodePixels(), ComparisonPolicy.Exact, context);
                Assert.True(result.IsMatch, result.Describe());
            }
        }
    }

    [Theory]
    [MemberData(nameof(PaletteFixtures))]
    public void ReferenceReaderAgreesWithTheCorpusPaletteImages(string id)
    {
        // The harness reader is the oracle of the indexed encoder tests: it must expand every paletted corpus file like the corpus
        var fixture = GoldenCorpus.Default.Get(id);
        var reference = ReferencePng.Parse(fixture.ReadInput());
        Assert.Equal((byte)3, reference.ColorType);
        var result = PixelBufferComparer.Compare(fixture.GetFrame(0, RawPixelLayout.Rgba8), reference.DecodePixels(), ComparisonPolicy.Exact, id);
        Assert.True(result.IsMatch, result.Describe());
    }

    [Fact]
    public void ReferenceReaderCoversEveryPaletteBitDepthAndRejectsPaletteErrors()
    {
        var depths = PaletteFixtures.Select(row => ReferencePng.Parse(GoldenCorpus.Default.Get(row.Data).ReadInput()).BitDepth).ToHashSet();
        Assert.Equal([1, 2, 4, 8], depths.Order().Select(depth => (int)depth));

        // An index beyond the palette fails when the pixels are expanded; a palette after the image data fails the structure check
        var outOfRange = ReferencePng.Parse(GoldenCorpus.Default.Get("invalid/png/palette-index-out-of-range").ReadInput());
        Assert.Throws<InvalidDataException>(() => outOfRange.DecodePixels());
        Assert.Throws<InvalidDataException>(() => ReferencePng.Parse(GoldenCorpus.Default.Get("invalid/png/plte-after-idat").ReadInput()));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void IndexedLayoutStoresTheCorpusReferencesExactlyOrRejectsThem(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, expected) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(expected, pixelFormat);
            var expectation = IndexedPngExpectation.Create(expected);
            foreach (var encoder in IndexedEncoders)
            {
                var context = $"{id} {name} as {pixelFormat} (indexed, filter {encoder.Filter}, {encoder.CompressionLevel}, interlaced {encoder.Interlaced})";
                using var stream = new MemoryStream();
                if (!expectation.IsStorable)
                {
                    // More colors than a palette holds, or 16-bit samples it cannot represent: never reduced, nothing written
                    var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
                    Assert.True(IsExpectedRejection(expectation, exception.Feature), $"{context}: rejected for '{exception.Feature}' ({expectation.ColorCount} colors, 8-bit exact {expectation.IsEightBitExact}).");
                    Assert.Equal(0, stream.Length);
                    continue;
                }

                image.Save(stream, encoder);
                var reference = ReferencePng.Parse(stream.ToArray());
                Assert.True(reference.ColorType == 3 && reference.BitDepth == expectation.BitDepth, $"{context}: color type {reference.ColorType}, bit depth {reference.BitDepth}.");
                Assert.Equal(encoder.Interlaced ? 1 : 0, reference.InterlaceMethod);
                Assert.Equal(expectation.Palette.ToArray(), reference.Palette.ToArray());
                Assert.Equal(expectation.Transparency.ToArray(), reference.Transparency.ToArray());
                Assert.True(reference.ArePaddingBitsZero(), context + ": padding bits are set.");
                var result = PixelBufferComparer.Compare(IndexedPngExpectation.ToRgba8(expected), reference.DecodePixels(), ComparisonPolicy.Exact, context);
                Assert.True(result.IsMatch, result.Describe());
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MinimumSizeStoresTheCorpusReferencesExactly(string id, string format)
    {
        // Any color type and bit depth: the reader decodes the file to 16-bit RGBA, compared with the reference itself
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, expected) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(expected, pixelFormat);
            foreach (var encoder in MinimumSizeEncoders)
            {
                var context = $"{id} {name} as {pixelFormat} (minimum size, filter {encoder.Filter}, interlaced {encoder.Interlaced})";
                using var stream = new MemoryStream();
                image.Save(stream, encoder);
                var reference = OptimizedPngOutputVerifier.VerifyStill(stream.ToArray(), image, context);
                Assert.Equal(encoder.Interlaced ? 1 : 0, reference.InterlaceMethod);
                var result = PixelBufferComparer.Compare(ReferencePng.ToRgba16(expected), reference.DecodeImageRgba16(), ComparisonPolicy.Exact, context);
                Assert.True(result.IsMatch, result.Describe());

                // The encoding without optimization, with the same filter strategy and Adam7, is one of the candidates
                using var unoptimized = new MemoryStream();
                image.Save(unoptimized, new PngEncoder { Filter = encoder.Filter, Interlaced = encoder.Interlaced });
                Assert.True(stream.Length <= unoptimized.Length, $"{context}: {stream.Length} bytes, {unoptimized.Length} without optimization.");
            }
        }
    }

    [Fact]
    public void CorpusCoversEveryIndexedOutcome()
    {
        var depths = new HashSet<int>();
        var rejections = new HashSet<string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            foreach (var (_, pixels) in EncoderSources.GetStills(fixture, format))
            {
                var expectation = IndexedPngExpectation.Create(pixels);
                if (expectation.IsStorable)
                {
                    depths.Add(expectation.BitDepth);
                }
                else
                {
                    rejections.Add(expectation.IsEightBitExact ? "colors" : "precision");
                }
            }
        }

        Assert.Equal([1, 2, 4, 8], depths.Order());
        Assert.Equal(["colors", "precision"], rejections.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The encoder reports the first pixel it cannot store, so a reference with both too many colors and inexact 16-bit
    /// samples is rejected for either reason.
    /// </summary>
    private static bool IsExpectedRejection(IndexedPngExpectation expectation, string? feature) => feature switch
    {
        "Bit depth reduction" => !expectation.IsEightBitExact,
        "Color reduction" => expectation.ColorCount > 256,
        _ => false,
    };
}
