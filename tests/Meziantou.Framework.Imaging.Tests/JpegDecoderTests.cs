using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
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
public sealed class JpegDecoderTests
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
        var data = image.Encode(new JpegTestEncodeOptions { RestartInterval = 1, Huffman = JpegTestHuffmanStyle.Skewed });
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

    private static byte[] Gray(byte[][] tables, byte[] entropy, int width = 8, byte scanTables = 0x00)
    {
        var output = new List<byte> { 0xFF, 0xD8 };
        var frame = JpegTestImage.Segment(0xC0, [8, 0, 8, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0]);
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
