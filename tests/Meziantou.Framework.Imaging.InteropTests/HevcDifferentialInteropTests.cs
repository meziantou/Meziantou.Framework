using System.Collections.Concurrent;
using System.Globalization;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.InteropTests;

// Differential test of the HEVC decoder against the native decoder of FFmpeg, dec265 (libde265) and TAppDecoder (HM).
//
// By default, 36 parameter tuples are derived from a fixed seed, so that a run is reproducible. Environment variables turn
// the test into a longer campaign:
// - MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_SEED: the seed of the tuples (an integer; default 20261012). A tuple only depends
//   on the seed and on its index, so "seed S, case N" identifies a stream whatever the number of cases.
// - MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_CASES: the number of tuples (default 36). At least 2 tuples out of 3 must be
//   effective (encoded, and decoded identically by two reference decoders).
// - MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_STREAMS: a directory of local raw HEVC streams (*.hevc, *.h265, *.265, *.bit and
//   *.bin, searched recursively), in the byte stream format of Annex B. When it is set, these streams are compared instead
//   of generated ones; the seed and the number of cases are ignored, and at least one stream must be effective. This is how
//   third-party conformance material (the JCT-VC conformance suite) is run: it is downloaded for the campaign and never
//   added to the fixtures. A local stream is decoded with a live allocation limit of 8 GiB, and one that the library
//   refuses as unsupported is reported and does not count.
//
// A failure writes the stream and a description of the tuple (with the command that encoded it) to the directory of
// MEZIANTOU_FRAMEWORK_IMAGING_INTEROP_ARTIFACTS (HevcDifferentialInteropTests), and the message names both files.

/// <summary>
/// The HEVC decoder against independent decoders on streams that are generated for the run: x265, HM and kvazaar encode
/// deterministic pictures with parameters derived from a seed (size, sample format, content, quantization and a set of
/// encoder options): all intra in one campaign, and with pictures predicted from other pictures, out of output order, in
/// another one. FFmpeg, dec265 and HM decode every picture to raw planes, in output order and, when two of them agree
/// byte for byte, the planes of the library must be the same bytes: the same pictures, as many, in the same order. The
/// decoded picture hash that a stream carries for a picture is checked too.
/// </summary>
/// <remarks>
/// HEVC decoding is normative: there is no tolerance. No reference decoder is right on every stream (see
/// <see cref="HevcReferenceDecoder"/>), and the encoders write streams that do not conform for some options (see
/// <see cref="HevcReferenceEncoder"/>): a tuple that an encoder refuses, or on which no two reference decoders agree,
/// proves nothing about the library. So does a stream that the library rejects as invalid when HM, which checks what it
/// decodes, does not decode it either. Such tuples are reported in the test output and do not count, and the test fails
/// when too few tuples are left. The hash of a picture is the one of the encoder, which is not always the hash of the
/// picture that its stream describes (x265 and the HM with weighted prediction of pictures without chroma, the HM in its
/// lossless cost mode): when the reference decoders agree with the library on the planes, a hash that differs is reported
/// as a property of the stream.
/// </remarks>
public sealed class HevcDifferentialInteropTests
{
    private const string SeedVariable = "MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_SEED";
    private const string CasesVariable = "MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_CASES";
    private const string StreamsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_HEVC_DIFF_STREAMS";
    private const int DefaultSeed = 20261012;
    private const int DefaultCases = 36;

    private const int X265OptionSets = 12;
    private const int HmOptionSets = 10;
    private const int KvazaarOptionSets = 5;
    private const int X265InterOptionSets = 9;
    private const int HmInterOptionSets = 6;
    private const int KvazaarInterOptionSets = 4;

    private const long LocalStreamAllocationLimit = 8L << 30;

    private static readonly TimeSpan EncodeTimeout = TimeSpan.FromSeconds(60);
    private static readonly string[] LocalStreamPatterns = ["*.hevc", "*.h265", "*.265", "*.bit", "*.bin"];

    private enum Outcome
    {
        Effective,
        NotCounted,
        Failed,
    }

    /// <param name="Name">The name of the stream in the report and in the artifacts.</param>
    /// <param name="Description">The parameters of the tuple, or the path of a local stream.</param>
    /// <param name="Encoder">The encoder of the tuple; <see langword="null"/> for a local stream.</param>
    /// <param name="Width">The width of the pictures to encode, in luma samples.</param>
    /// <param name="Height">The height of the pictures to encode, in luma samples.</param>
    /// <param name="ChromaFormatIdc">The chroma format of the pictures to encode.</param>
    /// <param name="BitDepth">The bit depth of the pictures to encode.</param>
    /// <param name="FrameCount">The number of pictures to encode.</param>
    /// <param name="Content">The content of the pictures to encode.</param>
    /// <param name="ContentSeed">The seed of the content.</param>
    /// <param name="Options">The options of the encoder.</param>
    /// <param name="LocalPath">The path of a local stream; <see langword="null"/> for a generated one.</param>
    private sealed record StreamTuple(string Name, string Description, string? Encoder, int Width, int Height, int ChromaFormatIdc, int BitDepth, int FrameCount, string Content, int ContentSeed, string[] Options, FullPath? LocalPath);

    /// <param name="Outcome">Whether the tuple counts, and whether the library decoded it as the references.</param>
    /// <param name="Message">The reason of a tuple that does not count, or the failure.</param>
    /// <param name="Tools">The coding tools the library met in the stream.</param>
    /// <param name="Features">Properties of the stream and of its decoding (a diagnostic for the report).</param>
    private sealed record TupleResult(StreamTuple Tuple, Outcome Outcome, string? Message, HevcTools Tools, IReadOnlyList<string> Features);

    [Fact]
    public Task StreamsDecodeAsTheReferenceDecoders() => RunCampaignAsync(inter: false);

    [Fact]
    public Task InterStreamsDecodeAsTheReferenceDecoders() => RunCampaignAsync(inter: true);

