using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// A transformation of the plane that keeps straight lines straight and parallel lines parallel: a combination of
/// translations, scalings, rotations and skews.
/// </summary>
/// <remarks>
/// <para>
/// A point <c>(x, y)</c> becomes <c>(x M11 + y M21 + OffsetX, x M12 + y M22 + OffsetY)</c>. The default value is the
/// identity. The coordinate system is the one of <see cref="PointD"/>: Y grows downward, so a positive angle turns
/// clockwise on the image.
/// </para>
/// <para>
/// Angles are in degrees. Their sine and cosine are computed with additions, multiplications and divisions only, so a
/// transformation is the same on every platform; a rotation by a multiple of 90 degrees is exact.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct AffineTransform : IEquatable<AffineTransform>
{
    // The default value of the structure is the identity: the coefficients are only read once they were set
    private readonly bool _isSet;
    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _offsetX;
    private readonly double _offsetY;

    /// <summary>Initializes a new instance of the <see cref="AffineTransform"/> struct from its coefficients.</summary>
    /// <param name="m11">The factor of x in the transformed x.</param>
    /// <param name="m12">The factor of x in the transformed y.</param>
    /// <param name="m21">The factor of y in the transformed x.</param>
    /// <param name="m22">The factor of y in the transformed y.</param>
    /// <param name="offsetX">The horizontal translation.</param>
    /// <param name="offsetY">The vertical translation.</param>
    /// <exception cref="ArgumentException">A coefficient is not a finite number.</exception>
    public AffineTransform(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        EnsureFinite(m11, nameof(m11));
        EnsureFinite(m12, nameof(m12));
        EnsureFinite(m21, nameof(m21));
        EnsureFinite(m22, nameof(m22));
        EnsureFinite(offsetX, nameof(offsetX));
        EnsureFinite(offsetY, nameof(offsetY));
        _isSet = true;
        _m11 = m11;
        _m12 = m12;
        _m21 = m21;
        _m22 = m22;
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    /// <summary>Gets the transformation that changes nothing.</summary>
    public static AffineTransform Identity => default;

    /// <summary>Gets the factor of x in the transformed x.</summary>
    public double M11 => _isSet ? _m11 : 1;

    /// <summary>Gets the factor of x in the transformed y.</summary>
    public double M12 => _m12;

    /// <summary>Gets the factor of y in the transformed x.</summary>
    public double M21 => _m21;

    /// <summary>Gets the factor of y in the transformed y.</summary>
    public double M22 => _isSet ? _m22 : 1;

    /// <summary>Gets the horizontal translation.</summary>
    public double OffsetX => _offsetX;

    /// <summary>Gets the vertical translation.</summary>
    public double OffsetY => _offsetY;

    /// <summary>Gets a value indicating whether the transformation changes nothing.</summary>
    public bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

    /// <summary>
    /// Gets the factor by which the transformation multiplies areas; it is negative when the transformation mirrors, and
    /// zero when it flattens the plane to a line or a point.
    /// </summary>
    public double Determinant => (M11 * M22) - (M12 * M21);

    /// <summary>Creates a translation.</summary>
    /// <param name="dx">The horizontal distance.</param>
    /// <param name="dy">The vertical distance.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException">A distance is not a finite number.</exception>
    public static AffineTransform CreateTranslation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    /// <summary>Creates a scaling around the origin.</summary>
    /// <param name="scaleX">The horizontal factor.</param>
    /// <param name="scaleY">The vertical factor.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException">A factor is not a finite number.</exception>
    public static AffineTransform CreateScale(double scaleX, double scaleY) => new(scaleX, 0, 0, scaleY, 0, 0);

    /// <summary>Creates a scaling around a point, which stays where it is.</summary>
    /// <param name="scaleX">The horizontal factor.</param>
    /// <param name="scaleY">The vertical factor.</param>
    /// <param name="center">The point that does not move.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException">A factor or a coordinate is not a finite number.</exception>
    public static AffineTransform CreateScale(double scaleX, double scaleY, PointD center)
        => new(scaleX, 0, 0, scaleY, center.X - (scaleX * center.X), center.Y - (scaleY * center.Y));

    /// <summary>Creates a rotation around the origin.</summary>
    /// <param name="degrees">The angle, in degrees. A positive angle turns clockwise on the image.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> is not a finite number.</exception>
    public static AffineTransform CreateRotation(double degrees) => CreateRotation(degrees, default);

    /// <summary>Creates a rotation around a point, which stays where it is.</summary>
    /// <param name="degrees">The angle, in degrees. A positive angle turns clockwise on the image.</param>
    /// <param name="center">The point that does not move.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentException"><paramref name="degrees"/> or a coordinate is not a finite number.</exception>
    public static AffineTransform CreateRotation(double degrees, PointD center)
    {
        EnsureFinite(degrees, nameof(degrees));
        DeterministicAngle.SinCos(degrees / 180, out var sin, out var cos);

        // Adding zero turns a negative zero into zero: a quarter turn has no sign left in its coefficients
        sin += 0.0;
        cos += 0.0;
        return new AffineTransform(
            cos,
            sin,
            -sin + 0.0,
            cos,
            center.X - ((cos * center.X) - (sin * center.Y)),
            center.Y - ((sin * center.X) + (cos * center.Y)));
    }

    /// <summary>Creates a skew: a point moves horizontally in proportion to its y, and vertically in proportion to its x.</summary>
    /// <param name="degreesX">The angle the vertical lines lean by, in degrees. Must be between -90 and 90, excluded.</param>
    /// <param name="degreesY">The angle the horizontal lines lean by, in degrees. Must be between -90 and 90, excluded.</param>
    /// <returns>The transformation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An angle is not between -90 and 90 degrees, excluded.</exception>
    public static AffineTransform CreateSkew(double degreesX, double degreesY)
    {
        if (!(degreesX > -90 && degreesX < 90))
            throw new ArgumentOutOfRangeException(nameof(degreesX), degreesX, "The angle must be between -90 and 90 degrees, excluded.");

        if (!(degreesY > -90 && degreesY < 90))
            throw new ArgumentOutOfRangeException(nameof(degreesY), degreesY, "The angle must be between -90 and 90 degrees, excluded.");

        return new AffineTransform(1, DeterministicAngle.Tan(degreesY / 180) + 0.0, DeterministicAngle.Tan(degreesX / 180) + 0.0, 1, 0, 0);
    }

    /// <summary>Gets the transformation that applies this one first, then another one.</summary>
    /// <param name="next">The transformation applied after this one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public AffineTransform Then(AffineTransform next) => new(
        (next.M11 * M11) + (next.M21 * M12),
        (next.M12 * M11) + (next.M22 * M12),
        (next.M11 * M21) + (next.M21 * M22),
        (next.M12 * M21) + (next.M22 * M22),
        ((next.M11 * OffsetX) + (next.M21 * OffsetY)) + next.OffsetX,
        ((next.M12 * OffsetX) + (next.M22 * OffsetY)) + next.OffsetY);

    /// <summary>Gets the transformation that applies a first one, then a second one.</summary>
    /// <param name="first">The transformation applied first.</param>
    /// <param name="second">The transformation applied to the result of the first one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public static AffineTransform Multiply(AffineTransform first, AffineTransform second) => first.Then(second);

    /// <summary>Gets the transformation that undoes this one.</summary>
    /// <param name="inverse">The inverse transformation, or the identity when there is none.</param>
    /// <returns><see langword="false"/> when the transformation flattens the plane to a line or a point: nothing undoes it.</returns>
    public bool TryInvert(out AffineTransform inverse)
    {
        var determinant = Determinant;
        if (determinant != 0)
        {
            var m11 = M22 / determinant;
            var m12 = -M12 / determinant;
            var m21 = -M21 / determinant;
            var m22 = M11 / determinant;
            var offsetX = ((M21 * OffsetY) - (M22 * OffsetX)) / determinant;
            var offsetY = ((M12 * OffsetX) - (M11 * OffsetY)) / determinant;
            if (double.IsFinite(m11) && double.IsFinite(m12) && double.IsFinite(m21) && double.IsFinite(m22) && double.IsFinite(offsetX) && double.IsFinite(offsetY))
            {
                inverse = new AffineTransform(m11 + 0.0, m12 + 0.0, m21 + 0.0, m22 + 0.0, offsetX + 0.0, offsetY + 0.0);
                return true;
            }
        }

        inverse = default;
        return false;
    }

    /// <summary>Transforms a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The transformed point.</returns>
    public PointD Transform(PointD point) => new(TransformX(point.X, point.Y), TransformY(point.X, point.Y));

    /// <summary>Transforms a vector: the difference of two points, which a translation does not change.</summary>
    /// <param name="vector">The vector.</param>
    /// <returns>The transformed vector.</returns>
    public PointD TransformVector(PointD vector) => new((M11 * vector.X) + (M21 * vector.Y), (M12 * vector.X) + (M22 * vector.Y));

    /// <summary>Gets the smallest rectangle, aligned with the axes, that contains a transformed rectangle.</summary>
    /// <remarks>
    /// The result contains the four transformed corners: it is where a rotated, skewed or flipped rectangle lands, such
    /// as the box of a text or an image drawn in <see cref="Drawing.DrawingCanvas.Transform(AffineTransform, Drawing.DrawingAction)"/>.
    /// A rotation that is not a multiple of a quarter of a turn makes it larger than the rectangle. A rectangle without
    /// width or height is a segment or a point, and gives the bounds of that.
    /// </remarks>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The bounds of the transformed rectangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size of the result is not a finite number.</exception>
    public RectangleD TransformBounds(RectangleD rectangle)
    {
        var topLeft = Transform(rectangle.Location);
        var topRight = Transform(new PointD(rectangle.Right, rectangle.Top));
        var bottomLeft = Transform(new PointD(rectangle.Left, rectangle.Bottom));
        var bottomRight = Transform(new PointD(rectangle.Right, rectangle.Bottom));
        var left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        var top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        var right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        var bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new RectangleD(left, top, right - left, bottom - top);
    }

    /// <inheritdoc />
    public bool Equals(AffineTransform other)
        => M11.Equals(other.M11) && M12.Equals(other.M12) && M21.Equals(other.M21) && M22.Equals(other.M22) && OffsetX.Equals(other.OffsetX) && OffsetY.Equals(other.OffsetY);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is AffineTransform other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);

    /// <summary>Returns the coefficients formatted as <c>[M11 M12 M21 M22 OffsetX OffsetY]</c>.</summary>
    /// <returns>A string representation of the transformation.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{M11} {M12} {M21} {M22} {OffsetX} {OffsetY}]");

    /// <summary>Compares two transformations for equality.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns><see langword="true"/> if every coefficient is equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(AffineTransform left, AffineTransform right) => left.Equals(right);

    /// <summary>Compares two transformations for inequality.</summary>
    /// <param name="left">The first transformation.</param>
    /// <param name="right">The second transformation.</param>
    /// <returns><see langword="true"/> if a coefficient differs; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(AffineTransform left, AffineTransform right) => !left.Equals(right);

    /// <summary>Gets the transformation that applies a first one, then a second one.</summary>
    /// <param name="left">The transformation applied first.</param>
    /// <param name="right">The transformation applied to the result of the first one.</param>
    /// <returns>The combined transformation.</returns>
    /// <exception cref="ArgumentException">A coefficient of the result is not a finite number.</exception>
    public static AffineTransform operator *(AffineTransform left, AffineTransform right) => left.Then(right);

    /// <summary>Gets the horizontal coordinate of a transformed point. The sums are evaluated in this order wherever a point is transformed.</summary>
    internal double TransformX(double x, double y) => ((M11 * x) + (M21 * y)) + OffsetX;

    /// <summary>Gets the vertical coordinate of a transformed point.</summary>
    internal double TransformY(double x, double y) => ((M12 * x) + (M22 * y)) + OffsetY;

    private static void EnsureFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The value must be a finite number.", paramName);
    }
}
