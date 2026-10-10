namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The colorimetry of the ICC profile connection space (ICC.1:2022 section 6.3 and annex A): the D50 illuminant and the
/// CIE 1976 L*a*b* functions.
/// </summary>
internal static class IccColorimetry
{
    /// <summary>The X tristimulus value of the PCS illuminant (D50, Y = 1).</summary>
    public const double D50X = 0.9642;

    /// <summary>The Y tristimulus value of the PCS illuminant.</summary>
    public const double D50Y = 1.0;

    /// <summary>The Z tristimulus value of the PCS illuminant.</summary>
    public const double D50Z = 0.8249;

    private const double Delta = 6.0 / 29;

    /// <summary>The CIELAB function: the cube root above (6/29)^3, its tangent below (which extends to negative values).</summary>
    public static double LabFunction(double t)
        => t > Delta * Delta * Delta ? Math.Cbrt(t) : (t / (3 * Delta * Delta)) + (4.0 / 29);

    /// <summary>The inverse of <see cref="LabFunction"/>.</summary>
    public static double LabInverse(double f)
        => f > Delta ? f * f * f : 3 * Delta * Delta * (f - (4.0 / 29));
}
