using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Color;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Lossless JPEG decoding (ITU-T T.81 Annex H, SOF3) on synthetic inputs encoded by the independent test-side encoder
/// (<see cref="JpegLosslessTestImage"/>): every predictor at every precision from 2 to 16 bits, the point transform,
/// restart intervals, interleaved and non-interleaved scans with subsampled components, Huffman tables with long codes,
/// compared exactly with the samples given to the encoder (upsampled and converted by the documented contract); plus a
/// datastream written by hand, the differences that need the modulo 2^16, scan and entropy defects, truncation, input
/// variants, readers and limits. The golden corpus compares the same decoder with libjpeg-turbo and FFmpeg.
/// </summary>
public sealed class JpegLosslessDecoderTests
{
    private static readonly Dictionary<string, (int H, int V)[]> Layouts = new(StringComparer.Ordinal)
    {
        ["gray"] = [(1, 1)],
        ["gray-declared-2x2"] = [(2, 2)],
        ["4:4:4"] = [(1, 1), (1, 1), (1, 1)],
        ["4:2:2"] = [(2, 1), (1, 1), (1, 1)],
        ["4:2:0"] = [(2, 2), (1, 1), (1, 1)],
        ["4:4:0"] = [(1, 2), (1, 1), (1, 1)],
        ["4:1:1"] = [(4, 1), (1, 1), (1, 1)],
        ["luma-upsampled"] = [(1, 1), (2, 2), (2, 2)],
        ["mixed-chroma"] = [(2, 2), (1, 2), (2, 1)],
    };

