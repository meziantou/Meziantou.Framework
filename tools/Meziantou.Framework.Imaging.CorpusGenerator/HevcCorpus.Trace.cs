using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The NAL units of a vector as FFmpeg's trace_headers bitstream filter prints them (libavcodec's coded bitstream reader, a
// header parser that shares nothing with x265, kvazaar, HM or libde265, and that FFmpeg's own hevc decoder does not use),
// and what the generator derives from them: the pictures in decoding order, their picture order counts, the pictures a
// decoder outputs and their order (H.265, clauses 8.1.3, 8.3.1 and C.5.2), and the picture hashes. Nothing here reads slice
// data: a tag describes what the headers signal, never what the encoder was asked to do.
internal static partial class HevcCorpus
{
    private const int NalRadlN = 6;
    private const int NalRadlR = 7;
    private const int NalRaslN = 8;
    private const int NalRaslR = 9;
    private const int NalBlaWLp = 16;
    private const int NalCra = 21;
    private const int NalVps = 32;
    private const int NalSps = 33;
    private const int NalPps = 34;
    private const int NalAud = 35;
    private const int NalEos = 36;
    private const int NalEob = 37;
    private const int NalFiller = 38;
    private const int NalPrefixSei = 39;
    private const int NalSuffixSei = 40;

    private const int SeiDecodedPictureHash = 132;

    /// <summary>The names of the NAL unit types of Table 7-1 that the tags list: the VCL types, and the access unit
    /// delimiter, end of sequence, end of bitstream and filler data units. Parameter sets are in every stream, and the SEI
    /// messages have a tag of their own.</summary>
    private static readonly Dictionary<int, string> NalNames = new()
    {
        [0] = "TRAIL_N", [1] = "TRAIL_R", [2] = "TSA_N", [3] = "TSA_R", [4] = "STSA_N", [5] = "STSA_R", [6] = "RADL_N", [7] = "RADL_R", [8] = "RASL_N", [9] = "RASL_R",
        [16] = "BLA_W_LP", [17] = "BLA_W_RADL", [18] = "BLA_N_LP", [19] = "IDR_W_RADL", [20] = "IDR_N_LP", [21] = "CRA",
        [NalAud] = "AUD", [NalEos] = "EOS", [NalEob] = "EOB", [NalFiller] = "FD",
    };

    /// <summary>The names of the SEI payload types (Annex D) the vectors carry.</summary>
    private static readonly Dictionary<int, string> SeiNames = new()
    {
        [0] = "bufferingPeriod", [1] = "pictureTiming", [4] = "userDataRegistered", [5] = "userDataUnregistered", [6] = "recoveryPoint", [129] = "activeParameterSets",
        [130] = "decodingUnitInfo", [131] = "temporalSubLayerZeroIndex", [SeiDecodedPictureHash] = "decodedPictureHash", [136] = "timeCode", [137] = "masteringDisplayColourVolume",
        [144] = "contentLightLevel", [147] = "alternativeTransferCharacteristics",
    };

    /// <summary>The syntax elements of one NAL unit, in bitstream order. Array elements keep their indices in the name.</summary>
    private sealed class HevcSyntax
    {
        private readonly List<(string Name, long Value)> _elements = [];
        private readonly Dictionary<string, long> _first = new(StringComparer.Ordinal);

        public IReadOnlyList<(string Name, long Value)> Elements => _elements;

        public void Add(string name, long value)
        {
            _elements.Add((name, value));
            _first.TryAdd(name, value);
        }

        public bool Has(string name) => _first.ContainsKey(name);

        /// <summary>The value of the first element of that name, or <paramref name="absent"/> when the unit does not code it.</summary>
        public long Get(string name, long absent = 0) => _first.TryGetValue(name, out var value) ? value : absent;

        /// <summary>The elements <c>prefix[...]</c>, in bitstream order.</summary>
        public List<(string Name, long Value)> Array(string prefix) => [.. _elements.Where(e => e.Name.StartsWith(prefix + "[", StringComparison.Ordinal))];

        /// <summary>Every value of the elements of that exact name, in bitstream order.</summary>
        public List<long> All(string name) => [.. _elements.Where(e => e.Name == name).Select(e => e.Value)];
    }

    /// <param name="Index">The index of the unit in the byte stream.</param>
    /// <param name="Type"><c>nal_unit_type</c>, read from the byte stream.</param>
    /// <param name="TemporalId"><c>nuh_temporal_id_plus1 - 1</c>, read from the byte stream.</param>
    private sealed record HevcNal(int Index, int Type, int TemporalId)
    {
        /// <summary>The syntax elements trace_headers prints for the unit. It prints none for an end of sequence and an
        /// end of bitstream NAL unit, which have no payload.</summary>
        public HevcSyntax Syntax { get; } = new();

        /// <summary>The payload types of the SEI messages of the unit, in order.</summary>
        public List<int> SeiPayloads { get; } = [];

        public bool IsVcl => Type is >= 0 and < 32;
    }

    /// <summary>One slice segment, with the parameter sets that are active when it is decoded.</summary>
    private sealed record HevcSliceSegment(HevcNal Nal, HevcSyntax Pps, HevcSyntax Sps)
    {
        public HevcSyntax Header => Nal.Syntax;

        public bool IsDependent => Header.Get("dependent_slice_segment_flag") == 1;
    }

    /// <summary>One coded picture: the slice segments from a <c>first_slice_segment_in_pic_flag = 1</c> to the next one.</summary>
    private sealed class HevcPicture
    {
        /// <summary>The index of the picture among the coded pictures of the stream, in decoding order.</summary>
        public int DecodeIndex { get; init; }

        public List<HevcSliceSegment> Segments { get; } = [];

        /// <summary>The index of the first NAL unit of the access unit of the picture (clause 7.4.2.4.4).</summary>
        public int AccessUnitStart { get; init; }

        public HevcSyntax Sps => Segments[0].Sps;

        public HevcSyntax Pps => Segments[0].Pps;

        public int NalType => Segments[0].Nal.Type;

        public int TemporalId => Segments[0].Nal.TemporalId;

        public bool IsIrap => NalType is >= NalBlaWLp and <= 23;

        public bool IsRasl => NalType is NalRaslN or NalRaslR;

        /// <summary><c>NoRaslOutputFlag</c> (clause 8.1.3): an IDR or a BLA picture, the first picture of the stream, or the
        /// first picture after an end of sequence NAL unit.</summary>
        public bool NoRaslOutputFlag { get; set; }

        /// <summary>The index of the coded video sequence the picture belongs to: a new one starts at each IRAP picture
        /// with <c>NoRaslOutputFlag = 1</c>.</summary>
        public int Sequence { get; set; }

        /// <summary><c>PicOrderCntVal</c> (clause 8.3.1).</summary>
        public int Poc { get; set; }

        /// <summary><c>PicOrderCntMsb</c>: not zero once the least significant bits have wrapped.</summary>
        public int PocMsb { get; set; }

