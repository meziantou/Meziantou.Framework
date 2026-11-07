using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The reference decoding of an HEVC stream that is not a vector of its own: the stream of a coded item or of a track of a
// HEIC file (HeicCorpus). The decoders, their commands and the rule are those of the vectors: the planes are a reference
// when at least two of the three decoders produce the same bytes for every plane of every output picture, and when no
// decoded picture hash of the stream contradicts them. A stream of a container need not carry picture hashes (libheif
// writes none): the agreement of two decoders is then the whole of the evidence, as it is for the AV1 payloads.
internal static partial class HevcCorpus
{
    /// <summary>One output picture of a payload, cropped to its conformance window.</summary>
    /// <param name="DecodeIndex">The index of the coded picture in decoding order.</param>
    /// <param name="SliceType">The slice type of its first slice: 0 is B, 1 is P, 2 is I.</param>
    /// <param name="Planes">The samples of each plane, in bitstream order.</param>
    internal sealed record PayloadPicture(int Width, int Height, int DecodeIndex, int SliceType, ushort[][] Planes);

    /// <summary>A decoder that is left out of the agreement on a payload.</summary>
    /// <param name="Outcome"><c>rejects</c> or <c>differs</c>.</param>
    internal sealed record PayloadExclusion(string Decoder, string Outcome, string Observed);

    /// <summary>A decoded payload: the output pictures in output order and what its headers signal.</summary>
    /// <param name="ChromaFormatIdc">0 is 4:0:0, 1 is 4:2:0, 2 is 4:2:2, 3 is 4:4:4.</param>
    /// <param name="CodedSize">The size of the decoded pictures before the conformance window.</param>
    /// <param name="MinCodingBlockSize"><c>MinCbSizeY</c>: a coded picture is a whole number of blocks of this size.</param>
    /// <param name="HasVideoSignal"><c>video_signal_type_present_flag</c> of the VUI (0 without VUI).</param>
    /// <param name="FullRangeFlag"><c>video_full_range_flag</c>, which only means something with <paramref name="HasVideoSignal"/>.</param>
    /// <param name="ColorDescription">The colour description of the VUI, or <see langword="null"/>.</param>
    /// <param name="ChromaSampleLocType"><c>chroma_sample_loc_type_top_field</c>, or <see langword="null"/> when the VUI does not code it.</param>
    /// <param name="CodedPictures">The number of coded pictures, output or not.</param>
    /// <param name="Agree">The decoders whose planes are the reference.</param>
    /// <param name="HashType">The type of the decoded picture hashes when every picture carries one, or <see langword="null"/>.</param>
    /// <param name="Features">The <c>hevc.*</c> tags of the stream, as for a vector.</param>
    internal sealed record Payload(
        int ChromaFormatIdc,
        int BitDepth,
        (int Width, int Height) CodedSize,
        int MinCodingBlockSize,
        bool HasVideoSignal,
        bool FullRangeFlag,
        (int Primaries, int Transfer, int Matrix)? ColorDescription,
        int? ChromaSampleLocType,
        IReadOnlyList<PayloadPicture> Outputs,
        int CodedPictures,
        IReadOnlyList<string> Agree,
        IReadOnlyList<PayloadExclusion> Excluded,
        string? HashType,
        IReadOnlySet<string> Features)
    {
        public bool IsMonochrome => ChromaFormatIdc == 0;

        public int SubsamplingX => ChromaFormatIdc is 1 or 2 ? 1 : 0;

        public int SubsamplingY => ChromaFormatIdc == 1 ? 1 : 0;

        public string Subsampling => ChromaFormatIdc switch { 0 => "4:0:0", 1 => "4:2:0", 2 => "4:2:2", _ => "4:4:4" };

        public bool HasConformanceWindow => Outputs.Any(picture => (picture.Width, picture.Height) != CodedSize);

        /// <summary>
        /// The size that counts toward the limits of a canvas when the pictures are decoded: the size of the output
        /// pictures when their conformance window only removes the padding of the coded picture to a whole number of
        /// minimum coding blocks (clause 7.4.3.2.1), the size of the coded picture in a direction where the window
        /// removes more: those samples are decoded all the same.
        /// </summary>
        public (int Width, int Height) CheckedSize => (
            CodedSize.Width - Outputs[0].Width < MinCodingBlockSize ? Outputs[0].Width : CodedSize.Width,
            CodedSize.Height - Outputs[0].Height < MinCodingBlockSize ? Outputs[0].Height : CodedSize.Height);

        public bool HasInterPictures => Features.Contains("hevc.frameTypes=inter");

        public bool IsReordered => Features.Contains("hevc.outputOrder=reordered");

        /// <summary>Every coding unit bypasses the transform, the quantization and the in-loop filters as far as the headers
        /// tell: the tag of the vectors, which is measured there and only signalled here.</summary>
        public bool SignalsTransquantBypass => Features.Contains("hevc.transquantBypass");
    }

