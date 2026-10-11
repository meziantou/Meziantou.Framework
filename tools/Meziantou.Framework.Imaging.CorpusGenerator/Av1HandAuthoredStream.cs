using System.Numerics;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// Hand-authored AV1 inter frames, for the coding tools that no encoder of the pinned tools produces: an interpolation
// filter per direction (enable_dual_filter), a frame-level BILINEAR filter, segment ids predicted from the previous
// frame (seg_id_predicted), and the reference frame, skip and global motion features of a segment.
//
// A stream is the key frame of a libaom encode followed by frames written here, bit by bit, from the AV1 Bitstream &
// Decoding Process Specification: the uncompressed header (section 5.9), the tile group (5.11) and the symbols of the
// blocks, coded by an arithmetic encoder that mirrors the symbol decoder of section 8.2. The frames are kept to what can
// be written without a mode decision or a context model:
// - the frame is split in tiles of one 64x64 superblock, each coded as one block (PARTITION_NONE). A block of another
//   tile is never available, so every context is the one of a block without neighbors, the motion vector stack is empty
//   and the predicted vector is the global one, which is zero (no global motion is coded);
// - disable_cdf_update = 1: the symbols are coded with the default CDFs of section 10 from the first to the last (a frame
//   only takes its primary reference among the frames written here, whose saved CDFs are those defaults);
// - every block is skipped (no coefficient): an inter block copies its reference at a coded sub-sample vector through
//   the coded filters, an intra block is a DC prediction.
// The pictures mean nothing, but every syntax element that the tools above add is coded, and its value shows in the
// pixels: a vector with a fraction in both directions and a different filter for each, references that are different
// pictures. Like any vector, a stream is only recorded with the plane digests that dav1d and libaom agree on.
internal static class Av1HandAuthoredStream
{
    // Reference frames (section 6.10.24)
    public const int IntraFrame = 0;
    public const int Last = 1;
    public const int Last2 = 2;
    public const int Last3 = 3;
    public const int Golden = 4;
    public const int BackwardReference = 5;
    public const int AlternateReference2 = 6;
    public const int AlternateReference = 7;

    // Interpolation filters (section 6.8.9)
    public const int Regular = 0;
    public const int Smooth = 1;
    public const int Sharp = 2;
    public const int Bilinear = 3;

    // Segmentation features (section 6.8.13): the three that change the syntax of a block
    public const int ReferenceFeature = 5;
    public const int SkipFeature = 6;
    public const int GlobalMotionFeature = 7;

    private const int PrimaryReferenceNone = 7;

    private static readonly int[] FeatureBits = [8, 6, 6, 6, 6, 3, 0, 0];
    private static readonly bool[] FeatureSigned = [true, true, true, true, true, false, false, false];

    // The default CDFs of section 10 ("Default CDF tables") that the blocks use, without their adaptation counter. Every
    // context is the one of a block whose above and left neighbors are not available.
    private static readonly ushort[] PartitionW64Cdf = [20137, 21547, 23078, 29566, 29837, 30261, 30524, 30892, 31724, 32768]; // Default_Partition_W64_Cdf[ 0 ]
    private static readonly ushort[] SegmentIdCdf = [5622, 7893, 16093, 18233, 27809, 28373, 32533, 32768]; // Default_Segment_Id_Cdf[ 0 ]
    private static readonly ushort[] SegmentIdPredictedCdf = [16384, 32768]; // Default_Segment_Id_Predicted_Cdf[ 0 ]
    private static readonly ushort[] SkipCdf = [31671, 32768]; // Default_Skip_Cdf[ 0 ]
    private static readonly ushort[] IsInterCdf = [806, 32768]; // Default_Is_Inter_Cdf[ 0 ]
    private static readonly ushort[][] SingleRefCdf = // Default_Single_Ref_Cdf[ 1 ][ 0..5 ]: no neighbor, so every count is equal
    [
        [16973, 32768], [16751, 32768], [19647, 32768], [24773, 32768], [11014, 32768], [15087, 32768],
    ];

    private static readonly ushort[] NewMvCdf = [24035, 32768]; // Default_New_Mv_Cdf[ 0 ]
    private static readonly ushort[] MvJointCdf = [4096, 11264, 19328, 32768]; // Default_Mv_Joint_Cdf
    private static readonly ushort[] MvSignCdf = [16384, 32768]; // Default_Mv_Sign_Cdf
    private static readonly ushort[] MvClassCdf = [28672, 30976, 31858, 32320, 32551, 32656, 32740, 32757, 32762, 32767, 32768]; // Default_Mv_Class_Cdf, the same for both components
    private static readonly ushort[] MvClass0BitCdf = [27648, 32768]; // Default_Mv_Class0_Bit_Cdf
    private static readonly ushort[][] MvClass0FrCdf = [[16384, 24576, 26624, 32768], [12288, 21248, 24128, 32768]]; // Default_Mv_Class0_Fr_Cdf[ comp ][ mv_class0_bit ], the same for both components
    private static readonly ushort[] MvClass0HpCdf = [20480, 32768]; // Default_Mv_Class0_Hp_Cdf
    private static readonly ushort[][] MvBitCdf = // Default_Mv_Bit_Cdf[ 0..9 ]
    [
        [17408, 32768], [17920, 32768], [18944, 32768], [20480, 32768], [22528, 32768],
        [24576, 32768], [28672, 32768], [29952, 32768], [29952, 32768], [30720, 32768],
    ];

