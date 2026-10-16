using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A transformation of the plane that keeps straight lines straight, but not parallel lines parallel: a plane seen in
/// perspective. An <see cref="AffineTransform"/> is the projective transformation whose third column is
/// <c>(0, 0, 1)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A point <c>(x, y)</c> becomes <c>(X / W, Y / W)</c>, with <c>X = x M11 + y M21 + M31</c>,
/// <c>Y = x M12 + y M22 + M32</c> and <c>W = x M13 + y M23 + M33</c>: the convention of <see cref="AffineTransform"/>,
/// whose offsets are <c>M31</c> and <c>M32</c>. The default value is the identity.
/// </para>
/// <para>
/// The points where <c>W</c> is positive are in front of the viewer; those where it is zero or negative have no image,
/// and are not drawn: a drawing that crosses that line (the horizon of the plane) is cut there. Multiplying every
/// coefficient by the same positive number does not change the transformation.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct ProjectiveTransform : IEquatable<ProjectiveTransform>
{
    // The default value of the structure is the identity: the coefficients are only read once they were set
    private readonly bool _isSet;
    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m13;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _m23;
    private readonly double _m31;
    private readonly double _m32;
    private readonly double _m33;

    /// <summary>Initializes a new instance of the <see cref="ProjectiveTransform"/> struct from its coefficients.</summary>
    /// <param name="m11">The factor of x in X.</param>
    /// <param name="m12">The factor of x in Y.</param>
    /// <param name="m13">The factor of x in W.</param>
    /// <param name="m21">The factor of y in X.</param>
    /// <param name="m22">The factor of y in Y.</param>
    /// <param name="m23">The factor of y in W.</param>
    /// <param name="m31">The constant of X: the horizontal translation of an affine transformation.</param>
    /// <param name="m32">The constant of Y: the vertical translation of an affine transformation.</param>
    /// <param name="m33">The constant of W.</param>
    /// <exception cref="ArgumentException">A coefficient is not a finite number.</exception>
    public ProjectiveTransform(double m11, double m12, double m13, double m21, double m22, double m23, double m31, double m32, double m33)
    {
        EnsureFinite(m11, nameof(m11));
        EnsureFinite(m12, nameof(m12));
        EnsureFinite(m13, nameof(m13));
        EnsureFinite(m21, nameof(m21));
        EnsureFinite(m22, nameof(m22));
        EnsureFinite(m23, nameof(m23));
        EnsureFinite(m31, nameof(m31));
        EnsureFinite(m32, nameof(m32));
        EnsureFinite(m33, nameof(m33));
        _isSet = true;
        _m11 = m11;
        _m12 = m12;
        _m13 = m13;
        _m21 = m21;
        _m22 = m22;
        _m23 = m23;
        _m31 = m31;
        _m32 = m32;
        _m33 = m33;
    }

    /// <summary>Gets the transformation that changes nothing.</summary>
    public static ProjectiveTransform Identity => default;

    /// <summary>Gets the factor of x in X.</summary>
    public double M11 => _isSet ? _m11 : 1;

    /// <summary>Gets the factor of x in Y.</summary>
    public double M12 => _m12;

    /// <summary>Gets the factor of x in W.</summary>
    public double M13 => _m13;

    /// <summary>Gets the factor of y in X.</summary>
    public double M21 => _m21;

    /// <summary>Gets the factor of y in Y.</summary>
    public double M22 => _isSet ? _m22 : 1;

    /// <summary>Gets the factor of y in W.</summary>
    public double M23 => _m23;

    /// <summary>Gets the constant of X.</summary>
    public double M31 => _m31;

    /// <summary>Gets the constant of Y.</summary>
    public double M32 => _m32;

    /// <summary>Gets the constant of W.</summary>
    public double M33 => _isSet ? _m33 : 1;

    /// <summary>Gets a value indicating whether the transformation changes nothing.</summary>
    public bool IsIdentity => M11 == 1 && M12 == 0 && M13 == 0 && M21 == 0 && M22 == 1 && M23 == 0 && M31 == 0 && M32 == 0 && M33 == 1;

    /// <summary>
    /// Gets a value indicating whether the transformation is affine: <c>W</c> is the same positive number everywhere,
    /// so parallel lines stay parallel and every point has an image.
    /// </summary>
    public bool IsAffine => M13 == 0 && M23 == 0 && M33 > 0;

    /// <summary>Gets the determinant of the nine coefficients: zero when the transformation flattens the plane to a line or a point.</summary>
    public double Determinant
        => (M11 * ((M22 * M33) - (M23 * M32))) - (M12 * ((M21 * M33) - (M23 * M31))) + (M13 * ((M21 * M32) - (M22 * M31)));

    /// <summary>Converts an affine transformation.</summary>
    /// <param name="transform">The affine transformation.</param>
    public static implicit operator ProjectiveTransform(AffineTransform transform)
        => new(transform.M11, transform.M12, 0, transform.M21, transform.M22, 0, transform.OffsetX, transform.OffsetY, 1);

    /// <summary>Creates the transformation that maps the corners of a rectangle to the corners of a quadrilateral: the rectangle seen in perspective.</summary>
    /// <param name="source">The rectangle. Must not be empty.</param>
    /// <param name="topLeft">Where the top-left corner of the rectangle goes.</param>
    /// <param name="topRight">Where the top-right corner goes.</param>
    /// <param name="bottomRight">Where the bottom-right corner goes.</param>
    /// <param name="bottomLeft">Where the bottom-left corner goes.</param>
    /// <returns>The transformation; <c>W</c> is positive inside the quadrilateral.</returns>
    /// <exception cref="ArgumentException">A coordinate is not a finite number, the rectangle is empty, or the points are not the corners of a convex quadrilateral, in order around it.</exception>
    public static ProjectiveTransform CreateFromQuadrilateral(RectangleD source, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft)
    {
        if (!double.IsFinite(source.X) || !double.IsFinite(source.Y) || !(source.Width > 0) || !(source.Height > 0) || !double.IsFinite(source.Width) || !double.IsFinite(source.Height))
            throw new ArgumentException("The rectangle must be finite and not empty.", nameof(source));

        EnsureFinite(topLeft, nameof(topLeft));
        EnsureFinite(topRight, nameof(topRight));
        EnsureFinite(bottomRight, nameof(bottomRight));
        EnsureFinite(bottomLeft, nameof(bottomLeft));
        var turn0 = Cross(topLeft, topRight, bottomRight);
        var turn1 = Cross(topRight, bottomRight, bottomLeft);
        var turn2 = Cross(bottomRight, bottomLeft, topLeft);
        var turn3 = Cross(bottomLeft, topLeft, topRight);
        if (!((turn0 > 0 && turn1 > 0 && turn2 > 0 && turn3 > 0) || (turn0 < 0 && turn1 < 0 && turn2 < 0 && turn3 < 0)))
            throw new ArgumentException("The points must be the corners of a convex quadrilateral, in order.", nameof(topLeft));

        // From the unit square to the quadrilateral, as in "Fundamentals of Texture Mapping and Image Warping" (Heckbert, 1989)
        var dx1 = topRight.X - bottomRight.X;
        var dx2 = bottomLeft.X - bottomRight.X;
        var dx3 = topLeft.X - topRight.X + bottomRight.X - bottomLeft.X;
        var dy1 = topRight.Y - bottomRight.Y;
        var dy2 = bottomLeft.Y - bottomRight.Y;
        var dy3 = topLeft.Y - topRight.Y + bottomRight.Y - bottomLeft.Y;
        var denominator = (dx1 * dy2) - (dx2 * dy1);
        var g = ((dx3 * dy2) - (dx2 * dy3)) / denominator;
        var h = ((dx1 * dy3) - (dx3 * dy1)) / denominator;
        var square = new ProjectiveTransform(
            topRight.X - topLeft.X + (g * topRight.X),
            topRight.Y - topLeft.Y + (g * topRight.Y),
            g,
            bottomLeft.X - topLeft.X + (h * bottomLeft.X),
            bottomLeft.Y - topLeft.Y + (h * bottomLeft.Y),
            h,
            topLeft.X,
            topLeft.Y,
            1);

        // Then from the rectangle to the unit square
        var toSquare = new AffineTransform(1 / source.Width, 0, 0, 1 / source.Height, -source.X / source.Width, -source.Y / source.Height);
        return ((ProjectiveTransform)toSquare).Then(square);

        static double Cross(PointD a, PointD b, PointD c) => ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
    }

    /// <summary>Gets the transformation that applies this one first, then another one.</summary>
    /// <param name="next">The transformation applied after this one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public ProjectiveTransform Then(ProjectiveTransform next) => new(
        ((M11 * next.M11) + (M12 * next.M21)) + (M13 * next.M31),
        ((M11 * next.M12) + (M12 * next.M22)) + (M13 * next.M32),
        ((M11 * next.M13) + (M12 * next.M23)) + (M13 * next.M33),
        ((M21 * next.M11) + (M22 * next.M21)) + (M23 * next.M31),
        ((M21 * next.M12) + (M22 * next.M22)) + (M23 * next.M32),
        ((M21 * next.M13) + (M22 * next.M23)) + (M23 * next.M33),
        ((M31 * next.M11) + (M32 * next.M21)) + (M33 * next.M31),
        ((M31 * next.M12) + (M32 * next.M22)) + (M33 * next.M32),
        ((M31 * next.M13) + (M32 * next.M23)) + (M33 * next.M33));

    /// <summary>Gets the transformation that applies a first one, then a second one.</summary>
    /// <param name="first">The transformation applied first.</param>
    /// <param name="second">The transformation applied to the result of the first one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public static ProjectiveTransform Multiply(ProjectiveTransform first, ProjectiveTransform second) => first.Then(second);

    /// <summary>Gets the transformation that undoes this one.</summary>
    /// <param name="inverse">The inverse transformation, or the identity when there is none.</param>
    /// <returns><see langword="false"/> when the transformation flattens the plane to a line or a point: nothing undoes it.</returns>
    /// <remarks>The inverse keeps the sign of <c>W</c>: a point in front of the viewer comes back with a positive <c>W</c>.</remarks>
    public bool TryInvert(out ProjectiveTransform inverse)
    {
        var determinant = Determinant;
        if (determinant != 0 && double.IsFinite(determinant))
        {
            var m11 = ((M22 * M33) - (M23 * M32)) / determinant;
            var m12 = ((M13 * M32) - (M12 * M33)) / determinant;
            var m13 = ((M12 * M23) - (M13 * M22)) / determinant;
            var m21 = ((M23 * M31) - (M21 * M33)) / determinant;
            var m22 = ((M11 * M33) - (M13 * M31)) / determinant;
            var m23 = ((M13 * M21) - (M11 * M23)) / determinant;
            var m31 = ((M21 * M32) - (M22 * M31)) / determinant;
            var m32 = ((M12 * M31) - (M11 * M32)) / determinant;
            var m33 = ((M11 * M22) - (M12 * M21)) / determinant;
            if (double.IsFinite(m11) && double.IsFinite(m12) && double.IsFinite(m13) && double.IsFinite(m21) && double.IsFinite(m22) && double.IsFinite(m23) && double.IsFinite(m31) && double.IsFinite(m32) && double.IsFinite(m33))
            {
                inverse = new ProjectiveTransform(m11 + 0.0, m12 + 0.0, m13 + 0.0, m21 + 0.0, m22 + 0.0, m23 + 0.0, m31 + 0.0, m32 + 0.0, m33 + 0.0);
                return true;
            }
        }

        inverse = default;
        return false;
    }

    /// <summary>Transforms a point.</summary>
    /// <param name="point">The point.</param>
    /// <param name="result">The transformed point, or the origin when the point has no image.</param>
    /// <returns><see langword="false"/> when <c>W</c> is not positive at the point: it is on or behind the horizon, and has no image.</returns>
    public bool TryTransform(PointD point, out PointD result)
    {
        var w = TransformW(point.X, point.Y);
        if (w > 0)
        {
            result = new PointD(TransformX(point.X, point.Y) / w, TransformY(point.X, point.Y) / w);
            if (double.IsFinite(result.X) && double.IsFinite(result.Y))
                return true;
        }

        result = default;
        return false;
    }

    /// <inheritdoc />
    public bool Equals(ProjectiveTransform other)
        => M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13)
        && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23)
        && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ProjectiveTransform other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(M11);
        hash.Add(M12);
        hash.Add(M13);
        hash.Add(M21);
        hash.Add(M22);
        hash.Add(M23);
        hash.Add(M31);
        hash.Add(M32);
        hash.Add(M33);
        return hash.ToHashCode();
    }

    /// <summary>Returns the coefficients formatted as <c>[M11 M12 M13 M21 M22 M23 M31 M32 M33]</c>.</summary>
    /// <returns>A string representation of the transformation.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{M11} {M12} {M13} {M21} {M22} {M23} {M31} {M32} {M33}]");

    /// <summary>Compares two transformations for equality.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns><see langword="true"/> if every coefficient is equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(ProjectiveTransform left, ProjectiveTransform right) => left.Equals(right);

    /// <summary>Compares two transformations for inequality.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns><see langword="true"/> if a coefficient differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ProjectiveTransform left, ProjectiveTransform right) => !left.Equals(right);

    /// <summary>Gets the transformation that applies a first one, then a second one.</summary>
    /// <param name="left">The transformation applied first.</param>
    /// <param name="right">The transformation applied to the result of the first one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public static ProjectiveTransform operator *(ProjectiveTransform left, ProjectiveTransform right) => left.Then(right);

    /// <summary>Gets <c>X</c> at a point. The sums are evaluated in this order wherever a point is transformed.</summary>
    internal double TransformX(double x, double y) => ((M11 * x) + (M21 * y)) + M31;

    /// <summary>Gets <c>Y</c> at a point.</summary>
    internal double TransformY(double x, double y) => ((M12 * x) + (M22 * y)) + M32;

    /// <summary>Gets <c>W</c> at a point.</summary>
    internal double TransformW(double x, double y) => ((M13 * x) + (M23 * y)) + M33;

    /// <summary>
    /// Gets the affine transformation this one is, when <c>W</c> is 1 everywhere: the same coefficients, without any
    /// rounding.
    /// </summary>
    internal bool TryGetExactAffine(out AffineTransform transform)
    {
        if (M13 == 0 && M23 == 0 && M33 == 1)
        {
            transform = new AffineTransform(M11, M12, M21, M22, M31, M32);
            return true;
        }

        transform = default;
        return false;
    }

    /// <summary>
    /// Gets a value indicating whether the transformation flattens the plane to a line or a point, up to the rounding
    /// of its coefficients: its determinant is at most 2^-40 of the product of the lengths of its rows (the largest it
    /// can be). Such a transformation is a plane seen from its edge, which shows nothing.
    /// </summary>
    internal bool IsFlat
    {
        get
        {
            var row1 = Math.Sqrt((M11 * M11) + (M12 * M12) + (M13 * M13));
            var row2 = Math.Sqrt((M21 * M21) + (M22 * M22) + (M23 * M23));
            var row3 = Math.Sqrt((M31 * M31) + (M32 * M32) + (M33 * M33));

            // Written so that a value that is not a number is flat
            return !(Math.Abs(Determinant) > row1 * row2 * row3 / 1099511627776);
        }
    }

    /// <summary>Gets the transformation with <c>W</c> brought to 1 when it is the same positive number everywhere.</summary>
    internal ProjectiveTransform Normalize()
    {
        var w = M33;
        if (M13 != 0 || M23 != 0 || !(w > 0) || w == 1)
            return this;

        return new ProjectiveTransform(M11 / w, M12 / w, 0, M21 / w, M22 / w, 0, M31 / w, M32 / w, 1);
    }

    private static void EnsureFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be a finite number.", paramName);
    }

    private static void EnsureFinite(PointD point, string paramName)
    {
        EnsureFinite(point.X, paramName);
        EnsureFinite(point.Y, paramName);
    }
}
