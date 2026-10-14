using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Color;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Sequential (baseline/extended) Huffman JPEG decoding on synthetic inputs encoded by the independent test-side
/// encoder (<see cref="JpegTestImage"/>): every sampling layout and upsampling path, odd sizes, interleaved and
/// non-interleaved scans, restart intervals, Huffman tables with long codes, 16-bit quantization tables, compared exactly
/// with pixels computed from the specifications (direct 2-D inverse DCT and the documented upsampling/color contract); plus
/// table, entropy and restart defects, truncation at every length, corruption, streaming memory bounds, input variants,
/// typed loads, readers, cancellation, limits and metadata. The golden corpus compares the same decoder with libjpeg-turbo.
/// </summary>
/// <remarks>
/// Four-component images (CMYK, YCCK) are inks, only loaded with a color conversion. Their expected pixels are the ink
/// amounts computed by <see cref="JpegTestImage.GetExpectedInks"/>, converted by the independent decimal reference of the
/// test harness (<see cref="ReferenceIccTransform"/>): every sample must be the correctly rounded exact value.
/// </remarks>
public sealed class JpegDecoderTests
{
    private static readonly ImageDecodeOptions InkOptions = new() { CmykConversion = CmykConversionOptions.Default };

    // Interleaved scans have at most 10 blocks per MCU (T.81 B.2.3)
    private static readonly Dictionary<string, (int H, int V)[]> InkLayouts = new(StringComparer.Ordinal)
    {
        ["full"] = [(1, 1), (1, 1), (1, 1), (1, 1)],
        ["chroma-2x2"] = [(2, 2), (1, 1), (1, 1), (2, 2)],
        ["chroma-2x1"] = [(2, 1), (1, 1), (1, 1), (2, 1)],
        ["black-subsampled"] = [(2, 1), (2, 1), (2, 1), (1, 1)],
        ["mixed"] = [(1, 2), (2, 1), (1, 1), (2, 2)],
    };

    private static readonly Dictionary<string, (int H, int V)[]> Layouts = new(StringComparer.Ordinal)
    {
        ["gray"] = [(1, 1)],
        ["gray-declared-2x2"] = [(2, 2)],
        ["4:4:4"] = [(1, 1), (1, 1), (1, 1)],
        ["4:2:2"] = [(2, 1), (1, 1), (1, 1)],
        ["4:2:0"] = [(2, 2), (1, 1), (1, 1)],
        ["4:4:0"] = [(1, 2), (1, 1), (1, 1)],
        ["4:1:1"] = [(4, 1), (1, 1), (1, 1)],
        ["4:1:0"] = [(4, 2), (1, 1), (1, 1)],
        ["3x1"] = [(3, 1), (1, 1), (1, 1)],
        ["luma-upsampled"] = [(1, 1), (2, 2), (2, 2)],
        ["mixed-chroma"] = [(2, 2), (1, 2), (2, 1)],
    };

    private static readonly (int Width, int Height)[] Sizes = [(1, 1), (2, 3), (8, 8), (9, 7), (17, 9), (37, 21), (1, 19), (35, 2)];

    public static TheoryData<string, int, int> LayoutCases
    {
        get
        {
            var data = new TheoryData<string, int, int>();
            foreach (var layout in Layouts.Keys)
            {
                foreach (var (width, height) in Sizes)
                {
                    data.Add(layout, width, height);
                }
            }

            return data;
        }
    }

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void EveryLayoutDecodesToTheContract(string layout, int width, int height)
    {
        var sampling = Layouts[layout];
        var image = JpegTestImage.CreateRandom(width, height, sampling, seed: (width * 31) + height + layout.Length);
        var expected = image.GetExpectedPixels();
        var all = Enumerable.Range(0, sampling.Length).ToArray();
        var encodings = new (string Name, JpegTestEncodeOptions Options)[]
        {
            ("interleaved, fixed tables", new JpegTestEncodeOptions()),
            ("interleaved, long codes, restart 1", new JpegTestEncodeOptions { Huffman = JpegTestHuffmanStyle.Skewed, RestartInterval = 1 }),
            ("extended sequential, restart 3", new JpegTestEncodeOptions { FrameMarker = 0xC1, RestartInterval = 3, HuffmanSeed = 7, Huffman = JpegTestHuffmanStyle.Skewed }),
            ("one scan per component (reversed), tables before each scan, restart 2", new JpegTestEncodeOptions { Scans = [.. all.Reverse().Select(index => new[] { index })], TablesBeforeEachScan = true, RestartInterval = 2 }),
            ("luma alone then chroma interleaved", new JpegTestEncodeOptions { Scans = sampling.Length == 3 ? [[0], [1, 2]] : [[0]], Huffman = JpegTestHuffmanStyle.Skewed }),
        };

        foreach (var (name, options) in encodings)
        {
            using var decoded = Image.Load(image.Encode(options));
            Assert.Equal(image.PixelFormat, decoded.PixelFormat);
            AssertPixels(expected, decoded, $"{layout} {width}x{height}, {name}");
        }
    }

