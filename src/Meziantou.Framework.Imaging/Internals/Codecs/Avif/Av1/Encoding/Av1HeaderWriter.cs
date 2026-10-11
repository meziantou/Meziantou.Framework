using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the sequence header and the uncompressed header of a frame (AV1 specification, sections 5.5 and 5.9): the mirror of
/// <see cref="Av1SequenceHeader.Parse"/> and of <see cref="Av1FrameHeaderParser"/>, in the order of their syntax tables.
/// </summary>
/// <remarks>
/// <para>
/// The encoder never builds the derived variables of a header itself: it writes the syntax elements, then parses the bits
/// it wrote with the parsers of the decoder. The model it encodes with is therefore the one a decoder derives from the
/// stream.
/// </para>
/// <para>
/// Only the syntax that the encoder produces is written: no scalability, no timing or decoder model information, no frame
/// identifier, no frame size override, no super-resolution, no segmentation, no global motion and no film grain.
/// </para>
/// </remarks>
internal static class Av1HeaderWriter
{
    private const int AllFrames = (1 << Av1Constants.NumRefFrames) - 1;

    /// <summary>Appends the payload of a sequence header OBU to <paramref name="output"/> (<c>sequence_header_obu()</c>, section 5.5.1).</summary>
    public static void WriteSequenceHeader(PooledPayloadWriter output, in Av1SequenceParameters parameters)
    {
        Debug.Assert(parameters.Width is >= 1 and <= 65536 && parameters.Height is >= 1 and <= 65536);
        var writer = new Av1BitWriter(output);
        var profile = parameters.Profile;
        var reduced = parameters.StillPicture;
        writer.WriteBits((uint)profile, 3);
        writer.WriteFlag(parameters.StillPicture); // still_picture
        writer.WriteFlag(reduced); // reduced_still_picture_header
        if (reduced)
        {
            writer.WriteBits((uint)parameters.LevelIndex, 5); // seq_level_idx[ 0 ]
        }
        else
        {
            writer.WriteFlag(false); // timing_info_present_flag
            writer.WriteFlag(false); // initial_display_delay_present_flag
            writer.WriteBits(0, 5); // operating_points_cnt_minus_1
            writer.WriteBits(0, 12); // operating_point_idc[ 0 ]
            writer.WriteBits((uint)parameters.LevelIndex, 5); // seq_level_idx[ 0 ]
            if (parameters.LevelIndex > 7)
                writer.WriteBits(0, 1); // seq_tier[ 0 ]
        }

        var widthBits = BitOperations.Log2((uint)(parameters.Width - 1) | 1) + 1;
        var heightBits = BitOperations.Log2((uint)(parameters.Height - 1) | 1) + 1;
        writer.WriteBits((uint)(widthBits - 1), 4); // frame_width_bits_minus_1
        writer.WriteBits((uint)(heightBits - 1), 4); // frame_height_bits_minus_1
        writer.WriteBits((uint)(parameters.Width - 1), widthBits); // max_frame_width_minus_1
        writer.WriteBits((uint)(parameters.Height - 1), heightBits); // max_frame_height_minus_1
        if (!reduced)
            writer.WriteFlag(false); // frame_id_numbers_present_flag

        writer.WriteFlag(parameters.Use128x128Superblock); // use_128x128_superblock
        writer.WriteFlag(true); // enable_filter_intra
        writer.WriteFlag(true); // enable_intra_edge_filter
        if (!reduced)
        {
            writer.WriteFlag(false); // enable_interintra_compound
            writer.WriteFlag(false); // enable_masked_compound
            writer.WriteFlag(false); // enable_warped_motion
            writer.WriteFlag(false); // enable_dual_filter
            writer.WriteFlag(true); // enable_order_hint
            writer.WriteFlag(false); // enable_jnt_comp
            writer.WriteFlag(true); // enable_ref_frame_mvs
            writer.WriteFlag(true); // seq_choose_screen_content_tools
            writer.WriteFlag(true); // seq_choose_integer_mv
            writer.WriteBits(Av1SequenceParameters.OrderHintBits - 1, 3); // order_hint_bits_minus_1
        }

        writer.WriteFlag(false); // enable_superres
        writer.WriteFlag(parameters.EnableCdef); // enable_cdef
        writer.WriteFlag(parameters.EnableRestoration); // enable_restoration
        WriteColorConfig(ref writer, parameters, profile);
        writer.WriteFlag(false); // film_grain_params_present
        writer.WriteTrailingBits();
    }

