using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The AVIF container fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/avif, invalid/avif and
// limit/avif; manifest entries of format "avif"): the "avif-container" generator. The AV1 conformance vectors of
// AvifCorpus.cs test the bitstream; these fixtures test what is around it: the boxes, the item model, the image
// sequences, the color conversion, and the errors and limits of the container.
//
// This generator is the reviewed, reproducible recipe of every committed AVIF fixture. It is NEVER run by the tests and
// does not use the library under test.
// - Inputs: written by libavif avifenc (and one by the FFmpeg avif muxer) from hand-defined patterns, or assembled box by
//   box (AvifFileBuilder, linked from the test harness) around the AV1 payloads of such a file or of a committed AV1 vector
//   for what the encoders do not write. Malformed, unsupported and over-limit inputs are assembled the same way.
// - References: the structure of every input is read by ReferenceAvif (an independent reader written from the
//   specifications, linked from the test harness); the AV1 payloads of the items and of the samples are extracted and
//   decoded to planes by two independent decoders, dav1d and libaom, through the pinned FFmpeg, in the native pixel format
//   of the stream (they must agree byte for byte, as for the AV1 vectors); the planes are converted by
//   ReferenceYuvConverter (exact rationals) with the matrix coefficients and the range of the colr box, or else of the
//   bitstream; the grid, the alpha plane and the clean aperture are applied here.
// - Comparison: identity-matrix, monochrome and alpha samples are exact. Matrixed samples use a tolerance of one unit: the
//   library documents coefficients rounded to 16 fractional bits, the reference uses exact rationals.
// - Cross-checks: libavif avifdec (its own container parser, dav1d and its own color conversion) and FFmpeg (its mov
//   demuxer and swscale) decode every input they accept; their differences are measured and recorded, never used as
//   references.
//
// Usage (from the repository root):
//     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif-container            # check
//     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif-container --write    # explicit regeneration
internal static partial class AvifCorpus
{
    public const string ContainerScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/AvifCorpus.Container.cs";

    private const string PinnedLibavif = "1.4.2";

    // Measured disagreement of the library decoder with each reference of a matrixed fixture: (max absolute error, mean
    // absolute color error) on the 8-bit scale, printed by Tests/Conformance/AvifDecoderConformanceTests, which also fails
    // when the policy derived from these values is looser or tighter than the decoder needs. The values are reviewed
    // measurements of the library (never used as pixel references).
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryAvifMeasurements = new(StringComparer.Ordinal)
    {
        ["avif/clap-420"] = (0, 0.0),
        ["avif/film-grain-420"] = (0, 0.0),
        ["avif/film-grain-420-alpha"] = (0, 0.0),
        ["avif/film-grain-420-odd"] = (0, 0.0),
        ["avif/film-grain-422-10bit-limited"] = (0, 0.0),
        ["avif/film-grain-444-12bit"] = (1, 0.0),
        ["avif/grid-2x1-10bit-444"] = (0, 0.0),
        ["avif/grid-2x1-alpha-420"] = (0, 0.0),
        ["avif/grid-3x1-420"] = (0, 0.0),
        ["avif/hdr-mastering-display"] = (1, 0.0), // one unit of a 16-bit sample: 1/257 on the 8-bit scale
        ["avif/iden-color-from-input"] = (0, 0.0),
        ["avif/layered-2-quality-420"] = (0, 0.0),
        ["avif/layered-3-scaled-422-10bit"] = (1, 0.0), // one unit of a 16-bit sample: 1/257 on the 8-bit scale
        ["avif/layered-lsel-0-420"] = (0, 0.0),
        ["avif/layered-progressive-420"] = (0, 0.0),
        ["avif/layered-progressive-alpha"] = (0, 0.0),
        ["avif/lossless-444-bt2020-constant-luminance-10bit"] = (0, 0.0),
        ["avif/lossless-444-chromaticity-constant-luminance-p3"] = (0, 0.0),
        ["avif/lossless-444-ictcp-hlg-12bit"] = (0, 0.0),
        ["avif/lossless-444-ictcp-pq-12bit"] = (0, 0.0),
        ["avif/lossy-420-alpha"] = (0, 0.0),
        ["avif/lossy-420-alpha-12bit"] = (1, 0.0001), // one unit on a few samples
        ["avif/lossy-420-bt2020-constant-luminance"] = (0, 0.0),
        ["avif/lossy-420-bt2020-limited"] = (0, 0.0),
        ["avif/lossy-420-bt2020-limited-10bit"] = (1, 0.0), // one unit of a 16-bit sample: 1/257 on the 8-bit scale
        ["avif/lossy-420-bt470bg-full"] = (0, 0.0),
        ["avif/lossy-420-bt470bg-limited-12bit"] = (0, 0.0),
        ["avif/lossy-420-bt601-1x1"] = (0, 0.0),
        ["avif/lossy-420-bt601-33x17"] = (0, 0.0),
        ["avif/lossy-420-bt601-5x1"] = (0, 0.0),
        ["avif/lossy-420-bt601-limited"] = (0, 0.0),
        ["avif/lossy-420-bt601-odd"] = (0, 0.0),
        ["avif/lossy-420-bt601-odd-10bit"] = (0, 0.0),
        ["avif/lossy-420-bt709-full"] = (0, 0.0),
        ["avif/lossy-420-bt709-limited-67x65"] = (1, 0.0002), // one unit on a few samples
        ["avif/lossy-420-bt709-odd-12bit"] = (0, 0.0),
        ["avif/lossy-420-chroma-colocated"] = (0, 0.0),
        ["avif/lossy-420-chroma-vertical"] = (0, 0.0),
        ["avif/lossy-420-chromaticity-p3-limited-10bit"] = (0, 0.0),
        ["avif/lossy-420-fcc-limited-10bit"] = (0, 0.0),
        ["avif/lossy-420-ictcp-pq"] = (0, 0.0),
        ["avif/lossy-420-pasp-clli"] = (0, 0.0),
        ["avif/lossy-420-smpte2085"] = (0, 0.0),
        ["avif/lossy-420-smpte240m-full-odd"] = (0, 0.0),
        ["avif/lossy-420-unspecified"] = (0, 0.0),
        ["avif/lossy-420-ycgco-full-12bit"] = (0, 0.0),
        ["avif/lossy-422-alpha-10bit-limited"] = (0, 0.0),
        ["avif/lossy-422-bt2020-full-12bit"] = (0, 0.0),
        ["avif/lossy-422-bt601-1x5"] = (0, 0.0),
        ["avif/lossy-422-bt601-odd"] = (0, 0.0),
        ["avif/lossy-422-bt709-full-12bit"] = (0, 0.0),
        ["avif/lossy-422-bt709-limited"] = (0, 0.0),
        ["avif/lossy-422-bt709-limited-10bit"] = (0, 0.0),
        ["avif/lossy-422-fcc-full"] = (0, 0.0),
        ["avif/lossy-422-smpte240m-limited-10bit"] = (1, 0.0),
        ["avif/lossy-422-ycgco-full"] = (0, 0.0),
        ["avif/lossy-422-ycgco-full-10bit"] = (0, 0.0),
        ["avif/lossy-444-bt2020-full"] = (0, 0.0),
        ["avif/lossy-444-bt601-full"] = (0, 0.0),
        ["avif/lossy-444-chromaticity-bt2020"] = (0, 0.0),
        ["avif/lossy-444-unspecified-limited"] = (0, 0.0),
        ["avif/nclx-overrides-bitstream"] = (0, 0.0),
        ["avif/no-colr-bitstream-bt709"] = (0, 0.0),
        ["avif/no-colr-bt709-mdat-first"] = (0, 0.0),
        ["avif/no-colr-unspecified"] = (0, 0.0),
        ["avif/premultiplied-alpha-lossy-420"] = (0, 0.0),
        ["avif/sequence-10bit-once"] = (0, 0.0),
        ["avif/sequence-12-frames-420"] = (1, 0.0002), // one unit on a few samples
        ["avif/sequence-8-frames-420-10bit"] = (1, 0.0),
        ["avif/sequence-8-frames-422-key-frames"] = (0, 0.0),
        ["avif/sequence-8-frames-444-slow"] = (0, 0.0),
        ["avif/sequence-8-frames-alpha-lossy"] = (1, 0.0002), // one unit on a few samples
        ["avif/sequence-alpha-10bit"] = (1, 0.0),
        ["avif/sequence-alpha-repeat-2"] = (0, 0.0),
        ["avif/sequence-co64-stz2"] = (0, 0.0),
        ["avif/sequence-ffmpeg"] = (0, 0.0),
        ["avif/sequence-ffmpeg-svt"] = (1, 0.0002), // one unit on a few samples
        ["avif/sequence-hdr-boxes"] = (0, 0.0),
        ["avif/sequence-ictcp-hlg"] = (0, 0.0),
        ["avif/sequence-matrix-mirror-clap"] = (0, 0.0),
        ["avif/sequence-mdat-first"] = (0, 0.0),
        ["avif/sequence-no-edit-list"] = (0, 0.0),
        ["avif/sequence-premultiplied"] = (0, 0.0),
    };

    private static bool IsAvifFixture(Obj fixture) => (string?)fixture["format"] == "avif";

