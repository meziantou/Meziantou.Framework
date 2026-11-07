using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The headers of a vector as FFmpeg's trace_headers bitstream filter prints them (libavcodec's coded bitstream reader, an
// AV1 header parser independent of libaom, SVT-AV1 and dav1d), and the feature tags derived from them. The tags describe
// what the bitstream signals, never what the encoder was asked to do: an encoder option does not guarantee that a tool is used.
internal static partial class AvifCorpus
{
    /// <summary>The syntax elements of one OBU, in bitstream order. Array elements keep their indices in the name.</summary>
    private sealed class Av1Syntax
    {
        private readonly List<(string Name, long Value)> _elements = [];
        private readonly Dictionary<string, (int Position, int BitCount)> _positions = new(StringComparer.Ordinal);

        public IReadOnlyList<(string Name, long Value)> Elements => _elements;

        /// <param name="position">The position of the first bit of the element, counted from the first bit of its OBU (the OBU header).</param>
        /// <param name="bitCount">The number of bits the element is coded with; 0 for an inferred element.</param>
        public void Add(string name, long value, int position, int bitCount)
        {
            _elements.Add((name, value));
            _positions.TryAdd(name, (position, bitCount));
        }

        /// <summary>Where the first element of that name is coded: its first bit, counted from the first bit of its OBU, and its number of bits.</summary>
        public (int Position, int BitCount) GetPosition(string name) => _positions.TryGetValue(name, out var position) ? position : throw new ToolException($"The header does not code {name}.");

        public bool Has(string name) => _elements.Any(e => e.Name == name);

        /// <summary>The value of an element, or <paramref name="absent"/> when the header does not code it.</summary>
        public long Get(string name, long absent = 0)
        {
            foreach (var (element, value) in _elements)
            {
                if (element == name)
                    return value;
            }

            return absent;
        }

        /// <summary>The values of the elements <c>prefix[...]</c>, in bitstream order.</summary>
        public List<(string Name, long Value)> Array(string prefix) => [.. _elements.Where(e => e.Name.StartsWith(prefix + "[", StringComparison.Ordinal))];
    }

    private sealed class Av1FrameTrace
    {
        public Av1Syntax Header { get; } = new();

        /// <summary>The number of tile groups of the frame: the one of an OBU_FRAME plus every OBU_TILE_GROUP.</summary>
        public int TileGroups { get; set; }

        /// <summary>Whether the header only shows a frame decoded earlier (<c>show_existing_frame</c>): it codes nothing else.</summary>
        public bool ShowsExistingFrame => Header.Get("show_existing_frame") == 1;

        /// <summary>The spatial layer of the OBU of the header (<c>spatial_id</c> of the OBU extension, 0 without extension).</summary>
        public int SpatialId { get; init; }

        /// <summary>The temporal layer of the OBU of the header (<c>temporal_id</c> of the OBU extension, 0 without extension).</summary>
        public int TemporalId { get; init; }

        /// <summary>The index of the temporal unit of the header: the number of temporal delimiters before it, minus one.</summary>
        public int TemporalUnit { get; init; }

        /// <summary>Whether the header shows a frame: a shown frame or a <c>show_existing_frame</c>. A hidden frame
        /// (<c>show_frame = 0</c>) shows nothing. Absent from a reduced still picture header: shown.</summary>
        public bool IsShown => ShowsExistingFrame || Header.Get("show_frame", absent: 1) == 1;

        /// <summary>Whether a decoder outputs a frame for this header, set by <see cref="ResolveOutputs"/>: a shown frame,
        /// unless the operating point has several spatial layers; a temporal unit then outputs one frame, the shown frame
        /// of the highest spatial layer it holds (section 7.18.1 "Output process"; what dav1d and libaom do by default).</summary>
        public bool IsOutput { get; set; }

        /// <summary>The size of the frame as a decoder outputs it, set by <see cref="ResolveFrameSizes"/>: the width after
        /// super-resolution upscaling (<c>UpscaledWidth</c>) and <c>FrameHeight</c>; the render size does not change it.</summary>
        public (int Width, int Height) Size { get; set; }
    }

