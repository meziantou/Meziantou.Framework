namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constant tables of the CABAC parsing process, transcribed from Rec. ITU-T H.265 (01/2026): the values of
/// <c>initValue</c> of Tables 9-5 to 9-37 arranged by <see cref="HevcContextLayout"/> for each of the three values of
/// <c>initType</c> (Table 9-4), <c>rangeTabLps</c> (Table 9-52), the state transitions (Table 9-53) and <c>ctxIdxMap</c>
/// (Table 9-50).
/// </summary>
/// <remarks>
/// A syntax element that does not exist for an <c>initType</c> (the syntax elements of inter prediction for
/// <c>initType</c> 0, three of the four contexts of <c>part_mode</c>) has the value 154 there, which no decoding reads.
/// </remarks>
internal static class HevcCabacTables
{
    /// <summary>The values of <c>initValue</c> of every context variable for <c>initType</c> 0.</summary>
    public static ReadOnlySpan<byte> InitValues0 =>
    [
        // SaoMergeFlag (Table 9-5)
        153,
        // SaoTypeIdx (Table 9-6)
        200,
        // SplitCuFlag (Table 9-7)
        139, 141, 157,
        // CuTransquantBypassFlag (Table 9-8)
        154,
        // CuSkipFlag (Table 9-9)
        154, 154, 154,
        // PredModeFlag (Table 9-10)
        154,
        // PartMode (Table 9-11)
        184, 154, 154, 154,
        // PrevIntraLumaPredFlag (Table 9-12)
        184,
        // IntraChromaPredMode (Table 9-13)
        63,
        // RqtRootCbf (Table 9-14)
        154,
        // MergeFlag (Table 9-15)
        154,
        // MergeIdx (Table 9-16)
        154,
        // InterPredIdc (Table 9-17)
        154, 154, 154, 154, 154,
        // RefIdx (Table 9-18)
        154, 154,
        // MvpFlag (Table 9-19)
        154,
        // SplitTransformFlag (Table 9-20)
        153, 138, 138,
        // CbfLuma (Table 9-21)
        111, 141,
        // CbfChroma (Table 9-22)
        94, 138, 182, 154, 154,
        // AbsMvdGreater0Flag (Table 9-23)
        154,
        // AbsMvdGreater1Flag (Table 9-23)
        154,
        // CuQpDeltaAbs (Table 9-24)
        154, 154,
        // TransformSkipFlag (Table 9-25)
        139, 139,
        // LastSigCoeffXPrefix (Table 9-26)
        110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108,
        123, 63,
        // LastSigCoeffYPrefix (Table 9-27)
        110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108,
        123, 63,
        // CodedSubBlockFlag (Table 9-28)
        91, 171, 134, 141,
        // SigCoeffFlag (Table 9-29)
        111, 111, 125, 110, 110, 94, 124, 108, 124, 107, 125, 141, 179, 153, 125, 107,
        125, 141, 179, 153, 125, 107, 125, 141, 179, 153, 125, 140, 139, 182, 182, 152,
        136, 152, 136, 153, 136, 139, 111, 136, 139, 111, 141, 111,
        // CoeffAbsLevelGreater1Flag (Table 9-30)
        140, 92, 137, 138, 140, 152, 138, 139, 153, 74, 149, 92, 139, 107, 122, 152,
        140, 179, 166, 182, 140, 227, 122, 197,
        // CoeffAbsLevelGreater2Flag (Table 9-31)
        138, 153, 136, 167, 152, 152,
        // ExplicitRdpcmFlag (Table 9-32)
        154, 154,
        // ExplicitRdpcmDirFlag (Table 9-33)
        154, 154,
        // CuChromaQpOffsetFlag (Table 9-34)
        154,
        // CuChromaQpOffsetIdx (Table 9-35)
        154,
        // Log2ResScaleAbsPlus1 (Table 9-36)
        154, 154, 154, 154, 154, 154, 154, 154,
        // ResScaleSignFlag (Table 9-37)
        154, 154,
    ];

