namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tiers and the levels of HEVC (Rec. ITU-T H.265, clause A.4 "Tiers and levels"): the limits that a decoder of a level
/// supports. The encoder announces the lowest level of the Main tier whose limits hold its pictures, then the lowest level of
/// the High tier, then level 8.5, which has no limit.
/// </summary>
/// <remarks>
/// <para>
/// A level limits the size of the pictures (clause A.4.1, items a to c), and the number of bytes of each coded picture:
/// whatever the hypothetical reference decoder, a picture is entirely in the coded picture buffer when it is decoded, which
/// holds at most <c>CpbVclFactor * MaxCPB</c> bits (item g). The video profiles also have a minimum compression ratio
/// (clause A.4.2, item g, for the first picture, and item h, for the others): the largest picture that holds at every
/// picture rate of the level has <c>FormatCapabilityFactor * Max( PicSizeInSamplesY, fR * MaxLumaSr ) / MinCr</c> bytes,
/// which is the limit of the first picture. The still picture profiles do not have it.
/// </para>
/// <para>
/// The encoder writes no hypothetical reference decoder parameters and no timing: a still picture has no rate, and the
/// rate of a sequence is the one of the file that carries it. The limits of the rates (the luma sample rate and the bit
/// rate) are the ones of the reader that plays the file.
/// </para>
/// <para>
/// The Main tier is preferred to a lower level of the High tier: a decoder of a level of the High tier decodes the Main
/// tier of that level, but a decoder of the Main tier does not decode the High tier.
/// </para>
/// </remarks>
internal static class HevcLevels
{
    /// <summary>The value of <c>general_level_idc</c> of level 5: 30 times the level number.</summary>
    public const int Level5 = 150;

    /// <summary>The value of <c>general_level_idc</c> of level 7.</summary>
    public const int Level7 = 210;

    /// <summary>
    /// The value of <c>general_level_idc</c> of level 8.5, the label of the bitstreams that exceed the limits of every
    /// other level (clause A.4.1): it has no limit.
    /// </summary>
    public const int Level85 = 255;

    /// <summary><c>maxDpbPicBuf</c> of the profiles that do not allow <c>sps_curr_pic_ref_enabled_flag</c> (clause A.4.2).</summary>
    private const int MaxDpbPicBuf = 6;

    // The number of values of a level in LevelLimits
    private const int LimitCount = 7;

    // Tables A.8 and A.9 of the edition V11 (01/2026), one level per row: general_level_idc, MaxLumaPs (samples), MaxCPB of
    // the Main tier and of the High tier (in units of CpbVclFactor bits; 0 when the level has no High tier), MaxLumaSr
    // (samples per second), MinCrBase of the Main tier and of the High tier
    private static ReadOnlySpan<long> LevelLimits =>
    [
        30, 36_864, 350, 0, 552_960, 2, 2, // 1
        60, 122_880, 1_500, 0, 3_686_400, 2, 2, // 2
        63, 245_760, 3_000, 0, 7_372_800, 2, 2, // 2.1
        90, 552_960, 6_000, 0, 16_588_800, 2, 2, // 3
        93, 983_040, 10_000, 0, 33_177_600, 2, 2, // 3.1
        120, 2_228_224, 12_000, 30_000, 66_846_720, 4, 4, // 4
        123, 2_228_224, 20_000, 50_000, 133_693_440, 4, 4, // 4.1
        150, 8_912_896, 25_000, 100_000, 267_386_880, 6, 4, // 5
        153, 8_912_896, 40_000, 160_000, 534_773_760, 8, 4, // 5.1
        156, 8_912_896, 60_000, 240_000, 1_069_547_520, 8, 4, // 5.2
        180, 35_651_584, 60_000, 240_000, 1_069_547_520, 8, 4, // 6
        183, 35_651_584, 120_000, 480_000, 2_139_095_040, 8, 4, // 6.1
        186, 35_651_584, 240_000, 800_000, 4_278_190_080, 6, 4, // 6.2
        189, 80_216_064, 240_000, 1_600_000, 4_812_963_840, 6, 4, // 6.3
        210, 142_606_336, 240_000, 1_600_000, 4_812_963_840, 6, 4, // 7
        213, 142_606_336, 480_000, 3_200_000, 8_556_380_160, 6, 4, // 7.1
        216, 142_606_336, 960_000, 6_400_000, 17_112_760_320, 6, 4, // 7.2
    ];

