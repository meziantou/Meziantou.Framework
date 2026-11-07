using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the encoder decides for the pictures that refer to a picture parameter set: the values of its syntax elements that
/// are not constant in the streams the encoder writes (Rec. ITU-T H.265, clauses 7.3.2.3.1 and 7.3.2.3.2). A value that is
/// not given is 0 or <see langword="false"/>, which is the value a tool that is not used has.
/// </summary>
/// <param name="InitQp"><c>init_qp_minus26 + 26</c>: the luma quantization parameter that <c>slice_qp_delta</c> is relative to, <c>-QpBdOffsetY</c> to 51.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HevcPictureParameters(int InitQp)
{
    /// <summary>Gets <c>pps_pic_parameter_set_id</c>: 0 to 63.</summary>
    public int PicParameterSetId { get; init; }

    /// <summary>Gets <c>sign_data_hiding_enabled_flag</c>.</summary>
    public bool SignDataHidingEnabled { get; init; }

    /// <summary>Gets <c>cabac_init_present_flag</c>: the slice headers of the P slices have <c>cabac_init_flag</c>.</summary>
    public bool CabacInitPresent { get; init; }

    /// <summary>Gets <c>constrained_intra_pred_flag</c>.</summary>
    public bool ConstrainedIntraPred { get; init; }

    /// <summary>Gets <c>transform_skip_enabled_flag</c>.</summary>
    public bool TransformSkipEnabled { get; init; }

    /// <summary>
    /// Gets <c>log2_max_transform_skip_block_size_minus2</c>, a syntax element of the range extension: more than 0 needs
    /// <see cref="HevcCodingTools.LargeTransformSkipBlocks"/> in the sequence.
    /// </summary>
    public int Log2MaxTransformSkipBlockSizeMinus2 { get; init; }

    /// <summary>Gets <c>cu_qp_delta_enabled_flag</c>.</summary>
    public bool CuQpDeltaEnabled { get; init; }

    /// <summary>Gets <c>diff_cu_qp_delta_depth</c>, when <see cref="CuQpDeltaEnabled"/> is set.</summary>
    public int DiffCuQpDeltaDepth { get; init; }

    /// <summary>Gets <c>pps_cb_qp_offset</c>: -12 to 12.</summary>
    public int CbQpOffset { get; init; }

    /// <summary>Gets <c>pps_cr_qp_offset</c>: -12 to 12.</summary>
    public int CrQpOffset { get; init; }

    /// <summary>Gets <c>pps_slice_chroma_qp_offsets_present_flag</c>.</summary>
    public bool SliceChromaQpOffsetsPresent { get; init; }

    /// <summary>Gets <c>transquant_bypass_enabled_flag</c>.</summary>
    public bool TransquantBypassEnabled { get; init; }

    /// <summary>Gets a value indicating whether each row of coding tree blocks is a substream of the wavefront (<c>entropy_coding_sync_enabled_flag</c>).</summary>
    public bool EntropyCodingSyncEnabled { get; init; }

    /// <summary>Gets <c>deblocking_filter_override_enabled_flag</c>.</summary>
    public bool DeblockingFilterOverrideEnabled { get; init; }

    /// <summary>Gets <c>pps_deblocking_filter_disabled_flag</c>.</summary>
    public bool DeblockingFilterDisabled { get; init; }

    /// <summary>Gets <c>pps_beta_offset_div2</c>: -6 to 6.</summary>
    public int BetaOffsetDiv2 { get; init; }

    /// <summary>Gets <c>pps_tc_offset_div2</c>: -6 to 6.</summary>
    public int TcOffsetDiv2 { get; init; }

    /// <summary>Gets <c>cross_component_prediction_enabled_flag</c>, a syntax element of the range extension, for 4:4:4 pictures.</summary>
    public bool CrossComponentPredictionEnabled { get; init; }

    /// <summary>Gets <c>log2_sao_offset_scale_luma</c>, a syntax element of the range extension: at most <c>BitDepthY - 10</c>.</summary>
    public int Log2SaoOffsetScaleLuma { get; init; }

    /// <summary>Gets <c>log2_sao_offset_scale_chroma</c>, a syntax element of the range extension: at most <c>BitDepthC - 10</c>.</summary>
    public int Log2SaoOffsetScaleChroma { get; init; }

    /// <summary>
    /// Gets <c>deblocking_filter_control_present_flag</c>: whether a syntax element of the deblocking filter has another
    /// value than the one that is inferred without them.
    /// </summary>
    public bool DeblockingFilterControlPresent => DeblockingFilterOverrideEnabled || DeblockingFilterDisabled || BetaOffsetDiv2 is not 0 || TcOffsetDiv2 is not 0;

    /// <summary>
    /// Gets <c>pps_range_extension_flag</c>: whether a syntax element of the range extension has another value than the
    /// one that is inferred without the extension.
    /// </summary>
    public bool HasRangeExtension => Log2MaxTransformSkipBlockSizeMinus2 is not 0 || CrossComponentPredictionEnabled || Log2SaoOffsetScaleLuma is not 0 || Log2SaoOffsetScaleChroma is not 0;
}
