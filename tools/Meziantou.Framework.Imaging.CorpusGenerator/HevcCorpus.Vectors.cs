using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The recipes of the HEVC conformance vectors: a deterministic lavfi source, a pixel format, an encoder and its options.
// Requires lists the feature tags a vector exists for; they are read back from the encoded headers (HevcCorpus.Trace.cs),
// so a recipe whose encoder stops using the tool fails the generation instead of silently losing coverage.
//
// Traps (each one cost a vector during the selection):
// - the "testsrc2" source rounds its size down to even numbers: an odd size is cropped from the source in the pixel
//   format of the stream (Cropped), and the generation checks the size of the output pictures;
// - an x265 parameter is a request: "open-gop" only gives RASL pictures with enough B frames before the CRA picture,
//   "weightp" only codes weights for a fade, "log2-max-poc-lsb" is raised with the number of B frames; the tags tell;
// - x265 codes wavefronts as soon as a picture has two CTB rows (the helpers pass "wpp=0" unless a recipe asks for them),
//   refuses several slices without wavefronts and a CTB larger than the picture, and "deblock=0" is a deblocking filter
//   with offsets of zero ("no-deblock=1" turns it off);
// - x265 writes a wrong CRC (hash=2) for the chroma planes of a picture, unless the picture is exactly one row of whole
//   CTBs: TAppDecoder rejects it. Its MD5 and its checksum are right. The CRC recipes of x265 are monochrome, or one CTB
//   high;
// - x265 does not write the hashes of the pictures it codes when an inter picture is a single 64x64 CTB: the three
//   decoders agree with each other and not with the hashes, from the second or third picture on. Its inter recipes
//   have 32x32 CTBs unless they ask for another size on a larger picture;
// - x265 signals level 8.5 for a lossless stream, and TAppDecoder refuses a 16x16 CTB from level 5 (the limit of clause
//   A.4.1): the lossless recipes have 32x32 or 64x64 CTBs, except the one that records this (HM left out);
// - x265 does not always encode an inter picture of a 12-bit lossless stream without loss, and then writes the hash of the
//   source: nothing is ever expected to be the source, and "hevc.lossless" is measured on the decoded pictures;
// - FFmpeg, libde265 and HM decode three different pictures for a 12-bit 4:4:4 stream of x265 at QP 51 (HM alone finds
//   the hashes), and for tiles with wavefronts in a Main profile stream of kvazaar: no vector;
// - dec265 computes the checksum (hash type 2) of a picture above 8 bits as if it had 8: it is not asked to check those.
//   Its check ("-c") only compares the hash of the last picture of a stream: a wrong hash of any other picture goes
//   unnoticed, so the hashes of a stream of several pictures are verified by TAppDecoder, or by FFmpeg for an MD5;
// - libde265 applies SAO across the boundaries of slices that disable in-loop filtering across them: for these streams
//   FFmpeg and HM agree, and the recipe declares libde265 left out (Excluded). The other known differences of the three
//   decoders are the constants below, each one on the recipes that meet it;
// - TAppDecoder (and TAppEncoder) refuse a tile narrower than 256 luma samples or lower than 64, the limit of the Main
//   profiles (clause A.3.2), and TAppEncoder refuses tiles with wavefronts outside the high throughput profile: the tile
//   recipes are 512 samples wide or more, and the small ones (kvazaar) declare HM left out;
// - TAppEncoder only writes VUI parameters with a target bit rate, scales the SAO offsets above 10 bits only, and has no
//   usable default without a configuration file: HmBase gives the block structure, the quantizer and the search;
// - kvazaar only encodes 8-bit 4:2:0 through FFmpeg, with 64x64 CTBs and a size that is a multiple of 8, and FFmpeg's
//   parameter list has the separator of kvazaar's lists: one explicit tile boundary in each direction at most;
// - kvazaar 2.3.2 (through FFmpeg and alone) does not write the scaling lists of a file ("cqmfile"): whatever the layout
//   of the file, each list it codes has the first half of the values of the file at the even positions, in raster order,
//   and 0 at the odd ones. A coefficient equal to 0 is forbidden (clause 7.4.5), and FFmpeg, libde265 and HM all decode
//   such a stream to the same samples: three decoders that agree do not make a stream conforming. No kvazaar vector has
//   lists of a file, and the generation reconstructs every coded list and refuses one with a coefficient equal to 0;
// - FFmpeg and libde265 ignore extended_precision_processing_flag and decode other samples, which the hashes contradict:
//   an agreement between them is not a reference. FFmpeg also ignores cabac_bypass_alignment_enabled_flag;
// - trace_headers prints nothing for an end of sequence and an end of bitstream NAL unit, and FFmpeg only passes the
//   packets of a stream whose sequence parameter set it cannot read (above 12 bits) to a muxer that needs no picture
//   size (framecrc);
// - no encoder of the pinned release writes long-term reference pictures, a reference picture list modification, scaling
//   lists in the picture parameter set, pic_output_flag = 0 or separate colour planes.
internal static partial class HevcCorpus
{
    private const string EncoderX265 = "x265";
    private const string EncoderKvazaar = "kvazaar";
    private const string EncoderHm = "hm";