    private static readonly ushort[] MvFrCdf = [8192, 17408, 21248, 32768]; // Default_Mv_Fr_Cdf, the same for both components
    private static readonly ushort[] MvHpCdf = [16384, 32768]; // Default_Mv_Hp_Cdf
    private static readonly ushort[][] InterpFilterCdf = [[28244, 32608, 32768], [27403, 32636, 32768]]; // Default_Interp_Filter_Cdf[ 3 ] (dir 0) and [ 11 ] (dir 1)
    private static readonly ushort[] YModeCdf = [20155, 21301, 22838, 23178, 23261, 23533, 23703, 24804, 25352, 26575, 27016, 28049, 32768]; // Default_Y_Mode_Cdf[ Size_Group[ BLOCK_64X64 ] = 3 ]
    private static readonly ushort[] UvModeCdf = [22631, 24152, 25378, 25661, 25986, 26520, 27055, 27923, 28244, 30059, 30941, 31961, 32768]; // Default_Uv_Mode_Cfl_Not_Allowed_Cdf[ DC_PRED ]

    /// <summary>The 64x64 block of a tile.</summary>
    internal sealed record Block
    {
        /// <summary>
        /// The motion vector in eighths of a sample, coded as a NEWMV difference from a zero prediction;
        /// <see langword="null"/> when the segment leaves none to code (intra reference, skip or global motion feature).
        /// </summary>
        public (int Row, int Column)? Vector { get; init; }

        /// <summary>The coded reference frame; <see langword="null"/> when a feature of the segment gives it.</summary>
        public int? Reference { get; init; }

        /// <summary>The coded filters when the frame filter is switchable; without dual filters, both are the same.</summary>
        public (int Vertical, int Horizontal)? Filters { get; init; }

        /// <summary>
        /// The coded segment id; <see langword="null"/> for the id of the previous map (<c>seg_id_predicted = 1</c>, or a
        /// frame that does not update its map) and in a frame without segmentation.
        /// </summary>
        public int? SegmentId { get; init; }
    }

    /// <param name="UpdateMap"><c>segmentation_update_map</c>.</param>
    /// <param name="TemporalUpdate"><c>segmentation_temporal_update</c>.</param>
    internal sealed record Segmentation(bool UpdateMap, bool TemporalUpdate)
    {
        /// <summary>
        /// The features to code (<c>segmentation_update_data = 1</c>), a value per segment and feature;
        /// <see langword="null"/> to keep those of the primary reference frame.
        /// </summary>
        public (int Segment, int Feature, int Value)[]? Features { get; init; }
    }

    /// <param name="RefreshSlot">The slot the frame is stored in.</param>
    /// <param name="ReferenceSlots">The slots of LAST, LAST2, LAST3, GOLDEN, BWDREF, ALTREF2 and ALTREF; missing ones are slot 0.</param>
    /// <param name="Blocks">The block of every tile, in raster order.</param>
    internal sealed record Frame(int RefreshSlot, int[] ReferenceSlots, Block[] Blocks)
    {
        /// <summary>The index in <see cref="ReferenceSlots"/> of the primary reference frame, which must be a frame written here; <see langword="null"/> for none.</summary>
        public int? PrimaryReference { get; init; }

        /// <summary><c>allow_high_precision_mv</c>: without it, the vectors are even (quarters of a sample).</summary>
        public bool HighPrecisionVectors { get; init; } = true;

        /// <summary>The frame-level filter, or <see langword="null"/> for a filter coded by every block (<c>is_filter_switchable = 1</c>).</summary>
        public int? Filter { get; init; }

        public Segmentation? Segmentation { get; init; }
    }

    /// <param name="Name">The name of the recipe.</param>
    /// <param name="DualFilter">Sets <c>enable_dual_filter</c> in the sequence header of the key frame (an inter-only flag that libaom leaves unset).</param>
    /// <param name="Description">What the frames code, for the provenance of the vector.</param>
    /// <param name="Frames">The frames written after the key frame.</param>
    internal sealed record Recipe(string Name, bool DualFilter, string Description, Frame[] Frames);

    /// <summary>
    /// Dual filters: the six pairs of different filters and the three pairs of equal ones, vectors with a fraction in one or
    /// both directions, of every magnitude class up to 5, with and without the high precision bit; the last frame
    /// codes the BILINEAR filter for the whole frame.
    /// </summary>
    public static Recipe DualFilters { get; } = new(
        "dual-filters",
        DualFilter: true,
        "4 inter frames of 4 tiles, one skipped 64x64 NEWMV block per tile: interp_filter coded for each direction (every pair of EIGHTTAP, EIGHTTAP_SMOOTH and EIGHTTAP_SHARP), sub-sample vectors of the classes 0 to 5 with and without allow_high_precision_mv, references LAST to GOLDEN; the last frame codes interpolation_filter = BILINEAR",
        [
            new(RefreshSlot: 1, [], [Inter(Last, 19, -13, Regular, Smooth), Inter(Last, -5, 27, Smooth, Sharp), Inter(Last, 11, 3, Sharp, Regular), Inter(Last, -21, -9, Sharp, Smooth)]),
            new(RefreshSlot: 2, [1], [Inter(Last, 6, -10, Smooth, Regular), Inter(Last2, -14, 2, Regular, Sharp), Inter(Last, 34, 70, Sharp, Sharp), Inter(Last2, -130, 18, Smooth, Smooth)]) { HighPrecisionVectors = false },
            new(RefreshSlot: 3, [2, 1], [Inter(Last, 0, 5, Regular, Sharp), Inter(Last2, 7, 0, Sharp, Regular), Inter(Last3, -3, 300, Smooth, Sharp), Inter(Last, -77, -41, Regular, Regular)]),
            new(RefreshSlot: 4, [3, 2, 1], [Inter(Last, 19, -13), Inter(Last2, -5, 27), Inter(Last3, 11, 3), Inter(Golden, -21, -9)]) { Filter = Bilinear },
        ]);