    public static TheoryData<string, int, int> DeepLayoutCases
    {
        get
        {
            var data = new TheoryData<string, int, int>();
            foreach (var layout in new[] { "gray", "4:4:4", "4:2:2", "4:2:0", "4:4:0", "4:1:1", "luma-upsampled", "mixed-chroma" })
            {
                foreach (var (width, height) in new[] { (1, 1), (2, 3), (9, 7), (17, 9), (37, 21) })
                {
                    data.Add(layout, width, height);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(DeepLayoutCases))]
    public void TwelveBitSamplesDecodeToTheContractScaledToSixteenBits(string layout, int width, int height)
    {
        // The contract of 8-bit samples at the precision of the frame (inverse DCT, upsampling, color conversion around
        // 2048, clamping to 0..4095), then each sample scaled to 16 bits: Gray16, or opaque Rgba64 as there is no 16-bit RGB
        var sampling = Layouts[layout];
        var image = JpegTestImage.CreateRandom(width, height, sampling, seed: (width * 37) + height + layout.Length, precision: 12);
        var expected = image.GetExpectedPixels();
        var all = Enumerable.Range(0, sampling.Length).ToArray();
        var encodings = new (string Name, JpegTestEncodeOptions Options)[]
        {
            ("extended sequential, fixed tables", new JpegTestEncodeOptions()),
            ("long codes, restart 1", new JpegTestEncodeOptions { Huffman = JpegTestHuffmanStyle.Skewed, RestartInterval = 1 }),
            ("a baseline frame marker, restart 3", new JpegTestEncodeOptions { FrameMarker = 0xC0, RestartInterval = 3, HuffmanSeed = 7, Huffman = JpegTestHuffmanStyle.Skewed }),
            ("one scan per component (reversed), tables before each scan, restart 2", new JpegTestEncodeOptions { Scans = [.. all.Reverse().Select(index => new[] { index })], TablesBeforeEachScan = true, RestartInterval = 2 }),
        };

        foreach (var (name, options) in encodings)
        {
            var data = image.Encode(options);
            Assert.Equal(12, data[FindSegment(data, options.FrameMarker ?? 0xC1) + 4]);
            var info = Image.Identify(data);
            Assert.Equal(12, info.BitsPerComponent);
            Assert.Equal(sampling.Length == 1 ? PixelFormat.Gray16 : PixelFormat.Rgba64, info.PixelFormat);
            using var decoded = Image.Load(data);
            Assert.Equal(image.PixelFormat, decoded.PixelFormat);
            AssertPixels(expected, decoded, $"12-bit {layout} {width}x{height}, {name}");
        }
    }

    [Fact]
    public void TwelveBitSamplesAreScaledToTheWholeSixteenBitRange()
    {
        // Blocks with only a DC coefficient and a quantization value of 1: each sample is 2048 + DC / 8, clamped to 0..4095
        // (in increasing order: the difference of two DC coefficients has at most 15 bits)
        int[] samples = [-40, 0, 1, 2047, 2048, 4094, 4095, 4200];
        ushort[] expected = [0, 0, 16, 32759, 32776, 65519, 65535, 65535];
        var tables = new[] { Enumerable.Repeat((ushort)1, 64).ToArray(), Enumerable.Repeat((ushort)1, 64).ToArray() };
        int[] Block(int blockX)
        {
            var block = new int[64];
            block[0] = (samples[blockX] - 2048) * 8;
            return block;
        }

        var gray = JpegTestImage.FromCoefficients(samples.Length * 8, 8, [(1, 1)], tables, (_, blockX, _) => Block(blockX), precision: 12);
        using (var decoded = Image.Load<Gray16>(gray.Encode()))
        {
            for (var x = 0; x < decoded.Width; x++)
            {
                Assert.Equal(expected[x / 8], decoded.Frames[0][x, 5].Value);
            }
        }

        // The same samples in the three components of an RGB frame, in another order, and an opaque alpha sample
        static int Index(int component, int blockX, int count) => component switch
        {
            0 => blockX,
            1 => count - 1 - blockX,
            _ => blockX / 2 * 2,
        };

        var rgb = JpegTestImage.CreateRandom(samples.Length * 8, 8, [(1, 1), (1, 1), (1, 1)], seed: 1, rgb: true, precision: 12);
        Array.Fill(rgb.QuantizationTables[0], (ushort)1);
        Array.Fill(rgb.QuantizationTables[1], (ushort)1);
        for (var c = 0; c < 3; c++)
        {
            for (var blockX = 0; blockX < samples.Length; blockX++)
            {
                rgb.Components[c].Blocks[blockX] = Block(Index(c, blockX, samples.Length));
            }
        }

        using var color = Image.Load<Rgba64>(rgb.Encode());
        for (var x = 0; x < color.Width; x++)
        {
            Assert.Equal(new Rgba64(expected[Index(0, x / 8, samples.Length)], expected[Index(1, x / 8, samples.Length)], expected[Index(2, x / 8, samples.Length)], ushort.MaxValue), color.Frames[0][x, 2]);
        }
    }

    [Fact]
    public void TwelveBitRgbSamplesAndSixteenBitQuantizationTablesDecodeExactly()
    {
        foreach (var sampling in new[] { Layouts["4:4:4"], Layouts["4:2:0"] })
        {
            var image = JpegTestImage.CreateRandom(13, 11, sampling, seed: 5, rgb: true, precision: 12);
            var data = image.Encode();
            Assert.Equal(ImageColorModel.Rgb, Image.Identify(data).ColorModel);
            using var decoded = Image.Load(data);
            AssertPixels(image.GetExpectedPixels(), decoded, "12-bit Adobe RGB");
        }

        var large = JpegTestImage.CreateRandom(24, 16, Layouts["4:2:0"], seed: 11, largeQuantization: true, precision: 12);
        using var coarse = Image.Load(large.Encode());
        AssertPixels(large.GetExpectedPixels(), coarse, "12-bit samples with 16-bit DQT");
    }

    [Theory]
    [InlineData("Cmyk")]
    [InlineData("Ycck")]
    [InlineData("CmykWithoutAdobe")]
    public void TwelveBitInksAreConvertedFromTheirExactAmounts(string inkCoding)
    {
        var coding = Enum.Parse<JpegTestInkCoding>(inkCoding);
        // The ink amounts are scaled to 16 bits like any 12-bit sample, then converted by the 16-bit ICC conversion: the
        // expected colors are those of the decimal reference of the test harness
        var reference = ReferenceIccTransform.Create(IccProfile.Cgats001CompatibleCmyk.Data.Span, IccProfile.Srgb.Data.Span, intent: 1, blackPointCompensation: true);
        foreach (var layout in new[] { "full", "chroma-2x2", "mixed" })
        {
            var image = JpegTestImage.CreateRandom(23, 18, InkLayouts[layout], seed: 61 + layout.Length + (int)coding, inks: coding, precision: 12);
            var inks = image.GetExpectedDeepInks();
            byte[]? first = null;
            foreach (var options in new[] { new JpegTestEncodeOptions(), new JpegTestEncodeOptions { Huffman = JpegTestHuffmanStyle.Skewed, RestartInterval = 2, Scans = [[3], [0, 1, 2]] } })
            {
                var data = image.Encode(options);
                var info = Image.Identify(data);
                Assert.Equal(coding == JpegTestInkCoding.Ycck ? ImageColorModel.Ycck : ImageColorModel.Cmyk, info.ColorModel);
                Assert.Equal(12, info.BitsPerComponent);
                Assert.Equal("JPEG CMYK/YCCK", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
                using var decoded = Image.Load(data, InkOptions);
                Assert.Equal(PixelFormat.Rgba64, decoded.PixelFormat);
                Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
                if (first is not null)
                {
                    AssertPixels(first, decoded, $"12-bit {layout} {coding}");
                    continue;
                }

                first = GetBytes(decoded);
                var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(first.AsSpan()));
                var colors = new ushort[image.Width * image.Height * 3];
                for (var pixel = 0; pixel < image.Width * image.Height; pixel++)
                {
                    pixels.Slice(4 * pixel, 3).CopyTo(colors.AsSpan(3 * pixel));
                    Assert.Equal(ushort.MaxValue, pixels[(4 * pixel) + 3]);
                }

                Assert.Empty(reference.Compare(inks, colors));
            }
        }
    }

    [Fact]
    public void TwelveBitPixelsFollowTheRequestedPixelFormat()
    {
        var image = JpegTestImage.CreateRandom(19, 11, Layouts["4:2:0"], seed: 14, precision: 12);
        var data = image.Encode();
        var expected = unsafe(MemoryMarshal.Cast<byte, ushort>(image.GetExpectedPixels().AsSpan())).ToArray();

        // The typed load of the default format is the untyped one; a gray file loads as gray
        using (var typed = Image.Load<Rgba64>(data))
        {
            Assert.Equal(new Rgba64(expected[0], expected[1], expected[2], ushort.MaxValue), typed.Frames[0][0, 0]);
        }

        // A narrower pixel type is an explicit precision reduction (nearest rounding); the alpha sample is opaque
        using (var reduced = Image.Load<Rgb24>(data))
        {
            var pixel = reduced.Frames[0][0, 0];
            Assert.Equal(((expected[0] + 128) / 257, (expected[1] + 128) / 257, (expected[2] + 128) / 257), (pixel.R, pixel.G, pixel.B));
        }

        // A sequential reader gives the same frame
        using var stream = new MemoryStream(data);
        using var reader = Image.OpenReader<Rgba64>(stream);
        Assert.Equal(12, reader.Info.BitsPerComponent);
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(new Rgba64(expected[^4], expected[^3], expected[^2], ushort.MaxValue), frame.Frames[0][18, 10]);
        Assert.Null(reader.ReadFrame());
    }

    public static TheoryData<string, int, int, int> ArithmeticLayoutCases
    {
        get
        {
            var data = new TheoryData<string, int, int, int>();
            foreach (var layout in new[] { "gray", "4:4:4", "4:2:2", "4:2:0", "4:1:1", "mixed-chroma" })
            {
                foreach (var (width, height) in new[] { (1, 1), (9, 7), (37, 21) })
                {
                    data.Add(layout, width, height, 8);
                    data.Add(layout, width, height, 12);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ArithmeticLayoutCases))]
    public void ArithmeticCodedFramesDecodeToTheContract(string layout, int width, int height, int precision)
    {
        // The same coefficients as a Huffman-coded frame, coded as binary decisions (SOF9): the same pixels. A restart
        // marker starts the statistics again, and each conditioning table changes the bins the decisions use
        var sampling = Layouts[layout];
        var image = JpegTestImage.CreateRandom(width, height, sampling, seed: (width * 43) + height + layout.Length + precision, precision: precision);
        var expected = image.GetExpectedPixels();
        var all = Enumerable.Range(0, sampling.Length).ToArray();
        var encodings = new (string Name, JpegTestEncodeOptions Options)[]
        {
            ("default conditioning", new JpegTestEncodeOptions { Arithmetic = true }),
            ("restart 1, final zero bytes omitted", new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 1, DiscardFinalZeros = true }),
            ("DC bounds 2-5 and 0-0, AC thresholds 1 and 63, restart 3", new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 3, DcConditioning = [(2, 5), (0, 0)], AcConditioning = [1, 63] }),
            ("one scan per component (reversed), DC bounds 1-1 and 3-15, AC thresholds 20 and 9, restart 2", new JpegTestEncodeOptions { Arithmetic = true, Scans = [.. all.Reverse().Select(index => new[] { index })], RestartInterval = 2, DcConditioning = [(1, 1), (3, 15)], AcConditioning = [20, 9], DiscardFinalZeros = true }),
        };

        foreach (var (name, options) in encodings)
        {
            var data = image.Encode(options);
            Assert.Equal(precision, data[FindSegment(data, 0xC9) + 4]);
            Assert.DoesNotContain((byte)0xC4, GetSegmentMarkers(data));
            foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
            {
                var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
                Assert.Equal(precision, info.BitsPerComponent);
                Assert.Equal(image.PixelFormat, info.PixelFormat);
                Assert.Equal(sampling.Length == 1 ? ImageColorModel.Grayscale : ImageColorModel.YCbCr, info.ColorModel);
            }

            using var decoded = Image.Load(data);
            Assert.Equal(image.PixelFormat, decoded.PixelFormat);
            AssertPixels(expected, decoded, $"arithmetic {precision}-bit {layout} {width}x{height}, {name}");
        }
    }

    [Theory]
    [InlineData("Cmyk")]
    [InlineData("Ycck")]
    public void ArithmeticCodedInksAndRgbSamplesDecodeLikeTheHuffmanCodedOnes(string inkCoding)
    {
        // The entropy coding does not change what the components are
        var inks = JpegTestImage.CreateRandom(23, 18, InkLayouts["chroma-2x2"], seed: 71, inks: Enum.Parse<JpegTestInkCoding>(inkCoding));
        using (var huffman = Image.Load(inks.Encode(), InkOptions))
        using (var arithmetic = Image.Load(inks.Encode(new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 2 }), InkOptions))
        {
            Assert.Equal(PixelFormat.Rgb24, arithmetic.PixelFormat);
            AssertPixels(GetBytes(huffman), arithmetic, inkCoding);
        }

        var rgb = JpegTestImage.CreateRandom(13, 11, Layouts["4:2:0"], seed: 5, rgb: true);
        var data = rgb.Encode(new JpegTestEncodeOptions { Arithmetic = true });
        Assert.Equal(ImageColorModel.Rgb, Image.Identify(data).ColorModel);
        using var decoded = Image.Load(data);
        AssertPixels(rgb.GetExpectedPixels(), decoded, "arithmetic Adobe RGB");
    }