    // Why a decoder is left out of the agreement of a vector: what was observed when the pinned release was verified
    private const string Libde265SaoAcrossSlices = "libde265 1.1.3 applies sample adaptive offset across the boundaries of slices that disable in-loop filtering across them (slice_loop_filter_across_slices_enabled_flag = 0).";
    private const string HmSmallCtbAtHighLevel = "HM 18.0 rejects a 16x16 coding tree block from level 5 (the limit of clause A.4.1), and x265 signals level 8.5 for a lossless stream.";
    private const string HmSmallTiles = "HM 18.0 rejects a tile narrower than 256 luma samples or lower than 64 (the limit of the Main profiles, clause A.3.2).";
    private const string FfmpegHighBitDepth = "FFmpeg's hevc decoder does not decode bit depths above 12.";
    private const string FfmpegUnequalBitDepth = "FFmpeg's hevc decoder does not decode luma and chroma planes of different bit depths.";
    private const string FfmpegBypassAlignment = "FFmpeg's hevc decoder ignores cabac_bypass_alignment_enabled_flag.";
    private const string FfmpegExtendedPrecision = "FFmpeg's hevc decoder ignores extended_precision_processing_flag.";
    private const string Libde265ExtendedPrecision = "libde265 1.1.3 ignores extended_precision_processing_flag.";
    private const string Libde265MonochromeOutput = "dec265 1.1.3 decodes the stream when it is not asked to write the pictures, and ends with std::bad_alloc when it writes them: monochrome, above 8 bits, with a conformance window that x265 crops on the right.";
    private const string Libde265HighBitDepthCrop = "dec265 1.1.3 writes other samples than the ones it decodes when the conformance window has a left or a top offset and the samples have more than 8 bits: its hash check of a one-picture stream passes, and the planes it writes differ.";
    private const string Libde265RangeExtensionsSubsampled = "libde265 1.1.3 decodes other chroma samples in the inter pictures of this 4:2:0 or 4:2:2 stream, which combines the range extensions tools of residual coding (its own check does not find the picture hashes).";
    private const string FfmpegBypassResidualTools = "FFmpeg 9.0.2's hevc decoder decodes other samples (its own check does not find the picture MD5) when a coding unit that bypasses the transform and the quantization uses residual DPCM or the rotation of its residual.";
    private const string FfmpegHighPrecisionOffsets = "FFmpeg 9.0.2's hevc decoder decodes other samples (its own check does not find the picture MD5) with high_precision_offsets_enabled_flag: the offsets of weighted prediction at the bit depth of the samples.";
    private const string Libde265NoOutputOfPriorPics = "libde265 1.1.3 outputs the pictures that were still waiting for output when a CRA picture follows an end of sequence: NoOutputOfPriorPicsFlag is then 1 and they are discarded (clause C.5.2.2), which FFmpeg and HM do.";
    private const string FfmpegHighQp12Bit = "FFmpeg 9.0.2's hevc decoder decodes other samples (its own check does not find the picture MD5) for this 12-bit stream at the highest quantization parameter.";
    private const string HmFieldOutput = "HM 18.0 decodes the same pictures (it finds the hashes of the stream), but TAppDecoder writes the two fields of a frame of a field sequence interleaved in one picture.";