    /// <summary>
    /// Returns the <c>general_level_idc</c> of the lowest level that holds pictures of
    /// <paramref name="picWidthInLumaSamples"/> x <paramref name="picHeightInLumaSamples"/> samples (clause A.4.1, items a
    /// to c): <c>PicSizeInSamplesY</c> is at most <c>MaxLumaPs</c>, and the width and the height are at most
    /// <c>Sqrt( MaxLumaPs * 8 )</c>. <see cref="Level85"/> when no level does.
    /// </summary>
    public static int GetLevelIdc(int picWidthInLumaSamples, int picHeightInLumaSamples)
    {
        var limits = LevelLimits;
        for (var i = 0; i < limits.Length; i += LimitCount)
        {
            if (HoldsPictureSize(limits[i + 1], picWidthInLumaSamples, picHeightInLumaSamples))
                return (int)limits[i];
        }

        return Level85;
    }

    /// <summary>
    /// Returns <c>general_level_idc</c> and <c>general_tier_flag</c> of the lowest level of the Main tier that holds
    /// pictures of <paramref name="picWidthInLumaSamples"/> x <paramref name="picHeightInLumaSamples"/> samples of the
    /// profile <paramref name="profile"/> whose largest one has <paramref name="maxPictureLength"/> bytes, then of the
    /// lowest level of the High tier, then level 8.5 in the High tier (clause A.4.1).
    /// </summary>
    /// <param name="profile">The profile of the bitstream.</param>
    /// <param name="picWidthInLumaSamples"><c>pic_width_in_luma_samples</c>.</param>
    /// <param name="picHeightInLumaSamples"><c>pic_height_in_luma_samples</c>.</param>
    /// <param name="maxPictureLength">
    /// The number of bytes of the NAL units of the largest coded picture, <c>NumBytesInNalUnit</c> of each; 0 when no
    /// picture is coded yet, which only limits the size of the pictures.
    /// </param>
    public static (int LevelIdc, bool TierFlag) GetLevel(HevcProfile profile, int picWidthInLumaSamples, int picHeightInLumaSamples, long maxPictureLength)
    {
        var limits = LevelLimits;
        foreach (var highTier in (ReadOnlySpan<bool>)[false, true])
        {
            for (var i = 0; i < limits.Length; i += LimitCount)
            {
                var level = limits.Slice(i, LimitCount);
                if (highTier && level[3] is 0)
                    continue;

                if (HoldsPictureSize(level[1], picWidthInLumaSamples, picHeightInLumaSamples) && HoldsPictureLength(profile, level, highTier, (long)picWidthInLumaSamples * picHeightInLumaSamples, maxPictureLength))
                    return ((int)level[0], highTier);
            }
        }

        return (Level85, true);
    }

    /// <summary>
    /// Returns <c>general_tier_flag</c> of the tier that the encoder announces with a level of the Main tier: 0, and the
    /// value 1 that clause A.4.1 requires with level 8.5.
    /// </summary>
    public static bool GetTierFlag(int levelIdc) => levelIdc is Level85;

    /// <summary>
    /// Returns whether a level requires coding tree blocks of 32 or 64 samples (clause A.4.1, item d): level 5 and the
    /// levels above it. Level 8.5 has no limit.
    /// </summary>
    public static bool RequiresLargeCodingTreeBlocks(int levelIdc) => levelIdc is >= Level5 and not Level85;

    /// <summary>
    /// Returns <c>MaxDpbSize</c> (Equation A-2), the largest value of <c>sps_max_dec_pic_buffering_minus1 + 1</c> of a
    /// level for pictures of <paramref name="picSizeInSamplesY"/> luma samples: 16 for level 8.5, which has no limit
    /// other than the range of the syntax element.
    /// </summary>
    public static int GetMaxDpbSize(int levelIdc, long picSizeInSamplesY)
    {
        var limits = LevelLimits;
        long maxLumaPs = -1;
        for (var i = 0; i < limits.Length; i += LimitCount)
        {
            // A level that is not in the table has the picture size of the level before it
            if (limits[i] <= levelIdc)
            {
                maxLumaPs = limits[i + 1];
            }
        }

        if (levelIdc is Level85 || maxLumaPs < 0)
            return HevcConstants.MaxDpbSize;

        if (picSizeInSamplesY <= maxLumaPs >> 2)
            return Math.Min(4 * MaxDpbPicBuf, HevcConstants.MaxDpbSize);

        if (picSizeInSamplesY <= maxLumaPs >> 1)
            return Math.Min(2 * MaxDpbPicBuf, HevcConstants.MaxDpbSize);

        if (picSizeInSamplesY <= (3 * maxLumaPs) >> 2)
            return Math.Min((4 * MaxDpbPicBuf) / 3, HevcConstants.MaxDpbSize);

        return MaxDpbPicBuf;
    }

