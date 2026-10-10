using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Progressive Huffman JPEG decoding on synthetic inputs encoded by the independent test-side progressive encoder
/// of <see cref="JpegTestImage"/> (written from ITU-T T.81 G.1.2): DC first and refinement scans, AC first scans with EOB runs,
/// AC refinements with correction bits, spectral selection, successive approximation down from bit 13, interleaved and
/// non-interleaved DC scans, restart intervals, long Huffman codes, every sampling layout and odd sizes. The decoded pixels
/// are compared exactly with the pixels computed from the specifications (direct 2-D inverse DCT of the complete or
/// partially sent coefficients and the documented upsampling/color contract), never with the library's sequential path.
/// Plus scan-parameter, progression and entropy defects, truncation at every length (exact partial images at scan
/// boundaries), corruption, the bounded scan work, coefficient-state accounting and release on faults, cancellation, input
/// variants, typed loads, readers and metadata. The golden corpus compares the same decoder with libjpeg-turbo.
/// </summary>
public sealed class JpegProgressiveDecoderTests
{
    private const string ColorStandard = "0,1,2: 0 0 0 1; 0: 1 5 0 2; 2: 1 63 0 1; 1: 1 63 0 1; 0: 6 63 0 2; 0: 1 63 2 1; 0,1,2: 0 0 1 0; 2: 1 63 1 0; 1: 1 63 1 0; 0: 1 63 1 0";
    private const string ColorSpectral = "0,1,2: 0 0 0 0; 0: 1 2 0 0; 0: 3 63 0 0; 2: 1 63 0 0; 1: 1 20 0 0; 1: 21 63 0 0";
    private const string ColorDeep =
        "0: 0 0 0 3; 1: 0 0 0 2; 2: 0 0 0 0; 0: 1 63 0 5; 1: 1 63 0 1; 2: 1 9 0 0; 2: 10 63 0 2; 0: 0 0 3 2; 0: 0 0 2 1; 1: 0 0 2 1; 0,1: 0 0 1 0; " +
        "0: 1 63 5 4; 0: 1 63 4 3; 0: 1 63 3 2; 0: 1 63 2 1; 0: 1 63 1 0; 1: 1 63 1 0; 2: 10 63 2 1; 2: 10 63 1 0";

    private const string GrayStandard = "0: 0 0 0 1; 0: 1 5 0 2; 0: 6 63 0 2; 0: 1 63 2 1; 0: 0 0 1 0; 0: 1 63 1 0";
    private const string GraySpectral = "0: 0 0 0 0; 0: 1 1 0 0; 0: 2 9 0 0; 0: 10 63 0 0";
    private const string GrayDeep = "0: 0 0 0 4; 0: 1 63 0 5; 0: 0 0 4 3; 0: 1 63 5 4; 0: 0 0 3 2; 0: 1 63 4 3; 0: 0 0 2 1; 0: 1 63 3 2; 0: 0 0 1 0; 0: 1 63 2 1; 0: 1 63 1 0";

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
        var gray = sampling.Length == 1;
        var encodings = new (string Name, JpegTestEncodeOptions Options)[]
        {
            ("standard progression, fixed tables", Progressive(gray ? GrayStandard : ColorStandard)),
            ("spectral selection only, long codes, restart 1", Progressive(gray ? GraySpectral : ColorSpectral, restartInterval: 1, huffman: JpegTestHuffmanStyle.Skewed)),
            ("successive approximation from bit 5, separate DC scans, tables before each scan, EOB runs of at most 3, restart 3", Progressive(gray ? GrayDeep : ColorDeep, restartInterval: 3, huffman: JpegTestHuffmanStyle.Skewed, maxEndOfBandRun: 3, tablesBeforeEachScan: true)),
            ("standard progression without EOB runs, restart 2", Progressive(gray ? GrayStandard : ColorStandard, restartInterval: 2, maxEndOfBandRun: 1)),
        };

