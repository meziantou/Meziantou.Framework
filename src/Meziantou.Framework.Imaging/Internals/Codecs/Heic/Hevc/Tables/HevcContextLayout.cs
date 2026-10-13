namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The position of the context variables of each syntax element in the context table of a slice (Rec. ITU-T H.265,
/// Table 9-4): the first context of the syntax element, to which <c>ctxInc</c> is added (Table 9-48). The layout is the
/// same for the three values of <c>initType</c>; <see cref="HevcCabacTables"/> has the initial values in this order.
/// </summary>
internal static class HevcContextLayout
{
    public const int SaoMergeFlag = 0;
    public const int SaoTypeIdx = 1;
    public const int SplitCuFlag = 2;
    public const int CuTransquantBypassFlag = 5;
    public const int CuSkipFlag = 6;
    public const int PredModeFlag = 9;
    public const int PartMode = 10;
    public const int PrevIntraLumaPredFlag = 14;
    public const int IntraChromaPredMode = 15;
    public const int RqtRootCbf = 16;
    public const int MergeFlag = 17;
    public const int MergeIdx = 18;
    public const int InterPredIdc = 19;
    public const int RefIdx = 24;
    public const int MvpFlag = 26;
    public const int SplitTransformFlag = 27;
    public const int CbfLuma = 30;
    public const int CbfChroma = 32;
    public const int AbsMvdGreater0Flag = 37;
    public const int AbsMvdGreater1Flag = 38;
    public const int CuQpDeltaAbs = 39;
    public const int TransformSkipFlag = 41;
    public const int LastSigCoeffXPrefix = 43;
    public const int LastSigCoeffYPrefix = 61;
    public const int CodedSubBlockFlag = 79;
    public const int SigCoeffFlag = 83;
    public const int CoeffAbsLevelGreater1Flag = 127;
    public const int CoeffAbsLevelGreater2Flag = 151;
    public const int ExplicitRdpcmFlag = 157;
    public const int ExplicitRdpcmDirFlag = 159;
    public const int CuChromaQpOffsetFlag = 161;
    public const int CuChromaQpOffsetIdx = 162;
    public const int Log2ResScaleAbsPlus1 = 163;
    public const int ResScaleSignFlag = 171;

    /// <summary>The number of context variables.</summary>
    public const int Count = 173;
}