        /// <summary>A RASL picture whose associated IRAP picture has <c>NoRaslOutputFlag = 1</c>: it is not decoded (its
        /// references are not in the stream) and not output.</summary>
        public bool IsSkipped { get; set; }

        /// <summary><c>PicOutputFlag</c> (clause 8.1.3): every decoded picture, unless its slice header says otherwise.</summary>
        public bool IsOutput => !IsSkipped && Segments[0].Header.Get("pic_output_flag", absent: 1) == 1;

        /// <summary>A decoded picture with <c>PicOutputFlag = 1</c> that is never output: it was still waiting in the
        /// decoded picture buffer when a picture with <c>NoOutputOfPriorPicsFlag = 1</c> emptied it (clause C.5.2.2).</summary>
        public bool IsDiscarded { get; set; }

        /// <summary>The SEI NAL unit that holds the decoded picture hash of the picture, or <see langword="null"/>.</summary>
        public HevcNal? Hash { get; set; }

        /// <summary>The slice type of the first slice: 0 is B, 1 is P, 2 is I.</summary>
        public int FirstSliceType => (int)Segments[0].Header.Get("slice_type");

        public HevcFormat Format => ReadFormat(Sps);

        /// <summary>The size of the luma plane of the picture a decoder outputs: the decoded picture cropped to the
        /// conformance window (clause 7.4.3.2.1). The offsets are in chroma samples.</summary>
        public (int Width, int Height) Size
        {
            get
            {
                var format = Format;
                return ((int)(Sps.Get("pic_width_in_luma_samples") - (format.SubWidthC * (Sps.Get("conf_win_left_offset") + Sps.Get("conf_win_right_offset")))),
                    (int)(Sps.Get("pic_height_in_luma_samples") - (format.SubHeightC * (Sps.Get("conf_win_top_offset") + Sps.Get("conf_win_bottom_offset")))));
            }
        }
    }

    /// <param name="Outputs">The pictures a decoder outputs, in output order (<see cref="ResolveOutputs"/>).</param>
    private sealed record HevcTrace(IReadOnlyList<HevcNal> Nals, IReadOnlyList<HevcPicture> Pictures, IReadOnlyList<HevcPicture> Outputs);

    /// <summary>The sample format of a sequence parameter set.</summary>
    /// <param name="ChromaFormatIdc">0 is 4:0:0, 1 is 4:2:0, 2 is 4:2:2, 3 is 4:4:4.</param>
    /// <param name="IdentityMatrix">Whether the VUI signals <c>matrix_coeffs = 0</c> for a 4:4:4 stream: its planes are G, B, R.</param>
    /// <param name="FullRange">Whether the VUI signals <c>video_full_range_flag = 1</c>.</param>
    private sealed record HevcFormat(int ChromaFormatIdc, int BitDepthLuma, int BitDepthChroma, bool IdentityMatrix, bool FullRange)
    {
        public string ChromaFormat => ChromaFormatIdc switch
        {
            0 => "4:0:0",
            1 => "4:2:0",
            2 => "4:2:2",
            3 => "4:4:4",
            _ => throw new ToolException("Unexpected chroma_format_idc."),
        };

        public int SubWidthC => ChromaFormatIdc is 1 or 2 ? 2 : 1;

        public int SubHeightC => ChromaFormatIdc == 1 ? 2 : 1;

        public int PlaneCount => ChromaFormatIdc == 0 ? 1 : 3;

        public int BitDepth(int plane) => plane == 0 ? BitDepthLuma : BitDepthChroma;

        /// <summary>The number of bytes of a sample of a plane in the digest layout: 1 up to 8 bits, 2 above.</summary>
        public int BytesPerSample(int plane) => BitDepth(plane) > 8 ? 2 : 1;

        /// <summary>The FFmpeg pixel format that holds the decoded planes without any conversion, when the luma and the
        /// chroma planes have the same bit depth. An identity-matrix stream is planar GBR, whose plane order G, B, R is
        /// the bitstream order. Above 12 bits the name is the one FFmpeg gives to 16-bit samples, although its decoder
        /// does not read such a stream.</summary>
        public string PixelFormat
        {
            get
            {
                var name = (ChromaFormatIdc, IdentityMatrix) switch
                {
                    (0, _) => "gray",
                    (3, true) => "gbrp",
                    _ => "yuv" + ChromaFormat.Replace(":", "", StringComparison.Ordinal) + "p",
                };
                if (BitDepthLuma != BitDepthChroma && ChromaFormatIdc != 0)
                    return Inv($"{name}-y{BitDepthLuma}-c{BitDepthChroma}");

                return BitDepthLuma == 8 ? name : Inv($"{name}{BitDepthLuma}le");
            }
        }

        /// <summary>The name FFmpeg's hevc decoder gives to <see cref="PixelFormat"/>: the same, except for a full-range
        /// 8-bit 4:2:0 stream, which it still decodes to the deprecated <c>yuvj420p</c>. The planes are the same; asking
        /// for <c>yuv420p</c> would have swscale convert the range of the samples.</summary>
        public string FfmpegPixelFormat => FullRange && PixelFormat == "yuv420p" ? "yuvj420p" : PixelFormat;

        /// <summary>The dimensions of the planes of a picture whose luma plane has that size.</summary>
        public List<(int Width, int Height)> PlaneSizes((int Width, int Height) luma) =>
            [.. Enumerable.Range(0, PlaneCount).Select(plane => plane == 0 ? luma : (luma.Width / SubWidthC, luma.Height / SubHeightC))];

        /// <summary>The number of bytes of a picture in the digest layout.</summary>
        public int FrameLength((int Width, int Height) luma) => PlaneSizes(luma).Select((plane, index) => plane.Width * plane.Height * BytesPerSample(index)).Sum();
    }

    private static HevcFormat ReadFormat(HevcSyntax sps)
    {
        var chroma = (int)sps.Get("chroma_format_idc");
        var identity = chroma == 3 && sps.Get("colour_description_present_flag") == 1 && sps.Get("matrix_coefficients", absent: 2) == 0;
        return new HevcFormat(chroma, (int)sps.Get("bit_depth_luma_minus8") + 8, (int)sps.Get("bit_depth_chroma_minus8") + 8, identity, sps.Get("video_full_range_flag") == 1);
    }