    [Fact]
    public void ArithmeticConditioningTablesAreValidatedAndTheLastDefinitionApplies()
    {
        var image = JpegTestImage.CreateRandom(24, 16, Layouts["4:2:0"], seed: 31);
        var expected = image.GetExpectedPixels();
        var conditioned = new JpegTestEncodeOptions { Arithmetic = true, DcConditioning = [(1, 3), (0, 2)], AcConditioning = [9, 30] };

        // A scan uses the conditioning defined when it starts: the tables written before the frame header are replaced
        byte[] others = [0x00, 0x74, 0x01, 0xF0, 0x10, 1, 0x11, 63, 0x02, 0x00, 0x13, 5];
        using (var decoded = Image.Load(image.Encode(new JpegTestEncodeOptions { Arithmetic = true, DcConditioning = conditioned.DcConditioning, AcConditioning = conditioned.AcConditioning, SegmentsBeforeFrame = [JpegTestImage.Segment(0xCC, others)] })))
        {
            AssertPixels(expected, decoded, "DAC defined twice");
        }

        // Without the segment the decoder has the default conditioning, and reads other decisions from the same bytes
        var data = image.Encode(conditioned);
        var withoutSegment = Remove(data, FindSegment(data, 0xCC), 12);
        try
        {
            using var decoded = Image.Load(withoutSegment);
            Assert.NotEqual(expected, GetBytes(decoded));
        }
        catch (InvalidImageContentException)
        {
            // Or values that no block has
        }

        // The tables of the other entropy coding are read and unused
        using (var decoded = Image.Load(image.Encode(new JpegTestEncodeOptions { Arithmetic = true, SegmentsBeforeFrame = [JpegTestImage.Segment(0xC4, Huffman(0x00, (1, 0))), JpegTestImage.Segment(0xC4, Huffman(0x13, (2, 0x00), (2, 0x11)))] })))
        {
            AssertPixels(expected, decoded, "DHT in an arithmetic-coded file");
        }

        using (var decoded = Image.Load(image.Encode(new JpegTestEncodeOptions { SegmentsBeforeFrame = [JpegTestImage.Segment(0xCC, others)] })))
        {
            AssertPixels(expected, decoded, "DAC in a Huffman-coded file");
        }

        // A segment without any table is valid and defines nothing: the conditioning defined before it still applies
        using (var decoded = Image.Load(Insert(data, FindSegment(data, 0xDA), JpegTestImage.Segment(0xCC, []))))
        {
            AssertPixels(expected, decoded, "DAC without table after the conditioning of the scan");
        }

        foreach (var arithmetic in new[] { false, true })
        {
            using var decoded = Image.Load(image.Encode(new JpegTestEncodeOptions { Arithmetic = arithmetic, SegmentsBeforeFrame = [JpegTestImage.Segment(0xCC, [])] }));
            AssertPixels(expected, decoded, "DAC without table before the frame header");
        }

        // Defects of a table are found by decoding, whatever the entropy coding of the file
        var defects = new (string Name, byte[] Payload)[]
        {
            ("half a table", [0x00]),
            ("a table and a half", [0x00, 0x10, 0x10]),
            ("class 2", [0x20, 0x10]),
            ("identifier 4", [0x04, 0x10]),
            ("AC identifier 4", [0x14, 5]),
            ("L above U", [0x00, 0x12]),
            ("Kx 0", [0x10, 0]),
            ("Kx 64", [0x11, 64]),
        };

        foreach (var (name, payload) in defects)
        {
            foreach (var arithmetic in new[] { false, true })
            {
                var defective = image.Encode(new JpegTestEncodeOptions { Arithmetic = arithmetic, SegmentsBeforeFrame = [JpegTestImage.Segment(0xCC, payload)] });
                Assert.Equal(ImageFormat.Jpeg, Image.Identify(defective, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Format);
                var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(defective));
                Assert.Equal(ImageFormat.Jpeg, exception.Format);
                Assert.False(string.IsNullOrEmpty(name));
            }
        }

        // A scan cannot select a fifth table
        var scan = FindSegment(data, 0xDA);
        var selector = (byte[])data.Clone();
        selector[scan + 6] = 0x40;
        Assert.Contains("conditioning table identifier", Assert.Throws<InvalidImageContentException>(() => Image.Load(selector)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArithmeticDataThatEndsEarlyReadsAsZeros()
    {
        // An encoder may omit the zero bytes that end a segment, so the decoder cannot tell a segment that is cut from a
        // complete one: the missing bytes are zeros, which decode to some coefficients, or to values no block can have
        var image = JpegTestImage.CreateRandom(24, 16, Layouts["4:2:0"], seed: 4, acDensity: 0.2);
        var data = image.Encode(new JpegTestEncodeOptions { Arithmetic = true });
        var scan = FindSegment(data, 0xDA);
        var decodedCuts = 0;
        for (var length = 8; length < data.Length; length++)
        {
            // A file that is cut has no EOI marker
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data.AsSpan(0, length)));
            if (length <= scan + 14 || length >= data.Length - 2)
                continue;

            byte[] cut = [.. data.AsSpan(0, length), 0xFF, 0xD9];
            try
            {
                using var decoded = Image.Load(cut);
                Assert.Equal(new Size(24, 16), decoded.Size);
                decodedCuts++;
            }
            catch (InvalidImageContentException)
            {
            }
        }

        Assert.True(decodedCuts > 0);

        // The structure of the scan is still checked: a restart marker that is missing, or out of sequence
        var restarts = JpegTestImage.CreateRandom(40, 24, Layouts["4:2:0"], seed: 9).Encode(new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 1 });
        var markers = FindRestartMarkers(restarts);
        Assert.HasCount(5, markers); // 3x2 MCUs
        Assert.Contains("restart marker is missing", Assert.Throws<InvalidImageContentException>(() => Image.Load(Remove(restarts, markers[4], 2))).Message, StringComparison.Ordinal);
        var renumbered = restarts.ToArray();
        renumbered[markers[2] + 1] = 0xD4;
        Assert.Contains("out of sequence", Assert.Throws<InvalidImageContentException>(() => Image.Load(renumbered)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RgbSamplesAreCopiedWithoutColorTransform()
    {
        foreach (var sampling in new[] { Layouts["4:4:4"], Layouts["4:2:0"] })
        {
            var image = JpegTestImage.CreateRandom(13, 11, sampling, seed: 5, rgb: true);
            var data = image.Encode();
            Assert.Equal(ImageColorModel.Rgb, Image.Identify(data).ColorModel);
            using var decoded = Image.Load(data);
            AssertPixels(image.GetExpectedPixels(), decoded, "Adobe RGB");
        }
    }

    [Fact]
    public void SixteenBitQuantizationTablesAndClampingDecodeExactly()
    {
        var image = JpegTestImage.CreateRandom(24, 16, Layouts["4:2:0"], seed: 11, largeQuantization: true);
        var data = image.Encode(new JpegTestEncodeOptions { FrameMarker = 0xC1 });
        using var decoded = Image.Load(data);
        AssertPixels(image.GetExpectedPixels(), decoded, "16-bit DQT");
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void InverseDctMatchesTheDirectTransformOfTheSpecification()
    {
        var random = new Random(3);
        var quantization = new ushort[64];
        var compared = 0;
        for (var iteration = 0; iteration < 20_000; iteration++)
        {
            for (var i = 0; i < 64; i++)
            {
                quantization[i] = (ushort)random.Next(1, iteration % 4 == 0 ? 300 : 30);
            }

            var coefficients = new int[64];
            var count = random.Next(1, 64);
            for (var i = 0; i < count; i++)
            {
                coefficients[random.Next(64)] = random.Next(-200, 201);
            }

            if (!JpegTestImage.TryTransform(coefficients, quantization, out var expected))
                continue; // within 1e-6 of a rounding boundary: both neighbors are acceptable

            var actual = new byte[64];
            var block = new double[64];
            for (var i = 0; i < 64; i++)
            {
                block[i] = coefficients[i] * (double)quantization[i];
            }

            if (coefficients.AsSpan(1).IndexOfAnyExcept(0) < 0)
            {
                JpegIdct.TransformDcOnly((long)coefficients[0] * quantization[0], actual, 8);
            }
            else
            {
                JpegIdct.Transform(block, actual, 8);
            }

            Assert.Equal(expected, actual);
            compared++;
        }

        Assert.True(compared > 19_000, $"Only {compared} blocks were compared.");

        // DC-only blocks are exact, ties upward: DC * Q / 8 = -0.5 -> 0 -> 128, 0.5 -> 1 -> 129
        var samples = new byte[64];
        JpegIdct.TransformDcOnly(-4, samples, 8);
        Assert.All(samples, sample => Assert.Equal(128, sample));
        JpegIdct.TransformDcOnly(4, samples, 8);
        Assert.All(samples, sample => Assert.Equal(129, sample));
        JpegIdct.TransformDcOnly(-2000, samples, 8);
        Assert.All(samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void HuffmanTablesMustBePrefixCodes()
    {
        var counts = new byte[16];
        counts[0] = 3;
        Assert.Null(JpegHuffmanTable.TryCreate(counts, [1, 2, 3], out var error));
        Assert.Contains("oversubscribed", error, StringComparison.Ordinal);
        Assert.Null(JpegHuffmanTable.TryCreate(new byte[16], [], out error));
        Assert.Contains("no code", error, StringComparison.Ordinal);
        counts = new byte[16];
        counts[15] = 255;
        counts[14] = 2;
        Assert.Null(JpegHuffmanTable.TryCreate(counts, new byte[257], out error));
        Assert.Contains("256", error, StringComparison.Ordinal);

        // A complete code (every 2-bit code used) and codes of every length up to 16 bits are valid
        counts = new byte[16];
        counts[1] = 4;
        Assert.NotNull(JpegHuffmanTable.TryCreate(counts, [1, 2, 3, 4], out _));
        counts = Enumerable.Repeat((byte)1, 16).ToArray();
        var table = JpegHuffmanTable.TryCreate(counts, [.. Enumerable.Range(0, 16).Select(i => (byte)i)], out _);
        Assert.NotNull(table);
        Assert.Equal(15, table.DecodeLong(0b1111_1111_1111_1110, out var length));
        Assert.Equal(16, length);
        Assert.Equal(-1, table.DecodeLong(0xFFFF, out _));
    }

    public static TheoryData<string, byte[]> TableAndScanDefects
    {
        get
        {
            var data = new TheoryData<string, byte[]>();
            var dc = Huffman(0x00, (1, 0));
            var ac = Huffman(0x10, (1, 0x00));
            var zero = JpegTestImage.PackBits([(0, 1), (0, 1)]);
            var quantization = Enumerable.Repeat((byte)1, 64).ToArray();
            data.Add("DQT precision 2", Gray([Segment(0xDB, [0x20, .. quantization]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DQT identifier 4", Gray([Segment(0xDB, [0x04, .. quantization]), Segment(0xDB, [0x00, .. quantization]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DQT truncated", Gray([Segment(0xDB, [0x00, .. quantization[..63]]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DQT 16-bit truncated", Gray([Segment(0xDB, [0x10, .. quantization]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DQT empty", Gray([Segment(0xDB, []), Segment(0xDB, [0x00, .. quantization]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DQT zero value", Gray([Segment(0xDB, [0x00, .. quantization[..10], 0, .. quantization[11..]]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DHT class 2", Gray([Dqt(), Segment(0xC4, [0x20, .. dc[1..]]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DHT identifier 4", Gray([Dqt(), Segment(0xC4, [0x04, .. dc[1..]]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DHT truncated counts", Gray([Dqt(), Segment(0xC4, dc[..10]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DHT truncated symbols", Gray([Dqt(), Segment(0xC4, Huffman(0x00, (2, 0), (2, 1))[..^1]), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DHT empty", Gray([Dqt(), Segment(0xC4, []), Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("DHT without code", Gray([Dqt(), Segment(0xC4, [0x00, .. new byte[16]]), Segment(0xC4, ac)], zero));
            data.Add("DHT oversubscribed", Gray([Dqt(), Segment(0xC4, Huffman(0x00, (1, 0), (1, 1), (1, 2))), Segment(0xC4, ac)], zero));
            data.Add("undefined DC table", Gray([Dqt(), Segment(0xC4, ac)], zero));
            data.Add("undefined AC table", Gray([Dqt(), Segment(0xC4, dc)], zero));
            data.Add("undefined quantization table", Gray([Segment(0xC4, dc), Segment(0xC4, ac)], zero));
            data.Add("table selector 4", Gray([Dqt(), Segment(0xC4, dc), Segment(0xC4, ac)], zero, scanTables: 0x44));
            data.Add("component coded twice", Gray([Dqt(), Segment(0xC4, dc), Segment(0xC4, ac)], [.. zero, .. Segment(0xDA, [1, 1, 0x00, 0, 63, 0]), .. zero]));

            // The categories of 12-bit samples end at 15 for DC differences and at 14 for AC coefficients
            data.Add("12-bit DC category 16", Gray([Dqt(), Segment(0xC4, Huffman(0x00, (1, 16))), Segment(0xC4, ac)], zero, precision: 12));
            data.Add("12-bit AC magnitude 15", Gray([Dqt(), Segment(0xC4, dc), Segment(0xC4, Huffman(0x10, (1, 0x0F)))], zero, precision: 12));

            // Entropy-coded defects (8-bit precision categories, coefficient positions, DC range, unassigned codes)
            data.Add("DC category 12", Gray([Dqt(), Segment(0xC4, Huffman(0x00, (1, 12))), Segment(0xC4, ac)], zero));
            data.Add("AC magnitude 11", Gray([Dqt(), Segment(0xC4, dc), Segment(0xC4, Huffman(0x10, (1, 0x0B)))], zero));
            data.Add("AC run past the block", Gray([Dqt(), Segment(0xC4, dc), Segment(0xC4, Huffman(0x10, (1, 0xF0), (2, 0xF1)))], JpegTestImage.PackBits([(0, 1), (0, 1), (0, 1), (0, 1), (0b10, 2), (1, 1)])));
            data.Add("unassigned Huffman code", Gray([Dqt(), Segment(0xC4, Huffman(0x00, (2, 0))), Segment(0xC4, ac)], [0xFF, 0x00, 0xFF, 0x00]));

            // 17 DC differences of +2047 exceed 16 bits (one block row of 136 pixels)
            var dcRange = Enumerable.Range(0, 17).SelectMany(_ => new[] { (0, 1), (0x7FF, 11), (0, 1) });
            data.Add("DC out of range", Gray([Dqt(), Segment(0xC4, Huffman(0x00, (1, 11))), Segment(0xC4, ac)], JpegTestImage.PackBits(dcRange), width: 136));
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TableAndScanDefects))]
    public void TableAndEntropyDefectsAreInvalidContent(string name, byte[] data)
    {
        // The container is valid: identification succeeds, decoding fails explicitly
        Assert.Equal(ImageFormat.Jpeg, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Format);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Equal(ImageFormat.Jpeg, exception.Format);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void ColorFramesNeedEveryComponentCodedOnce()
    {
        var image = JpegTestImage.CreateRandom(16, 16, Layouts["4:2:0"], seed: 2);
        var missing = image.Encode(new JpegTestEncodeOptions { Scans = [[0], [1]] });
        Assert.Contains("no scan for the component 3", Assert.Throws<InvalidImageContentException>(() => Image.Load(missing)).Message, StringComparison.Ordinal);
        var twice = image.Encode(new JpegTestEncodeOptions { Scans = [[0, 1, 2], [2]] });
        Assert.Contains("several sequential scans", Assert.Throws<InvalidImageContentException>(() => Image.Load(twice)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartMarkersAreValidated()
    {
        var image = JpegTestImage.CreateRandom(40, 24, Layouts["4:2:0"], seed: 9);
        var expected = image.GetExpectedPixels();
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 1 });
        var restarts = FindRestartMarkers(data);
        Assert.HasCount(5, restarts); // 3x2 MCUs

        using (var decoded = Image.Load(data))
        {
            AssertPixels(expected, decoded, "restart interval 1");
        }

        // Out of sequence, missing (data of the next interval cannot replace the marker), unexpected (no DRI)
        var renumbered = data.ToArray();
        renumbered[restarts[2] + 1] = 0xD4;
        Assert.Contains("out of sequence", Assert.Throws<InvalidImageContentException>(() => Image.Load(renumbered)).Message, StringComparison.Ordinal);
        var removed = Remove(data, restarts[1], 2);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(removed));
        var last = Remove(data, restarts[4], 2);
        Assert.Contains("restart marker is missing", Assert.Throws<InvalidImageContentException>(() => Image.Load(last)).Message, StringComparison.Ordinal);
        var withoutInterval = image.Encode(new JpegTestEncodeOptions { RestartInterval = 1 });
        var dri = FindSegment(withoutInterval, 0xDD);
        withoutInterval = Remove(withoutInterval, dri, 6);
        Assert.Contains("no restart interval", Assert.Throws<InvalidImageContentException>(() => Image.Load(withoutInterval)).Message, StringComparison.Ordinal);

        // Bytes after the last MCU of an interval (before its marker) and a marker after the last MCU are ignored
        var padded = Insert(data, restarts[3], [0x00, 0x12, 0xFF, 0x00]);
        using (var decoded = Image.Load(padded))
        {
            AssertPixels(expected, decoded, "bytes before a restart marker");
        }

        var trailing = Insert(data, data.Length - 2, [0xFF, 0xD5]);
        using (var decoded = Image.Load(trailing))
        {
            AssertPixels(expected, decoded, "restart marker after the last MCU");
        }
    }

    [Fact]
    public void TruncatedInputsAreInvalidAtEveryLength()
    {
        var image = JpegTestImage.CreateRandom(16, 16, Layouts["4:2:0"], seed: 4, acDensity: 0.2);
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 2 });
        var scan = FindSegment(data, 0xDA);
        for (var length = 8; length < data.Length; length++)
        {
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data.AsSpan(0, length)));
            if (length > scan + 14 && length < data.Length - 2)
            {
                // Entropy-coded data cut short but followed by EOI
                byte[] cut = [.. data.AsSpan(0, length), 0xFF, 0xD9];
                Assert.Throws<InvalidImageContentException>(() => Image.Load(cut));
            }
        }
    }

    [Fact]
    public void CorruptedBytesFailOnlyWithImageExceptions()
    {
        var image = JpegTestImage.CreateRandom(16, 8, Layouts["4:2:2"], seed: 6, acDensity: 0.15);
        foreach (var options in new[] { new JpegTestEncodeOptions { RestartInterval = 1, Huffman = JpegTestHuffmanStyle.Skewed }, new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 1, DcConditioning = [(0, 1), (1, 4)], AcConditioning = [5, 12] } })
        {
            var data = image.Encode(options);
            for (var position = 2; position < data.Length; position++)
            {
                foreach (var mask in new byte[] { 0x01, 0x10, 0x80, 0xFF })
                {
                    var corrupt = data.ToArray();
                    corrupt[position] ^= mask;
                    try
                    {
                        using var decoded = Image.Load(corrupt);
                        Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
                    }
                    catch (ImageException)
                    {
                        // Malformed, unsupported or over a limit: explicit
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task LargeImagesStreamThroughEveryInputVariant(InputVariant variant)
    {
        // About 100 KB of entropy-coded data with stuffing, long codes and restart markers: fed piece by piece
        var image = JpegTestImage.CreateRandom(181, 133, Layouts["4:2:0"], seed: 8, acDensity: 0.6);
        var expected = image.GetExpectedPixels();
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 7, Huffman = JpegTestHuffmanStyle.Skewed });
        using (var decoded = await InputVariants.LoadAsync<Rgb24>(variant, data, ImageFormat.Jpeg, options: null, XunitCancellationToken))
        {
            AssertPixels(expected, decoded, variant.ToString());
        }

        var separate = image.Encode(new JpegTestEncodeOptions { Scans = [[2], [0], [1]] });
        using (var decoded = await InputVariants.LoadAsync<Rgb24>(variant, separate, ImageFormat.Jpeg, options: null, XunitCancellationToken))
        {
            AssertPixels(expected, decoded, variant + " (separate scans)");
        }

        // Arithmetic coding has no bound of a few hundred bytes per block: the decoder waits for more bytes before an MCU
        var arithmetic = image.Encode(new JpegTestEncodeOptions { Arithmetic = true, RestartInterval = 7, DiscardFinalZeros = true });
        using (var decoded = await InputVariants.LoadAsync<Rgb24>(variant, arithmetic, ImageFormat.Jpeg, options: null, XunitCancellationToken))
        {
            AssertPixels(expected, decoded, variant + " (arithmetic coding)");
        }
    }

    [Fact]
    public void DecoderStateIsBoundedAndReleased()
    {
        // Interleaved frames are decoded in a ring of three MCU rows; frames coded in several scans keep full planes
        var image = JpegTestImage.CreateRandom(2048, 256, Layouts["4:2:0"], seed: 1, dcOnlyProbability: 1);
        var interleaved = image.Encode();
        using (var decoded = Image.Load<Rgb24>(interleaved))
        {
            var scope = decoded.Owner.Scope;
            var pixels = scope.GetLiveBytes(AllocationKind.ImagePixels);
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            var state = scope.GetDiagnostics().PeakLiveBytes - pixels;
            Assert.True(state < 320 * 1024, $"Peak decoder state {state} bytes: three MCU rows per component, the entropy buffer and two chroma rows are expected.");
        }

        var separate = image.Encode(new JpegTestEncodeOptions { Scans = [[0], [1], [2]] });
        using (var decoded = Image.Load<Rgb24>(separate))
        {
            var scope = decoded.Owner.Scope;
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
            Assert.True(state >= 2048 * 256 * 3 / 2, $"Peak decoder state {state} bytes: the full component planes are kept until the last scan.");
        }
    }

    [Fact]
    public void TypedLoadsConvertTheDecodedRows()
    {
        var color = JpegTestImage.CreateRandom(19, 13, Layouts["4:2:2"], seed: 12).Encode();
        using var rgb = Image.Load<Rgb24>(color);
        AssertSameAsConverted<Rgba32>(color, rgb);
        AssertSameAsConverted<Bgra32>(color, rgb);
        AssertSameAsConverted<Rgba64>(color, rgb);
        AssertSameAsConverted<Gray8>(color, rgb);
        AssertSameAsConverted<Gray16>(color, rgb);

        var gray = JpegTestImage.CreateRandom(9, 7, Layouts["gray"], seed: 13).Encode();
        using var luma = Image.Load<Gray8>(gray);
        AssertSameAsConverted<Rgb24>(gray, luma);
        AssertSameAsConverted<Rgba64>(gray, luma);

        static void AssertSameAsConverted<TPixel>(byte[] data, Image source)
            where TPixel : unmanaged
        {
            using var direct = Image.Load<TPixel>(data);
            using var converted = source.CloneAs<TPixel>();
            Assert.Equal(GetBytes(converted), GetBytes(direct));
        }
    }

    [Fact]
    public void SequentialReadersDecodeTheFrameAndTrailingMetadata()
    {
        var image = JpegTestImage.CreateRandom(30, 20, Layouts["4:2:0"], seed: 14);
        var expected = image.GetExpectedPixels();
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 2, SegmentsAfterScans = [JpegTestImage.Segment(0xFE, "after the scan"u8)] });
        using (var eager = Image.Load(data))
        {
            Assert.Equal("after the scan", Assert.Single(eager.Metadata.TextEntries).Value);
            Assert.Equal(ImageFormat.Jpeg, eager.Metadata.SourceFormat);
            Assert.Equal(FrameDuration.Zero, eager.Frames[0].Metadata.Duration);
            Assert.False(eager.IsAnimated);
        }

        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data)))
        {
            Assert.Empty(reader.Info.Metadata.TextEntries);
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            AssertPixels(expected, frame, "ReadFrame");
            Assert.Equal("after the scan", Assert.Single(frame.Metadata.TextEntries).Value);
            Assert.Null(reader.ReadFrame());
        }

        using var destination = new Image<Rgb24>(30, 20, new Rgb24(1, 2, 3));
        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data)))
        {
            Assert.True(reader.ReadFrameInto(destination));
            Assert.False(reader.ReadFrameInto(destination));
        }

        AssertPixels(expected, destination, "ReadFrameInto");

    }

    [Fact]
    public async Task CancellationDuringEntropyDecodingFaultsTheOperation()
    {
        var data = JpegTestImage.CreateRandom(160, 160, Layouts["4:2:0"], seed: 15, acDensity: 0.6).Encode();
        var position = data.Length / 2;
        using (var source = new CancellationTokenSource())
        {
            await using var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 256 };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgb24>(stream, cancellationToken: source.Token));
        }

        using (var source = new CancellationTokenSource())
        {
            var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 256 };
            await using var reader = await Image.OpenReaderAsync<Rgb24>(stream, cancellationToken: XunitCancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadFrameAsync(source.Token).AsTask());
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        }
    }

    [Fact]
    public void ResourceLimitsApplyToDecoding()
    {
        var data = JpegTestImage.CreateRandom(64, 48, Layouts["4:2:0"], seed: 16).Encode();
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = (64 * 48) - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxWidth = 63 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxLiveAllocationBytes = 32 * 1024 }))).Kind);
        using var atLimit = Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = 64 * 48, MaxEncodedBytes = data.Length, MaxWidth = 64 }));
        Assert.Equal(64, atLimit.Width);

        static ImageDecodeOptions Options(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Fact]
    public void MetadataIsDecodedButNeverApplied()
    {
        var image = JpegTestImage.CreateRandom(12, 6, Layouts["4:4:4"], seed: 17);
        var exif = new TiffBuilder(bigEndian: true).Ifd0(TiffBuilder.Short(0x0112, 6)).Build();
        var profile = TestRawImage.CreateIccHeader("RGB "u8);
        var data = image.Encode(new JpegTestEncodeOptions
        {
            SegmentsBeforeFrame =
            [
                JpegTestImage.Segment(0xE1, [.. "Exif\0\0"u8, .. exif]),
                JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 2, 2, .. profile.AsSpan(64)]),
                JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile.AsSpan(0, 64)]),
            ],
        });

        // EXIF orientation is metadata only: the stored pixels are returned
        using (var decoded = Image.Load(data))
        {
            Assert.Equal(new Size(12, 6), decoded.Size);
            Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
            Assert.Equal(exif, decoded.Metadata.ExifProfile?.Data.ToArray());
            Assert.Equal(profile, decoded.Metadata.IccProfile?.Data.ToArray());
            AssertPixels(image.GetExpectedPixels(), decoded, "EXIF orientation 6");
        }

        // The RGB profile labels color pixels only: a typed gray load requires an explicit decision
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Gray8>(data));
        using (var gray = Image.Load<Gray8>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { DiscardIncompatibleColorProfile = true } }))
        {
            Assert.Null(gray.Metadata.IccProfile);
            Assert.Equal(ExifOrientation.RightTop, gray.Metadata.Orientation);
        }
    }

    public static TheoryData<string, string> InkCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var layout in InkLayouts.Keys)
            {
                foreach (var coding in new[] { JpegTestInkCoding.Cmyk, JpegTestInkCoding.Ycck, JpegTestInkCoding.CmykWithoutAdobe })
                {
                    data.Add(layout, coding.ToString());
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(InkCases))]
    public void InksAreConvertedFromTheirExactAmounts(string layout, string coding)
    {
        var inkCoding = Enum.Parse<JpegTestInkCoding>(coding);
        var sampling = InkLayouts[layout];
        var reference = ReferenceIccTransform.Create(IccProfile.Cgats001CompatibleCmyk.Data.Span, IccProfile.Srgb.Data.Span, intent: 1, blackPointCompensation: true);
        foreach (var (width, height) in new[] { (1, 1), (9, 7), (17, 9), (23, 18) })
        {
            var image = JpegTestImage.CreateRandom(width, height, sampling, seed: (width * 31) + height + layout.Length + coding.Length, inks: inkCoding);
            var inks = image.GetExpectedInks();
            var encodings = new (string Name, JpegTestEncodeOptions Options)[]
            {
                ("interleaved, fixed tables", new JpegTestEncodeOptions()),
                ("interleaved, long codes, restart 1", new JpegTestEncodeOptions { Huffman = JpegTestHuffmanStyle.Skewed, RestartInterval = 1 }),
                ("extended sequential, one scan per component (reversed), restart 2", new JpegTestEncodeOptions { FrameMarker = 0xC1, Scans = [[3], [2], [1], [0]], TablesBeforeEachScan = true, RestartInterval = 2 }),
                ("black alone then the others interleaved", new JpegTestEncodeOptions { Scans = [[3], [0, 1, 2]], Huffman = JpegTestHuffmanStyle.Skewed }),
            };

            byte[]? first = null;
            foreach (var (name, options) in encodings)
            {
                var data = image.Encode(options);
                Assert.Equal(inkCoding == JpegTestInkCoding.Ycck ? ImageColorModel.Ycck : ImageColorModel.Cmyk, Image.Identify(data).ColorModel);
                using var decoded = Image.Load(data, InkOptions);
                Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
                Assert.Equal(new Size(width, height), decoded.Size);
                Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
                if (first is null)
                {
                    first = GetBytes(decoded);
                    Assert.Empty(reference.Compare(inks, first));
                }
                else
                {
                    AssertPixels(first, decoded, $"{layout} {coding} {width}x{height}, {name}");
                }
            }
        }
    }

    [Fact]
    public void InksUseTheEmbeddedProfileTheDestinationAndTheIntent()
    {
        var profile = IccTestProfiles.CmykLabLut16().Data.ToArray();
        var half = profile.Length / 2;
        var image = JpegTestImage.CreateRandom(19, 13, InkLayouts["chroma-2x2"], seed: 23, inks: JpegTestInkCoding.Ycck);
        var data = image.Encode(new JpegTestEncodeOptions
        {
            SegmentsBeforeFrame =
            [
                JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 2, 2, .. profile.AsSpan(half)]),
                JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 2, .. profile.AsSpan(0, half)]),
            ],
        });

        // Identification reports the profile of the inks; the loaded pixels are labeled with the destination
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.Ycck, info.ColorModel);
        Assert.Equal(IccProfileColorSpace.Cmyk, info.Metadata.IccProfile?.ColorSpace);
        Assert.Equal(profile, info.Metadata.IccProfile?.Data.ToArray());

        var inks = image.GetExpectedInks();
        var destination = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var cases = new (IccRenderingIntent Intent, bool BlackPointCompensation)[]
        {
            (IccRenderingIntent.Perceptual, false),
            (IccRenderingIntent.RelativeColorimetric, true),
            (IccRenderingIntent.Saturation, true),
            (IccRenderingIntent.AbsoluteColorimetric, false),
        };

        var results = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (intent, compensation) in cases)
        {
            var conversion = new CmykConversionOptions { DestinationProfile = destination, Transform = new IccColorTransformOptions { Intent = intent, BlackPointCompensation = compensation } };
            using var decoded = Image.Load(data, new ImageDecodeOptions { CmykConversion = conversion });
            Assert.Equal(destination.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
            var actual = GetBytes(decoded);
            var reference = ReferenceIccTransform.Create(profile, destination.Data.Span, (int)intent, compensation);
            Assert.Empty(reference.Compare(inks, actual));
            results.Add(Convert.ToHexString(actual));
        }

        // The profile has one table per intent and a media white point: every case gives other pixels
        Assert.HasCount(cases.Length, results);
    }

    [Fact]
    public void InksAreOnlyLoadedWithAConversion()
    {
        var image = JpegTestImage.CreateRandom(9, 7, InkLayouts["full"], seed: 29, inks: JpegTestInkCoding.Cmyk);
        var data = image.Encode();
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(ImageColorModel.Cmyk, info.ColorModel);
            Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
            Assert.Equal(8, info.BitsPerComponent);
            Assert.Null(info.Metadata.IccProfile);
        }

        // No pixel format holds inks, and a profile is never applied implicitly
        AssertUnsupported("JPEG CMYK/YCCK", () => Image.Load(data));
        AssertUnsupported("JPEG CMYK/YCCK", () => Image.Load<Rgba32>(data));
        AssertUnsupported("JPEG CMYK/YCCK", () =>
        {
            using var reader = Image.OpenReader<Rgb24>(new MemoryStream(data));
            using var frame = reader.ReadFrame();
        });
    }

    [Fact]
    public void InksWithoutProfileUseTheAssumedProfile()
    {
        var image = JpegTestImage.CreateRandom(14, 10, InkLayouts["chroma-2x1"], seed: 31, inks: JpegTestInkCoding.Cmyk);
        var inks = image.GetExpectedInks();
        var data = image.Encode();

        var assumed = IccTestProfiles.CmykLabLutAToB();
        using (var decoded = Image.Load(data, new ImageDecodeOptions { CmykConversion = new CmykConversionOptions { AssumedSourceProfile = assumed } }))
        {
            var reference = ReferenceIccTransform.Create(assumed.Data.Span, IccProfile.Srgb.Data.Span, intent: 1, blackPointCompensation: true);
            Assert.Empty(reference.Compare(inks, GetBytes(decoded)));
        }

        var withoutAssumption = new ImageDecodeOptions { CmykConversion = new CmykConversionOptions { AssumedSourceProfile = null } };
        AssertUnsupported("JPEG CMYK without color profile", () => Image.Load(data, withoutAssumption));

        // An RGB profile cannot describe inks: it is not adopted, and the image is one without profile
        var rgbProfile = JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. IccProfile.Srgb.Data.Span]);
        var mislabeled = image.Encode(new JpegTestEncodeOptions { SegmentsBeforeFrame = [rgbProfile] });
        Assert.Null(Image.Identify(mislabeled).Metadata.IccProfile);
        AssertUnsupported("JPEG CMYK without color profile", () => Image.Load(mislabeled, withoutAssumption));
        using var untagged = Image.Load(data, InkOptions);
        using var fromMislabeled = Image.Load(mislabeled, InkOptions);
        AssertPixels(GetBytes(untagged), fromMislabeled, "RGB profile on inks");
    }

    [Fact]
    public void AnUnusableEmbeddedInkProfileIsNeverReplacedByTheAssumedOne()
    {
        var image = JpegTestImage.CreateRandom(8, 8, InkLayouts["full"], seed: 37, inks: JpegTestInkCoding.Ycck);

        // A CMYK profile needs a lookup table: without it the profile is malformed
        var withoutTable = IccTestProfiles.BuildBytes("CMYK", "Lab ", [("wtpt", IccTestProfiles.Xyz(0.9642, 1, 0.8249))]);
        var malformed = image.Encode(new JpegTestEncodeOptions { SegmentsBeforeFrame = [JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. withoutTable])] });
        Assert.Equal(IccProfileColorSpace.Cmyk, Image.Identify(malformed).Metadata.IccProfile?.ColorSpace);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(malformed, InkOptions));

        // A device link is a valid profile of a class that is not supported
        var deviceLink = IccTestProfiles.BuildBytes("CMYK", "Lab ", [("wtpt", IccTestProfiles.Xyz(0.9642, 1, 0.8249))], header => "link"u8.CopyTo(header.AsSpan(12)));
        var unsupported = image.Encode(new JpegTestEncodeOptions { SegmentsBeforeFrame = [JpegTestImage.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. deviceLink])] });
        AssertUnsupported("ICC profile class", () => Image.Load(unsupported, InkOptions), ImageFormat.Unknown);
    }

    [Fact]
    public void InksLoadAsTypedPixelsAndThroughReaders()
    {
        var image = JpegTestImage.CreateRandom(21, 12, InkLayouts["chroma-2x2"], seed: 41, inks: JpegTestInkCoding.Ycck);
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 2 });
        using var eager = Image.Load(data, InkOptions);
        var expected = GetBytes(eager);

        using (var rgba = Image.Load<Rgba32>(data, InkOptions))
        {
            var actual = GetBytes(rgba);
            for (var i = 0; i < expected.Length / 3; i++)
            {
                Assert.True(actual.AsSpan(4 * i, 3).SequenceEqual(expected.AsSpan(3 * i, 3)) && actual[(4 * i) + 3] == 255, $"Pixel {i} differs.");
            }
        }

        // The destination profile labels color pixels only, like any RGB profile
        AssertUnsupported("Incompatible color profile", () => Image.Load<Gray8>(data, InkOptions));
        using (var gray = Image.Load<Gray8>(data, new ImageDecodeOptions { CmykConversion = CmykConversionOptions.Default, Conversion = new PixelConversionOptions { DiscardIncompatibleColorProfile = true } }))
        {
            Assert.Null(gray.Metadata.IccProfile);
        }

        var readerOptions = new ImageReaderOptions { CmykConversion = CmykConversionOptions.Default };
        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data), readerOptions))
        {
            Assert.Equal(ImageColorModel.Ycck, reader.Info.ColorModel);
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            AssertPixels(expected, frame, "ReadFrame");
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), frame.Metadata.IccProfile?.Data.ToArray());
            Assert.Null(reader.ReadFrame());
        }

        using var destination = new Image<Rgb24>(21, 12, new Rgb24(1, 2, 3));
        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data), readerOptions))
        {
            Assert.True(reader.ReadFrameInto(destination));
        }

        AssertPixels(expected, destination, "ReadFrameInto");
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task InksLoadThroughEveryInputVariant(InputVariant variant)
    {
        var image = JpegTestImage.CreateRandom(40, 33, InkLayouts["chroma-2x2"], seed: 43, inks: JpegTestInkCoding.Ycck);
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 3 });
        using var expected = Image.Load(data, InkOptions);
        using var decoded = await InputVariants.LoadAsync<Rgb24>(variant, data, ImageFormat.Jpeg, InkOptions, XunitCancellationToken);
        AssertPixels(GetBytes(expected), decoded, variant.ToString());
    }

    private static void AssertUnsupported(string feature, Action action, ImageFormat format = ImageFormat.Jpeg)
    {
        var exception = Assert.Throws<UnsupportedImageFeatureException>(action);
        Assert.Equal(feature, exception.Feature);
        Assert.Equal(format, exception.Format);
    }

    private static byte[] Gray(byte[][] tables, byte[] entropy, int width = 8, byte scanTables = 0x00, int precision = 8)
    {
        var output = new List<byte> { 0xFF, 0xD8 };
        var frame = JpegTestImage.Segment(precision == 8 ? (byte)0xC0 : (byte)0xC1, [(byte)precision, 0, 8, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0]);
        output.AddRange(frame);
        foreach (var table in tables)
        {
            output.AddRange(table);
        }

        output.AddRange(JpegTestImage.Segment(0xDA, [1, 1, scanTables, 0, 63, 0]));
        output.AddRange(entropy);
        output.AddRange([0xFF, 0xD9]);
        return [.. output];
    }

    private static byte[] Dqt() => JpegTestImage.Segment(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]);

    private static byte[] Segment(byte marker, byte[] payload) => JpegTestImage.Segment(marker, payload);

    /// <summary>A DHT table definition from (code length, symbol) pairs sorted by length.</summary>
    private static byte[] Huffman(byte classAndId, params (int Length, byte Symbol)[] codes)
    {
        var counts = new byte[16];
        foreach (var (length, _) in codes)
        {
            counts[length - 1]++;
        }

        return [classAndId, .. counts, .. codes.OrderBy(code => code.Length).Select(code => code.Symbol)];
    }

    private static List<int> FindRestartMarkers(byte[] data)
    {
        var start = FindSegment(data, 0xDA);
        var result = new List<int>();
        for (var i = start; i < data.Length - 1; i++)
        {
            if (data[i] == 0xFF && data[i + 1] is >= 0xD0 and <= 0xD7)
            {
                result.Add(i);
            }
        }

        return result;
    }

    /// <summary>The markers of the segments before the first scan.</summary>
    private static List<byte> GetSegmentMarkers(byte[] data)
    {
        var markers = new List<byte>();
        for (var offset = 2; data[offset + 1] != 0xDA; offset += 2 + ((data[offset + 2] << 8) | data[offset + 3]))
        {
            markers.Add(data[offset + 1]);
        }

        return markers;
    }

    private static int FindSegment(byte[] data, byte marker)
    {
        var offset = 2;
        while (data[offset + 1] != marker)
        {
            offset += 2 + ((data[offset + 2] << 8) | data[offset + 3]);
        }

        return offset;
    }

    private static byte[] Remove(byte[] data, int offset, int count) => [.. data.AsSpan(0, offset), .. data.AsSpan(offset + count)];

    private static byte[] Insert(byte[] data, int offset, byte[] bytes) => [.. data.AsSpan(0, offset), .. bytes, .. data.AsSpan(offset)];

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static void AssertPixels(byte[] expected, Image image, string context)
    {
        var actual = GetBytes(image);
        if (expected.AsSpan().SequenceEqual(actual))
            return;

        var index = expected.AsSpan().CommonPrefixLength(actual);
        var channels = PixelFormats.GetBytesPerPixel(image.PixelFormat);
        var pixel = index / channels;
        Assert.Fail($"{context}: first difference at ({pixel % image.Width}, {pixel / image.Width}) channel {index % channels}: expected {expected[index]}, actual {actual[index]}.");
    }
}