    /// <summary>The decoders of the vectors, for the streams of a container. Decoded streams are kept by content: the
    /// same payload in several files is decoded once.</summary>
    internal sealed class PayloadDecoder
    {
        private readonly HevcTools _tools;
        private readonly Dictionary<string, Payload> _cache = new(StringComparer.Ordinal);
        private readonly string _x265 = "";

        /// <param name="encoders">Whether x265 and kvazaar are needed (generation), or the decoders only (verification).</param>
        public PayloadDecoder(bool acceptVersions, bool encoders)
        {
            _tools = new HevcTools(acceptVersions, encoders);
            if (!encoders)
                return;

            // The stand-alone encoder of the release must be the x265 that FFmpeg is linked with, whose version is pinned
            _x265 = PrebuiltTools.X265.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var version = Proc.Exec([_x265, "--version"]);
            if (!(Encoding.UTF8.GetString(version.Stderr) + Encoding.UTF8.GetString(version.Stdout)).Contains("HEVC encoder version " + _tools.X265Version, StringComparison.Ordinal))
                throw new FatalException($"The stand-alone x265 is not the x265 {_tools.X265Version} that FFmpeg is linked with.");
        }

        public ToolSet Tools => _tools;

        public string Ffmpeg => _tools.Ffmpeg;

        public string FfmpegLabel => _tools.FfmpegLabel;

        public string X265Label => _tools.X265Label;

        public string KvazaarLabel => _tools.KvazaarLabel;

        public string HmLabel => _tools.HmLabel;

        /// <summary>The three decoders, as the manifest names them.</summary>
        public string DecodersLabel => $"{_tools.FfmpegLabel} (hevc) + {_tools.Libde265Label} (dec265) + {_tools.HmLabel} (TAppDecoder)";

        /// <summary>The label of one decoder.</summary>
        public string Label(string decoder) => decoder switch
        {
            DecoderFfmpeg => _tools.FfmpegLabel + " (hevc)",
            DecoderLibde265 => _tools.Libde265Label + " (dec265)",
            _ => _tools.HmLabel + " (TAppDecoder)",
        };

