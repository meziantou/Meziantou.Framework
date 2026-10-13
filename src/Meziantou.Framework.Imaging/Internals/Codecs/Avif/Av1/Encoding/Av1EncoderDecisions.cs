using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The limits of the search of the encoder for a frame: which candidates are evaluated, and how a rate compares with a
/// distortion. A level is the size of a square block, from 4x4 (0) to 128x128 (5).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Av1EncoderDecisions
{
    /// <summary>The largest square block that is coded without being split.</summary>
    public int MaxBlockLevel;

    /// <summary>The smallest square block that the search reaches by splitting.</summary>
    public int MinBlockLevel;

    /// <summary>Whether the partitions into two rectangles are evaluated.</summary>
    public bool RectangularPartitions;

    /// <summary>Whether the partitions into a half and two quarters, and into four strips, are evaluated.</summary>
    public bool ExtendedPartitions;

    /// <summary>The number of luma modes that are encoded, after all of them are ranked by their prediction error.</summary>
    public int LumaModeCandidates;

    /// <summary>The number of chroma modes that are encoded.</summary>
    public int ChromaModeCandidates;

    /// <summary>Whether the angle deltas of the best directional mode are evaluated.</summary>
    public bool AngleDeltas;

    /// <summary>Whether the recursive filter intra modes are evaluated.</summary>
    public bool FilterIntra;

    /// <summary>
    /// Whether the search evaluates the candidates that are unlikely to be kept: every angle delta and every recursive
    /// filter intra mode rather than the one whose prediction is the closest to the block, the partitions of a block that
    /// codes no coefficient, and the partitions into two rectangles of a block that costs much less, or much more, split in
    /// four than as a whole.
    /// </summary>
    public bool ExhaustiveSearch;

    /// <summary>
    /// Whether a block of a lossless frame of screen content may be a copy of a part of the frame that is already coded
    /// (intra block copy). The fastest effort does not look for copies: its blocks of 16x16 samples seldom are one.
    /// </summary>
    public bool IntraBlockCopy;

    /// <summary>Whether the prediction of the chroma samples from the luma samples of the block is evaluated.</summary>
    public bool ChromaFromLuma;

    /// <summary>
    /// The number of transform types that are evaluated for a luma transform block, among the ones of its transform set: 1
    /// for a DCT in both directions.
    /// </summary>
    public int TransformTypeCandidates;

    /// <summary>
    /// The number of times the largest transform of a block that is not inter may be split: 0 when every block has the
    /// largest transform of its size, which is then not coded.
    /// </summary>
    public int TransformSizeDepth;

    /// <summary>
    /// How the parameters of the in-loop filters are searched: 0 for a few values, 1 for every value from the middle one by
    /// halving a step, 2 for every value.
    /// </summary>
    public int FilterSearch;

    /// <summary>
    /// The largest step of the motion search of a block of an inter frame, in samples: the search moves the motion vector
    /// by this step while the prediction improves, then by half of it, down to one sample.
    /// </summary>
    public int MotionStep;

    /// <summary>Whether the motion search refines a motion vector to a half then to a quarter of a sample.</summary>
    public bool SubpelMotion;

    /// <summary>The number of motion vectors of the stack that the NEARMV mode is evaluated with: 1 to 3.</summary>
    public int NearMvCandidates;

    /// <summary>
    /// The Lagrange multiplier of the rate: the squared error that a bit is worth, with 10 fractional bits; 0 for a lossless
    /// frame, which has no distortion.
    /// </summary>
    public long Lambda;

    /// <summary>
    /// The Lagrange multiplier of the motion search: the sum of absolute differences that a bit of a motion vector is worth,
    /// with 8 fractional bits.
    /// </summary>
    public long MotionLambda;

    /// <summary>The rounding offset of the quantizer, in units of 1/256 of a quantizer step (128 rounds to nearest).</summary>
    public int QuantizerRounding;

    /// <summary>
    /// Returns the cost of a candidate: its rate (in units of 1/256 bit) for a lossless frame; otherwise its squared error
    /// plus <see cref="Lambda"/> times its rate, in units of 1/256 of a squared sample difference.
    /// </summary>
    public readonly long GetCost(long distortion, long rate) => Lambda is 0 ? rate : (distortion << 8) + ((rate * Lambda) >> 10);

    /// <summary>Returns the limits of an effort, 0 (fastest) to 9 (smallest output).</summary>
    public static Av1EncoderDecisions Create(int effort)
    {
        return effort switch
        {
            <= 0 => new() { MaxBlockLevel = 2, MinBlockLevel = 2, LumaModeCandidates = 1, ChromaModeCandidates = 1, MotionStep = 4, NearMvCandidates = 1, TransformTypeCandidates = 1 },
            1 => new() { IntraBlockCopy = true, MaxBlockLevel = 3, MinBlockLevel = 1, LumaModeCandidates = 1, ChromaModeCandidates = 1, MotionStep = 8, NearMvCandidates = 1, TransformTypeCandidates = 1 },
            2 => new() { IntraBlockCopy = true, MaxBlockLevel = 3, MinBlockLevel = 1, LumaModeCandidates = 2, ChromaModeCandidates = 1, MotionStep = 8, SubpelMotion = true, NearMvCandidates = 1, TransformTypeCandidates = 2 },
            3 => new() { IntraBlockCopy = true, MaxBlockLevel = 4, MinBlockLevel = 1, LumaModeCandidates = 2, ChromaModeCandidates = 2, AngleDeltas = true, MotionStep = 8, SubpelMotion = true, NearMvCandidates = 1, TransformTypeCandidates = 2, ChromaFromLuma = true, TransformSizeDepth = 1, FilterSearch = 1 },
            4 => new() { IntraBlockCopy = true, MaxBlockLevel = 4, MinBlockLevel = 1, LumaModeCandidates = 2, ChromaModeCandidates = 2, AngleDeltas = true, RectangularPartitions = true, MotionStep = 8, SubpelMotion = true, NearMvCandidates = 1, TransformTypeCandidates = 2, ChromaFromLuma = true, TransformSizeDepth = 1, FilterSearch = 1 },
            5 => new() { IntraBlockCopy = true, MaxBlockLevel = 4, MinBlockLevel = 0, LumaModeCandidates = 3, ChromaModeCandidates = 2, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true, MotionStep = 16, SubpelMotion = true, NearMvCandidates = 2, TransformTypeCandidates = 4, ChromaFromLuma = true, TransformSizeDepth = 1, FilterSearch = 1 },
            6 => new() { IntraBlockCopy = true, MaxBlockLevel = 4, MinBlockLevel = 0, LumaModeCandidates = 4, ChromaModeCandidates = 3, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true, MotionStep = 16, SubpelMotion = true, NearMvCandidates = 2, TransformTypeCandidates = 4, ChromaFromLuma = true, TransformSizeDepth = 2, FilterSearch = 1 },
            7 => new() { IntraBlockCopy = true, MaxBlockLevel = 5, MinBlockLevel = 0, LumaModeCandidates = 6, ChromaModeCandidates = 4, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true, MotionStep = 16, SubpelMotion = true, NearMvCandidates = 3, TransformTypeCandidates = 7, ChromaFromLuma = true, TransformSizeDepth = 2, FilterSearch = 1 },
            8 => new() { IntraBlockCopy = true, MaxBlockLevel = 5, MinBlockLevel = 0, LumaModeCandidates = 13, ChromaModeCandidates = 6, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true, MotionStep = 32, SubpelMotion = true, NearMvCandidates = 3, TransformTypeCandidates = 16, ChromaFromLuma = true, TransformSizeDepth = 2, ExhaustiveSearch = true, FilterSearch = 2 },
            _ => new() { IntraBlockCopy = true, MaxBlockLevel = 5, MinBlockLevel = 0, LumaModeCandidates = 13, ChromaModeCandidates = 13, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true, MotionStep = 32, SubpelMotion = true, NearMvCandidates = 3, TransformTypeCandidates = 16, ChromaFromLuma = true, TransformSizeDepth = 2, ExhaustiveSearch = true, FilterSearch = 2, ExtendedPartitions = true },
        };
    }
}