    private sealed record Av1Trace(Av1Syntax Sequence, IReadOnlyList<Av1FrameTrace> Frames);

    /// <summary>Parses the trace of the packets of a stream (the copy of the sequence header in the extradata is skipped).</summary>
    private static Av1Trace ParseTrace(string log)
    {
        Av1Syntax? sequence = null;
        var frames = new List<Av1FrameTrace>();
        Av1Syntax? current = null;
        Av1Syntax? unitHeader = null;
        var temporalUnit = -1;
        var inPackets = false;
        foreach (var rawLine in log.Split('\n'))
        {
            var prefix = TracePrefixRegex().Match(rawLine);
            if (!prefix.Success)
                continue;

            var line = rawLine[prefix.Length..].TrimEnd();
            if (line.StartsWith("Packet:", StringComparison.Ordinal))
            {
                inPackets = true;
                current = null;
                continue;
            }

            if (!inPackets)
                continue;

            var element = TraceElementRegex().Match(line);
            if (element.Success)
            {
                current?.Add(element.Groups["name"].Value, long.Parse(element.Groups["value"].Value, CultureInfo.InvariantCulture), int.Parse(element.Groups["position"].Value, CultureInfo.InvariantCulture), element.Groups["bits"].Length);
                continue;
            }

            switch (line)
            {
                case "Sequence Header":
                    // A repeated sequence header must be identical (section 7.5); the last one is kept and the others are
                    // compared by the decoders
                    current = sequence = new Av1Syntax();
                    break;
                case "Frame Header":
                    // The layer of the frame is in the extension of its OBU header, which precedes it in the trace
                    frames.Add(new Av1FrameTrace { SpatialId = (int)(unitHeader?.Get("spatial_id") ?? 0), TemporalId = (int)(unitHeader?.Get("temporal_id") ?? 0), TemporalUnit = temporalUnit });
                    current = frames[^1].Header;
                    break;
                case "Tile Group":
                    if (frames.Count == 0)
                        throw new ToolException("trace_headers reports a tile group before any frame header.");
                    frames[^1].TileGroups++;
                    current = null;
                    break;
                case "OBU header":
                    current = unitHeader = new Av1Syntax();
                    break;
                case "Temporal Delimiter":
                    temporalUnit++;
                    current = null;
                    break;
                case "Padding":
                    current = null;
                    break;
                default:
                    // Metadata, tile lists and anything this generator does not expect in a vector
                    throw new ToolException("Unexpected trace_headers section: " + line);
            }
        }

        if (sequence is null || frames.Count == 0)
            throw new ToolException("trace_headers found no sequence header or no frame header.");

        var trace = new Av1Trace(sequence, frames);
        ResolveFrameSizes(trace);
        ResolveOutputs(trace);
        return trace;
    }

    /// <summary>The spatial layers of the operating point a decoder selects by default, the first one: bits 8 to 11 of its
    /// <c>operating_point_idc</c> (section 6.4.1). 0 when the stream has no scalability information.</summary>
    private static int SpatialLayerMask(Av1Syntax sequence) => (int)(sequence.Get("operating_point_idc[0]") >> 8) & 0xF;

    /// <summary>
    /// Marks the frames a decoder outputs (section 7.18.1 "Output process"). Without scalability, or with one spatial
    /// layer, every shown frame is output. When the first operating point has several spatial layers, a temporal unit
    /// outputs one frame: the shown frame of the highest spatial layer that the unit holds (the lower layers are decoded
    /// and not output), which is what dav1d and libaom do unless they are asked for every layer. The streams of this
    /// corpus have no unit outside their first operating point: a stream that has one fails.
    /// </summary>
    private static void ResolveOutputs(Av1Trace trace)
    {
        var idc = (int)trace.Sequence.Get("operating_point_idc[0]");
        foreach (var frame in trace.Frames)
        {
            if (idc != 0 && (((idc >> 8) >> frame.SpatialId) & 1) == 0)
                throw new ToolException(Inv($"a frame of spatial layer {frame.SpatialId} is outside the first operating point (idc {idc}): the outputs cannot be followed from the trace."));
            if (idc != 0 && ((idc >> frame.TemporalId) & 1) == 0)
                throw new ToolException(Inv($"a frame of temporal layer {frame.TemporalId} is outside the first operating point (idc {idc}): the outputs cannot be followed from the trace."));

            frame.IsOutput = frame.IsShown;
        }

        if (int.PopCount(SpatialLayerMask(trace.Sequence)) <= 1)
            return;

        foreach (var unit in trace.Frames.Where(frame => frame.IsShown).GroupBy(frame => frame.TemporalUnit))
        {
            var highest = unit.Max(frame => frame.SpatialId);
            if (unit.GroupBy(frame => frame.SpatialId).Any(layer => layer.Count() > 1))
                throw new ToolException("a temporal unit shows two frames of one spatial layer.");

            foreach (var frame in unit)
                frame.IsOutput = frame.SpatialId == highest;
        }
    }