    private static async Task RunCampaignAsync(bool inter)
    {
        // The local streams are those of the first campaign
        if (inter && Environment.GetEnvironmentVariable(StreamsVariable) is { Length: > 0 })
            return;

        var decoders = await RequiredTools.HevcReferenceDecodersAsync();
        var encoders = new Dictionary<string, HevcReferenceEncoder>(StringComparer.Ordinal);
        var output = TestContext.Current.TestOutputHelper;
        var tuples = CreateTuples(inter, out var minimumEffective, out var campaign);
        foreach (var name in tuples.Select(tuple => tuple.Encoder).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            encoders.Add(name, await RequiredTools.HevcReferenceEncoderAsync(name));
        }

        output?.WriteLine(campaign);
        var artifacts = InteropSettings.GetArtifactsDirectory(nameof(HevcDifferentialInteropTests));
        var results = new ConcurrentBag<TupleResult>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8), CancellationToken = XunitCancellationToken };
        await Parallel.ForEachAsync(tuples, options, async (tuple, cancellationToken) => results.Add(await RunAsync(decoders, tuple.Encoder is null ? null : encoders[tuple.Encoder], tuple, artifacts, cancellationToken)));

        var ordered = results.OrderBy(result => result.Tuple.Name, StringComparer.Ordinal).ToList();
        var effective = ordered.Where(result => result.Outcome is Outcome.Effective).ToList();
        var failures = ordered.Where(result => result.Outcome is Outcome.Failed).ToList();
        foreach (var result in ordered)
        {
            var status = result.Outcome switch
            {
                Outcome.Effective => "identical",
                Outcome.NotCounted => "NOT COUNTED",
                _ => "FAILED",
            };
            output?.WriteLine($"{result.Tuple.Name}: {status}: {result.Tuple.Description}{(result.Message is null ? "" : " -- " + result.Message)}{(result.Features.Count == 0 ? "" : " [" + string.Join(' ', result.Features) + "]")}");
        }

        // What the effective streams reached: the coding tools of their blocks and the properties of their decoding
        output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{effective.Count} effective tuple(s) out of {ordered.Count} ({failures.Count} failed, {ordered.Count - effective.Count - failures.Count} not counted); {minimumEffective} required."));
        foreach (var tool in Enum.GetValues<HevcTools>().Where(tool => tool is not HevcTools.None))
        {
            output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  tool {tool}: {effective.Count(result => result.Tools.HasFlag(tool))} stream(s)"));
        }

        foreach (var feature in effective.SelectMany(result => result.Features).Select(feature => feature.Split('=')[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var values = effective.SelectMany(result => result.Features).Where(item => item.Split('=')[0] == feature).GroupBy(item => item, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);
            output?.WriteLine("  " + string.Join(", ", values.Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}: {group.Count()}"))));
        }

        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} stream(s) are not decoded as the reference decoders decode them:\n" + string.Join("\n", failures.Select(failure => $"- {failure.Tuple.Name}: {failure.Message}")));

        Assert.HasCountGreaterThanOrEqual(minimumEffective, effective, $"Only {effective.Count} tuple(s) out of {ordered.Count} were encoded and decoded identically by two reference decoders; at least {minimumEffective} are required. See the test output for the tuples that do not count.");
    }

    private static List<StreamTuple> CreateTuples(bool inter, out int minimumEffective, out string campaign)
    {
        if (Environment.GetEnvironmentVariable(StreamsVariable) is { Length: > 0 } directory)
        {
            if (!Directory.Exists(directory))
                Assert.Fail($"{StreamsVariable} is set to '{directory}', which is not a directory.");

            var files = LocalStreamPatterns.SelectMany(pattern => Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (files.Count == 0)
                Assert.Fail($"{StreamsVariable} is set to '{directory}', which has no raw HEVC stream.");

            minimumEffective = 1;
            campaign = $"{files.Count} local stream(s) of '{directory}' ({StreamsVariable}).";
            return [.. files.Select((file, index) => new StreamTuple(string.Create(CultureInfo.InvariantCulture, $"local-{index:D4}-{Path.GetFileNameWithoutExtension(file)}"), file, Encoder: null, 0, 0, 0, 0, 0, "", 0, [], FullPath.FromPath(file)))];
        }

        var seed = ReadInteger(SeedVariable, DefaultSeed, minimum: int.MinValue);
        var cases = ReadInteger(CasesVariable, DefaultCases, minimum: 1);
        minimumEffective = ((cases * 2) + 2) / 3;
        campaign = string.Create(CultureInfo.InvariantCulture, $"Seed {seed} ({SeedVariable}), {cases} tuple(s) ({CasesVariable}).");
        return [.. Enumerable.Range(0, cases).Select(index => inter ? CreateInterTuple(seed, index) : CreateTuple(seed, index))];
    }

    private static int ReadInteger(string variable, int defaultValue, int minimum)
    {
        var text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum)
            Assert.Fail($"{variable} is set to '{text}', which is not an integer of at least {minimum}.");

        return value;
    }

    /// <summary>
    /// Derives a tuple from the seed and its index only: encoder, size, sample format, content, quantization and one set of
    /// encoder options. Three tuples out of six are encoded by x265, two by HM and one by kvazaar; the option sets of an
    /// encoder are taken in turn, so that a run of a few dozen tuples has each of them, and everything else is drawn at
    /// random.
    /// </summary>
    /// <remarks>
    /// Known limits of the pinned encoders that the tuples respect: x265 needs pictures of at least 16 samples and, with
    /// several slices, enough rows of coding tree blocks for its wavefront; its CRC of a picture is not the one of the
    /// specification, so its hashes are MD5 or checksums. HM is slow: its pictures are small. Its streams announce the
    /// 4:4:4 16 intra profile, which allows every tool of the range extensions, and do not combine tiles with the
    /// wavefront, which only another profile allows. kvazaar encodes 8-bit 4:2:0 and 4:0:0 pictures of even sizes with
    /// coding tree blocks of 64 samples; a tile column is at least 256 samples wide (clause A.4.1), which HM checks.
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static StreamTuple CreateTuple(int seed, int index)
    {
        // A generator per tuple: the tuple does not depend on the tuples before it
        var random = new Random(Mix(seed, index));
        var position = index % 6;
        var encoder = position switch
        {
            1 or 4 => HevcReferenceEncoder.Hm,
            3 => HevcReferenceEncoder.Kvazaar,
            _ => HevcReferenceEncoder.X265,
        };
        var ordinal = encoder switch
        {
            HevcReferenceEncoder.X265 => ((index / 6) * 3) + (position switch { 0 => 0, 2 => 1, _ => 2 }),
            HevcReferenceEncoder.Hm => ((index / 6) * 2) + (position is 1 ? 0 : 1),
            _ => index / 6,
        };
        var content = Pick(random, ["gradient", "noise", "blocks", "noise", "blocks", "flat"]);
        var contentSeed = random.Next(1, 1 << 20);
        var frameCount = random.Next(1, 3);
        var qp = random.Next(0, 52);
        int width;
        int height;
        int chromaFormatIdc;
        int bitDepth;
        var options = new List<string>();
        if (encoder is HevcReferenceEncoder.X265)
        {
            chromaFormatIdc = random.Next(0, 4);
            bitDepth = Pick(random, [8, 8, 10, 12]);
            (width, height) = random.Next(3) switch
            {
                0 => (random.Next(16, 41), random.Next(16, 41)),
                1 => (8 * random.Next(2, 14), 8 * random.Next(2, 12)),
                _ => (random.Next(40, 150), random.Next(40, 110)),
            };
            var ctu = Pick(random, [16, 32, 64]);
            var set = (ordinal + (Mix(seed, -1) % X265OptionSets)) % X265OptionSets;
            switch (set)
            {
                case 0:
                    options.AddRange(["--qp", Text(qp)]);
                    break;

                case 1:
                    ctu = 16;
                    options.AddRange(["--qp", Text(qp), "--min-cu-size", "8", "--max-tu-size", Pick(random, ["4", "8", "16"]), "--tu-intra-depth", Text(random.Next(1, 4)), "--rd", Text(random.Next(1, 7))]);
                    break;

                case 2:
                    options.Add("--lossless");
                    break;

                case 3:
                    options.AddRange(["--qp", Text(qp), "--cu-lossless", "--rd", Text(random.Next(3, 7))]);
                    break;

                case 4:
                    options.AddRange(["--qp", Text(qp), "--tskip", "--no-signhide"]);
                    break;

                case 5:
                    options.AddRange(["--qp", Text(qp), "--aq-mode", Text(random.Next(1, 4)), "--qg-size", Pick(random, ["8", "16", "32"])]);
                    ctu = Math.Max(ctu, 32);
                    break;

                case 6:
                    options.AddRange(["--qp", Text(qp), "--scaling-list", "default"]);
                    break;

                case 7:
                    // The wavefront: several rows of coding tree blocks
                    ctu = 16;
                    (width, height) = (Math.Max(width, 48), Math.Max(height, 80));
                    options.AddRange(["--qp", Text(qp), "--pools", "2", "--wpp"]);
                    break;

                case 8:
                    // Several slices need the wavefront, and x265 writes empty slices for a picture of few rows
                    ctu = 16;
                    (width, height) = (Math.Max(width, 48), Math.Max(height, 96));
                    options.AddRange(["--qp", Text(qp), "--pools", "2", "--wpp", "--slices", Text(random.Next(2, 4))]);
                    break;

                case 9:
                    options.AddRange(["--qp", Text(qp), "--no-deblock", "--no-sao", "--cbqpoffs", Text(random.Next(-12, 13)), "--crqpoffs", Text(random.Next(-12, 13))]);
                    break;

                case 10:
                    options.AddRange(["--crf", Text(random.Next(0, 45)), "--deblock", string.Create(CultureInfo.InvariantCulture, $"{random.Next(-6, 7)}:{random.Next(-6, 7)}")]);
                    break;

                default:
                    options.AddRange(["--qp", Text(qp), "--constrained-intra", "--no-strong-intra-smoothing", "--rd", "5", "--hash", "3"]);
                    break;
            }

            // A picture of x265 has at least one coding tree block
            while (ctu > Math.Min(width, height))
            {
                ctu /= 2;
            }

            options.AddRange(["--ctu", Text(ctu)]);
        }
        else if (encoder is HevcReferenceEncoder.Hm)
        {
            chromaFormatIdc = random.Next(0, 4);
            bitDepth = Pick(random, [8, 8, 10, 12]);
            (width, height) = random.Next(3) switch
            {
                0 => (random.Next(8, 25), random.Next(8, 25)),
                1 => (8 * random.Next(1, 9), 8 * random.Next(1, 9)),
                _ => (random.Next(24, 80), random.Next(24, 72)),
            };
            var ctu = Pick(random, [16, 32, 64]);
            var depth = ctu is 16 ? 2 : random.Next(2, ctu is 32 ? 4 : 5);
            var log2Ctu = int.Log2(ctu);
            var maxTu = Math.Min(log2Ctu, 5);
            var set = (ordinal + (Mix(seed, -2) % HmOptionSets)) % HmOptionSets;
            switch (set)
            {
                case 0:
                    // PCM: the encoder chooses it for the blocks that cost more coded than as they are
                    content = "noise";
                    qp = random.Next(0, 6);
                    var log2MinCb = log2Ctu - depth + 1;
                    var pcmMin = Math.Min(log2MinCb, 5);
                    options.AddRange(["--PCMEnabledFlag=1", "--PCMLog2MinSize=" + Text(pcmMin), "--PCMLog2MaxSize=" + Text(random.Next(pcmMin, maxTu + 1)), "--PCMFilterDisableFlag=" + Text(random.Next(2)), "--PCMInputBitDepthFlag=" + Text(random.Next(2))]);
                    break;

                case 1:
                    ctu = 16;
                    depth = 2;
                    maxTu = 4;
                    (width, height) = (Math.Max(width, 40), Math.Max(height, 40));
                    options.AddRange(["--SliceMode=1", "--SliceArgument=" + Text(random.Next(1, 5)), "--LFCrossSliceBoundaryFlag=" + Text(random.Next(2))]);
                    break;

                case 2:
                    ctu = 16;
                    depth = 2;
                    maxTu = 4;
                    (width, height) = (Math.Max(width, 40), Math.Max(height, 40));
                    options.AddRange(["--SliceMode=1", "--SliceArgument=" + Text(random.Next(3, 7)), "--SliceSegmentMode=1", "--SliceSegmentArgument=" + Text(random.Next(1, 3)), "--WaveFrontSynchro=" + Text(random.Next(2))]);
                    break;

                case 3:
                    ctu = 16;
                    depth = 2;
                    maxTu = 4;
                    (width, height) = (Math.Max(width, 40), Math.Max(height, 40));
                    options.AddRange(["--TileUniformSpacing=1", "--NumTileColumnsMinus1=1", "--NumTileRowsMinus1=" + Text(random.Next(2)), "--LFCrossTileBoundaryFlag=" + Text(random.Next(2))]);
                    break;

                case 4:
                    ctu = 16;
                    depth = 2;
                    maxTu = 4;
                    (width, height) = (Math.Max(width, 56), Math.Max(height, 40));
                    options.AddRange(["--TileUniformSpacing=0", "--NumTileColumnsMinus1=2", "--TileColumnWidthArray=1 1", "--NumTileRowsMinus1=1", "--TileRowHeightArray=1", "--LFCrossTileBoundaryFlag=" + Text(random.Next(2)), "--SliceMode=1", "--SliceArgument=" + Text(random.Next(2, 5))]);
                    break;

                case 5:
                    options.AddRange(["--ImplicitResidualDPCM=1", "--ResidualRotation=1", "--SingleSignificanceMapContext=1", "--TransformSkipLog2MaxSize=" + Text(random.Next(2, maxTu + 1))]);
                    if (random.Next(2) is 0)
                    {
                        options.AddRange(["--TransquantBypassEnable=1", "--CostMode=mixed_lossless_lossy"]);
                        qp = random.Next(0, 5);
                    }

                    break;

                case 6:
                    options.AddRange(["--GolombRiceParameterAdaptation=1", "--IntraReferenceSmoothing=" + Text(random.Next(2))]);
                    if (chromaFormatIdc is 3)
                    {
                        options.Add("--CrossComponentPrediction=1");
                    }

                    if (chromaFormatIdc is not 0)
                    {
                        options.Add("--MaxCUChromaQpAdjustmentDepth=" + Text(random.Next(depth)));
                    }

                    if (bitDepth is 12)
                    {
                        options.AddRange(["--SaoLumaOffsetBitShift=" + Text(random.Next(3)), "--SaoChromaOffsetBitShift=" + Text(random.Next(3))]);
                    }

                    break;

                case 7:
                    options.AddRange(["--MaxDeltaQP=" + Text(random.Next(1, 7)), "--MaxCuDQPDepth=" + Text(random.Next(depth)), "--CbQpOffset=" + Text(random.Next(-12, 13)), "--CrQpOffset=" + Text(random.Next(-12, 13))]);
                    break;

                case 8:
                    options.AddRange(["--ScalingList=1", "--ConstrainedIntraPred=" + Text(random.Next(2)), "--StrongIntraSmoothing=" + Text(random.Next(2)), "--SignHideFlag=" + Text(random.Next(2)), "--SEIDecodedPictureHash=" + Text(random.Next(2, 4))]);
                    break;

                default:
                    options.AddRange(["--TransquantBypassEnable=1", random.Next(2) is 0 ? "--CUTransquantBypassFlagForce=1" : "--CostMode=lossless", "--LoopFilterOffsetInPPS=" + Text(random.Next(2)), "--LoopFilterBetaOffset_div2=" + Text(random.Next(-6, 7)), "--LoopFilterTcOffset_div2=" + Text(random.Next(-6, 7))]);
                    break;
            }

            options.AddRange(["--QP=" + Text(qp), "--MaxCUWidth=" + Text(ctu), "--MaxCUHeight=" + Text(ctu), "--MaxPartitionDepth=" + Text(depth), "--QuadtreeTULog2MaxSize=" + Text(random.Next(3, maxTu + 1)), "--QuadtreeTUMaxDepthIntra=" + Text(random.Next(1, 4))]);
        }
        else
        {
            chromaFormatIdc = random.Next(4) is 0 ? 0 : 1;
            bitDepth = 8;
            (width, height) = random.Next(3) switch
            {
                0 => (random.Next(8, 65), random.Next(8, 65)),
                1 => (64 * random.Next(1, 4), 64 * random.Next(1, 3)),
                _ => (random.Next(64, 200), random.Next(64, 150)),
            };
            options.AddRange(["--qp", Text(qp), "--sao", Pick(random, ["off", "band", "edge", "full"])]);
            var set = (ordinal + (Mix(seed, -3) % KvazaarOptionSets)) % KvazaarOptionSets;
            switch (set)
            {
                case 0:
                    // A slice per tile: dec265 filters across these slices, so FFmpeg and HM are the pair that agrees
                    (width, height) = (random.Next(512, 600), Math.Max(height, 130));
                    options.AddRange(["--tiles", random.Next(2) is 0 ? "2x1" : "2x2", "--slices", "tiles"]);
                    break;

                case 1:
                    (width, height) = (Math.Max(width, 70), Math.Max(height, 130));
                    options.AddRange(["--wpp", "--slices", "wpp"]);
                    break;

                case 2:
                    (width, height) = (random.Next(512, 600), Math.Max(height, 130));
                    options.AddRange(["--tiles", "2x1", "--wpp"]);
                    break;

                case 3:
                    options.AddRange(["--lossless", "--implicit-rdpcm"]);
                    break;

                default:
                    options.AddRange(["--vaq", Text(random.Next(1, 20)), "--signhide", "--transform-skip", "--scaling-list", "default", "--hash", "checksum"]);
                    break;
            }
        }

        // A picture with subsampled chroma has a whole number of chroma samples, and kvazaar only takes even sizes
        if (chromaFormatIdc is 1 or 2 || encoder is HevcReferenceEncoder.Kvazaar)
        {
            width &= ~1;
        }

        if (chromaFormatIdc is 1 || encoder is HevcReferenceEncoder.Kvazaar)
        {
            height &= ~1;
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"seed{seed}-case{index:D4}");
        var format = chromaFormatIdc switch
        {
            0 => "4:0:0",
            1 => "4:2:0",
            2 => "4:2:2",
            _ => "4:4:4",
        };
        var description = string.Create(CultureInfo.InvariantCulture, $"{encoder} {width}x{height} {format} {bitDepth}-bit {frameCount} picture(s) {content} [{string.Join(' ', options)}]");
        return new StreamTuple(name, description, encoder, width, height, chromaFormatIdc, bitDepth, frameCount, content, contentSeed, [.. options], LocalPath: null);

        static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A well-mixed non-negative number from a seed and an index (the finalizer of SplitMix64): <see cref="Random"/>
    /// instances created from consecutive seeds give correlated first values.
    /// </summary>
    // A tuple of the campaign of the pictures predicted from other pictures: 3 to 9 pictures of a content that moves or
    // fades, and a structure of pictures (reference pictures, order, random access points) that the option set chooses
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static StreamTuple CreateInterTuple(int seed, int index)
    {
        var random = new Random(Mix(seed, index + 1_000_000));
        var position = index % 6;
        var encoder = position switch
        {
            1 or 4 => HevcReferenceEncoder.Hm,
            3 => HevcReferenceEncoder.Kvazaar,
            _ => HevcReferenceEncoder.X265,
        };
        var ordinal = encoder switch
        {
            HevcReferenceEncoder.X265 => ((index / 6) * 3) + (position switch { 0 => 0, 2 => 1, _ => 2 }),
            HevcReferenceEncoder.Hm => ((index / 6) * 2) + (position is 1 ? 0 : 1),
            _ => index / 6,
        };
        var content = Pick(random, ["pan", "pan", "fade", "panfade", "panfade"]);
        var contentSeed = random.Next(1, 1 << 20);
        var frameCount = random.Next(3, 10);
        var qp = random.Next(12, 46);
        int width;
        int height;
        int chromaFormatIdc;
        int bitDepth;
        var options = new List<string>();
        if (encoder is HevcReferenceEncoder.X265)
        {
            chromaFormatIdc = random.Next(0, 4);
            bitDepth = Pick(random, [8, 8, 10, 12]);
            (width, height) = random.Next(2) is 0 ? (8 * random.Next(4, 14), 8 * random.Next(4, 12)) : (random.Next(40, 150), random.Next(40, 110));
            var ctu = Pick(random, [16, 32, 64]);
            while (ctu > Math.Min(width, height))
            {
                ctu /= 2;
            }

            options.AddRange(["--ctu", Text(ctu), "--scenecut", "0", "--rc-lookahead", "10"]);
            var set = (ordinal + (Mix(seed, -2) % X265InterOptionSets)) % X265InterOptionSets;
            switch (set)
            {
                case 0:
                    options.AddRange(["--keyint", "250", "--bframes", "0", "--ref", Text(random.Next(1, 4)), "--qp", Text(qp)]);
                    break;

                case 1:
                    options.AddRange(["--keyint", "250", "--bframes", "3", "--b-adapt", "0", "--b-pyramid", "--ref", "3", "--weightp", "--weightb", "--qp", Text(qp)]);
                    break;

                case 2:
                    options.AddRange(["--keyint", "4", "--min-keyint", "4", "--bframes", Text(random.Next(2, 5)), "--b-adapt", "0", "--open-gop", "--qp", Text(qp)]);
                    break;

                case 3:
                    options.AddRange(["--keyint", "250", "--bframes", "2", "--ref", "4", "--rect", "--amp", "--max-merge", "5", "--subme", "5", "--qp", Text(qp)]);
                    break;

                case 4:
                    options.AddRange(["--keyint", "250", "--bframes", "1", "--no-temporal-mvp", "--max-merge", "1", "--me", "full", "--merange", "8", "--qp", Text(qp)]);
                    break;

                case 5:
                    options.AddRange(["--keyint", "250", "--bframes", "4", "--b-adapt", "2", "--lossless"]);
                    break;

                case 6:
                    options.AddRange(["--keyint", "3", "--min-keyint", "3", "--bframes", "2", "--b-adapt", "0", "--no-open-gop", "--radl", "2", "--qp", Text(qp)]);
                    break;

                case 7:
                    options.AddRange(["--keyint", "250", "--bframes", "2", "--tskip", "--constrained-intra", "--cu-lossless", "--no-sao", "--qp", Text(qp)]);
                    break;

                default:
                    options.AddRange(["--keyint", "250", "--bframes", "3", "--b-adapt", "0", "--temporal-layers", "2", "--ref", "2", "--aq-mode", "2", "--qg-size", "16", "--deblock", "-2:3", "--crf", Text(random.Next(10, 40))]);
                    break;
            }
        }
        else if (encoder is HevcReferenceEncoder.Hm)
        {
            chromaFormatIdc = random.Next(0, 4);
            bitDepth = Pick(random, [8, 8, 10, 12]);
            (width, height) = random.Next(2) is 0 ? (8 * random.Next(2, 12), 8 * random.Next(2, 10)) : (random.Next(24, 110), random.Next(24, 90));
            var ctu = Pick(random, [16, 32, 64]);
            var depth = ctu is 16 ? 2 : 3;
            options.AddRange(["--IntraConstraintFlag=0", "--MaxBitDepthConstraint=12", "--MaxCUWidth=" + Text(ctu), "--MaxCUHeight=" + Text(ctu), "--MaxPartitionDepth=" + Text(depth),
                "--QuadtreeTULog2MaxSize=" + Text(ctu is 16 ? 4 : 5), "--QuadtreeTUMaxDepthInter=" + Text(random.Next(1, 4)), "--QP=" + Text(qp), "--SearchRange=16", "--FastSearch=1"]);
            var set = (ordinal + (Mix(seed, -3) % HmInterOptionSets)) % HmInterOptionSets;
            switch (set)
            {
                case 0:
                    options.AddRange(["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=P 1 0 0.0 0.0 0 0 1.0 0 0 0 2 2 -1 -2 0"]);
                    break;

                case 1:
                    options.AddRange(["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=B 1 1 0.0 0.0 0 0 1.0 0 0 0 3 3 -1 -2 -3 0", "--WeightedPredP=1", "--WeightedPredB=1"]);
                    break;

                case 2:
                    options.AddRange(RandomAccess4);
                    options.AddRange(["--IntraPeriod=8", "--DecodingRefreshType=1"]);
                    break;

                case 3:
                    options.AddRange(["--IntraPeriod=-1", "--GOPSize=2", "--Frame1=B 2 1 0.0 0.0 0 0 1.0 0 0 0 2 2 -2 -4 0", "--Frame2=B 1 2 0.0 0.0 0 0 1.0 0 0 1 2 3 -1 -3 1 0",
                        "--AMP=1", "--MaxNumMergeCand=3", "--TMVPMode=0"]);
                    break;

                case 4:
                    options.AddRange(RandomAccess4);
                    options.AddRange(["--IntraPeriod=-1", "--ExplicitResidualDPCM=1", "--ImplicitResidualDPCM=1", "--ResidualRotation=1", "--GolombRiceParameterAdaptation=1", "--Log2ParallelMergeLevel=3"]);
                    if (chromaFormatIdc is 3)
                    {
                        options.Add("--CrossComponentPrediction=1");
                    }

                    break;

                default:
                    options.AddRange(RandomAccess4);
                    options.AddRange(["--IntraPeriod=8", "--DecodingRefreshType=2", "--SliceMode=1", "--SliceArgument=" + Text(random.Next(1, 4)), "--SliceSegmentMode=1", "--SliceSegmentArgument=1", "--MaxDeltaQP=2", "--ConstrainedIntraPred=1"]);
                    break;
            }
        }
        else
        {
            // kvazaar encodes 4:2:0 and 4:0:0 pictures of 8 bits
            chromaFormatIdc = random.Next(2);
            bitDepth = 8;
            (width, height) = (2 * random.Next(32, 100), 2 * random.Next(32, 80));
            options.AddRange(["--qp", Text(qp)]);
            var set = (ordinal + (Mix(seed, -4) % KvazaarInterOptionSets)) % KvazaarInterOptionSets;
            switch (set)
            {
                case 0:
                    options.AddRange(["-p", "0", "--gop", "lp-g4d3t1", "--ref", "2"]);
                    break;

                case 1:
                    options.AddRange(["-p", "0", "--gop", "8", "--bipred", "--ref", "3"]);
                    break;

                case 2:
                    options.AddRange(["-p", "8", "--gop", "lp-g4d4t1", "--wpp", "--ref", "1", "--no-tmvp"]);
                    break;

                default:
                    options.AddRange(["-p", "0", "--gop", "0", "--ref", "3", "--smp", "--amp", "--max-merge", "3"]);
                    break;
            }
        }

        if (chromaFormatIdc is 1 or 2)
        {
            width &= ~1;
        }

        if (chromaFormatIdc is 1)
        {
            height &= ~1;
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"seed{seed}-inter{index:D4}");
        var format = chromaFormatIdc switch
        {
            0 => "4:0:0",
            1 => "4:2:0",
            2 => "4:2:2",
            _ => "4:4:4",
        };
        var description = string.Create(CultureInfo.InvariantCulture, $"{encoder} {width}x{height} {format} {bitDepth}-bit {frameCount} picture(s) {content} [{string.Join(' ', options)}]");
        return new StreamTuple(name, description, encoder, width, height, chromaFormatIdc, bitDepth, frameCount, content, contentSeed, [.. options], LocalPath: null);

        static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    // A structure of 4 pictures of the HM whose pictures are decoded out of output order, with a temporal sub-layer per level
    private static readonly string[] RandomAccess4 =
    [
        "--GOPSize=4",
        "--Frame1=B 4 1 0.0 0.0 0 0 1.0 0 0 0 2 2 -4 -8 0",
        "--Frame2=B 2 2 0.0 0.0 0 0 1.0 0 0 1 2 3 -2 -6 2 0",
        "--Frame3=B 1 3 0.0 0.0 0 0 1.0 0 0 2 2 4 -1 -5 1 3 0",
        "--Frame4=B 3 3 0.0 0.0 0 0 1.0 0 0 2 2 4 -1 -3 -7 1 0",
    ];

    private static int Mix(int seed, int index)
    {
        unchecked
        {
            var z = (((ulong)(uint)seed << 32) | (uint)index) + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// Writes the pictures of a tuple as raw planes: a gradient, blocks of constant values or noise, which is what makes an
    /// encoder choose different tools.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] CreateSource(StreamTuple tuple)
    {
        if (tuple.Content is "pan" or "fade" or "panfade")
            return CreateMovingSource(tuple);

        var random = new Random(tuple.ContentSeed);
        var maxValue = (1 << tuple.BitDepth) - 1;
        var bytesPerSample = tuple.BitDepth > 8 ? 2 : 1;
        using var stream = new MemoryStream();
        for (var frame = 0; frame < tuple.FrameCount; frame++)
        {
            for (var plane = 0; plane < (tuple.ChromaFormatIdc is 0 ? 1 : 3); plane++)
            {
                var width = plane > 0 && tuple.ChromaFormatIdc is 1 or 2 ? tuple.Width / 2 : tuple.Width;
                var height = plane > 0 && tuple.ChromaFormatIdc is 1 ? tuple.Height / 2 : tuple.Height;
                var blockSize = random.Next(3, 20);
                var blockValues = new int[((width / blockSize) + 1) * ((height / blockSize) + 1)];
                for (var i = 0; i < blockValues.Length; i++)
                {
                    blockValues[i] = random.Next(maxValue + 1);
                }

                var flat = random.Next(maxValue + 1);
                var slopeX = random.Next(-4, 5);
                var slopeY = random.Next(-4, 5);
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var value = tuple.Content switch
                        {
                            "gradient" => (maxValue / 2) + (((x * slopeX) + (y * slopeY) + (frame * 3)) * (maxValue + 1) / 256),
                            "noise" => random.Next(4) is 0 ? random.Next(maxValue + 1) : blockValues[((y / blockSize) * ((width / blockSize) + 1)) + (x / blockSize)] + random.Next(-(maxValue / 32), (maxValue / 32) + 1),
                            "blocks" => blockValues[((y / blockSize) * ((width / blockSize) + 1)) + (x / blockSize)],
                            _ => flat,
                        };
                        value = Math.Clamp(value, 0, maxValue);
                        stream.WriteByte((byte)value);
                        if (bytesPerSample is 2)
                        {
                            stream.WriteByte((byte)(value >> 8));
                        }
                    }
                }
            }
        }

        return stream.ToArray();
    }

    // Pictures of a texture that moves by a few samples a picture, and whose values fade in or out
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] CreateMovingSource(StreamTuple tuple)
    {
        var random = new Random(tuple.ContentSeed);
        var maxValue = (1 << tuple.BitDepth) - 1;
        var bytesPerSample = tuple.BitDepth > 8 ? 2 : 1;
        var planeCount = tuple.ChromaFormatIdc is 0 ? 1 : 3;
        var pan = tuple.Content is not "fade";
        var fade = tuple.Content is not "pan";
        var speedX = pan ? random.Next(-5, 6) : 0;
        var speedY = pan ? random.Next(-3, 4) : 0;
        var fadeIn = random.Next(2) is 0;

        // The texture of each plane: blocks of random values and a slope, on a surface larger than the pictures
        const int Margin = 64;
        var textures = new int[planeCount][];
        var textureWidth = tuple.Width + (2 * Margin);
        var textureHeight = tuple.Height + (2 * Margin);
        for (var plane = 0; plane < planeCount; plane++)
        {
            var blockSize = random.Next(5, 24);
            var blocksPerRow = (textureWidth / blockSize) + 1;
            var blockValues = new int[blocksPerRow * ((textureHeight / blockSize) + 1)];
            for (var i = 0; i < blockValues.Length; i++)
            {
                blockValues[i] = random.Next(maxValue + 1);
            }

            var slope = random.Next(-3, 4);
            textures[plane] = new int[textureWidth * textureHeight];
            for (var y = 0; y < textureHeight; y++)
            {
                for (var x = 0; x < textureWidth; x++)
                {
                    textures[plane][(y * textureWidth) + x] = Math.Clamp(((blockValues[((y / blockSize) * blocksPerRow) + (x / blockSize)] * 3) + (maxValue / 2) + ((x + y) * slope * (maxValue + 1) / 512)) / 4, 0, maxValue);
                }
            }
        }

        using var stream = new MemoryStream();
        for (var frame = 0; frame < tuple.FrameCount; frame++)
        {
            var weight = !fade ? 16 : fadeIn ? Math.Min(16, 2 + (frame * 3)) : Math.Max(1, 16 - (frame * 3));
            for (var plane = 0; plane < planeCount; plane++)
            {
                var subWidth = plane > 0 && tuple.ChromaFormatIdc is 1 or 2 ? 2 : 1;
                var subHeight = plane > 0 && tuple.ChromaFormatIdc is 1 ? 2 : 1;
                var offsetX = Math.Clamp(frame * speedX, -Margin, Margin);
                var offsetY = Math.Clamp(frame * speedY, -Margin, Margin);
                var neutral = plane is 0 ? 0 : (maxValue + 1) / 2;
                for (var y = 0; y < tuple.Height / subHeight; y++)
                {
                    for (var x = 0; x < tuple.Width / subWidth; x++)
                    {
                        var texel = textures[plane][((Margin + (y * subHeight) + offsetY) * textureWidth) + Margin + (x * subWidth) + offsetX];
                        var value = Math.Clamp(neutral + (((texel - neutral) * weight) / 16), 0, maxValue);
                        stream.WriteByte((byte)value);
                        if (bytesPerSample is 2)
                        {
                            stream.WriteByte((byte)(value >> 8));
                        }
                    }
                }
            }
        }

        return stream.ToArray();
    }

    private static async Task<TupleResult> RunAsync(IReadOnlyList<HevcReferenceDecoder> decoders, HevcReferenceEncoder? encoder, StreamTuple tuple, FullPath artifacts, CancellationToken cancellationToken)
    {
        var work = artifacts / "work" / tuple.Name;
        Directory.CreateDirectory(work);
        try
        {
            var stream = tuple.LocalPath ?? work / "stream.hevc";
            var command = "(local stream)";
            if (encoder is not null)
            {
                var source = work / "source.yuv";
                await File.WriteAllBytesAsync(source, CreateSource(tuple), cancellationToken);
                var arguments = encoder.GetArguments(source, stream, tuple.Width, tuple.Height, tuple.ChromaFormatIdc, tuple.BitDepth, tuple.FrameCount, tuple.Options);
                command = encoder.Name + " " + string.Join(' ', arguments.Select(Quote));

                // An encoder that does not finish refuses the tuple, like one that fails
                var refusal = await encoder.EncodeAsync(arguments, EncodeTimeout, cancellationToken);
                if (refusal is not null || !File.Exists(stream) || new FileInfo(stream).Length == 0)
                    return NotCounted(tuple, "the encoder refuses the tuple: " + (refusal ?? "it wrote no stream."));
            }

            // The references: every output picture as decoded by each decoder, in the format of the stream. The planes that
            // two decoders give are the reference
            var data = await File.ReadAllBytesAsync(stream, cancellationToken);
            var references = new List<(HevcReferenceDecoder Decoder, HevcReferenceDecoderResult Result)>();
            foreach (var decoder in decoders)
            {
                references.Add((decoder, await decoder.DecodeAsync(stream, work / decoder.Name, cancellationToken)));
            }

            ReadOnlyMemory<byte>? reference = null;
            var agreeing = new List<string>();
            foreach (var (decoder, result) in references)
            {
                if (!result.Succeeded)
                    continue;

                var names = references.Where(other => other.Result.Succeeded && other.Result.Planes.Span.SequenceEqual(result.Planes.Span)).Select(other => other.Decoder.Name).ToList();
                if (names.Count > agreeing.Count)
                {
                    agreeing = names;
                    reference = result.Planes;
                }
            }

            var hmRejects = !references.First(item => item.Decoder.Name is HevcReferenceDecoder.Hm).Result.Succeeded;
            var (failure, tools, features, kind) = DecodeAndCompare(tuple.Name, data, agreeing.Count >= 2 ? reference : null, isLocal: tuple.LocalPath is not null, cancellationToken);
            var decoderReport = string.Join("; ", references.Select(item => item.Result.Succeeded ? $"{item.Decoder.Name}: {item.Result.Planes.Length} bytes" : $"{item.Decoder.Name}: {item.Result.Error}"));
            if (agreeing.Count < 2)
                return NotCounted(tuple, $"no two reference decoders agree ({decoderReport}); the library: {failure ?? "decodes the stream, and its pictures have the hashes of the stream"}.");

            features.Add("references=" + string.Join('+', agreeing));
            if (failure is null)
                return new TupleResult(tuple, Outcome.Effective, Message: null, tools, [.. features]);

            if (kind is FailureKind.Unsupported)
                return NotCounted(tuple, failure);

            // A stream that the library rejects and that the reference software of the specification does not decode either
            // is a stream that two tolerant decoders read in the same way
            if (kind is FailureKind.Invalid && hmRejects)
                return NotCounted(tuple, $"{failure} HM does not decode the stream either ({decoderReport}).");

            // The stream and its tuple are kept for the diagnosis
            var streamCopy = artifacts / (tuple.Name + ".hevc");
            var descriptionPath = artifacts / (tuple.Name + ".txt");
            File.Copy(stream, streamCopy, overwrite: true);
            await File.WriteAllTextAsync(descriptionPath, $"{tuple.Name}\n{tuple.Description}\n{command}\nreferences: {decoderReport}\n{failure}\n", cancellationToken);
            return new TupleResult(tuple, Outcome.Failed, $"{failure} Stream: '{streamCopy}', tuple: '{descriptionPath}'.", tools, [.. features]);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        static TupleResult NotCounted(StreamTuple tuple, string reason) => new(tuple, Outcome.NotCounted, reason, HevcTools.None, []);

        // Enough for a POSIX shell: the arguments have no single quote
        static string Quote(string argument) => argument.AsSpan().IndexOfAny(" ;&|<>()$*?[\"") >= 0 ? "'" + argument + "'" : argument;
    }

    private enum FailureKind
    {
        None,
        Different,
        Invalid,
        Unsupported,
    }

    /// <summary>
    /// Decodes a stream with the library, one access unit at a time, compares the planes of its pictures, cropped to the
    /// conformance window, with the reference bytes, and checks the decoded picture hashes of the stream.
    /// </summary>
    /// <returns>The first difference, or <see langword="null"/>; the coding tools and the properties of the stream; the kind of the failure.</returns>
    private static (string? Failure, HevcTools Tools, SortedSet<string> Features, FailureKind Kind) DecodeAndCompare(string name, byte[] data, ReadOnlyMemory<byte>? reference, bool isLocal, CancellationToken cancellationToken)
    {
        var features = new SortedSet<string>(StringComparer.Ordinal);

        // Conformance streams have pictures of up to 8192 samples in a direction, and a decoded picture buffer of 16 pictures
        var configuration = isLocal ? new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = LocalStreamAllocationLimit } } : ImageConfiguration.Default;
        var scope = AllocationScope.Create(configuration, name);
        using var decoder = new HevcDecoder(scope);
        using var actual = new MemoryStream();
        var pictures = 0;
        var hashes = 0;
        string? hashFailure = null;
        try
        {
            var accessUnits = ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(data));
            foreach (var accessUnit in accessUnits)
            {
                var input = ReferenceHevcStream.ToLengthPrefixed(accessUnit);
                decoder.SetInput(input, 0, input.Length, nalLengthSize: 4);
                if (Drain() is { } failure)
                    return (failure, decoder.Tools, features, FailureKind.Different);
            }

            decoder.EndOfSequence();
            if (Drain() is { } lastFailure)
                return (lastFailure, decoder.Tools, features, FailureKind.Different);
        }
        catch (UnsupportedImageFeatureException exception)
        {
            return ($"the library does not support the stream: {exception.Message}", decoder.Tools, features, isLocal ? FailureKind.Unsupported : FailureKind.Different);
        }
        catch (InvalidImageContentException exception)
        {
            return ($"The library rejects the stream after {pictures} picture(s): {exception.Message}", decoder.Tools, features, FailureKind.Invalid);
        }
        catch (InvalidDataException exception)
        {
            return ($"The test harness cannot split the stream: {exception.Message}", decoder.Tools, features, FailureKind.Invalid);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ($"The library fails after {pictures} picture(s): {exception.GetType().Name}: {exception.Message}", decoder.Tools, features, FailureKind.Different);
        }

        features.Add(string.Create(CultureInfo.InvariantCulture, $"hashes={(hashFailure is not null ? "not-those-of-the-encoder" : hashes == pictures ? "all" : hashes > 0 ? "some" : "none")}"));
        features.Add(string.Create(CultureInfo.InvariantCulture, $"pictures={pictures}"));
        if (decoder.Tools.HasFlag(HevcTools.PictureReordering))
        {
            features.Add("order=reordered");
        }

        if (decoder.HasEnhancementLayers)
        {
            features.Add("layers=ignored");
        }

        // Without planes that two reference decoders agree on, the hash of the encoder is all there is
        if (reference is null)
            return (hashFailure, decoder.Tools, features, hashFailure is null ? FailureKind.None : FailureKind.Different);

        var bytes = actual.GetBuffer().AsSpan(0, (int)actual.Length);
        var expected = reference.Value.Span;
        if (bytes.SequenceEqual(expected))
            return (null, decoder.Tools, features, FailureKind.None);

        var index = bytes.CommonPrefixLength(expected);
        return (string.Create(CultureInfo.InvariantCulture, $"The planes of the library differ from the planes of the reference decoders at the byte {index} ({bytes.Length} bytes for {pictures} picture(s); {expected.Length} bytes expected)."), decoder.Tools, features, FailureKind.Different);

        string? Drain()
        {
            while (decoder.DecodeNextOutputPicture(cancellationToken) is { } picture)
            {
                try
                {
                    var sps = picture.Sps;
                    features.Add(string.Create(CultureInfo.InvariantCulture, $"format={sps.ChromaFormatIdc switch { 0 => "400", 1 => "420", 2 => "422", _ => "444" }}/{sps.BitDepth}"));
                    features.Add(string.Create(CultureInfo.InvariantCulture, $"ctb={sps.CtbSizeY}"));
                    if (sps.ConformanceWindowFlag)
                    {
                        features.Add("window=cropped");
                    }

                    if (CheckHash(picture, ref hashes) is { } failure)
                    {
                        hashFailure ??= failure;
                    }

                    // The planes as a reference decoder writes them: cropped to the conformance window
                    for (var plane = 0; plane < picture.PlaneCount; plane++)
                    {
                        var subWidth = plane is 0 ? 1 : sps.SubWidthC;
                        var subHeight = plane is 0 ? 1 : sps.SubHeightC;
                        var samples = picture.GetPlane(plane);
                        for (var y = sps.OutputTop / subHeight; y < (sps.OutputTop + sps.OutputHeight) / subHeight; y++)
                        {
                            foreach (var sample in samples.GetRow(y)[(sps.OutputLeft / subWidth)..((sps.OutputLeft + sps.OutputWidth) / subWidth)])
                            {
                                actual.WriteByte((byte)sample);
                                if (sps.BitDepth > 8)
                                {
                                    actual.WriteByte((byte)(sample >> 8));
                                }
                            }
                        }
                    }

                    pictures++;
                }
                finally
                {
                    picture.Release();
                }
            }

            return null;
        }
    }

    // Clause D.3.20 of Rec. ITU-T H.265: the hash of each plane of the picture before its cropping
    private static string? CheckHash(HevcPicture picture, ref int hashes)
    {
        var hash = picture.DecodedPictureHash;
        if (hash is null)
            return null;

        for (var cIdx = 0; cIdx < picture.PlaneCount; cIdx++)
        {
            var plane = picture.GetPlane(cIdx);
            var matches = hash.HashType switch
            {
                HevcDecodedPictureHash.Md5 => hash.GetMd5(cIdx).SequenceEqual(ReferenceHevcPictureHash.ComputeMd5(plane.Samples, plane.Width, plane.Height, plane.Stride, picture.Sps.BitDepth)),
                HevcDecodedPictureHash.Crc => hash.GetValue(cIdx) == ReferenceHevcPictureHash.ComputeCrc(plane.Samples, plane.Width, plane.Height, plane.Stride, picture.Sps.BitDepth),
                _ => hash.GetValue(cIdx) == ReferenceHevcPictureHash.ComputeChecksum(plane.Samples, plane.Width, plane.Height, plane.Stride, picture.Sps.BitDepth),
            };
            if (!matches)
                return string.Create(CultureInfo.InvariantCulture, $"The plane {cIdx} of the picture {picture.PicOrderCntVal} of the access unit {picture.AccessUnitIndex} does not have the hash of the stream (hash type {hash.HashType}).");
        }

        hashes++;
        return null;
    }
}
