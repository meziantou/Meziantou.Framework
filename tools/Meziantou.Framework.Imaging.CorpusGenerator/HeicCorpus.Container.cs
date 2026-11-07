using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The HEIC fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/heic, invalid/heic and limit/heic;
// manifest entries of format "heic"): the "heic-container" generator. The HEVC conformance vectors of HevcCorpus.cs test
// the bitstream; these fixtures test what is around it: the boxes, the item model, the configuration record, the image
// sequences, the color conversion, and the errors and limits of the container.
//
// This generator is the reviewed, reproducible recipe of every committed HEIC fixture. It is NEVER run by the tests and
// does not use the library under test.
// - Inputs: written by libheif heif-enc (with x265) from hand-defined patterns, or assembled box by box (HeifFileBuilder,
//   linked from the test harness) around the payloads of such a file, around a stream x265 codes from a pattern, or
//   around a committed HEVC vector, for what heif-enc does not write. Malformed, unsupported and over-limit inputs are
//   assembled the same way.
// - References: the structure of every input is read by ReferenceHeif (an independent reader written from the
//   specifications, linked from the test harness); the data of each coded item and the samples of each track are turned
//   into a byte stream (the NAL units of the configuration record, then those of the data) and decoded to planes by the
//   three decoders of the HEVC vectors, FFmpeg's hevc decoder, libde265 and HM, in the native format of the stream: at
//   least two must agree byte for byte, and a recipe names the decoder it expects to be left out (HevcCorpus.Reference.cs);
//   the planes are converted by ReferenceYuvConverter (exact rationals) with the matrix coefficients and the range of the
//   colr box, or else of the video signal description of the bitstream, and with the chroma sample location of the
//   bitstream; the grid, the alpha plane and the clean aperture are applied here.
// - Comparison: identity-matrix, monochrome and alpha samples are exact. Matrixed samples use a tolerance of one unit: the
//   library documents coefficients rounded to 16 fractional bits, the reference uses exact rationals.
// - Cross-checks: libheif heif-dec (its own container parser, libde265 and its own color conversion) decodes every input
//   it accepts; its differences are measured and recorded, never used as references.
//
// Usage (from the repository root):
//     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- heic-container            # check
//     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- heic-container --write    # explicit regeneration
internal static partial class HeicCorpus
{
    public const string ContainerScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/HeicCorpus.Container.cs";

    private const string PinnedLibheif = "1.23.6";

    // Measured disagreement of the library decoder with each reference of a matrixed fixture: (max absolute error, mean
    // absolute color error) on the 8-bit scale, printed by Heic.Tests/Conformance/HeicDecoderConformanceTests, which also
    // fails when the policy derived from these values is looser or tighter than the decoder needs. The values are reviewed
    // measurements of the library (never used as pixel references).
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryHeicMeasurements = new(StringComparer.Ordinal)
    {
        ["heic/alpha-420-picture"] = (0, 0.0),
        ["heic/alpha-limited-range"] = (0, 0.0),
        ["heic/alpha-mpegb-urn"] = (0, 0.0),
        ["heic/chroma-loc-0-420"] = (0, 0.0),
        ["heic/chroma-loc-1-420"] = (0, 0.0),
        ["heic/chroma-loc-2-420"] = (0, 0.0),
        ["heic/clap-420-odd"] = (0, 0.0),
        ["heic/clap-irot"] = (0, 0.0),
        ["heic/grid-2x1-420"] = (0, 0.0),
        ["heic/grid-2x1-420-center"] = (0, 0.0),
        ["heic/grid-2x1-444-10bit"] = (1, 0.0),
        ["heic/hvcc-incomplete-arrays"] = (0, 0.0),
        ["heic/hvcc-length-size-1"] = (0, 0.0),
        ["heic/hvcc-length-size-2"] = (0, 0.0),
        ["heic/hvcc-several-parameter-sets"] = (0, 0.0),
        ["heic/lossy-420-alpha"] = (0, 0.0),
        ["heic/lossy-420-bt2020-limited-10bit"] = (0, 0.0),
        ["heic/lossy-420-bt601"] = (0, 0.0),
        ["heic/lossy-420-bt601-12bit"] = (1, 0.0),
        ["heic/lossy-420-bt601-33x17"] = (0, 0.0),
        ["heic/lossy-420-bt601-odd"] = (0, 0.0),
        ["heic/lossy-420-bt709-limited"] = (0, 0.0),
        ["heic/lossy-420-center-bt2020-limited-10bit"] = (0, 0.0),
        ["heic/lossy-420-center-bt601"] = (0, 0.0),
        ["heic/lossy-420-center-bt601-odd"] = (0, 0.0),
        ["heic/lossy-420-center-bt709-limited-67x65"] = (1, 0.0001),
        ["heic/lossy-420-colocated-bt2020-10bit"] = (1, 0.0),
        ["heic/lossy-420-colocated-bt601"] = (0, 0.0),
        ["heic/lossy-422-alpha"] = (0, 0.0),
        ["heic/lossy-422-bt2020-full-12bit"] = (0, 0.0),
        ["heic/lossy-422-bt601"] = (0, 0.0),
        ["heic/lossy-422-bt601-odd"] = (0, 0.0),
        ["heic/lossy-422-bt709-limited-10bit"] = (1, 0.0),
        ["heic/lossy-444-alpha-10bit"] = (1, 0.0),
        ["heic/lossy-444-bt2020-full-10bit"] = (0, 0.0),
        ["heic/lossy-444-bt470bg-limited-12bit"] = (0, 0.0),
        ["heic/lossy-444-bt709-limited"] = (0, 0.0),
        ["heic/lossy-444-unspecified-limited"] = (0, 0.0),
        ["heic/no-colr-bitstream-bt709"] = (0, 0.0),
        ["heic/no-colr-bitstream-bt709-limited"] = (0, 0.0),
        ["heic/no-colr-bitstream-limited-range"] = (0, 0.0),
        ["heic/no-colr-no-signal-444"] = (0, 0.0),
        ["heic/parameter-sets-in-band"] = (0, 0.0),
        ["heic/phone-grid-420"] = (1, 0.0002),
        ["heic/phone-grid-420-10bit"] = (1, 0.0),
        ["heic/phone-grid-420-center"] = (1, 0.0002),
        ["heic/sequence-420"] = (1, 0.0),
        ["heic/sequence-420-center"] = (1, 0.0001),
        ["heic/sequence-422"] = (1, 0.0001),
        ["heic/sequence-444-10bit"] = (1, 0.0),
        ["heic/sequence-clap-odd-420"] = (0, 0.0),
        ["heic/sequence-window-420"] = (0, 0.0),
        ["heic/window-420-bt2020-limited-10bit"] = (1, 0.0),
        ["heic/window-422"] = (0, 0.0),
    };

    private static bool IsHeicFixture(Obj fixture) => (string?)fixture["format"] == "heic";

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    public static int RunContainer(GeneratorOptions options)
    {
        var tools = new HeicFileTools(options.AcceptToolVersions, encoders: true);
        return CorpusDriver.Run(options, "mfi-heic-container-", output => GenerateContainer(output, tools),
            (fixtures, files, bytes) => Inv($"{fixtures} HEIC fixtures; corpus: {files} files, {bytes} bytes"),
            "The committed HEIC fixtures are reproducible.");
    }

