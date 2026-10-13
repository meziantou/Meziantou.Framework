namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The parameters of the hypothetical reference decoder (Rec. ITU-T H.265, clauses E.2.2 "HRD parameters syntax" and E.2.3
/// "Sub-layer HRD parameters syntax"). They describe the timing of a conforming decoder and do not take part in the
/// decoding of the samples: the syntax is read to reach the syntax elements that follow it, and its values are validated
/// and not kept.
/// </summary>
internal static class HevcHrdParameters
{
    /// <summary>Reads the syntax structure <c>hrd_parameters( commonInfPresentFlag, maxNumSubLayersMinus1 )</c>.</summary>
    public static void Skip(ref HevcBitReader reader, bool commonInfPresentFlag, int maxNumSubLayersMinus1)
    {
        var nalHrdParametersPresentFlag = false;
        var vclHrdParametersPresentFlag = false;
        var subPicHrdParamsPresentFlag = false;
        if (commonInfPresentFlag)
        {
            nalHrdParametersPresentFlag = reader.ReadFlag();
            vclHrdParametersPresentFlag = reader.ReadFlag();
            if (nalHrdParametersPresentFlag || vclHrdParametersPresentFlag)
            {
                subPicHrdParamsPresentFlag = reader.ReadFlag();
                if (subPicHrdParamsPresentFlag)
                {
                    // tick_divisor_minus2, du_cpb_removal_delay_increment_length_minus1,
                    // sub_pic_cpb_params_in_pic_timing_sei_flag, dpb_output_delay_du_length_minus1
                    reader.Skip(8 + 5 + 1 + 5);
                }

                // bit_rate_scale, cpb_size_scale
                reader.Skip(4 + 4);
                if (subPicHrdParamsPresentFlag)
                {
                    // cpb_size_du_scale
                    reader.Skip(4);
                }

                // initial_cpb_removal_delay_length_minus1, au_cpb_removal_delay_length_minus1,
                // dpb_output_delay_length_minus1
                reader.Skip(5 + 5 + 5);
            }
        }

        for (var i = 0; i <= maxNumSubLayersMinus1; i++)
        {
            var fixedPicRateGeneralFlag = reader.ReadFlag();
            var fixedPicRateWithinCvsFlag = fixedPicRateGeneralFlag || reader.ReadFlag();
            var lowDelayHrdFlag = false;
            if (fixedPicRateWithinCvsFlag)
            {
                reader.ReadUE(2047, "elemental_duration_in_tc_minus1");
            }
            else
            {
                lowDelayHrdFlag = reader.ReadFlag();
            }

            var cpbCntMinus1 = lowDelayHrdFlag ? 0 : reader.ReadUE(31, "cpb_cnt_minus1");
            if (nalHrdParametersPresentFlag)
            {
                SkipSubLayerHrdParameters(ref reader, cpbCntMinus1 + 1, subPicHrdParamsPresentFlag);
            }

            if (vclHrdParametersPresentFlag)
            {
                SkipSubLayerHrdParameters(ref reader, cpbCntMinus1 + 1, subPicHrdParamsPresentFlag);
            }
        }
    }

    // sub_layer_hrd_parameters( subLayerId )
    private static void SkipSubLayerHrdParameters(ref HevcBitReader reader, int cpbCnt, bool subPicHrdParamsPresentFlag)
    {
        for (var i = 0; i < cpbCnt; i++)
        {
            // bit_rate_value_minus1, cpb_size_value_minus1
            reader.ReadUE();
            reader.ReadUE();
            if (subPicHrdParamsPresentFlag)
            {
                // cpb_size_du_value_minus1, bit_rate_du_value_minus1
                reader.ReadUE();
                reader.ReadUE();
            }

            // cbr_flag
            reader.Skip(1);
        }
    }
}