        /// <summary>Decodes a byte stream with the three decoders and reads its headers with trace_headers.</summary>
        /// <exception cref="ToolException">No two decoders agree, or the picture hashes of the stream contradict them.</exception>
        public Payload Decode(FullPath scratch, string name, byte[] stream)
        {
            var key = Bytes.Sha256Hex(stream);
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var path = scratch / (name + ".hevc");
            File.WriteAllBytes(path, stream);
            var trace = ParseTrace(Proc.RunText(TraceCommand(_tools, path.Value), stderr: true), SplitAnnexB(stream));
            if (trace.Pictures.Count == 0)
                throw new ToolException($"{name}: the stream has no picture.");

            var formats = trace.Pictures.Select(picture => picture.Format).Distinct().ToList();
            var signals = trace.Pictures.Select(picture => VideoSignal(picture.Sps)).Distinct().ToList();
            if (formats.Count != 1 || signals.Count != 1)
                throw new ToolException($"{name}: the sample format or the video signal description changes inside the stream.");

            var format = formats[0];
            if (format.BitDepthLuma != format.BitDepthChroma && format.ChromaFormatIdc != 0)
                throw new ToolException($"{name}: the luma and the chroma planes have different bit depths.");

            var outputs = trace.Outputs;
            var sizes = outputs.Select(picture => picture.Size).ToList();
            var hashTypes = trace.Pictures.All(picture => picture.Hash is not null) ? trace.Pictures.Select(picture => ReadPictureHash(picture).Type).Distinct(StringComparer.Ordinal).ToList() : [];
            var hashType = hashTypes.Count == 1 ? hashTypes[0] : null;
            var results = new List<DecoderResult>
            {
                DecodeFfmpeg(_tools, path, format, sizes, outputs.Select(picture => picture.FirstSliceType).ToList(), hashType),
                DecodeLibde265(_tools, path, format, sizes, hashType, trace.Pictures.Count, scratch),
                DecodeHm(_tools, path, format, sizes, [.. trace.Pictures.Where(picture => !picture.IsSkipped).Select(picture => picture.Poc)], scratch),
            };

            var groups = results.Where(result => result.Frames is not null).GroupBy(result => result, DecoderResultComparer.Instance).Select(group => group.ToList()).OrderByDescending(group => group.Count).ToList();
            var agreed = groups.FirstOrDefault(group => group.Count >= 2 && group.All(result => result.Hash != HashCheck.Mismatch));
            if (agreed is null)
            {
                throw new ToolException($"{name}: no two decoders agree without being contradicted by the picture hashes of the stream (" + string.Join("; ", results.Select(result =>
                    result.Frames is null ? $"{result.Decoder}: {result.Failure}" : Inv($"{result.Decoder} decodes {Bytes.Sha256Hex(Bytes.Concat(result.Frames.SelectMany(frame => frame)))[..12]}, hash {result.Hash}"))) + "); no reference is recorded from a single decoder.");
            }

            var reference = agreed[0].Frames!;
            var pictures = new List<PayloadPicture>();
            for (var i = 0; i < outputs.Count; i++)
            {
                var planeSizes = format.PlaneSizes(sizes[i]);
                var planes = new ushort[planeSizes.Count][];
                for (var plane = 0; plane < planes.Length; plane++)
                {
                    var bytes = reference[i][plane];
                    var samples = new ushort[planeSizes[plane].Width * planeSizes[plane].Height];
                    for (var s = 0; s < samples.Length; s++)
                        samples[s] = format.BytesPerSample(plane) == 1 ? bytes[s] : (ushort)(bytes[2 * s] | (bytes[(2 * s) + 1] << 8));

                    planes[plane] = samples;
                }

                pictures.Add(new PayloadPicture(sizes[i].Width, sizes[i].Height, outputs[i].DecodeIndex, outputs[i].FirstSliceType, planes));
            }

            var sps = trace.Pictures[0].Sps;
            var (hasSignal, fullRange, description, chromaLocation) = signals[0];
            var excluded = results.Where(result => !agreed.Contains(result)).Select(result => new PayloadExclusion(result.Decoder, result.Decodes ? "differs" : "rejects",
                result.Frames is null ? result.Failure ?? "no message" : "outputs other samples" + (result.Hash == HashCheck.Mismatch ? "; its own check does not find the picture hashes of the stream" : ""))).ToList();
            var payload = new Payload(
                format.ChromaFormatIdc,
                format.BitDepthLuma,
                ((int)sps.Get("pic_width_in_luma_samples"), (int)sps.Get("pic_height_in_luma_samples")),
                8 << (int)sps.Get("log2_min_luma_coding_block_size_minus3"),
                hasSignal,
                fullRange,
                description,
                chromaLocation,
                pictures,
                trace.Pictures.Count,
                [.. DecoderIds.Where(id => agreed.Any(result => result.Decoder == id))],
                excluded,
                hashType,
                Features(trace));
            _cache.Add(key, payload);
            return payload;
        }

        // The video signal description of the VUI of a sequence parameter set (clause E.2.1)
        private static (bool HasSignal, bool FullRange, (int, int, int)? Description, int? ChromaLocation) VideoSignal(HevcSyntax sps)
        {
            var hasSignal = sps.Get("video_signal_type_present_flag") == 1;
            (int, int, int)? description = hasSignal && sps.Get("colour_description_present_flag") == 1
                ? ((int)sps.Get("colour_primaries"), (int)sps.Get("transfer_characteristics"), (int)sps.Get("matrix_coefficients"))
                : null;
            int? chromaLocation = sps.Get("chroma_loc_info_present_flag") == 1 ? (int)sps.Get("chroma_sample_loc_type_top_field") : null;
            return (hasSignal, hasSignal && sps.Get("video_full_range_flag") == 1, description, chromaLocation);
        }

