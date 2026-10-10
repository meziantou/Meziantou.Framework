namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The film grain parameters of a frame with <c>apply_grain</c> equal to 1 (AV1 specification, section 5.9.30). Immutable once
/// parsed: a frame that inherits the parameters of a reference frame shares the arrays and only replaces the seed.
/// </summary>
internal sealed class Av1FilmGrainParameters
{
    public const int MaxLumaPoints = 14;
    public const int MaxChromaPoints = 10;

    public int GrainSeed { get; set; }

    public int NumYPoints { get; init; }

    /// <summary>Gets <c>point_y_value</c> and <c>point_y_scaling</c>: <see cref="NumYPoints"/> entries each.</summary>
    public byte[] PointYValue { get; init; } = [];

    public byte[] PointYScaling { get; init; } = [];

    public bool ChromaScalingFromLuma { get; init; }

    public int NumCbPoints { get; init; }

    public byte[] PointCbValue { get; init; } = [];

    public byte[] PointCbScaling { get; init; } = [];

    public int NumCrPoints { get; init; }

    public byte[] PointCrValue { get; init; } = [];

    public byte[] PointCrScaling { get; init; } = [];

    /// <summary>Gets <c>grain_scaling_minus_8 + 8</c>: the shift applied to the scaled grain.</summary>
    public int GrainScaling { get; init; }

    public int ArCoeffLag { get; init; }

    /// <summary>Gets the luma auto-regression coefficients (<c>ar_coeffs_y_plus_128 - 128</c>).</summary>
    public sbyte[] ArCoeffsY { get; init; } = [];

    public sbyte[] ArCoeffsCb { get; init; } = [];

    public sbyte[] ArCoeffsCr { get; init; } = [];

    /// <summary>Gets <c>ar_coeff_shift_minus_6 + 6</c>.</summary>
    public int ArCoeffShift { get; init; }

    public int GrainScaleShift { get; init; }

    public int CbMult { get; init; }

    public int CbLumaMult { get; init; }

    public int CbOffset { get; init; }

    public int CrMult { get; init; }

    public int CrLumaMult { get; init; }

    public int CrOffset { get; init; }

    public bool OverlapFlag { get; init; }

    public bool ClipToRestrictedRange { get; init; }

    /// <summary>Returns the parameters of a frame that inherits these parameters with its own seed (<c>update_grain</c> equal to 0).</summary>
    public Av1FilmGrainParameters WithSeed(int grainSeed)
    {
        var clone = (Av1FilmGrainParameters)MemberwiseClone();
        clone.GrainSeed = grainSeed;
        return clone;
    }
}
