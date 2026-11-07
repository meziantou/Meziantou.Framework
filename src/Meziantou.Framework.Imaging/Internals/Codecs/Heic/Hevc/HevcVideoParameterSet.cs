namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A video parameter set (Rec. ITU-T H.265, clause 7.3.2.1 "Video parameter set RBSP syntax"). The decoding of the base
/// layer does not depend on its values: the syntax is validated, and the extension data, which describe the other layers,
/// are skipped.
/// </summary>
internal sealed class HevcVideoParameterSet
{
    private HevcVideoParameterSet()
    {
    }

    /// <summary>Gets <c>vps_video_parameter_set_id</c>.</summary>
    public int VideoParameterSetId { get; private init; }

    /// <summary>Gets <c>vps_max_layers_minus1</c>.</summary>
    public int MaxLayersMinus1 { get; private init; }

    /// <summary>Gets <c>vps_max_sub_layers_minus1</c>.</summary>
    public int MaxSubLayersMinus1 { get; private init; }

    /// <summary>Gets <c>vps_temporal_id_nesting_flag</c>.</summary>
    public bool TemporalIdNestingFlag { get; private init; }

    /// <summary>Gets the profile, tier and level of the layer set of the base layer.</summary>
    public required HevcProfileTierLevel ProfileTierLevel { get; init; }

    /// <summary>Gets <c>vps_timing_info_present_flag</c>.</summary>
    public bool TimingInfoPresentFlag { get; private init; }

    /// <summary>Gets <c>vps_num_units_in_tick</c>.</summary>
    public uint NumUnitsInTick { get; private init; }

    /// <summary>Gets <c>vps_time_scale</c>.</summary>
    public uint TimeScale { get; private init; }

    /// <summary>Reads a video parameter set from the payload of its NAL unit, without emulation prevention bytes.</summary>
    /// <exception cref="InvalidImageContentException">The payload is malformed or truncated.</exception>
    public static HevcVideoParameterSet Parse(ReadOnlySpan<byte> rbsp)
    {
        var reader = new HevcBitReader(rbsp);
        var id = (int)reader.ReadBits(4);

        // vps_base_layer_internal_flag, vps_base_layer_available_flag
        reader.Skip(2);
        var maxLayersMinus1 = (int)reader.ReadBits(6);
        var maxSubLayersMinus1 = (int)reader.ReadBits(3);
        if (maxSubLayersMinus1 > 6)
            throw HevcErrors.Invalid("vps_max_sub_layers_minus1 is out of range.");

        var temporalIdNestingFlag = reader.ReadFlag();

        // vps_reserved_0xffff_16bits
        reader.Skip(16);
        var profileTierLevel = HevcProfileTierLevel.Parse(ref reader, profilePresentFlag: true, maxSubLayersMinus1);
        var subLayerOrderingInfoPresentFlag = reader.ReadFlag();
        for (var i = subLayerOrderingInfoPresentFlag ? 0 : maxSubLayersMinus1; i <= maxSubLayersMinus1; i++)
        {
            var maxDecPicBufferingMinus1 = reader.ReadUE(HevcConstants.MaxDpbSize - 1, "vps_max_dec_pic_buffering_minus1");
            reader.ReadUE(maxDecPicBufferingMinus1, "vps_max_num_reorder_pics");

            // vps_max_latency_increase_plus1
            reader.ReadUE();
        }

        var maxLayerId = (int)reader.ReadBits(6);
        var numLayerSetsMinus1 = reader.ReadUE(1023, "vps_num_layer_sets_minus1");

        // layer_id_included_flag[ i ][ j ]
        reader.Skip((long)numLayerSetsMinus1 * (maxLayerId + 1));
        var timingInfoPresentFlag = reader.ReadFlag();
        uint numUnitsInTick = 0;
        uint timeScale = 0;
        if (timingInfoPresentFlag)
        {
            numUnitsInTick = reader.ReadBits(32);
            timeScale = reader.ReadBits(32);

            // vps_poc_proportional_to_timing_flag, vps_num_ticks_poc_diff_one_minus1
            if (reader.ReadFlag())
            {
                reader.ReadUE();
            }

            var numHrdParameters = reader.ReadUE(numLayerSetsMinus1 + 1, "vps_num_hrd_parameters");
            for (var i = 0; i < numHrdParameters; i++)
            {
                reader.ReadUE(numLayerSetsMinus1, "hrd_layer_set_idx");
                var cprmsPresentFlag = i is 0 || reader.ReadFlag();
                HevcHrdParameters.Skip(ref reader, cprmsPresentFlag, maxSubLayersMinus1);
            }
        }

        // vps_extension_flag: the extension data are those of the layers that are not decoded
        if (reader.ReadFlag())
        {
            reader.SkipExtensionData();
        }

        reader.ReadRbspTrailingBits();
        return new HevcVideoParameterSet
        {
            VideoParameterSetId = id,
            MaxLayersMinus1 = maxLayersMinus1,
            MaxSubLayersMinus1 = maxSubLayersMinus1,
            TemporalIdNestingFlag = temporalIdNestingFlag,
            ProfileTierLevel = profileTierLevel,
            TimingInfoPresentFlag = timingInfoPresentFlag,
            NumUnitsInTick = numUnitsInTick,
            TimeScale = timeScale,
        };
    }
}
