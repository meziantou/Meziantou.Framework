namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A picture parameter set (Rec. ITU-T H.265, clauses 7.3.2.3 "Picture parameter set RBSP syntax" and 7.4.3.3 "Picture
/// parameter set RBSP semantics"). The names are those of the specification. A picture parameter set is read without its
/// sequence parameter set: the constraints that depend on it are checked when a picture activates both
/// (<see cref="HevcPictureLayout"/>).
/// </summary>
internal sealed class HevcPictureParameterSet
{
    private int[] _columnWidthMinus1 = [];
    private int[] _rowHeightMinus1 = [];
    private sbyte[] _cbQpOffsetList = [];
    private sbyte[] _crQpOffsetList = [];

    private HevcPictureParameterSet()
    {
    }

    public int PicParameterSetId { get; private set; }

    public int SeqParameterSetId { get; private set; }

    public bool DependentSliceSegmentsEnabledFlag { get; private set; }

    public bool OutputFlagPresentFlag { get; private set; }

    public int NumExtraSliceHeaderBits { get; private set; }

    public bool SignDataHidingEnabledFlag { get; private set; }

    public bool CabacInitPresentFlag { get; private set; }

    public int NumRefIdxL0DefaultActiveMinus1 { get; private set; }

    public int NumRefIdxL1DefaultActiveMinus1 { get; private set; }

    public int InitQpMinus26 { get; private set; }

    public bool ConstrainedIntraPredFlag { get; private set; }

    public bool TransformSkipEnabledFlag { get; private set; }

    public bool CuQpDeltaEnabledFlag { get; private set; }

    public int DiffCuQpDeltaDepth { get; private set; }

    public int CbQpOffset { get; private set; }

    public int CrQpOffset { get; private set; }

    public bool SliceChromaQpOffsetsPresentFlag { get; private set; }

    public bool WeightedPredFlag { get; private set; }

    public bool WeightedBipredFlag { get; private set; }

    public bool TransquantBypassEnabledFlag { get; private set; }

    public bool TilesEnabledFlag { get; private set; }

    public bool EntropyCodingSyncEnabledFlag { get; private set; }

    /// <summary>Gets <c>num_tile_columns_minus1 + 1</c>: 1 when the picture has no tiles.</summary>
    public int NumTileColumns { get; private set; } = 1;

    /// <summary>Gets <c>num_tile_rows_minus1 + 1</c>: 1 when the picture has no tiles.</summary>
    public int NumTileRows { get; private set; } = 1;

    public bool UniformSpacingFlag { get; private set; } = true;

    /// <summary>Gets <c>column_width_minus1[ i ]</c>: <c>num_tile_columns_minus1</c> values when the spacing is not uniform.</summary>
    public ReadOnlySpan<int> ColumnWidthMinus1 => _columnWidthMinus1;

    /// <summary>Gets <c>row_height_minus1[ i ]</c>: <c>num_tile_rows_minus1</c> values when the spacing is not uniform.</summary>
    public ReadOnlySpan<int> RowHeightMinus1 => _rowHeightMinus1;

    public bool LoopFilterAcrossTilesEnabledFlag { get; private set; } = true;

    public bool LoopFilterAcrossSlicesEnabledFlag { get; private set; }

    public bool DeblockingFilterControlPresentFlag { get; private set; }

    public bool DeblockingFilterOverrideEnabledFlag { get; private set; }

    public bool DeblockingFilterDisabledFlag { get; private set; }

    public int BetaOffsetDiv2 { get; private set; }

    public int TcOffsetDiv2 { get; private set; }

    /// <summary>Gets the scaling lists of the parameter set, or <see langword="null"/> when <c>pps_scaling_list_data_present_flag</c> is 0.</summary>
    public HevcScalingList? ScalingList { get; private set; }

    public bool ListsModificationPresentFlag { get; private set; }

    /// <summary>Gets <c>Log2ParMrgLevel</c> (Equation 7-37).</summary>
    public int Log2ParMrgLevel { get; private set; }

    public bool SliceSegmentHeaderExtensionPresentFlag { get; private set; }

    // pps_range_extension( )

    /// <summary>Gets <c>Log2MaxTransformSkipSize</c> (Equation 7-38).</summary>
    public int Log2MaxTransformSkipSize { get; private set; } = 2;

    public bool CrossComponentPredictionEnabledFlag { get; private set; }

    public bool ChromaQpOffsetListEnabledFlag { get; private set; }

    public int DiffCuChromaQpOffsetDepth { get; private set; }

    public int ChromaQpOffsetListLenMinus1 { get; private set; }

    /// <summary>Gets <c>cb_qp_offset_list[ i ]</c>.</summary>
    public ReadOnlySpan<sbyte> CbQpOffsetList => _cbQpOffsetList;

    /// <summary>Gets <c>cr_qp_offset_list[ i ]</c>.</summary>
    public ReadOnlySpan<sbyte> CrQpOffsetList => _crQpOffsetList;

    public int Log2SaoOffsetScaleLuma { get; private set; }

    public int Log2SaoOffsetScaleChroma { get; private set; }

    /// <summary>Reads a picture parameter set from the payload of its NAL unit, without emulation prevention bytes.</summary>
    /// <exception cref="InvalidImageContentException">The payload is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The parameter set uses a feature that the decoder does not support.</exception>
    public static HevcPictureParameterSet ParseRbsp(ReadOnlySpan<byte> rbsp)
    {
        var reader = new HevcBitReader(rbsp);
        var pps = new HevcPictureParameterSet();
        pps.PicParameterSetId = reader.ReadUE(HevcConstants.MaxPpsCount - 1, "pps_pic_parameter_set_id");
        pps.SeqParameterSetId = reader.ReadUE(HevcConstants.MaxSpsCount - 1, "pps_seq_parameter_set_id");
        pps.DependentSliceSegmentsEnabledFlag = reader.ReadFlag();
        pps.OutputFlagPresentFlag = reader.ReadFlag();
        pps.NumExtraSliceHeaderBits = (int)reader.ReadBits(3);
        pps.SignDataHidingEnabledFlag = reader.ReadFlag();
        pps.CabacInitPresentFlag = reader.ReadFlag();
        pps.NumRefIdxL0DefaultActiveMinus1 = reader.ReadUE(14, "num_ref_idx_l0_default_active_minus1");
        pps.NumRefIdxL1DefaultActiveMinus1 = reader.ReadUE(14, "num_ref_idx_l1_default_active_minus1");

        // The lower bound depends on the bit depth of the sequence: -( 26 + QpBdOffsetY )
        pps.InitQpMinus26 = reader.ReadSE(-(26 + 48), 25, "init_qp_minus26");
        pps.ConstrainedIntraPredFlag = reader.ReadFlag();
        pps.TransformSkipEnabledFlag = reader.ReadFlag();
        pps.CuQpDeltaEnabledFlag = reader.ReadFlag();
        if (pps.CuQpDeltaEnabledFlag)
        {
            pps.DiffCuQpDeltaDepth = reader.ReadUE(3, "diff_cu_qp_delta_depth");
        }

        pps.CbQpOffset = reader.ReadSE(-12, 12, "pps_cb_qp_offset");
        pps.CrQpOffset = reader.ReadSE(-12, 12, "pps_cr_qp_offset");
        pps.SliceChromaQpOffsetsPresentFlag = reader.ReadFlag();
        pps.WeightedPredFlag = reader.ReadFlag();
        pps.WeightedBipredFlag = reader.ReadFlag();
        pps.TransquantBypassEnabledFlag = reader.ReadFlag();
        pps.TilesEnabledFlag = reader.ReadFlag();
        pps.EntropyCodingSyncEnabledFlag = reader.ReadFlag();
        if (pps.TilesEnabledFlag)
        {
            // The range of the syntax is the number of coding tree blocks of the picture; the levels of Annex A limit a
            // picture to 20 tile columns and 22 tile rows
            var numTileColumnsMinus1 = reader.ReadUE();
            var numTileRowsMinus1 = reader.ReadUE();
            if (numTileColumnsMinus1 > HevcSequenceParameterSet.MaxPictureDimension || numTileRowsMinus1 > HevcSequenceParameterSet.MaxPictureDimension)
                throw HevcErrors.Invalid("the number of tiles is out of range.");

            if (numTileColumnsMinus1 >= HevcConstants.MaxTileColumns || numTileRowsMinus1 >= HevcConstants.MaxTileRows)
                throw HevcErrors.Unsupported("more than 20 tile columns or 22 tile rows");

            pps.NumTileColumns = (int)numTileColumnsMinus1 + 1;
            pps.NumTileRows = (int)numTileRowsMinus1 + 1;
            pps.UniformSpacingFlag = reader.ReadFlag();
            if (!pps.UniformSpacingFlag)
            {
                pps._columnWidthMinus1 = new int[pps.NumTileColumns - 1];
                for (var i = 0; i < pps._columnWidthMinus1.Length; i++)
                {
                    pps._columnWidthMinus1[i] = reader.ReadUE(HevcSequenceParameterSet.MaxPictureDimension, "column_width_minus1");
                }

                pps._rowHeightMinus1 = new int[pps.NumTileRows - 1];
                for (var i = 0; i < pps._rowHeightMinus1.Length; i++)
                {
                    pps._rowHeightMinus1[i] = reader.ReadUE(HevcSequenceParameterSet.MaxPictureDimension, "row_height_minus1");
                }
            }

            pps.LoopFilterAcrossTilesEnabledFlag = reader.ReadFlag();
        }

        pps.LoopFilterAcrossSlicesEnabledFlag = reader.ReadFlag();
        pps.DeblockingFilterControlPresentFlag = reader.ReadFlag();
        if (pps.DeblockingFilterControlPresentFlag)
        {
            pps.DeblockingFilterOverrideEnabledFlag = reader.ReadFlag();
            pps.DeblockingFilterDisabledFlag = reader.ReadFlag();
            if (!pps.DeblockingFilterDisabledFlag)
            {
                pps.BetaOffsetDiv2 = reader.ReadSE(-6, 6, "pps_beta_offset_div2");
                pps.TcOffsetDiv2 = reader.ReadSE(-6, 6, "pps_tc_offset_div2");
            }
        }

        if (reader.ReadFlag())
        {
            pps.ScalingList = HevcScalingList.Parse(ref reader);
        }

        pps.ListsModificationPresentFlag = reader.ReadFlag();
        pps.Log2ParMrgLevel = reader.ReadUE(4, "log2_parallel_merge_level_minus2") + 2;
        pps.SliceSegmentHeaderExtensionPresentFlag = reader.ReadFlag();
        var ppsRangeExtensionFlag = false;
        var ppsExtension4Bits = 0u;
        if (reader.ReadFlag())
        {
            ppsRangeExtensionFlag = reader.ReadFlag();
            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("multilayer extension");

            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("3D extension");

            if (reader.ReadFlag())
                throw HevcErrors.Unsupported("screen content coding extension");

            ppsExtension4Bits = reader.ReadBits(4);
        }

        if (ppsRangeExtensionFlag)
        {
            // Clause 7.3.2.3.2 "Picture parameter set range extension syntax"
            if (pps.TransformSkipEnabledFlag)
            {
                pps.Log2MaxTransformSkipSize = reader.ReadUE(3, "log2_max_transform_skip_block_size_minus2") + 2;
            }

            pps.CrossComponentPredictionEnabledFlag = reader.ReadFlag();
            pps.ChromaQpOffsetListEnabledFlag = reader.ReadFlag();
            if (pps.ChromaQpOffsetListEnabledFlag)
            {
                pps.DiffCuChromaQpOffsetDepth = reader.ReadUE(3, "diff_cu_chroma_qp_offset_depth");
                pps.ChromaQpOffsetListLenMinus1 = reader.ReadUE(5, "chroma_qp_offset_list_len_minus1");
                pps._cbQpOffsetList = new sbyte[pps.ChromaQpOffsetListLenMinus1 + 1];
                pps._crQpOffsetList = new sbyte[pps.ChromaQpOffsetListLenMinus1 + 1];
                for (var i = 0; i <= pps.ChromaQpOffsetListLenMinus1; i++)
                {
                    pps._cbQpOffsetList[i] = (sbyte)reader.ReadSE(-12, 12, "cb_qp_offset_list");
                    pps._crQpOffsetList[i] = (sbyte)reader.ReadSE(-12, 12, "cr_qp_offset_list");
                }
            }

            pps.Log2SaoOffsetScaleLuma = reader.ReadUE(HevcConstants.MaxBitDepth - 10, "log2_sao_offset_scale_luma");
            pps.Log2SaoOffsetScaleChroma = reader.ReadUE(HevcConstants.MaxBitDepth - 10, "log2_sao_offset_scale_chroma");
        }

        // pps_extension_data_flag: the data of the extensions that this version of the specification does not define
        if (ppsExtension4Bits is not 0)
        {
            reader.SkipExtensionData();
        }

        reader.ReadRbspTrailingBits();
        return pps;
    }
}
