using System.IO.Compression;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Static PNG pixel decoding on synthetic inputs encoded by the independent test-side encoder
/// (<see cref="PngTestImage"/>): every legal color type/bit depth with and without tRNS, interlaced or not, all filters,
/// odd and degenerate sizes, IDAT splitting, bounded streaming, conversion policies, truncation and expansion errors,
/// sequential readers and decoder-state accounting. The golden corpus conformance tests compare the same decoder with
/// externally produced inputs.
/// </summary>
public sealed class PngDecoderTests
{
    private static readonly (byte ColorType, byte BitDepth)[] LegalCombinations =
    [
        (0, 1), (0, 2), (0, 4), (0, 8), (0, 16),
        (2, 8), (2, 16),
        (3, 1), (3, 2), (3, 4), (3, 8),
        (4, 8), (4, 16),
        (6, 8), (6, 16),
    ];

    private static readonly (int Width, int Height)[] Sizes = [(1, 1), (2, 1), (1, 7), (5, 3), (9, 10), (33, 2)];

    public static TheoryData<byte, byte, bool, bool> Combinations
    {
        get
        {
            var data = new TheoryData<byte, byte, bool, bool>();
            foreach (var (colorType, bitDepth) in LegalCombinations)
            {
                foreach (var interlaced in new[] { false, true })
                {
                    data.Add(colorType, bitDepth, interlaced, false);
                    if (colorType is 0 or 2 or 3)
                    {
                        data.Add(colorType, bitDepth, interlaced, true);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryLegalLayoutDecodesExactly(byte colorType, byte bitDepth, bool interlaced, bool transparency)
    {
        foreach (var (width, height) in Sizes)
        {
            for (var filterSeed = 0; filterSeed < 5; filterSeed++)
            {
                var image = PngTestImage.CreateRandom(width, height, colorType, bitDepth, transparency, seed: (width * 31) + height + filterSeed);
                var data = image.Encode(interlaced, filterSeed);
                var expected = image.GetExpectedPixels();
                var context = $"{width}x{height}, filter seed {filterSeed}";

                // Untyped load: the documented default representation, converted losslessly to Rgba64 for comparison
                using (var untyped = Image.Load(data))
                {
                    Assert.Equal(DefaultPixelFormats.ForPng(colorType, bitDepth, transparency, isAnimated: false), untyped.PixelFormat);
                    Assert.False(untyped.IsAnimated);
                    using var converted = untyped.CloneAs<Rgba64>();
                    AssertPixels(expected, converted, context);
                }

                // Typed load at full precision: written directly by the decoder sink
                using var typed = Image.Load<Rgba64>(data);
                AssertPixels(expected, typed, context);
            }
        }
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(2, 16)]
    [InlineData(4, 16)]
    [InlineData(6, 16)]
    public void SixteenBitSamplesKeepTheirLowBits(byte colorType, byte bitDepth)
    {
        // Samples that differ only in their low byte, and byte-swapped values
        ushort[] values = [0x1234, 0x1235, 0x3412, 0x00FF, 0xFF00, 0x0001, 0xFFFE, 0x8000];
        var channels = PngTestImage.GetChannels(colorType);
        var samples = Enumerable.Range(0, values.Length * channels).Select(i => values[(i / channels + i) % values.Length]).ToArray();
        var image = PngTestImage.Create(values.Length, 1, colorType, bitDepth, samples);
        foreach (var interlaced in new[] { false, true })
        {
            using var decoded = Image.Load<Rgba64>(image.Encode(interlaced));
            AssertPixels(image.GetExpectedPixels(), decoded, interlaced ? "interlaced" : "progressive");
        }

        if (colorType == 0)
        {
            using var gray = Image.Load<Gray16>(image.Encode(interlaced: false));
            var pixels = new Gray16[values.Length];
            gray.Frames[0].CopyPixelDataTo(pixels);
            Assert.Equal(samples, pixels.Select(pixel => pixel.Value));
        }
    }

    [Fact]
    public void SubByteGrayIsScaledExactlyAndPaletteEntriesAreExpanded()
    {
        // 1/2/4-bit gray scale by 255, 85 and 17 (every value of the range)
        foreach (var bitDepth in new byte[] { 1, 2, 4 })
        {
            var max = (1 << bitDepth) - 1;
            var samples = Enumerable.Range(0, max + 1).Select(value => (ushort)value).ToArray();
            using var gray = Image.Load<Gray8>(PngTestImage.Create(samples.Length, 1, 0, bitDepth, samples).Encode(interlaced: false));
            var pixels = new Gray8[samples.Length];
            gray.Frames[0].CopyPixelDataTo(pixels);
            Assert.Equal(samples.Select(value => (byte)(value * 255 / max)), pixels.Select(pixel => pixel.Value));
        }

        // A palette without tRNS is opaque; tRNS entries apply to the first indices only
        var palette = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
        var data = PngTestImage.Create(3, 1, 3, 2, [0, 1, 2], palette, transparency: [0, 128]).Encode(interlaced: false);
        using var image = Image.Load<Rgba32>(data);
        var rgba = new Rgba32[3];
        image.Frames[0].CopyPixelDataTo(rgba);
        Assert.Equal([new Rgba32(10, 20, 30, 0), new Rgba32(40, 50, 60, 128), new Rgba32(70, 80, 90, 255)], rgba);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(100)]
    public void ImageDataSplitIntoManyChunksDecodesTheSame(int idatChunkSize)
    {
        var image = PngTestImage.CreateRandom(17, 11, 6, 8, transparency: false, seed: 42);
        foreach (var interlaced in new[] { false, true })
        {
            using var decoded = Image.Load<Rgba64>(image.Encode(interlaced, idatChunkSize: idatChunkSize));
            AssertPixels(image.GetExpectedPixels(), decoded, $"IDAT chunks of {idatChunkSize} bytes");
        }

        // Empty IDAT chunks are legal anywhere in the run
        var stream = SyntheticImages.Zlib(image.GetFilteredData(interlaced: false));
        var builder = new SyntheticImages.PngBuilder().Header(17, 11, 8, 6).Chunk("IDAT", []).Chunk("IDAT", stream.AsSpan(0, 10)).Chunk("IDAT", []).Chunk("IDAT", stream.AsSpan(10)).Chunk("IDAT", []);
        using var withEmpty = Image.Load<Rgba64>(builder.End().ToArray());
        AssertPixels(image.GetExpectedPixels(), withEmpty, "empty IDAT chunks");
    }

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task LargeImagesStreamThroughEveryInputVariant(InputVariant variant)
    {
        // About 470 KB of incompressible image data in one IDAT chunk: inflated piece by piece (bounded feed), short reads and non-seekable streams included
        var image = PngTestImage.CreateRandom(160, 184, 6, 16, transparency: false, seed: 7);
        var expected = image.GetExpectedPixels();
        foreach (var interlaced in new[] { false, true })
        {
            using var decoded = await InputVariants.LoadAsync<Rgba64>(variant, image.Encode(interlaced), ImageFormat.Png, options: null, XunitCancellationToken);
            AssertPixels(expected, decoded, variant.ToString());
        }
    }

    [Fact]
    public void DecodingBuffersAreBoundedAndReleased()
    {
        // An interlaced 16-bit image converted to Bgra32: no intermediate full image (source or converted) is allocated
        var image = PngTestImage.CreateRandom(512, 256, 6, 16, transparency: false, seed: 3);
        var data = image.Encode(interlaced: true);
        using var decoded = Image.Load<Bgra32>(data);
        var scope = decoded.Owner.Scope;
        var diagnostics = scope.GetDiagnostics();
        var pixels = scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        Assert.True(diagnostics.PeakLiveBytes - pixels < 256 * 1024, $"Peak {diagnostics.PeakLiveBytes} bytes for {pixels} bytes of pixels: the decoder state must stay bounded by a few scanlines.");
    }

    [Fact]
    public void InterlacedAndProgressiveEncodingsDecodeTheSameInEveryPixelFormat()
    {
        var image = PngTestImage.CreateRandom(13, 9, 6, 8, transparency: false, seed: 11);
        var progressive = image.Encode(interlaced: false);
        var interlaced = image.Encode(interlaced: true);
        AssertSame<Bgra32>(progressive, interlaced);
        AssertSame<Rgba64>(progressive, interlaced);

        var opaque = PngTestImage.CreateRandom(13, 9, 2, 16, transparency: false, seed: 12);
        AssertSame<Rgb24>(opaque.Encode(interlaced: false), opaque.Encode(interlaced: true));
        AssertSame<Gray8>(opaque.Encode(interlaced: false), opaque.Encode(interlaced: true));
        AssertSame<Gray16>(opaque.Encode(interlaced: false), opaque.Encode(interlaced: true));

        static void AssertSame<TPixel>(byte[] first, byte[] second)
            where TPixel : unmanaged, IEquatable<TPixel>
        {
            using var a = Image.Load<TPixel>(first);
            using var b = Image.Load<TPixel>(second);
            var pixelsA = new TPixel[a.Width * a.Height];
            var pixelsB = new TPixel[b.Width * b.Height];
            a.Frames[0].CopyPixelDataTo(pixelsA);
            b.Frames[0].CopyPixelDataTo(pixelsB);
            Assert.Equal(pixelsA, pixelsB);
        }
    }

    [Fact]
    public void TypedDecodingObeysTheAlphaPolicy()
    {
        // Gray+alpha with one translucent pixel: removing alpha requires an explicit background (also when interlaced)
        var image = PngTestImage.Create(2, 1, 4, 8, [200, 255, 100, 51]);
        foreach (var interlaced in new[] { false, true })
        {
            var data = image.Encode(interlaced);
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(data));
            Assert.Equal(ImageFormat.Png, exception.Format);
            Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Gray8>(data));

            var options = new ImageDecodeOptions { Conversion = new PixelConversionOptions { BackgroundColor = new Rgba32(0, 0, 255, 255) } };
            using var flattened = Image.Load<Rgb24>(data, options);
            var pixels = new Rgb24[2];
            flattened.Frames[0].CopyPixelDataTo(pixels);

            // out = (src * a + bg * (255 - a) + 127) / 255
            Assert.Equal([new Rgb24(200, 200, 200), new Rgb24(20, 20, (byte)(((100 * 51) + (255 * 204) + 127) / 255))], pixels);
        }

        // Opaque pixels never need a background; 16 -> 8 bits rounds to nearest
        var opaque = PngTestImage.Create(3, 1, 0, 16, [0x0080, 0x0081, 0xFFFF]);
        using var reduced = Image.Load<Gray8>(opaque.Encode(interlaced: true));
        var gray = new Gray8[3];
        reduced.Frames[0].CopyPixelDataTo(gray);
        Assert.Equal([(byte)0, (byte)1, (byte)255], gray.Select(pixel => pixel.Value));
    }

    [Fact]
    public void AnimatedPngIsNeverDecodedAsAStillImage()
    {
        // An acTL before the image data routes the file to APNG decoding: a one-frame animation, never a still image
        var apng = SyntheticImages.Apng(2, 2, frames: 1);
        using (var animated = Image.Load(apng))
        {
            Assert.True(animated.IsAnimated);
            Assert.NotNull(animated.Animation);
            Assert.Equal(PixelFormat.Rgba32, animated.PixelFormat);
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(apng)))
        {
            Assert.True(reader.Info.IsAnimated);
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Null(reader.ReadFrame());
        }

        // An acTL after the image data is ignored: a static PNG, as for non-APNG decoders
        var image = PngTestImage.CreateRandom(2, 2, 6, 8, transparency: false, seed: 1);
        var late = image.Encode(interlaced: false, afterImageData: builder => builder.AnimationControl(2, 0));
        using var still = Image.Load<Rgba64>(late);
        Assert.False(still.IsAnimated);
        AssertPixels(image.GetExpectedPixels(), still, "acTL after IDAT");
    }

    public static TheoryData<string, byte[]> CorruptImageData
    {
        get
        {
            var image = PngTestImage.CreateRandom(6, 4, 2, 8, transparency: false, seed: 5);
            var filtered = image.GetFilteredData(interlaced: false);
            var rowBytes = 1 + (6 * 3);
            var badFilter = filtered.ToArray();
            badFilter[rowBytes * 2] = 5;
            var stream = SyntheticImages.Zlib(filtered);
            var badAdler = stream.ToArray();
            badAdler[^1] ^= 1;
            var badHeader = stream.ToArray();
            badHeader[1] ^= 0x20; // FDICT: preset dictionaries are not allowed (and the header check fails)
            return new TheoryData<string, byte[]>
            {
                { "filter type 5", image.Encode(false, zlib: SyntheticImages.Zlib(badFilter)) },
                { "one scanline too many", image.Encode(false, zlib: SyntheticImages.Zlib([.. filtered, .. filtered.AsSpan(0, rowBytes)])) },
                { "one byte too many", image.Encode(false, zlib: SyntheticImages.Zlib([.. filtered, 0])) },
                { "one scanline missing", image.Encode(false, zlib: SyntheticImages.Zlib(filtered.AsSpan(0, filtered.Length - rowBytes))) },
                { "one byte missing", image.Encode(false, zlib: SyntheticImages.Zlib(filtered.AsSpan(0, filtered.Length - 1))) },
                { "Adler-32 mismatch", image.Encode(false, zlib: badAdler) },
                { "Adler-32 missing", image.Encode(false, zlib: stream[..^4]) },
                { "bytes after the zlib datastream", image.Encode(false, zlib: [.. stream, 0, 0]) },
                { "invalid zlib header", image.Encode(false, zlib: badHeader) },
                { "empty datastream", image.Encode(false, zlib: []) },
                { "palette index out of range", PngTestImage.Create(2, 1, 3, 8, [0, 2], [1, 2, 3, 4, 5, 6]).Encode(false) },
            };
        }
    }

    [Theory]
    [MemberData(nameof(CorruptImageData))]
    public void CorruptImageDataIsInvalidContent(string name, byte[] data)
    {
        // The container is valid (a full scan succeeds); only pixel decoding finds the defect, eager and sequential alike
        _ = name;
        Assert.Equal(ImageIdentifyMode.FullScan, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).IdentifyMode);
        Assert.Equal(ImageFormat.Png, Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Format);
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        Assert.Throws<InvalidImageContentException>(() => reader.ReadFrame());
    }

    [Fact]
    public void TruncatedZlibDatastreamsAreInvalidAtEveryLength()
    {
        // The BCL inflater reports truncated data as a clean end: every prefix of the datastream (valid CRCs) must fail
        var image = PngTestImage.CreateRandom(5, 4, 0, 8, transparency: false, seed: 9);
        foreach (var interlaced in new[] { false, true })
        {
            var stream = SyntheticImages.Zlib(image.GetFilteredData(interlaced));
            for (var length = 0; length < stream.Length; length++)
            {
                var data = image.Encode(interlaced, zlib: stream[..length]);
                Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
            }
        }
    }

    [Fact]
    public void DecompressionIsBoundedByTheImageSize()
    {
        // A zlib bomb: a 4x4 image followed by 64 MiB of zeros in the same datastream (about 64 KB compressed). Decoding
        // stops at the first byte past the last scanline instead of inflating (or buffering) the rest.
        var image = PngTestImage.CreateRandom(4, 4, 0, 8, transparency: false, seed: 2);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(image.GetFilteredData(interlaced: false));
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < 64; i++)
            {
                zlib.Write(zeros);
            }
        }