    private static int GenerateContainer(FullPath outDir, HeicFileTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<HeicFileTools>(outDir, tools, ContainerScriptPath))
        {
            var context = new ContainerContext(corpus);
            BuildStills(context);
            BuildWrapped(context);
            BuildLayouts(context);
            BuildSequences(context);
            BuildInvalid(context);
            BuildUnsupported(context);
            BuildLimits(context);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsHeicFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsHeicFixture);
        var valid = fixtures.Where(f => (string)f["kind"]! == "valid").ToList();
        Console.WriteLine(Inv($"heic: {valid.Count} valid, {fixtures.Count - valid.Count} error fixtures"));
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The pinned tools of the HEIC fixtures: the FFmpeg, the x265 and the three decoders of the HEVC vectors, and
    /// libheif's heif-enc and heif-dec, all from the meziantou/prebuilt release.</summary>
    private sealed partial class HeicFileTools : ToolSet
    {
        public HeicFileTools(bool acceptVersions, bool encoders)
            : this(new HevcCorpus.PayloadDecoder(acceptVersions, encoders), acceptVersions)
        {
        }

        private HeicFileTools(HevcCorpus.PayloadDecoder hevc, bool acceptVersions)
            : base(hevc.Ffmpeg)
        {
            Hevc = hevc;
            HeifEnc = PrebuiltTools.HeifEnc.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            HeifDec = PrebuiltTools.HeifDec.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var encoder = LibheifVersionRegex().Match(Proc.RunText([HeifEnc, "--version"]));
            var decoder = LibheifVersionRegex().Match(Proc.RunText([HeifDec, "--version"]));
            if (!encoder.Success || !decoder.Success || encoder.Groups["version"].Value != decoder.Groups["version"].Value)
                throw new FatalException("Cannot read one libheif version from heif-enc and heif-dec.");

            LibheifVersion = encoder.Groups["version"].Value;
            if (LibheifVersion != PinnedLibheif && !acceptVersions)
                throw new FatalException($"libheif {LibheifVersion} differs from the pinned version {PinnedLibheif}. Review the impact and rerun with --accept-tool-versions (then update PinnedLibheif).");

            DisplayNames = new Dictionary<string, string>(hevc.Tools.DisplayNames, StringComparer.Ordinal) { [HeifEnc] = "heif-enc", [HeifDec] = "heif-dec" };
        }

        public HevcCorpus.PayloadDecoder Hevc { get; }

        public string HeifEnc { get; }

        public string HeifDec { get; }

        public string LibheifVersion { get; }

        /// <summary>heif-enc and the HEVC encoder it is linked with.</summary>
        public string HeifEncLabel => $"libheif {LibheifVersion} heif-enc ({Hevc.X265Label})";