    /// <param name="Name">The file name without extension; the vector id is <c>hevc/Name</c>.</param>
    /// <param name="Encoder"><c>x265</c> or <c>kvazaar</c> (through FFmpeg), or <c>hm</c> (TAppEncoder).</param>
    /// <param name="Source">The lavfi source graph.</param>
    /// <param name="PixelFormat">The pixel format of the stream: the FFmpeg name of its planes.</param>
    /// <param name="Arguments">The parameter string of x265 or kvazaar, or the options of TAppEncoder.</param>
    /// <param name="Frames">The number of pictures a decoder outputs.</param>
    private sealed record HevcVector(string Name, string Encoder, string Source, string PixelFormat, string[] Arguments, int Frames = 1)
    {
        /// <summary>The feature tags the encoded stream must signal: a tag, or one of the values of a <c>key=value</c> tag.</summary>
        public string[] Requires { get; init; } = [];

        /// <summary><see langword="false"/> (intra vector): every slice must be an I slice. <see langword="true"/> (inter
        /// vector): the stream must have P or B slices.</summary>
        public bool Inter { get; init; }

        /// <summary>The decoders that are expected not to agree with the two others, and why. Any other disagreement
        /// fails the generation, and so does a decoder that is listed here and agrees.</summary>
        public (string Decoder, string Reason)[] Excluded { get; init; } = [];

        /// <summary>Whether no two decoders are expected to agree: no digest is recorded. Only for a stream the library
        /// rejects (<c>hevc.unsupported</c>), which TAppDecoder must decode with the hashes of the stream.</summary>
        public bool WithoutDigests { get; init; }

        /// <summary>A file the encoder reads (<c>{file}</c> in the arguments), or <see langword="null"/>.</summary>
        public HevcFile? File { get; init; }

        /// <summary>How the stream is derived from the stream this recipe encodes, or <see langword="null"/>.</summary>
        public HevcDerivation? Derivation { get; init; }

        /// <summary>The name of the vector whose recipe this one encodes, when <see cref="Derivation"/> is set.</summary>
        public string? DerivedFrom { get; init; }

        /// <summary>The number of source frames, when it is not <see cref="Frames"/> (a derived stream, skipped pictures).</summary>
        public int? SourceFrames { get; init; }

        /// <summary>The pixel format given to the encoder, when it is not the one of the stream.</summary>
        public string? InputPixelFormat { get; init; }

        /// <summary>The size of the output pictures (<c>WxH</c>), when it is not the size of the source: the encoder is
        /// asked for a conformance window that crops the source.</summary>
        public string? OutputSize { get; init; }

        public int EncodedFrames => SourceFrames ?? Frames;

        public string SourcePixelFormat => InputPixelFormat ?? PixelFormat;

        /// <summary>The size of the source: the one of its crop filter (<see cref="Cropped"/>), or the size option of its
        /// first filter.</summary>
        public (int Width, int Height) SourceSize
        {
            get
            {
                var match = SourceCropRegex().Match(Source);
                if (!match.Success)
                    match = SourceSizeRegex().Match(Source);
                if (!match.Success)
                    throw new ToolException("the source of the recipe has no size.");
                return (int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture));
            }
        }

        /// <summary>The same recipe with another type of picture hash: <c>md5</c>, <c>crc</c> or <c>checksum</c>.</summary>
        public HevcVector Hash(string type)
        {
            var (from, to) = Encoder switch
            {
                EncoderX265 => ("hash=1", "hash=" + type switch { "crc" => "2", "checksum" => "3", _ => "1" }),
                EncoderKvazaar => ("hash=md5", "hash=" + type),
                _ => ("--SEIDecodedPictureHash=1", "--SEIDecodedPictureHash=" + type switch { "crc" => "2", "checksum" => "3", _ => "1" }),
            };
            return this with { Arguments = [.. Arguments.Select(argument => ReplaceOnce(argument, from, to))], Requires = [.. Requires, "hevc.pictureHash=" + type] };
        }

        /// <summary>The same recipe with a decoder that is expected to be left out.</summary>
        public HevcVector Without(string decoder, string reason) => this with { Excluded = [.. Excluded, (decoder, reason)] };

