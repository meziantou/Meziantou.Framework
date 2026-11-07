using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Represents a position with sub-pixel precision. The origin is the top-left corner of the stored pixels; Y grows
/// downward. Integer coordinates are pixel boundaries: the pixel <c>(x, y)</c> covers the square from <c>(x, y)</c> to
/// <c>(x + 1, y + 1)</c>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PointD : IEquatable<PointD>
{
    /// <summary>Initializes a new instance of the <see cref="PointD"/> struct.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    public PointD(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the point whose coordinates are zero.</summary>
    public static PointD Empty => default;

    /// <summary>Gets the horizontal coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the vertical coordinate.</summary>
    public double Y { get; }

    /// <summary>Deconstructs the point into its coordinates.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    public void Deconstruct(out double x, out double y)
    {
        x = X;
        y = Y;
    }

    /// <summary>Converts a pixel coordinate to the position of the top-left corner of that pixel.</summary>
    /// <param name="point">The pixel coordinate.</param>
    /// <returns>The position.</returns>
    public static PointD FromPoint(Point point) => new(point.X, point.Y);

    /// <summary>Moves the point.</summary>
    /// <param name="dx">The horizontal distance.</param>
    /// <param name="dy">The vertical distance.</param>
    /// <returns>The moved point.</returns>
    public PointD Offset(double dx, double dy) => new(X + dx, Y + dy);

    /// <summary>Gets the distance to another point.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The length of the segment between both points.</returns>
    public double DistanceTo(PointD other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Adds the coordinates of two points.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>The sum.</returns>
    public static PointD Add(PointD left, PointD right) => new(left.X + right.X, left.Y + right.Y);

    /// <summary>Subtracts the coordinates of a point from those of another.</summary>
    /// <param name="left">The point to subtract from.</param>
    /// <param name="right">The point to subtract.</param>
    /// <returns>The difference: the vector from <paramref name="right"/> to <paramref name="left"/>.</returns>
    public static PointD Subtract(PointD left, PointD right) => new(left.X - right.X, left.Y - right.Y);

    /// <summary>Negates the coordinates of a point.</summary>
    /// <param name="value">The point.</param>
    /// <returns>The opposite point.</returns>
    public static PointD Negate(PointD value) => new(-value.X, -value.Y);

    /// <summary>Multiplies the coordinates of a point by a factor.</summary>
    /// <param name="left">The point.</param>
    /// <param name="right">The factor.</param>
    /// <returns>The scaled point.</returns>
    public static PointD Multiply(PointD left, double right) => new(left.X * right, left.Y * right);

    /// <summary>Divides the coordinates of a point by a divisor.</summary>
    /// <param name="left">The point.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The scaled point.</returns>
    public static PointD Divide(PointD left, double right) => new(left.X / right, left.Y / right);

    /// <inheritdoc />
    public bool Equals(PointD other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PointD other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Returns the point formatted as <c>({X}, {Y})</c>.</summary>
    /// <returns>A string representation of the point.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");

    /// <summary>Converts a pixel coordinate to the position of the top-left corner of that pixel. The conversion is lossless.</summary>
    /// <param name="point">The pixel coordinate.</param>
    public static implicit operator PointD(Point point) => FromPoint(point);

    /// <summary>Compares two points for equality.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> if both points are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(PointD left, PointD right) => left.Equals(right);

    /// <summary>Compares two points for inequality.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> if the points differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(PointD left, PointD right) => !left.Equals(right);

    /// <summary>Adds the coordinates of two points.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns>The sum.</returns>
    public static PointD operator +(PointD left, PointD right) => Add(left, right);

    /// <summary>Subtracts the coordinates of a point from those of another.</summary>
    /// <param name="left">The point to subtract from.</param>
    /// <param name="right">The point to subtract.</param>
    /// <returns>The difference: the vector from <paramref name="right"/> to <paramref name="left"/>.</returns>
    public static PointD operator -(PointD left, PointD right) => Subtract(left, right);

    /// <summary>Negates the coordinates of a point.</summary>
    /// <param name="value">The point.</param>
    /// <returns>The opposite point.</returns>
    public static PointD operator -(PointD value) => Negate(value);

    /// <summary>Multiplies the coordinates of a point by a factor.</summary>
    /// <param name="left">The point.</param>
    /// <param name="right">The factor.</param>
    /// <returns>The scaled point.</returns>
    public static PointD operator *(PointD left, double right) => Multiply(left, right);

    /// <summary>Multiplies the coordinates of a point by a factor.</summary>
    /// <param name="left">The factor.</param>
    /// <param name="right">The point.</param>
    /// <returns>The scaled point.</returns>
    public static PointD operator *(double left, PointD right) => Multiply(right, left);

    /// <summary>Divides the coordinates of a point by a divisor.</summary>
    /// <param name="left">The point.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The scaled point.</returns>
    public static PointD operator /(PointD left, double right) => Divide(left, right);
}