    private static bool HoldsPictureSize(long maxLumaPs, int picWidthInLumaSamples, int picHeightInLumaSamples)
    {
        return (long)picWidthInLumaSamples * picHeightInLumaSamples <= maxLumaPs
            && (long)picWidthInLumaSamples * picWidthInLumaSamples <= maxLumaPs * 8
            && (long)picHeightInLumaSamples * picHeightInLumaSamples <= maxLumaPs * 8;
    }

    // Clause A.4.1, item g, and clause A.4.2, item g, for a picture of maxPictureLength bytes. HbrFactor is 1: the
    // profiles of the format range extensions are announced with general_lower_bit_rate_constraint_flag equal to 1, and
    // the others are Main, Main 10 and the still picture profiles. MinCr is then MinCrBase * MinCrScaleFactor
    private static bool HoldsPictureLength(HevcProfile profile, ReadOnlySpan<long> level, bool highTier, long picSizeInSamplesY, long maxPictureLength)
    {
        if (maxPictureLength is 0)
            return true;

        var (cpbVclFactor, formatCapabilityFactor, minCrScaleFactor, stillPicture) = GetProfileFactors(profile);
        var maxCpb = level[highTier ? 3 : 2];
        if (maxPictureLength * 8 > cpbVclFactor * maxCpb)
            return false;

        if (stillPicture)
            return true;

        // bytes <= FormatCapabilityFactor * Max( PicSizeInSamplesY, MaxLumaSr / fRDenominator ) / ( MinCrBase * MinCrScaleFactor ),
        // with FormatCapabilityFactor in thousandths and MinCrScaleFactor in halves
        var fRDenominator = highTier && level[0] >= Level7 ? 960 : 300;
        var minCrBase = level[highTier ? 6 : 5];
        var maxLumaSr = level[4];
        return maxPictureLength * minCrBase * minCrScaleFactor * 1000 * fRDenominator <= formatCapabilityFactor * 2 * Math.Max(picSizeInSamplesY * fRDenominator, maxLumaSr);
    }

    // Table A.10: CpbVclFactor, FormatCapabilityFactor (in thousandths) and MinCrScaleFactor (in halves) of a profile, and
    // whether it is a still picture profile, which clause A.4.2 does not limit
    private static (long CpbVclFactor, long FormatCapabilityFactor, long MinCrScaleFactor, bool StillPicture) GetProfileFactors(HevcProfile profile) => profile switch
    {
        HevcProfile.Main => (1000, 1500, 2, false),
        HevcProfile.Main10 => (1000, 1875, 2, false),
        HevcProfile.MainStillPicture => (1000, 1500, 2, true),
        HevcProfile.Main10StillPicture => (1000, 1875, 2, true),
        HevcProfile.Monochrome => (667, 1000, 2, false),
        HevcProfile.Monochrome10 => (833, 1250, 2, false),
        HevcProfile.Monochrome12 => (1000, 1500, 2, false),
        HevcProfile.Main12 or HevcProfile.Main12Intra => (1500, 2250, 2, false),
        HevcProfile.Main42210 or HevcProfile.Main42210Intra => (1667, 2500, 1, false),
        HevcProfile.Main42212 or HevcProfile.Main42212Intra => (2000, 3000, 1, false),
        HevcProfile.Main444 or HevcProfile.Main444Intra => (2000, 3000, 1, false),
        HevcProfile.Main44410 or HevcProfile.Main44410Intra => (2500, 3750, 1, false),
        HevcProfile.Main44412 or HevcProfile.Main44412Intra => (3000, 4500, 1, false),
        HevcProfile.Main444StillPicture => (2000, 3000, 1, true),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, message: null),
    };
}