    // Section 5.5.2 "Color config syntax"
    private static void WriteColorConfig(ref Av1BitWriter writer, in Av1SequenceParameters parameters, int profile)
    {
        var highBitDepth = parameters.BitDepth > 8;
        writer.WriteFlag(highBitDepth); // high_bitdepth
        if (profile is 2 && highBitDepth)
            writer.WriteFlag(parameters.BitDepth is 12); // twelve_bit

        if (profile is not 1)
            writer.WriteFlag(parameters.Monochrome); // mono_chrome

        writer.WriteFlag(true); // color_description_present_flag
        writer.WriteBits((uint)parameters.ColorPrimaries, 8);
        writer.WriteBits((uint)parameters.TransferCharacteristics, 8);
        writer.WriteBits((uint)parameters.MatrixCoefficients, 8);
        if (parameters.Monochrome)
        {
            writer.WriteFlag(parameters.FullRange); // color_range
            return;
        }

        if (parameters.ColorPrimaries is Av1Constants.CpBt709 && parameters.TransferCharacteristics is Av1Constants.TcSrgb && parameters.MatrixCoefficients is Av1Constants.McIdentity)
        {
            // Full range 4:4:4 samples are implied
            Debug.Assert(parameters.FullRange && parameters.SubsamplingX is 0 && parameters.SubsamplingY is 0);
        }
        else
        {
            writer.WriteFlag(parameters.FullRange); // color_range
            if (profile is 2 && parameters.BitDepth is 12)
            {
                writer.WriteBits((uint)parameters.SubsamplingX, 1);
                if (parameters.SubsamplingX is 1)
                    writer.WriteBits((uint)parameters.SubsamplingY, 1);
            }

            if (parameters.SubsamplingX is 1 && parameters.SubsamplingY is 1)
                writer.WriteBits((uint)parameters.ChromaSamplePosition, 2); // chroma_sample_position
        }

        writer.WriteFlag(false); // separate_uv_delta_q
    }