    /// <summary>
    /// Computes the size of every frame from the headers alone, following the reference slots the way a decoder does
    /// (sections 5.9.5 "Frame size syntax", 5.9.7 "Frame size with refs syntax" and 7.20 "Reference frame update process"):
    /// the size of a frame is explicit, the maximum of the sequence, or the size of one of its references
    /// (<c>found_ref</c>), and a <c>show_existing_frame</c> outputs the frame of a slot. trace_headers does not print the
    /// elements a header infers, so an element is tested by its presence, never by the flag that implies it.
    /// </summary>
    private static void ResolveFrameSizes(Av1Trace trace)
    {
        const int KeyFrame = 0;
        const int SwitchFrame = 3;
        var sequence = trace.Sequence;
        var slots = new (int Width, int Height, bool IsKeyFrame)?[8];
        foreach (var frame in trace.Frames)
        {
            var header = frame.Header;
            if (frame.ShowsExistingFrame)
            {
                var shown = slots[header.Get("frame_to_show_map_idx")] ?? throw new ToolException("show_existing_frame shows a reference slot that holds no frame.");
                frame.Size = (shown.Width, shown.Height);
                if (shown.IsKeyFrame)
                {
                    // Showing a key frame again refreshes every slot with it
                    Array.Fill(slots, shown);
                }

                continue;
            }

            if (header.Get("frame_refs_short_signaling") == 1)
                throw new ToolException("frame_refs_short_signaling = 1: the reference slots cannot be followed from the trace.");

            var type = header.Get("frame_type");
            var fromReference = Enumerable.Range(0, 7).Where(i => header.Get(Inv($"found_ref[{i}]")) == 1).Select(i => (int?)i).FirstOrDefault();
            if (fromReference is { } index)
            {
                var reference = slots[header.Get(Inv($"ref_frame_idx[{index}]"))] ?? throw new ToolException("found_ref points to a reference slot that holds no frame.");
                frame.Size = (reference.Width, reference.Height);
            }
            else if (header.Has("frame_width_minus_1"))
            {
                frame.Size = ((int)header.Get("frame_width_minus_1") + 1, (int)header.Get("frame_height_minus_1") + 1);
            }
            else
            {
                frame.Size = ((int)sequence.Get("max_frame_width_minus_1") + 1, (int)sequence.Get("max_frame_height_minus_1") + 1);
            }

            // Inferred (and not printed) to be every slot for a shown key frame and for a switch frame
            var shownFrame = header.Get("show_frame", absent: 1) == 1;
            if (!header.Has("refresh_frame_flags") && !((type == KeyFrame && shownFrame) || type == SwitchFrame))
                throw new ToolException("A frame header codes no refresh_frame_flags where they cannot be inferred.");

            var refresh = header.Get("refresh_frame_flags", absent: 0xFF);
            for (var slot = 0; slot < slots.Length; slot++)
            {
                if ((refresh & (1L << slot)) != 0)
                    slots[slot] = (frame.Size.Width, frame.Size.Height, type == KeyFrame);
            }
        }
    }

