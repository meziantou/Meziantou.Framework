using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the encoder decides for a picture, which is one slice of one slice segment: the values of the syntax elements of
/// its slice segment header (Rec. ITU-T H.265, clause 7.3.6.1) that the parameter sets do not give. A value that is not
/// given is 0 or <see langword="false"/>.
/// </summary>
/// <param name="NalUnitType">
/// <c>nal_unit_type</c> of the slice segment: <see cref="HevcConstants.NalIdrNLp"/> for a key frame, which no leading
/// picture follows, and <see cref="HevcConstants.NalTrailR"/> for a picture that the next one refers to.
/// </param>
/// <param name="SliceType"><c>slice_type</c>: <see cref="HevcConstants.SliceI"/>, or <see cref="HevcConstants.SliceP"/> for a slice that refers to the previous picture.</param>
/// <param name="PicOrderCnt">
/// The picture order count of the picture, 0 for a key frame: its bits below <c>MaxPicOrderCntLsb</c> are
/// <c>slice_pic_order_cnt_lsb</c>.
/// </param>
/// <param name="SliceQp"><c>SliceQpY</c>: <c>-QpBdOffsetY</c> to 51.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HevcSliceParameters(int NalUnitType, int SliceType, int PicOrderCnt, int SliceQp)
{
    /// <summary>Gets <c>slice_sao_luma_flag</c>, when the sequence enables the sample adaptive offset.</summary>
    public bool SliceSaoLuma { get; init; }

    /// <summary>Gets <c>slice_sao_chroma_flag</c>, when the sequence enables the sample adaptive offset and has chroma samples.</summary>
    public bool SliceSaoChroma { get; init; }

    /// <summary>Gets <c>slice_temporal_mvp_enabled_flag</c>, when the sequence enables the temporal motion vector prediction.</summary>
    public bool SliceTemporalMvpEnabled { get; init; }

    /// <summary>Gets <c>cabac_init_flag</c> of a P slice, when the picture parameter set has <c>cabac_init_present_flag</c>.</summary>
    public bool CabacInit { get; init; }

    /// <summary>Gets <c>five_minus_max_num_merge_cand</c> of a P slice: 0 to 4.</summary>
    public int FiveMinusMaxNumMergeCand { get; init; }

    /// <summary>Gets <c>slice_cb_qp_offset</c>, when the picture parameter set has <c>pps_slice_chroma_qp_offsets_present_flag</c>.</summary>
    public int SliceCbQpOffset { get; init; }

    /// <summary>Gets <c>slice_cr_qp_offset</c>.</summary>
    public int SliceCrQpOffset { get; init; }

    /// <summary>Gets <c>deblocking_filter_override_flag</c>, when the picture parameter set has <c>deblocking_filter_override_enabled_flag</c>.</summary>
    public bool DeblockingFilterOverride { get; init; }

    /// <summary>Gets <c>slice_deblocking_filter_disabled_flag</c>, when <see cref="DeblockingFilterOverride"/> is set.</summary>
    public bool SliceDeblockingFilterDisabled { get; init; }

    /// <summary>Gets <c>slice_beta_offset_div2</c>, when <see cref="DeblockingFilterOverride"/> is set: -6 to 6.</summary>
    public int SliceBetaOffsetDiv2 { get; init; }

    /// <summary>Gets <c>slice_tc_offset_div2</c>, when <see cref="DeblockingFilterOverride"/> is set: -6 to 6.</summary>
    public int SliceTcOffsetDiv2 { get; init; }

    /// <summary>Creates the parameters of a key frame: an instantaneous decoding refresh picture, in an I slice.</summary>
    public static HevcSliceParameters CreateKeyFrame(int sliceQp) => new(HevcConstants.NalIdrNLp, HevcConstants.SliceI, PicOrderCnt: 0, sliceQp);

    /// <summary>
    /// Creates the parameters of a picture that follows a key frame: a trailing picture that the next picture may refer
    /// to, in a P slice that refers to the picture before it.
    /// </summary>
    /// <param name="picOrderCnt">The number of pictures since the key frame.</param>
    /// <param name="sliceQp"><c>SliceQpY</c>.</param>
    public static HevcSliceParameters CreateInterFrame(int picOrderCnt, int sliceQp) => new(HevcConstants.NalTrailR, HevcConstants.SliceP, picOrderCnt, sliceQp);
}