    /// <summary>
    /// Segmentation: a coded map whose segments give the reference frame, a map predicted from the previous frame
    /// (<c>seg_id_predicted</c> 1 and 0), a map that is not updated while the features change, and the intra, skip and
    /// global motion features. The references of the segments are different pictures, so a wrong id shows in the pixels.
    /// </summary>
    public static Recipe SegmentFeatures { get; } = new(
        "segment-features",
        DualFilter: false,
        "8 inter frames of 4 tiles, one skipped 64x64 block per tile: 2 frames without segmentation that make different reference pictures, a coded segment map with the reference frame feature, a map predicted from the previous frame (seg_id_predicted = 1 and 0), a map that is not updated with new feature data, then the intra reference, skip and global motion features and their prediction",
        [
            new(RefreshSlot: 1, [], [Inter(Last, 19, -13), Inter(Last, -5, 27), Inter(Last, 11, 3), Inter(Last, -21, -9)]) { Filter = Regular },
            new(RefreshSlot: 2, [1], [Inter(Last, -9, 9), Inter(Last2, 40, 12), Inter(Last, 3, -30), Inter(Last2, -6, -6)]) { Filter = Sharp },
            new(RefreshSlot: 3, [2, 1, 0, 2], [Segment(1, 5, 5), Segment(2, -12, 4), Segment(0, 8, -8) with { Reference = Last }, Segment(3, 2, 14)])
            {
                Filter = Regular,
                Segmentation = new(UpdateMap: true, TemporalUpdate: false) { Features = [(1, ReferenceFeature, Last2), (2, ReferenceFeature, Last3), (3, ReferenceFeature, Golden)] },
            },
            new(RefreshSlot: 4, [3, 1, 0, 2], [Predicted(-7, 3), Segment(3, 9, 9), Predicted(4, -20) with { Reference = Last2 }, Predicted(-10, -2)])
            {
                PrimaryReference = 0,
                Filter = Smooth,
                Segmentation = new(UpdateMap: true, TemporalUpdate: true),
            },
            new(RefreshSlot: 5, [4, 1, 0, 2, 3], [Predicted(6, 6), Predicted(-4, 12), Predicted(14, -2) with { Reference = BackwardReference }, Predicted(-8, -8)])
            {
                PrimaryReference = 0,
                Filter = Regular,
                Segmentation = new(UpdateMap: false, TemporalUpdate: false) { Features = [(1, ReferenceFeature, Golden), (3, ReferenceFeature, Last2)] },
            },
            new(RefreshSlot: 6, [5, 1, 0, 2, 3], [Segment(2, 3, 3), Predicted(-2, 6) with { Reference = AlternateReference }, Predicted(10, -4) with { Reference = AlternateReference2 }, Segment(1, -6, -10)])
            {
                PrimaryReference = 0,
                Filter = Sharp,
                Segmentation = new(UpdateMap: true, TemporalUpdate: true) { Features = [(1, ReferenceFeature, Last), (2, ReferenceFeature, Last2), (4, ReferenceFeature, Last3)] },
            },
            new(RefreshSlot: 7, [6, 1, 0, 2, 3], [new() { SegmentId = 1 }, new() { SegmentId = 2 }, new() { SegmentId = 3 }, new() { SegmentId = 4 }])
            {
                PrimaryReference = 0,
                Filter = Regular,
                Segmentation = new(UpdateMap: true, TemporalUpdate: false)
                {
                    Features = [(1, ReferenceFeature, IntraFrame), (2, SkipFeature, 0), (3, GlobalMotionFeature, 0), (4, ReferenceFeature, Last2), (4, GlobalMotionFeature, 0)],
                },
            },
            new(RefreshSlot: 0, [7, 6, 0, 2, 3], [new(), Segment(0, 5, -3) with { Reference = Last2, Filters = (Smooth, Smooth) }, new() { SegmentId = 4 }, new()])
            {
                PrimaryReference = 0,
                Segmentation = new(UpdateMap: true, TemporalUpdate: true),
            },
        ]);

    public static IReadOnlyList<Recipe> Recipes { get; } = [DualFilters, SegmentFeatures];

    private static Block Inter(int reference, int row, int column) => new() { Reference = reference, Vector = (row, column) };

    private static Block Inter(int reference, int row, int column, int vertical, int horizontal) => new() { Reference = reference, Vector = (row, column), Filters = (vertical, horizontal) };

    /// <summary>A block with a coded segment id, whose reference comes from the segment unless one is added.</summary>
    private static Block Segment(int segmentId, int row, int column) => new() { SegmentId = segmentId, Vector = (row, column) };

    /// <summary>A block that takes its segment id from the previous map.</summary>
    private static Block Predicted(int row, int column) => new() { Vector = (row, column) };