    /// <summary>The color configuration of the sequence header (AV1 specification, section 5.5.2 "Color config syntax").</summary>
    private sealed record Av1ColorConfig(int BitDepth, bool Monochrome, int SubsamplingX, int SubsamplingY, bool IdentityMatrix)
    {
        public string Subsampling => (Monochrome, SubsamplingX, SubsamplingY) switch
        {
            (true, _, _) => "4:0:0",
            (false, 1, 1) => "4:2:0",
            (false, 1, 0) => "4:2:2",
            (false, 0, 0) => "4:4:4",
            _ => throw new ToolException("Unexpected subsampling."),
        };

        /// <summary>The FFmpeg pixel format that holds the decoded planes without any conversion. An identity-matrix stream
        /// is planar GBR, whose plane order G, B, R is the bitstream order Y, U, V.</summary>
        public string PixelFormat
        {
            get
            {
                var name = (Monochrome, IdentityMatrix) switch
                {
                    (true, _) => "gray",
                    (false, true) => "gbrp",
                    _ => "yuv" + Subsampling.Replace(":", "", StringComparison.Ordinal) + "p",
                };
                if (BitDepth == 8)
                    return name;
                return name + BitDepth.ToString(CultureInfo.InvariantCulture) + "le";
            }
        }

        public int PlaneCount => Monochrome ? 1 : 3;

        public int BytesPerSample => BitDepth > 8 ? 2 : 1;
    }

    private static Av1ColorConfig ReadColorConfig(Av1Syntax sequence)
    {
        var profile = (int)sequence.Get("seq_profile");
        var highBitDepth = sequence.Get("high_bitdepth") == 1;
        var bitDepth = profile == 2 && highBitDepth ? (sequence.Get("twelve_bit") == 1 ? 12 : 10) : (highBitDepth ? 10 : 8);
        var monochrome = profile != 1 && sequence.Get("mono_chrome") == 1;
        // Without a color description the matrix coefficients are unspecified (2)
        var matrix = sequence.Get("color_description_present_flag") == 1 ? sequence.Get("matrix_coefficients") : 2;
        var primaries = sequence.Get("color_description_present_flag") == 1 ? sequence.Get("color_primaries") : 2;
        var transfer = sequence.Get("color_description_present_flag") == 1 ? sequence.Get("transfer_characteristics") : 2;
        int subsamplingX;
        int subsamplingY;
        if (monochrome)
        {
            (subsamplingX, subsamplingY) = (1, 1);
        }
        else if (primaries == 1 && transfer == 13 && matrix == 0)
        {
            (subsamplingX, subsamplingY) = (0, 0);
        }
        else if (profile == 0)
        {
            (subsamplingX, subsamplingY) = (1, 1);
        }
        else if (profile == 1)
        {
            (subsamplingX, subsamplingY) = (0, 0);
        }
        else if (bitDepth == 12)
        {
            subsamplingX = (int)sequence.Get("subsampling_x");
            subsamplingY = subsamplingX == 1 ? (int)sequence.Get("subsampling_y") : 0;
        }
        else
        {
            (subsamplingX, subsamplingY) = (1, 0);
        }

        var identity = !monochrome && matrix == 0;
        if (identity && (subsamplingX, subsamplingY) != (0, 0))
            throw new ToolException("An identity-matrix stream must be 4:4:4.");
        return new Av1ColorConfig(bitDepth, monochrome, subsamplingX, subsamplingY, identity);
    }

    /// <summary>The number of tile columns and rows (section 5.9.15 "Tile info syntax").</summary>
    private static (int Columns, int Rows) TileCounts(Av1Syntax sequence, Av1FrameTrace coded)
    {
        var frame = coded.Header;
        var (upscaledWidth, frameHeight) = coded.Size;
        var frameWidth = upscaledWidth;
        if (frame.Get("use_superres") == 1)
        {
            var denominator = (int)frame.Get("coded_denom") + 9;
            frameWidth = ((upscaledWidth * 8) + (denominator / 2)) / denominator;
        }

        var miCols = 2 * ((frameWidth + 7) >> 3);
        var miRows = 2 * ((frameHeight + 7) >> 3);
        var use128 = sequence.Get("use_128x128_superblock") == 1;
        var sbCols = use128 ? (miCols + 31) >> 5 : (miCols + 15) >> 4;
        var sbRows = use128 ? (miRows + 31) >> 5 : (miRows + 15) >> 4;
        if (frame.Get("uniform_tile_spacing_flag") == 1)
            return (UniformCount(sbCols, (int)frame.Get("tile_cols_log2")), UniformCount(sbRows, (int)frame.Get("tile_rows_log2")));

        return (frame.Array("width_in_sbs_minus_1").Count, frame.Array("height_in_sbs_minus_1").Count);

        static int UniformCount(int sizeInSuperblocks, int log2)
        {
            var tileSize = (sizeInSuperblocks + (1 << log2) - 1) >> log2;
            return (sizeInSuperblocks + tileSize - 1) / tileSize;
        }
    }

