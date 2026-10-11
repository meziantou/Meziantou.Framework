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
}
