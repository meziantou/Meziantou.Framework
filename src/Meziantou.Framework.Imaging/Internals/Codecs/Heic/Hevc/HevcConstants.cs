namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The symbolic constants of Rec. ITU-T H.265 that the decoder names.</summary>
internal static class HevcConstants
{
    // Table 7-1 - NAL unit type codes and NAL unit type classes
    public const int NalTrailN = 0;
    public const int NalTrailR = 1;
    public const int NalTsaN = 2;
    public const int NalTsaR = 3;
    public const int NalStsaN = 4;
    public const int NalStsaR = 5;
    public const int NalRadlN = 6;
    public const int NalRadlR = 7;
    public const int NalRaslN = 8;
    public const int NalRaslR = 9;
    public const int NalRsvVclR15 = 15;
    public const int NalBlaWLp = 16;
    public const int NalBlaWRadl = 17;
    public const int NalBlaNLp = 18;
    public const int NalIdrWRadl = 19;
    public const int NalIdrNLp = 20;
    public const int NalCra = 21;
    public const int NalRsvIrapVcl23 = 23;
    public const int NalRsvVcl31 = 31;
    public const int NalVps = 32;
    public const int NalSps = 33;
    public const int NalPps = 34;
    public const int NalAud = 35;
    public const int NalEos = 36;
    public const int NalEob = 37;
    public const int NalFd = 38;
    public const int NalPrefixSei = 39;
    public const int NalSuffixSei = 40;

    // Table 7-7 - Name association to slice_type
    public const int SliceB = 0;
    public const int SliceP = 1;
    public const int SliceI = 2;

    // CuPredMode
    public const int ModeInter = 0;
    public const int ModeIntra = 1;

    // PartMode (Table 7-10)
    public const int Part2Nx2N = 0;
    public const int Part2NxN = 1;
    public const int PartNx2N = 2;
    public const int PartNxN = 3;
    public const int Part2NxnU = 4;
    public const int Part2NxnD = 5;
    public const int PartnLx2N = 6;
    public const int PartnRx2N = 7;

    // Table 8-1 - Specification of intra prediction mode and associated names
    public const int IntraPlanar = 0;
    public const int IntraDc = 1;
    public const int IntraAngular10 = 10;
    public const int IntraAngular26 = 26;
    public const int IntraAngular34 = 34;

    // SEI payload types (clause D.2.1)
    public const int SeiDecodedPictureHash = 132;
    public const int SeiMasteringDisplayColourVolume = 137;
    public const int SeiContentLightLevelInfo = 144;
    public const int SeiAlphaChannelInfo = 165;

    /// <summary>The number of video parameter sets (<c>vps_video_parameter_set_id</c> is 4 bits).</summary>
    public const int MaxVpsCount = 16;

    /// <summary>The number of sequence parameter sets (<c>sps_seq_parameter_set_id</c> is in the range 0 to 15).</summary>
    public const int MaxSpsCount = 16;

    /// <summary>The number of picture parameter sets (<c>pps_pic_parameter_set_id</c> is in the range 0 to 63).</summary>
    public const int MaxPpsCount = 64;

    /// <summary>The largest value of <c>sps_max_dec_pic_buffering_minus1</c> plus 1 (clause A.4.2: <c>MaxDpbSize</c> is at most 16).</summary>
    public const int MaxDpbSize = 16;

    /// <summary>The largest number of pictures of a short-term reference picture set.</summary>
    public const int MaxRefPicSetPictures = 16;

    /// <summary>The largest number of short-term reference picture sets of a sequence parameter set.</summary>
    public const int MaxShortTermRefPicSets = 64;

    /// <summary>The largest number of long-term reference pictures of a sequence parameter set.</summary>
    public const int MaxLongTermRefPicsSps = 32;

    /// <summary>The largest number of tile columns (Table A.8, level 6.2 and above).</summary>
    public const int MaxTileColumns = 20;

    /// <summary>The largest number of tile rows (Table A.8, level 6.2 and above).</summary>
    public const int MaxTileRows = 22;

    /// <summary>The largest bit depth of the samples that the decoder supports.</summary>
    public const int MaxBitDepth = 12;
}
