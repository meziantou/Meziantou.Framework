namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The levels of HEVC (Rec. ITU-T H.265, clause A.4 "Tiers and levels"): the limits that a decoder of a level supports.
/// The encoder announces the lowest level whose picture size limits hold its pictures.
/// </summary>
/// <remarks>
/// A level also limits the bit rate, the size of the coded picture buffer and the luma sample rate (Table A.9). The
/// encoder writes no hypothetical reference decoder parameters and no timing: a still picture has no rate, and the rate of
/// a sequence is the one of the file that carries it. The level only says how large the pictures are.
/// </remarks>
internal static class HevcLevels
{
    /// <summary>The value of <c>general_level_idc</c> of level 5: 30 times the level number.</summary>
    public const int Level5 = 150;

    /// <summary>
    /// The value of <c>general_level_idc</c> of level 8.5, the label of the bitstreams that exceed the limits of every
    /// other level (clause A.4.1): it has no limit.
    /// </summary>
    public const int Level85 = 255;

    /// <summary><c>maxDpbPicBuf</c> of the profiles that do not allow <c>sps_curr_pic_ref_enabled_flag</c> (clause A.4.2).</summary>
    private const int MaxDpbPicBuf = 6;

    // general_level_idc and MaxLumaPs of the levels that differ by their picture size (Table A.8). The other levels (4.1,
    // 5.1, 5.2, 6.1, 6.2, 7.1 and 7.2) only raise the rates
    private static ReadOnlySpan<int> PictureLimits =>
    [
        30, 36864, // 1
        60, 122880, // 2
        63, 245760, // 2.1
        90, 552960, // 3
        93, 983040, // 3.1
        120, 2228224, // 4
        150, 8912896, // 5
        180, 35651584, // 6
        189, 80216064, // 6.3
        210, 142606336, // 7
    ];

    /// <summary>
    /// Returns the <c>general_level_idc</c> of the lowest level that holds pictures of
    /// <paramref name="picWidthInLumaSamples"/> x <paramref name="picHeightInLumaSamples"/> samples (clause A.4.1, items a
    /// to c): <c>PicSizeInSamplesY</c> is at most <c>MaxLumaPs</c>, and the width and the height are at most
    /// <c>Sqrt( MaxLumaPs * 8 )</c>. <see cref="Level85"/> when no level does.
    /// </summary>
    public static int GetLevelIdc(int picWidthInLumaSamples, int picHeightInLumaSamples)
    {
        var limits = PictureLimits;
        var picSizeInSamplesY = (long)picWidthInLumaSamples * picHeightInLumaSamples;
        for (var i = 0; i < limits.Length; i += 2)
        {
            long maxLumaPs = limits[i + 1];
            if (picSizeInSamplesY <= maxLumaPs
                && (long)picWidthInLumaSamples * picWidthInLumaSamples <= maxLumaPs * 8
                && (long)picHeightInLumaSamples * picHeightInLumaSamples <= maxLumaPs * 8)
            {
                return limits[i];
            }
        }

        return Level85;
    }

    /// <summary>
    /// Returns <c>general_tier_flag</c> of the tier that the encoder announces with a level: the Main tier, and the value
    /// 1 that clause A.4.1 requires with level 8.5.
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
        var limits = PictureLimits;
        long maxLumaPs = -1;
        for (var i = 0; i < limits.Length; i += 2)
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
}
