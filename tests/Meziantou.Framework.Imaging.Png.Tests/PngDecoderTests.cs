using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
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
    public async Task MetadataAfterTheImageDataIsBoundedBeforeItIsBuffered()
    {
        // A 1 MB tEXt chunk after the image data, with a 4 KiB budget: loads fail from its header, without reading its data
        var image = PngTestImage.CreateRandom(3, 2, 2, 8, transparency: false, seed: 8);
        var data = image.Encode(interlaced: false, afterImageData: builder => builder.Chunk("tEXt", [.. "Comment"u8, 0, .. new byte[1_000_000]]));
        var options = new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 4096 } } };

        Assert.Equal(new Size(3, 2), Image.Identify(data, new ImageIdentifyOptions { Configuration = options.Configuration }).Size);
        Assert.Equal(1_000_008, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, options)).Requested);

        await using (var stream = new TestInputStream(data) { Seekable = false, ForbidAsynchronousReads = true })
        {
            var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(stream, options));
            Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
            Assert.True(stream.BytesRead < 64 * 1024, $"{stream.BytesRead} bytes were read.");
        }

        await using (var stream = new TestInputStream(data) { Seekable = false, ForbidSynchronousReads = true })
        {
            var exception = await Assert.ThrowsAsync<ImageResourceLimitException>(() => Image.LoadAsync(stream, options, XunitCancellationToken));
            Assert.Equal(ImageResourceLimitKind.MetadataBytes, exception.Kind);
            Assert.True(stream.BytesRead < 64 * 1024, $"{stream.BytesRead} bytes were read.");
        }
    }

    [Fact]
    public void InterlacedImagesAreShownPassByPass()
    {
        // IDAT chunks of 32 bytes: the passes are completed by different pieces of the data. Each rendering holds the
        // completed passes, every other pixel being the top left pixel of its block in them (rows of the next pass may
        // already be there). The image of the caller may change between two reads.
        var image = PngTestImage.CreateRandom(19, 17, 6, 8, transparency: false, seed: 3);
        var data = image.Encode(interlaced: true, idatChunkSize: 32);
        var expected = image.GetExpectedPixels();
        using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data));
        using var first = new Image<Rgba64>(19, 17, new Rgba64(1, 2, 3, 4));
        using var second = new Image<Rgba64>(19, 17, new Rgba64(5, 6, 7, 8));
        var passes = new List<int>();
        for (var index = 0; ; index++)
        {
            var destination = index % 2 == 0 ? first : second;
            var refinement = reader.ReadFrameRefinementInto(destination);
            Assert.NotNull(refinement);
            if (refinement.IsFinal)
            {
                Assert.Equal(7, refinement.CompletedPasses);
                Assert.Equal(1, refinement.Downsampling);
                AssertPixels(expected, destination, "final rendering");
                break;
            }

            var completed = refinement.CompletedPasses;
            passes.Add(completed);
            var (blockWidth, blockHeight) = PngInterlace.GetBlockSize(completed);
            Assert.Equal(Math.Max(blockWidth, blockHeight), refinement.Downsampling);
            var pixels = new Rgba64[19 * 17];
            destination.Frames[0].CopyPixelDataTo(pixels);
            for (var y = 0; y < 17; y++)
            {
                for (var x = 0; x < 19; x++)
                {
                    var anchor = expected[((y - (y % blockHeight)) * 19) + x - (x % blockWidth)];
                    var pixel = pixels[(y * 19) + x];
                    var isComplete = PngInterlace.GetPassOfPixel(x, y) < completed;
                    Assert.True(isComplete ? pixel == expected[(y * 19) + x] : pixel == anchor || pixel == expected[(y * 19) + x], $"rendering of {completed} passes, pixel ({x}, {y})");
                }
            }
        }

        Assert.Equal([1, 2, 3, 4, 5, 6], passes);
        Assert.Null(reader.ReadFrameRefinementInto(first));

        // A read of the frame after a rendering gives the frame, in any image
        using var other = Image.OpenReader<Rgba64>(new MemoryStream(data));
        Assert.NotNull(other.ReadFrameRefinementInto(first));
        using var frame = other.ReadFrame();
        Assert.NotNull(frame);
        AssertPixels(expected, (Image<Rgba64>)frame, "frame after a rendering");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATruncatedImageIsShownAsFarAsItWasDecodedThenReported(bool interlaced)
    {
        // Every prefix of the file that ends in the image data: the rendering has the rows (or the passes) that were
        // decoded, the other pixels being zero or, with completed passes, the pixel of their block; the next read reports
        // the truncation
        var image = PngTestImage.CreateRandom(9, 10, 6, 8, transparency: false, seed: 5);
        var data = image.Encode(interlaced, idatChunkSize: 16);
        var expected = image.GetExpectedPixels();
        var partials = 0;

        // From the data of the first IDAT chunk: a reader is opened once the header is complete
        var start = data.AsSpan().IndexOf("IDAT"u8) + 4;
        for (var length = start; length < data.Length - 1; length++)
        {
            using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data[..length]));
            using var destination = new Image<Rgba64>(9, 10, new Rgba64(1, 2, 3, 4));

            // Renderings of the completed passes, then the one of what was decoded, then the truncation
            while (true)
            {
                FrameRefinement? refinement;
                try
                {
                    refinement = reader.ReadFrameRefinementInto(destination);
                }
                catch (InvalidImageContentException)
                {
                    break;
                }

                Assert.NotNull(refinement);
                Assert.False(refinement.IsFinal);
                var completed = refinement.CompletedPasses;
                var (blockWidth, blockHeight) = interlaced && completed > 0 ? PngInterlace.GetBlockSize(completed) : (8, 8);
                var pixels = new Rgba64[9 * 10];
                destination.Frames[0].CopyPixelDataTo(pixels);
                for (var y = 0; y < 10; y++)
                {
                    for (var x = 0; x < 9; x++)
                    {
                        var pixel = pixels[(y * 9) + x];
                        var isAnchor = interlaced && pixel == expected[((y - (y % blockHeight)) * 9) + x - (x % blockWidth)];
                        Assert.True(pixel == expected[(y * 9) + x] || pixel == default || isAnchor, $"length {length}, pixel ({x}, {y})");
                    }
                }

                if (!interlaced)
                {
                    // The rows that were decoded are the first ones
                    var decodedRows = Enumerable.Range(0, 10).TakeWhile(y => pixels.AsSpan(y * 9, 9).SequenceEqual(expected.AsSpan(y * 9, 9))).Count();
                    Assert.True(pixels.AsSpan(decodedRows * 9).IndexOfAnyExcept(default(Rgba64)) < 0, $"length {length}");
                }

                if (pixels.Any(pixel => pixel != default))
                {
                    partials++;
                }
            }

            Assert.Throws<InvalidOperationException>(() => reader.ReadFrameRefinementInto(destination));
        }

        Assert.True(partials > 5, $"{partials}");
    }

    [Fact]
    public void ColorDescriptionsAndLightLevelsAreDecoded()
    {
        // cICP: primaries, transfer, matrix 0, full range; mDCV: R, G, B and white chromaticities (0.00002), luminances
        // (0.0001 cd/m2); cLLI: MaxCLL and MaxFALL (0.0001 cd/m2), to the nearest whole cd/m2
        byte[] mdcv = [0x8A, 0x48, 0x39, 0x08, 0x21, 0x34, 0x9B, 0xAA, 0x19, 0x96, 0x08, 0xFC, 0x3D, 0x13, 0x40, 0x42, 0x00, 0x98, 0x96, 0x80, 0x00, 0x00, 0x00, 0x32];
        byte[] clli = [0x00, 0x98, 0xAB, 0x18, 0x00, 0x3D, 0x09, 0x00];
        var profile = new IccProfile(new MetadataBlob(IccTestBlobs.Create("RGB ")));
        byte[] iccp = [.. "ICC profile"u8, 0, 0, .. SyntheticImages.Zlib(profile.Data.ToArray())];
        var data = Png(builder => builder.Chunk("iCCP", iccp).Chunk("cICP", [9, 16, 0, 1]).Chunk("mDCV", mdcv).Chunk("cLLI", clli));
        foreach (var metadata in new[] { Image.Identify(data).Metadata, LoadMetadata(data) })
        {
            Assert.Equal(new CicpColorDescription(9, 16, 0, fullRange: true), metadata.Cicp);
            Assert.Equal(new MasteringDisplayColorVolume(35400, 14600, 8500, 39850, 6550, 2300, 15635, 16450, 10_000_000, 50), metadata.MasteringDisplay);
            Assert.Equal(new ContentLightLevel(1001, 400), metadata.ContentLightLevel);

            // The color description takes precedence over the profile, wherever it is
            Assert.Null(metadata.IccProfile);
        }

        Assert.Null(LoadMetadata(Png(builder => builder.Chunk("cICP", [9, 16, 0, 1]).Chunk("iCCP", iccp))).IccProfile);

        // A matrix other than 0 (RGB), a wrong length or a chunk after the palette is skipped; the profile then describes the colors
        foreach (var skipped in new[]
        {
            Png(builder => builder.Chunk("cICP", [9, 16, 1, 1]).Chunk("iCCP", iccp)),
            Png(builder => builder.Chunk("iCCP", iccp).Chunk("cICP", [9, 16, 0, 1, 0])),
        })
        {
            var metadata = LoadMetadata(skipped);
            Assert.Null(metadata.Cicp);
            Assert.Equal(profile.Data.ToArray(), metadata.IccProfile?.Data.ToArray());
        }

        var palette = new SyntheticImages.PngBuilder().Header(2, 2, 8, 3).Chunk("PLTE", [1, 2, 3]).Chunk("cICP", [9, 16, 0, 1]).Chunk("cLLI", clli).ImageData(2, 2, 3, 8).End().ToArray();
        Assert.Null(LoadMetadata(palette).Cicp);
        Assert.Null(LoadMetadata(palette).ContentLightLevel);

        // Narrow-range samples would have to be expanded
        var narrow = Png(builder => builder.Chunk("cICP", [9, 16, 0, 0]));
        Assert.Equal("PNG narrow-range cICP", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(narrow)).Feature);

        static byte[] Png(Action<SyntheticImages.PngBuilder> chunks)
        {
            var builder = new SyntheticImages.PngBuilder().Header(2, 2);
            chunks(builder);
            return builder.ImageData(2, 2).End().ToArray();
        }

        static ImageMetadata LoadMetadata(byte[] data)
        {
            using var image = Image.Load(data);
            return image.Metadata.Clone();
        }
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(16)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Adam7PixelCopiesMatchTheScalarReference(int bytesPerPixel)
    {
        // The typed copies of the usual pixel sizes scatter and gather exactly the pixels of the reference, and nothing else
        var random = new Random(bytesPerPixel);
        foreach (var (sourceStep, destinationStep) in new[] { (1, 1), (1, 2), (1, 8), (2, 1), (4, 1), (8, 1), (3, 5) })
        {
            for (var count = 0; count <= 19; count++)
            {
                var sourceLength = (Math.Max(count - 1, 0) * sourceStep * bytesPerPixel) + bytesPerPixel + 3;
                var destinationLength = (Math.Max(count - 1, 0) * destinationStep * bytesPerPixel) + bytesPerPixel + 3;
                var source = new byte[sourceLength];
                random.NextBytes(source);
                var expected = new byte[destinationLength];
                random.NextBytes(expected);
                var actual = expected.ToArray();
                PngInterlace.CopyPixelsScalar(source, sourceStep, expected, destinationStep, count, bytesPerPixel);
                PngInterlace.CopyPixels(source, sourceStep, actual, destinationStep, count, bytesPerPixel);
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"steps {sourceStep}/{destinationStep}, {count} pixels");
            }
        }

        // One pixel
        var pixel = new byte[bytesPerPixel + 2];
        random.NextBytes(pixel);
        var copy = new byte[bytesPerPixel + 2];
        PngInterlace.CopyPixel(pixel.AsSpan(1), copy.AsSpan(1), bytesPerPixel);
        Assert.True(pixel.AsSpan(1, bytesPerPixel).SequenceEqual(copy.AsSpan(1, bytesPerPixel)));
        Assert.Equal(0, copy[0]);
        Assert.Equal(0, copy[^1]);

        // The last pixel of each sequence must be in its span
        Assert.Throws<ArgumentOutOfRangeException>(() => PngInterlace.CopyPixels(new byte[(2 * bytesPerPixel) - 1], 1, new byte[64], 1, 2, bytesPerPixel));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngInterlace.CopyPixels(new byte[64], 1, new byte[(7 * bytesPerPixel) - 1], 3, 3, bytesPerPixel));
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

    // Apple CgBI files written by Xcode's pngcrush 1.6.4 (`xcrun pngcrush -iphone`, Xcode on macOS 27.2) from PNG files
    // of the patterns below, and the pixels Apple ImageIO decodes from them (`sips -s format png`, sips-316, read back
    // as RGBA): rgba is 8x5 RGBA whose alpha cycles through 0, 1, 2, 3, 77, 128, 200, 254 and 255; rgb is 6x4 RGB
    // (pngcrush writes it as opaque RGBA); adam7 is the same RGBA pattern, 9x7 and interlaced
    private static readonly (string Name, string File, int Width, int Height, string ExpectedPixels)[] AppleCgbiFixtures =
    [
        (
            "rgba",
            "iVBORw0KGgoAAAAEQ2dCSTAAIAIQ80R8AAAADUlIRFIAAAAIAAAABQgGAAAAeJGtVQAAAJZJREFUNc1LCkFRAMfh/+/qkkfE5MpjJJIUue7JlTyKOp2ZgRKmZkxsw8AKlM0oMxuwGAP5NvAhSUggkMQ41XXtVD+Mc2vbyW0MHkosgrwblerhrRbZSnlqAr/F8z5BEtwvxde20TG9ZJPP8QGABB6goe/Q7+Bvvozce2DCmd3bXXwweCKRzgYuyjTCc2Fmr4WVOfgtTq8qkvgCsO4CSwAAAABJRU5ErkJggg==",
            8,
            5,
            "000000000000ff018000ff025500aa039517c74db91cc780de21c8c80326c8fe0055aa032e4fde4d5654f5807a590dc89f5e24fec4633bff00000000000000011691c8c83b96f6fe609b24ff00000000ffffff01ff808002ffaaff031ab30a4d0000000000ff000180ff8002aaffaa03b6ebdb4dd9f12280fff666c824fbabfe5500aa034f24244d762a80809c2eddc8c03338fee53894ff0000000000000001"),
        (
            "rgb",
            "iVBORw0KGgoAAAAEQ2dCSTAAIAYXnoBlAAAADUlIRFIAAAAGAAAABAgGAAAArQROQwAAAC1JREFUY2ziZPjPwKDJgI4Zm3wY/gszaDKgY8amfob/agyaDOiYsekSw39LBk0GdAwAeDwZ8gAAAABJRU5ErkJggg==",
            6,
            4,
            "000982ff290982ff520982ff7b0982ffa40982ffcd0982ff004c82ff294c95ff524ca8ff7b4cbbffa44cceffcd4ce1ff008f82ff298fa8ff528fceff7b8ff4ffa48f1affcd8f40ff00d282ff29d2bbff52d2f4ff7bd22dffa4d266ffcdd29fff"),
        (
            "adam7",
            "iVBORw0KGgoAAAAEQ2dCSTAAIAIQ80R8AAAADUlIRFIAAAAJAAAABwgGAAABrZxX9gAAAPxJREFUJcu9SwIBHAbg9/2ZNhwISeL5kZt9IA113F3iUVeEHSeSNaZLRi062Whnbo21NMUFNbT1FwTRcnUttbY0FzQ3Bdrgsz8EgLA4P6I1uVijgBFDe/x7fduYoIBylbl/pp0sD55+ggH97lR4trBtfh7diFAk3ch5BAAhpVKyqw+HH72ksWOSAAlKK970DtS7Pn8vy0OlHQgpInqsSkVRa7fpVu9rum12wqwQBEkRa//Eq3de+hQisp6KuyuJGe08pzsZddVIRWcZ+BYBkAQJAOM5Zm/q7vuyoa05DWev1DQqsQLp14vhdXbJOI4WuHtxSoIckSSH3JpLuN/5vPYPb/VSKAAAAABJRU5ErkJggg==",
            9,
            7,
            "000000000000ff018000ff025500aa039517c74db91cc780de21c8c80326c8fe282bc8ff0055aa032e4fde4d5654f5807a590dc89f5e24fec4633bff0000000000000001008080021691c8c83b96f6fe609b24ff00000000ffffff01ff808002ffaaff031ab30a4d3eb938800000000000ff000180ff8002aaffaa03b6ebdb4dd9f12280fff666c824fbabfe4900f0ff5500aa034f24244d762a80809c2eddc8c03338fee53894ff0000000000000001808080023766c8c85c6b3bfe8170aeff00000000ff00ff01ff80000200aa55033c88ee4d608d60800000000000ff00018080ff02aaaa5503d7c0ee4dfbc57a8020cb04c845d08efe6ad518ff"),
    ];

    public static TheoryData<string, string, int, int, string> AppleCgbiFiles
    {
        get
        {
            var data = new TheoryData<string, string, int, int, string>();
            foreach (var (name, file, width, height, expectedPixels) in AppleCgbiFixtures)
            {
                data.Add(name, file, width, height, expectedPixels);
            }

            return data;
        }
    }

    // pngcrush -iphone writes the iCCP and zTXt chunks of a CgBI file as raw deflate datastreams too: a 3x2 RGBA image
    // with the Display P3 profile of macOS (536 bytes), a zTXt and a tEXt chunk
    private const string AppleCgbiFileWithMetadata = "iVBORw0KGgoAAAAEQ2dCSTAAIAIQ80R8AAAADUlIRFIAAAADAAAAAggGAAAAnXRmGgAAAU1pQ0NQRGlzcGxheSBQMwAAldA/axNxAMbxz7VKIdZBvMGhw03iEKWkgi4O/QOiOISqkMTpek3SwjX+uFyRglvfQMF3oIKzm8Whjg4OgugkduvkpOCi5RwSSR0cfJbn4cszPDxMXUhDyE9ha1AWqzeXkla7k8wciURAmg3DYrN5B/743/rxadT9cDkNIfd/qq13hxl+YT4LRUlUR/NRGUqix4iLVrtDtIe4P8rPEa+N8ivExb3VZaJ3SLKNdJ3oK+prJ3j/RN7Kt7Pxhgiz3cH9u4gxZ8WmoSCX2pFoWvhH/yrmLHso2FHY1LehlFgUBLmuxC0DmSvqEg3zGhqtdicZ//dx/F99wnYPuXFQVdXrCbt9wMtr1PYn7NJ1zp3h7X5IixRMY6rX49sLzrY5/57ag2FvoTFaP7vE6S9V9f0iM0843quqn0+r6vgZ0595M/gNE/oCgQAAACl6VFh0Q29tbWVudAAAc87PLShKLS5OTVFIqlQoyEtPLiotzlDQzSzIyM9LBQCk8jF4AAAACnRFWHRUaXRsZQBDZ0JJgyqiuAAAABxJREFUY2RgYGBgZGBgZGRgZGRkYmRgdhTn87IQkjUGABhWqjAAAAAASUVORK5CYII=";

    [Theory]
    [MemberData(nameof(AppleCgbiFiles))]
    public void AppleCgbiFilesDecodeLikeAppleImageIo(string name, string file, int width, int height, string expectedPixels)
    {
        var data = Convert.FromBase64String(file);
        var expected = Convert.FromHexString(expectedPixels);
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(ImageFormat.Png, info.Format);
        Assert.Equal(new Size(width, height), info.Size);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);

        using (var untyped = Image.Load(data))
        {
            Assert.Equal(PixelFormat.Rgba32, untyped.PixelFormat);
            using var typed = untyped.CloneAs<Rgba32>();
            Assert.Equal(expected, GetBytes(typed), $"{name}: untyped load");
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(expected, GetBytes(frame), $"{name}: sequential reader");
            Assert.Null(reader.ReadFrame());
        }

        // Pieces of one byte go through every state of the inflater, the end of the datastream included
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1 };
        using var image = Image.Load<Rgba32>(stream);
        Assert.Equal(expected, GetBytes(image), $"{name}: one byte at a time");

        static byte[] GetBytes(Image<Rgba32> image)
        {
            var pixels = new Rgba32[image.Width * image.Height];
            image.Frames[0].CopyPixelDataTo(pixels);
            return [.. pixels.SelectMany(pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A })];
        }
    }

    [Fact]
    public void AppleCgbiMetadataIsReadFromRawDeflateDatastreams()
    {
        using var image = Image.Load(Convert.FromBase64String(AppleCgbiFileWithMetadata));
        var profile = image.Metadata.IccProfile;
        Assert.NotNull(profile);
        Assert.Equal("0ff6958f98684c61f6bbdce1368ddeaf3873baf84545baba482e920d92a914c0", Convert.ToHexStringLower(SHA256.HashData(profile.Data.Span)));
        Assert.Equal(["Comment", "Title"], image.Metadata.TextEntries.Select(entry => entry.Keyword));
        Assert.Equal(["Compressed by pngcrush -iphone", "CgBI"], image.Metadata.TextEntries.Select(entry => entry.Value));

        // Both datastreams are read in a CgBI file; a truncated one, or one followed by other bytes, is not adopted
        var text = RawDeflate("Raw deflate text"u8);
        byte[] keyword = [.. "Comment"u8, 0, 0];
        using var texts = Image.Load(new SyntheticImages.PngBuilder().Chunk("CgBI", [0x30, 0x00, 0x20, 0x02]).Header(1, 1)
            .Chunk("zTXt", [.. keyword, .. text])
            .Chunk("zTXt", [.. keyword, .. SyntheticImages.Zlib("zlib text"u8)])
            .Chunk("zTXt", [.. keyword, .. text.AsSpan(0, text.Length - 1)])
            .Chunk("zTXt", [.. keyword, .. text, 0])
            .Chunk("IDAT", RawDeflate([0, 0, 0, 0, 0])).End().ToArray());
        Assert.Equal(["Raw deflate text", "zlib text"], texts.Metadata.TextEntries.Select(entry => entry.Value));
    }

    [Fact]
    public void AppleCgbiColorsAreUnpremultipliedLikeAppleImageIo()
    {
        // Every premultiplied color c and alpha a with c <= a (B = c, G = c / 2, R = 3c / 4 at x = c, y = a). Apple ImageIO
        // (sips-316) gives round(c * 255 / a) for each of these 32,896 pairs, which was checked on the same file: colors
        // with a low alpha cannot be recovered exactly (an alpha of 1 only leaves 0 and 255)
        var scanlines = new byte[256 * (1 + (256 * 4))];
        for (var alpha = 0; alpha < 256; alpha++)
        {
            for (var color = 0; color < 256; color++)
            {
                var value = color <= alpha ? color : 0;
                var offset = (alpha * (1 + (256 * 4))) + 1 + (color * 4);
                scanlines[offset] = (byte)value;
                scanlines[offset + 1] = (byte)(value / 2);
                scanlines[offset + 2] = (byte)(value * 3 / 4);
                scanlines[offset + 3] = (byte)alpha;
            }
        }

        using var image = Image.Load<Rgba32>(CreateAppleCgbiFile(256, 256, colorType: 6, RawDeflate(scanlines)));
        var pixels = new Rgba32[256 * 256];
        image.Frames[0].CopyPixelDataTo(pixels);
        for (var alpha = 0; alpha < 256; alpha++)
        {
            for (var color = 0; color < 256; color++)
            {
                var value = color <= alpha ? color : 0;
                var expected = new Rgba32(Unpremultiply(value * 3 / 4, alpha), Unpremultiply(value / 2, alpha), Unpremultiply(value, alpha), (byte)alpha);
                if (!expected.Equals(pixels[(alpha * 256) + color]))
                    Assert.Fail($"Premultiplied ({value * 3 / 4}, {value / 2}, {value}, {alpha}) decodes to {pixels[(alpha * 256) + color]}, expected {expected}.");
            }
        }

        static byte Unpremultiply(int value, int alpha) => alpha == 0 ? (byte)0 : (byte)Math.Round(value * 255.0 / alpha, MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void AppleCgbiBgrSamplesAreSwapped()
    {
        // No tool at hand writes an RGB CgBI file; Apple ImageIO (sips-316) decodes this one to (20, 10, 200) and (7, 6, 5)
        using var image = Image.Load(CreateAppleCgbiFile(2, 1, colorType: 2, RawDeflate([0, 200, 10, 20, 5, 6, 7])));
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        using var pixels = image.CloneAs<Rgb24>();
        var actual = new Rgb24[2];
        pixels.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal([new Rgb24(20, 10, 200), new Rgb24(7, 6, 5)], actual);
    }

    public static TheoryData<string, byte[]> InvalidAppleCgbiFiles
    {
        get
        {
            byte[] cgbi = [0x30, 0x00, 0x20, 0x02];
            byte[] scanlines = [0, 1, 2, 3, 4, 0, 0, 0, 0];
            var idat = RawDeflate(scanlines);
            return new TheoryData<string, byte[]>
            {
                { "CgBI chunk of 5 bytes", new SyntheticImages.PngBuilder().Chunk("CgBI", [.. cgbi, 0]).Header(2, 1).Chunk("IDAT", idat).End().ToArray() },
                { "two CgBI chunks before IHDR", new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Chunk("CgBI", cgbi).Header(2, 1).Chunk("IDAT", idat).End().ToArray() },
                { "a CgBI chunk after the IHDR of a CgBI file", new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Header(2, 1).Chunk("CgBI", cgbi).Chunk("IDAT", idat).End().ToArray() },
                { "tEXt between CgBI and IHDR", new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Text("Title", "x").Header(2, 1).Chunk("IDAT", idat).End().ToArray() },
                { "CgBI file of gray samples", new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Header(2, 1, bitDepth: 8, colorType: 0).Chunk("IDAT", RawDeflate([0, 1, 2])).End().ToArray() },
                { "CgBI file of 16-bit samples", new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Header(1, 1, bitDepth: 16, colorType: 6).Chunk("IDAT", RawDeflate(new byte[9])).End().ToArray() },
                { "zlib datastream", CreateAppleCgbiFile(2, 1, colorType: 6, SyntheticImages.Zlib(scanlines)) },
                { "color above its alpha", CreateAppleCgbiFile(2, 1, colorType: 6, RawDeflate([0, 1, 2, 3, 4, 0, 0, 5, 4])) },
                { "colored transparent pixel", CreateAppleCgbiFile(2, 1, colorType: 6, RawDeflate([0, 1, 2, 3, 4, 1, 0, 0, 0])) },
                { "extra bytes after the deflate datastream", CreateAppleCgbiFile(2, 1, colorType: 6, [.. idat, 0, 0, 0, 0, 0]) },
                { "one extra byte after the deflate datastream", CreateAppleCgbiFile(2, 1, colorType: 6, [.. idat, 0]) },
                { "one scanline too many", CreateAppleCgbiFile(2, 1, colorType: 6, RawDeflate([.. scanlines, .. scanlines])) },
                { "one scanline too few", CreateAppleCgbiFile(2, 2, colorType: 6, idat) },
            };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidAppleCgbiFiles))]
    public void InconsistentAppleCgbiFilesAreInvalidContent(string name, byte[] data)
    {
        _ = name;
        Assert.Equal(ImageFormat.Png, Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Format);
        Assert.Throws<InvalidImageContentException>(() =>
        {
            using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
            using var frame = reader.ReadFrame();
        });
    }

    [Fact]
    public void TruncatedAppleCgbiFilesAreInvalidAtEveryLength()
    {
        // Every prefix of the file after its signature, and every prefix of its deflate datastream in a file whose chunks are valid: the last
        // one only lacks the end of the final block, after the data of every scanline
        foreach (var file in AppleCgbiFixtures.Select(fixture => Convert.FromBase64String(fixture.File)))
        {
            for (var length = PngCodec.Signature.Length; length < file.Length; length++)
            {
                Assert.Throws<InvalidImageContentException>(() => Image.Load(file.AsSpan(0, length)));
            }

            var idatStart = file.AsSpan().IndexOf("IDAT"u8) + 4;
            var idatLength = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(idatStart - 8));
            var width = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(8 + 12 + 8));
            var height = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(8 + 12 + 12));
            var interlace = file[8 + 12 + 8 + 12];
            for (var length = 0; length < idatLength; length++)
            {
                var data = new SyntheticImages.PngBuilder().Chunk("CgBI", [0x30, 0x00, 0x20, 0x02]).Header(width, height, interlace: interlace)
                    .Chunk("IDAT", file.AsSpan(idatStart, length)).End().ToArray();
                Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
            }
        }
    }

    [Fact]
    public void AnimatedAndKeyedAppleCgbiFilesAreUnsupported()
    {
        byte[] cgbi = [0x30, 0x00, 0x20, 0x02];
        var animated = new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Header(1, 1).AnimationControl(1, 0).FrameControl(1, 1)
            .Chunk("IDAT", RawDeflate([0, 0, 0, 0, 0])).End().ToArray();
        Assert.Equal("PNG CgBI animation", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(animated)).Feature);

        var keyed = new SyntheticImages.PngBuilder().Chunk("CgBI", cgbi).Header(1, 1, colorType: 2).Chunk("tRNS", [0, 1, 0, 2, 0, 3])
            .Chunk("IDAT", RawDeflate([0, 3, 2, 1])).End().ToArray();
        Assert.Equal("PNG CgBI tRNS", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(keyed)).Feature);

        // Only a chunk before IHDR makes a CgBI file: after it, the chunk is an unknown critical chunk
        var late = new SyntheticImages.PngBuilder().Header(1, 1).Chunk("CgBI", cgbi).Chunk("IDAT", SyntheticImages.Zlib([0, 0, 0, 0, 0])).End().ToArray();
        Assert.Equal("PNG critical chunk CgBI", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(late)).Feature);
    }

    public static TheoryData<byte, int> ShortColorKeys => new() { { 0, 0 }, { 0, 1 }, { 2, 0 }, { 2, 2 }, { 2, 4 }, { 2, 5 } };

    [Theory]
    [MemberData(nameof(ShortColorKeys))]
    public void LenientDecodingIgnoresAColorKeyShorterThanItsColorType(byte colorType, int keyLength)
    {
        // libpng, FFmpeg, Apple ImageIO, Chromium, Firefox and WebKit ignore the chunk: the pixels are opaque, as without it
        var gray = colorType == 0;
        byte[] key = gray ? [0, 128] : [0, 0, 0, 255, 0, 0];
        var source = gray
            ? PngTestImage.Create(3, 1, 0, 8, [0, 128, 255], transparency: key[..keyLength])
            : PngTestImage.Create(3, 1, 2, 8, [255, 0, 0, 0, 255, 0, 0, 0, 255], transparency: key[..keyLength]);
        var data = source.Encode(interlaced: false);
        Rgba32[] expected = gray
            ? [new(0, 0, 0, 255), new(128, 128, 128, 255), new(255, 255, 255, 255)]
            : [new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255)];
        var pixelFormat = gray ? PixelFormat.Gray8 : PixelFormat.Rgb24;

        Assert.Equal("The tRNS chunk length is invalid for the color type.", Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Message);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));

        var lenient = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
        using (var untyped = Image.Load(data, new ImageDecodeOptions { Configuration = lenient }))
        {
            Assert.Equal(pixelFormat, untyped.PixelFormat);
        }

        using (var image = Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = lenient }))
        {
            var pixels = new Rgba32[3];
            image.Frames[0].CopyPixelDataTo(pixels);
            Assert.Equal(expected, pixels);
        }

        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var info = Image.Identify(data, new ImageIdentifyOptions { Configuration = lenient, Mode = mode });
            Assert.Equal(pixelFormat, info.PixelFormat);
            Assert.Equal(false, info.MayHaveTransparency);
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), new ImageReaderOptions { Configuration = lenient }))
        {
            Assert.Equal(pixelFormat, reader.Info.PixelFormat);
            using var frame = reader.ReadFrame();
            var pixels = new Rgba32[3];
            frame!.Frames[0].CopyPixelDataTo(pixels);
            Assert.Equal(expected, pixels);
            Assert.Null(reader.ReadFrame());
        }

        // The ignored chunk counts toward the encoded bytes like any other
        using (var atLimit = Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient, Limits = new ImageResourceLimits { MaxEncodedBytes = data.Length } } }))
        {
            Assert.Equal(pixelFormat, atLimit.PixelFormat);
        }

        var tooSmall = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient, Limits = new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 } };
        Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = tooSmall }));
    }

    public static TheoryData<string, byte[]> InvalidTransparencyChunks => new()
    {
        // The reference decoders disagree on these: libpng, Firefox, WebKit and Apple ImageIO ignore the chunk, while
        // Chromium (and FFmpeg for a palette, Pillow for all of them) use its first entries
        { "gray key too long", PngTestImage.Create(3, 1, 0, 8, [0, 128, 255], transparency: [0, 128, 0, 0]).Encode(interlaced: false) },
        { "RGB key too long", PngTestImage.Create(3, 1, 2, 8, [255, 0, 0, 0, 255, 0, 0, 0, 255], transparency: [0, 0, 0, 255, 0, 0, 0, 0]).Encode(interlaced: false) },
        { "palette alphas past the palette", new SyntheticImages.PngBuilder().Header(3, 1, 8, 3).Chunk("PLTE", [255, 0, 0, 0, 255, 0, 0, 0, 255]).Chunk("tRNS", [0, 128, 255, 64]).Chunk("IDAT", SyntheticImages.Zlib([0, 0, 1, 2])).End().ToArray() },

        // Every decoder ignores it but Pillow, which applies it
        { "palette alphas before PLTE", new SyntheticImages.PngBuilder().Header(3, 1, 8, 3).Chunk("tRNS", [0, 128]).Chunk("PLTE", [255, 0, 0, 0, 255, 0, 0, 0, 255]).Chunk("IDAT", SyntheticImages.Zlib([0, 0, 1, 2])).End().ToArray() },
    };

    [Theory]
    [MemberData(nameof(InvalidTransparencyChunks))]
    public void LenientDecodingStillRejectsTheOtherInvalidTransparencyChunks(string name, byte[] data)
    {
        _ = name;
        var lenient = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = lenient }));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = lenient }));
    }

    private static byte[] CreateAppleCgbiFile(int width, int height, byte colorType, byte[] imageData)
        => new SyntheticImages.PngBuilder().Chunk("CgBI", [0x30, 0x00, 0x20, 0x02]).Header(width, height, colorType: colorType).Chunk("IDAT", imageData).End().ToArray();

    private static byte[] RawDeflate(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
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
