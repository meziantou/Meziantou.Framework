namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The coding tools that the encoder decides to enable for a coded video sequence: the flags of the sequence parameter
/// set that are not constant in the streams it writes, and the tools of the picture parameter sets that the profile of
/// the sequence depends on (Rec. ITU-T H.265, Table A.1).
/// </summary>
[Flags]
internal enum HevcCodingTools
{
    None = 0,

    /// <summary><c>scaling_list_enabled_flag</c>, with the default scaling lists (Tables 7-5 and 7-6).</summary>
    ScalingLists = 1 << 0,

    /// <summary><c>amp_enabled_flag</c>: asymmetric partitions of the coding units coded in inter prediction mode.</summary>
    AsymmetricMotionPartitions = 1 << 1,

    /// <summary><c>sample_adaptive_offset_enabled_flag</c>.</summary>
    SampleAdaptiveOffset = 1 << 2,

    /// <summary><c>pcm_enabled_flag</c>: coding units whose samples are in the bitstream as they are.</summary>
    Pcm = 1 << 3,

    /// <summary><c>sps_temporal_mvp_enabled_flag</c>.</summary>
    TemporalMvp = 1 << 4,

    /// <summary><c>strong_intra_smoothing_enabled_flag</c>.</summary>
    StrongIntraSmoothing = 1 << 5,

    /// <summary><c>transform_skip_rotation_enabled_flag</c> (range extensions).</summary>
    TransformSkipRotation = 1 << 6,

    /// <summary><c>transform_skip_context_enabled_flag</c> (range extensions).</summary>
    TransformSkipContext = 1 << 7,

    /// <summary><c>implicit_rdpcm_enabled_flag</c> (range extensions).</summary>
    ImplicitRdpcm = 1 << 8,

    /// <summary><c>explicit_rdpcm_enabled_flag</c> (range extensions).</summary>
    ExplicitRdpcm = 1 << 9,

    /// <summary><c>intra_smoothing_disabled_flag</c> (range extensions).</summary>
    IntraSmoothingDisabled = 1 << 10,

    /// <summary><c>persistent_rice_adaptation_enabled_flag</c> (range extensions).</summary>
    PersistentRiceAdaptation = 1 << 11,

    /// <summary>
    /// The picture parameter sets of the sequence may have <c>log2_max_transform_skip_block_size_minus2</c> greater than
    /// 0 (range extensions): transform skip blocks of more than 4x4 samples.
    /// </summary>
    LargeTransformSkipBlocks = 1 << 12,

    /// <summary>
    /// The tools that only the 4:4:4 profiles of the format range extensions allow, whatever the chroma format (Table A.1):
    /// a sequence that enables one of them announces one of these profiles.
    /// </summary>
    ExtendedTools = TransformSkipRotation | TransformSkipContext | ImplicitRdpcm | ExplicitRdpcm | IntraSmoothingDisabled | PersistentRiceAdaptation | LargeTransformSkipBlocks,
}