    /// <summary>
    /// Writes <c>uncompressed_header()</c> (section 5.9.2) from the syntax elements of <paramref name="header"/>. The writer is
    /// left after the last bit of the header.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="sequence">The sequence header of the frame.</param>
    /// <param name="header">The syntax elements of the frame header; the derived variables are not read.</param>
    public static void WriteFrameHeader(ref Av1BitWriter writer, Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        Debug.Assert(!header.ShowExistingFrame && !sequence.FrameIdNumbersPresent && !sequence.DecoderModelInfoPresent && !sequence.EnableSuperres && !sequence.FilmGrainParamsPresent);
        Debug.Assert(header.FrameType is Av1Constants.KeyFrame or Av1Constants.InterFrame);
        var frameIsIntra = header.FrameType is Av1Constants.KeyFrame;
        bool errorResilientMode;
        if (sequence.ReducedStillPictureHeader)
        {
            Debug.Assert(frameIsIntra && header.ShowFrame);
            errorResilientMode = true;
        }
        else
        {
            writer.WriteFlag(false); // show_existing_frame
            writer.WriteBits((uint)header.FrameType, 2);
            writer.WriteFlag(header.ShowFrame);
            if (!header.ShowFrame)
                writer.WriteFlag(header.ShowableFrame);

            if (header.FrameType is Av1Constants.KeyFrame && header.ShowFrame)
            {
                errorResilientMode = true;
            }
            else
            {
                errorResilientMode = header.ErrorResilientMode;
                writer.WriteFlag(errorResilientMode);
            }
        }

        writer.WriteFlag(header.DisableCdfUpdate);
        if (sequence.ForceScreenContentTools is Av1Constants.SelectScreenContentTools)
            writer.WriteFlag(header.AllowScreenContentTools);

        if (header.AllowScreenContentTools && sequence.ForceIntegerMv is Av1Constants.SelectIntegerMv)
            writer.WriteFlag(header.ForceIntegerMv);

        var forceIntegerMv = frameIsIntra || (header.AllowScreenContentTools && header.ForceIntegerMv);
        if (!sequence.ReducedStillPictureHeader)
            writer.WriteFlag(false); // frame_size_override_flag

        writer.WriteBits((uint)header.OrderHint, sequence.OrderHintBits);
        if (!frameIsIntra && !errorResilientMode)
            writer.WriteBits((uint)header.PrimaryRefFrame, 3);

        if (!(header.FrameType is Av1Constants.KeyFrame && header.ShowFrame))
            writer.WriteBits((uint)header.RefreshFrameFlags, 8);

        // ref_order_hint[ i ] is only present in an error resilient frame that keeps or reads reference frames
        var refreshFrameFlags = header.FrameType is Av1Constants.KeyFrame && header.ShowFrame ? AllFrames : header.RefreshFrameFlags;
        Debug.Assert(!((!frameIsIntra || refreshFrameFlags is not AllFrames) && errorResilientMode && sequence.EnableOrderHint));

        if (frameIsIntra)
        {
            // frame_size( ) has no syntax element without size override and super-resolution
            writer.WriteFlag(false); // render_and_frame_size_different
            if (header.AllowScreenContentTools)
                writer.WriteFlag(header.AllowIntrabc);
        }
        else
        {
            if (sequence.EnableOrderHint)
                writer.WriteFlag(false); // frame_refs_short_signaling

            for (var i = 0; i < Av1Constants.RefsPerFrame; i++)
            {
                writer.WriteBits((uint)header.RefFrameIdx[i], 3);
            }

            writer.WriteFlag(false); // render_and_frame_size_different
            if (!forceIntegerMv)
                writer.WriteFlag(header.AllowHighPrecisionMv);

            // read_interpolation_filter( )
            writer.WriteFlag(header.InterpolationFilter is Av1Constants.Switchable); // is_filter_switchable
            if (header.InterpolationFilter is not Av1Constants.Switchable)
                writer.WriteBits((uint)header.InterpolationFilter, 2);

            writer.WriteFlag(header.IsMotionModeSwitchable);
            if (!errorResilientMode && sequence.EnableRefFrameMvs)
                writer.WriteFlag(header.UseRefFrameMvs);
        }

        if (!sequence.ReducedStillPictureHeader && !header.DisableCdfUpdate)
            writer.WriteFlag(header.DisableFrameEndUpdateCdf);

        WriteTileInfo(ref writer, sequence, header);
        WriteQuantizationParams(ref writer, sequence, header);
        Debug.Assert(!header.SegmentationEnabled);
        writer.WriteFlag(false); // segmentation_enabled

        // delta_q_params( ) and delta_lf_params( )
        if (header.BaseQIdx > 0)
        {
            writer.WriteFlag(header.DeltaQPresent);
            if (header.DeltaQPresent)
            {
                writer.WriteBits((uint)header.DeltaQRes, 2);
                Debug.Assert(!header.DeltaLfPresent);
                if (!header.AllowIntrabc)
                    writer.WriteFlag(false); // delta_lf_present
            }
        }

        var codedLossless = header.BaseQIdx is 0 && header.DeltaQYDc is 0 && header.DeltaQUDc is 0 && header.DeltaQUAc is 0 && header.DeltaQVDc is 0 && header.DeltaQVAc is 0;
        WriteLoopFilterParams(ref writer, sequence, header, codedLossless);
        WriteCdefParams(ref writer, sequence, header, codedLossless);
        WriteLoopRestorationParams(ref writer, sequence, header, codedLossless);

        // read_tx_mode( )
        if (!codedLossless)
            writer.WriteFlag(header.TxMode is Av1Constants.TxModeSelect); // tx_mode_select

        if (!frameIsIntra)
        {
            // Without reference_select no skip mode is allowed, and skip_mode_present is absent
            Debug.Assert(!header.ReferenceSelect && !header.SkipModePresent);
            writer.WriteFlag(false); // reference_select
            if (!errorResilientMode && sequence.EnableWarpedMotion)
                writer.WriteFlag(header.AllowWarpedMotion);
        }

        writer.WriteFlag(header.ReducedTxSet);
        if (!frameIsIntra)
        {
            // global_motion_params( ): every reference has the identity model
            for (var reference = Av1Constants.LastFrame; reference <= Av1Constants.AltrefFrame; reference++)
            {
                writer.WriteFlag(false); // is_global
            }
        }
    }