    /// <summary>
    /// Parses the trace of the packets of a stream (the copy of the parameter sets in the extradata is skipped), groups
    /// the slice segments into pictures and derives the coded video sequences, the picture order counts and the pictures
    /// that are output. The NAL units are those of the byte stream (<paramref name="units"/>): the n-th unit of the
    /// trace is the n-th unit of the stream that is neither an end of sequence nor an end of bitstream, and must have
    /// its type.
    /// </summary>
    private static HevcTrace ParseTrace(string log, IReadOnlyList<HevcUnit> units)
    {
        var nals = new List<HevcNal>();
        HevcNal? current = null;
        var inPackets = false;
        var payloadPrefixBytes = 0;
        foreach (var rawLine in log.Split('\n'))
        {
            var prefix = TracePrefixRegex().Match(rawLine);
            if (!prefix.Success)
                continue;

            var line = rawLine[prefix.Length..].TrimEnd();
            if (line.StartsWith("Packet:", StringComparison.Ordinal))
            {
                inPackets = true;
                continue;
            }

            if (!inPackets)
                continue;

            var element = TraceElementRegex().Match(line);
            if (!element.Success)
            {
                // The name of a NAL unit, or of an SEI message inside one
                continue;
            }

            var name = element.Groups["name"].Value;
            var value = long.Parse(element.Groups["value"].Value, CultureInfo.InvariantCulture);
            if (name == "forbidden_zero_bit" && element.Groups["position"].Value == "0")
            {
                // The units trace_headers does not print
                while (nals.Count < units.Count && units[nals.Count].Type is NalEos or NalEob)
                    nals.Add(new HevcNal(nals.Count, units[nals.Count].Type, units[nals.Count].TemporalId));

                if (nals.Count == units.Count)
                    throw new ToolException("trace_headers describes more NAL units than the byte stream has.");

                current = new HevcNal(nals.Count, units[nals.Count].Type, units[nals.Count].TemporalId);
                nals.Add(current);
                payloadPrefixBytes = 0;
            }

            if (current is null)
                throw new ToolException("trace_headers prints a syntax element outside a NAL unit.");

            current.Syntax.Add(name, value);
            if (name is "nal_unit_type" && value != current.Type)
                throw new ToolException(Inv($"NAL unit {current.Index} has the type {current.Type} in the byte stream, and trace_headers reads {value}."));

            // sei_message(): payloadType is 255 for each ff_byte, plus last_payload_type_byte
            switch (name)
            {
                case "ff_byte":
                    payloadPrefixBytes++;
                    break;
                case "last_payload_type_byte":
                    current.SeiPayloads.Add((255 * payloadPrefixBytes) + (int)value);
                    payloadPrefixBytes = 0;
                    break;
                case "last_payload_size_byte":
                    payloadPrefixBytes = 0;
                    break;
            }
        }

        while (nals.Count < units.Count && units[nals.Count].Type is NalEos or NalEob)
            nals.Add(new HevcNal(nals.Count, units[nals.Count].Type, units[nals.Count].TemporalId));

        if (nals.Count != units.Count)
            throw new ToolException(Inv($"the byte stream has {units.Count} NAL units, and trace_headers describes {nals.Count} of them (end of sequence and end of bitstream units aside)."));

        var pictures = ReadPictures(nals);
        if (pictures.Count == 0)
            throw new ToolException("trace_headers found no slice segment.");

        ResolvePictureOrder(nals, pictures);
        return new HevcTrace(nals, pictures, ResolveOutputs(pictures));
    }

    /// <summary>
    /// The pictures a decoder outputs, in output order: the "bumping" process of the output order operation of the
    /// decoded picture buffer (clauses C.5.2.2 to C.5.2.4). A picture waits until more pictures wait than
    /// <c>sps_max_num_reorder_pics</c> allows, or until it has waited longer than <c>SpsMaxLatencyPictures</c>; the
    /// picture with the smallest picture order count is then output. An IRAP picture with <c>NoRaslOutputFlag = 1</c>
    /// first empties the buffer: the waiting pictures are output, unless <c>NoOutputOfPriorPicsFlag</c> is 1 (always for
    /// a CRA picture, otherwise <c>no_output_of_prior_pics_flag</c>), in which case they are discarded.
    /// The third condition of the process, a full buffer, is not followed: it needs the reference picture sets. The
    /// decoders tell when it matters: the generation fails when they output other pictures than these.
    /// </summary>
    private static List<HevcPicture> ResolveOutputs(List<HevcPicture> pictures)
    {
        var outputs = new List<HevcPicture>();
        var waiting = new List<(HevcPicture Picture, int Latency)>();
        foreach (var picture in pictures.Where(picture => !picture.IsSkipped))
        {
            var reorder = picture.Sps.Array("sps_max_num_reorder_pics")[^1].Value;
            var latencyIncrease = picture.Sps.Array("sps_max_latency_increase_plus1")[^1].Value;
            if (picture.NoRaslOutputFlag && picture.DecodeIndex > 0)
            {
                if (picture.NalType == NalCra || picture.Segments[0].Header.Get("no_output_of_prior_pics_flag") == 1)
                {
                    foreach (var (discarded, _) in waiting)
                        discarded.IsDiscarded = true;

                    waiting.Clear();
                }

                while (waiting.Count > 0)
                    Bump();
            }

            // The pictures that follow the current one in output order have waited one picture more
            for (var i = 0; i < waiting.Count; i++)
            {
                if (waiting[i].Picture.Poc > picture.Poc)
                    waiting[i] = (waiting[i].Picture, waiting[i].Latency + 1);
            }

            if (picture.IsOutput)
                waiting.Add((picture, 0));

            while (waiting.Count > reorder || (latencyIncrease != 0 && waiting.Any(entry => entry.Latency >= reorder + latencyIncrease - 1)))
                Bump();
        }

        while (waiting.Count > 0)
            Bump();

        return outputs;

        void Bump()
        {
            var next = waiting.MinBy(entry => entry.Picture.Poc);
            waiting.Remove(next);
            outputs.Add(next.Picture);
        }
    }