        foreach (var (name, options) in encodings)
        {
            var data = image.Encode(options);
            Assert.Equal(0xC2, data[FindSegment(data, 0xC2) + 1]);
            using var decoded = Image.Load(data);
            Assert.Equal(image.PixelFormat, decoded.PixelFormat);
            AssertPixels(expected, decoded, $"{layout} {width}x{height}, {name}");
        }
    }

    [Theory]
    [InlineData("4:2:0", "0,1,2: 0 0 0 2; 0: 1 5 0 1; 1: 1 63 0 0")]
    [InlineData("4:2:2", "0: 0 0 0 0; 1: 0 0 0 3; 2: 0 0 0 1; 0: 1 63 0 2; 1: 1 9 0 0; 1: 0 0 3 2; 0: 1 63 2 1")]
    [InlineData("gray", "0: 0 0 0 1; 0: 1 9 0 3; 0: 10 63 0 0; 0: 1 9 3 2")]
    public void PartialProgressionsKeepOnlyTheSentBits(string layout, string script)
    {
        // Coefficient bits that no scan sends are zero (T.81 G.1.2); a file may legitimately stop before the last refinement
        var image = JpegTestImage.CreateRandom(37, 21, Layouts[layout], seed: 3);
        var scans = JpegTestProgressiveScan.ParseScript(script);
        var expected = Approximate(image, scans).GetExpectedPixels();
        using var decoded = Image.Load(image.Encode(new JpegTestEncodeOptions { Progression = scans, RestartInterval = 2 }));
        AssertPixels(expected, decoded, script);
    }

    [Fact]
    public void QuantizationTablesAreLatchedAtTheFirstScanOfEachComponent()
    {
        // A DQT redefining both tables after the first scan does not change components that were already coded
        var image = JpegTestImage.CreateRandom(24, 16, Layouts["4:2:0"], seed: 18);
        var data = image.Encode(Progressive(ColorStandard));
        var second = FindSegments(data, 0xDA)[1];
        byte[] redefined = [.. JpegTestImage.Segment(0xDB, [0x00, .. Enumerable.Repeat((byte)7, 64)]), .. JpegTestImage.Segment(0xDB, [0x01, .. Enumerable.Repeat((byte)9, 64)])];
        using var decoded = Image.Load(Insert(data, second, redefined));
        AssertPixels(image.GetExpectedPixels(), decoded, "DQT after the first scan");
    }

    [Fact]
    public void TheLongestValidProgressionDecodesAndBoundsTheScanWork()
    {
        // Every coefficient coded from bit 13 down to bit 0, one coefficient per scan: 64 x 14 = 896 scans, the most a component
        // can have since every scan must advance the state of each coefficient of its band
        var image = JpegTestImage.CreateRandom(17, 9, Layouts["gray"], seed: 19);
        var scans = new List<JpegTestProgressiveScan>();
        for (var bit = 13; bit >= 0; bit--)
        {
            for (var k = 0; k < 64; k++)
            {
                scans.Add(new JpegTestProgressiveScan([0], k, k, bit == 13 ? 0 : bit + 1, bit));
            }
        }

        var data = image.Encode(new JpegTestEncodeOptions { Progression = scans, Huffman = JpegTestHuffmanStyle.Skewed });
        using (var decoded = Image.Load(data))
        {
            AssertPixels(image.GetExpectedPixels(), decoded, "896 scans");
        }

        Assert.Equal(17, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Width);
        foreach (var extra in new[] { "0: 0 0 0 0", "0: 0 0 1 0", "0: 5 5 0 3", "0: 63 63 1 0" })
        {
            var more = image.Encode(new JpegTestEncodeOptions { Progression = [.. scans, JpegTestProgressiveScan.Parse(extra)] });
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(more, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
            Assert.Throws<InvalidImageContentException>(() => Image.Load(more));
        }
    }

    public static TheoryData<string, string, string> ScanParameterDefects => new()
    {
        { "Ss greater than Se", "0,1,2: 0 0 0 0; 0: 5 3 0 0", "invalid spectral selection (Ss = 5, Se = 3)" },
        { "Se greater than 63", "0,1,2: 0 0 0 0; 0: 1 64 0 0", "invalid spectral selection (Ss = 1, Se = 64)" },
        { "DC and AC together", "0,1,2: 0 5 0 0", "codes the DC and AC coefficients together" },
        { "AC scan of two components", "0,1,2: 0 0 0 0; 0,1: 1 63 0 0", "AC scan codes several components" },
        { "Al above 13", "0,1,2: 0 0 0 14", "greater than 13 (Ah = 0, Al = 14)" },
        { "Ah above 13", "0,1,2: 0 0 0 13; 0,1,2: 0 0 14 13", "greater than 13 (Ah = 14, Al = 13)" },
        { "refinement of two bits", "0,1,2: 0 0 0 2; 0,1,2: 0 0 2 0", "must refine one bit (Ah = 2, Al = 0)" },
        { "DC refinement before the first scan", "0,1,2: 0 0 1 0", "coefficient 0 of the component 1 precedes its first scan" },
        { "DC refinement of another bit position", "0,1,2: 0 0 0 2; 0,1,2: 0 0 1 0", "expects Al = 1 from the previous scan, which had Al = 2" },
        { "DC first scan repeated", "0,1,2: 0 0 0 0; 1: 0 0 0 0", "coefficient 0 of the JPEG component 2 is coded by several first scans" },
        { "overlapping AC first scans", "0,1,2: 0 0 0 0; 0: 1 10 0 0; 0: 5 20 0 0", "coefficient 5 of the JPEG component 1 is coded by several first scans" },
        { "AC before DC", "0: 1 63 0 0; 0,1,2: 0 0 0 0", "AC coefficients of the component 1 before its DC coefficient" },
        { "AC refinement skipping a bit", "0,1,2: 0 0 0 0; 0: 1 63 0 3; 0: 1 63 2 1", "expects Al = 2 from the previous scan, which had Al = 3" },
        { "AC refinement past the coded band", "0,1,2: 0 0 0 0; 0: 1 10 0 1; 0: 1 20 1 0", "coefficient 11 of the component 1 precedes its first scan" },
    };

    [Theory]
    [MemberData(nameof(ScanParameterDefects))]
    public void ScanParameterAndProgressionDefectsAreFoundByTheWalker(string name, string script, string message)
    {
        var image = JpegTestImage.CreateRandom(16, 16, Layouts["4:2:0"], seed: 20);
        var data = image.Encode(new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript(script) });

        // Scan headers precede the entropy-coded data: a full identification already rejects them, a header one does not see them
        Assert.Equal(16, Image.Identify(data).Width);
        Assert.Contains(message, Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan })).Message, StringComparison.Ordinal);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.Equal(ImageFormat.Jpeg, exception.Format);
        Assert.False(string.IsNullOrEmpty(name));
    }

    public static TheoryData<string, byte[], string> EntropyDefects
    {
        get
        {
            var dc0 = Huffman(0x00, (1, 0));
            var eob = Huffman(0x10, (1, 0x00));
            var zeroBit = JpegTestImage.PackBits([(0, 1)]);
            var dcFirst = new TestScan(0, 0, 0, 0, zeroBit);
            return new TheoryData<string, byte[], string>
            {
                { "DC category 12", GrayProgressive([Huffman(0x00, (1, 12))], dcFirst), "category is greater than 11" },
                { "DC out of range after the point transform", GrayProgressive([Huffman(0x00, (1, 11))], new TestScan(0, 0, 0, 5, JpegTestImage.PackBits([(0, 1), (0x7FF, 11)]))), "DC coefficient is out of range" },
                { "AC magnitude 11", GrayProgressive([dc0, Huffman(0x10, (1, 0x0B))], dcFirst, new TestScan(1, 63, 0, 0, zeroBit)), "magnitude category is greater than 10" },
                { "AC out of range after the point transform", GrayProgressive([dc0, Huffman(0x10, (1, 0x0A))], dcFirst, new TestScan(1, 63, 0, 6, JpegTestImage.PackBits([(0, 1), (0x3FF, 10)]))), "AC coefficient is out of range" },
                { "AC first run past the band", GrayProgressive([dc0, Huffman(0x10, (1, 0x51))], dcFirst, new TestScan(1, 5, 0, 0, JpegTestImage.PackBits([(0, 1), (1, 1)]))), "run past the end of the spectral band" },
                { "AC refinement run past the band", GrayProgressive([dc0, eob, Huffman(0x11, (1, 0x51))], dcFirst, new TestScan(1, 5, 0, 1, zeroBit), new TestScan(1, 5, 1, 0, JpegTestImage.PackBits([(0, 1), (1, 1)]), 0x01)), "run past the end of the spectral band" },
                { "AC refinement magnitude 2", GrayProgressive([dc0, eob, Huffman(0x11, (1, 0x02))], dcFirst, new TestScan(1, 63, 0, 1, zeroBit), new TestScan(1, 63, 1, 0, JpegTestImage.PackBits([(0, 1), (1, 2)]), 0x01)), "magnitude other than 1" },
                { "invalid Huffman code in a refinement", GrayProgressive([dc0, eob, Huffman(0x11, (2, 0x01))], dcFirst, new TestScan(1, 63, 0, 1, zeroBit), new TestScan(1, 63, 1, 0, [0xFF, 0x00, 0xFF, 0x00], 0x01)), "invalid Huffman code" },
                { "truncated DC refinement", GrayProgressive([dc0], dcFirst with { Al = 1 }, new TestScan(0, 0, 1, 0, [])), "truncated" },
                { "undefined AC table", GrayProgressive([dc0], dcFirst, new TestScan(1, 63, 0, 0, zeroBit)), "undefined AC Huffman table 0" },
                { "undefined DC table", GrayProgressive([eob], dcFirst), "undefined DC Huffman table 0" },
            };
        }
    }

    [Theory]
    [MemberData(nameof(EntropyDefects))]
    public void EntropyDefectsAreFoundByDecoding(string name, byte[] data, string message)
    {
        // The container and the scan headers are valid: a full identification succeeds, decoding fails explicitly
        Assert.Equal(ImageFormat.Jpeg, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Format);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void ScansNeedOnlyTheTablesTheyUse()
    {
        // A DC refinement has no Huffman table (its selectors are ignored); an EOB run longer than the rest of the scan is ignored
        var data = GrayProgressive(
            [Huffman(0x00, (1, 0)), Huffman(0x13, (1, 0xE0))],
            new TestScan(0, 0, 0, 1, JpegTestImage.PackBits([(0, 1)])),
            new TestScan(0, 0, 1, 0, JpegTestImage.PackBits([(1, 1)]), 0x33),
            new TestScan(1, 63, 0, 0, JpegTestImage.PackBits([(0, 1), (0x3FFF, 14)]), 0x33));
        using var decoded = Image.Load(data);

        // DC = 1 (the refined bit) with quantization 1: (1 + 4) >> 3 = 0, then the level shift
        Assert.All(GetBytes(decoded), value => Assert.Equal(128, value));
    }

    [Fact]
    public void EveryComponentNeedsADcScan()
    {
        var image = JpegTestImage.CreateRandom(16, 16, Layouts["4:2:0"], seed: 21);
        var data = image.Encode(new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript("0,1: 0 0 0 0; 0: 1 63 0 0; 1: 1 63 0 0") });
        Assert.Equal(16, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Width);
        Assert.Contains("no DC scan for the component 3", Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartMarkersAreValidatedInEveryScan()
    {
        var image = JpegTestImage.CreateRandom(40, 24, Layouts["4:2:0"], seed: 9);
        var expected = image.GetExpectedPixels();
        var data = image.Encode(Progressive(ColorStandard, restartInterval: 1));
        using (var decoded = Image.Load(data))
        {
            AssertPixels(expected, decoded, "restart interval 1");
        }

        // In a non-interleaved AC scan of the luma (5x3 blocks: 14 markers), out of sequence, missing, without DRI
        var scans = FindSegments(data, 0xDA);
        var markers = FindRestartMarkers(data, scans[1], scans[2]);
        Assert.HasCount(14, markers);
        var renumbered = data.ToArray();
        renumbered[markers[5] + 1] = 0xD0;
        Assert.Contains("out of sequence", Assert.Throws<InvalidImageContentException>(() => Image.Load(renumbered)).Message, StringComparison.Ordinal);
        Assert.Contains("restart marker is missing", Assert.Throws<InvalidImageContentException>(() => Image.Load(Remove(data, markers[^1], 2))).Message, StringComparison.Ordinal);
        var dri = FindSegment(data, 0xDD);
        Assert.Contains("no restart interval", Assert.Throws<InvalidImageContentException>(() => Image.Load(Remove(data, dri, 6))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedInputsAreInvalidExceptAtScanBoundaries()
    {
        var image = JpegTestImage.CreateRandom(16, 16, Layouts["4:2:0"], seed: 4, acDensity: 0.3);
        var scans = JpegTestProgressiveScan.ParseScript(ColorStandard);
        var data = image.Encode(new JpegTestEncodeOptions { Progression = scans, RestartInterval = 2 });
        var sosOffsets = FindSegments(data, 0xDA);

        // A prefix that ends at a marker after a complete scan, followed by EOI, is a valid file with fewer scans
        var boundaries = FindMarkersAfter(data, sosOffsets[0]);
        var partialImages = 0;
        for (var length = 8; length < data.Length; length++)
        {
            Assert.Throws<InvalidImageContentException>(() => Image.Load(data.AsSpan(0, length)));
            if (length <= sosOffsets[0] + 12)
                continue;

            // A cut after the 0xFF of the next marker leaves a fill byte before EOI, which is valid too
            byte[] cut = [.. data.AsSpan(0, length), 0xFF, 0xD9];
            var completed = sosOffsets.Count(offset => offset < length && boundaries.Any(boundary => boundary > offset && boundary <= length));
            if ((boundaries.Contains(length) || boundaries.Contains(length - 1)) && completed > 0)
            {
                using var decoded = Image.Load(cut);
                AssertPixels(Approximate(image, scans[..completed]).GetExpectedPixels(), decoded, $"{completed} scans");
                partialImages++;
                continue;
            }

            try
            {
                using var decoded = Image.Load(cut);
                Assert.Fail($"The prefix of {length} bytes (of {data.Length}) followed by EOI was decoded.");
            }
            catch (InvalidImageContentException)
            {
            }
        }

        Assert.True(partialImages >= scans.Length - 1, $"{partialImages} prefixes decoded as partial progressions.");
    }

    [Fact]
    public void CorruptedBytesFailOnlyWithImageExceptions()
    {
        var image = JpegTestImage.CreateRandom(16, 8, Layouts["4:2:2"], seed: 6, acDensity: 0.15);
        var data = image.Encode(Progressive(ColorStandard, restartInterval: 1, huffman: JpegTestHuffmanStyle.Skewed));
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
    public async Task LargeImagesDecodeThroughEveryInputVariant(InputVariant variant)
    {
        // Scans of tens of kilobytes with stuffing, long codes, EOB runs and restart markers: fed piece by piece
        var image = JpegTestImage.CreateRandom(181, 133, Layouts["4:2:0"], seed: 8, acDensity: 0.6);
        var data = image.Encode(Progressive(ColorStandard, restartInterval: 7, huffman: JpegTestHuffmanStyle.Skewed));
        using var decoded = await InputVariants.LoadAsync<Rgb24>(variant, data, ImageFormat.Jpeg, options: null, XunitCancellationToken);
        AssertPixels(image.GetExpectedPixels(), decoded, variant.ToString());
    }

    [Fact]
    public void CoefficientStateIsChargedForTheWholeImageAndReleased()
    {
        // 1024x256 4:2:0: 4096 luma and 2 x 1024 chroma blocks of 64 16-bit coefficients, kept until EOI
        var image = JpegTestImage.CreateRandom(1024, 256, Layouts["4:2:0"], seed: 1, dcOnlyProbability: 1);
        const long Coefficients = 6144 * 128;
        using var decoded = Image.Load<Rgb24>(image.Encode(Progressive(ColorStandard)));
        var scope = decoded.Owner.Scope;
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.True(state >= Coefficients, $"Peak decoder state {state} bytes: the coefficients of every block ({Coefficients} bytes) are kept.");
        Assert.True(state < Coefficients + (320 * 1024), $"Peak decoder state {state} bytes: only the coefficients, the entropy buffer, three MCU rows of samples and two chroma rows are expected.");
    }

    [Fact]
    public async Task ResourceFaultsReleaseCoefficientsAndImageBuffers()
    {
        var image = JpegTestImage.CreateRandom(256, 128, Layouts["4:4:4"], seed: 22);
        var data = image.Encode(Progressive(ColorStandard));

        // The coefficients alone (3 x 512 blocks x 128 bytes) exceed the live-allocation limit
        var codec = new CapturingJpegCodec();
        using (ImageCodecRegistry.Override(new ImageCodecRegistry([codec])))
        {
            var limits = new ImageResourceLimits { MaxLiveAllocationBytes = 256 * 128 * 4 };
            var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = limits } }));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);

            // Canceled during the reconstruction at EOI: frame pixels, coefficients and planes are all live, then released
            using var cancellation = new CancellationTokenSource();
            codec.BeforeEnd = () =>
            {
                Assert.True(codec.LastContext!.Scope.GetLiveBytes(AllocationKind.DecoderState) >= 3 * 512 * 128);
                cancellation.Cancel();
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync(new MemoryStream(data), cancellationToken: cancellation.Token));
            Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);

            // Invalid entropy-coded data in the last scan
            codec.BeforeEnd = null;
            var scans = FindSegments(data, 0xDA);
            var corrupt = Insert(data, scans[^1] + 14, [0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00]);
            Assert.Throws<InvalidImageContentException>(() => Image.Load(corrupt));
            Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
        }
    }

    [Fact]
    public async Task CancellationDuringScansFaultsTheOperation()
    {
        var data = JpegTestImage.CreateRandom(160, 160, Layouts["4:2:0"], seed: 15, acDensity: 0.6).Encode(Progressive(ColorStandard));
        var position = data.Length / 2;
        var codec = new CapturingJpegCodec();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([codec]));
        using (var source = new CancellationTokenSource())
        {
            await using var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = position, MaxBytesPerRead = 256 };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgb24>(stream, cancellationToken: source.Token));
            Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
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
        var data = JpegTestImage.CreateRandom(64, 48, Layouts["4:2:0"], seed: 16).Encode(Progressive(ColorStandard));
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = (64 * 48) - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Options(new ImageResourceLimits { MaxLiveAllocationBytes = 32 * 1024 }))).Kind);
        using var atLimit = Image.Load(data, Options(new ImageResourceLimits { MaxFramePixels = 64 * 48, MaxEncodedBytes = data.Length }));
        Assert.Equal(64, atLimit.Width);

        static ImageDecodeOptions Options(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Fact]
    public void TypedLoadsConvertTheDecodedRows()
    {
        var color = JpegTestImage.CreateRandom(19, 13, Layouts["4:2:2"], seed: 12).Encode(Progressive(ColorStandard));
        using var rgb = Image.Load<Rgb24>(color);
        AssertSameAsConverted<Rgba32>(color, rgb);
        AssertSameAsConverted<Bgra32>(color, rgb);
        AssertSameAsConverted<Rgba64>(color, rgb);
        AssertSameAsConverted<Gray16>(color, rgb);

        var gray = JpegTestImage.CreateRandom(9, 7, Layouts["gray"], seed: 13).Encode(Progressive(GrayStandard));
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
    public void SequentialReadersDecodeTheFrameAndMetadata()
    {
        var image = JpegTestImage.CreateRandom(30, 20, Layouts["4:2:0"], seed: 14, rgb: true);
        var expected = image.GetExpectedPixels();
        var exif = new TiffBuilder(bigEndian: false).Ifd0(TiffBuilder.Short(0x0112, 3)).Build();
        var data = image.Encode(new JpegTestEncodeOptions
        {
            Progression = JpegTestProgressiveScan.ParseScript(ColorDeep),
            RestartInterval = 2,
            SegmentsBeforeFrame = [JpegTestImage.Segment(0xE1, [.. "Exif\0\0"u8, .. exif])],
            SegmentsAfterScans = [JpegTestImage.Segment(0xFE, "after the scans"u8)],
        });
        using (var eager = Image.Load(data))
        {
            Assert.Equal(ImageColorModel.Rgb, Image.Identify(data).ColorModel);
            Assert.Equal("after the scans", Assert.Single(eager.Metadata.TextEntries).Value);
            Assert.Equal(ExifOrientation.BottomRight, eager.Metadata.Orientation);
            Assert.Equal(FrameDuration.Zero, eager.Frames[0].Metadata.Duration);
            Assert.Single(eager.Frames);
            AssertPixels(expected, eager, "eager, stored pixels (orientation not applied)");
        }

        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data)))
        {
            Assert.Empty(reader.Info.Metadata.TextEntries);
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            AssertPixels(expected, frame, "ReadFrame");
            Assert.Equal("after the scans", Assert.Single(frame.Metadata.TextEntries).Value);
            Assert.Null(reader.ReadFrame());
        }

        using var destination = new Image<Rgb24>(30, 20, new Rgb24(1, 2, 3));
        using (var reader = Image.OpenReader<Rgb24>(new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 7 }))
        {
            Assert.True(reader.ReadFrameInto(destination));
            Assert.False(reader.ReadFrameInto(destination));
        }

        AssertPixels(expected, destination, "ReadFrameInto, non-seekable short reads");
    }

    /// <summary>The coefficients that a sequence of scans sends: each coefficient keeps the bits down to the Al of its last scan.</summary>
    private static JpegTestImage Approximate(JpegTestImage image, IEnumerable<JpegTestProgressiveScan> scans)
    {
        var low = new int[image.Components.Length][];
        for (var c = 0; c < low.Length; c++)
        {
            low[c] = new int[64];
            low[c].AsSpan().Fill(-1);
        }

        foreach (var scan in scans)
        {
            foreach (var component in scan.Components)
            {
                for (var k = scan.Start; k <= scan.End; k++)
                {
                    low[component][k] = scan.Low;
                }
            }
        }

        return image.Approximate((component, k) => low[component][k]);
    }

    private static JpegTestEncodeOptions Progressive(string script, int restartInterval = 0, JpegTestHuffmanStyle huffman = JpegTestHuffmanStyle.Fixed, int maxEndOfBandRun = 0x7FFF, bool tablesBeforeEachScan = false)
        => new()
        {
            Progression = JpegTestProgressiveScan.ParseScript(script),
            RestartInterval = restartInterval,
            Huffman = huffman,
            HuffmanSeed = 3,
            MaxEndOfBandRun = maxEndOfBandRun,
            TablesBeforeEachScan = tablesBeforeEachScan,
        };

    /// <summary>A progressive 8x8 grayscale file (quantization 1) with hand-written tables and scans.</summary>
    private static byte[] GrayProgressive(byte[][] tables, params TestScan[] scans)
    {
        var output = new List<byte> { 0xFF, 0xD8 };
        output.AddRange(JpegTestImage.Segment(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]));
        output.AddRange(JpegTestImage.Segment(0xC2, [8, 0, 8, 0, 8, 1, 1, 0x11, 0]));
        foreach (var table in tables)
        {
            output.AddRange(JpegTestImage.Segment(0xC4, table));
        }

        foreach (var scan in scans)
        {
            output.AddRange(JpegTestImage.Segment(0xDA, [1, 1, scan.Tables, (byte)scan.Ss, (byte)scan.Se, (byte)((scan.Ah << 4) | scan.Al)]));
            output.AddRange(scan.Entropy);
        }

        output.AddRange([0xFF, 0xD9]);
        return [.. output];
    }

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

    private static int FindSegment(byte[] data, byte marker) => FindSegments(data, marker)[0];

    /// <summary>Gets the offsets of the marker segments with a marker, walking the segments and skipping entropy-coded data.</summary>
    private static int[] FindSegments(byte[] data, byte marker)
    {
        var result = new List<int>();
        var offset = 2;
        while (offset < data.Length - 1 && data[offset + 1] != 0xD9)
        {
            var current = data[offset + 1];
            if (current == marker)
            {
                result.Add(offset);
            }

            offset += 2 + ((data[offset + 2] << 8) | data[offset + 3]);
            if (current == 0xDA)
            {
                while (!(data[offset] == 0xFF && data[offset + 1] != 0x00 && data[offset + 1] is < 0xD0 or > 0xD7))
                {
                    offset++;
                }
            }
        }

        return [.. result];
    }

    /// <summary>Gets the offsets of the markers (not restart markers or stuffing) after an offset: the ends of entropy-coded segments and segment starts.</summary>
    private static HashSet<int> FindMarkersAfter(byte[] data, int start)
    {
        var result = new HashSet<int>();
        var offset = start;
        while (offset < data.Length - 1)
        {
            var marker = data[offset + 1];
            result.Add(offset);
            if (marker == 0xD9)
                break;

            offset += 2 + ((data[offset + 2] << 8) | data[offset + 3]);
            if (marker == 0xDA)
            {
                while (!(data[offset] == 0xFF && data[offset + 1] != 0x00 && data[offset + 1] is < 0xD0 or > 0xD7))
                {
                    offset++;
                }
            }
        }

        return result;
    }

    private static List<int> FindRestartMarkers(byte[] data, int start, int end)
    {
        var result = new List<int>();
        for (var i = start; i < end - 1; i++)
        {
            if (data[i] == 0xFF && data[i + 1] is >= 0xD0 and <= 0xD7)
            {
                result.Add(i);
            }
        }

        return result;
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

    /// <summary>A hand-written progressive scan: parameters, entropy-coded bytes and table selectors.</summary>
    private sealed record TestScan(int Ss, int Se, int Ah, int Al, byte[] Entropy, byte Tables = 0);

    /// <summary>The JPEG codec with the library decoder, capturing the context of the last decode (its allocation scope) and calling a hook before EOI.</summary>
    private sealed class CapturingJpegCodec : JpegCodec
    {
        public ImageCodecContext? LastContext { get; private set; }

        public Action? BeforeEnd { get; set; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The observer disposes the decoder.")]
        protected override JpegDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context)
        {
            LastContext = context;
            return new Observer(this, new JpegDecoder(request, context));
        }

        private sealed class Observer(CapturingJpegCodec codec, JpegDecoder inner) : JpegDecodeObserver
        {
            public override void OnSegment(byte marker, ReadOnlySpan<byte> payload) => inner.OnSegment(marker, payload);

            public override void OnHeaderComplete(JpegStructureParser structure) => inner.OnHeaderComplete(structure);

            public override void OnEntropyData(ReadOnlySpan<byte> data) => inner.OnEntropyData(data);

            public override bool OnScanEnd() => inner.OnScanEnd();

            public override void OnEnd(JpegStructureParser structure)
            {
                codec.BeforeEnd?.Invoke();
                inner.OnEnd(structure);
            }

            public override Image GetResult() => inner.GetResult();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
