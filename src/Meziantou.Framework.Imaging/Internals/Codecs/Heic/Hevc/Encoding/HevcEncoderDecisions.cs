using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the HEVC encoder searches at an effort, and how it compares two candidates: every decision of the encoder is
/// made with integers, so that a stream only depends on the samples, the settings and the version of the library.
/// </summary>
/// <remarks>
/// The effort decides the parameter sets too (the sizes of the blocks, the depth of the transform trees, the tools that
/// are enabled), which are final when the encoder is created: a picture never changes them.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct HevcEncoderDecisions
{
    // 2 ^ ( k / 3 ) for k of 0 to 2, with 16 fractional bits
    private static ReadOnlySpan<int> CubeRootsOfTwo => [65536, 82570, 104032];

    private HevcEncoderDecisions(in HevcEncoderSettings settings)
    {
        var effort = Math.Clamp(settings.Effort, 0, 9);
        var lossless = settings.Lossless;
        var sequence = !settings.StillPicture && settings.KeyFrameInterval is not 1;
        Effort = effort;
        Lossless = lossless;

        // The blocks. A lossless picture is predicted better by small blocks, whose neighbours are close; the residual of
        // a lossy one costs less in large transform blocks.
        CtbLog2Size = sequence && effort >= 3 ? 6 : 5;
        MaxIntraCbLog2Size = lossless
            ? (effort is 0 ? 3 : effort <= 3 ? 4 : 5)
            : (effort <= 1 ? 4 : 5);
        MinCbLog2Size = !lossless && effort is 0 ? 4 : 3;
        MaxInterCbLog2Size = Math.Min(CtbLog2Size, effort <= 1 ? 4 : effort <= 2 ? 5 : 6);
        IntraNxN = effort >= 4;
        MaxTransformHierarchyDepthIntra = effort <= 2 ? (lossless ? 1 : 0) : effort <= 5 ? 1 : effort <= 7 ? 2 : 3;
        MaxTransformHierarchyDepthInter = effort <= 2 ? (lossless ? 1 : 0) : effort <= 5 ? 1 : effort <= 7 ? 2 : 3;

        // The intra prediction modes: a first choice with the difference between the prediction and the source, at one
        // angular mode out of IntraModeStep, then the real cost of the IntraCandidates best ones
        IntraModeStep = effort <= 1 ? 8 : effort <= 3 ? 4 : effort <= 6 ? 2 : 1;
        IntraModeRefinement = effort is >= 3 and <= 6;
        IntraCandidates = effort switch
        {
            <= 1 => 1,
            <= 3 => 2,
            <= 5 => 3,
            6 => 4,
            7 => 6,
            8 => 10,
            _ => 35,
        };
        ChromaCandidates = effort <= 2 ? 1 : effort <= 4 ? 3 : 5;

        // The residual
        CodedBlockDecision = !lossless && effort >= 2;
        SignDataHiding = !lossless && effort >= 4;
        TransformSkip = !lossless && effort >= 6;
        SampleAdaptiveOffset = !lossless && effort >= 3;
        SampleAdaptiveOffsetBands = effort >= 5;

        // The pictures that are predicted from the previous one
        MergeCandidates = effort <= 1 ? 2 : effort <= 4 ? 3 : 5;
        MotionSearchRange = effort <= 1 ? 8 : effort <= 4 ? 16 : effort <= 7 ? 32 : 64;
        MotionSearchRefinement = effort is 0 ? 0 : effort <= 2 ? 1 : 2;
        RectangularPartitions = effort >= 6;
        AsymmetricPartitions = effort >= 8;
        TemporalMvp = effort >= 4;
        IntraInInterPictures = effort switch
        {
            <= 1 => 1,
            <= 5 => 2,
            _ => 3,
        };

        // The quantization parameter: linear in the quality, 100 being the finest one, which is not lossless
        SliceQp = lossless ? 4 : Math.Clamp((((100 - Math.Clamp(settings.Quality, 0, 100)) * 51) + 50) / 100, 0, 51);
        var qP = SliceQp + (6 * (settings.BitDepth - 8));
        Lambda = lossless ? 0 : GetLambda(qP, LambdaFactor);
        MotionLambda = lossless ? HevcCost.Scale : Math.Max(1, SquareRoot(Lambda << 8));
        QuantizerRoundingIntra = 85;
        QuantizerRoundingInter = 43;
    }

    // The Lagrange multiplier of a quantization parameter: the squared error that a bit is worth doubles every three
    // steps. LambdaFactor / 256 is its value for a qP of 12.
    private const int LambdaFactor = 146;

    public int Effort { get; }

    public bool Lossless { get; }

    /// <summary>Gets the base 2 logarithm of the size of the coding tree blocks.</summary>
    public int CtbLog2Size { get; }

    /// <summary>Gets the base 2 logarithm of the size of the largest coding block coded in intra prediction mode that is searched.</summary>
    public int MaxIntraCbLog2Size { get; }

    /// <summary>Gets the base 2 logarithm of the size of the largest coding block coded in inter prediction mode that is searched.</summary>
    public int MaxInterCbLog2Size { get; }

    /// <summary>Gets the base 2 logarithm of the size of the smallest coding block that is searched where a larger one fits.</summary>
    public int MinCbLog2Size { get; }

    /// <summary>Gets a value indicating whether the four prediction blocks of a coding unit of the minimum size are searched.</summary>
    public bool IntraNxN { get; }

    public int MaxTransformHierarchyDepthIntra { get; }

    public int MaxTransformHierarchyDepthInter { get; }

    /// <summary>Gets the distance between the angular modes of the first choice.</summary>
    public int IntraModeStep { get; }

    /// <summary>Gets a value indicating whether the angular modes around the best one of the first choice are tried.</summary>
    public bool IntraModeRefinement { get; }

    /// <summary>Gets the number of intra prediction modes whose real cost is computed.</summary>
    public int IntraCandidates { get; }

    /// <summary>Gets the number of values of <c>intra_chroma_pred_mode</c> that are tried: the mode of the luma block first.</summary>
    public int ChromaCandidates { get; }

    /// <summary>Gets a value indicating whether a block whose levels cost more than they repair is coded without residual.</summary>
    public bool CodedBlockDecision { get; }

    public bool SignDataHiding { get; }

    public bool TransformSkip { get; }

    public bool SampleAdaptiveOffset { get; }

    /// <summary>Gets a value indicating whether the band offsets are searched, in addition to the edge offsets.</summary>
    public bool SampleAdaptiveOffsetBands { get; }

    public int MergeCandidates { get; }

    /// <summary>Gets the distance, in samples, that the motion search goes from its starting points.</summary>
    public int MotionSearchRange { get; }

    /// <summary>Gets the precision of the motion vectors: 0 for whole samples, 1 for half samples, 2 for quarter samples.</summary>
    public int MotionSearchRefinement { get; }

    public bool RectangularPartitions { get; }

    public bool AsymmetricPartitions { get; }

    public bool TemporalMvp { get; }

    /// <summary>
    /// Gets how the coding units of a picture predicted from another one are tried in intra prediction mode: 1 when no
    /// inter candidate is good, 2 for the blocks of at most 16 samples as well, 3 always.
    /// </summary>
    public int IntraInInterPictures { get; }

    /// <summary>Gets <c>SliceQpY</c> of every picture.</summary>
    public int SliceQp { get; }

    /// <summary>
    /// Gets the Lagrange multiplier of the squared error: the distortion that a bit is worth, with 8 fractional bits; 0
    /// for a lossless stream, whose candidates only differ by their size.
    /// </summary>
    public long Lambda { get; }

    /// <summary>
    /// Gets the multiplier of a sum of absolute differences, with 8 fractional bits: the square root of the one of the
    /// squared error.
    /// </summary>
    public long MotionLambda { get; }

    public int QuantizerRoundingIntra { get; }

    public int QuantizerRoundingInter { get; }

    public static HevcEncoderDecisions Create(in HevcEncoderSettings settings) => new(settings);

    /// <summary>
    /// Gets the cost of a candidate, which the search minimizes.
    /// </summary>
    /// <param name="distortion">The sum of the squared differences between the source and the reconstruction.</param>
    /// <param name="rate">The size of the candidate, in units of 1 / <see cref="HevcCost.Scale"/> of a bit.</param>
    public long GetCost(long distortion, long rate) => Lambda is 0 ? rate : (distortion << 8) + ((rate * Lambda) >> 8);

    /// <summary>Gets the cost of a candidate from the sum of the absolute differences between the source and its prediction.</summary>
    public long GetMotionCost(long sad, long rate) => (sad << 8) + ((rate * MotionLambda) >> 8);

    // The largest integer whose square is at most value
    private static long SquareRoot(long value)
    {
        var root = 0L;
        for (var bit = 1L << 31; bit is not 0; bit >>= 1)
        {
            var candidate = root | bit;
            if (candidate * candidate <= value)
            {
                root = candidate;
            }
        }

        return root;
    }

    // factor * 2 ^ ( ( qP - 12 ) / 3 ), with 8 fractional bits when factor has 8
    private static long GetLambda(int qP, int factor)
    {
        var exponent = qP - 12;
        var quotient = Math.DivRem(exponent + 300, 3, out var remainder) - 100;
        var value = (long)factor * CubeRootsOfTwo[remainder];
        var shift = 16 - quotient;
        return shift >= 0 ? Math.Max(1, (value + ((1L << shift) >> 1)) >> shift) : value << -shift;
    }
}