    /// <summary>Groups the slice segments into pictures, each with the parameter sets its slice headers refer to: the last
    /// ones received with these identifiers (clause 7.4.2.4.2).</summary>
    private static List<HevcPicture> ReadPictures(List<HevcNal> nals)
    {
        var spsById = new Dictionary<long, HevcSyntax>();
        var ppsById = new Dictionary<long, HevcSyntax>();
        var pictures = new List<HevcPicture>();
        var lastVcl = -1;
        HevcSyntax? pps = null;
        foreach (var nal in nals)
        {
            switch (nal.Type)
            {
                case NalSps:
                    spsById[nal.Syntax.Get("sps_seq_parameter_set_id")] = nal.Syntax;
                    break;
                case NalPps:
                    ppsById[nal.Syntax.Get("pps_pic_parameter_set_id")] = nal.Syntax;
                    break;
            }

            if (nal.Syntax.Get("nuh_layer_id") != 0)
                throw new ToolException("the stream has a NAL unit of another layer (nuh_layer_id > 0).");
            if (!nal.IsVcl)
                continue;

            if (nal.Syntax.Get("first_slice_segment_in_pic_flag") == 1)
            {
                // The access unit starts at the first unit, after the last VCL unit of the previous picture, that may
                // only precede the VCL units of a picture (clause 7.4.2.4.4)
                var start = nal.Index;
                for (var i = lastVcl + 1; i < nal.Index; i++)
                {
                    if (nals[i].Type is NalAud or NalVps or NalSps or NalPps or NalPrefixSei or (>= 41 and <= 44) or (>= 48 and <= 55))
                    {
                        start = i;
                        break;
                    }
                }

                pictures.Add(new HevcPicture { DecodeIndex = pictures.Count, AccessUnitStart = start });
            }
            else if (pictures.Count == 0)
            {
                throw new ToolException("the first slice segment of the stream is not the first one of a picture.");
            }

            // A dependent slice segment has the picture parameter set of the slice it belongs to
            if (nal.Syntax.Has("slice_pic_parameter_set_id"))
            {
                if (!ppsById.TryGetValue(nal.Syntax.Get("slice_pic_parameter_set_id"), out pps))
                    throw new ToolException("a slice segment refers to a picture parameter set the stream does not hold.");
            }

            if (pps is null || !spsById.TryGetValue(pps.Get("pps_seq_parameter_set_id"), out var sps))
                throw new ToolException("a slice segment refers to a sequence parameter set the stream does not hold.");

            pictures[^1].Segments.Add(new HevcSliceSegment(nal, pps, sps));
            lastVcl = nal.Index;
        }

        // The hash of a picture is in a suffix SEI message of its access unit: after its slice segments
        var owner = -1;
        foreach (var nal in nals)
        {
            if (nal.IsVcl)
            {
                if (nal.Syntax.Get("first_slice_segment_in_pic_flag") == 1)
                    owner++;
                continue;
            }

            if (!nal.SeiPayloads.Contains(SeiDecodedPictureHash))
                continue;
            if (nal.Type != NalSuffixSei || owner < 0)
                throw new ToolException("a decoded picture hash is not in a suffix SEI message that follows a picture.");
            if (pictures[owner].Hash is not null)
                throw new ToolException("a picture has two decoded picture hashes.");

            pictures[owner].Hash = nal;
        }

        return pictures;
    }

    /// <summary>
    /// Derives, for every picture in decoding order, <c>NoRaslOutputFlag</c> and the coded video sequence (clause 8.1.3),
    /// the picture order count (clause 8.3.1) and whether the picture is skipped: a RASL picture is not decoded when its
    /// associated IRAP picture, the previous one in decoding order, has <c>NoRaslOutputFlag = 1</c>.
    /// </summary>
    private static void ResolvePictureOrder(List<HevcNal> nals, List<HevcPicture> pictures)
    {
        var sequence = -1;
        var afterEndOfSequence = true;
        var nalIndex = 0;
        HevcPicture? associatedIrap = null;
        var previousLsb = 0;
        var previousMsb = 0;
        foreach (var picture in pictures)
        {
            // An end of sequence NAL unit between the previous picture and this one
            for (; nalIndex < picture.Segments[0].Nal.Index; nalIndex++)
                afterEndOfSequence |= nals[nalIndex].Type == NalEos;

            if (picture.IsIrap)
            {
                picture.NoRaslOutputFlag = picture.NalType != NalCra || afterEndOfSequence;
                associatedIrap = picture;
            }
            else if (afterEndOfSequence)
            {
                throw new ToolException("the first picture of the stream, or the first one after an end of sequence, is not an IRAP picture.");
            }

            afterEndOfSequence = false;
            if (picture.NoRaslOutputFlag)
                sequence++;

            picture.Sequence = sequence;
            picture.IsSkipped = picture.IsRasl && associatedIrap!.NoRaslOutputFlag;

            var header = picture.Segments[0].Header;
            var maxLsb = 1 << ((int)picture.Sps.Get("log2_max_pic_order_cnt_lsb_minus4") + 4);
            var lsb = (int)header.Get("slice_pic_order_cnt_lsb");
            int msb;
            if (picture.NoRaslOutputFlag)
                msb = 0;
            else if (lsb < previousLsb && previousLsb - lsb >= maxLsb / 2)
                msb = previousMsb + maxLsb;
            else if (lsb > previousLsb && lsb - previousLsb > maxLsb / 2)
                msb = previousMsb - maxLsb;
            else
                msb = previousMsb;

            picture.PocMsb = msb;
            picture.Poc = msb + lsb;

            // prevTid0Pic: the previous picture of temporal layer 0 that is not a RASL, a RADL or a sub-layer
            // non-reference picture (the even types below 16)
            var subLayerNonReference = picture.NalType < 16 && picture.NalType % 2 == 0;
            if (picture.TemporalId == 0 && !picture.IsRasl && picture.NalType is not (NalRadlN or NalRadlR) && !subLayerNonReference)
                (previousLsb, previousMsb) = (lsb, msb);
        }
    }