        /// <summary>
        /// The number of bytes of each slice segment NAL unit of a byte stream that precede its slice data: the NAL unit
        /// header and the slice segment header, up to its byte alignment, as trace_headers reads them (bit positions in the
        /// NAL unit).
        /// </summary>
        public List<int> SliceHeaderLengths(FullPath scratch, string name, byte[] stream)
        {
            var path = scratch / (name + ".headers.hevc");
            File.WriteAllBytes(path, stream);
            var result = new List<int>();
            var isSlice = false;
            var end = 0;
            void Flush()
            {
                if (isSlice)
                    result.Add((end + 7) / 8);

                isSlice = false;
                end = 0;
            }

            foreach (var raw in Proc.RunText(TraceCommand(_tools, path.Value), stderr: true).Split('\n'))
            {
                var prefix = TracePrefixRegex().Match(raw);
                if (!prefix.Success || TraceElementRegex().Match(raw[prefix.Length..].TrimEnd()) is not { Success: true } match)
                    continue;

                var position = int.Parse(match.Groups["position"].Value, CultureInfo.InvariantCulture);
                var elementName = match.Groups["name"].Value;
                if (elementName == "forbidden_zero_bit")
                    Flush();
                else if (elementName == "nal_unit_type")
                    isSlice = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture) < 32;

                end = Math.Max(end, position + match.Groups["bits"].Value.Length);
            }

            Flush();
            return result;
        }

        /// <summary>Rewrites a byte stream with a bitstream filter of the pinned FFmpeg (<c>hevc_metadata</c>): whole NAL
        /// units are copied, the parameter sets are rewritten.</summary>
        /// <returns>The byte stream and the command, with symbolic file names.</returns>
        public (byte[] Stream, string Command) Filter(FullPath scratch, string name, byte[] stream, string filter)
        {
            var input = scratch / (name + ".source.hevc");
            var output = scratch / (name + ".filtered.hevc");
            File.WriteAllBytes(input, stream);
            var command = _tools.FfmpegCmd(["-f", "hevc", "-i", input.Value, "-c", "copy", "-bsf:v", filter, "-flags", "+bitexact", "-f", "hevc", output.Value]);
            var result = Proc.Exec(command);
            if (result.ExitCode != 0 || !File.Exists(output))
                throw new ToolException($"{name}: the bitstream filter failed: {LastLines(Encoding.UTF8.GetString(result.Stderr))}");

            return (File.ReadAllBytes(output), CommonCorpus.Display(command, _tools, [(input.Value, "{stream}.hevc"), (output.Value, "{filtered}.hevc")]));
        }

