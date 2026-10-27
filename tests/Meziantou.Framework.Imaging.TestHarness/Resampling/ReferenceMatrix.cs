namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>
/// A projective transformation of the plane in <see cref="decimal"/> arithmetic, independent of the library: a point
/// <c>(x, y)</c> becomes <c>(X / W, Y / W)</c> with <c>X = x M11 + y M21 + M31</c>, <c>Y = x M12 + y M22 + M32</c> and
/// <c>W = x M13 + y M23 + M33</c>.
/// </summary>
/// <param name="M11">The factor of x in X.</param>
/// <param name="M12">The factor of x in Y.</param>
/// <param name="M13">The factor of x in W.</param>
/// <param name="M21">The factor of y in X.</param>
/// <param name="M22">The factor of y in Y.</param>
/// <param name="M23">The factor of y in W.</param>
/// <param name="M31">The constant of X.</param>
/// <param name="M32">The constant of Y.</param>
/// <param name="M33">The constant of W.</param>
public readonly record struct ReferenceMatrix(decimal M11, decimal M12, decimal M13, decimal M21, decimal M22, decimal M23, decimal M31, decimal M32, decimal M33)
{
    /// <summary>Gets a value indicating whether <c>W</c> is the same positive number everywhere.</summary>
    public bool IsAffine => M13 == 0 && M23 == 0 && M33 > 0;

    /// <summary>Creates an affine transformation.</summary>
    /// <param name="m11">The factor of x in X.</param>
    /// <param name="m12">The factor of x in Y.</param>
    /// <param name="m21">The factor of y in X.</param>
    /// <param name="m22">The factor of y in Y.</param>
    /// <param name="offsetX">The horizontal translation.</param>
    /// <param name="offsetY">The vertical translation.</param>
    /// <returns>The transformation.</returns>
    public static ReferenceMatrix CreateAffine(decimal m11, decimal m12, decimal m21, decimal m22, decimal offsetX, decimal offsetY) => new(m11, m12, 0, m21, m22, 0, offsetX, offsetY, 1);

    /// <summary>Creates a translation.</summary>
    /// <param name="dx">The horizontal translation.</param>
    /// <param name="dy">The vertical translation.</param>
    /// <returns>The transformation.</returns>
    public static ReferenceMatrix CreateTranslation(decimal dx, decimal dy) => CreateAffine(1, 0, 0, 1, dx, dy);

    /// <summary>Creates a clockwise rotation around the origin (Y grows downward).</summary>
    /// <param name="degrees">The angle, in degrees.</param>
    /// <returns>The transformation.</returns>
    public static ReferenceMatrix CreateRotation(decimal degrees)
    {
        // The series of the sine gives 1 - 1e-28 for a quarter turn: its exact values are known
        var quarterTurns = degrees / 90;
        if (quarterTurns == decimal.Floor(quarterTurns))
        {
            var turn = (int)(((quarterTurns % 4) + 4) % 4);
            ReadOnlySpan<int> sines = [0, 1, 0, -1];
            return CreateAffine(sines[(turn + 1) % 4], sines[turn], -sines[turn], sines[(turn + 1) % 4], 0, 0);
        }

        var sin = DecimalMath.SinPi(degrees / 180);
        var cos = DecimalMath.SinPi((degrees / 180) + 0.5m);
        return CreateAffine(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Creates a skew: <c>x + y tan(degreesX)</c>, <c>y + x tan(degreesY)</c>.</summary>
    /// <param name="degreesX">The angle the vertical lines lean by.</param>
    /// <param name="degreesY">The angle the horizontal lines lean by.</param>
    /// <returns>The transformation.</returns>
    public static ReferenceMatrix CreateSkew(decimal degreesX, decimal degreesY) => CreateAffine(1, Tan(degreesY), Tan(degreesX), 1, 0, 0);

    /// <summary>Gets the transformation that applies this one, then <paramref name="next"/>.</summary>
    /// <param name="next">The transformation applied second.</param>
    /// <returns>The product.</returns>
    public ReferenceMatrix Then(ReferenceMatrix next) => new(
        (M11 * next.M11) + (M12 * next.M21) + (M13 * next.M31),
        (M11 * next.M12) + (M12 * next.M22) + (M13 * next.M32),
        (M11 * next.M13) + (M12 * next.M23) + (M13 * next.M33),
        (M21 * next.M11) + (M22 * next.M21) + (M23 * next.M31),
        (M21 * next.M12) + (M22 * next.M22) + (M23 * next.M32),
        (M21 * next.M13) + (M22 * next.M23) + (M23 * next.M33),
        (M31 * next.M11) + (M32 * next.M21) + (M33 * next.M31),
        (M31 * next.M12) + (M32 * next.M22) + (M33 * next.M32),
        (M31 * next.M13) + (M32 * next.M23) + (M33 * next.M33));

    /// <summary>Gets the inverse (the adjugate divided by the determinant), which keeps the sign of <c>W</c>.</summary>
    /// <returns>The inverse.</returns>
    public ReferenceMatrix Invert()
    {
        var determinant = (M11 * ((M22 * M33) - (M23 * M32))) - (M12 * ((M21 * M33) - (M23 * M31))) + (M13 * ((M21 * M32) - (M22 * M31)));
        if (determinant == 0)
            throw new InvalidOperationException("The transformation cannot be inverted.");

        return new(
            ((M22 * M33) - (M23 * M32)) / determinant,
            ((M13 * M32) - (M12 * M33)) / determinant,
            ((M12 * M23) - (M13 * M22)) / determinant,
            ((M23 * M31) - (M21 * M33)) / determinant,
            ((M11 * M33) - (M13 * M31)) / determinant,
            ((M13 * M21) - (M11 * M23)) / determinant,
            ((M21 * M32) - (M22 * M31)) / determinant,
            ((M12 * M31) - (M11 * M32)) / determinant,
            ((M11 * M22) - (M12 * M21)) / determinant);
    }

    /// <summary>Gets the transformation with <c>W</c> brought to 1 when it is the same positive number everywhere.</summary>
    /// <returns>The normalized transformation.</returns>
    public ReferenceMatrix Normalize()
        => IsAffine && M33 != 1 ? new(M11 / M33, M12 / M33, 0, M21 / M33, M22 / M33, 0, M31 / M33, M32 / M33, 1) : this;

    /// <summary>Gets <c>W</c> at a point.</summary>
    /// <param name="x">The abscissa.</param>
    /// <param name="y">The ordinate.</param>
    /// <returns>The value.</returns>
    public decimal GetW(decimal x, decimal y) => (x * M13) + (y * M23) + M33;

    /// <summary>Transforms a point.</summary>
    /// <param name="x">The abscissa.</param>
    /// <param name="y">The ordinate.</param>
    /// <returns>The transformed point, or <see langword="null"/> when <c>W</c> is not positive there.</returns>
    public (decimal X, decimal Y)? Transform(decimal x, decimal y)
    {
        var w = GetW(x, y);
        if (w <= 0)
            return null;

        return (((x * M11) + (y * M21) + M31) / w, ((x * M12) + (y * M22) + M32) / w);
    }

    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"[{M11:G8} {M12:G8} {M13:G8}; {M21:G8} {M22:G8} {M23:G8}; {M31:G8} {M32:G8} {M33:G8}]");

    private static decimal Tan(decimal degrees) => DecimalMath.SinPi(degrees / 180) / DecimalMath.SinPi((degrees / 180) + 0.5m);
}
