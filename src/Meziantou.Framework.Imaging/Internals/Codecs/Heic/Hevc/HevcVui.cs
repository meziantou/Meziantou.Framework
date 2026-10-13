namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The video usability information of a sequence parameter set (Rec. ITU-T H.265, clause E.2.1 "VUI parameters syntax"):
/// the description of the color of the samples and of their display, which does not take part in the decoding of the
/// samples.
/// </summary>
internal sealed class HevcVui
{
    /// <summary>The value of <c>aspect_ratio_idc</c> that is followed by <c>sar_width</c> and <c>sar_height</c> (Table E.1).</summary>
    public const int ExtendedSar = 255;

    private HevcVui()
    {
    }

    /// <summary>Gets <c>aspect_ratio_idc</c>: 0 (unspecified) when it is not present.</summary>
    public int AspectRatioIdc { get; private init; }

    /// <summary>Gets <c>sar_width</c>, present when <see cref="AspectRatioIdc"/> is <see cref="ExtendedSar"/>.</summary>
    public int SarWidth { get; private init; }

    /// <summary>Gets <c>sar_height</c>, present when <see cref="AspectRatioIdc"/> is <see cref="ExtendedSar"/>.</summary>
    public int SarHeight { get; private init; }

    /// <summary>Gets <c>video_signal_type_present_flag</c>.</summary>
    public bool VideoSignalTypePresentFlag { get; private init; }

    /// <summary>Gets <c>video_format</c>: 5 (unspecified) when it is not present.</summary>
    public int VideoFormat { get; private init; } = 5;

    /// <summary>Gets <c>video_full_range_flag</c>: <see langword="false"/> when it is not present.</summary>
    public bool VideoFullRangeFlag { get; private init; }

    /// <summary>Gets <c>colour_description_present_flag</c>.</summary>
    public bool ColourDescriptionPresentFlag { get; private init; }

    /// <summary>Gets <c>colour_primaries</c> (Rec. ITU-T H.273): 2 (unspecified) when it is not present.</summary>
    public int ColourPrimaries { get; private init; } = 2;

    /// <summary>Gets <c>transfer_characteristics</c> (Rec. ITU-T H.273): 2 (unspecified) when it is not present.</summary>
    public int TransferCharacteristics { get; private init; } = 2;

    /// <summary>Gets <c>matrix_coeffs</c> (Rec. ITU-T H.273): 2 (unspecified) when it is not present.</summary>
    public int MatrixCoeffs { get; private init; } = 2;

    /// <summary>Gets <c>chroma_loc_info_present_flag</c>.</summary>
    public bool ChromaLocInfoPresentFlag { get; private init; }

    /// <summary>Gets <c>chroma_sample_loc_type_top_field</c>, the location of the chroma samples of a frame: 0 when it is not present.</summary>
    public int ChromaSampleLocTypeTopField { get; private init; }

    /// <summary>Gets <c>chroma_sample_loc_type_bottom_field</c>: 0 when it is not present.</summary>
    public int ChromaSampleLocTypeBottomField { get; private init; }

    /// <summary>Gets <c>neutral_chroma_indication_flag</c>.</summary>
    public bool NeutralChromaIndicationFlag { get; private init; }

    /// <summary>Gets <c>field_seq_flag</c>: the pictures of the sequence are fields.</summary>
    public bool FieldSeqFlag { get; private init; }

    /// <summary>Gets <c>frame_field_info_present_flag</c>.</summary>
    public bool FrameFieldInfoPresentFlag { get; private init; }

    /// <summary>Gets <c>default_display_window_flag</c>.</summary>
    public bool DefaultDisplayWindowFlag { get; private init; }

    /// <summary>Gets <c>def_disp_win_left_offset</c>, in chroma sample units.</summary>
    public int DefDispWinLeftOffset { get; private init; }

    /// <summary>Gets <c>def_disp_win_right_offset</c>, in chroma sample units.</summary>
    public int DefDispWinRightOffset { get; private init; }

    /// <summary>Gets <c>def_disp_win_top_offset</c>, in chroma sample units.</summary>
    public int DefDispWinTopOffset { get; private init; }

    /// <summary>Gets <c>def_disp_win_bottom_offset</c>, in chroma sample units.</summary>
    public int DefDispWinBottomOffset { get; private init; }

    /// <summary>Gets <c>vui_timing_info_present_flag</c>.</summary>
    public bool TimingInfoPresentFlag { get; private init; }

    /// <summary>Gets <c>vui_num_units_in_tick</c>.</summary>
    public uint NumUnitsInTick { get; private init; }

    /// <summary>Gets <c>vui_time_scale</c>.</summary>
    public uint TimeScale { get; private init; }