    /// <summary>
    /// Appends the frames of <paramref name="recipe"/> to a stream that holds a sequence header and one shown key frame,
    /// which fills the eight reference slots.
    /// </summary>
    public static byte[] Build(ReadOnlySpan<byte> keyFrameStream, Recipe recipe)
    {
        var output = new List<byte>();
        Sequence? sequence = null;
        var frames = 0;
        var position = 0;
        while (position < keyFrameStream.Length)
        {
            var header = keyFrameStream[position++];
            if ((header & 0x86) != 0x02)
                throw new ToolException("the key frame stream has an OBU with an extension or without a size field.");

            var size = 0;
            for (var shift = 0; ; shift += 7)
            {
                var value = keyFrameStream[position++];
                size |= (value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                    break;
            }

            var type = (header >> 3) & 0xF;
            var payload = keyFrameStream.Slice(position, size).ToArray();
            position += size;
            if (type == 1)
            {
                sequence = Sequence.Parse(payload);
                if (recipe.DualFilter)
                {
                    if (sequence.DualFilter)
                        throw new ToolException("the key frame stream already sets enable_dual_filter.");

                    payload[sequence.DualFilterBit >> 3] |= (byte)(0x80 >> (sequence.DualFilterBit & 7));
                    sequence = sequence with { DualFilter = true };
                }
            }
            else if (type is 3 or 4 or 6)
            {
                frames++;
            }

            WriteObu(output, type, payload);
        }

        if (sequence is null || frames != 1)
            throw new ToolException("the key frame stream must hold a sequence header and one frame OBU.");

        // What the reference slots hold: the segment id of every tile and the segmentation features (none for the key frame,
        // which libaom codes without segmentation), and whether the frame was written here (default CDFs)
        var tileCount = sequence.TileColumns * sequence.TileRows;
        var slots = new SlotState[8];
        Array.Fill(slots, new SlotState(new int[tileCount], [], HandAuthored: false));
        for (var index = 0; index < recipe.Frames.Length; index++)
        {
            var frame = recipe.Frames[index];
            if (frame.Blocks.Length != tileCount)
                throw new ToolException(Invariant($"{recipe.Name}: frame {index + 1} has {frame.Blocks.Length} blocks for {tileCount} tiles."));

            var references = new int[7];
            frame.ReferenceSlots.CopyTo(references, 0);
            SlotState? previous = null;
            if (frame.PrimaryReference is { } primary)
            {
                previous = slots[references[primary]];
                if (!previous.HandAuthored)
                    throw new ToolException(Invariant($"{recipe.Name}: the primary reference of frame {index + 1} is the key frame, whose CDFs are not the default ones."));
            }

            // Section 7.20 and 7.21: the features are coded, or loaded from the primary reference frame; without
            // segmentation they are reset. The previous map is only loaded from the primary reference frame
            Dictionary<(int Segment, int Feature), int> features = [];
            if (frame.Segmentation is { } segmentation)
            {
                if (segmentation.Features is not null)
                {
                    foreach (var (segment, feature, value) in segmentation.Features)
                        features.Add((segment, feature), value);
                }
                else if (previous is not null)
                {
                    features = previous.Features;
                }
                else
                {
                    throw new ToolException(Invariant($"{recipe.Name}: frame {index + 1} has no primary reference frame to take its segmentation features from."));
                }

                if (previous is null && (!segmentation.UpdateMap || segmentation.TemporalUpdate))
                    throw new ToolException(Invariant($"{recipe.Name}: frame {index + 1} has no primary reference frame to take its segment map from."));
            }

            var previousIds = previous?.SegmentIds ?? new int[tileCount];
            var segmentIds = new int[tileCount];
            var header = new BitWriter();
            WriteFrameHeader(header, sequence, frame, orderHint: index + 1, references, features);
            header.Align();
            var tiles = new byte[tileCount][];
            for (var tile = 0; tile < tileCount; tile++)
            {
                try
                {
                    tiles[tile] = EncodeTile(sequence, frame, frame.Blocks[tile], features, previousIds[tile], out segmentIds[tile]);
                }
                catch (ToolException ex)
                {
                    throw new ToolException(Invariant($"{recipe.Name}: frame {index + 1}, tile {tile}: {ex.Message}"));
                }
            }

            // Section 5.11.1: the tile group header (no start and end), then every tile but the last one with its size
            if (tileCount > 1)
            {
                header.Write(1, 0);
                header.Align();
            }

            var payload = new List<byte>(header.ToArray());
            for (var tile = 0; tile < tileCount; tile++)
            {
                if (tile != tileCount - 1)
                {
                    var tileSize = tiles[tile].Length - 1;
                    payload.AddRange([(byte)tileSize, (byte)(tileSize >> 8), (byte)(tileSize >> 16), (byte)(tileSize >> 24)]);
                }

                payload.AddRange(tiles[tile]);
            }

            WriteObu(output, 2, []);
            WriteObu(output, 6, [.. payload]);
            slots[frame.RefreshSlot] = new SlotState(segmentIds, features, HandAuthored: true);
        }

        return [.. output];
    }

    /// <summary>The uncompressed header of a shown inter frame (section 5.9.2), with every tool that is not needed left off.</summary>
    private static void WriteFrameHeader(BitWriter writer, Sequence sequence, Frame frame, int orderHint, int[] references, Dictionary<(int Segment, int Feature), int> features)
    {
        writer.Write(1, 0); // show_existing_frame
        writer.Write(2, 1); // frame_type = INTER_FRAME
        writer.Write(1, 1); // show_frame
        writer.Write(1, 0); // error_resilient_mode
        writer.Write(1, 1); // disable_cdf_update
        if (sequence.SelectScreenContentTools)
            writer.Write(1, 0); // allow_screen_content_tools

        writer.Write(1, 0); // frame_size_override_flag
        writer.Write(sequence.OrderHintBits, orderHint);
        writer.Write(3, frame.PrimaryReference ?? PrimaryReferenceNone);
        writer.Write(8, 1 << frame.RefreshSlot); // refresh_frame_flags
        if (sequence.OrderHintBits > 0)
            writer.Write(1, 0); // frame_refs_short_signaling

        foreach (var slot in references)
            writer.Write(3, slot); // ref_frame_idx

        if (sequence.SuperResolution)
            writer.Write(1, 0); // use_superres

        writer.Write(1, 0); // render_and_frame_size_different
        writer.Write(1, frame.HighPrecisionVectors ? 1 : 0); // allow_high_precision_mv
        if (frame.Filter is { } filter)
        {
            writer.Write(1, 0); // is_filter_switchable
            writer.Write(2, filter); // interpolation_filter
        }
        else
        {
            writer.Write(1, 1);
        }

        writer.Write(1, 0); // is_motion_mode_switchable
        if (sequence.ReferenceFrameMotionVectors)
            writer.Write(1, 0); // use_ref_frame_mvs

        // disable_frame_end_update_cdf is inferred from disable_cdf_update

        // tile_info: uniform spacing, one superblock per tile
        writer.Write(1, 1); // uniform_tile_spacing_flag
        var tileBits = 0;
        foreach (var count in (ReadOnlySpan<int>)[sequence.TileColumns, sequence.TileRows])
        {
            for (var log2 = 0; (1 << log2) < count; log2++)
            {
                writer.Write(1, 1); // increment_tile_cols_log2, increment_tile_rows_log2
                tileBits++;
            }
        }

        if (tileBits > 0)
        {
            writer.Write(tileBits, 0); // context_update_tile_id
            writer.Write(2, 3); // tile_size_bytes_minus_1
        }

        // quantization_params: no coefficient is coded; the index only has to be non-zero (not lossless)
        writer.Write(8, 100); // base_q_idx
        writer.Write(1, 0); // DeltaQYDc
        if (!sequence.Monochrome)
        {
            if (sequence.SeparateUvDeltaQ)
                writer.Write(1, 0); // diff_uv_delta

            writer.Write(1, 0); // DeltaQUDc
            writer.Write(1, 0); // DeltaQUAc
        }

        writer.Write(1, 0); // using_qmatrix

        // segmentation_params
        if (frame.Segmentation is { } segmentation)
        {
            writer.Write(1, 1); // segmentation_enabled
            if (frame.PrimaryReference is not null)
            {
                writer.Write(1, segmentation.UpdateMap ? 1 : 0);
                if (segmentation.UpdateMap)
                    writer.Write(1, segmentation.TemporalUpdate ? 1 : 0);

                writer.Write(1, segmentation.Features is null ? 0 : 1); // segmentation_update_data
            }

            if (segmentation.Features is not null)
            {
                for (var segment = 0; segment < 8; segment++)
                {
                    for (var feature = 0; feature < 8; feature++)
                    {
                        if (!features.TryGetValue((segment, feature), out var value))
                        {
                            writer.Write(1, 0); // feature_enabled
                            continue;
                        }

                        writer.Write(1, 1);
                        if (FeatureSigned[feature])
                            writer.Write(1 + FeatureBits[feature], value & ((1 << (1 + FeatureBits[feature])) - 1));
                        else
                            writer.Write(FeatureBits[feature], value);
                    }
                }
            }
        }
        else
        {
            writer.Write(1, 0);
        }

        writer.Write(1, 0); // delta_q_present (base_q_idx > 0)

        // loop_filter_params: off
        writer.Write(6, 0); // loop_filter_level[ 0 ]
        writer.Write(6, 0); // loop_filter_level[ 1 ]
        writer.Write(3, 0); // loop_filter_sharpness
        writer.Write(1, 0); // loop_filter_delta_enabled
        if (sequence.Cdef)
        {
            // cdef_params: one set of zero strengths (a skipped block is not filtered anyway)
            writer.Write(2, 0); // cdef_damping_minus_3
            writer.Write(2, 0); // cdef_bits
            writer.Write(6, 0); // cdef_y_pri_strength, cdef_y_sec_strength
            if (!sequence.Monochrome)
                writer.Write(6, 0); // cdef_uv_pri_strength, cdef_uv_sec_strength
        }

        if (sequence.Restoration)
        {
            for (var plane = 0; plane < (sequence.Monochrome ? 1 : 3); plane++)
                writer.Write(2, 0); // lr_type = RESTORE_NONE
        }

        writer.Write(1, 0); // tx_mode_select
        writer.Write(1, 0); // reference_select
        if (sequence.WarpedMotion)
            writer.Write(1, 0); // allow_warped_motion

        writer.Write(1, 0); // reduced_tx_set
        for (var reference = Last; reference <= AlternateReference; reference++)
            writer.Write(1, 0); // is_global

        if (sequence.FilmGrain)
            writer.Write(1, 0); // apply_grain
    }

    /// <summary>The symbols of the block of a tile (sections 5.11.4 to 5.11.26), in the order the decoder reads them.</summary>
    private static byte[] EncodeTile(Sequence sequence, Frame frame, Block block, Dictionary<(int Segment, int Feature), int> features, int previousSegmentId, out int segmentId)
    {
        var encoder = new SymbolEncoder();
        encoder.Encode(0, PartitionW64Cdf); // partition = PARTITION_NONE

        // inter_segment_id( 1 )
        segmentId = 0;
        if (frame.Segmentation is { } segmentation)
        {
            // The segment id of a skipped block is only coded before the skip flag, which takes a feature that changes the
            // syntax (SegIdPreSkip)
            if (!features.Keys.Any(key => key.Feature >= ReferenceFeature))
                throw new ToolException("the segmentation has no reference frame, skip or global motion feature.");

            var predicted = block.SegmentId is null;
            if (!segmentation.UpdateMap)
            {
                if (!predicted)
                    throw new ToolException("a segment id cannot be coded in a frame that does not update its map.");
            }
            else if (segmentation.TemporalUpdate)
            {
                encoder.Encode(predicted ? 1 : 0, SegmentIdPredictedCdf); // seg_id_predicted
            }
            else if (predicted)
            {
                throw new ToolException("the segment id must be coded: the map is not predicted.");
            }

            if (predicted)
            {
                segmentId = previousSegmentId;
            }
            else
            {
                // segment_id: without neighbors the prediction is 0, so the coded value is the id (neg_deinterleave)
                segmentId = block.SegmentId.GetValueOrDefault();
                if (segmentId > features.Keys.Max(key => key.Segment))
                    throw new ToolException("the segment id is above the last active segment.");

                encoder.Encode(segmentId, SegmentIdCdf);
            }
        }
        else if (block.SegmentId is not null)
        {
            throw new ToolException("the frame has no segmentation.");
        }

        int? referenceFeature = features.TryGetValue((segmentId, ReferenceFeature), out var featureValue) ? featureValue : null;
        var skipFeature = features.ContainsKey((segmentId, SkipFeature));
        var globalMotionFeature = features.ContainsKey((segmentId, GlobalMotionFeature));

        if (!skipFeature)
            encoder.Encode(1, SkipCdf); // skip

        if (referenceFeature == IntraFrame)
        {
            // intra_block_mode_info: DC prediction; a 64x64 block has no palette, no filter intra and no CfL
            ThrowIfCoded(block, vector: true, reference: true, filters: true);
            encoder.Encode(0, YModeCdf); // y_mode = DC_PRED
            if (!sequence.Monochrome)
                encoder.Encode(0, UvModeCdf); // uv_mode = DC_PRED

            return encoder.Finish();
        }

        if (referenceFeature is null && !globalMotionFeature)
            encoder.Encode(1, IsInterCdf); // is_inter

        if (skipFeature || globalMotionFeature)
        {
            // The reference is the feature's or LAST, the mode is GLOBALMV and, with an identity model, no filter is coded
            ThrowIfCoded(block, vector: true, reference: true, filters: true);
            return encoder.Finish();
        }

        if (referenceFeature is null)
        {
            // read_ref_frames: a single reference
            var reference = block.Reference ?? throw new ToolException("the reference frame must be coded.");
            if (reference >= BackwardReference)
            {
                encoder.Encode(1, SingleRefCdf[0]); // single_ref_p1
                encoder.Encode(reference == AlternateReference ? 1 : 0, SingleRefCdf[1]); // single_ref_p2
                if (reference != AlternateReference)
                    encoder.Encode(reference == AlternateReference2 ? 1 : 0, SingleRefCdf[5]); // single_ref_p6
            }
            else
            {
                encoder.Encode(0, SingleRefCdf[0]);
                encoder.Encode(reference >= Last3 ? 1 : 0, SingleRefCdf[2]); // single_ref_p3
                if (reference >= Last3)
                    encoder.Encode(reference == Golden ? 1 : 0, SingleRefCdf[4]); // single_ref_p5
                else
                    encoder.Encode(reference == Last2 ? 1 : 0, SingleRefCdf[3]); // single_ref_p4
            }
        }
        else
        {
            ThrowIfCoded(block, vector: false, reference: true, filters: false);
        }

        // NEWMV: the motion vector stack is empty, so no drl_mode is coded and the prediction is the zero global vector
        if (block.Vector is not (var row, var column))
            throw new ToolException("the motion vector must be coded.");

        if (row == 0 && column == 0)
            throw new ToolException("a zero vector is not a NEWMV difference worth coding.");

        encoder.Encode(0, NewMvCdf); // new_mv
        encoder.Encode((row != 0 ? 2 : 0) | (column != 0 ? 1 : 0), MvJointCdf); // mv_joint
        if (row != 0)
            EncodeVectorComponent(encoder, row, frame.HighPrecisionVectors);

        if (column != 0)
            EncodeVectorComponent(encoder, column, frame.HighPrecisionVectors);

        // A 64x64 block has no inter-intra mode; the motion mode is not switchable and the block is not compound
        if (frame.Filter is null)
        {
            if (block.Filters is not (var vertical, var horizontal))
                throw new ToolException("the filters must be coded.");

            encoder.Encode(vertical, InterpFilterCdf[0]); // interp_filter[ 0 ]
            if (sequence.DualFilter)
                encoder.Encode(horizontal, InterpFilterCdf[1]); // interp_filter[ 1 ]
            else if (horizontal != vertical)
                throw new ToolException("without enable_dual_filter both filters are the same.");
        }
        else
        {
            ThrowIfCoded(block, vector: false, reference: false, filters: true);
        }

        return encoder.Finish();
    }

    private static void ThrowIfCoded(Block block, bool vector, bool reference, bool filters)
    {
        if ((vector && block.Vector is not null) || (reference && block.Reference is not null) || (filters && block.Filters is not null))
            throw new ToolException("the block describes an element that its segment or its frame does not code.");
    }

    /// <summary>read_mv_component (section 5.11.32), reversed.</summary>
    private static void EncodeVectorComponent(SymbolEncoder encoder, int value, bool highPrecision)
    {
        // mag - 1 is the class base plus ( integer bits << 3 ) | ( mv_fr << 1 ) | mv_hp
        var magnitude = Math.Abs(value) - 1;
        if (!highPrecision && (magnitude & 1) == 0)
            throw new ToolException("without allow_high_precision_mv the vectors are even.");

        var mvClass = magnitude < 16 ? 0 : BitOperations.Log2((uint)(magnitude >> 3));
        var offset = magnitude - (mvClass == 0 ? 0 : 2 << (mvClass + 2));
        var integer = offset >> 3;
        var fraction = (offset >> 1) & 3;
        encoder.Encode(value < 0 ? 1 : 0, MvSignCdf); // mv_sign
        encoder.Encode(mvClass, MvClassCdf); // mv_class
        if (mvClass == 0)
        {
            encoder.Encode(integer, MvClass0BitCdf); // mv_class0_bit
            encoder.Encode(fraction, MvClass0FrCdf[integer]); // mv_class0_fr
            if (highPrecision)
                encoder.Encode(offset & 1, MvClass0HpCdf); // mv_class0_hp
        }
        else
        {
            for (var bit = 0; bit < mvClass; bit++)
                encoder.Encode((integer >> bit) & 1, MvBitCdf[bit]); // mv_bit

            encoder.Encode(fraction, MvFrCdf); // mv_fr
            if (highPrecision)
                encoder.Encode(offset & 1, MvHpCdf); // mv_hp
        }
    }

    private static void WriteObu(List<byte> output, int type, byte[] payload)
    {
        output.Add((byte)((type << 3) | 2)); // obu_has_size_field
        var size = payload.Length;
        do
        {
            var value = size & 0x7F;
            size >>= 7;
            output.Add((byte)(size != 0 ? value | 0x80 : value));
        }
        while (size != 0);

        output.AddRange(payload);
    }

    private sealed record SlotState(int[] SegmentIds, Dictionary<(int Segment, int Feature), int> Features, bool HandAuthored);

    /// <summary>What the frame headers depend on in the sequence header of the key frame (section 5.5).</summary>
    private sealed record Sequence
    {
        public required int TileColumns { get; init; }

        public required int TileRows { get; init; }

        public required int DualFilterBit { get; init; }

        public required bool DualFilter { get; init; }

        public required bool WarpedMotion { get; init; }

        public required bool ReferenceFrameMotionVectors { get; init; }

        public required bool SelectScreenContentTools { get; init; }

        public required int OrderHintBits { get; init; }

        public required bool SuperResolution { get; init; }

        public required bool Cdef { get; init; }

        public required bool Restoration { get; init; }

        public required bool Monochrome { get; init; }

        public required bool SeparateUvDeltaQ { get; init; }

        public required bool FilmGrain { get; init; }

        public static Sequence Parse(byte[] payload)
        {
            var position = 0;
            var profile = Read(3);
            Read(1); // still_picture
            if (Read(1) != 0)
                throw new ToolException("the key frame stream has a reduced still picture header: it cannot be followed by inter frames.");

            if (Read(1) != 0)
                throw new ToolException("the key frame stream has timing information.");

            var displayDelay = Read(1) != 0;
            var operatingPoints = Read(5) + 1;
            for (var i = 0; i < operatingPoints; i++)
            {
                Read(12); // operating_point_idc
                if (Read(5) > 7)
                    Read(1); // seq_tier

                if (displayDelay && Read(1) != 0)
                    Read(4);
            }

            var widthBits = Read(4) + 1;
            var heightBits = Read(4) + 1;
            var width = Read(widthBits) + 1;
            var height = Read(heightBits) + 1;
            if (Read(1) != 0)
                throw new ToolException("the key frame stream has frame ids.");

            if (Read(1) != 0)
                throw new ToolException("the key frame stream has 128x128 superblocks: the frames written here are made of 64x64 blocks.");

            if (width % 64 != 0 || height % 64 != 0 || !BitOperations.IsPow2(width / 64) || !BitOperations.IsPow2(height / 64))
                throw new ToolException("the size of the key frame must be a power-of-two number of 64x64 superblocks in each direction.");

            Read(2); // enable_filter_intra, enable_intra_edge_filter
            Read(2); // enable_interintra_compound (a 64x64 block has no inter-intra mode), enable_masked_compound
            var warpedMotion = Read(1) != 0;
            var dualFilterBit = position;
            var dualFilter = Read(1) != 0;
            var orderHint = Read(1) != 0;
            var referenceFrameMotionVectors = false;
            if (orderHint)
            {
                Read(1); // enable_jnt_comp
                referenceFrameMotionVectors = Read(1) != 0;
            }

            // seq_force_screen_content_tools: SELECT (2) when seq_choose_screen_content_tools is set
            var screenContentTools = Read(1) != 0 ? 2 : Read(1);
            if (screenContentTools == 1)
                throw new ToolException("the key frame stream forces the screen content tools.");

            if (screenContentTools > 0 && Read(1) == 0)
                Read(1); // seq_force_integer_mv

            var orderHintBits = orderHint ? Read(3) + 1 : 0;
            var superResolution = Read(1) != 0;
            var cdef = Read(1) != 0;
            var restoration = Read(1) != 0;

            // color_config
            var highBitDepth = Read(1) != 0;
            var twelveBit = profile == 2 && highBitDepth && Read(1) != 0;
            var monochrome = profile != 1 && Read(1) != 0;
            var (primaries, transfer, matrix) = (2, 2, 2);
            if (Read(1) != 0)
                (primaries, transfer, matrix) = (Read(8), Read(8), Read(8));

            var separateUvDeltaQ = false;
            if (monochrome)
            {
                Read(1); // color_range
            }
            else if (primaries == 1 && transfer == 13 && matrix == 0)
            {
                separateUvDeltaQ = Read(1) != 0;
            }
            else
            {
                Read(1); // color_range
                var (subsamplingX, subsamplingY) = profile switch
                {
                    0 => (true, true),
                    1 => (false, false),
                    _ when twelveBit => ReadSubsampling(),
                    _ => (true, false),
                };
                if (subsamplingX && subsamplingY)
                    Read(2); // chroma_sample_position

                separateUvDeltaQ = Read(1) != 0;
            }

            var filmGrain = Read(1) != 0;
            if (Read(1) != 1)
                throw new ToolException("the sequence header of the key frame stream does not end where expected.");

            return new Sequence
            {
                TileColumns = width / 64,
                TileRows = height / 64,
                DualFilterBit = dualFilterBit,
                DualFilter = dualFilter,
                WarpedMotion = warpedMotion,
                ReferenceFrameMotionVectors = referenceFrameMotionVectors,
                SelectScreenContentTools = screenContentTools == 2,
                OrderHintBits = orderHintBits,
                SuperResolution = superResolution,
                Cdef = cdef,
                Restoration = restoration,
                Monochrome = monochrome,
                SeparateUvDeltaQ = separateUvDeltaQ,
                FilmGrain = filmGrain,
            };

            int Read(int count)
            {
                var value = 0;
                for (var i = 0; i < count; i++, position++)
                    value = (value << 1) | ((payload[position >> 3] >> (7 - (position & 7))) & 1);

                return value;
            }

            (bool X, bool Y) ReadSubsampling()
            {
                var x = Read(1) != 0;
                return (x, x && Read(1) != 0);
            }
        }
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bitCount;

        public void Write(int count, int value)
        {
            if (count < 0 || (count < 31 && (uint)value >= 1u << count))
                throw new ToolException(Invariant($"the value {value} does not fit in {count} bits."));

            for (var bit = count - 1; bit >= 0; bit--)
            {
                if ((_bitCount & 7) == 0)
                    _bytes.Add(0);

                if (((value >> bit) & 1) != 0)
                    _bytes[^1] |= (byte)(0x80 >> (_bitCount & 7));

                _bitCount++;
            }
        }

        public void Align() => _bitCount = (_bitCount + 7) & ~7;

        public byte[] ToArray() => [.. _bytes];
    }

    /// <summary>
    /// The inverse of the symbol decoder of section 8.2. The decoder keeps the distance from the coded value to the top
    /// of the interval it narrows with every symbol; the encoder keeps the interval itself, with all its bits: the low end
    /// (an integer that grows by the bits of every renormalization) and the 16-bit range. The CDFs are never adapted
    /// (<c>disable_cdf_update = 1</c>).
    /// </summary>
    private sealed class SymbolEncoder
    {
        private BigInteger _low = BigInteger.Zero;
        private int _range = 1 << 15;
        private int _shift;

        public void Encode(int symbol, ushort[] cdf)
        {
            if ((uint)symbol >= (uint)cdf.Length)
                throw new ToolException(Invariant($"the symbol {symbol} is not one of the {cdf.Length} values of its CDF."));

            // Section 8.2.6: the symbol is the first one whose "cur" is not above the decoder value; in the domain of the
            // coded bits, it owns [ range - prev, range - cur ) above the low end of the interval
            var previous = symbol == 0 ? _range : Current(symbol - 1);
            var current = Current(symbol);
            _low += _range - previous;
            _range = previous - current;
            var bits = 15 - BitOperations.Log2((uint)_range);
            _range <<= bits;
            _low <<= bits;
            _shift += bits;

            int Current(int index) => ((_range >> 8) * (((1 << 15) - cdf[index]) >> 6) >> 1) + (4 * (cdf.Length - index - 1));
        }

        /// <summary>
        /// Ends the tile with the bits the exit process requires (section 8.2.4): the 15 bits the decoder holds are a one
        /// followed by zeros, and so is the padding to the next byte. The range is at least 1 &lt;&lt; 15 after a
        /// renormalization, so one value of the interval ends with these 15 bits.
        /// </summary>
        public byte[] Finish()
        {
            var value = _low + ((0x4000 - (int)(_low & 0x7FFF)) & 0x7FFF);
            if (value >= _low + _range)
                throw new ToolException("the symbol encoder cannot end its interval.");

            var bitCount = 15 + _shift;
            var result = new byte[(bitCount + 7) / 8];
            value <<= (result.Length * 8) - bitCount;
            for (var index = result.Length - 1; index >= 0; index--, value >>= 8)
                result[index] = (byte)(value & 0xFF);

            return result;
        }
    }

    private static string Invariant(FormattableString value) => FormattableString.Invariant(value);
}