        private static string ReplaceOnce(string text, string from, string to)
        {
            var index = text.IndexOf(from, StringComparison.Ordinal);
            return index < 0 ? text : text[..index] + to + text[(index + from.Length)..];
        }
    }

    /// <param name="Name">The file name, which the commands of the manifest show as <c>{Name}</c>.</param>
    /// <param name="Description">What the file holds, for the provenance of the vector.</param>
    private sealed record HevcFile(string Name, string Description, string Content);

    // Sources
    private static string TestSrc2(string size) => "testsrc2=size=" + size;

    private static string Mandelbrot(string size) => "mandelbrot=size=" + size;

    private static string TestSrc(string size) => "testsrc=size=" + size;

    /// <summary>The "testsrc2" pattern at 10 frames per second: its counters and its moving bars change in every frame.</summary>
    private static string Moving(string size) => Inv($"testsrc2=size={size}:rate=10");

    /// <summary>The "testsrc2" pattern rotating around its center, one radian every three seconds at 10 frames per second.</summary>
    private static string Rotating(string size)
    {
        var dimensions = size.Split('x');
        return Inv($"testsrc2=size={size}:rate=10,rotate=t/3:ow={dimensions[0]}:oh={dimensions[1]}");
    }

    /// <summary>The moving pattern fading in from black: what weighted prediction is for.</summary>
    private static string Fading(string size) => Inv($"testsrc2=size={size}:rate=10,fade=t=in:st=0:d=0.8");

    /// <summary>A source cropped to a size its filter does not produce: "testsrc2" rounds its size down to even numbers.
    /// The crop is done in the pixel format of the stream, which has no subsampling in the direction of an odd size.</summary>
    private static string Cropped(string source, string pixelFormat, string size)
    {
        var dimensions = size.Split('x');
        return Inv($"{source},format={pixelFormat},crop={dimensions[0]}:{dimensions[1]}:0:0");
    }

    /// <summary>Adds seeded noise: detail that keeps the encoders from coding flat blocks only.</summary>
    private static string Noise(string source, int strength, int seed) => Inv($"{source},noise=alls={strength}:allf=t:all_seed={seed}");

    // Encoders. Single-threaded: "-threads 1" is added to every FFmpeg command (one frame thread for x265), "pools=1" to
    // x265 (one worker thread: the output does not depend on it, wavefronts included) and "threads=0,owf=0" to kvazaar.
    // Every stream carries the hash of its pictures (an MD5 unless Hash() asks for another type), and no SEI message that
    // describes the encoder and its build.

    /// <summary>x265 with I slices only (<c>keyint=1</c>).</summary>
    private static HevcVector X265Intra(string name, string source, string pixelFormat, int frames, string parameters, params string[] requires) =>
        new(name, EncoderX265, source, pixelFormat, [X265Parameters("keyint=1", parameters)], frames) { Requires = requires };

    /// <summary>The parameter string of x265: the hash, no SEI message about the encoder, one worker thread, and no
    /// wavefronts unless the recipe asks for them (x265 codes them by default as soon as a picture has two CTB rows).</summary>
    private static string X265Parameters(string structure, string parameters) =>
        "hash=1:info=0:pools=1" + (parameters.Contains("wpp=", StringComparison.Ordinal) ? "" : ":wpp=0") + (structure.Length == 0 ? "" : ":" + structure) + (parameters.Length == 0 ? "" : ":" + parameters);

    /// <summary>x265 with P or B slices. 32x32 CTBs unless the parameters say otherwise: x265 does not write the hashes
    /// of the pictures it codes when an inter picture is one CTB (see the traps).</summary>
    private static HevcVector X265(string name, string source, string pixelFormat, int frames, string parameters, params string[] requires) =>
        new(name, EncoderX265, source, pixelFormat, [X265Parameters(parameters.Contains("ctu=", StringComparison.Ordinal) ? "" : "ctu=32", parameters)], frames) { Requires = requires, Inter = true };

    /// <summary>kvazaar (8-bit 4:2:0, 64x64 CTUs).</summary>
    private static HevcVector Kvazaar(string name, string source, int frames, bool inter, string parameters, params string[] requires) =>
        new(name, EncoderKvazaar, source, "yuv420p", ["hash=md5,info=0,threads=0,owf=0" + (inter ? "" : ",period=1") + (parameters.Length == 0 ? "" : "," + parameters)], frames) { Requires = requires, Inter = inter };

    /// <summary>The options of TAppEncoder that every recipe starts from. It has no usable defaults without a
    /// configuration file: the block structure, the quantizer and the search are given here, and a later option replaces
    /// an earlier one.</summary>
    private static string[] HmBase(string pixelFormat)
    {
        var chroma = pixelFormat.StartsWith("gray", StringComparison.Ordinal) ? "400" : pixelFormat.StartsWith("gbrp", StringComparison.Ordinal) ? "444" : pixelFormat[3..6];
        var depth = pixelFormat.EndsWith("16le", StringComparison.Ordinal) ? 16 : pixelFormat.EndsWith("12le", StringComparison.Ordinal) ? 12 : pixelFormat.EndsWith("10le", StringComparison.Ordinal) ? 10 : 8;
        var profile = (chroma, depth) switch
        {
            ("420", 8) => "main",
            ("420", 10) => "main10",
            _ => "main-RExt",
        };
        return
        [
            Inv($"--InputChromaFormat={chroma}"), Inv($"--InputBitDepth={depth}"), "--Profile=" + profile, "--Level=3.1", "--FrameRate=10", "--ConformanceWindowMode=1",
            "--MaxCUWidth=64", "--MaxCUHeight=64", "--MaxPartitionDepth=4", "--QuadtreeTULog2MaxSize=5", "--QuadtreeTULog2MinSize=2", "--QuadtreeTUMaxDepthInter=3", "--QuadtreeTUMaxDepthIntra=3",
            "--QP=30", "--FastSearch=1", "--SearchRange=16", "--SEIDecodedPictureHash=1",
        ];
    }

    /// <summary>HM with I slices only.</summary>
    private static HevcVector HmIntra(string name, string source, string pixelFormat, int frames, string[] options, params string[] requires) =>
        new(name, EncoderHm, source, pixelFormat, HmOptions([.. HmBase(pixelFormat), "--IntraPeriod=1", "--GOPSize=1", .. options]), frames) { Requires = requires };

    /// <summary>HM with P or B slices: the options give the structure of the group of pictures.</summary>
    private static HevcVector Hm(string name, string source, string pixelFormat, int frames, string[] options, params string[] requires) =>
        new(name, EncoderHm, source, pixelFormat, HmOptions([.. HmBase(pixelFormat), .. options]), frames) { Requires = requires, Inter = true };

    /// <summary>The options of a command line, each one once: a later option replaces an earlier one of the same name,
    /// at its place.</summary>
    private static string[] HmOptions(string[] options)
    {
        var merged = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in options)
            merged[option[..option.IndexOf('=', StringComparison.Ordinal)]] = option;

        return [.. merged.Values];
    }

    // The groups of pictures of HM: one "FrameN" option per picture of the group, in decoding order. Its fields are the
    // slice type, the picture order count in the group, the QP offset and its model, the Cb and Cr QP offsets, the QP
    // factor, the deblocking offsets (tc, beta), the temporal layer, the number of active references, the references
    // (delta POCs) and how the reference picture set is predicted from the one of the previous entry.

    /// <summary>One P picture after the other, each one predicted from the previous one.</summary>
    private static readonly string[] HmLowDelayP = ["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=P 1 0 0.0 0.0 0 0 1.0 0 0 0 1 1 -1 0"];

    /// <summary>Low-delay B pictures with two references each.</summary>
    private static readonly string[] HmLowDelayB = ["--IntraPeriod=-1", "--GOPSize=1", "--Frame1=B 1 0 0.0 0.0 0 0 1.0 0 0 0 2 2 -1 -2 0"];

    /// <summary>Hierarchical B pictures, groups of 4 in three temporal layers, an intra picture every 8 pictures: the
    /// reference picture sets are in the sequence parameter set, one of them predicted from the previous one.</summary>
    private static string[] HmRandomAccess(int decodingRefreshType) =>
    [
        "--IntraPeriod=8", Inv($"--DecodingRefreshType={decodingRefreshType}"), "--GOPSize=4",
        "--Frame1=B 4 1 0.0 0.0 0 0 1.0 0 0 0 2 2 -4 -8 0",
        "--Frame2=B 2 2 0.0 0.0 0 0 1.0 0 0 1 2 3 -2 -6 2 1 2 3 1 1 1",
        "--Frame3=B 1 3 0.0 0.0 0 0 1.0 0 0 2 2 4 -1 -5 1 3 0",
        "--Frame4=B 3 3 0.0 0.0 0 0 1.0 0 0 2 2 4 -1 -3 -7 1 0",
    ];

    /// <summary>A vector derived from the stream of another recipe (HevcCorpus.Stream.cs).</summary>
    /// <param name="frames">The number of pictures a decoder outputs for the derived stream.</param>
    private static HevcVector Derived(IReadOnlyList<HevcVector> vectors, string source, string name, HevcDerivation derivation, int frames, params string[] requires)
    {
        var recipe = vectors.Single(vector => vector.Name == source);
        return recipe with { Name = name, DerivedFrom = source, Derivation = derivation, Frames = frames, SourceFrames = recipe.EncodedFrames, Requires = ["hevc.derived=" + derivation.Kind, .. requires] };
    }

    private static List<HevcVector> Vectors()
    {
        // The recipes are in HevcCorpus.X265.cs (x265), HevcCorpus.Kvazaar.cs (kvazaar), HevcCorpus.Hm.cs (HM, and the
        // streams the library rejects) and HevcCorpus.Derived.cs (the vectors derived from the stream of another recipe)
        var vectors = new List<HevcVector>();
        vectors.AddRange(X265IntraVectors());
        vectors.AddRange(X265InterVectors());
        vectors.AddRange(KvazaarVectors());
        vectors.AddRange(HmVectors());
        vectors.AddRange(UnsupportedVectors());
        vectors.AddRange(DerivedVectors(vectors));
        return vectors;
    }

    [GeneratedRegex(@"size=(?<width>\d+)x(?<height>\d+)", RegexOptions.ExplicitCapture)]
    private static partial Regex SourceSizeRegex();

    [GeneratedRegex(@"crop=(?<width>\d+):(?<height>\d+):0:0", RegexOptions.ExplicitCapture)]
    private static partial Regex SourceCropRegex();

    /// <summary>
    /// A file of scaling lists in the format of HM, which x265 reads too: for each size and each matrix, the name of the
    /// list, its coefficients in raster order and, for the 16x16 and 32x32 lists, the DC coefficient. The lists differ
    /// from one another, except the ones a recipe wants equal so that the encoder codes a reference to the previous list.
    /// </summary>
    /// <param name="copies">Whether the Cb and Cr lists are the luma list of their size and prediction mode.</param>
    /// <param name="defaults">Whether the 4x4 lists are the default ones (flat, Table 7-5): the encoder then codes a
    /// reference to the default list.</param>
    private static string ScalingListFile(int seed, bool copies, bool defaults = false)
    {
        var text = new StringBuilder();
        string[] sizes = ["4X4", "8X8", "16X16", "32X32"];
        string[] matrices = ["INTRA{0}_LUMA", "INTRA{0}_CHROMAU", "INTRA{0}_CHROMAV", "INTER{0}_LUMA", "INTER{0}_CHROMAU", "INTER{0}_CHROMAV"];
        for (var sizeId = 0; sizeId < sizes.Length; sizeId++)
        {
            for (var matrixId = 0; matrixId < matrices.Length; matrixId++)
            {
                // 32x32 lists only exist for luma
                if (sizeId == 3 && matrixId % 3 != 0)
                    continue;

                var name = string.Format(CultureInfo.InvariantCulture, matrices[matrixId], sizes[sizeId]);
                var variant = copies ? matrixId / 3 * 3 : matrixId;
                var side = sizeId == 0 ? 4 : 8;
                text.Append(name).Append(" =\n");
                for (var y = 0; y < side; y++)
                {
                    for (var x = 0; x < side; x++)
                    {
                        var value = defaults && sizeId == 0 ? 16 : 16 + (2 * (x + y)) + (((x * 3) + (y * 5) + (variant * 7) + (sizeId * 11) + seed) % 9);
                        text.Append(CultureInfo.InvariantCulture, $"{value}").Append(x == side - 1 ? "\n" : ",");
                    }
                }

                if (sizeId >= 2)
                    text.Append(name).Append("_DC =\n").Append(CultureInfo.InvariantCulture, $"{12 + variant + seed}").Append('\n');

                text.Append('\n');
            }
        }

        return text.ToString();
    }
}