        /// <summary>heif-dec, which decodes HEVC with the libde265 it is linked with.</summary>
        public string HeifDecLabel => $"libheif {LibheifVersion} heif-dec (libde265)";

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        [GeneratedRegex(@"libheif: (?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex LibheifVersionRegex();
    }

    /// <summary>The state of one generation: the corpus, the valid inputs by name and the reference files already written.</summary>
    private sealed class ContainerContext(Corpus<HeicFileTools> corpus)
    {
        public Corpus<HeicFileTools> Corpus { get; } = corpus;

        public HeicFileTools Tools => Corpus.Tools;

        /// <summary>The bytes of the valid inputs, by fixture name: the sources of the assembled files.</summary>
        public Dictionary<string, byte[]> Inputs { get; } = new(StringComparer.Ordinal);

        /// <summary>The manifest entries of the valid inputs, by fixture name (limit fixtures reuse their input).</summary>
        public Dictionary<string, Obj> InputEntries { get; } = new(StringComparer.Ordinal);

        /// <summary>The references of the valid inputs, by fixture name.</summary>
        public Dictionary<string, HeicReference> References { get; } = new(StringComparer.Ordinal);

        /// <summary>The heif-dec cross-check of the valid inputs, by fixture name: a file that heif-dec rejects is assembled
        /// around the payloads of one of them, whose measured cross-check still applies to the payloads.</summary>
        public Dictionary<string, Obj> HeifDecChecks { get; } = new(StringComparer.Ordinal);

        /// <summary>The reference buffers written so far, by layout, size and content hash: fixtures that present the same
        /// pixels (one coded payload in several containers) share their reference files.</summary>
        public Dictionary<string, Obj> Buffers { get; } = new(StringComparer.Ordinal);

        /// <summary>The decoder each valid input expects to be left out, by fixture name: a file assembled around the
        /// payloads of a fixture expects the same one.</summary>
        public Dictionary<string, ExpectedExclusion?> Exclusions { get; } = new(StringComparer.Ordinal);

        /// <summary>The encoder of the payloads of each valid input, by fixture name, as the provenance of a file assembled
        /// around them names it.</summary>
        public Dictionary<string, string> PayloadTools { get; } = new(StringComparer.Ordinal);

        private int _scratchIndex;

        public FullPath NewScratch(string name)
        {
            var directory = Corpus.Scratch / Inv($"{_scratchIndex++:D3}-{name}");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The reference of an input: structure by ReferenceHeif, planes by the decoders, colors by ReferenceYuvConverter
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>One decoded picture: the planes the decoders agree on, and what the headers of its stream signal.</summary>
    /// <param name="ChromaFormat">0 is 4:0:0, 1 is 4:2:0, 2 is 4:2:2, 3 is 4:4:4.</param>
    /// <param name="ChromaLocation"><c>chroma_sample_loc_type_top_field</c>, or <see langword="null"/> when the stream does not code it.</param>
    private sealed record PlaneImage(int Width, int Height, int ChromaFormat, int BitDepth, bool HasVideoSignal, bool FullRangeFlag, (int Primaries, int Transfer, int Matrix)? ColorDescription, int? ChromaLocation, ushort[][] Planes)
    {
        public bool Monochrome => ChromaFormat == 0;

        public int SubsamplingX => ChromaFormat is 1 or 2 ? 1 : 0;

        public int SubsamplingY => ChromaFormat == 1 ? 1 : 0;

        public string Subsampling => ChromaFormat switch { 0 => "4:0:0", 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };

        /// <summary>The range of the samples of a color image, when no colr box gives it: the one of the video signal
        /// description of the bitstream. Without one, <c>video_full_range_flag</c> is inferred to be 0 (ITU-T H.265, clause
        /// E.3.1): the limited range.</summary>
        public bool FullRange => HasVideoSignal && FullRangeFlag;

        /// <summary>The range of the samples of an alpha plane: the full range, unless its bitstream explicitly says
        /// otherwise (an alpha plane is not video: encoders write it in full range and say nothing).</summary>
        public bool AlphaFullRange => !HasVideoSignal || FullRangeFlag;
    }

    private sealed record Timing(long Duration, uint Timescale);

    /// <summary>Everything the manifest records about a valid input, derived from its bytes.</summary>
    private sealed class HeicReference
    {
        public required List<Img> Frames { get; init; }

        public required string PixelFormat { get; init; }

        public required string ColorModel { get; init; }

        public required int BitsPerComponent { get; init; }

        public required int Orientation { get; init; }

        public required ReferenceHeif.ColorDescription? Cicp { get; init; }

        public required byte[]? Icc { get; init; }

        public required byte[]? Exif { get; init; }

        public required byte[]? Xmp { get; init; }

        public required bool IsMatrixed { get; init; }

        public required List<Timing> Timings { get; init; }

        public required bool IsSequence { get; init; }

        public required ReferenceHeif.Track? ColorTrack { get; init; }

        public required SortedSet<string> Features { get; init; }

        /// <summary>The decoded streams of the file (the items of the image and of its alpha plane, or the tracks): each one
        /// names the decoders that agree on it and the one that is left out.</summary>
        public required List<HevcCorpus.Payload> Payloads { get; init; }

        /// <summary>Whether a tool that applies the transformations of the file shows other pixels than the stored ones.</summary>
        public bool IsTransformed => Orientation != 1;
    }

    private static PlaneImage ToPlaneImage(HevcCorpus.Payload payload, HevcCorpus.PayloadPicture picture) =>
        new(picture.Width, picture.Height, payload.ChromaFormatIdc, payload.BitDepth, payload.HasVideoSignal, payload.FullRangeFlag, payload.ColorDescription, payload.ChromaSampleLocType, picture.Planes);

    /// <summary>The planes of an image whose luma plane has that size.</summary>
    private static List<(int Width, int Height)> PlaneSizes(PlaneImage image, (int Width, int Height) size) =>
        image.Monochrome ? [size] : [size, ((size.Width + image.SubsamplingX) >> image.SubsamplingX, (size.Height + image.SubsamplingY) >> image.SubsamplingY), ((size.Width + image.SubsamplingX) >> image.SubsamplingX, (size.Height + image.SubsamplingY) >> image.SubsamplingY)];

    /// <summary>Decodes a coded item: the byte stream of its configuration record and of its data must be one picture,
    /// of the sample format the record declares. Only the base layer exists in these files (a layer selector selects it).</summary>
    private static (PlaneImage Image, HevcCorpus.Payload Payload) DecodeCodedItem(HeicFileTools tools, FullPath scratch, ReferenceHeif file, ReferenceHeif.Item item, string role)
    {
        if (item.Type != "hvc1")
            throw new ToolException($"{role}: the item is of type {item.Type}, not hvc1.");

        var configuration = item.HevcConfiguration ?? throw new ToolException($"{role}: the coded item has no hvcC property.");
        Py.Assert(item.LayerSelector is null or 0 or 0xFFFF, "a valid fixture has one layer");
        var payload = tools.Hevc.Decode(scratch, role, configuration.ToByteStream(file.ReadItemData(item)));
        if (payload.Outputs.Count != 1 || payload.CodedPictures != 1)
            throw new ToolException(Inv($"{role}: the item holds {payload.CodedPictures} pictures."));

        CheckConfiguration(configuration, payload, role);
        return (ToPlaneImage(payload, payload.Outputs[0]), payload);
    }

    private static void CheckConfiguration(ReferenceHeif.HevcConfiguration configuration, HevcCorpus.Payload payload, string role)
    {
        if (configuration.ChromaFormat != payload.ChromaFormatIdc || configuration.BitDepthLuma != payload.BitDepth || (payload.ChromaFormatIdc != 0 && configuration.BitDepthChroma != payload.BitDepth))
            throw new ToolException($"{role}: the hvcC record does not match the bitstream.");
    }

    /// <summary>Decodes an image item: a coded item, or a grid whose tiles are placed in row-major order and cropped to
    /// the size of the grid (ISO/IEC 23008-12, section 6.6.2.3). The planes are tiled before the color conversion.</summary>
    private static (PlaneImage Image, List<HevcCorpus.Payload> Payloads, string? Grid) DecodeItem(HeicFileTools tools, FullPath scratch, ReferenceHeif file, ReferenceHeif.Item item, string role)
    {
        if (item.Type != "grid")
        {
            var (image, payload) = DecodeCodedItem(tools, scratch, file, item, role);
            return (image, [payload], null);
        }

        var (rows, columns, width, height) = file.ReadGridDescriptor(item);
        var tiles = file.GetGridTiles(item);
        Py.Assert(tiles.Count == rows * columns, "grid tiles");
        var decoded = tiles.Select((tile, index) => DecodeCodedItem(tools, scratch, file, tile, Inv($"{role}-tile{index}"))).ToList();
        var first = decoded[0].Image;
        Py.Assert(decoded.All(tile => tile.Image with { Planes = first.Planes } == first), "grid tiles differ");
        Py.Assert(width <= (long)columns * first.Width && height <= (long)rows * first.Height && width > (long)(columns - 1) * first.Width && height > (long)(rows - 1) * first.Height, "grid size");
        var planes = new List<ushort[]>();
        var planeSizes = PlaneSizes(first, ((int)width, (int)height));
        var tileSizes = PlaneSizes(first, (first.Width, first.Height));
        for (var plane = 0; plane < planeSizes.Count; plane++)
        {
            var (planeWidth, planeHeight) = planeSizes[plane];
            var (tileWidth, tileHeight) = tileSizes[plane];
            var samples = new ushort[planeWidth * planeHeight];
            for (var y = 0; y < planeHeight; y++)
            {
                for (var x = 0; x < planeWidth; x++)
                {
                    var tile = decoded[(y / tileHeight * columns) + (x / tileWidth)].Image;
                    samples[(y * planeWidth) + x] = tile.Planes[plane][(y % tileHeight * tileWidth) + (x % tileWidth)];
                }
            }

            planes.Add(samples);
        }

        return (first with { Width = (int)width, Height = (int)height, Planes = [.. planes] }, [.. decoded.Select(tile => tile.Payload)], Inv($"{columns}x{rows}"));
    }

    /// <summary>
    /// The position of the chroma samples of a picture (ReferenceYuvConverter.GetHevcChromaSiting, from ITU-T H.265, figure
    /// E.1 and clause 6.2): those of a 4:2:0 picture are at the left of their luma samples and between two rows of them
    /// (<c>chroma_sample_loc_type</c> 0, which is also what a bitstream that says nothing means), in the middle of four
    /// luma samples (1) or on the top-left one (2); those of a 4:2:2 picture are on the luma samples of even columns.
    /// </summary>
    private static (bool Horizontally, bool Vertically) ChromaSiting(PlaneImage image)
    {
        if (image.ChromaFormat == 1 && image.ChromaLocation is > 2)
            throw new ToolException(Inv($"chroma_sample_loc_type {image.ChromaLocation} is the position of the samples of a bottom field: not a valid fixture."));

        return ReferenceYuvConverter.GetHevcChromaSiting(image.ChromaFormat, image.ChromaLocation ?? 0);
    }

    /// <summary>Converts the planes of a color image and of its alpha plane to a frame, then keeps a rectangle of it.
    /// The alpha plane is the luma plane of its picture, whatever its chroma format.</summary>
    private static Img ToFrame(PlaneImage color, PlaneImage? alpha, int matrix, bool fullRange, (int X, int Y, int Width, int Height) crop)
    {
        var deep = color.BitDepth > 8 || alpha?.BitDepth > 8;
        var maximum = deep ? 65535 : 255;
        var (width, height) = (color.Width, color.Height);
        int[] rgb;
        if (color.Monochrome)
        {
            rgb = new int[3 * width * height];
            for (var i = 0; i < width * height; i++)
            {
                rgb[3 * i] = rgb[(3 * i) + 1] = rgb[(3 * i) + 2] = ReferenceYuvConverter.ScaleSample(color.Planes[0][i], color.BitDepth, fullRange, maximum);
            }
        }
        else
        {
            var (cositedHorizontally, cositedVertically) = ChromaSiting(color);
            rgb = ReferenceYuvConverter.ConvertToRgb(color.Planes[0], color.Planes[1], color.Planes[2], width, height, color.BitDepth, color.SubsamplingX, color.SubsamplingY,
                cositedHorizontally, cositedVertically, matrix, fullRange, maximum);
        }

        if (alpha is not null)
            Py.Assert(alpha.Width == width && alpha.Height == height, "alpha plane");

        var model = alpha is not null ? "rgba" : color.Monochrome ? "gray" : deep ? "rgba" : "rgb";
        var pixels = new List<Px>();
        for (var y = crop.Y; y < crop.Y + crop.Height; y++)
        {
            for (var x = crop.X; x < crop.X + crop.Width; x++)
            {
                var i = (y * width) + x;
                var a = alpha is null ? maximum : ReferenceYuvConverter.ScaleSample(alpha.Planes[0][i], alpha.BitDepth, alpha.AlphaFullRange, maximum);
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
    private static HeicReference Describe(HeicFileTools tools, FullPath scratch, byte[] data)
    {
        var file = ReferenceHeif.Parse(data);
        var features = new SortedSet<string>(StringComparer.Ordinal) { "heic.majorBrand=" + file.MajorBrand };
        Py.Assert(HeicBrands.Any(file.HasBrand), "brands");
        if (file.HasBrand("avif") || file.HasBrand("avis"))
            features.Add("heic.brands=avif");

        DescribeLayout(file, data, features);

        // A file with the brand of the image sequences presents its sequence; any other file its primary item
        return file.IsSequence ? DescribeSequence(tools, scratch, file, features) : DescribeStill(tools, scratch, file, features);
    }

    /// <summary>The brands of ISO/IEC 23008-12 for HEVC: images (heic, heix), image sequences (hevc, hevx).</summary>
    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx"];

    private static void DescribeLayout(ReferenceHeif file, byte[] data, SortedSet<string> features)
    {
        var boxes = file.TopLevelBoxes;
        var index = boxes.Select((box, i) => (box, i)).First(pair => pair.box.Type == (file.IsSequence ? "moov" : "meta")).i;
        var media = boxes.Select((box, i) => (box, i)).Where(pair => pair.box.Type == "mdat").Select(pair => pair.i).ToList();
        if (media.Any(i => i < index))
            features.Add("heic.layout=media-first");

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
            features.Add("heic.boxes=" + string.Join(',', kinds));

        // The bytes a forward-only reader never needs: what follows the last byte of the presented items or samples
        long last;
        if (file.IsSequence)
        {
            var tracks = new[] { file.ColorTrack, file.AlphaTrack }.OfType<ReferenceHeif.Track>();
            last = tracks.SelectMany(track => track.Samples).Max(sample => sample.Offset + sample.Size);
            last = Math.Max(last, boxes[index].Offset + boxes[index].Size);
            var (exif, xmp) = file.FindSequenceMetadata();
            foreach (var item in new[] { exif, xmp }.OfType<ReferenceHeif.Item>().Where(item => item.ConstructionMethod == 0))
            {
                last = Math.Max(last, item.Extents.Max(extent => extent.Offset + extent.Length));
            }
        }
        else
        {
            last = boxes[index].Offset + boxes[index].Size;
            foreach (var item in PresentedItems(file).Where(item => item.ConstructionMethod == 0 && item.Extents.Count > 0))
            {
                last = Math.Max(last, item.Extents.Max(extent => extent.Offset + extent.Length));
            }
        }

        if (last < data.Length)
            features.Add("heic.trailingData");
    }

    /// <summary>The items whose data a decoder of the primary item reads: the coded items of the image and of its alpha
    /// plane, the grid descriptors, and the Exif and XMP items.</summary>
    private static List<ReferenceHeif.Item> PresentedItems(ReferenceHeif file)
    {
        var result = new List<ReferenceHeif.Item>();
        void AddImage(ReferenceHeif.Item? item)
        {
            if (item is null)
                return;

            result.Add(item);
            if (item.Type == "grid")
                result.AddRange(file.GetGridTiles(item));
        }

        var primary = file.PrimaryItem!;
        AddImage(primary);
        AddImage(file.AlphaItem);
        result.AddRange(file.FindDescriptions(primary.Id).Where(item => item.Type == "Exif" || item.ContentType == "application/rdf+xml"));
        return result;
    }

    private static HeicReference DescribeStill(HeicFileTools tools, FullPath scratch, ReferenceHeif file, SortedSet<string> features)
    {
        var primary = file.PrimaryItem ?? throw new ToolException("the file has no primary item.");
        var (color, payloads, grid) = DecodeItem(tools, scratch, file, primary, "color");
        var size = primary.Size ?? throw new ToolException("the primary item has no ispe property.");
        if (size != (color.Width, color.Height))
            throw new ToolException(Inv($"the ispe property declares {size.Width}x{size.Height} and the picture is {color.Width}x{color.Height} once cropped to its conformance window."));

        PlaneImage? alpha = null;
        if (file.AlphaItem is { } alphaItem)
        {
            (alpha, var alphaPayloads, _) = DecodeItem(tools, scratch, file, alphaItem, "alpha");
            payloads.AddRange(alphaPayloads);
            Py.Assert(!file.ItemReferences.Any(reference => reference.Type == "prem"), "premultiplied alpha is not a valid fixture");
            features.Add("heic.alpha");
            features.Add("heic.alpha.urn=" + (alphaItem.AuxiliaryType == ReferenceHeif.HevcAlphaAuxiliaryType ? "hevc" : "mpegB"));
            features.Add("heic.alpha.subsampling=" + alpha.Subsampling);
            if (alpha.BitDepth != color.BitDepth)
                features.Add(Inv($"heic.alpha.bitDepth={alpha.BitDepth}"));
            if (!alpha.AlphaFullRange)
                features.Add("heic.alpha.range=limited");
        }

        var nclx = primary.Nclx;
        var (matrix, fullRange, cicp) = ResolveColor(nclx, color);
        var crop = (0, 0, color.Width, color.Height);
        if (primary.CleanAperture is { } aperture)
        {
            crop = aperture.GetRectangle(color.Width, color.Height) ?? throw new ToolException("the clean aperture is not a whole number of samples.");
            Py.Assert(primary.Transforms[0].Type == "clap", "clap must come first");
            features.Add("heic.clap");
        }

        foreach (var transform in primary.Transforms.Where(t => t.Type is "irot" or "imir"))
        {
            features.Add(Inv($"heic.{transform.Type}={transform.Payload.Span[0] & 3}"));
        }

        // MIAF orders the transformations clap, irot, imir; a valid fixture follows it
        var order = primary.Transforms.Select(t => t.Type).ToList();
        Py.Assert(order.SequenceEqual(order.OrderBy(type => Array.IndexOf(["clap", "irot", "imir"], type))), "transformations must be in the MIAF order");

        var presented = PresentedItems(file);
        var coded = presented.Where(item => item.Type == "hvc1").ToList();
        if (grid is not null)
        {
            features.Add("heic.grid=" + grid);

            // The transformations apply once, to the assembled image: a tile that declares one is not a valid fixture
            Py.Assert(file.GetGridTiles(primary).All(tile => tile.Transforms.Count == 0), "the tiles of a grid have no transformation");
        }

        if (presented.Any(item => item.ConstructionMethod == 1))
            features.Add("heic.idat");
        if (presented.Any(item => item.Extents.Count > 1))
            features.Add(Inv($"heic.extents={presented.Max(item => item.Extents.Count)}"));

        // What a reader of the primary item ignores: thumbnails, auxiliary images that are not its alpha plane, and the
        // other images of the file
        if (file.ItemReferences.Any(reference => reference.Type == "thmb" && reference.ToItemIds.Contains(primary.Id)))
            features.Add("heic.thumbnail");
        var otherAuxiliaries = file.ItemReferences.Where(reference => reference.Type == "auxl" && reference.ToItemIds.Contains(primary.Id)).Select(reference => file.FindItem(reference.FromItemId))
            .OfType<ReferenceHeif.Item>().Where(item => item.AuxiliaryType is not (ReferenceHeif.AlphaAuxiliaryType or ReferenceHeif.HevcAlphaAuxiliaryType)).ToList();
        if (otherAuxiliaries.Count > 0)
            features.Add("heic.auxiliary=other");
        var used = presented.Select(item => item.Id).ToHashSet();
        if (file.Items.Any(item => item.Type is "hvc1" or "grid" && !item.IsHidden && !used.Contains(item.Id) && !file.ItemReferences.Any(reference => reference.Type is "thmb" or "auxl" && reference.FromItemId == item.Id)))
            features.Add("heic.otherImages");

        AddRecordFeatures(features, coded.Select(item => (item.HevcConfiguration!, file.ReadItemData(item))));
        var descriptions = file.FindDescriptions(primary.Id);
        var exifItem = descriptions.FirstOrDefault(item => item.Type == "Exif");
        var xmpItem = descriptions.FirstOrDefault(item => item.ContentType == "application/rdf+xml");
        var icc = primary.IccProfile?.ToArray();
        AddColorFeatures(features, color, payloads, matrix, fullRange, nclx is not null, icc is not null);
        var metadata = new List<string>();
        if (exifItem is not null)
            metadata.Add("exif");
        if (xmpItem is not null)
            metadata.Add("xmp");
        if (metadata.Count > 0)
            features.Add("heic.metadata=" + string.Join(',', metadata));

        return new HeicReference
        {
            Frames = [ToFrame(color, alpha, matrix, fullRange, crop)],
            PixelFormat = PixelFormatOf(color, alpha),
            ColorModel = ColorModelOf(color, alpha is not null, matrix),
            BitsPerComponent = color.BitDepth,
            Orientation = primary.Orientation,
            Cicp = cicp,
            Icc = icc,
            Exif = exifItem is null ? null : file.ReadExifPayload(exifItem),
            Xmp = xmpItem is null ? null : file.ReadItemData(xmpItem),
            IsMatrixed = !color.Monochrome && matrix != 0,
            Timings = [],
            IsSequence = false,
            ColorTrack = null,
            Features = features,
            Payloads = payloads,
        };
    }

    /// <summary>The byte stream of a track: the NAL units of its configuration record, then those of its samples in
    /// decoding order.</summary>
    private static byte[] TrackStream(ReferenceHeif file, ReferenceHeif.Track track)
    {
        var configuration = track.HevcConfiguration ?? throw new ToolException("a track has no hvcC box.");
        return [.. track.Samples.SelectMany((sample, index) => configuration.ToByteStream(file.ReadSample(sample), includeParameterSets: index == 0))];
    }

    /// <summary>Decodes a track: one picture per sample, every picture is output, and the composition times of the
    /// samples give the output order of the bitstream (clause C.5.2), which is the order of the returned images.</summary>
    private static (HevcCorpus.Payload Payload, List<PlaneImage> Images, IReadOnlyList<(int SampleIndex, long Duration)> Order) DecodeTrack(HeicFileTools tools, FullPath scratch, ReferenceHeif file, ReferenceHeif.Track track, string role)
    {
        var payload = tools.Hevc.Decode(scratch, role, TrackStream(file, track));
        CheckConfiguration(track.HevcConfiguration!, payload, role);
        if (payload.CodedPictures != track.Samples.Count)
            throw new ToolException(Inv($"{role}: the track has {track.Samples.Count} samples and its stream {payload.CodedPictures} pictures."));
        if (payload.Outputs.Count != payload.CodedPictures)
            throw new ToolException(Inv($"{role}: {payload.CodedPictures - payload.Outputs.Count} pictures of the track are not output."));

        var order = track.PresentationOrder;
        if (!order.Select(entry => entry.SampleIndex).SequenceEqual(payload.Outputs.Select(picture => picture.DecodeIndex)))
            throw new ToolException($"{role}: the composition times order the samples as {string.Join(' ', order.Select(entry => entry.SampleIndex))} and the bitstream outputs its pictures as {string.Join(' ', payload.Outputs.Select(picture => picture.DecodeIndex))}.");
        if (payload.Outputs.Any(picture => (picture.Width, picture.Height) != (track.Width, track.Height)))
            throw new ToolException($"{role}: the pictures do not have the size of the sample entry.");

        return (payload, [.. payload.Outputs.Select(picture => ToPlaneImage(payload, picture))], order);
    }

    private static HeicReference DescribeSequence(HeicFileTools tools, FullPath scratch, ReferenceHeif file, SortedSet<string> features)
    {
        var track = file.ColorTrack ?? throw new ToolException("the file has no track of HEVC images.");
        features.Add("heic.sequence");
        var (payload, images, order) = DecodeTrack(tools, scratch, file, track, "color");
        List<PlaneImage>? alphaImages = null;
        var payloads = new List<HevcCorpus.Payload> { payload };
        if (file.AlphaTrack is { } alphaTrack)
        {
            (var alphaPayload, alphaImages, var alphaOrder) = DecodeTrack(tools, scratch, file, alphaTrack, "alpha");
            payloads.Add(alphaPayload);
            Py.Assert(alphaImages.Count == images.Count, "one alpha plane per image");
            Py.Assert(alphaOrder.Select(entry => entry.Duration).SequenceEqual(order.Select(entry => entry.Duration)), "the alpha planes have the durations of the images");
            Py.Assert(!track.References.Any(reference => reference.Type == "prem"), "premultiplied alpha is not a valid fixture");
            features.Add("heic.alpha");
            features.Add("heic.alpha.urn=" + (alphaTrack.AuxiliaryType == ReferenceHeif.HevcAlphaAuxiliaryType ? "hevc" : "mpegB"));
            features.Add("heic.alpha.subsampling=" + alphaImages[0].Subsampling);
        }

        var first = images[0];
        var (matrix, fullRange, cicp) = ResolveColor(track.Nclx, first);
        var icc = track.IccProfile?.ToArray();
        AddColorFeatures(features, first, payloads, matrix, fullRange, track.Nclx is not null, icc is not null);
        AddRecordFeatures(features, new[] { track, file.AlphaTrack }.OfType<ReferenceHeif.Track>().SelectMany(candidate => candidate.Samples.Select(sample => (candidate.HevcConfiguration!, file.ReadSample(sample)))));
        features.Add("heic.seq.sampleEntry=" + track.SampleEntryType);
        features.Add("heic.seq.editList=" + (track.EditList is not { } edits ? "none" : !edits.IsRepeating ? "once" : track.Duration is null ? "repeat-indefinite" : "repeat"));
        if (track.EditList is { Entries: [{ MediaTime: not 0 }, ..] })
            features.Add("heic.seq.editMediaTime");
        features.Add("heic.seq.sampleSizes=" + track.SampleSizeBox);
        features.Add("heic.seq.chunkOffsets=" + track.ChunkOffsetBox);
        if (payload.HasInterPictures)
            features.Add("heic.seq.interFrames");
        if (track.CompositionOffsets is not null)
            features.Add("heic.seq.compositionOffsets");
        if (!order.Select(entry => entry.SampleIndex).SequenceEqual(Enumerable.Range(0, order.Count)))
            features.Add("heic.seq.reordered");

        // The Exif and XMP items of the sequence: those of the meta box of the track, or else of the meta box of the file
        // when it precedes the moov box (ReferenceHeif.FindSequenceMetadata)
        var (exifItem, xmpItem) = file.FindSequenceMetadata();
        var exif = exifItem is null ? null : file.ReadExifPayload(exifItem);
        var xmp = xmpItem is null ? null : file.ReadItemData(xmpItem);
        var metadataItems = new[] { exifItem, xmpItem }.OfType<ReferenceHeif.Item>().ToList();
        var metadata = new List<string>();
        if (exifItem is not null)
            metadata.Add("exif");
        if (xmpItem is not null)
            metadata.Add("xmp");
        if (metadata.Count > 0)
        {
            features.Add("heic.metadata=" + string.Join(',', metadata));
            features.Add("heic.seq.metadataSource=" + (track.HasMeta ? "track" : "file"));
            if (metadataItems.Any(item => item.ConstructionMethod == 1))
                features.Add("heic.idat");

            // Item data that follow the samples: the last image is only complete with them
            var lastSample = track.Samples.Max(sample => sample.Offset);
            if (metadataItems.Any(item => item.ConstructionMethod == 0 && item.Extents.Any(extent => extent.Offset > lastSample)))
                features.Add("heic.seq.metadataAfterSamples");
        }

        // The image items of the meta box of the file, which a reader of the sequence does not present
        if (file.HasMeta && file.PrimaryItem is not null)
            features.Add("heic.seq.fileImageNotRead");

        // The chunks of a track may be anywhere in the file: here the first sample is not the first one in the file
        if (track.Samples.Skip(1).Any(sample => sample.Offset < track.Samples[0].Offset))
            features.Add("heic.seq.samplesOutOfOrder");

        // The clean aperture of the sample entry crops every image of the track, and the alpha track has the same one
        var crop = (0, 0, track.Width, track.Height);
        if (track.CleanAperture is { } aperture)
        {
            crop = aperture.GetRectangle(track.Width, track.Height) ?? throw new ToolException("the clean aperture of the track is not a whole number of samples.");
            features.Add("heic.clap");
        }

        if (file.AlphaTrack is { } alphaAperture)
            Py.Assert(alphaAperture.CleanAperture == track.CleanAperture, "the alpha track has the clean aperture of the color track");

        var frames = images.Select((image, i) => ToFrame(image, alphaImages?[i], matrix, fullRange, crop)).ToList();
        return new HeicReference
        {
            Frames = frames,
            PixelFormat = PixelFormatOf(first, alphaImages?[0]),
            ColorModel = ColorModelOf(first, alphaImages is not null, matrix),
            BitsPerComponent = first.BitDepth,
            Orientation = 1,
            Cicp = cicp,
            Icc = icc,
            Exif = exif,
            Xmp = xmp,
            IsMatrixed = !first.Monochrome && matrix != 0,
            Timings = [.. order.Select(entry => new Timing(entry.Duration, track.Timescale))],
            IsSequence = true,
            ColorTrack = track,
            Features = features,
            Payloads = payloads,
        };
    }

    /// <summary>The colr box of type nclx describes the colors; otherwise the video signal description of the bitstream
    /// does. Unspecified matrix coefficients are BT.601 in the conversion; the description stays what the file says, or
    /// none; and without video signal description the samples use the limited range (ITU-T H.265, clause E.3.1).</summary>
    private static (int Matrix, bool FullRange, ReferenceHeif.ColorDescription? Cicp) ResolveColor(ReferenceHeif.ColorDescription? nclx, PlaneImage color)
    {
        if (nclx is not null)
            return (nclx.MatrixCoefficients, nclx.FullRange, nclx);

        return color.ColorDescription is var (primaries, transfer, matrix)
            ? (matrix, color.FullRange, new ReferenceHeif.ColorDescription(primaries, transfer, matrix, color.FullRange))
            : (2, color.FullRange, null);
    }

    /// <summary>What the configuration records of the coded items or of the tracks say beyond the sample format: the
    /// size of the lengths of the NAL units, arrays that are not complete, parameter sets in the data, several
    /// parameter sets of a type.</summary>
    private static void AddRecordFeatures(SortedSet<string> features, IEnumerable<(ReferenceHeif.HevcConfiguration Configuration, byte[] Data)> streams)
    {
        foreach (var (configuration, data) in streams)
        {
            features.Add(Inv($"heic.hvcc.lengthSize={configuration.NalUnitLengthSize}"));
            if (configuration.Arrays.Any(array => !array.IsComplete))
                features.Add("heic.hvcc.incompleteArrays");
            if (configuration.Arrays.Any(array => array.NalUnitType is 33 or 34 && array.NalUnits.Count > 1))
                features.Add("heic.hvcc.severalParameterSets");
            if (configuration.Arrays.Any(array => array.NalUnitType is 39 or 40))
                features.Add("heic.hvcc.sei");
            if (configuration.SplitNalUnits(data).Any(unit => HevcCorpus.IsParameterSet(HevcCorpus.NalUnitType(unit.ToArray()))))
                features.Add("heic.parameterSets=in-band");
        }
    }

    private static void AddColorFeatures(SortedSet<string> features, PlaneImage color, List<HevcCorpus.Payload> payloads, int matrix, bool fullRange, bool hasNclx, bool hasIcc)
    {
        features.Add(Inv($"heic.bitDepth={color.BitDepth}"));
        features.Add("heic.subsampling=" + color.Subsampling);
        if (!color.Monochrome)
            features.Add(Inv($"heic.matrix={matrix}"));
        features.Add("heic.range=" + (fullRange ? "full" : "limited"));
        var colr = new List<string>();
        if (hasIcc)
            colr.Add("icc");
        if (hasNclx)
            colr.Add("nclx");
        features.Add("heic.colr=" + (colr.Count == 0 ? "none" : string.Join(',', colr)));
        features.Add("heic.videoSignal=" + (!color.HasVideoSignal ? "none" : color.ColorDescription is null ? "range" : "colour"));
        if (color.ChromaFormat == 1)
            features.Add("heic.chromaLoc=" + (color.ChromaLocation is { } location ? Inv($"{location}") : "default"));
        if (payloads.Any(payload => payload.HasConformanceWindow))
            features.Add("heic.conformanceWindow");

        // The size that counts toward the limits of the canvas when the pictures of the color image are decoded, when it
        // is not the size of the pictures they output: their conformance window removes more than the padding of the coded
        // picture (HevcCorpus.Payload.CheckedSize)
        if (payloads[0].CheckedSize != (payloads[0].Outputs[0].Width, payloads[0].Outputs[0].Height))
            features.Add(Inv($"heic.checkedSize={payloads[0].CheckedSize.Width}x{payloads[0].CheckedSize.Height}"));
        if (payloads.All(payload => payload.SignalsTransquantBypass))
            features.Add("heic.lossless");
    }

    /// <summary>The documented pixel format of an untyped load: the smallest format that stores the samples exactly, Gray8,
    /// La16 (monochrome with alpha), Rgb24 or Rgba32 at 8 bits; Gray16, La32, Rgb48 or Rgba64 for deeper samples (the
    /// deepest of the color and alpha planes).</summary>
    private static string PixelFormatOf(PlaneImage color, PlaneImage? alpha)
    {
        var deep = color.BitDepth > 8 || alpha?.BitDepth > 8;
        return (color.Monochrome, alpha is not null, deep) switch
        {
            (true, false, false) => "Gray8",
            (true, false, true) => "Gray16",
            (true, true, false) => "La16",
            (true, true, true) => "La32",
            (false, false, false) => "Rgb24",
            (false, false, true) => "Rgb48",
            (false, true, false) => "Rgba32",
            (false, true, true) => "Rgba64",
        };
    }

    private static string ColorModelOf(PlaneImage color, bool hasAlpha, int matrix)
    {
        if (color.Monochrome)
            return hasAlpha ? "GrayscaleAlpha" : "Grayscale";

        return matrix != 0 ? "YCbCr" : hasAlpha ? "Rgba" : "Rgb";
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Manifest entries
    // -----------------------------------------------------------------------------------------------------------------

    private static List<string> LayoutsOf(HeicReference reference) => reference.PixelFormat switch
    {
        "Rgb24" => ["rgba8", "rgb8"],
        "Gray8" => ["rgba8", "gray8"],
        "Gray16" => ["rgba16le", "gray16le"],
        "Rgba64" or "La32" or "Rgb48" => ["rgba16le"],
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

    /// <summary>Whether the tags include a tag, or one of the comma-separated values of a <c>key=value</c> tag.</summary>
    private static bool HasFeature(IReadOnlyCollection<string> features, string tag)
    {
        if (features.Contains(tag, StringComparer.Ordinal))
            return true;

        var separator = tag.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0)
            return false;

        var prefix = tag[..(separator + 1)];
        return features.Any(feature => feature.StartsWith(prefix, StringComparison.Ordinal) && feature[prefix.Length..].Split(',').Contains(tag[prefix.Length..], StringComparer.Ordinal));
    }

    /// <summary>A decoder that a recipe expects to be left out of the agreement on a stream of its file.</summary>
    private sealed record ExpectedExclusion(string Decoder, string Reason);

    /// <summary>Records a valid fixture: the input, its reference derived from its bytes, the cross-checks and the policy.</summary>
    /// <param name="origin"><c>generated</c> (written by an encoder) or <c>hand-authored</c> (assembled box by box).</param>
    /// <param name="function">The builder and what it does, recorded after the script path.</param>
    /// <param name="payloadsOf">The valid fixture whose payloads the file is assembled around, or <see langword="null"/>.</param>
    /// <param name="excluded">The decoder that the recipe expects to be left out of the agreement, and why.</param>
    private static HeicReference AddValidHeic(ContainerContext context, string name, byte[] data, string origin, string function, List<string> tools, IList<string>? commands, Obj? parameters, string? notes = null, string[]? requires = null, string? payloadsOf = null, ExpectedExclusion? excluded = null)
    {
        var id = "heic/" + name;
        var scratch = context.NewScratch(name);
        HeicReference reference;
        try
        {
            reference = Describe(context.Tools, scratch, data);
        }
        catch (Exception error) when (error is ToolException or InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            throw new FatalException($"{id}: {error.Message}");
        }

        var missing = (requires ?? []).Where(tag => !HasFeature([.. reference.Features], tag)).ToList();
        if (missing.Count > 0)
            throw new FatalException($"{id}: the file does not have {string.Join(", ", missing)} (it has {string.Join(" ", reference.Features)}).");

        // The decoders that are left out of a stream of the file: the recipe expects each one, and only those
        var leftOut = reference.Payloads.SelectMany(payload => payload.Excluded).GroupBy(exclusion => exclusion.Decoder, StringComparer.Ordinal).Select(group => group.First()).ToList();
        var unexpected = leftOut.Where(exclusion => exclusion.Decoder != excluded?.Decoder).ToList();
        if (unexpected.Count > 0)
            throw new FatalException($"{id}: the recipe does not expect {string.Join(" and ", unexpected.Select(exclusion => $"{exclusion.Decoder} ({exclusion.Outcome}: {exclusion.Observed})"))} to be left out of the agreement of the decoders.");
        if (excluded is not null && leftOut.Count == 0)
            throw new FatalException($"{id}: the recipe expects {excluded.Decoder} to be left out, and it agrees with the others.");

        var isSequence = reference.IsSequence;
        var extension = isSequence ? ".heics" : ".heic";
        var path = scratch / (name + extension);
        File.WriteAllBytes(path, data);
        var heifDec = HeifDecCrossCheck(context, scratch, path, reference);
        var checks = new List<object?> { heifDec };
        context.HeifDecChecks.Add(name, heifDec);
        if (!heifDec.ContainsKey("maxAbsoluteError") && payloadsOf is not null && context.HeifDecChecks[payloadsOf] is var source && source.TryGetValue("maxAbsoluteError", out var sourceMax)
            && context.References[payloadsOf].Frames.Select(frame => Bytes.Sha256Hex(RgbaRaw(frame, frame.Depth == 8 ? "rgba8" : "rgba16le"))).SequenceEqual(reference.Frames.Select(frame => Bytes.Sha256Hex(RgbaRaw(frame, frame.Depth == 8 ? "rgba8" : "rgba16le")))))
        {
            // heif-dec does not read this layout; what it measures on the same payloads in the layout it reads still holds
            checks.Add(new Obj
            {
                ["decoder"] = (string)source["decoder"]! + ", on heic/" + payloadsOf,
                ["command"] = source["command"],
                ["maxAbsoluteError"] = sourceMax,
                ["meanAbsoluteError"] = source["meanAbsoluteError"],
                ["result"] = source["result"],
                ["notes"] = "The same payloads in the layout heif-dec reads; the references are the same files.",
            });
        }

        var input = context.Corpus.Write(id + extension, data);
        input["role"] = "encoded";
        context.Inputs.Add(name, data);
        context.InputEntries.Add(name, input);
        context.References.Add(name, reference);
        context.Exclusions.Add(name, excluded);
        context.PayloadTools.Add(name, tools[0].EndsWith("(payloads)", StringComparison.Ordinal) ? tools[0] : tools[0] + " (payloads)");
        var layouts = LayoutsOf(reference);
        var frames = new List<object?>();
        for (var i = 0; i < reference.Frames.Count; i++)
        {
            var frame = new Obj();
            if (isSequence)
            {
                var timing = reference.Timings[i];
                frame["duration"] = CommonCorpus.Normalize(timing.Duration, timing.Timescale);
                if (reference.Frames.Count > 1)
                    frame["encodedDelay"] = new Obj { ["sampleDelta"] = timing.Duration, ["timescale"] = (long)timing.Timescale };
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
            ["animation"] = animation,
            ["resolution"] = null,
            ["profiles"] = new Obj
            {
                ["icc"] = reference.Icc is null ? null : CommonCorpus.ProfileRecord(reference.Icc),
                ["exif"] = reference.Exif is null ? null : CommonCorpus.ProfileRecord(reference.Exif),
                ["xmp"] = reference.Xmp is null ? null : CommonCorpus.ProfileRecord(reference.Xmp),
            },
            ["text"] = new List<object?>(),
            ["frameCount"] = reference.Frames.Count,
            ["frames"] = frames,
            ["poster"] = null,
        };

        var fixture = new Obj
        {
            ["id"] = id,
            ["kind"] = "valid",
            ["format"] = "heic",
            ["input"] = input,
            ["provenance"] = context.Corpus.Provenance(origin, function, parameters, tools, commands),
            ["features"] = reference.Features.ToList(),
            ["reference"] = new Obj
            {
                ["method"] = "decoded",
                ["description"] = ReferenceDescription(context, reference, excluded),
                ["decoder"] = context.Tools.Hevc.DecodersLabel,
                ["backend"] = "ReferenceHeif and ReferenceYuvConverter (test harness)",
                ["command"] = "hevcVectors.decoders[].command",
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

    private static string ReferenceDescription(ContainerContext context, HeicReference reference, ExpectedExclusion? excluded)
    {
        var colors = reference.IsMatrixed ? "converted with exact rationals (matrix, range and chroma sample location of the file)" : "scaled without matrix";
        var agree = HevcCorpus.DecoderNames.Where(decoder => reference.Payloads.All(payload => payload.Agree.Contains(decoder, StringComparer.Ordinal))).Select(context.Tools.Hevc.Label).ToList();
        var description = "Planes of the " + (reference.IsSequence ? "samples" : "items") + " decoded identically by " + string.Join(agree.Count > 2 ? ", " : " and ", agree) + ", " + colors + ".";
        if (excluded is not null)
        {
            var observed = reference.Payloads.SelectMany(payload => payload.Excluded).First(exclusion => exclusion.Decoder == excluded.Decoder);
            description += $" {context.Tools.Hevc.Label(excluded.Decoder)} is left out ({observed.Outcome}: {observed.Observed}): {excluded.Reason}";
        }

        return description;
    }

    /// <summary>Exact for the content no matrix applies to. Matrixed content: one unit, and the measured mean disagreement
    /// of the library plus 0.01 (the library rounds its coefficients to 16 fractional bits; the reference does not round
    /// them), when an independent decoder also disagrees with the reference.</summary>
    private static Obj Policy(string id, HeicReference reference, List<Obj> checks)
    {
        var measured = checks.Where(check => check.ContainsKey("maxAbsoluteError") && !(check.TryGetValue("excludedFromTolerance", out var excluded) && excluded is true)).ToList();
        if (!reference.IsMatrixed || measured.Count == 0 || measured.All(check => Convert.ToInt32(check["maxAbsoluteError"], CultureInfo.InvariantCulture) == 0))
            return new Obj { ["mode"] = "exact" };

        var (_, _, justifiedMax, justifiedMean) = CommonCorpus.JpegJustifiedTolerance(measured);
        var hasMeasurement = LibraryHeicMeasurements.TryGetValue(id, out var library);
        var mean = hasMeasurement ? Math.Ceiling(Math.Round((library.Mean + 0.01) * 100, 6, MidpointRounding.AwayFromZero)) / 100 : 0.5;
        Py.Assert(!hasMeasurement || library.Max <= 1, id + ": the library disagrees with the reference by more than one unit");
        Py.Assert(1 <= justifiedMax && (mean <= justifiedMean || !hasMeasurement), id + ": the tolerance is not justified by the cross-checks");
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

    private static byte[] RgbaRaw(Img img, string layout) => new Img(img.Width, img.Height, "rgba", img.Depth, img.Rgba()).Raw(layout);

    /// <summary>libheif heif-dec: its own container parser, libde265 and its own conversion. It writes PNG files (read back
    /// losslessly through FFmpeg), applies the clean aperture and the orientation, and writes deep samples as 16-bit ones.
    /// A sequence is decoded image by image, without its repetitions (<c>--ignore-editlist</c>).</summary>
    private static Obj HeifDecCrossCheck(ContainerContext context, FullPath scratch, FullPath path, HeicReference reference)
    {
        var tools = context.Tools;
        var deep = reference.Frames[0].Depth == 16;
        var output = scratch / "heif-dec.png";
        var command = new List<string> { tools.HeifDec, "--quiet" };
        if (reference.IsSequence)
            command.AddRange(["-S", "--ignore-editlist"]);
        command.AddRange([path.Value, output.Value]);
        var check = new Obj
        {
            ["decoder"] = tools.HeifDecLabel,
            ["command"] = CommonCorpus.Display(command, tools, [(path.Value, "{input}"), (output.Value, "{output}.png")]),
        };
        var result = Proc.Exec(command);
        var files = reference.IsSequence
            ? Enumerable.Range(1, reference.Frames.Count).Select(i => scratch / Inv($"heif-dec-{i}.png")).ToList()
            : [output];
        if (result.ExitCode != 0 || files.Any(file => !File.Exists(file)) || (reference.IsSequence && File.Exists(scratch / Inv($"heif-dec-{reference.Frames.Count + 1}.png"))))
        {
            // heif-dec prints what it does on both streams: its error is the line that says so, or else its last line
            var lines = (Encoding.UTF8.GetString(result.Stdout) + Encoding.UTF8.GetString(result.Stderr)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var message = lines.LastOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Could not", StringComparison.Ordinal)) ?? lines.LastOrDefault() ?? "error";
            check["result"] = "differs";
            check["notes"] = result.ExitCode != 0
                ? "heif-dec rejects the file: " + message.Replace(path.Value, "{input}", StringComparison.Ordinal)
                : Inv($"heif-dec did not write the {files.Count} expected images.");
            return check;
        }

        var oriented = reference.Frames.Select(frame => Orient(frame, reference.Orientation)).ToList();
        var (pixelFormat, bytesPerPixel, sample, layout) = deep ? ("rgba64le", 8, 2, "rgba16le") : ("rgba", 4, 1, "rgba8");
        // The size of each image heif-dec wrote, from the IHDR chunk of its PNG file. It does not apply the clap box of a
        // sample entry: it then writes the whole pictures of the track, which are cropped here before they are compared
        var written = files.Select(file => File.ReadAllBytes(file)).Select(png => (Width: (int)Bytes.U32BE(png, 16), Height: (int)Bytes.U32BE(png, 20))).Distinct().ToList();
        var crop = (X: 0, Y: 0, oriented[0].Width, oriented[0].Height);
        var cropped = false;
        if (written.Count == 1 && reference is { IsSequence: true, IsTransformed: false, ColorTrack: { CleanAperture: { } aperture } track } && written[0] == (track.Width, track.Height)
            && aperture.GetRectangle(track.Width, track.Height) is { } rectangle && (rectangle.Width, rectangle.Height) == (oriented[0].Width, oriented[0].Height))
        {
            crop = rectangle;
            cropped = true;
        }
        else if (written.Count != 1 || written[0] != (oriented[0].Width, oriented[0].Height))
        {
            check["result"] = "differs";
            check["notes"] = Inv($"heif-dec writes images of {string.Join(", ", written.Select(size => Inv($"{size.Width}x{size.Height}")))}, not the {oriented[0].Width}x{oriented[0].Height} of the reference.");
            return check;
        }

        List<byte[]> decoded;
        try
        {
            decoded = [.. files.Select(file => CommonCorpus.FfmpegDecode(tools, file, pixelFormat, written[0].Width, written[0].Height, bytesPerPixel, animated: false).Frames[0])];
            if (cropped)
            {
                decoded = [.. decoded.Select(pixels =>
                {
                    var result = new byte[crop.Width * crop.Height * bytesPerPixel];
                    for (var y = 0; y < crop.Height; y++)
                        pixels.AsSpan((((crop.Y + y) * written[0].Width) + crop.X) * bytesPerPixel, crop.Width * bytesPerPixel).CopyTo(result.AsSpan(y * crop.Width * bytesPerPixel));

                    return result;
                })];
            }
        }
        catch (Exception error) when (error is ToolException or InvalidOperationException)
        {
            check["result"] = "differs";
            check["notes"] = Inv($"heif-dec writes an image of another size than the {oriented[0].Width}x{oriented[0].Height} of the reference.");
            return check;
        }

        var expected = oriented.Select(frame => RgbaRaw(frame, layout)).ToList();
        var difference = reference.IsMatrixed
            ? "Independent conversion (its own chroma upsampling and rounding)."
            : reference.Features.Contains("heic.range=limited")
                ? "Known difference: heif-dec returns limited-range samples that no matrix applies to as they are stored, and the reference expands them to the full range."
                : "Its own sample scaling.";
        Record(check, expected, decoded, sample, difference);
        if (reference.IsTransformed)
            check["notes"] = (check.TryGetValue("notes", out var text) ? text + " " : "") + Inv($"Compared with the reference displayed with orientation {reference.Orientation} (heif-dec applies irot and imir).");
        if (cropped)
            check["notes"] = (check.TryGetValue("notes", out var text) ? text + " " : "") + Inv($"heif-dec does not apply the clap box of a sample entry: it writes the {written[0].Width}x{written[0].Height} pictures of the track, which are cropped to the aperture before they are compared.");
        return check;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Verification (the "verify" subcommand): the committed references re-derived with the pinned tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Re-derives the reference of every valid HEIC fixture from its committed input (ReferenceHeif, the three
    /// decoders, ReferenceYuvConverter) and compares it with the committed buffers, which must be identical.</summary>
    public static (int Verified, List<string> Failures) VerifyContainer()
    {
        var failures = new List<string>();
        var fixtures = CorpusDriver.Fixtures(CorpusDriver.LoadCommittedManifest()).Where(f => IsHeicFixture(f) && (string)f["kind"]! == "valid").ToList();
        if (fixtures.Count == 0)
            return (0, failures);

        var tools = new HeicFileTools(acceptVersions: true, encoders: false);
        var root = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-heic-verify-").FullName);
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
                catch (Exception error) when (error is ToolException or InvalidDataException or InvalidOperationException or NotSupportedException)
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