    // Section 5.9.15 "Tile info syntax", with uniform tile spacing
    private static void WriteTileInfo(ref Av1BitWriter writer, Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        var miCols = 2 * ((sequence.MaxFrameWidth + 7) >> 3);
        var miRows = 2 * ((sequence.MaxFrameHeight + 7) >> 3);
        var limits = Av1FrameHeaderParser.ComputeTileLimits(sequence.Use128x128Superblock, miCols, miRows);
        Debug.Assert(header.TileColsLog2 >= limits.MinLog2TileCols && header.TileColsLog2 <= limits.MaxLog2TileCols);
        writer.WriteFlag(true); // uniform_tile_spacing_flag
        for (var i = limits.MinLog2TileCols; i < header.TileColsLog2; i++)
        {
            writer.WriteFlag(true); // increment_tile_cols_log2
        }

        if (header.TileColsLog2 < limits.MaxLog2TileCols)
            writer.WriteFlag(false);

        var minLog2TileRows = Math.Max(limits.MinLog2Tiles - header.TileColsLog2, 0);
        Debug.Assert(header.TileRowsLog2 >= minLog2TileRows && header.TileRowsLog2 <= limits.MaxLog2TileRows);
        for (var i = minLog2TileRows; i < header.TileRowsLog2; i++)
        {
            writer.WriteFlag(true); // increment_tile_rows_log2
        }

        if (header.TileRowsLog2 < limits.MaxLog2TileRows)
            writer.WriteFlag(false);

        if (header.TileColsLog2 > 0 || header.TileRowsLog2 > 0)
        {
            writer.WriteBits((uint)header.ContextUpdateTileId, header.TileRowsLog2 + header.TileColsLog2);
            writer.WriteBits((uint)(header.TileSizeBytes - 1), 2); // tile_size_bytes_minus_1
        }
    }

    // Section 5.9.12 "Quantization params syntax"
    private static void WriteQuantizationParams(ref Av1BitWriter writer, Av1SequenceHeader sequence, Av1FrameHeader header)
    {
        writer.WriteBits((uint)header.BaseQIdx, 8);
        WriteDeltaQ(ref writer, header.DeltaQYDc);
        if (sequence.PlaneCount > 1)
        {
            // diff_uv_delta is absent without separate_uv_delta_q: the V deltas are the U deltas
            Debug.Assert(!sequence.SeparateUvDeltaQ && header.DeltaQVDc == header.DeltaQUDc && header.DeltaQVAc == header.DeltaQUAc);
            WriteDeltaQ(ref writer, header.DeltaQUDc);
            WriteDeltaQ(ref writer, header.DeltaQUAc);
        }

        writer.WriteFlag(header.UsingQmatrix);
        if (header.UsingQmatrix)
        {
            writer.WriteBits((uint)header.QmY, 4);
            writer.WriteBits((uint)header.QmU, 4);
            Debug.Assert(header.QmV == header.QmU);
        }
    }

    private static void WriteDeltaQ(ref Av1BitWriter writer, int value)
    {
        writer.WriteFlag(value is not 0); // delta_coded
        if (value is not 0)
            writer.WriteSigned(value, 7);
    }