    public static int RunContainer(GeneratorOptions options)
    {
        var tools = new AvifFileTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-avif-container-", output => GenerateContainer(output, tools),
            (fixtures, files, bytes) => Inv($"{fixtures} AVIF fixtures; corpus: {files} files, {bytes} bytes"),
            "The committed AVIF fixtures are reproducible.");
    }

    private static int GenerateContainer(FullPath outDir, AvifFileTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<AvifFileTools>(outDir, tools, ContainerScriptPath))
        {
            var context = new ContainerContext(corpus);
            BuildStills(context);
            BuildLayouts(context);
            BuildDerivedImages(context);
            BuildSequences(context);
            BuildLayered(context);
            BuildInvalid(context);
            BuildUnsupported(context);
            BuildLimits(context);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsAvifFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsAvifFixture);
        var valid = fixtures.Where(f => (string)f["kind"]! == "valid").ToList();
        Console.WriteLine(Inv($"avif: {valid.Count} valid, {fixtures.Count - valid.Count} error fixtures"));
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The pinned tools of the container fixtures: the FFmpeg of the AV1 vectors (encoders, the two decoders,
    /// trace_headers) and libavif's avifenc and avifdec, all from the meziantou/prebuilt release.</summary>
    private sealed partial class AvifFileTools : ToolSet
    {
        public AvifFileTools(bool acceptVersions)
            : this(new AvifTools(acceptVersions), acceptVersions)
        {
        }

        private AvifFileTools(AvifTools av1, bool acceptVersions)
            : base(av1.Ffmpeg)
        {
            Av1 = av1;
            Avifenc = PrebuiltTools.Avifenc.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Avifdec = PrebuiltTools.Avifdec.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var encoder = LibavifVersionRegex().Match(Proc.RunText([Avifenc, "--version"]));
            var decoder = LibavifVersionRegex().Match(Proc.RunText([Avifdec, "--version"]));
            if (!encoder.Success || !decoder.Success || encoder.Groups["version"].Value != decoder.Groups["version"].Value)
                throw new FatalException("Cannot read one libavif version from avifenc and avifdec.");

            LibavifVersion = encoder.Groups["version"].Value;
            LibavifCodecs = encoder.Groups["codecs"].Value;
            if (LibavifVersion != PinnedLibavif && !acceptVersions)
                throw new FatalException($"libavif {LibavifVersion} differs from the pinned version {PinnedLibavif}. Review the impact and rerun with --accept-tool-versions (then update PinnedLibavif).");

            DisplayNames = new Dictionary<string, string>(av1.DisplayNames, StringComparer.Ordinal) { [Avifenc] = "avifenc", [Avifdec] = "avifdec" };
        }

        public AvifTools Av1 { get; }

        public string Avifenc { get; }

        public string Avifdec { get; }

        public string LibavifVersion { get; }

        /// <summary>The codecs avifenc reports: <c>dav1d [dec]:..., aom [enc]:...</c>.</summary>
        public string LibavifCodecs { get; }

        /// <summary>avifenc and the AV1 encoder it is linked with.</summary>
        public string AvifencLabel => $"libavif {LibavifVersion} avifenc ({Codec("aom [enc]")})";

        /// <summary>avifdec and the AV1 decoder it is linked with.</summary>
        public string AvifdecLabel => $"libavif {LibavifVersion} avifdec ({Codec("dav1d [dec]")})";

        /// <summary>The two AV1 decoders of the pinned FFmpeg, which decode every plane of every reference.</summary>
        public string DecodersLabel => $"{Av1.Dav1dLabel} + {Av1.LibaomLabel} ({FfmpegLabel})";

        // "dav1d [dec]:1.5.3-0-gb546257, aom [enc]:3.14.1" -> "aom 3.14.1"
        private string Codec(string name)
        {
            var entry = LibavifCodecs.Split(',', StringSplitOptions.TrimEntries).First(codec => codec.StartsWith(name + ":", StringComparison.Ordinal));
            return name.Split(' ')[0] + " " + entry[(name.Length + 1)..].Split('-')[0];
        }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        [GeneratedRegex(@"Version: (?<version>\d+\.\d+\.\d+) \((?<codecs>[^)]*)\)", RegexOptions.ExplicitCapture)]
        private static partial Regex LibavifVersionRegex();
    }

    /// <summary>The state of one generation: the corpus, the valid inputs by name and the reference files already written.</summary>
    private sealed class ContainerContext(Corpus<AvifFileTools> corpus)
    {
        public Corpus<AvifFileTools> Corpus { get; } = corpus;

        public AvifFileTools Tools => Corpus.Tools;

        /// <summary>The bytes of the valid inputs, by fixture name: the sources of the assembled files.</summary>
        public Dictionary<string, byte[]> Inputs { get; } = new(StringComparer.Ordinal);

        /// <summary>The manifest entries of the valid inputs, by fixture name (limit fixtures reuse their input).</summary>
        public Dictionary<string, Obj> InputEntries { get; } = new(StringComparer.Ordinal);

        /// <summary>The references of the valid inputs, by fixture name.</summary>
        public Dictionary<string, AvifReference> References { get; } = new(StringComparer.Ordinal);

        /// <summary>The avifdec cross-check of the valid inputs, by fixture name: a file that avifdec rejects is assembled
        /// around the payloads of one of them, whose measured cross-check still applies to the payloads.</summary>
        public Dictionary<string, Obj> AvifdecChecks { get; } = new(StringComparer.Ordinal);

        /// <summary>The reference buffers written so far, by layout, size and content hash: fixtures that present the same
        /// pixels (one coded payload in several containers) share their reference files.</summary>
        public Dictionary<string, Obj> Buffers { get; } = new(StringComparer.Ordinal);

        private int _scratchIndex;

        public FullPath NewScratch(string name)
        {
            var directory = Corpus.Scratch / Inv($"{_scratchIndex++:D3}-{name}");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The reference of an input: structure by ReferenceAvif, planes by two decoders, colors by ReferenceYuvConverter
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>One decoded image of an AV1 payload: the planes both decoders agree on, and what its headers signal.</summary>
    private sealed record PlaneImage(int Width, int Height, Av1ColorConfig Color, int ChromaSamplePosition, bool FullRange, (int Primaries, int Transfer, int Matrix)? ColorDescription, ushort[][] Planes);

    /// <summary>A decoded AV1 payload: its output frames and the facts of its headers the manifest records.</summary>
    /// <param name="OutputLayers">The spatial layer of each output frame.</param>
    /// <param name="LowerLayerPixels">The pixels of the shown frames that are not output: the lower spatial layers of the
    /// temporal units, each with its own size. They are decoded, so they count toward <c>MaxTotalPixels</c>.</param>
    private sealed record DecodedPayload(List<PlaneImage> Frames, bool HasFilmGrain, bool IsLossless, int HiddenFrames, bool HasInterFrames, List<(int Width, int Height)> CodedSizes, List<int> OutputLayers, long LowerLayerPixels);

    /// <summary>The spatial layers of the bitstream of a coded item or of a track, before any selection.</summary>
    /// <param name="Count">The number of spatial layers the bitstream holds.</param>
    /// <param name="IsScaled">Whether the shown frames of the layers have different sizes.</param>
    /// <param name="OperatingPoints">The <c>operating_point_idc</c> of each operating point of the sequence header.</param>
    private sealed record PayloadLayers(int Count, bool IsScaled, List<int> OperatingPoints);

    private sealed record Timing(uint SampleDelta, uint Timescale);

    /// <summary>Everything the manifest records about a valid input, derived from its bytes.</summary>
    private sealed class AvifReference
    {
        public required List<Img> Frames { get; init; }

        public required string PixelFormat { get; init; }

        public required string ColorModel { get; init; }

        public required int BitsPerComponent { get; init; }

        public required int Orientation { get; init; }

        public required ReferenceAvif.ColorDescription? Cicp { get; init; }

        /// <summary>The <c>clli</c> box of the primary item or of the sample entry, or <see langword="null"/>.</summary>
        public ReferenceAvif.ContentLightLevel? ContentLightLevel { get; init; }

        /// <summary>The <c>mdcv</c> box of the primary item or of the sample entry, or <see langword="null"/>.</summary>
        public ReferenceAvif.MasteringDisplay? MasteringDisplay { get; init; }

        /// <summary>The <c>pasp</c> box of the primary item or of the sample entry, or <see langword="null"/>.</summary>
        public ReferenceAvif.PixelAspectRatio? PixelAspectRatio { get; init; }

        public required byte[]? Icc { get; init; }

        public required byte[]? Exif { get; init; }

        public required byte[]? Xmp { get; init; }

        public required bool IsMatrixed { get; init; }

        public required List<Timing> Timings { get; init; }

        public required bool IsSequence { get; init; }

        public required ReferenceAvif.Track? ColorTrack { get; init; }

        public required SortedSet<string> Features { get; init; }

        public required int HiddenFrames { get; init; }

        /// <summary>The pixels of the lower spatial layers that are decoded and not returned, for every coded item or sample.</summary>
        public required long LowerLayerPixels { get; init; }

        /// <summary>Whether the transformations are associated in the order of MIAF (clap, irot, imir), which libavif assumes.</summary>
        public bool HasMiafTransformOrder { get; init; } = true;

        /// <summary>Whether a tool that applies the transformations of the file shows other pixels than the stored ones.</summary>
        public bool IsTransformed => Orientation != 1;
    }

    /// <summary>Writes an AV1 payload as a low-overhead OBU stream that starts with a temporal delimiter (libavif keeps the
    /// delimiter in the item data and in the samples; a payload without one gets one).</summary>
    private static byte[] ToTemporalUnit(byte[] payload) => payload.Length >= 2 && payload[0] == 0x12 && payload[1] == 0x00 ? payload : [0x12, 0x00, .. payload];

    /// <summary>Decodes an OBU stream with both decoders (same bytes or failure) and reads its headers with trace_headers.</summary>
    private static DecodedPayload DecodePayload(AvifFileTools tools, FullPath scratch, string name, byte[] stream)
    {
        var path = scratch / (name + ".obu");
        File.WriteAllBytes(path, stream);
        var trace = ParseTrace(Proc.RunText(TraceCommand(tools.Av1, path.Value), stderr: true));
        var color = ReadColorConfig(trace.Sequence);
        var outputs = trace.Frames.Where(f => f.IsOutput).Select(f => f.Size).ToList();
        List<byte[]>? reference = null;
        foreach (var decoder in new[] { Dav1d, Libaom })
        {
            var decoded = Decode(tools.Av1, decoder, path, color.PixelFormat);
            if (decoded.Count != outputs.Count)
                throw new ToolException(Inv($"{name}: {decoder} produced {decoded.Count} frames, the headers describe {outputs.Count}."));

            for (var i = 0; i < decoded.Count; i++)
            {
                var frameLength = PlaneSizes(color, outputs[i]).Sum(plane => plane.Width * plane.Height * color.BytesPerSample);
                if (decoded[i].Length != frameLength)
                    throw new ToolException(Inv($"{name}: {decoder} produced {decoded[i].Length} bytes for frame {i}, not {frameLength}."));
                if (reference is not null && !reference[i].AsSpan().SequenceEqual(decoded[i]))
                    throw new ToolException(Inv($"{name}: {Dav1d} and {Libaom} decode different samples (frame {i}); no reference is recorded from a single decoder."));
            }

            reference ??= decoded;
        }

        var sequence = trace.Sequence;
        (int, int, int)? description = sequence.Get("color_description_present_flag") == 1
            ? ((int)sequence.Get("color_primaries"), (int)sequence.Get("transfer_characteristics"), (int)sequence.Get("matrix_coefficients"))
            : null;

        // color_range is not coded for sRGB identity content, which is full range (section 5.5.2)
        var fullRange = description is (1, 13, 0) || sequence.Get("color_range") == 1;
        var frames = new List<PlaneImage>();
        for (var i = 0; i < outputs.Count; i++)
        {
            var planes = new List<ushort[]>();
            var offset = 0;
            foreach (var (width, height) in PlaneSizes(color, outputs[i]))
            {
                var samples = new ushort[width * height];
                for (var s = 0; s < samples.Length; s++)
                {
                    samples[s] = color.BytesPerSample == 1 ? reference![i][offset + s] : (ushort)(reference![i][offset + (2 * s)] | (reference[i][offset + (2 * s) + 1] << 8));
                }

                planes.Add(samples);
                offset += samples.Length * color.BytesPerSample;
            }

            frames.Add(new PlaneImage(outputs[i].Width, outputs[i].Height, color, (int)sequence.Get("chroma_sample_position"), fullRange, description, [.. planes]));
        }

        var coded = trace.Frames.Where(f => !f.ShowsExistingFrame).ToList();
        return new DecodedPayload(
            frames,
            HasFilmGrain: coded.Any(f => f.Header.Get("apply_grain") == 1),
            IsLossless: coded.All(f => IsCodedLossless(f.Header)),
            HiddenFrames: coded.Count(f => f.Header.Get("show_frame", absent: 1) == 0),
            HasInterFrames: coded.Any(f => f.Header.Get("frame_type") != 0),
            CodedSizes: [.. coded.Select(f => f.Size)],
            OutputLayers: [.. trace.Frames.Where(f => f.IsOutput).Select(f => f.SpatialId)],
            LowerLayerPixels: trace.Frames.Where(f => f.IsShown && !f.IsOutput).Sum(f => (long)f.Size.Width * f.Size.Height));
    }

    /// <summary>The spatial layers of a payload as trace_headers reads them, before the selection of an operating point
    /// or of a layer.</summary>
    private static PayloadLayers ReadPayloadLayers(AvifFileTools tools, FullPath scratch, string name, byte[] stream)
    {
        var path = scratch / (name + ".layers.obu");
        File.WriteAllBytes(path, stream);
        var trace = ParseTrace(Proc.RunText(TraceCommand(tools.Av1, path.Value), stderr: true));
        var shown = trace.Frames.Where(f => f.IsShown).ToList();
        var operatingPoints = Enumerable.Range(0, (int)trace.Sequence.Get("operating_points_cnt_minus_1") + 1).Select(index => (int)trace.Sequence.Get(Inv($"operating_point_idc[{index}]"))).ToList();
        return new PayloadLayers(shown.Select(f => f.SpatialId).Distinct().Count(), shown.GroupBy(f => f.TemporalUnit).Any(unit => unit.Select(f => f.Size).Distinct().Count() > 1), operatingPoints);
    }

    /// <summary>
    /// Decodes the bitstream of a coded item as its properties select it: the <c>a1op</c> property names an operating point
    /// of the sequence header (AV1 Image File Format, section 2.3.2.1; the first one otherwise), whose
    /// <c>operating_point_idc</c> tells which units a decoder drops (AV1 specification, section 5.3.1), and the
    /// <c>lsel</c> property a spatial layer whose frame is the image (ISO/IEC 23008-12, section 6.5.11; 0xFFFF and no
    /// property: the highest layer). The units that are dropped are removed here, from the specification, and what is left
    /// is decoded by both decoders like any stream: they output the frame of the highest layer that the temporal unit
    /// still holds. A valid fixture selects an operating point and a layer that the bitstream has.
    /// </summary>
    private static (DecodedPayload Payload, PayloadLayers Layers) DecodeCodedItem(AvifFileTools tools, FullPath scratch, ReferenceAvif file, ReferenceAvif.Item item, string role)
    {
        var stream = ToTemporalUnit(file.ReadItemData(item));
        var layers = ReadPayloadLayers(tools, scratch, role, stream);
        var operatingPoint = item.OperatingPoint ?? 0;
        var selector = item.LayerSelector ?? 0xFFFF;
        if (operatingPoint != 0 || selector != 0xFFFF)
        {
            if (operatingPoint >= layers.OperatingPoints.Count)
                throw new ToolException(Inv($"{role}: the a1op property selects operating point {operatingPoint} of {layers.OperatingPoints.Count}."));
            if (selector is > 3 and not 0xFFFF)
                throw new ToolException(Inv($"{role}: the lsel property selects layer {selector}; AV1 has 4 spatial layers."));

            stream = SelectObus(stream, layers.OperatingPoints[operatingPoint], selector == 0xFFFF ? 3 : selector);
        }

        var payload = DecodePayload(tools, scratch, role, stream);
        if (payload.Frames.Count != 1)
            throw new ToolException(Inv($"{role}: the item decodes to {payload.Frames.Count} frames."));
        if (selector != 0xFFFF && payload.OutputLayers[0] != selector)
            throw new ToolException(Inv($"{role}: the lsel property selects layer {selector}, which the operating point of the bitstream does not have (its highest layer is {payload.OutputLayers[0]})."));

        return (payload, layers);
    }

    /// <summary>Decodes an image item: a coded item, or a grid whose tiles are placed in row-major order and cropped to
    /// the size of the grid (ISO/IEC 23008-12, section 6.6.2.3). The planes are tiled before the color conversion.</summary>
    private static (PlaneImage Image, List<DecodedPayload> Payloads, string? Grid, List<PayloadLayers> Layers) DecodeItem(AvifFileTools tools, FullPath scratch, ReferenceAvif file, ReferenceAvif.Item item, string role)
    {
        if (item.Type == "av01")
        {
            var (payload, layers) = DecodeCodedItem(tools, scratch, file, item, role);
            return (payload.Frames[0], [payload], null, [layers]);
        }

        Py.Assert(item.Type == "grid", item.Type);
        // The operating point and the layer are those of each tile: a grid item that selects one is not a valid fixture
        Py.Assert(item.LayerSelector is null or 0xFFFF && item.OperatingPoint is null or 0, "a grid item selects a layer or an operating point");
        var (rows, columns, width, height) = file.ReadGridDescriptor(item);
        var tiles = file.GetGridTiles(item);
        Py.Assert(tiles.Count == rows * columns, "grid tiles");
        var decodedTiles = tiles.Select((tile, index) => DecodeCodedItem(tools, scratch, file, tile, Inv($"{role}-tile{index}"))).ToList();
        var decoded = decodedTiles.Select(tile => tile.Payload).ToList();
        var first = decoded[0].Frames[0];
        var color = first.Color;
        Py.Assert(decoded.All(d => d.Frames[0].Width == first.Width && d.Frames[0].Height == first.Height && d.Frames[0].Color == color), "grid tiles differ");
        Py.Assert(width <= (long)columns * first.Width && height <= (long)rows * first.Height && width > (long)(columns - 1) * first.Width && height > (long)(rows - 1) * first.Height, "grid size");
        var planes = new List<ushort[]>();
        var size = ((int)width, (int)height);
        var planeSizes = PlaneSizes(color, size);
        var tileSizes = PlaneSizes(color, (first.Width, first.Height));
        for (var plane = 0; plane < planeSizes.Count; plane++)
        {
            var (planeWidth, planeHeight) = planeSizes[plane];
            var (tileWidth, tileHeight) = tileSizes[plane];
            var samples = new ushort[planeWidth * planeHeight];
            for (var y = 0; y < planeHeight; y++)
            {
                for (var x = 0; x < planeWidth; x++)
                {
                    var tile = decoded[(y / tileHeight * columns) + (x / tileWidth)].Frames[0];
                    samples[(y * planeWidth) + x] = tile.Planes[plane][(y % tileHeight * tileWidth) + (x % tileWidth)];
                }
            }

            planes.Add(samples);
        }

        return (first with { Width = (int)width, Height = (int)height, Planes = [.. planes] }, decoded, Inv($"{columns}x{rows}"), [.. decodedTiles.Select(tile => tile.Layers)]);
    }

    /// <summary>Converts the planes of a color image and of its alpha plane to a frame, then keeps a rectangle of it.</summary>
    private static Img ToFrame(PlaneImage color, PlaneImage? alpha, int matrix, bool fullRange, (int X, int Y, int Width, int Height) crop, bool isPremultiplied = false, int primaries = 2, int transfer = 2)
    {
        var deep = color.Color.BitDepth > 8 || alpha?.Color.BitDepth > 8;
        var maximum = deep ? 65535 : 255;
        var (width, height) = (color.Width, color.Height);
        int[] rgb;
        if (color.Color.Monochrome)
        {
            rgb = new int[3 * width * height];
            for (var i = 0; i < width * height; i++)
            {
                rgb[3 * i] = rgb[(3 * i) + 1] = rgb[(3 * i) + 2] = ReferenceYuvConverter.ScaleSample(color.Planes[0][i], color.Color.BitDepth, fullRange, maximum);
            }
        }
        else
        {
            rgb = ReferenceYuvConverter.ConvertToRgb(color.Planes[0], color.Planes[1], color.Planes[2], width, height, color.Color.BitDepth, color.Color.SubsamplingX, color.Color.SubsamplingY,
                color.ChromaSamplePosition, matrix, fullRange, maximum, primaries, transfer);
        }

        if (alpha is not null)
            Py.Assert(alpha.Width == width && alpha.Height == height && alpha.Color.Monochrome, "alpha plane");

        var model = alpha is not null ? "rgba" : color.Color.Monochrome ? "gray" : deep ? "rgba" : "rgb";
        var pixels = new List<Px>();
        for (var y = crop.Y; y < crop.Y + crop.Height; y++)
        {
            for (var x = crop.X; x < crop.X + crop.Width; x++)
            {
                var i = (y * width) + x;
                var a = alpha is null ? maximum : ReferenceYuvConverter.ScaleSample(alpha.Planes[0][i], alpha.Color.BitDepth, alpha.FullRange, maximum);
                if (isPremultiplied && alpha is not null)
                {
                    // The colors are divided by the alpha once they are converted
                    for (var channel = 0; channel < 3; channel++)
                    {
                        rgb[(3 * i) + channel] = ReferenceYuvConverter.Unpremultiply(rgb[(3 * i) + channel], a, maximum);
                    }
                }

                pixels.Add(model switch
                {
                    "gray" => new Px(rgb[3 * i]),
                    "rgb" => new Px(rgb[3 * i], rgb[(3 * i) + 1], rgb[(3 * i) + 2]),
                    _ => new Px(rgb[3 * i], rgb[(3 * i) + 1], rgb[(3 * i) + 2], a),
                });
            }
        }

        return new Img(crop.Width, crop.Height, model, deep ? 16 : 8, pixels);
    }

    /// <summary>Derives the reference of an input from its bytes.</summary>
    private static AvifReference Describe(AvifFileTools tools, FullPath scratch, byte[] data)
    {
        var file = ReferenceAvif.Parse(data);
        var features = new SortedSet<string>(StringComparer.Ordinal) { "avif.majorBrand=" + file.MajorBrand };
        Py.Assert(file.HasBrand("avif") || file.HasBrand("avis"), "brands");
        DescribeLayout(file, data, features);

        // A file with the brand of the image sequences presents its sequence; any other file its primary item
        return file.IsSequence ? DescribeSequence(tools, scratch, file, features) : DescribeStill(tools, scratch, file, features);
    }

    private static void DescribeLayout(ReferenceAvif file, byte[] data, SortedSet<string> features)
    {
        var boxes = file.TopLevelBoxes;
        var index = boxes.Select((box, i) => (box, i)).First(pair => pair.box.Type == (file.IsSequence ? "moov" : "meta")).i;
        var media = boxes.Select((box, i) => (box, i)).Where(pair => pair.box.Type == "mdat").Select(pair => pair.i).ToList();
        if (media.Any(i => i < index))
            features.Add("avif.layout=media-first");

        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var box in boxes)
        {
            if (box.HasLargeSize)
                kinds.Add("large-size");
            if (box.ExtendsToEnd)
                kinds.Add("to-end");
            if (box.Type is "free" or "skip")
                kinds.Add("free");
            else if (box.Type is not ("ftyp" or "meta" or "moov" or "mdat"))
                kinds.Add("unknown");
        }

        if (kinds.Count > 0)
            features.Add("avif.boxes=" + string.Join(',', kinds));

        // The bytes a forward-only reader never needs: what follows the last byte of the presented items or samples
        long last;
        if (file.IsSequence)
        {
            var tracks = new[] { file.ColorTrack, file.AlphaTrack }.OfType<ReferenceAvif.Track>();
            last = tracks.SelectMany(track => track.Samples).Max(sample => sample.Offset + sample.Size);
            last = Math.Max(last, boxes[index].Offset + boxes[index].Size);
            var (exif, xmp) = file.FindSequenceMetadata();
            foreach (var item in new[] { exif, xmp }.OfType<ReferenceAvif.Item>().Where(item => item.ConstructionMethod == 0))
            {
                last = Math.Max(last, item.Extents.Max(extent => extent.Offset + extent.Length));
            }
        }
        else
        {
            last = boxes[index].Offset + boxes[index].Size;
            foreach (var item in PresentedItems(file).Where(item => item.ConstructionMethod == 0))
            {
                last = Math.Max(last, item.Extents.Max(extent => extent.Offset + extent.Length));
            }
        }

        if (last < data.Length)
            features.Add("avif.trailingData");
    }

    /// <summary>The items whose data a decoder of the primary item reads: the coded items of the image and of its alpha
    /// plane, the grid descriptors, and the Exif and XMP items.</summary>
    private static List<ReferenceAvif.Item> PresentedItems(ReferenceAvif file)
    {
        var result = new List<ReferenceAvif.Item>();
        void AddImage(ReferenceAvif.Item? item)
        {
            if (item is null)
                return;

            result.Add(item);
            if (item.Type == "grid")
                result.AddRange(file.GetGridTiles(item));
        }

        // An identity derivation has no data: what is read is the coded image it presents
        var primary = file.PrimaryItem!;
        AddImage(file.GetIdentityChain(primary)[^1]);
        AddImage(file.AlphaItem is { } alphaItem ? file.GetIdentityChain(alphaItem)[^1] : null);
        result.AddRange(file.FindDescriptions(primary.Id).Where(item => item.Type == "Exif" || item.ContentType == "application/rdf+xml"));
        return result;
    }

    private static AvifReference DescribeStill(AvifFileTools tools, FullPath scratch, ReferenceAvif file, SortedSet<string> features)
    {
        var primary = file.PrimaryItem ?? throw new ToolException("the file has no primary item.");

        // An identity derivation (iden) presents its input image with its own transformations after those of the input:
        // the coded image is the last item of the chain, and its samples go through the transformations of every item
        var chain = file.GetIdentityChain(primary);
        var coded = chain[^1];
        var transforms = ReferenceAvif.GetTransforms(chain);
        if (chain.Count > 1)
            features.Add(Inv($"avif.iden={chain.Count - 1}"));

        var (color, payloads, grid, layers) = DecodeItem(tools, scratch, file, coded, "color");
        var size = coded.Size ?? throw new ToolException("the coded image has no ispe property.");
        Py.Assert(size == (color.Width, color.Height), "ispe does not match the bitstream");
        CheckConfiguration(grid is null ? coded : file.GetGridTiles(coded)[0], color);
        for (var i = chain.Count - 2; i >= 0; i--)
        {
            // The size of a derived image is the size of its input, as the input is presented
            var input = ReferenceAvif.Item.Present(ReferenceAvif.GetTransforms([.. chain.Skip(i + 1)]), color.Width, color.Height) ?? throw new ToolException("the clean aperture is not a whole number of samples.");
            Py.Assert(chain[i].Size == (input.Width, input.Height), "the ispe of an identity derivation is the size of its input");
        }

        PlaneImage? alpha = null;
        if (file.AlphaItem is { } alphaItem)
        {
            var alphaChain = file.GetIdentityChain(alphaItem);
            (alpha, var alphaPayloads, _, _) = DecodeItem(tools, scratch, file, alphaChain[^1], "alpha");

            // The alpha plane is presented as the color image; the alpha plane of a coded image may also declare no
            // transformation, in which case those of the coded image apply to it
            var alphaTransforms = ReferenceAvif.GetTransforms(alphaChain);
            Py.Assert((alphaTransforms.Count == 0 && chain.Count == 1)
                || (ReferenceAvif.Item.Present(alphaTransforms, alpha.Width, alpha.Height)?.Rectangle == ReferenceAvif.Item.Present(transforms, color.Width, color.Height)?.Rectangle && ReferenceAvif.ComposeOrientation(alphaTransforms) == ReferenceAvif.ComposeOrientation(transforms)),
                "the alpha plane is presented as the color image");
            payloads.AddRange(alphaPayloads);
            features.Add("avif.alpha");
            if (file.IsPremultiplied)
                features.Add("avif.premultiplied");
        }

        // The properties that describe the image are those of the primary item, then those of the image it is derived from
        var nclx = chain.Select(item => item.Nclx).FirstOrDefault(value => value is not null);
        var (matrix, fullRange, cicp) = ResolveColor(nclx, color);
        // The transformations apply in the order of their association: the samples that they present are a rectangle of
        // the coded image, which stays as it is coded (the rotation and the mirroring are the orientation of the image)
        var crop = ReferenceAvif.Item.Present(transforms, color.Width, color.Height)?.Rectangle ?? throw new ToolException("the clean aperture is not a whole number of samples.");
        if (transforms.Any(transform => transform.Type == "clap"))
            features.Add("avif.clap");

        foreach (var transform in transforms.Where(t => t.Type is "irot" or "imir"))
        {
            features.Add(Inv($"avif.{transform.Type}={transform.Payload.Span[0] & 3}"));
        }

        // MIAF orders the transformations clap, irot, imir. HEIF applies the properties in the order of their association,
        // which is what a file that does otherwise gets (libavif applies them in the MIAF order whatever the file says)
        var hasMiafTransformOrder = chain.All(item => item.HasMiafTransformOrder);
        if (!hasMiafTransformOrder)
            features.Add("avif.transformOrder=association");

        if (grid is not null)
        {
            features.Add("avif.grid=" + grid);

            // libavif repeats the transformations of a grid on its tiles; they apply once, to the assembled image, and a
            // tile that declares other ones is not a valid fixture
            var tiles = file.GetGridTiles(coded);
            if (tiles.Any(tile => tile.Transforms.Count > 0))
            {
                Py.Assert(tiles.All(tile => tile.Transforms.Select(t => (t.Type, Convert.ToHexString(t.Payload.Span))).SequenceEqual(coded.Transforms.Select(t => (t.Type, Convert.ToHexString(t.Payload.Span))))), "the tiles of a grid repeat its transformations or have none");
                features.Add("avif.gridTileTransforms");
            }
        }

        var presented = PresentedItems(file);
        AddLayerFeatures(features, layers, payloads, presented.Where(item => item.Type == "av01").ToList());
        if (presented.Any(item => item.ConstructionMethod == 1))
            features.Add("avif.idat");
        if (presented.Any(item => item.Extents.Count > 1))
            features.Add(Inv($"avif.extents={presented.Max(item => item.Extents.Count)}"));

        var descriptions = file.FindDescriptions(primary.Id);
        var exifItem = descriptions.FirstOrDefault(item => item.Type == "Exif");
        var xmpItem = descriptions.FirstOrDefault(item => item.ContentType == "application/rdf+xml");
        var icc = chain.Select(item => item.IccProfile).FirstOrDefault(value => value is not null)?.ToArray();
        var lightLevel = chain.Select(item => item.ContentLightLevel).FirstOrDefault(value => value is not null);
        var masteringDisplay = chain.Select(item => item.MasteringDisplay).FirstOrDefault(value => value is not null);
        var pixelAspectRatio = chain.Select(item => item.PixelAspectRatio).FirstOrDefault(value => value is not null);
        AddColorFeatures(features, color, alpha, payloads, matrix, fullRange, nclx is not null, icc is not null);
        var metadata = new List<string>();
        if (exifItem is not null)
            metadata.Add("exif");
        if (xmpItem is not null)
            metadata.Add("xmp");
        if (metadata.Count > 0)
            features.Add("avif.metadata=" + string.Join(',', metadata));

        AddPresentationFeatures(features, lightLevel, masteringDisplay, pixelAspectRatio);
        return new AvifReference
        {
            ContentLightLevel = lightLevel,
            MasteringDisplay = masteringDisplay,
            PixelAspectRatio = pixelAspectRatio,
            HasMiafTransformOrder = hasMiafTransformOrder,
            Frames = [ToFrame(color, alpha, matrix, fullRange, crop, file.IsPremultiplied, cicp?.ColorPrimaries ?? 2, cicp?.TransferCharacteristics ?? 2)],
            PixelFormat = PixelFormatOf(color, alpha),
            ColorModel = ColorModelOf(color, alpha is not null, matrix),
            BitsPerComponent = color.Color.BitDepth,
            Orientation = ReferenceAvif.ComposeOrientation(transforms),
            Cicp = cicp,
            Icc = icc,
            Exif = exifItem is null ? null : file.ReadExifPayload(exifItem),
            Xmp = xmpItem is null ? null : file.ReadItemData(xmpItem),
            IsMatrixed = !color.Color.Monochrome && matrix != 0,
            Timings = [],
            IsSequence = false,
            ColorTrack = null,
            Features = features,
            HiddenFrames = payloads.Sum(payload => payload.HiddenFrames),
            LowerLayerPixels = payloads.Sum(payload => payload.LowerLayerPixels),
        };
    }

    /// <summary>The boxes that describe how the samples are presented: labels, which change no pixel.</summary>
    private static void AddPresentationFeatures(SortedSet<string> features, ReferenceAvif.ContentLightLevel? lightLevel, ReferenceAvif.MasteringDisplay? display, ReferenceAvif.PixelAspectRatio? pixelAspectRatio)
    {
        if (lightLevel is not null)
            features.Add("avif.clli");
        if (display is not null)
            features.Add("avif.mdcv");
        if (pixelAspectRatio is not null)
        {
            Py.Assert(pixelAspectRatio.HorizontalSpacing > 0 && pixelAspectRatio.VerticalSpacing > 0, "the terms of the pixel aspect ratio are positive");
            features.Add("avif.pasp");
        }
    }

    /// <summary>
    /// The features of a layered image: the spatial layers of the bitstream of its color image (the most of its coded
    /// items), whether their sizes differ, the properties that select an operating point or a layer, and the pixels of the
    /// lower layers, which are decoded without being returned.
    /// </summary>
    private static void AddLayerFeatures(SortedSet<string> features, List<PayloadLayers> colorLayers, List<DecodedPayload> payloads, List<ReferenceAvif.Item> codedItems)
    {
        var count = colorLayers.Max(layers => layers.Count);
        if (count > 1)
            features.Add(Inv($"avif.layers={count}"));
        if (colorLayers.Any(layers => layers.IsScaled))
            features.Add("avif.layers.scaled");
        if (codedItems.Any(item => item.LayerSizes is not null))
            features.Add("avif.a1lx");

        var selectors = codedItems.Where(item => item.LayerSelector is not null).Select(item => item.LayerSelector!.Value).Distinct().Order().ToList();
        if (selectors.Count > 0)
            features.Add("avif.lsel=" + string.Join(',', selectors.Select(selector => Inv($"{selector}"))));

        var operatingPoints = codedItems.Where(item => item.OperatingPoint is not null).Select(item => item.OperatingPoint!.Value).Distinct().Order().ToList();
        if (operatingPoints.Count > 0)
            features.Add("avif.a1op=" + string.Join(',', operatingPoints.Select(index => Inv($"{index}"))));

        var lowerLayerPixels = payloads.Sum(payload => payload.LowerLayerPixels);
        if (lowerLayerPixels > 0)
            features.Add(Inv($"avif.layers.hiddenPixels={lowerLayerPixels}"));
    }

    private static AvifReference DescribeSequence(AvifFileTools tools, FullPath scratch, ReferenceAvif file, SortedSet<string> features)
    {
        var track = file.ColorTrack ?? throw new ToolException("the file has no track of AV1 images.");
        features.Add("avif.sequence");
        byte[] colorStream = [.. track.Samples.SelectMany(sample => ToTemporalUnit(file.ReadSample(sample)))];
        var payload = DecodePayload(tools, scratch, "color", colorStream);
        Py.Assert(payload.Frames.Count == track.Samples.Count, "one image per sample");
        Py.Assert(payload.Frames.All(frame => frame.Width == track.Width && frame.Height == track.Height), "the images have the size of the sample entry");
        DecodedPayload? alphaPayload = null;
        if (file.AlphaTrack is { } alphaTrack)
        {
            alphaPayload = DecodePayload(tools, scratch, "alpha", [.. alphaTrack.Samples.SelectMany(sample => ToTemporalUnit(file.ReadSample(sample)))]);
            Py.Assert(alphaPayload.Frames.Count == payload.Frames.Count, "one alpha plane per image");
            features.Add("avif.alpha");
            if (file.IsPremultiplied)
                features.Add("avif.premultiplied");
        }

        var first = payload.Frames[0];
        var (matrix, fullRange, cicp) = ResolveColor(track.Nclx, first);
        var icc = track.IccProfile?.ToArray();
        AddColorFeatures(features, first, alphaPayload?.Frames[0], alphaPayload is null ? [payload] : [payload, alphaPayload], matrix, fullRange, track.Nclx is not null, icc is not null);
        features.Add("avif.seq.editList=" + (track.EditList is not { } edits ? "none" : !edits.IsRepeating ? "once" : track.Duration is null ? "repeat-indefinite" : "repeat"));
        features.Add("avif.seq.sampleSizes=" + track.SampleSizeBox);
        features.Add("avif.seq.chunkOffsets=" + track.ChunkOffsetBox);
        if (payload.HasInterFrames)
            features.Add("avif.seq.interFrames");

        // Samples that are layered temporal units: every sample gives the frame of its highest spatial layer
        AddLayerFeatures(features, [ReadPayloadLayers(tools, scratch, "color", colorStream)], alphaPayload is null ? [payload] : [payload, alphaPayload], []);

        // Frames that are decoded without being shown (the alternate reference frames libaom codes with a lookahead; a
        // later sample then only shows one of them again): they count toward MaxTotalPixels, so their number is recorded,
        // for the color and the alpha tracks together
        var hiddenFrames = payload.HiddenFrames + (alphaPayload?.HiddenFrames ?? 0);
        if (hiddenFrames > 0)
        {
            Py.Assert(payload.CodedSizes.Concat(alphaPayload?.CodedSizes ?? []).All(size => size == (track.Width, track.Height)), "the hidden frames have the size of the images");
            features.Add(Inv($"avif.seq.hiddenFrames={hiddenFrames}"));
        }

        // The Exif and XMP items of the sequence: those of the meta box of the track, or else of the meta box of the file
        // when it precedes the moov box (ReferenceAvif.FindSequenceMetadata)
        var (exifItem, xmpItem) = file.FindSequenceMetadata();
        var exif = exifItem is null ? null : file.ReadExifPayload(exifItem);
        var xmp = xmpItem is null ? null : file.ReadItemData(xmpItem);
        var metadataItems = new[] { exifItem, xmpItem }.OfType<ReferenceAvif.Item>().ToList();
        var metadata = new List<string>();
        if (exifItem is not null)
            metadata.Add("exif");
        if (xmpItem is not null)
            metadata.Add("xmp");
        if (metadata.Count > 0)
        {
            features.Add("avif.metadata=" + string.Join(',', metadata));
            features.Add("avif.seq.metadataSource=" + (track.HasMeta ? "track" : "file"));
            if (metadataItems.Any(item => item.ConstructionMethod == 1))
                features.Add("avif.idat");

            // Item data that follow the samples: the last image is only complete with them
            var lastSample = track.Samples.Max(sample => sample.Offset);
            if (metadataItems.Any(item => item.ConstructionMethod == 0 && item.Extents.Any(extent => extent.Offset > lastSample)))
                features.Add("avif.seq.metadataAfterSamples");
        }

        // Exif and XMP items of the meta box of the file that are not the metadata of the sequence: the track has its
        // own meta box, or the meta box of the file follows the moov box
        var fileMetadata = file.Items.Where(item => item.Type == "Exif" || (item.Type == "mime" && item.ContentType == "application/rdf+xml")).ToList();
        if (fileMetadata.Any(item => !metadataItems.Any(used => file.ReadItemData(used).AsSpan().SequenceEqual(file.ReadItemData(item)))))
            features.Add("avif.seq.fileMetadataNotRead");

        // The chunks of a track may be anywhere in the file: here the first image is not the first one in the file
        if (track.Samples.Skip(1).Any(sample => sample.Offset < track.Samples[0].Offset))
            features.Add("avif.seq.samplesOutOfOrder");

        // The clean aperture of the sample entry is the part of the samples that is shown, and the matrix of the track header
        // the orientation of the images, which stay as they are coded. The alpha track has the same ones or none
        var crop = (0, 0, track.Width, track.Height);
        if (track.CleanAperture is { } aperture)
        {
            crop = aperture.GetRectangle(track.Width, track.Height) ?? throw new ToolException("the clean aperture is not a whole number of samples.");
            features.Add("avif.seq.clap");
        }

        if (track.Orientation != 1)
            features.Add(Inv($"avif.seq.matrix={track.Orientation}"));

        if (file.AlphaTrack is { } transformedAlpha)
        {
            Py.Assert(transformedAlpha.CleanAperture is null || transformedAlpha.CleanAperture.GetRectangle(track.Width, track.Height) == crop, "the alpha track has the clean aperture of the color track or none");
            Py.Assert(transformedAlpha.Orientation == 1 || transformedAlpha.Orientation == track.Orientation, "the alpha track has the matrix of the color track or none");
        }

        var frames = payload.Frames.Select((frame, i) => ToFrame(frame, alphaPayload?.Frames[i], matrix, fullRange, crop, file.IsPremultiplied, cicp?.ColorPrimaries ?? 2, cicp?.TransferCharacteristics ?? 2)).ToList();
        AddPresentationFeatures(features, track.ContentLightLevel, track.MasteringDisplay, track.PixelAspectRatio);
        return new AvifReference
        {
            ContentLightLevel = track.ContentLightLevel,
            MasteringDisplay = track.MasteringDisplay,
            PixelAspectRatio = track.PixelAspectRatio,
            Frames = frames,
            PixelFormat = PixelFormatOf(first, alphaPayload?.Frames[0]),
            ColorModel = ColorModelOf(first, alphaPayload is not null, matrix),
            BitsPerComponent = first.Color.BitDepth,
            Orientation = track.Orientation,
            Cicp = cicp,
            Icc = icc,
            Exif = exif,
            Xmp = xmp,
            IsMatrixed = !first.Color.Monochrome && matrix != 0,
            Timings = [.. track.Samples.Select(sample => new Timing(sample.Duration, track.Timescale))],
            IsSequence = true,
            ColorTrack = track,
            Features = features,
            HiddenFrames = payload.HiddenFrames + (alphaPayload?.HiddenFrames ?? 0),
            LowerLayerPixels = payload.LowerLayerPixels + (alphaPayload?.LowerLayerPixels ?? 0),
        };
    }

    /// <summary>The colr box of type nclx describes the colors; otherwise the bitstream does (unspecified matrix
    /// coefficients are BT.601 in the conversion; the description stays what the file says, or none).</summary>
    private static (int Matrix, bool FullRange, ReferenceAvif.ColorDescription? Cicp) ResolveColor(ReferenceAvif.ColorDescription? nclx, PlaneImage color)
    {
        if (nclx is not null)
            return (nclx.MatrixCoefficients, nclx.FullRange, nclx);

        return color.ColorDescription is var (primaries, transfer, matrix)
            ? (matrix, color.FullRange, new ReferenceAvif.ColorDescription(primaries, transfer, matrix, color.FullRange))
            : (2, color.FullRange, null);
    }

    private static void CheckConfiguration(ReferenceAvif.Item coded, PlaneImage image)
    {
        var configuration = coded.Configuration ?? throw new ToolException("a coded item has no av1C property.");
        Py.Assert(configuration.BitDepth == image.Color.BitDepth && configuration.IsMonochrome == image.Color.Monochrome
            && (configuration.SubsamplingX, configuration.SubsamplingY) == (image.Color.SubsamplingX, image.Color.SubsamplingY), "av1C does not match the bitstream");
    }

    private static void AddColorFeatures(SortedSet<string> features, PlaneImage color, PlaneImage? alpha, List<DecodedPayload> payloads, int matrix, bool fullRange, bool hasNclx, bool hasIcc)
    {
        features.Add(Inv($"avif.bitDepth={color.Color.BitDepth}"));
        features.Add("avif.subsampling=" + color.Color.Subsampling);
        if (!color.Color.Monochrome)
            features.Add(Inv($"avif.matrix={matrix}"));
        features.Add("avif.range=" + (fullRange ? "full" : "limited"));
        var colr = new List<string>();
        if (hasIcc)
            colr.Add("icc");
        if (hasNclx)
            colr.Add("nclx");
        features.Add("avif.colr=" + (colr.Count == 0 ? "none" : string.Join(',', colr)));
        if (color.Color is { SubsamplingX: 1, SubsamplingY: 1, Monochrome: false } && color.ChromaSamplePosition != 0)
            features.Add("avif.chromaSamplePosition=" + (color.ChromaSamplePosition == 1 ? "vertical" : "colocated"));
        // AV1 Image File Format, section 4: an alpha image has the bit depth of its master image
        Py.Assert(alpha is null || alpha.Color.BitDepth == color.Color.BitDepth, "the alpha plane must have the bit depth of the color image");
        if (payloads.Any(payload => payload.HasFilmGrain))
            features.Add("avif.filmGrain");
        if (payloads.All(payload => payload.IsLossless))
            features.Add("avif.lossless");
    }

    /// <summary>The documented pixel format of an untyped load: Rgb24, Rgba32 with alpha and Gray8 for monochrome at 8 bits;
    /// Rgba64 and Gray16 for deeper samples (the deepest of the color and alpha planes).</summary>
    private static string PixelFormatOf(PlaneImage color, PlaneImage? alpha)
    {
        var deep = color.Color.BitDepth > 8 || alpha?.Color.BitDepth > 8;
        if (color.Color.Monochrome && alpha is null)
            return deep ? "Gray16" : "Gray8";

        return deep ? "Rgba64" : alpha is null ? "Rgb24" : "Rgba32";
    }

    private static string ColorModelOf(PlaneImage color, bool hasAlpha, int matrix)
    {
        if (color.Color.Monochrome)
            return hasAlpha ? "GrayscaleAlpha" : "Grayscale";

        return matrix != 0 ? "YCbCr" : hasAlpha ? "Rgba" : "Rgb";
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Manifest entries
    // -----------------------------------------------------------------------------------------------------------------

    private static List<string> LayoutsOf(AvifReference reference) => reference.PixelFormat switch
    {
        "Rgb24" => ["rgba8", "rgb8"],
        "Gray8" => ["rgba8", "gray8"],
        "Gray16" => ["rgba16le", "gray16le"],
        "Rgba64" => ["rgba16le"],
        _ => ["rgba8"],
    };

    /// <summary>Writes a reference buffer, or reuses the file of a fixture that has the same pixels. Buffers of 2 KB and
    /// more are stored gzip-compressed when that saves a quarter of them.</summary>
    private static Obj WriteBuffer(ContainerContext context, string stem, string role, Img img, string layout)
    {
        var raw = img.Raw(layout);
        var key = Inv($"{layout}/{img.Width}x{img.Height}/{Bytes.Sha256Hex(raw)}");
        if (context.Buffers.TryGetValue(key, out var existing))
            return new Obj(existing);

        var compression = "none";
        var stored = raw;
        if (raw.Length >= 2048)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(raw);
            }

            if (output.Length <= raw.Length * 3 / 4)
            {
                stored = output.ToArray();
                compression = "gzip";
            }
        }

        var bytesPerPixel = layout switch { "rgba8" => 4, "rgba16le" => 8, "rgb8" => 3, "gray8" => 1, _ => 2 };
        var entry = context.Corpus.Write($"{stem}.{role}.{layout}.raw" + (compression == "gzip" ? ".gz" : ""), stored);
        entry["layout"] = layout;
        entry["width"] = img.Width;
        entry["height"] = img.Height;
        entry["rowBytes"] = img.Width * bytesPerPixel;
        entry["byteLength"] = raw.Length;
        entry["alpha"] = layout.StartsWith("rgba", StringComparison.Ordinal) ? "straight" : "none";
        entry["rowOrder"] = "top-down";
        entry["compression"] = compression;
        context.Buffers.Add(key, entry);
        return new Obj(entry);
    }

    /// <summary>Records a valid fixture: the input, its reference derived from its bytes, the cross-checks and the policy.</summary>
    /// <param name="origin"><c>generated</c> (written by an encoder) or <c>hand-authored</c> (assembled box by box).</param>
    /// <param name="function">The builder and what it does, recorded after the script path.</param>
    /// <param name="payloadsOf">The valid fixture whose payloads the file is assembled around, or <see langword="null"/>.</param>
    /// <param name="sourceLayer">The spatial layer of <paramref name="payloadsOf"/> that is the image of this file, which
    /// <c>avifdec --progressive --index</c> decodes from that fixture as one more cross-check; or <see langword="null"/>.</param>
    private static AvifReference AddValidAvif(ContainerContext context, string name, byte[] data, string origin, string function, IList<string> tools, IList<string>? commands, Obj? parameters, string? notes = null, string[]? requires = null, string? payloadsOf = null, int? sourceLayer = null)
    {
        var id = "avif/" + name;
        var scratch = context.NewScratch(name);
        AvifReference reference;
        try
        {
            reference = Describe(context.Tools, scratch, data);
        }
        catch (Exception error) when (error is ToolException or InvalidDataException or InvalidOperationException)
        {
            throw new FatalException($"{id}: {error.Message}");
        }

        var missing = (requires ?? []).Where(tag => !HasFeature([.. reference.Features], tag)).ToList();
        if (missing.Count > 0)
            throw new FatalException($"{id}: the file does not have {string.Join(", ", missing)} (it has {string.Join(" ", reference.Features)}).");
        if (reference.HiddenFrames > 0 && !reference.IsSequence)
            throw new FatalException($"{id}: the item has frames that are not shown.");

        var path = scratch / (name + ".avif");
        File.WriteAllBytes(path, data);
        var avifdec = AvifdecCrossCheck(context, scratch, path, reference);
        var checks = new List<object?> { avifdec };
        context.AvifdecChecks.Add(name, avifdec);
        if (!avifdec.ContainsKey("maxAbsoluteError") && payloadsOf is not null && context.AvifdecChecks[payloadsOf] is var source && source.TryGetValue("maxAbsoluteError", out var sourceMax))
        {
            // avifdec does not read this layout; what it measures on the same payloads in the layout it reads still holds
            checks.Add(new Obj
            {
                ["decoder"] = (string)source["decoder"]! + ", on avif/" + payloadsOf,
                ["command"] = source["command"],
                ["maxAbsoluteError"] = sourceMax,
                ["meanAbsoluteError"] = source["meanAbsoluteError"],
                ["result"] = source["result"],
                ["notes"] = "The same payloads in the layout avifdec reads; the references are the same files.",
            });
        }

        if (sourceLayer is { } layer && payloadsOf is not null)
            checks.Add(AvifdecLayerCrossCheck(context, scratch, payloadsOf, layer, reference));

        if (FfmpegCrossCheck(context, path, reference) is { } ffmpeg)
            checks.Add(ffmpeg);

        var input = context.Corpus.Write(id + ".avif", data);
        input["role"] = "encoded";
        context.Inputs.Add(name, data);
        context.InputEntries.Add(name, input);
        context.References.Add(name, reference);
        var layouts = LayoutsOf(reference);
        var frames = new List<object?>();
        for (var i = 0; i < reference.Frames.Count; i++)
        {
            var frame = new Obj();
            if (reference.IsSequence)
            {
                var timing = reference.Timings[i];
                frame["duration"] = CommonCorpus.Normalize(timing.SampleDelta, timing.Timescale);
                if (reference.Frames.Count > 1)
                    frame["encodedDelay"] = new Obj { ["sampleDelta"] = (long)timing.SampleDelta, ["timescale"] = (long)timing.Timescale };
            }
            else
            {
                frame["duration"] = "0/1";
            }

            frame["buffers"] = layouts.Select(layout => (object?)WriteBuffer(context, id, Inv($"frame-{i}"), reference.Frames[i], layout)).ToList();
            frames.Add(frame);
        }

        Obj? animation = null;
        if (reference.Frames.Count > 1)
        {
            var track = reference.ColorTrack!;
            animation = new Obj
            {
                ["totalPlays"] = track.TotalPlays,
                ["editList"] = track.EditList is not { } edits ? null : new Obj
                {
                    ["repeating"] = edits.IsRepeating,
                    ["segmentDuration"] = edits.Entries[0].SegmentDuration is { } segment ? (long)segment : null,
                    ["trackDuration"] = track.Duration is { } duration ? (long)duration : null,
                },
            };
        }

        var cicp = reference.Cicp;
        var expected = new Obj
        {
            ["width"] = reference.Frames[0].Width,
            ["height"] = reference.Frames[0].Height,
            ["pixelFormat"] = reference.PixelFormat,
            ["colorModel"] = reference.ColorModel,
            ["bitsPerComponent"] = reference.BitsPerComponent,
            ["orientation"] = reference.Orientation,
            ["iccProfile"] = reference.Icc is null ? "none" : Bytes.Latin1(reference.Icc.AsSpan(16, 4)) == "GRAY" ? "gray" : "rgb",
            ["cicp"] = cicp is null ? null : new Obj
            {
                ["colorPrimaries"] = cicp.ColorPrimaries,
                ["transferCharacteristics"] = cicp.TransferCharacteristics,
                ["matrixCoefficients"] = cicp.MatrixCoefficients,
                ["fullRange"] = cicp.FullRange,
            },
        };

        // The labels that few files have are only written for the files that have them
        if (reference.ContentLightLevel is { } lightLevel)
        {
            expected["contentLightLevel"] = new Obj
            {
                ["maxContentLightLevel"] = lightLevel.MaxContentLightLevel,
                ["maxPictureAverageLightLevel"] = lightLevel.MaxPictureAverageLightLevel,
            };
        }

        if (reference.MasteringDisplay is { } display)
        {
            expected["masteringDisplay"] = new Obj
            {
                ["red"] = new List<object?> { display.Red.X, display.Red.Y },
                ["green"] = new List<object?> { display.Green.X, display.Green.Y },
                ["blue"] = new List<object?> { display.Blue.X, display.Blue.Y },
                ["whitePoint"] = new List<object?> { display.WhitePoint.X, display.WhitePoint.Y },
                ["maxLuminance"] = display.MaxLuminance,
                ["minLuminance"] = display.MinLuminance,
            };
        }

        if (reference.PixelAspectRatio is { } pixelAspectRatio)
        {
            expected["pixelAspectRatio"] = new Obj
            {
                ["horizontalSpacing"] = pixelAspectRatio.HorizontalSpacing,
                ["verticalSpacing"] = pixelAspectRatio.VerticalSpacing,
            };
        }

        expected["animation"] = animation;
        expected["resolution"] = null;
        expected["profiles"] = new Obj
        {
            ["icc"] = reference.Icc is null ? null : CommonCorpus.ProfileRecord(reference.Icc),
            ["exif"] = reference.Exif is null ? null : CommonCorpus.ProfileRecord(reference.Exif),
            ["xmp"] = reference.Xmp is null ? null : CommonCorpus.ProfileRecord(reference.Xmp),
        };
        expected["text"] = new List<object?>();
        expected["frameCount"] = reference.Frames.Count;
        expected["frames"] = frames;
        expected["poster"] = null;

        var fixture = new Obj
        {
            ["id"] = id,
            ["kind"] = "valid",
            ["format"] = "avif",
            ["input"] = input,
            ["provenance"] = context.Corpus.Provenance(origin, function, parameters, tools, commands),
            ["features"] = reference.Features.ToList(),
            ["reference"] = new Obj
            {
                ["method"] = "decoded",
                ["description"] = ReferenceDescription(reference),
                ["decoder"] = context.Tools.DecodersLabel,
                ["backend"] = "ReferenceAvif and ReferenceYuvConverter (test harness)",
                ["command"] = "av1Vectors.decoders[].command",
                ["crossChecks"] = checks,
            },
            ["expected"] = expected,
            ["comparison"] = Policy(id, reference, checks.Cast<Obj>().ToList()),
        };
        if (!string.IsNullOrEmpty(notes))
            fixture["notes"] = notes;

        context.Corpus.Fixtures.Add(fixture);
        return reference;
    }

    private static string ReferenceDescription(AvifReference reference)
    {
        var colors = reference.IsMatrixed ? "converted with exact rationals (matrix, range and chroma position of the file)" : "scaled without matrix";
        return "Planes of the " + (reference.IsSequence ? "samples" : "items") + " decoded identically by both decoders, " + colors + ".";
    }

    /// <summary>Exact for the content no matrix applies to. Matrixed content: one unit, and the measured mean disagreement
    /// of the library plus 0.01 (the library rounds its coefficients to 16 fractional bits; the reference does not round
    /// them), when an independent decoder also disagrees with the reference.</summary>
    private static Obj Policy(string id, AvifReference reference, List<Obj> checks)
    {
        var measured = checks.Where(check => check.ContainsKey("maxAbsoluteError") && !(check.TryGetValue("excludedFromTolerance", out var excluded) && excluded is true)).ToList();
        if (!reference.IsMatrixed || measured.Count == 0 || measured.All(check => Convert.ToInt32(check["maxAbsoluteError"], CultureInfo.InvariantCulture) == 0))
            return new Obj { ["mode"] = "exact" };

        var (_, _, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(measured);
        var hasMeasurement = LibraryAvifMeasurements.TryGetValue(id, out var library);
        var mean = hasMeasurement ? Math.Ceiling(Math.Round((library.Mean + 0.01) * 100, 6, MidpointRounding.AwayFromZero)) / 100 : 0.5;
        Py.Assert(!hasMeasurement || library.Max <= 1, id + ": the library disagrees with the reference by more than one unit");
        Py.Assert(1 <= justifiedMax && mean <= justifiedMean, id + ": the tolerance is not justified by the cross-checks");
        return new Obj
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = 1,
            ["maxMeanAbsoluteError"] = mean,
            ["justification"] = "Exact-rational reference; the library rounds its matrix coefficients to 16 fractional bits"
                + (hasMeasurement ? Inv($" (measured: max {library.Max}, mean {Py.Repr(library.Mean)})") : " (not measured yet)")
                + Inv($". Allowed: 1 and the measured mean + 0.01; the cross-checks would justify max {justifiedMax}, mean {Py.Repr(justifiedMean)}."),
        };
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cross-checks
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The frames as a tool that applies the transformations of the file shows them: the stored frames after the
    /// orientation (an index permutation written from the EXIF definitions).</summary>
    private static Img Orient(Img img, int orientation)
    {
        var transposed = orientation >= 5;
        var (width, height) = transposed ? (img.Height, img.Width) : (img.Width, img.Height);
        var pixels = new List<Px>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // The stored sample that is displayed at (x, y)
                var (sx, sy) = orientation switch
                {
                    1 => (x, y),
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (y, width - 1 - x),
                    7 => (height - 1 - y, width - 1 - x),
                    _ => (height - 1 - y, x),
                };
                pixels.Add(img.Pixels[(sy * img.Width) + sx]);
            }
        }

        return new Img(width, height, img.Model, img.Depth, pixels);
    }

    /// <summary>Errors on the 8-bit scale: 16-bit samples are compared in units of 257.</summary>
    private static (int Max, double Mean, int Alpha) CompareScaled(IReadOnlyList<byte[]> expected, IReadOnlyList<byte[]> actual, int sampleBytes)
    {
        var max = 0;
        var alpha = 0;
        var mean = 0.0;
        for (var i = 0; i < expected.Count; i++)
        {
            var (frameMax, frameMean, frameAlpha) = CommonCorpus.Compare(expected[i], actual[i], sampleBytes);
            max = Math.Max(max, frameMax);
            alpha = Math.Max(alpha, frameAlpha);
            mean += frameMean / expected.Count;
        }

        return sampleBytes == 1 ? (max, Py.Round(mean, 4), alpha) : ((max + 256) / 257, Py.Round(mean / 257, 4), (alpha + 256) / 257);
    }

    private static void Record(Obj check, IReadOnlyList<byte[]> expected, IReadOnlyList<byte[]> actual, int sampleBytes, string differs)
    {
        var (max, mean, alpha) = CompareScaled(expected, actual, sampleBytes);
        check["maxAbsoluteError"] = max;
        check["meanAbsoluteError"] = mean;
        if (max == 0 && expected.Zip(actual).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)))
        {
            check["result"] = "exact";
        }
        else
        {
            check["result"] = "differs";
            check["notes"] = differs + (alpha == 0 ? " Alpha is identical." : Inv($" Alpha differs by up to {alpha}."));
        }
    }

    /// <summary>libavif avifdec: its own container parser, dav1d and its own conversion. It writes PNG files (read back
    /// losslessly through FFmpeg), applies the clean aperture and the orientation, and scales deep samples to 16 bits.</summary>
    private static Obj AvifdecCrossCheck(ContainerContext context, FullPath scratch, FullPath path, AvifReference reference)
    {
        var tools = context.Tools;
        var deep = reference.Frames[0].Depth == 16;
        var output = scratch / "avifdec.png";
        var command = new List<string> { tools.Avifdec, "-j", "1" };
        if (deep)
            command.AddRange(["-d", "16"]);
        if (reference.IsSequence)
            command.AddRange(["--index", "all"]);
        command.AddRange([path.Value, output.Value]);
        var check = new Obj
        {
            ["decoder"] = tools.AvifdecLabel,
            ["command"] = CommonCorpus.Display(command, tools, [(path.Value, "{input}"), (output.Value, "{output}.png")]),
        };
        if (!reference.HasMiafTransformOrder)
        {
            check["result"] = "differs";
            check["notes"] = "avifdec applies the transformations in the order of MIAF (the clean aperture, the rotation, then the mirroring), whatever the order of their association: it presents other samples of this file, which is not compared.";
            return check;
        }

        var result = Proc.Exec(command);
        if (result.ExitCode != 0)
        {
            var message = Encoding.UTF8.GetString(result.Stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "error";
            check["result"] = "differs";
            check["notes"] = "avifdec rejects the file: " + message.Replace(path.Value, "{input}", StringComparison.Ordinal).Replace(output.Value, "{output}.png", StringComparison.Ordinal);
            return check;
        }

        // avifdec applies the irot and imir properties of an item; it does not read the matrix of a track header
        var appliedOrientation = reference.IsSequence ? 1 : reference.Orientation;
        var oriented = reference.Frames.Select(frame => Orient(frame, appliedOrientation)).ToList();
        var (pixelFormat, bytesPerPixel, sample, layout) = deep ? ("rgba64le", 8, 2, "rgba16le") : ("rgba", 4, 1, "rgba8");
        var files = reference.IsSequence
            ? Enumerable.Range(0, reference.Frames.Count).Select(i => scratch / Inv($"avifdec-{i:D10}.png")).ToList()
            : [output];
        if (files.Any(file => !File.Exists(file)))
        {
            check["result"] = "differs";
            check["notes"] = Inv($"avifdec did not write the {files.Count} expected frames.");
            return check;
        }

        var decoded = files.Select(file => CommonCorpus.FfmpegDecode(tools, file, pixelFormat, oriented[0].Width, oriented[0].Height, bytesPerPixel, animated: false).Frames[0]).ToList();
        var expected = oriented.Select(frame => RgbaRaw(frame, layout)).ToList();
        Record(check, expected, decoded, sample, reference.IsMatrixed ? "Independent conversion (its own chroma upsampling and rounding)." : "Its own sample scaling.");
        if (reference.IsTransformed)
        {
            check["notes"] = (check.TryGetValue("notes", out var text) ? text + " " : "") + (reference.IsSequence
                ? Inv($"Compared with the images as they are stored: avifdec does not read the matrix of the track header (orientation {reference.Orientation}).")
                : Inv($"Compared with the reference displayed with orientation {reference.Orientation} (avifdec applies irot and imir)."));
        }
        return check;
    }

    /// <summary>
    /// libavif avifdec asked for one layer of a layered image (<c>--progressive --index</c>): the layer of the fixture the
    /// payloads come from that is the image of a file that selects it. Only for a layer that has the size of the image
    /// (avifdec scales a smaller layer up to the size of the last one).
    /// </summary>
    private static Obj AvifdecLayerCrossCheck(ContainerContext context, FullPath scratch, string source, int layer, AvifReference reference)
    {
        var tools = context.Tools;
        var deep = reference.Frames[0].Depth == 16;
        var input = scratch / "layers.avif";
        var output = scratch / "avifdec-layer.png";
        File.WriteAllBytes(input, context.Inputs[source]);
        var command = new List<string> { tools.Avifdec, "-j", "1" };
        if (deep)
            command.AddRange(["-d", "16"]);
        command.AddRange(["--progressive", "--index", Inv($"{layer}"), input.Value, output.Value]);
        var check = new Obj
        {
            ["decoder"] = Inv($"{tools.AvifdecLabel}, layer {layer} of avif/{source}"),
            ["command"] = CommonCorpus.Display(command, tools, [(input.Value, "{source}"), (output.Value, "{output}.png")]),
        };
        var result = Proc.Exec(command);
        if (result.ExitCode != 0 || !File.Exists(output))
        {
            check["result"] = "differs";
            check["notes"] = "avifdec does not decode the layer.";
            return check;
        }

        var frame = reference.Frames[0];
        var (pixelFormat, bytesPerPixel, sample, layout) = deep ? ("rgba64le", 8, 2, "rgba16le") : ("rgba", 4, 1, "rgba8");
        var decoded = CommonCorpus.FfmpegDecode(tools, output, pixelFormat, frame.Width, frame.Height, bytesPerPixel, animated: false).Frames[0];
        Record(check, [RgbaRaw(frame, layout)], [decoded], sample, reference.IsMatrixed ? "Independent conversion (its own chroma upsampling and rounding)." : "Its own sample scaling.");
        return check;
    }

    private static byte[] RgbaRaw(Img img, string layout) => new Img(img.Width, img.Height, "rgba", img.Depth, img.Rgba()).Raw(layout);

    /// <summary>FFmpeg: its mov demuxer and swscale, for the still images whose samples no matrix applies to and that it
    /// presents as stored (no alpha plane, grid, clean aperture or orientation, which it handles its own way). Matrixed
    /// content is left out: swscale converts with its own chroma positions and tables (measured up to 75 units from the
    /// documented conversion), which says nothing about the reference; and it presents the item of a sequence file, not
    /// its track.</summary>
    private static Obj? FfmpegCrossCheck(ContainerContext context, FullPath path, AvifReference reference)
    {
        var features = reference.Features;
        if (reference.IsSequence || reference.IsMatrixed || features.Contains("avif.alpha") || features.Contains("avif.clap") || features.Any(f => f.StartsWith("avif.grid=", StringComparison.Ordinal) || f.StartsWith("avif.irot=", StringComparison.Ordinal) || f.StartsWith("avif.imir=", StringComparison.Ordinal)))
            return null;

        // FFmpeg does not read the a1op and lsel properties: it presents the last layer whatever they select
        if (features.Any(f => f.StartsWith("avif.lsel=", StringComparison.Ordinal) || f.StartsWith("avif.a1op=", StringComparison.Ordinal)))
            return null;

        var tools = context.Tools;
        var deep = reference.Frames[0].Depth == 16;
        var (pixelFormat, bytesPerPixel, sample, layout) = deep ? ("rgba64le", 8, 2, "rgba16le") : ("rgba", 4, 1, "rgba8");
        var command = tools.FfmpegCmd(["-threads", "1", "-c:v", Dav1d, "-noautorotate", "-i", path.Value, .. CommonCorpus.FfmpegDecodeFlags, "-pix_fmt", pixelFormat, "-"]);
        var check = new Obj
        {
            ["decoder"] = $"{tools.FfmpegLabel} (mov demuxer, libdav1d, swscale)",
            ["command"] = CommonCorpus.Display(command, tools, [(path.Value, "{input}")]),
        };
        var result = Proc.Exec(command);
        var frameLength = reference.Frames[0].Width * reference.Frames[0].Height * bytesPerPixel;
        if (result.ExitCode != 0 || result.Stdout.Length != frameLength * reference.Frames.Count)
        {
            check["result"] = "differs";
            check["notes"] = "FFmpeg does not decode the file.";
            return check;
        }

        Record(check, [.. reference.Frames.Select(frame => RgbaRaw(frame, layout))], CommonCorpus.Chunks(result.Stdout, frameLength), sample, "swscale scales deep samples by bit replication, not by the exact ratio.");
        return check;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Verification (the "verify" subcommand): the committed references re-derived with the pinned tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Re-derives the reference of every valid AVIF fixture from its committed input (ReferenceAvif, both decoders,
    /// ReferenceYuvConverter) and compares it with the committed buffers, which must be identical.</summary>
    public static (int Verified, List<string> Failures) VerifyContainer()
    {
        var failures = new List<string>();
        var fixtures = CorpusDriver.Fixtures(CorpusDriver.LoadCommittedManifest()).Where(f => IsAvifFixture(f) && (string)f["kind"]! == "valid").ToList();
        if (fixtures.Count == 0)
            return (0, failures);

        var tools = new AvifFileTools(acceptVersions: true);
        var root = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-avif-verify-").FullName);
        try
        {
            foreach (var fixture in fixtures)
            {
                var id = (string)fixture["id"]!;
                var scratch = root / id.Replace('/', '_');
                Directory.CreateDirectory(scratch);
                try
                {
                    var reference = Describe(tools, scratch, File.ReadAllBytes(CorpusDriver.CorpusDir / (string)((Obj)fixture["input"]!)["path"]!));
                    var frames = ((List<object?>)((Obj)fixture["expected"]!)["frames"]!).Cast<Obj>().ToList();
                    if (frames.Count != reference.Frames.Count)
                    {
                        failures.Add(Inv($"{id}: {reference.Frames.Count} frames re-derived, {frames.Count} committed."));
                        continue;
                    }

                    for (var i = 0; i < frames.Count; i++)
                    {
                        foreach (var buffer in ((List<object?>)frames[i]["buffers"]!).Cast<Obj>())
                        {
                            var committed = File.ReadAllBytes(CorpusDriver.CorpusDir / (string)buffer["path"]!);
                            if ((string)buffer["compression"]! == "gzip")
                            {
                                using var gzip = new GZipStream(new MemoryStream(committed), CompressionMode.Decompress);
                                using var raw = new MemoryStream();
                                gzip.CopyTo(raw);
                                committed = raw.ToArray();
                            }

                            if (!committed.AsSpan().SequenceEqual(reference.Frames[i].Raw((string)buffer["layout"]!)))
                                failures.Add(Inv($"{id}: frame {i} ({buffer["layout"]}) re-derived with the pinned decoders differs from the committed reference."));
                        }
                    }
                }
                catch (Exception error) when (error is ToolException or InvalidDataException or InvalidOperationException)
                {
                    failures.Add($"{id}: {error.Message.Split('\n')[0]}");
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        return (fixtures.Count, failures);
    }
}