        var data = image.Encode(interlaced: false, zlib: compressed.ToArray());
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains("more bytes than the image needs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceLimitsApplyToDecoding()
    {
        var image = PngTestImage.CreateRandom(8, 8, 6, 16, transparency: false, seed: 4);
        var data = image.Encode(interlaced: true);
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = 63 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxLiveAllocationBytes = 1024 }))).Kind);
        using var atLimit = Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = 64, MaxEncodedBytes = data.Length }));
        Assert.Equal(8, atLimit.Width);

        static ImageDecodeOptions Options(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Fact]
    public void MetadataIsDecodedIndependentlyOfThePixelsAndBounded()
    {
        var image = PngTestImage.CreateRandom(3, 2, 2, 8, transparency: false, seed: 8);
        var text = new string('t', 5000);
        var data = image.Encode(interlaced: true, afterImageData: builder => builder.Text("After", "the image data").CompressedText("Long", Encoding.Latin1.GetBytes(text)));

        // Eager loads and reader frames carry the metadata that follows the image data
        using (var eager = Image.Load(data))
        {
            Assert.Equal(["After", "Long"], eager.Metadata.TextEntries.Select(entry => entry.Keyword));
            Assert.Equal(text, eager.Metadata.TextEntries[1].Value);
            Assert.Equal(ImageFormat.Png, eager.Metadata.SourceFormat);
        }

        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data)))
        {
            Assert.Empty(reader.Info.Metadata.TextEntries);
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(["After", "Long"], frame.Metadata.TextEntries.Select(entry => entry.Keyword));
            Assert.Null(reader.ReadFrame());
        }

        // Decompressed text counts toward MaxMetadataBytes: the tEXt data (20 bytes), keyword "Long" + 5000 inflated bytes
        var limit = 20 + 4 + 5000;
        using (var atLimit = Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = limit } } }))
        {
            Assert.HasCount(2, atLimit.Metadata.TextEntries);
        }

        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = limit - 1 } } }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
    }

    [Fact]
    public void SequentialReadersDecodeIntoReusedDestinations()
    {
        var first = PngTestImage.CreateRandom(7, 5, 3, 4, transparency: true, seed: 21);
        var data = first.Encode(interlaced: true);
        using var destination = new Image<Rgba64>(7, 5, new Rgba64(1, 2, 3, 4));
        using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(data)))
        {
            Assert.True(reader.ReadFrameInto(destination));
            Assert.False(reader.ReadFrameInto(destination));
        }

        AssertPixels(first.GetExpectedPixels(), destination, "ReadFrameInto");

        using var limited = Image.OpenReader<Rgba64>(new MemoryStream(data), new ImageReaderOptions { FrameLimit = 1 });
        using var frame = limited.ReadFrame();
        Assert.NotNull(frame);
        Assert.Null(limited.ReadFrame());
    }

    [Fact]
    public void Adam7PassGeometryCoversEveryPixelOnce()
    {
        // Every size up to 17x17 (all combinations of empty passes): each pixel holds its own coordinates
        for (var height = 1; height <= 17; height++)
        {
            for (var width = 1; width <= 17; width++)
            {
                var samples = new ushort[width * height * 3];
                for (var i = 0; i < width * height; i++)
                {
                    samples[i * 3] = (ushort)(i % width);
                    samples[(i * 3) + 1] = (ushort)(i / width);
                    samples[(i * 3) + 2] = 0xA5;
                }

                var image = PngTestImage.Create(width, height, 2, 8, samples);
                using var decoded = Image.Load<Rgba64>(image.Encode(interlaced: true));
                AssertPixels(image.GetExpectedPixels(), decoded, $"{width}x{height}");
            }
        }
    }

    [Fact]
    public void FilterReconstructionMatchesTheSpecificationPredictors()
    {
        // Paeth: the neighbor closest to p = left + above - upper left, ties in the order left, above, upper left
        Assert.Equal(10, PngFilters.PaethPredictor(10, 10, 10));
        Assert.Equal(20, PngFilters.PaethPredictor(20, 10, 10)); // p = 20: left is exact
        Assert.Equal(30, PngFilters.PaethPredictor(10, 30, 10)); // p = 30: above is exact
        Assert.Equal(20, PngFilters.PaethPredictor(10, 20, 5)); // p = 25: above (distance 5)
        Assert.Equal(15, PngFilters.PaethPredictor(10, 20, 15)); // p = 15: upper left (distance 0)
        Assert.Equal(0, PngFilters.PaethPredictor(0, 10, 10)); // p = 0: left (distance 0) before upper left (distance 10)
        Assert.Equal(40, PngFilters.PaethPredictor(40, 40, 0)); // p = 80: left and above tie (distance 40): left

        Span<byte> row = [1, 2, 3, 4];
        Assert.Throws<InvalidImageContentException>(() => PngFilters.Unfilter(5, [1, 2], [0, 0], 1));
        PngFilters.Unfilter(PngFilters.Average, row, [10, 20, 30, 40], 2);
        Assert.Equal([1 + 5, 2 + 10, 3 + ((6 + 30) / 2), 4 + ((12 + 40) / 2)], row.ToArray().Select(value => (int)value));
    }

    private static void AssertPixels(Rgba64[] expected, Image<Rgba64> image, string context)
    {
        var actual = new Rgba64[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(actual);
        for (var i = 0; i < expected.Length; i++)
        {
            if (!expected[i].Equals(actual[i]))
                Assert.Fail($"{context}: pixel ({i % image.Width}, {i / image.Width}) is {Format(actual[i])}, expected {Format(expected[i])}.");
        }

        static string Format(Rgba64 pixel) => string.Create(CultureInfo.InvariantCulture, $"({pixel.R:X4}, {pixel.G:X4}, {pixel.B:X4}, {pixel.A:X4})");
    }
}