    // Section 5.9.11 "Loop filter params syntax"
    private static void WriteLoopFilterParams(ref Av1BitWriter writer, Av1SequenceHeader sequence, Av1FrameHeader header, bool codedLossless)
    {
        if (codedLossless || header.AllowIntrabc)
            return;

        writer.WriteBits(header.LoopFilterLevel[0], 6);
        writer.WriteBits(header.LoopFilterLevel[1], 6);
        if (sequence.PlaneCount > 1 && (header.LoopFilterLevel[0] is not 0 || header.LoopFilterLevel[1] is not 0))
        {
            writer.WriteBits(header.LoopFilterLevel[2], 6);
            writer.WriteBits(header.LoopFilterLevel[3], 6);
        }

        writer.WriteBits((uint)header.LoopFilterSharpness, 3);
        writer.WriteFlag(header.LoopFilterDeltaEnabled);
        if (header.LoopFilterDeltaEnabled)
        {
            // The deltas keep the values of setup_past_independence or of the primary reference frame
            Debug.Assert(!header.LoopFilterDeltaUpdate);
            writer.WriteFlag(false); // loop_filter_delta_update
        }
    }

    // Section 5.9.19 "CDEF params syntax"
    private static void WriteCdefParams(ref Av1BitWriter writer, Av1SequenceHeader sequence, Av1FrameHeader header, bool codedLossless)
    {
        if (codedLossless || header.AllowIntrabc || !sequence.EnableCdef)
            return;

        writer.WriteBits((uint)(header.CdefDamping - 3), 2); // cdef_damping_minus_3
        writer.WriteBits((uint)header.CdefBits, 2);
        for (var i = 0; i < (1 << header.CdefBits); i++)
        {
            writer.WriteBits(header.CdefYPriStrength[i], 4);
            writer.WriteBits(header.CdefYSecStrength[i] is 4 ? 3u : header.CdefYSecStrength[i], 2);
            if (sequence.PlaneCount > 1)
            {
                writer.WriteBits(header.CdefUvPriStrength[i], 4);
                writer.WriteBits(header.CdefUvSecStrength[i] is 4 ? 3u : header.CdefUvSecStrength[i], 2);
            }
        }
    }

    // Section 5.9.20 "Loop restoration params syntax"
    private static void WriteLoopRestorationParams(ref Av1BitWriter writer, Av1SequenceHeader sequence, Av1FrameHeader header, bool codedLossless)
    {
        // AllLossless is CodedLossless without super-resolution
        if (codedLossless || header.AllowIntrabc || !sequence.EnableRestoration)
            return;

        var usesLr = false;
        var usesChromaLr = false;
        for (var i = 0; i < sequence.PlaneCount; i++)
        {
            // The inverse of Remap_Lr_Type
            var type = header.FrameRestorationType[i];
            writer.WriteBits(type switch
            {
                Av1Constants.RestoreNone => 0u,
                Av1Constants.RestoreSwitchable => 1u,
                Av1Constants.RestoreWiener => 2u,
                _ => 3u,
            }, 2);

            if (type is not Av1Constants.RestoreNone)
            {
                usesLr = true;
                usesChromaLr |= i > 0;
            }
        }

        if (!usesLr)
            return;

        // LoopRestorationSize[ 0 ] is RESTORATION_TILESIZE_MAX >> (2 - unitShift)
        var unitShift = 2 - BitOperations.Log2((uint)(Av1Constants.RestorationTilesizeMax / header.LoopRestorationSize[0]));
        if (sequence.Use128x128Superblock)
        {
            Debug.Assert(unitShift >= 1);
            writer.WriteBits((uint)(unitShift - 1), 1); // lr_unit_shift
        }
        else
        {
            writer.WriteBits(unitShift > 0 ? 1u : 0u, 1); // lr_unit_shift
            if (unitShift > 0)
                writer.WriteBits((uint)(unitShift - 1), 1); // lr_unit_extra_shift
        }

        if (sequence.SubsamplingX is 1 && sequence.SubsamplingY is 1 && usesChromaLr)
            writer.WriteBits(header.LoopRestorationSize[1] < header.LoopRestorationSize[0] ? 1u : 0u, 1); // lr_uv_shift
    }
}