    public static TheoryData<int, int> PredictorCases
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var precision in new[] { 2, 3, 7, 8, 9, 12, 15, 16 })
            {
                for (var predictor = 1; predictor <= 7; predictor++)
                {
                    data.Add(predictor, precision);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, int, int, int> LayoutCases
    {
        get
        {
            var data = new TheoryData<string, int, int, int>();
            foreach (var layout in Layouts.Keys)
            {
                foreach (var (width, height) in new[] { (1, 1), (2, 3), (9, 7), (17, 9), (37, 21) })
                {
                    foreach (var precision in new[] { 8, 12, 16 })
                    {
                        data.Add(layout, width, height, precision);
                    }
                }
            }

            return data;
        }
    }

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(PredictorCases))]
    public void EveryPredictorDecodesToTheSamplesAtEveryPrecision(int predictor, int precision)
    {
        foreach (var (width, height) in new[] { (1, 1), (1, 6), (7, 1), (13, 9) })
        {
            foreach (var color in new[] { false, true })
            {
                // RGB samples: nothing but the scaling to 16 bits stands between the samples and the pixels
                var image = JpegLosslessTestImage.CreateRandom(width, height, color ? Layouts["4:4:4"] : Layouts["gray"], precision, seed: (predictor * 100) + precision + width, rgb: color);
                var expected = image.GetExpectedPixels();
                var encodings = new (string Name, JpegLosslessTestOptions Options)[]
                {
                    ("fixed tables", new JpegLosslessTestOptions { Predictor = predictor }),
                    ("long codes, restart every line", new JpegLosslessTestOptions { Predictor = predictor, Huffman = JpegTestHuffmanStyle.Skewed, RestartRows = 1 }),
                    ("restart every 3 lines, a table per component", new JpegLosslessTestOptions { Predictor = predictor, Huffman = JpegTestHuffmanStyle.Skewed, HuffmanSeed = 9, RestartRows = 3, TablePerComponent = true }),
                };

                foreach (var (name, options) in encodings)
                {
                    var data = image.Encode(options);
                    var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
                    Assert.Equal(ImageFormat.Jpeg, info.Format);
                    Assert.Equal(precision, info.BitsPerComponent);
                    Assert.Equal(image.PixelFormat, info.PixelFormat);
                    Assert.Equal(color ? ImageColorModel.Rgb : ImageColorModel.Grayscale, info.ColorModel);
                    using var decoded = Image.Load(data);
                    Assert.Equal(image.PixelFormat, decoded.PixelFormat);
                    Assert.Equal(new Size(width, height), decoded.Size);
                    AssertPixels(expected, decoded, $"predictor {predictor}, {precision} bits, {width}x{height}, {(color ? "RGB" : "gray")}, {name}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void EveryLayoutDecodesToTheContract(string layout, int width, int height, int precision)
    {
        // The planes are the samples given to the encoder; subsampled components are upsampled and three components are
        // converted from YCbCr like the ones of the other frames. The samples that complete the MCUs are random: a decoder
        // predicts from them and removes them
        var sampling = Layouts[layout];
        var image = JpegLosslessTestImage.CreateRandom(width, height, sampling, precision, seed: (width * 31) + height + layout.Length + precision);
        var expected = image.GetExpectedPixels();
        var all = Enumerable.Range(0, sampling.Length).ToArray();
        var encodings = new (string Name, JpegLosslessTestOptions Options)[]
        {
            ("interleaved, predictor 1", new JpegLosslessTestOptions { PaddingSeed = 1 }),
            ("interleaved, predictor 4, long codes, restart every MCU row", new JpegLosslessTestOptions { Predictor = 4, Huffman = JpegTestHuffmanStyle.Skewed, RestartRows = 1, PaddingSeed = 2 }),
            ("interleaved, predictor 7, restart every 2 MCU rows", new JpegLosslessTestOptions { Predictor = 7, RestartRows = 2, PaddingSeed = 3 }),
            ("one scan per component (reversed), a predictor each", new JpegLosslessTestOptions { Scans = [.. all.Reverse().Select(index => new JpegLosslessTestScan([index], 2 + index))], RestartRows = 2 }),
            ("first component alone then the others interleaved", new JpegLosslessTestOptions { Scans = sampling.Length == 3 ? [new([0], 5), new([1, 2], 6)] : [new([0], 5)], Huffman = JpegTestHuffmanStyle.Skewed, PaddingSeed = 4 }),
        };

        foreach (var (name, options) in encodings)
        {
            using var decoded = Image.Load(image.Encode(options));
            Assert.Equal(image.PixelFormat, decoded.PixelFormat);
            AssertPixels(expected, decoded, $"{layout} {width}x{height}, {precision} bits, {name}");
        }
    }

    [Fact]
    public void ADatastreamWrittenByHandDecodesToItsSamples()
    {
        // 3x2 samples of 8 bits, predictor 7. The categories 0, 1 and 2 have the codes 00, 01 and 10, the category 5 the
        // code 110. First line: 2^7 predicts 100 (difference -28: category 5, the low 5 bits of -29), then the sample on
        // the left predicts 101 (+1) and 99 (-2: the low 2 bits of -3). Second line: the sample above predicts 98 (-2), then
        // (Ra + Rb) / 2 = (98 + 101) / 2 = 99 predicts 100 (+1), and (100 + 99) / 2 = 99 predicts 102 (+3)
        var entropy = JpegTestImage.PackBits(
        [
            (0b110, 3), (0b00011, 5),
            (0b01, 2), (0b1, 1),
            (0b10, 2), (0b01, 2),
            (0b10, 2), (0b01, 2),
            (0b01, 2), (0b1, 1),
            (0b10, 2), (0b11, 2),
        ]);
        byte[] data =
        [
            0xFF, 0xD8,
            .. JpegTestImage.Segment(0xC3, [8, 0, 2, 0, 3, 1, 1, 0x11, 0]),
            .. JpegTestImage.Segment(0xC4, [0x00, 0, 3, 1, .. new byte[13], 0, 1, 2, 5]),
            .. JpegTestImage.Segment(0xDA, [1, 1, 0x00, 7, 0, 0]),
            .. entropy,
            0xFF, 0xD9,
        ];

        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        Assert.Equal(new Size(3, 2), decoded.Size);
        Assert.Equal(new byte[] { 100, 101, 99, 98, 100, 102 }, GetBytes(decoded));
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 7)]
    [InlineData(12, 4)]
    [InlineData(16, 1)]
    [InlineData(16, 15)]
    [InlineData(5, 2)]
    public void ThePointTransformIsUndone(int precision, int pointTransform)
    {
        // The samples are coded divided by 2^Pt and the first prediction is 2^(P - Pt - 1): the output is multiplied back
        foreach (var predictor in new[] { 1, 4, 5, 6, 7 })
        {
            var image = JpegLosslessTestImage.CreateRandom(11, 8, Layouts["4:4:4"], precision, seed: precision + pointTransform + predictor, rgb: true, pointTransform: pointTransform);
            using var decoded = Image.Load(image.Encode(new JpegLosslessTestOptions { Predictor = predictor, PointTransform = pointTransform, RestartRows = 3 }));
            AssertPixels(image.GetExpectedPixels(), decoded, $"{precision} bits, point transform {pointTransform}, predictor {predictor}");
        }

        // Each scan has its own point transform
        var mixed = JpegLosslessTestImage.FromSamples(6, 5, Layouts["4:4:4"], precision, rgb: true, (component, x, y) => component == 1 ? (((x * 3) + y) % (1 << (precision - pointTransform))) << pointTransform : ((x + (y * 2) + component) % (1 << precision)));
        using var separate = Image.Load(mixed.Encode(new JpegLosslessTestOptions { Scans = [new([0], 1), new([1], 4, pointTransform), new([2], 7)] }));
        AssertPixels(mixed.GetExpectedPixels(), separate, $"{precision} bits, point transform {pointTransform} for one component");
    }

    [Fact]
    public void DifferencesAreAddedModuloSixteenBits()
    {
        // The first sample is 0: the difference from the prediction 32,768 is the one value of the category 16, which has
        // no additional bit. From 65,535 to 0 the difference is 1, modulo 2^16, and from 0 to 65,535 it is -1
        ushort[] samples = [0, 65535, 0, 32768, 0, 65535, 65535, 1, 32769, 1, 0, 32768];
        var image = JpegLosslessTestImage.FromSamples(4, 3, Layouts["gray"], 16, rgb: false, (_, x, y) => samples[(y * 4) + x]);
        for (var predictor = 1; predictor <= 7; predictor++)
        {
            var data = image.Encode(new JpegLosslessTestOptions { Predictor = predictor, Huffman = JpegTestHuffmanStyle.Skewed });
            Assert.True(image.WroteLargestDifference);
            using var decoded = Image.Load(data);
            Assert.Equal(PixelFormat.Gray16, decoded.PixelFormat);
            Assert.Equal(samples, unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(decoded).AsSpan())).ToArray());
        }
    }

    [Fact]
    public void SamplesAreScaledToTheWholeSixteenBitRange()
    {
        // Every sample of every precision: (s * 65535 + max / 2) / max, so that 0 and the largest sample are black and white
        foreach (var precision in new[] { 2, 5, 9, 12, 15 })
        {
            var max = (1 << precision) - 1;
            var width = Math.Min(max + 1, 256);
            var height = (max + 1) / width;
            var image = JpegLosslessTestImage.FromSamples(width, height, Layouts["gray"], precision, rgb: false, (_, x, y) => (y * width) + x);
            using var decoded = Image.Load<Gray16>(image.Encode(new JpegLosslessTestOptions { Predictor = 2 }));
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(decoded).AsSpan()));
            for (var sample = 0; sample <= max; sample++)
            {
                Assert.Equal((int)((((long)sample * 65535) + (max / 2)) / max), (int)pixels[sample]);
            }

            Assert.Equal(0, (int)pixels[0]);
            Assert.Equal(ushort.MaxValue, pixels[max]);
            Assert.HasCount(max + 1, pixels.ToArray().ToHashSet());
        }
    }

    [Fact]
    public void ScanParametersAreValidated()
    {
        var image = JpegLosslessTestImage.CreateRandom(9, 6, Layouts["4:4:4"], 8, seed: 3, rgb: true);
        var fullScan = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan };

        // The predictor 0 is for the differential frames of the hierarchical mode; there is no predictor above 7
        foreach (var predictor in new[] { 0, 8, 255 })
        {
            var data = image.Encode(new JpegLosslessTestOptions { Predictor = predictor });
            Assert.Contains("predictor", Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Message, StringComparison.Ordinal);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, fullScan));

            // The frame header is valid: only the scans are not
            Assert.Equal(8, Image.Identify(data).BitsPerComponent);
        }

        // The point transform leaves at least one bit of the samples
        var shifted = JpegLosslessTestImage.FromSamples(4, 4, Layouts["gray"], 8, rgb: false, static (_, _, _) => 0);
        using (var oneBit = Image.Load(shifted.Encode(new JpegLosslessTestOptions { PointTransform = 7 })))
        {
            Assert.Equal(new byte[16], GetBytes(oneBit));
        }

        foreach (var pointTransform in new[] { 8, 15 })
        {
            var data = shifted.Encode(new JpegLosslessTestOptions { PointTransform = pointTransform });
            Assert.Contains("point transform", Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Message, StringComparison.Ordinal);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, fullScan));
        }

        // Se and Ah have no meaning in a lossless scan: they are ignored, like the parameters of a sequential scan
        using (var ignored = Image.Load(image.Encode(new JpegLosslessTestOptions { Predictor = 3, UnusedScanParameters = (63, 5) })))
        {
            AssertPixels(image.GetExpectedPixels(), ignored, "Se and Ah");
        }

        // A restart interval is a whole number of MCU rows (T.81 H.1.1)
        var partialRow = image.Encode(new JpegLosslessTestOptions { RestartInterval = 4 });
        Assert.Contains("whole number of MCU rows", Assert.Throws<InvalidImageContentException>(() => Image.Load(partialRow)).Message, StringComparison.Ordinal);
        using (var wholeRows = Image.Load(image.Encode(new JpegLosslessTestOptions { RestartInterval = 18 })))
        {
            AssertPixels(image.GetExpectedPixels(), wholeRows, "restart interval of two lines");
        }

        // A component is coded by one scan, and every component is coded
        var twice = image.Encode(new JpegLosslessTestOptions { Scans = [new([0, 1, 2], 1), new([1], 1)] });
        Assert.Contains("several lossless scans", Assert.Throws<InvalidImageContentException>(() => Image.Load(twice)).Message, StringComparison.Ordinal);
        var missing = image.Encode(new JpegLosslessTestOptions { Scans = [new([0], 1), new([2], 1)] });
        Assert.Contains("no scan for the component", Assert.Throws<InvalidImageContentException>(() => Image.Load(missing)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FramePrecisionsAndCodingsAreValidated()
    {
        var image = JpegLosslessTestImage.CreateRandom(5, 4, Layouts["gray"], 8, seed: 1);
        var data = image.Encode();
        var frame = FindSegment(data, 0xC3);
        foreach (var precision in new[] { 0, 1, 17, 255 })
        {
            var invalid = data.ToArray();
            invalid[frame + 4] = (byte)precision;
            Assert.Contains("between 2 and 16", Assert.Throws<InvalidImageContentException>(() => Image.Identify(invalid)).Message, StringComparison.Ordinal);
            Assert.Throws<InvalidImageContentException>(() => Image.Load(invalid));
        }

        // The lossless process with arithmetic coding (SOF11) is recognized and not decoded
        var arithmetic = image.Encode(new JpegLosslessTestOptions { FrameMarker = 0xCB });
        foreach (var action in new Action[] { () => Image.Identify(arithmetic), () => Image.Load(arithmetic) })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(action);
            Assert.Equal("JPEG lossless with arithmetic coding", exception.Feature);
            Assert.Equal(ImageFormat.Jpeg, exception.Format);
        }

        // A lossless frame uses no quantization table: a file without one is decoded, and its selectors are not looked up
        Assert.DoesNotContain((byte)0xDB, GetSegmentMarkers(data));
        using var decoded = Image.Load(data);
        AssertPixels(image.GetExpectedPixels(), decoded, "no quantization table");
    }

    [Fact]
    public void EntropyDefectsAreInvalidContent()
    {
        // A category above 16: the table of a DCT frame may hold one, a lossless scan cannot code it
        byte[] Stream(byte[] table, byte[] entropy, int precision = 8, int width = 2) =>
        [
            0xFF, 0xD8,
            .. JpegTestImage.Segment(0xC3, [(byte)precision, 0, 1, 0, (byte)width, 1, 1, 0x11, 0]),
            .. JpegTestImage.Segment(0xC4, table),
            .. JpegTestImage.Segment(0xDA, [1, 1, 0x00, 1, 0, 0]),
            .. entropy,
            0xFF, 0xD9,
        ];

        // Two codes of 1 bit: 0 for the first symbol, 1 for the second one
        static byte[] Table(byte first, byte second) => [0x00, 2, .. new byte[15], first, second];

        var category17 = Stream(Table(0, 17), JpegTestImage.PackBits([(0, 1), (1, 1)]));
        Assert.Contains("category is greater than 16", Assert.Throws<InvalidImageContentException>(() => Image.Load(category17)).Message, StringComparison.Ordinal);

        // A sample that does not fit in the precision of the frame: 128 + 127 + 127 is 382 for 8-bit samples
        var overflow = Stream(Table(0, 7), JpegTestImage.PackBits([(1, 1), (127, 7), (1, 1), (127, 7)]));
        Assert.Contains("does not fit in 8 bits", Assert.Throws<InvalidImageContentException>(() => Image.Load(overflow)).Message, StringComparison.Ordinal);

        // The same differences are samples of a deeper frame: 2048 + 127 + 127
        using (var deep = Image.Load<Gray16>(Stream(Table(0, 7), JpegTestImage.PackBits([(1, 1), (127, 7), (1, 1), (127, 7)]), precision: 12)))
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(deep).AsSpan()));
            Assert.Equal(new[] { (2175 * 65535 + 2047) / 4095, (2302 * 65535 + 2047) / 4095 }, pixels.ToArray().Select(value => (int)value).ToArray());
        }

        // And a sample below zero wraps to a large one: 128 - 127 - 127 is 65,410 modulo 2^16
        var underflow = Stream(Table(0, 7), JpegTestImage.PackBits([(1, 1), (0, 7), (1, 1), (0, 7)]));
        Assert.Contains("does not fit in 8 bits", Assert.Throws<InvalidImageContentException>(() => Image.Load(underflow)).Message, StringComparison.Ordinal);

        // The table the scan selects must be defined, and its codes must be valid
        var image = JpegLosslessTestImage.CreateRandom(9, 6, Layouts["4:4:4"], 8, seed: 5, rgb: true);
        var data = image.Encode();
        var undefined = data.ToArray();
        undefined[FindSegment(data, 0xDA) + 6] = 0x30;
        Assert.Contains("undefined DC Huffman table 3", Assert.Throws<InvalidImageContentException>(() => Image.Load(undefined)).Message, StringComparison.Ordinal);
        var selector = data.ToArray();
        selector[FindSegment(data, 0xDA) + 6] = 0x40;
        Assert.Contains("identifier greater than 3", Assert.Throws<InvalidImageContentException>(() => Image.Load(selector)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartMarkersAreValidated()
    {
        var image = JpegLosslessTestImage.CreateRandom(12, 7, Layouts["4:2:0"], 8, seed: 9);
        var expected = image.GetExpectedPixels();
        var data = image.Encode(new JpegLosslessTestOptions { Predictor = 4, RestartRows = 1 });
        var restarts = FindRestartMarkers(data);
        Assert.HasCount(3, restarts); // 4 MCU rows

        using (var decoded = Image.Load(data))
        {
            AssertPixels(expected, decoded, "restart every MCU row");
        }

        var renumbered = data.ToArray();
        renumbered[restarts[1] + 1] = 0xD4;
        Assert.Contains("out of sequence", Assert.Throws<InvalidImageContentException>(() => Image.Load(renumbered)).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Remove(data, restarts[0], 2)));
        Assert.Contains("restart marker is missing", Assert.Throws<InvalidImageContentException>(() => Image.Load(Remove(data, restarts[2], 2))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedInputsAreInvalidAtEveryLength()
    {
        var image = JpegLosslessTestImage.CreateRandom(11, 9, Layouts["4:2:0"], 12, seed: 4);
        var data = image.Encode(new JpegLosslessTestOptions { Predictor = 6, RestartRows = 2, Huffman = JpegTestHuffmanStyle.Skewed });
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
        var image = JpegLosslessTestImage.CreateRandom(16, 8, Layouts["4:2:2"], 8, seed: 6);
        var data = image.Encode(new JpegLosslessTestOptions { Predictor = 5, RestartRows = 1, Huffman = JpegTestHuffmanStyle.Skewed });
        for (var position = 2; position < data.Length; position++)
        {
            foreach (var mask in new byte[] { 0x01, 0x10, 0x80, 0xFF })
            {
                var corrupt = data.ToArray();
                corrupt[position] ^= mask;
                try
                {
                    using var decoded = Image.Load(corrupt);
                    Assert.Equal(8, decoded.Height);
                }
                catch (ImageException)
                {
                    // Malformed, unsupported or over a limit: explicit
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task LargeImagesStreamThroughEveryInputVariant(InputVariant variant)
    {
        // About 100 KB of entropy-coded data with stuffing, long codes and restart markers: fed piece by piece
        var image = JpegLosslessTestImage.CreateRandom(181, 133, Layouts["4:2:0"], 8, seed: 8);
        var expected = image.GetExpectedPixels();
        var data = image.Encode(new JpegLosslessTestOptions { Predictor = 4, RestartRows = 7, Huffman = JpegTestHuffmanStyle.Skewed, PaddingSeed = 5 });
        using (var decoded = await InputVariants.LoadAsync<Rgb24>(variant, data, ImageFormat.Jpeg, options: null, XunitCancellationToken))
        {
            AssertPixels(expected, decoded, variant.ToString());
        }

        var separate = image.Encode(new JpegLosslessTestOptions { Scans = [new([2], 7), new([0], 1), new([1], 6)] });
        using (var decoded = await InputVariants.LoadAsync<Rgb24>(variant, separate, ImageFormat.Jpeg, options: null, XunitCancellationToken))
        {
            AssertPixels(expected, decoded, variant + " (separate scans)");
        }

        var deep = JpegLosslessTestImage.CreateRandom(150, 120, Layouts["gray"], 16, seed: 18);
        using (var decoded = await InputVariants.LoadAsync<Gray16>(variant, deep.Encode(new JpegLosslessTestOptions { Predictor = 7 }), ImageFormat.Jpeg, options: null, XunitCancellationToken))
        {
            AssertPixels(deep.GetExpectedPixels(), decoded, variant + " (16 bits)");
        }
    }

    [Fact]
    public void DecoderStateIsBoundedAndReleased()
    {
        // An interleaved frame is decoded in a ring of three MCU rows, which are three lines here; a frame coded in several
        // scans keeps its planes
        var image = JpegLosslessTestImage.FromSamples(2048, 256, Layouts["4:4:4"], 8, rgb: true, static (component, x, y) => (x + y + component) & 0xFF);
        using (var decoded = Image.Load<Rgb24>(image.Encode()))
        {
            var scope = decoded.Owner.Scope;
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
            Assert.True(state < 256 * 1024, $"Peak decoder state {state} bytes: three lines and two lines of differences per component and the entropy buffer are expected.");
        }

        using (var decoded = Image.Load<Rgb24>(image.Encode(new JpegLosslessTestOptions { Scans = [new([0], 1), new([1], 1), new([2], 1)] })))
        {
            var scope = decoded.Owner.Scope;
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
            Assert.True(state >= 2048 * 256 * 3, $"Peak decoder state {state} bytes: the full component planes are kept until the last scan.");
        }
    }

    [Fact]
    public void TypedLoadsAndReadersDecodeTheFrame()
    {
        var image = JpegLosslessTestImage.CreateRandom(19, 13, Layouts["4:2:2"], 12, seed: 12);
        var data = image.Encode(new JpegLosslessTestOptions { Predictor = 3, RestartRows = 4, SegmentsBeforeFrame = [JpegTestImage.Segment(0xFE, "lossless"u8)] });
        var expected = image.GetExpectedPixels();
        using var deep = Image.Load<Rgba64>(data);
        AssertPixels(expected, deep, "Rgba64");
        Assert.Equal("lossless", Assert.Single(deep.Metadata.TextEntries).Value);
        Assert.Equal(ImageFormat.Jpeg, deep.Metadata.SourceFormat);
        AssertSameAsConverted<Rgb24>(data, deep);
        AssertSameAsConverted<Rgba32>(data, deep);
        AssertSameAsConverted<Gray16>(data, deep);

        using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(data)))
        {
            Assert.Equal(12, reader.Info.BitsPerComponent);
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            AssertPixels(expected, frame, "ReadFrame");
            Assert.Null(reader.ReadFrame());
        }

        using var destination = new Image<Rgba64>(19, 13);
        using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(data)))
        {
            Assert.True(reader.ReadFrameInto(destination));
            Assert.False(reader.ReadFrameInto(destination));
        }

        AssertPixels(expected, destination, "ReadFrameInto");

        static void AssertSameAsConverted<TPixel>(byte[] data, Image source)
            where TPixel : unmanaged
        {
            using var direct = Image.Load<TPixel>(data);
            using var converted = source.CloneAs<TPixel>();
            Assert.Equal(GetBytes(converted), GetBytes(direct));
        }
    }

    [Fact]
    public void ResourceLimitsApplyToDecoding()
    {
        var data = JpegLosslessTestImage.CreateRandom(64, 48, Layouts["4:2:0"], 16, seed: 16).Encode();
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = (64 * 48) - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxWidth = 63 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxLiveAllocationBytes = 16 * 1024 }))).Kind);
        using var atLimit = Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = 64 * 48, MaxEncodedBytes = data.Length, MaxWidth = 64 }));
        Assert.Equal(64, atLimit.Width);

        static ImageDecodeOptions Options(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void InksAreOnlyLoadedWithAConversionOfTheirExactAmounts(int precision)
    {
        // Four components are inks, stored as complements like the ones of the other frames: the amounts are scaled to 16
        // bits when they do not have 8, then converted. The expected colors are those of the decimal reference
        var image = JpegLosslessTestImage.CreateRandom(13, 9, [(1, 1), (1, 1), (1, 1), (1, 1)], precision, seed: 21 + precision);
        var data = image.Encode(new JpegLosslessTestOptions { Predictor = 4, RestartRows = 2 });
        Assert.Equal(ImageColorModel.Cmyk, Image.Identify(data).ColorModel);
        Assert.Equal("JPEG CMYK/YCCK", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);

        var reference = ReferenceIccTransform.Create(IccProfile.Cgats001CompatibleCmyk.Data.Span, IccProfile.Srgb.Data.Span, intent: 1, blackPointCompensation: true);
        var stored = image.GetStoredSamples();
        using var decoded = Image.Load(data, new ImageDecodeOptions { CmykConversion = CmykConversionOptions.Default });
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        if (precision == 8)
        {
            Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
            Assert.Empty(reference.Compare([.. stored.Select(sample => (byte)(255 - sample))], GetBytes(decoded)));
            return;
        }

        Assert.Equal(PixelFormat.Rgba64, decoded.PixelFormat);
        var pixels = unsafe(MemoryMarshal.Cast<byte, ushort>(GetBytes(decoded).AsSpan()));
        var colors = new ushort[image.Width * image.Height * 3];
        for (var pixel = 0; pixel < image.Width * image.Height; pixel++)
        {
            pixels.Slice(4 * pixel, 3).CopyTo(colors.AsSpan(3 * pixel));
            Assert.Equal(ushort.MaxValue, pixels[(4 * pixel) + 3]);
        }

        Assert.Empty(reference.Compare([.. stored.Select(sample => image.ScaleToSixteenBits(image.MaxSample - sample))], colors));
    }

    [Fact]
    public void ReconstructionKernelsFollowTheSpecification()
    {
        // The predictors of table H.1 on reconstructed samples, with the arithmetic shifts of the predictors 5 and 6 on
        // negative values and sums that need 17 bits
        ushort[] above = [10, 65535, 3, 40000, 7];
        ushort[] differences = [5, 1, 65535, 2, 3];
        (int Predictor, ushort[] Expected)[] cases =
        [
            (1, [15, 16, 15, 17, 20]),
            (2, [15, 0, 2, 40002, 10]),
            (3, [15, 11, 65534, 5, 40003]),

            // Ra + Rb - Rc: 15 + 65535 - 10 + 1 is 5 modulo 2^16, then 5 + 3 - 65535 - 1, 8 + 40000 - 3 + 2 and 40007 + 7 - 40000 + 3
            (4, [15, 5, 8, 40007, 17]),

            // Ra + ((Rb - Rc) >> 1): 15 + 32762 + 1, then (3 - 65535) >> 1 is -32766, (40000 - 3) >> 1 is 19998 and
            // (7 - 40000) >> 1 is -19997 (rounded down)
            (5, [15, 32778, 11, 20011, 17]),

            // Rb + ((Ra - Rc) >> 1): 65535 + 2 + 1, then (2 - 65535) >> 1 is -32767 (rounded down), (32771 - 3) >> 1 is
            // 16384 and (56386 - 40000) >> 1 is 8193
            (6, [15, 2, 32771, 56386, 8203]),

            // (Ra + Rb) >> 1 on sums of 17 bits: 32775 + 1, then 16389 - 1, 28194 + 2 and 14101 + 3
            (7, [15, 32776, 16388, 28196, 14104]),
        ];

        foreach (var (predictor, expected) in cases)
        {
            var samples = differences.ToArray();
            JpegLosslessScanDecoder.Reconstruct(samples, above, predictor);
            Assert.Equal(expected, samples);
        }

        // The first line: the initial prediction, then the sample on the left
        ushort[] first = [65535, 2, 65534];
        JpegLosslessScanDecoder.ReconstructFirstLine(first, 128);
        Assert.Equal(new ushort[] { 127, 129, 127 }, first);
        Assert.Throws<ArgumentOutOfRangeException>(() => JpegLosslessScanDecoder.Reconstruct(new ushort[2], new ushort[2], 0));
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
