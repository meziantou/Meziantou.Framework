namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The tools an encoding uses, set by the effort (<see cref="Create"/>).</summary>
/// <remarks>
/// The main image of a lossless frame and the alpha plane of a lossy frame have their own tables. Measured on photographs,
/// screenshots, text and graphics: the transforms make most of the size of a photograph, whose pixels rarely repeat (the
/// backward references of a greedy hash chain search make it larger, and a parse with a cost model rarely makes it
/// smaller); the backward references make most of the size of flat images, and their parse with a cost model makes text
/// 10 to 30 % smaller.
/// </remarks>
internal sealed record Vp8LEncoderTools
{
    /// <summary>Gets how the predictor modes are chosen.</summary>
    public required Vp8LPredictorSearch Predictor { get; init; }

    /// <summary>Gets how the color transform multipliers are chosen.</summary>
    public required Vp8LColorSearch Color { get; init; }

    /// <summary>Gets a value indicating whether subtract green is only applied (before the predictor) when it makes the red and blue residuals cheaper.</summary>
    public bool SubtractGreenWhenCheaper { get; init; }

    /// <summary>Gets the number of hash chain entries the first parse searches at a pixel (0: the left and top neighbors only).</summary>
    public required int MatcherChainDepth { get; init; }

    /// <summary>Gets a value indicating whether the first parse looks one pixel ahead before taking a match.</summary>
    public required bool Lazy { get; init; }

    /// <summary>Gets the number of parses with a cost model after the first one.</summary>
    public required int CostModelRounds { get; init; }

    /// <summary>Gets the number of hash chain entries the parses with a cost model search at a pixel.</summary>
    public int CostModelChainDepth { get; init; }

    /// <summary>
    /// Gets a value indicating whether the first cost model prices the literals as if every pixel were one (the length and
    /// distance prices still come from the first parse).
    /// </summary>
    public bool SeedLiterals { get; init; }

    /// <summary>
    /// Gets a value indicating whether a parse with a cost model is kept by the estimated size of its prefix codes (at least
    /// one bit per symbol) instead of the entropy of its symbols.
    /// </summary>
    public bool PrefixCodeEstimates { get; init; }

    /// <summary>Gets a value indicating whether a match of 128 pixels or more is taken as it is by a parse with a cost model.</summary>
    public bool CommitLongMatches { get; init; } = true;

    /// <summary>Gets the color cache sizes (bits, 0 for none) tried on the main image.</summary>
    public required int[] CacheBits { get; init; }

    /// <summary>Gets the entropy image tile size bits tried (empty: a single prefix code group).</summary>
    public required int[] EntropyImageBits { get; init; }

    /// <summary>Gets the most groups of the first clustering of the entropy image.</summary>
    public int MaxInitialGroups { get; init; }

    /// <summary>Gets the number of passes that move the tiles of the entropy image to their best group.</summary>
    public int RefinePasses { get; init; }

    /// <summary>Gets the tools of an effort.</summary>
    /// <param name="effort">The effort, 0 to 9.</param>
    /// <param name="alphaPlane">Whether the image is the alpha plane of a lossy frame (<see cref="Vp8LEncoder.EncodeImageStream"/>).</param>
    /// <param name="flat">Whether at least half the pixels repeat their left or top neighbor (see <see cref="Vp8LConfiguration.Flat"/>).</param>
    public static Vp8LEncoderTools Create(int effort, bool alphaPlane, bool flat)
    {
        return alphaPlane ? CreateAlphaPlane(effort) : CreateMainImage(effort, flat);
    }

    // The main image: the transforms gain the most per unit of time, then the backward references of flat images
    private static Vp8LEncoderTools CreateMainImage(int effort, bool flat)
    {
        var flatRounds = effort switch
        {
            < 4 => 0,
            < 9 => 1,
            _ => 2,
        };

        var photoRounds = effort switch
        {
            < 6 => 0,
            < 9 => 1,
            _ => 2,
        };

        return new Vp8LEncoderTools
        {
            Predictor = effort switch
            {
                0 => Vp8LPredictorSearch.Select,
                1 => Vp8LPredictorSearch.FourModes,
                _ => Vp8LPredictorSearch.AllModesByCost,
            },
            Color = effort >= 3 ? Vp8LColorSearch.Cost : Vp8LColorSearch.None,
            SubtractGreenWhenCheaper = true,
            MatcherChainDepth = !flat ? 0 : effort switch
            {
                0 => 4,
                <= 2 => 8,
                <= 4 => 16,
                5 => 32,
                6 => 64,
                7 => 128,
                8 => 256,
                _ => 512,
            },
            Lazy = effort >= 3,
            CostModelRounds = flat ? flatRounds : photoRounds,
            CostModelChainDepth = flat
                ? effort switch
                {
                    4 => 16,
                    5 => 64,
                    6 or 7 => 128,
                    8 => 256,
                    _ => 512,
                }
                : effort switch
                {
                    < 9 => 16,
                    _ => 32,
                },
            SeedLiterals = !flat,
            PrefixCodeEstimates = true,
            CommitLongMatches = effort < 9,
            CacheBits = effort switch
            {
                < 3 => [0],
                < 6 => [0, 10],
                _ => [0, 4, 6, 8, 10],
            },
            EntropyImageBits = effort switch
            {
                < 3 => [],
                < 4 => [5],
                < 9 => [4, 5],
                _ => [3, 4, 5, 6],
            },
            MaxInitialGroups = effort switch
            {
                < 4 => 27,
                < 9 => 64,
                _ => 125,
            },
            RefinePasses = effort switch
            {
                < 4 => 1,
                < 9 => 2,
                _ => 4,
            },
        };
    }

    // The alpha plane of a lossy frame keeps the tools it was tuned with (its output must not change)
    private static Vp8LEncoderTools CreateAlphaPlane(int effort)
    {
        return new Vp8LEncoderTools
        {
            Predictor = effort switch
            {
                0 => Vp8LPredictorSearch.Select,
                < 3 => Vp8LPredictorSearch.FourModes,
                _ => Vp8LPredictorSearch.AllModes,
            },
            Color = effort >= 3 ? Vp8LColorSearch.Entropy : Vp8LColorSearch.None,
            MatcherChainDepth = effort switch
            {
                0 => 4,
                <= 2 => 8,
                <= 4 => 16,
                <= 6 => 32,
                <= 8 => 128,
                _ => 512,
            },
            Lazy = effort >= 3,
            CostModelRounds = effort switch
            {
                < 5 => 0,
                < 9 => 1,
                _ => 2,
            },
            CostModelChainDepth = effort switch
            {
                <= 6 => 64,
                <= 8 => 128,
                _ => 512,
            },
            CommitLongMatches = effort < 9,
            CacheBits = effort switch
            {
                < 3 => [0],
                < 6 => [0, 10],
                _ => [0, 4, 6, 8, 10],
            },
            EntropyImageBits = effort switch
            {
                < 3 => [],
                < 5 => [5],
                < 9 => [4, 5],
                _ => [3, 4, 5, 6],
            },
            MaxInitialGroups = effort switch
            {
                < 5 => 27,
                < 9 => 64,
                _ => 125,
            },
            RefinePasses = effort switch
            {
                < 5 => 1,
                < 9 => 2,
                _ => 4,
            },
        };
    }
}