        /// <summary>
        /// Encodes raw pictures with the stand-alone x265 of the pinned release (the same version as the one FFmpeg is
        /// linked with): one thread, no wavefront, so that the stream does not depend on the machine. Unlike FFmpeg's
        /// wrapper, it codes pictures as small as its smallest coding tree block and writes a video signal description
        /// only when it is asked for one. Each picture gets a decoded picture hash (MD5) unless the parameters say otherwise.
        /// </summary>
        /// <param name="source">The pictures, as the planes of <paramref name="pixelFormat"/>: 8-bit samples, or 16-bit
        /// little-endian ones above 8 bits.</param>
        /// <param name="pixelFormat">An FFmpeg planar format: <c>gray</c>, <c>yuv420p</c>, <c>yuv422p</c>, <c>yuv444p</c> or
        /// <c>gbrp</c> (4:4:4 planes in the order G, B, R, signalled by the identity matrix), with <c>10le</c> or <c>12le</c>.</param>
        /// <param name="parameters">x265 options as <c>name=value</c> pairs separated by colons.</param>
        /// <returns>The byte stream and the command, with symbolic file names.</returns>
        public (byte[] Stream, string Command) EncodeX265(FullPath scratch, string name, byte[] source, string pixelFormat, int width, int height, int frames, string parameters)
        {
            var input = scratch / (name + ".yuv");
            var output = scratch / (name + ".x265.hevc");
            File.WriteAllBytes(input, source);
            var depth = pixelFormat.EndsWith("10le", StringComparison.Ordinal) ? 10 : pixelFormat.EndsWith("12le", StringComparison.Ordinal) ? 12 : 8;
            var layout = depth == 8 ? pixelFormat : pixelFormat[..^4];
            var csp = layout switch { "gray" => "i400", "yuv420p" => "i420", "yuv422p" => "i422", "yuv444p" or "gbrp" => "i444", _ => throw new ToolException($"{name}: unknown pixel format {pixelFormat}.") };
            var command = new List<string>
            {
                _x265, "--input", input.Value, "--input-res", Inv($"{width}x{height}"), "--fps", "10", "--input-csp", csp, "--input-depth", Inv($"{depth}"), "--output-depth", Inv($"{depth}"),
                "--frames", Inv($"{frames}"), "--no-info", "--pools", "1", "--no-wpp", "--frame-threads", "1", "--log-level", "error",
            };

            // Every picture carries the MD5 of its decoded samples (a decoded picture hash SEI message), which HM and FFmpeg
            // check when they decode the stream
            if (!parameters.Split(':').Any(parameter => parameter.StartsWith("hash=", StringComparison.Ordinal)))
                command.AddRange(["--hash", "1"]);

            if (layout == "gbrp")
                command.AddRange(["--colormatrix", "gbr"]);

            // x265 refuses a picture smaller than its coding tree block: the largest size that fits, unless the recipe names one
            if (!parameters.Split(':').Any(parameter => parameter.StartsWith("ctu=", StringComparison.Ordinal)))
                command.AddRange(["--ctu", Math.Min(width, height) >= 64 ? "64" : Math.Min(width, height) >= 32 ? "32" : "16"]);

            foreach (var parameter in parameters.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = parameter.IndexOf('=', StringComparison.Ordinal);
                var (key, value) = (parameter[..separator], parameter[(separator + 1)..]);
                if (key == "lossless")
                    command.Add(value == "1" ? "--lossless" : "--no-lossless");
                else
                    command.AddRange(["--" + key, value]);
            }

            command.AddRange(["-o", output.Value]);
            var result = Proc.Exec(command);
            var log = Encoding.UTF8.GetString(result.Stderr) + Encoding.UTF8.GetString(result.Stdout);
            if (result.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0 || log.Contains("[error]", StringComparison.Ordinal))
                throw new ToolException($"{name}: x265 failed: {LastLines(log)}");

            return (File.ReadAllBytes(output), CommonCorpus.Display(command, _tools, [(_x265, "x265"), (input.Value, "{source}.yuv"), (output.Value, "{stream}.hevc")]));
        }
    }

    /// <summary>The NAL units of a byte stream, without their start codes, grouped by access unit: a new access unit
    /// starts at the first unit that may only precede the first slice segment of a picture (an access unit delimiter, a
    /// parameter set, a prefix SEI message) or at a slice segment with <c>first_slice_segment_in_pic_flag = 1</c>, once
    /// the previous picture has a slice segment (clause 7.4.2.4.4).</summary>
    internal static List<List<byte[]>> SplitAccessUnits(byte[] stream)
    {
        var result = new List<List<byte[]>>();
        var current = new List<byte[]>();
        var hasSlice = false;
        foreach (var unit in SplitAnnexB(stream))
        {
            var isVcl = unit.Type < 32;
            var startsPicture = isVcl ? (stream[unit.Header + 2] & 0x80) != 0 : unit.Type is NalVps or NalSps or NalPps or NalAud or NalPrefixSei;
            if (hasSlice && startsPicture)
            {
                result.Add(current);
                current = [];
                hasSlice = false;
            }

            current.Add(stream[unit.Header..unit.End]);
            hasSlice |= isVcl;
        }

        if (current.Count > 0)
            result.Add(current);

        return result;
    }

    /// <summary>The identifiers of the three decoders, in the order of the manifest.</summary>
    internal static IReadOnlyList<string> DecoderNames => DecoderIds;

    /// <summary><c>nal_unit_type</c> of a NAL unit that starts with its header.</summary>
    internal static int NalUnitType(byte[] unit) => (unit[0] >> 1) & 0x3F;

    /// <summary>Whether a NAL unit type is a parameter set (VPS, SPS, PPS).</summary>
    internal static bool IsParameterSet(int nalUnitType) => nalUnitType is NalVps or NalSps or NalPps;
}