    /// <summary>The feature tags of a vector: what its sequence header and its frame headers signal. The value of a tag of a
    /// multi-frame vector lists the distinct values of its frames. A <c>show_existing_frame</c> header codes nothing but the
    /// slot it shows: the other tags are those of the coded frames, hidden ones included. The tags of the inter tools are
    /// those of the inter and switch frames, and are absent from a stream made of key frames.</summary>
    private static List<string> Features(Av1Trace trace, Av1ColorConfig color)
    {
        var sequence = trace.Sequence;
        var tags = new SortedSet<string>(StringComparer.Ordinal)
        {
            Inv($"av1.profile={sequence.Get("seq_profile")}"),
            Inv($"av1.bitDepth={color.BitDepth}"),
            "av1.subsampling=" + color.Subsampling,
            "av1.superblock=" + (sequence.Get("use_128x128_superblock") == 1 ? "128" : "64"),
        };

        // The two intra tools a sequence header can switch off are tagged when they are off
        Flag(sequence.Get("enable_filter_intra") == 0, "av1.enable_filter_intra=0");
        Flag(sequence.Get("enable_intra_edge_filter") == 0, "av1.enable_intra_edge_filter=0");
        Flag(sequence.Get("still_picture") == 1, "av1.still_picture");
        Flag(sequence.Get("reduced_still_picture_header") == 1, "av1.reduced_still_picture_header");
        Flag(color.IdentityMatrix, "av1.matrix=identity");
        Flag(sequence.Get("color_range") == 1, "av1.color_range=full");
        Flag(sequence.Get("frame_id_numbers_present_flag") == 1, "av1.frame_id_numbers_present");
        // Only coded, and only meaningful, when the sequence may have inter frames (not a reduced still picture header)
        Flag(sequence.Get("enable_dual_filter") == 1, "av1.enable_dual_filter");

        // Scalability (section 6.4.1): the operating points of the sequence header, the spatial layers of the first one
        // (the one a decoder selects by default) and the layers the stream holds. A stream without scalability has none
        // of these tags
        var operatingPoints = (int)sequence.Get("operating_points_cnt_minus_1") + 1;
        Flag(operatingPoints > 1, Inv($"av1.operating_points={operatingPoints}"));
        var layerCount = int.PopCount(SpatialLayerMask(sequence));
        if (layerCount > 1)
        {
            tags.Add(Inv($"av1.operating_point_idc=0x{sequence.Get("operating_point_idc[0]"):X3}"));
            tags.Add(Inv($"av1.spatial_layers={layerCount}"));
            var present = trace.Frames.Select(f => f.SpatialId).Distinct().Order().ToList();

            // The layers the stream holds, when it does not hold every layer of the operating point
            Flag(present.Count != layerCount, Inv($"av1.spatial_layers.present={present.Count}"));

            // The sizes of the shown frames of the layers, when a lower layer is smaller (reference scaling between layers)
            var layerSizes = trace.Frames.Where(f => f.IsShown).GroupBy(f => f.SpatialId).OrderBy(layer => layer.Key)
                .Select(layer => string.Join('/', layer.Select(f => Inv($"{f.Size.Width}x{f.Size.Height}")).Distinct(StringComparer.Ordinal))).ToList();
            Flag(layerSizes.Distinct(StringComparer.Ordinal).Count() > 1, "av1.spatial_layer_sizes=" + string.Join(',', layerSizes));
        }

        // The number of output frames: a hidden frame is not one, a show_existing_frame is; neither is the shown frame of
        // a lower spatial layer
        var outputs = trace.Frames.Where(f => f.IsOutput).ToList();
        Flag(outputs.Count > 1, Inv($"av1.frames={outputs.Count}"));
        var sizes = outputs.Select(f => f.Size).Distinct().ToList();
        Flag(sizes.Count > 1, "av1.frame_sizes=" + string.Join(',', sizes.Select(size => Inv($"{size.Width}x{size.Height}"))));

        var coded = trace.Frames.Where(f => !f.ShowsExistingFrame).ToList();
        var frames = coded.Select(f => f.Header).ToList();

        // Frame types and output order
        var frameTypes = frames.Select(f => f.Get("frame_type")).Distinct().Order().ToList();
        Flag(frameTypes is not [0], "av1.frame_types=" + string.Join(',', frameTypes.Select(type => type switch { 0 => "key", 1 => "inter", 2 => "intra_only", 3 => "switch", _ => throw new ToolException("Unexpected frame_type.") })));
        Flag(trace.Frames.Any(f => f.ShowsExistingFrame), "av1.show_existing_frame");
        Any("av1.hidden_frames", f => f.Get("show_frame", absent: 1) == 0);
        Any("av1.error_resilient_mode", f => f.Get("error_resilient_mode") == 1);
        Any("av1.found_ref", f => f.Array("found_ref").Any(e => e.Value == 1));
        Any("av1.allow_intrabc", f => f.Get("allow_intrabc") == 1);

        // Inter tools: the frames that predict from references
        var inter = frames.Where(f => f.Get("frame_type") is 1 or 3).ToList();
        if (inter.Count > 0)
        {
            // Not coded (PRIMARY_REF_NONE is inferred) with error_resilient_mode and in a switch frame
            tags.Add("av1.primary_ref_frame=" + string.Join(',', inter.Select(f => f.Get("primary_ref_frame", absent: 7) == 7 ? "none" : "set").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
            // interpolation_filter is only coded when the filter is not switchable per block
            tags.Add("av1.interpolation_filter=" + string.Join(',', inter.Select(f => f.Get("is_filter_switchable") == 1 ? 4 : f.Get("interpolation_filter")).Distinct().Order()
                .Select(filter => filter switch { 0 => "eighttap", 1 => "smooth", 2 => "sharp", 3 => "bilinear", 4 => "switchable", _ => throw new ToolException("Unexpected interpolation_filter.") })));
            InterAny("av1.reference_select", f => f.Get("reference_select") == 1);
            InterAny("av1.skip_mode_present", f => f.Get("skip_mode_present") == 1);
            InterAny("av1.use_ref_frame_mvs", f => f.Get("use_ref_frame_mvs") == 1);
            InterAny("av1.allow_warped_motion", f => f.Get("allow_warped_motion") == 1);
            InterAny("av1.is_motion_mode_switchable", f => f.Get("is_motion_mode_switchable") == 1);
            InterAny("av1.force_integer_mv", f => f.Get("force_integer_mv") == 1);

            // Global motion (section 5.9.24): the types of the references whose parameters are not the identity
            var globalMotion = inter.SelectMany(f => Enumerable.Range(1, 7).Where(reference => f.Get(Inv($"is_global[{reference}]")) == 1)
                .Select(reference => f.Get(Inv($"is_rot_zoom[{reference}]")) == 1 ? "rotzoom" : f.Get(Inv($"is_translation[{reference}]")) == 1 ? "translation" : "affine"))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            Flag(globalMotion.Count > 0, "av1.gm=" + string.Join(',', globalMotion));
        }

        Values("av1.base_q_idx", f => f.Get("base_q_idx"));
        Flag(frames.All(IsCodedLossless), "av1.lossless");
        Any("av1.using_qmatrix", f => f.Get("using_qmatrix") == 1);
        Any("av1.delta_q.y_dc", f => f.Get("delta_q_y_dc.delta_coded") == 1);
        Any("av1.delta_q.uv", f => f.Get("delta_q_u_dc.delta_coded") == 1 || f.Get("delta_q_u_ac.delta_coded") == 1 || f.Get("delta_q_v_dc.delta_coded") == 1 || f.Get("delta_q_v_ac.delta_coded") == 1);
        Any("av1.segmentation_enabled", f => f.Get("segmentation_enabled") == 1);
        var segmentationFeatures = frames.SelectMany(f => f.Array("feature_enabled")).Where(e => e.Value == 1)
            .Select(e => SegmentationFeatureRegex().Match(e.Name).Groups["feature"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Flag(segmentationFeatures.Count > 0, "av1.segmentation.features=" + string.Join(',', segmentationFeatures));
        // Only coded with a primary reference frame: without one the map is always updated, without temporal prediction
        var segmentationMaps = frames.Where(f => f.Has("segmentation_update_map")).ToList();
        Flag(segmentationMaps.Count > 0, "av1.segmentation.update_map=" + Join(segmentationMaps.Select(f => f.Get("segmentation_update_map"))));
        Any("av1.segmentation.temporal_update", f => f.Get("segmentation_temporal_update") == 1);
        Any("av1.delta_q_present", f => f.Get("delta_q_present") == 1);
        Any("av1.delta_lf_present", f => f.Get("delta_lf_present") == 1);
        Any("av1.delta_lf_multi", f => f.Get("delta_lf_multi") == 1);
        Any("av1.allow_screen_content_tools", f => f.Get("allow_screen_content_tools") == 1);
        Any("av1.disable_cdf_update", f => f.Get("disable_cdf_update") == 1);
        // Only when coded: the element is inferred to be 1 with a reduced still picture header or disable_cdf_update
        Any("av1.disable_frame_end_update_cdf", f => f.Get("disable_frame_end_update_cdf") == 1);
        Any("av1.frame_size_override_flag", f => f.Get("frame_size_override_flag") == 1);
        Any("av1.render_and_frame_size_different", f => f.Get("render_and_frame_size_different") == 1);

        var tiles = coded.Select(f => TileCounts(sequence, f)).Distinct().ToList();
        if (tiles.Any(t => t != (1, 1)))
        {
            tags.Add("av1.tiles=" + string.Join(',', tiles.Select(t => Inv($"{t.Columns}x{t.Rows}"))));
            tags.Add("av1.tiles.spacing=" + string.Join(',', frames.Select(f => f.Get("uniform_tile_spacing_flag") == 1 ? "uniform" : "explicit").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }

        var tileGroups = coded.Select(f => f.TileGroups).Distinct().Order().ToList();
        Flag(tileGroups.Any(count => count > 1), "av1.tileGroups=" + string.Join(',', tileGroups.Select(count => Inv($"{count}"))));

        Any("av1.loop_filter", f => f.Get("loop_filter_level[0]") != 0 || f.Get("loop_filter_level[1]") != 0);
        Any("av1.loop_filter_sharpness", f => f.Get("loop_filter_sharpness") != 0);
        Any("av1.loop_filter_delta_update", f => f.Get("loop_filter_delta_update") == 1);

        // CDEF is signalled whenever the sequence enables it; it only filters with a nonzero strength
        var cdefFrames = frames.Where(f => new[] { "cdef_y_pri_strength", "cdef_y_sec_strength", "cdef_uv_pri_strength", "cdef_uv_sec_strength" }.Any(name => f.Array(name).Any(e => e.Value != 0))).ToList();
        if (cdefFrames.Count > 0)
            tags.Add("av1.cdef_bits=" + Join(cdefFrames.Select(f => f.Get("cdef_bits"))));

        foreach (var (plane, name) in new[] { (0, "y"), (1, "u"), (2, "v") })
        {
            // lr_type is remapped (section 6.10.15): 1 is RESTORE_SWITCHABLE, 2 RESTORE_WIENER, 3 RESTORE_SGRPROJ
            var types = frames.Select(f => f.Get(Inv($"lr_type[{plane}]"))).Where(type => type != 0).Distinct().Order()
                .Select(type => type switch { 1 => "switchable", 2 => "wiener", 3 => "sgrproj", _ => throw new ToolException("Unexpected lr_type.") }).ToList();
            Flag(types.Count > 0, $"av1.lr.{name}=" + string.Join(',', types));
        }

        var restorationFrames = frames.Where(f => f.Has("lr_unit_shift")).ToList();
        if (restorationFrames.Count > 0)
        {
            tags.Add("av1.lr.unitSize=" + Join(restorationFrames.Select(f => 64L << (int)f.Get("lr_unit_shift"))));
            Flag(restorationFrames.Any(f => f.Get("lr_uv_shift") == 1), "av1.lr_uv_shift");
        }

        // tx_mode is coded as an increment (section 5.9.21): 1 is TX_MODE_LARGEST, 2 TX_MODE_SELECT; absent for a lossless frame
        var txModes = frames.Where(f => f.Has("tx_mode")).Select(f => f.Get("tx_mode") == 2 ? "select" : "largest").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Flag(txModes.Count > 0, "av1.tx_mode=" + string.Join(',', txModes));
        Any("av1.reduced_tx_set", f => f.Get("reduced_tx_set") == 1);

        var superresFrames = frames.Where(f => f.Get("use_superres") == 1).ToList();
        if (superresFrames.Count > 0)
            tags.Add("av1.superres.denominator=" + Join(superresFrames.Select(f => f.Get("coded_denom") + 9)));

        var grainFrames = frames.Where(f => f.Get("apply_grain") == 1).ToList();
        if (grainFrames.Count > 0)
        {
            tags.Add("av1.film_grain");
            // update_grain = 0 (inter frames only): the parameters are those of a reference frame, with another random seed
            var updated = grainFrames.Where(f => f.Get("update_grain", absent: 1) == 1).ToList();
            Flag(updated.Count < grainFrames.Count, "av1.film_grain.from_reference");
            // The numbers of scaling points of Y, Cb and Cr
            tags.Add("av1.film_grain.points=" + string.Join(',', updated.Select(f => Inv($"{f.Get("num_y_points")}/{f.Get("num_cb_points")}/{f.Get("num_cr_points")}")).Distinct(StringComparer.Ordinal)));
            tags.Add("av1.film_grain.ar_coeff_lag=" + Join(updated.Select(f => f.Get("ar_coeff_lag"))));
            Flag(updated.Any(f => f.Get("chroma_scaling_from_luma") == 1), "av1.film_grain.chroma_scaling_from_luma");
            Flag(updated.Any(f => f.Get("overlap_flag") == 1), "av1.film_grain.overlap");
            Flag(updated.Any(f => f.Get("clip_to_restricted_range") == 1), "av1.film_grain.clip_to_restricted_range");
        }

        return [.. tags];

        void Flag(bool condition, string tag)
        {
            if (condition)
                tags.Add(tag);
        }

        void Any(string tag, Func<Av1Syntax, bool> predicate) => Flag(frames.Any(predicate), tag);

        void InterAny(string tag, Func<Av1Syntax, bool> predicate) => Flag(inter.Any(predicate), tag);

        void Values(string tag, Func<Av1Syntax, long> selector) => tags.Add(tag + "=" + Join(frames.Select(selector)));

        static string Join(IEnumerable<long> values) => string.Join(',', values.Distinct().Order().Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Whether every block of the frame is lossless (<c>CodedLossless</c>, section 5.9.2). Only claimed for a frame
    /// without segmentation: a zero base quantizer index without any delta.</summary>
    private static bool IsCodedLossless(Av1Syntax frame) =>
        frame.Get("base_q_idx") == 0 && frame.Get("segmentation_enabled") == 0 && frame.Elements.Where(e => e.Name.EndsWith(".delta_coded", StringComparison.Ordinal)).All(e => e.Value == 0);

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\[trace_headers @ [^\]]+\] ")]
    private static partial Regex TracePrefixRegex();

    // "<bit position> <name> <bits> = <value>"; the bit string is empty for an inferred element
    [GeneratedRegex(@"^(?<position>\d+)\s+(?<name>\S+)\s+(?<bits>[01]*)\s*= (?<value>-?\d+)$", RegexOptions.ExplicitCapture)]
    private static partial Regex TraceElementRegex();

    [GeneratedRegex(@"^feature_enabled\[\d+\]\[(?<feature>\d+)\]$", RegexOptions.ExplicitCapture)]
    private static partial Regex SegmentationFeatureRegex();
}