    /// <summary>The type and the values of the decoded picture hash of a picture (clause D.3.19), as lowercase hexadecimal
    /// strings, one per colour component: 16 bytes for an MD5, 2 for a CRC, 4 for a checksum.</summary>
    private static (string Type, List<string> Values) ReadPictureHash(HevcPicture picture)
    {
        var syntax = (picture.Hash ?? throw new ToolException(Inv($"the picture {picture.DecodeIndex} (decoding order) has no decoded picture hash SEI message."))).Syntax;
        var components = picture.Format.PlaneCount;
        var values = new List<string>();
        switch (syntax.Get("hash_type", absent: -1))
        {
            case 0:
                for (var c = 0; c < components; c++)
                {
                    var bytes = syntax.Array(Inv($"picture_md5[{c}]"));
                    if (bytes.Count != 16)
                        throw new ToolException("a picture MD5 does not have 16 bytes.");
                    values.Add(Convert.ToHexStringLower([.. bytes.Select(b => (byte)b.Value)]));
                }

                return ("md5", values);
            case 1:
                for (var c = 0; c < components; c++)
                    values.Add(Read(Inv($"picture_crc[{c}]")).ToString("x4", CultureInfo.InvariantCulture));
                return ("crc", values);
            case 2:
                for (var c = 0; c < components; c++)
                    values.Add(Read(Inv($"picture_checksum[{c}]")).ToString("x8", CultureInfo.InvariantCulture));
                return ("checksum", values);
            default:
                throw new ToolException("a decoded picture hash has an unknown hash_type.");
        }

        long Read(string name) => syntax.Has(name) ? syntax.Get(name) : throw new ToolException($"a decoded picture hash does not code {name}.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Feature tags
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The feature tags of a vector: what its parameter sets, its slice segment headers and its other NAL units signal.
    /// A value of a tag lists the distinct values of the stream. The parameter sets that count are the ones a slice
    /// segment refers to.
    /// </summary>
    private static SortedSet<string> Features(HevcTrace trace)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        var pictures = trace.Pictures;
        var segments = pictures.SelectMany(picture => picture.Segments).ToList();
        var slices = segments.Where(segment => !segment.IsDependent).ToList();
        var spsList = segments.Select(segment => segment.Sps).Distinct().ToList();
        var ppsList = segments.Select(segment => segment.Pps).Distinct().ToList();
        var outputs = trace.Outputs;

        // Profile, tier, level and sample format (clauses 7.3.3 and 7.3.2.2, Annex A)
        Values("hevc.profile", spsList.Select(ProfileName));
        Values("hevc.level", spsList.Select(sps => LevelName((int)sps.Get("general_level_idc"))));
        Flag(spsList.Any(sps => sps.Get("general_tier_flag") == 1), "hevc.tier=high");
        var formats = spsList.Select(ReadFormat).ToList();
        Values("hevc.chroma", formats.Select(format => format.ChromaFormat));
        Numbers("hevc.bitDepth", formats.Select(format => (long)format.BitDepthLuma));
        Numbers("hevc.bitDepthChroma", formats.Where(format => format.ChromaFormatIdc != 0 && format.BitDepthChroma != format.BitDepthLuma).Select(format => (long)format.BitDepthChroma));
        Flag(formats.Any(format => format.IdentityMatrix), "hevc.matrix=identity");
        Any(spsList, "hevc.separateColourPlanes", sps => sps.Get("separate_colour_plane_flag") == 1);

        // Block structure
        Numbers("hevc.ctb", spsList.Select(CtbSize));
        Numbers("hevc.minCb", spsList.Select(sps => 8L << (int)sps.Get("log2_min_luma_coding_block_size_minus3")));
        Numbers("hevc.minTb", spsList.Select(sps => 4L << (int)sps.Get("log2_min_luma_transform_block_size_minus2")));
        Numbers("hevc.maxTb", spsList.Select(sps => 4L << (int)(sps.Get("log2_min_luma_transform_block_size_minus2") + sps.Get("log2_diff_max_min_luma_transform_block_size"))));
        Numbers("hevc.tuDepthIntra", spsList.Select(sps => sps.Get("max_transform_hierarchy_depth_intra")));
        Numbers("hevc.tuDepthInter", spsList.Select(sps => sps.Get("max_transform_hierarchy_depth_inter")));
        Values("hevc.ctbs", spsList.Select(sps => Inv($"{CeilDiv(sps.Get("pic_width_in_luma_samples"), CtbSize(sps))}x{CeilDiv(sps.Get("pic_height_in_luma_samples"), CtbSize(sps))}")));
        Values("hevc.crop", spsList.SelectMany(sps => new[] { "left", "right", "top", "bottom" }.Where(side => sps.Get($"conf_win_{side}_offset") != 0)));

        // Coding tools of the sequence parameter set
        Any(spsList, "hevc.amp", sps => sps.Get("amp_enabled_flag") == 1);
        Numbers("hevc.strongIntraSmoothing", spsList.Select(sps => sps.Get("strong_intra_smoothing_enabled_flag")));
        var pcm = spsList.Where(sps => sps.Get("pcm_enabled_flag") == 1).ToList();
        if (pcm.Count > 0)
        {
            tags.Add("hevc.pcm");
            Values("hevc.pcm.bitDepth", pcm.Select(sps => sps.Get("chroma_format_idc") == 0
                ? Inv($"{sps.Get("pcm_sample_bit_depth_luma_minus1") + 1}")
                : Inv($"{sps.Get("pcm_sample_bit_depth_luma_minus1") + 1}/{sps.Get("pcm_sample_bit_depth_chroma_minus1") + 1}")));
            Values("hevc.pcm.size", pcm.Select(sps =>
            {
                var min = 8L << (int)sps.Get("log2_min_pcm_luma_coding_block_size_minus3");
                return Inv($"{min}-{min << (int)sps.Get("log2_diff_max_min_pcm_luma_coding_block_size")}");
            }));
            Any(pcm, "hevc.pcm.loopFilterDisabled", sps => sps.Get("pcm_loop_filter_disabled_flag") == 1);
        }

        // Scaling lists (clause 7.3.4): the lists of the picture parameter set replace those of the sequence
        Values("hevc.scalingList", segments.Where(segment => segment.Sps.Get("scaling_list_enabled_flag") == 1).Select(segment =>
            segment.Pps.Get("pps_scaling_list_data_present_flag") == 1 ? "pps" : segment.Sps.Get("sps_scaling_list_data_present_flag") == 1 ? "sps" : "default"));
        var scalingData = spsList.Where(sps => sps.Get("sps_scaling_list_data_present_flag") == 1).Concat(ppsList.Where(pps => pps.Get("pps_scaling_list_data_present_flag") == 1)).ToList();
        Values("hevc.scalingList.coding", scalingData.SelectMany(ScalingListCodings));
        foreach (var set in scalingData)
            CheckScalingLists(set);

        // Range extensions of the sequence parameter set (clause 7.3.2.2.2)
        Any(spsList, "hevc.transformSkipRotation", sps => sps.Get("transform_skip_rotation_enabled_flag") == 1);
        Any(spsList, "hevc.transformSkipContext", sps => sps.Get("transform_skip_context_enabled_flag") == 1);
        Any(spsList, "hevc.implicitRdpcm", sps => sps.Get("implicit_rdpcm_enabled_flag") == 1);
        Any(spsList, "hevc.explicitRdpcm", sps => sps.Get("explicit_rdpcm_enabled_flag") == 1);
        Any(spsList, "hevc.extendedPrecision", sps => sps.Get("extended_precision_processing_flag") == 1);
        Any(spsList, "hevc.intraSmoothingDisabled", sps => sps.Get("intra_smoothing_disabled_flag") == 1);
        Any(spsList, "hevc.highPrecisionOffsets", sps => sps.Get("high_precision_offsets_enabled_flag") == 1);
        Any(spsList, "hevc.persistentRice", sps => sps.Get("persistent_rice_adaptation_enabled_flag") == 1);
        Any(spsList, "hevc.cabacBypassAlignment", sps => sps.Get("cabac_bypass_alignment_enabled_flag") == 1);

        // Video usability information (Annex E)
        Values("hevc.colourDescription", spsList.Where(sps => sps.Get("colour_description_present_flag") == 1)
            .Select(sps => Inv($"{sps.Get("colour_primaries")}/{sps.Get("transfer_characteristics")}/{sps.Get("matrix_coefficients")}")));
        Any(spsList, "hevc.fullRange", sps => sps.Get("video_full_range_flag") == 1);
        Numbers("hevc.chromaLoc", spsList.Where(sps => sps.Get("chroma_loc_info_present_flag") == 1).Select(sps => sps.Get("chroma_sample_loc_type_top_field")));
        Any(spsList, "hevc.defaultDisplayWindow", sps => sps.Get("default_display_window_flag") == 1);
        Any(spsList, "hevc.hrd", sps => sps.Get("vui_hrd_parameters_present_flag") == 1);
        Any(spsList, "hevc.fieldSeq", sps => sps.Get("field_seq_flag") == 1);

        // Coding tools of the picture parameter set (clause 7.3.2.3)
        Numbers("hevc.signDataHiding", ppsList.Select(pps => pps.Get("sign_data_hiding_enabled_flag")));
        Any(ppsList, "hevc.constrainedIntraPred", pps => pps.Get("constrained_intra_pred_flag") == 1);
        Any(ppsList, "hevc.transformSkip", pps => pps.Get("transform_skip_enabled_flag") == 1);
        Any(ppsList, "hevc.transquantBypass", pps => pps.Get("transquant_bypass_enabled_flag") == 1);
        Any(ppsList, "hevc.wpp", pps => pps.Get("entropy_coding_sync_enabled_flag") == 1);
        Any(ppsList, "hevc.outputFlagPresent", pps => pps.Get("output_flag_present_flag") == 1);
        Numbers("hevc.parallelMergeLevel", ppsList.Where(pps => pps.Get("log2_parallel_merge_level_minus2") != 0).Select(pps => pps.Get("log2_parallel_merge_level_minus2") + 2));
        var qpGroups = segments.Where(segment => segment.Pps.Get("cu_qp_delta_enabled_flag") == 1).ToList();
        if (qpGroups.Count > 0)
        {
            tags.Add("hevc.cuQpDelta");
            Numbers("hevc.qgSize", qpGroups.Select(segment => CtbSize(segment.Sps) >> (int)segment.Pps.Get("diff_cu_qp_delta_depth")));
        }

        Values("hevc.chromaQpOffset", ppsList.Where(pps => pps.Get("pps_cb_qp_offset") != 0 || pps.Get("pps_cr_qp_offset") != 0).Select(pps => Inv($"{pps.Get("pps_cb_qp_offset")}/{pps.Get("pps_cr_qp_offset")}")));
        Any(slices, "hevc.sliceChromaQpOffset", slice => slice.Header.Get("slice_cb_qp_offset") != 0 || slice.Header.Get("slice_cr_qp_offset") != 0);

        var tiles = ppsList.Where(pps => pps.Get("tiles_enabled_flag") == 1).ToList();
        if (tiles.Count > 0)
        {
            Values("hevc.tiles", tiles.Select(pps => Inv($"{pps.Get("num_tile_columns_minus1") + 1}x{pps.Get("num_tile_rows_minus1") + 1}")));
            Values("hevc.tiles.spacing", tiles.Select(pps => pps.Get("uniform_spacing_flag") == 1 ? "uniform" : "explicit"));
            Numbers("hevc.tiles.loopFilterAcross", tiles.Select(pps => pps.Get("loop_filter_across_tiles_enabled_flag")));
        }

        // Range extensions of the picture parameter set (clause 7.3.2.3.2)
        Numbers("hevc.transformSkip.maxSize", ppsList.Where(pps => pps.Get("log2_max_transform_skip_block_size_minus2") != 0).Select(pps => 4L << (int)pps.Get("log2_max_transform_skip_block_size_minus2")));
        Any(ppsList, "hevc.crossComponentPrediction", pps => pps.Get("cross_component_prediction_enabled_flag") == 1);
        Numbers("hevc.chromaQpOffsetList", ppsList.Where(pps => pps.Get("chroma_qp_offset_list_enabled_flag") == 1).Select(pps => pps.Get("chroma_qp_offset_list_len_minus1") + 1));
        Values("hevc.saoOffsetScale", ppsList.Where(pps => pps.Get("log2_sao_offset_scale_luma") != 0 || pps.Get("log2_sao_offset_scale_chroma") != 0)
            .Select(pps => Inv($"{pps.Get("log2_sao_offset_scale_luma")}/{pps.Get("log2_sao_offset_scale_chroma")}")));

        // Slices (clause 7.3.6.1)
        Values("hevc.sliceTypes", slices.Select(slice => slice.Header.Get("slice_type") switch { 0 => "B", 1 => "P", 2 => "I", _ => throw new ToolException("Unexpected slice_type.") }));
        tags.Add(slices.All(slice => slice.Header.Get("slice_type") == 2) ? "hevc.frameTypes=intra" : "hevc.frameTypes=inter");
        Numbers("hevc.sliceQp", slices.Select(slice => 26 + slice.Pps.Get("init_qp_minus26") + slice.Header.Get("slice_qp_delta")));
        var slicesPerPicture = pictures.Max(picture => picture.Segments.Count(segment => !segment.IsDependent));
        Flag(slicesPerPicture > 1, Inv($"hevc.slices={slicesPerPicture}"));
        if (segments.Any(segment => segment.IsDependent))
            tags.Add(Inv($"hevc.dependentSlices={pictures.Max(picture => picture.Segments.Count)}"));

        Any(slices, "hevc.cabacInit", slice => slice.Header.Get("cabac_init_flag") == 1);
        Any(slices, "hevc.cuChromaQpOffset", slice => slice.Header.Get("cu_chroma_qp_offset_enabled_flag") == 1);

        // In-loop filters. slice_loop_filter_across_slices_enabled_flag is inferred from the picture parameter set when
        // the slice header does not code it
        Any(slices, "hevc.loopFilterAcrossSlices=0", slice => slice.Header.Get("slice_loop_filter_across_slices_enabled_flag", slice.Pps.Get("pps_loop_filter_across_slices_enabled_flag")) == 0);
        Numbers("hevc.deblocking", slices.Select(slice => Deblocking(slice).Disabled ? 0L : 1L));
        Values("hevc.deblocking.offsets", slices.Select(Deblocking).Where(filter => !filter.Disabled && (filter.Beta != 0 || filter.Tc != 0)).Select(filter => Inv($"{filter.Beta}/{filter.Tc}")));
        Any(slices, "hevc.deblocking.sliceOverride", slice => slice.Header.Get("deblocking_filter_override_flag") == 1);
        Values("hevc.sao", slices.SelectMany(SaoStates));

        // Inter prediction: the P and B slices
        var inter = slices.Where(slice => slice.Header.Get("slice_type") != 2).ToList();
        if (inter.Count > 0)
        {
            Numbers("hevc.temporalMvp", inter.Select(slice => slice.Header.Get("slice_temporal_mvp_enabled_flag")));
            Numbers("hevc.mergeCand", inter.Select(slice => 5 - slice.Header.Get("five_minus_max_num_merge_cand")));
            Numbers("hevc.refs", inter.SelectMany(slice => ActiveReferences(slice)));
            Any(inter, "hevc.mvdL1Zero", slice => slice.Header.Get("mvd_l1_zero_flag") == 1);
            Any(inter, "hevc.collocatedFromL1", slice => slice.Header.Get("collocated_from_l0_flag", absent: 1) == 0);
            Any(inter, "hevc.collocatedRefIdx", slice => slice.Header.Get("collocated_ref_idx") != 0);
            Values("hevc.weightedPred", inter.SelectMany(slice => new[] { slice.Header.Get("slice_type") == 1 && slice.Pps.Get("weighted_pred_flag") == 1 ? "p" : null,
                slice.Header.Get("slice_type") == 0 && slice.Pps.Get("weighted_bipred_flag") == 1 ? "b" : null }.OfType<string>()));
            Values("hevc.weights", inter.SelectMany(slice => new[] { slice.Header.Elements.Any(e => e.Value == 1 && e.Name.StartsWith("luma_weight_l", StringComparison.Ordinal)) ? "luma" : null,
                slice.Header.Elements.Any(e => e.Value == 1 && e.Name.StartsWith("chroma_weight_l", StringComparison.Ordinal)) ? "chroma" : null }.OfType<string>()));
            Any(inter, "hevc.listsModification", slice => slice.Header.Get("ref_pic_list_modification_flag_l0") == 1 || slice.Header.Get("ref_pic_list_modification_flag_l1") == 1);
            Any(inter, "hevc.longTermRefs", slice => slice.Header.Get("num_long_term_sps") != 0 || slice.Header.Get("num_long_term_pics") != 0);
        }

        // Reference picture sets (clause 7.3.7): the slices of the pictures that are not IDR pictures
        var withRps = slices.Where(slice => slice.Header.Has("short_term_ref_pic_set_sps_flag")).ToList();
        Values("hevc.rps", withRps.Select(slice => slice.Header.Get("short_term_ref_pic_set_sps_flag") == 1 ? "sps" : "slice"));
        Flag(spsList.Any(sps => sps.All("inter_ref_pic_set_prediction_flag").Contains(1)) || withRps.Any(slice => slice.Header.Get("inter_ref_pic_set_prediction_flag") == 1), "hevc.rps.interPrediction");
        Flag(pictures.Any(picture => picture.NalType < 16 && picture.NalType % 2 == 1 && picture.Segments.Any(segment => !segment.IsDependent && segment.Header.Get("slice_type") == 0)), "hevc.referenceB");

        // NAL units and SEI messages
        Values("hevc.nal", trace.Nals.Where(nal => NalNames.ContainsKey(nal.Type)).Select(nal => NalNames[nal.Type]));
        Values("hevc.sei", trace.Nals.SelectMany(nal => nal.SeiPayloads).Select(type => SeiNames.TryGetValue(type, out var name) ? name : Inv($"{type}")));
        Values("hevc.pictureHash", pictures.Where(picture => picture.Hash is not null).Select(picture => ReadPictureHash(picture).Type));
        var temporalLayers = segments.Max(segment => segment.Nal.TemporalId) + 1;
        Flag(temporalLayers > 1, Inv($"hevc.temporalLayers={temporalLayers}"));
        foreach (var type in new[] { NalVps, NalSps, NalPps })
        {
            // The same parameter set sent again, or another one with the same identifier
            var sets = trace.Nals.Where(nal => nal.Type == type).ToList();
            Flag(sets.Count > 1, "hevc.repeatedParameterSets");
        }

        // Parameter sets whose content changes inside the stream: sent again with other values, or under another identifier
        var spsContents = spsList.Select(Content).Distinct(StringComparer.Ordinal).Count();
        var ppsContents = ppsList.Select(Content).Distinct(StringComparer.Ordinal).Count();
        Flag(spsContents > 1, Inv($"hevc.sps={spsContents}"));
        Flag(ppsContents > 1, Inv($"hevc.pps={ppsContents}"));

        // Output (clauses 8.3.1 and C.5.2)
        Flag(outputs.Count > 1, Inv($"hevc.frames={outputs.Count}"));
        Flag(!outputs.Select(picture => picture.DecodeIndex).SequenceEqual(outputs.Select(picture => picture.DecodeIndex).Order()), "hevc.outputOrder=reordered");
        var sequences = pictures.Max(picture => picture.Sequence) + 1;
        Flag(sequences > 1, Inv($"hevc.sequences={sequences}"));
        var sizes = outputs.Select(picture => picture.Size).Distinct().ToList();
        Flag(sizes.Count > 1, "hevc.frameSizes=" + string.Join(',', sizes.Select(size => Inv($"{size.Width}x{size.Height}"))));
        Flag(pictures.Any(picture => picture.IsSkipped), "hevc.raslSkipped");
        Flag(pictures.Any(picture => picture.IsDiscarded), "hevc.noOutputOfPriorPics");
        Flag(pictures.Any(picture => picture.PocMsb != 0), "hevc.pocWrap");
        Numbers("hevc.pocLsbBits", spsList.Select(sps => sps.Get("log2_max_pic_order_cnt_lsb_minus4") + 4));
        Numbers("hevc.reorder", spsList.Select(sps => sps.Array("sps_max_num_reorder_pics").Max(e => e.Value)).Where(count => count > 0));

        // What the library does not decode
        Values("hevc.unsupported", new[]
        {
            tags.Contains("hevc.extendedPrecision") ? "extendedPrecision" : null,
            tags.Contains("hevc.cabacBypassAlignment") ? "cabacBypassAlignment" : null,
            formats.Any(format => format.BitDepthLuma > 12 || format.BitDepthChroma > 12) ? "bitDepth" : null,
            formats.Any(format => format.ChromaFormatIdc != 0 && format.BitDepthLuma != format.BitDepthChroma) ? "unequalBitDepth" : null,
            tags.Contains("hevc.separateColourPlanes") ? "separateColourPlanes" : null,
            tags.Contains("hevc.fieldSeq") ? "fieldSeq" : null,
        }.OfType<string>());

        return tags;

        void Flag(bool condition, string tag)
        {
            if (condition)
                tags.Add(tag);
        }

        void Any<T>(IEnumerable<T> items, string tag, Func<T, bool> predicate) => Flag(items.Any(predicate), tag);

        void Values(string key, IEnumerable<string> values)
        {
            var distinct = values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (distinct.Count > 0)
                tags.Add(key + "=" + string.Join(',', distinct));
        }

        void Numbers(string key, IEnumerable<long> values)
        {
            var distinct = values.Distinct().Order().ToList();
            if (distinct.Count > 0)
                tags.Add(key + "=" + string.Join(',', distinct.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>What a slice does with sample adaptive offset: <c>off</c> when the sequence disables it, otherwise the
    /// planes the slice enables it for, or <c>sliceOff</c> when the slice enables it for none.</summary>
    private static IEnumerable<string> SaoStates(HevcSliceSegment slice)
    {
        if (slice.Sps.Get("sample_adaptive_offset_enabled_flag") == 0)
        {
            yield return "off";
            yield break;
        }

        var luma = slice.Header.Get("slice_sao_luma_flag") == 1;
        var chroma = slice.Header.Get("slice_sao_chroma_flag") == 1;
        if (luma)
            yield return "luma";
        if (chroma)
            yield return "chroma";
        if (!luma && !chroma)
            yield return "sliceOff";
    }

    /// <summary>Every syntax element of a parameter set: two sets with the same content are the same set sent twice.</summary>
    private static string Content(HevcSyntax set) => string.Join(';', set.Elements.Select(element => Inv($"{element.Name}={element.Value}")));

    private static long CtbSize(HevcSyntax sps) => 8L << (int)(sps.Get("log2_min_luma_coding_block_size_minus3") + sps.Get("log2_diff_max_min_luma_coding_block_size"));

    private static long CeilDiv(long value, long divisor) => (value + divisor - 1) / divisor;

    /// <summary>The numbers of active reference pictures of the lists of a P or B slice (clause 7.4.7.1).</summary>
    private static IEnumerable<long> ActiveReferences(HevcSliceSegment slice)
    {
        var header = slice.Header;
        var overridden = header.Get("num_ref_idx_active_override_flag") == 1;
        yield return (overridden ? header.Get("num_ref_idx_l0_active_minus1") : slice.Pps.Get("num_ref_idx_l0_default_active_minus1")) + 1;
        if (header.Get("slice_type") == 0)
            yield return (overridden ? header.Get("num_ref_idx_l1_active_minus1") : slice.Pps.Get("num_ref_idx_l1_default_active_minus1")) + 1;
    }

    /// <summary>The deblocking filter of a slice (clauses 7.4.3.3.1 and 7.4.7.1): the slice header overrides the picture
    /// parameter set when <c>deblocking_filter_override_flag</c> is set. The offsets are those the syntax codes (halves).</summary>
    private static (bool Disabled, long Beta, long Tc) Deblocking(HevcSliceSegment slice)
    {
        if (slice.Header.Get("deblocking_filter_override_flag") == 1)
            return (slice.Header.Get("slice_deblocking_filter_disabled_flag") == 1, slice.Header.Get("slice_beta_offset_div2"), slice.Header.Get("slice_tc_offset_div2"));

        return (slice.Pps.Get("pps_deblocking_filter_disabled_flag") == 1, slice.Pps.Get("pps_beta_offset_div2"), slice.Pps.Get("pps_tc_offset_div2"));
    }

    /// <summary>How the lists of a scaling_list_data() are coded (clause 7.3.4): <c>explicit</c> (its coefficients),
    /// <c>copy</c> (a reference list) or <c>default</c> (the default list, a reference delta of zero).</summary>
    private static IEnumerable<string> ScalingListCodings(HevcSyntax set)
    {
        foreach (var (name, value) in set.Array("scaling_list_pred_mode_flag"))
        {
            if (value == 1)
            {
                yield return "explicit";
                continue;
            }

            // The delta has the indices of its flag: scaling_list_pred_matrix_id_delta[sizeId][matrixId]
            var delta = set.Get("scaling_list_pred_matrix_id_delta" + name["scaling_list_pred_mode_flag".Length..], absent: -1);
            if (delta < 0)
                throw new ToolException("a scaling list is predicted without a reference.");

            yield return delta == 0 ? "default" : "copy";
        }
    }

    /// <summary>
    /// Reconstructs the coefficients of every list a scaling_list_data() codes (clauses 7.3.4 and 7.4.5) and refuses a
    /// value of 0: <c>ScalingList[sizeId][matrixId][i]</c> and the DC coefficient of a 16x16 or 32x32 list shall be
    /// greater than 0. FFmpeg, libde265 and HM all decode a stream that breaks the rule, to the same samples, so that
    /// their agreement says nothing about it (kvazaar writes such lists from a file).
    /// </summary>
    private static void CheckScalingLists(HevcSyntax set)
    {
        const string ModePrefix = "scaling_list_pred_mode_flag";
        const string DcPrefix = "scaling_list_dc_coef_minus8[";
        const string DeltaPrefix = "scaling_list_delta_coeff[";
        var elements = set.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            var (name, mode) = elements[i];
            if (!name.StartsWith(ModePrefix + "[", StringComparison.Ordinal) || mode != 1)
                continue;

            // An explicit list: scaling_list_dc_coef_minus8 for the sizes above 8x8, then one delta per coefficient
            var list = name[ModePrefix.Length..];
            var nextCoef = 8L;
            var count = 0;
            for (i++; i < elements.Count; i++)
            {
                var (element, value) = elements[i];
                if (element.StartsWith(DcPrefix, StringComparison.Ordinal) && count == 0)
                {
                    nextCoef = value + 8;
                    if (nextCoef is < 1 or > 255)
                        throw new ToolException(Inv($"the DC coefficient of the scaling list {list} is {nextCoef}: clause 7.4.5 requires 1 to 255."));
                }
                else if (element.StartsWith(DeltaPrefix, StringComparison.Ordinal))
                {
                    nextCoef = (nextCoef + value + 256) % 256;
                    if (nextCoef == 0)
                        throw new ToolException(Inv($"the coefficient {count} of the scaling list {list} is 0: clause 7.4.5 requires a value greater than 0."));

                    count++;
                }
                else
                {
                    break;
                }
            }

            if (count is not (16 or 64))
                throw new ToolException(Inv($"the scaling list {list} has {count} coefficients."));

            i--;
        }
    }

    /// <summary>The profile of profile_tier_level() (Annex A). The format range extensions profiles (general_profile_idc
    /// 4) and the high throughput ones (5) are named after their constraint flags: the chroma format and the bit depth
    /// they allow, <c>intra</c> and <c>still</c>.</summary>
    private static string ProfileName(HevcSyntax sps)
    {
        var idc = sps.Get("general_profile_idc");
        switch (idc)
        {
            case 1:
                return "main";
            case 2:
                return "main10";
            case 3:
                return "mainStillPicture";
            case 4 or 5:
                var chroma = sps.Get("general_max_monochrome_constraint_flag") == 1 ? "400" : sps.Get("general_max_420chroma_constraint_flag") == 1 ? "420" : sps.Get("general_max_422chroma_constraint_flag") == 1 ? "422" : "444";
                var depth = sps.Get("general_max_8bit_constraint_flag") == 1 ? 8 : sps.Get("general_max_10bit_constraint_flag") == 1 ? 10 : sps.Get("general_max_12bit_constraint_flag") == 1 ? 12 : 16;
                return Inv($"{(idc == 4 ? "rext" : "highThroughput")}-{chroma}-{depth}{(sps.Get("general_intra_constraint_flag") == 1 ? "-intra" : "")}{(sps.Get("general_one_picture_only_constraint_flag") == 1 ? "-still" : "")}");
            default:
                return Inv($"profile{idc}");
        }
    }

    /// <summary>general_level_idc is thirty times the level number.</summary>
    private static string LevelName(int idc) => idc % 30 == 0 ? Inv($"{idc / 30}") : Inv($"{idc / 30}.{idc % 30 / 3}");

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\[trace_headers @ [^\]]+\] ")]
    private static partial Regex TracePrefixRegex();

    // "<bit position> <name> <bits> = <value>"; the bit string is empty for an inferred element
    [GeneratedRegex(@"^(?<position>\d+)\s+(?<name>\S+)\s+(?<bits>[01]*)\s*= (?<value>-?\d+)$", RegexOptions.ExplicitCapture)]
    private static partial Regex TraceElementRegex();
}