    /// <summary>The values of <c>initValue</c> of every context variable for <c>initType</c> 1.</summary>
    public static ReadOnlySpan<byte> InitValues1 =>
    [
        // SaoMergeFlag (Table 9-5)
        153,
        // SaoTypeIdx (Table 9-6)
        185,
        // SplitCuFlag (Table 9-7)
        107, 139, 126,
        // CuTransquantBypassFlag (Table 9-8)
        154,
        // CuSkipFlag (Table 9-9)
        197, 185, 201,
        // PredModeFlag (Table 9-10)
        149,
        // PartMode (Table 9-11)
        154, 139, 154, 154,
        // PrevIntraLumaPredFlag (Table 9-12)
        154,
        // IntraChromaPredMode (Table 9-13)
        152,
        // RqtRootCbf (Table 9-14)
        79,
        // MergeFlag (Table 9-15)
        110,
        // MergeIdx (Table 9-16)
        122,
        // InterPredIdc (Table 9-17)
        95, 79, 63, 31, 31,
        // RefIdx (Table 9-18)
        153, 153,
        // MvpFlag (Table 9-19)
        168,
        // SplitTransformFlag (Table 9-20)
        124, 138, 94,
        // CbfLuma (Table 9-21)
        153, 111,
        // CbfChroma (Table 9-22)
        149, 107, 167, 154, 154,
        // AbsMvdGreater0Flag (Table 9-23)
        140,
        // AbsMvdGreater1Flag (Table 9-23)
        198,
        // CuQpDeltaAbs (Table 9-24)
        154, 154,
        // TransformSkipFlag (Table 9-25)
        139, 139,
        // LastSigCoeffXPrefix (Table 9-26)
        125, 110, 94, 110, 95, 79, 125, 111, 110, 78, 110, 111, 111, 95, 94, 108,
        123, 108,
        // LastSigCoeffYPrefix (Table 9-27)
        125, 110, 94, 110, 95, 79, 125, 111, 110, 78, 110, 111, 111, 95, 94, 108,
        123, 108,
        // CodedSubBlockFlag (Table 9-28)
        121, 140, 61, 154,
        // SigCoeffFlag (Table 9-29)
        155, 154, 139, 153, 139, 123, 123, 63, 153, 166, 183, 140, 136, 153, 154, 166,
        183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 170, 153, 123, 123, 107,
        121, 107, 121, 167, 151, 183, 140, 151, 183, 140, 140, 140,
        // CoeffAbsLevelGreater1Flag (Table 9-30)
        154, 196, 196, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 137,
        169, 194, 166, 167, 154, 167, 137, 182,
        // CoeffAbsLevelGreater2Flag (Table 9-31)
        107, 167, 91, 122, 107, 167,
        // ExplicitRdpcmFlag (Table 9-32)
        139, 139,
        // ExplicitRdpcmDirFlag (Table 9-33)
        139, 139,
        // CuChromaQpOffsetFlag (Table 9-34)
        154,
        // CuChromaQpOffsetIdx (Table 9-35)
        154,
        // Log2ResScaleAbsPlus1 (Table 9-36)
        154, 154, 154, 154, 154, 154, 154, 154,
        // ResScaleSignFlag (Table 9-37)
        154, 154,
    ];

    /// <summary>The values of <c>initValue</c> of every context variable for <c>initType</c> 2.</summary>
    public static ReadOnlySpan<byte> InitValues2 =>
    [
        // SaoMergeFlag (Table 9-5)
        153,
        // SaoTypeIdx (Table 9-6)
        160,
        // SplitCuFlag (Table 9-7)
        107, 139, 126,
        // CuTransquantBypassFlag (Table 9-8)
        154,
        // CuSkipFlag (Table 9-9)
        197, 185, 201,
        // PredModeFlag (Table 9-10)
        134,
        // PartMode (Table 9-11)
        154, 139, 154, 154,
        // PrevIntraLumaPredFlag (Table 9-12)
        183,
        // IntraChromaPredMode (Table 9-13)
        152,
        // RqtRootCbf (Table 9-14)
        79,
        // MergeFlag (Table 9-15)
        154,
        // MergeIdx (Table 9-16)
        137,
        // InterPredIdc (Table 9-17)
        95, 79, 63, 31, 31,
        // RefIdx (Table 9-18)
        153, 153,
        // MvpFlag (Table 9-19)
        168,
        // SplitTransformFlag (Table 9-20)
        224, 167, 122,
        // CbfLuma (Table 9-21)
        153, 111,
        // CbfChroma (Table 9-22)
        149, 92, 167, 154, 154,
        // AbsMvdGreater0Flag (Table 9-23)
        169,
        // AbsMvdGreater1Flag (Table 9-23)
        198,
        // CuQpDeltaAbs (Table 9-24)
        154, 154,
        // TransformSkipFlag (Table 9-25)
        139, 139,
        // LastSigCoeffXPrefix (Table 9-26)
        125, 110, 124, 110, 95, 94, 125, 111, 111, 79, 125, 126, 111, 111, 79, 108,
        123, 93,
        // LastSigCoeffYPrefix (Table 9-27)
        125, 110, 124, 110, 95, 94, 125, 111, 111, 79, 125, 126, 111, 111, 79, 108,
        123, 93,
        // CodedSubBlockFlag (Table 9-28)
        121, 140, 61, 154,
        // SigCoeffFlag (Table 9-29)
        170, 154, 139, 153, 139, 123, 123, 63, 124, 166, 183, 140, 136, 153, 154, 166,
        183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 170, 153, 138, 138, 122,
        121, 122, 121, 167, 151, 183, 140, 151, 183, 140, 140, 140,
        // CoeffAbsLevelGreater1Flag (Table 9-30)
        154, 196, 167, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 122,
        169, 208, 166, 167, 154, 152, 167, 182,
        // CoeffAbsLevelGreater2Flag (Table 9-31)
        107, 167, 91, 107, 107, 167,
        // ExplicitRdpcmFlag (Table 9-32)
        139, 139,
        // ExplicitRdpcmDirFlag (Table 9-33)
        139, 139,
        // CuChromaQpOffsetFlag (Table 9-34)
        154,
        // CuChromaQpOffsetIdx (Table 9-35)
        154,
        // Log2ResScaleAbsPlus1 (Table 9-36)
        154, 154, 154, 154, 154, 154, 154, 154,
        // ResScaleSignFlag (Table 9-37)
        154, 154,
    ];