    /// <summary>Reads the syntax structure <c>vui_parameters( )</c>.</summary>
    /// <param name="reader">The reader, at the first bit of the structure.</param>
    /// <param name="maxSubLayersMinus1"><c>sps_max_sub_layers_minus1</c>.</param>
    public static HevcVui Parse(ref HevcBitReader reader, int maxSubLayersMinus1)
    {
        var aspectRatioIdc = 0;
        var sarWidth = 0;
        var sarHeight = 0;
        if (reader.ReadFlag())
        {
            aspectRatioIdc = (int)reader.ReadBits(8);
            if (aspectRatioIdc is ExtendedSar)
            {
                sarWidth = (int)reader.ReadBits(16);
                sarHeight = (int)reader.ReadBits(16);
            }
        }

        // overscan_info_present_flag, overscan_appropriate_flag
        if (reader.ReadFlag())
        {
            reader.Skip(1);
        }

        var videoSignalTypePresentFlag = reader.ReadFlag();
        var videoFormat = 5;
        var videoFullRangeFlag = false;
        var colourDescriptionPresentFlag = false;
        var colourPrimaries = 2;
        var transferCharacteristics = 2;
        var matrixCoeffs = 2;
        if (videoSignalTypePresentFlag)
        {
            videoFormat = (int)reader.ReadBits(3);
            videoFullRangeFlag = reader.ReadFlag();
            colourDescriptionPresentFlag = reader.ReadFlag();
            if (colourDescriptionPresentFlag)
            {
                colourPrimaries = (int)reader.ReadBits(8);
                transferCharacteristics = (int)reader.ReadBits(8);
                matrixCoeffs = (int)reader.ReadBits(8);
            }
        }

        var chromaLocInfoPresentFlag = reader.ReadFlag();
        var chromaSampleLocTypeTopField = 0;
        var chromaSampleLocTypeBottomField = 0;
        if (chromaLocInfoPresentFlag)
        {
            chromaSampleLocTypeTopField = reader.ReadUE(5, "chroma_sample_loc_type_top_field");
            chromaSampleLocTypeBottomField = reader.ReadUE(5, "chroma_sample_loc_type_bottom_field");
        }

        var neutralChromaIndicationFlag = reader.ReadFlag();
        var fieldSeqFlag = reader.ReadFlag();
        var frameFieldInfoPresentFlag = reader.ReadFlag();
        var defaultDisplayWindowFlag = reader.ReadFlag();
        var left = 0;
        var right = 0;
        var top = 0;
        var bottom = 0;
        if (defaultDisplayWindowFlag)
        {
            left = reader.ReadUE(ushort.MaxValue, "def_disp_win_left_offset");
            right = reader.ReadUE(ushort.MaxValue, "def_disp_win_right_offset");
            top = reader.ReadUE(ushort.MaxValue, "def_disp_win_top_offset");
            bottom = reader.ReadUE(ushort.MaxValue, "def_disp_win_bottom_offset");
        }

        var timingInfoPresentFlag = reader.ReadFlag();
        uint numUnitsInTick = 0;
        uint timeScale = 0;
        if (timingInfoPresentFlag)
        {
            numUnitsInTick = reader.ReadBits(32);
            timeScale = reader.ReadBits(32);

            // vui_poc_proportional_to_timing_flag, vui_num_ticks_poc_diff_one_minus1
            if (reader.ReadFlag())
            {
                reader.ReadUE();
            }

            // vui_hrd_parameters_present_flag
            if (reader.ReadFlag())
            {
                HevcHrdParameters.Skip(ref reader, commonInfPresentFlag: true, maxSubLayersMinus1);
            }
        }

        // bitstream_restriction_flag
        if (reader.ReadFlag())
        {
            // tiles_fixed_structure_flag, motion_vectors_over_pic_boundaries_flag, restricted_ref_pic_lists_flag
            reader.Skip(3);
            reader.ReadUE(4095, "min_spatial_segmentation_idc");
            reader.ReadUE(16, "max_bytes_per_pic_denom");
            reader.ReadUE(16, "max_bits_per_min_cu_denom");
            reader.ReadUE(15, "log2_max_mv_length_horizontal");
            reader.ReadUE(15, "log2_max_mv_length_vertical");
        }

        return new HevcVui
        {
            AspectRatioIdc = aspectRatioIdc,
            SarWidth = sarWidth,
            SarHeight = sarHeight,
            VideoSignalTypePresentFlag = videoSignalTypePresentFlag,
            VideoFormat = videoFormat,
            VideoFullRangeFlag = videoFullRangeFlag,
            ColourDescriptionPresentFlag = colourDescriptionPresentFlag,
            ColourPrimaries = colourPrimaries,
            TransferCharacteristics = transferCharacteristics,
            MatrixCoeffs = matrixCoeffs,
            ChromaLocInfoPresentFlag = chromaLocInfoPresentFlag,
            ChromaSampleLocTypeTopField = chromaSampleLocTypeTopField,
            ChromaSampleLocTypeBottomField = chromaSampleLocTypeBottomField,
            NeutralChromaIndicationFlag = neutralChromaIndicationFlag,
            FieldSeqFlag = fieldSeqFlag,
            FrameFieldInfoPresentFlag = frameFieldInfoPresentFlag,
            DefaultDisplayWindowFlag = defaultDisplayWindowFlag,
            DefDispWinLeftOffset = left,
            DefDispWinRightOffset = right,
            DefDispWinTopOffset = top,
            DefDispWinBottomOffset = bottom,
            TimingInfoPresentFlag = timingInfoPresentFlag,
            NumUnitsInTick = numUnitsInTick,
            TimeScale = timeScale,
        };
    }
}