    /// <summary><c>rangeTabLps[ pStateIdx ][ qRangeIdx ]</c> (Table 9-52): four values per state.</summary>
    public static ReadOnlySpan<byte> RangeTabLps =>
    [
        128, 176, 208, 240, 128, 167, 197, 227, 128, 158, 187, 216, 123, 150, 178, 205,
        116, 142, 169, 195, 111, 135, 160, 185, 105, 128, 152, 175, 100, 122, 144, 166,
        95, 116, 137, 158, 90, 110, 130, 150, 85, 104, 123, 142, 81, 99, 117, 135,
        77, 94, 111, 128, 73, 89, 105, 122, 69, 85, 100, 116, 66, 80, 95, 110,
        62, 76, 90, 104, 59, 72, 86, 99, 56, 69, 81, 94, 53, 65, 77, 89,
        51, 62, 73, 85, 48, 59, 69, 80, 46, 56, 66, 76, 43, 53, 63, 72,
        41, 50, 59, 69, 39, 48, 56, 65, 37, 45, 54, 62, 35, 43, 51, 59,
        33, 41, 48, 56, 32, 39, 46, 53, 30, 37, 43, 50, 29, 35, 41, 48,
        27, 33, 39, 45, 26, 31, 37, 43, 24, 30, 35, 41, 23, 28, 33, 39,
        22, 27, 32, 37, 21, 26, 30, 35, 20, 24, 29, 33, 19, 23, 27, 31,
        18, 22, 26, 30, 17, 21, 25, 28, 16, 20, 23, 27, 15, 19, 22, 25,
        14, 18, 21, 24, 14, 17, 20, 23, 13, 16, 19, 22, 12, 15, 18, 21,
        12, 14, 17, 20, 11, 14, 16, 19, 11, 13, 15, 18, 10, 12, 15, 17,
        10, 12, 14, 16, 9, 11, 13, 15, 9, 11, 12, 14, 8, 10, 12, 14,
        8, 9, 11, 13, 7, 9, 11, 12, 7, 9, 10, 12, 7, 8, 10, 11,
        6, 8, 9, 11, 6, 7, 9, 10, 6, 7, 8, 9, 2, 2, 2, 2,
    ];

    /// <summary><c>transIdxLps</c> (Table 9-53).</summary>
    public static ReadOnlySpan<byte> TransIdxLps =>
    [
        0, 0, 1, 2, 2, 4, 4, 5, 6, 7, 8, 9, 9, 11, 11, 12,
        13, 13, 15, 15, 16, 16, 18, 18, 19, 19, 21, 21, 22, 22, 23, 24,
        24, 25, 26, 26, 27, 27, 28, 29, 29, 30, 30, 30, 31, 32, 32, 33,
        33, 33, 34, 34, 35, 35, 35, 36, 36, 36, 37, 37, 37, 38, 38, 63,
    ];

    /// <summary><c>transIdxMps</c> (Table 9-53).</summary>
    public static ReadOnlySpan<byte> TransIdxMps =>
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
        49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 62, 63,
    ];

    /// <summary><c>ctxIdxMap[ i ]</c> (Table 9-50): the context of <c>sig_coeff_flag</c> in a 4x4 transform block.</summary>
    public static ReadOnlySpan<byte> CtxIdxMap => [0, 1, 4, 5, 2, 3, 4, 5, 6, 6, 8, 8, 7, 7, 8];
}
