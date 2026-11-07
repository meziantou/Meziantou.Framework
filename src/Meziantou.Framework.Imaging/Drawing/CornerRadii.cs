using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The radius of each corner of a rounded rectangle. A corner whose radius is zero is not rounded.</summary>
/// <remarks>
/// The corners are quarters of a circle. Radii that do not fit are reduced as CSS reduces the radii of a border: when
/// the radii of two corners of a side add up to more than that side, every radius is multiplied by the same factor,
/// the largest one that makes them all fit.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct CornerRadii : IEquatable<CornerRadii>
{
    /// <summary>Initializes a new instance of the <see cref="CornerRadii"/> struct whose corners have the same radius.</summary>
    /// <param name="radius">The radius of every corner. Must be zero or positive.</param>
    /// <exception cref="ArgumentException"><paramref name="radius"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or exceeds 16777216.</exception>
    public CornerRadii(double radius)
        : this(radius, radius, radius, radius)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CornerRadii"/> struct.</summary>
    /// <param name="topLeft">The radius of the top-left corner. Must be zero or positive.</param>
    /// <param name="topRight">The radius of the top-right corner. Must be zero or positive.</param>
    /// <param name="bottomRight">The radius of the bottom-right corner. Must be zero or positive.</param>
    /// <param name="bottomLeft">The radius of the bottom-left corner. Must be zero or positive.</param>
    /// <exception cref="ArgumentException">A radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius is negative or exceeds 16777216.</exception>
    public CornerRadii(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        DrawingGuard.Length(topLeft, nameof(topLeft));
        DrawingGuard.Length(topRight, nameof(topRight));
        DrawingGuard.Length(bottomRight, nameof(bottomRight));
        DrawingGuard.Length(bottomLeft, nameof(bottomLeft));
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>Gets the radius of the top-left corner.</summary>
    public double TopLeft { get; }

    /// <summary>Gets the radius of the top-right corner.</summary>
    public double TopRight { get; }

    /// <summary>Gets the radius of the bottom-right corner.</summary>
    public double BottomRight { get; }

    /// <summary>Gets the radius of the bottom-left corner.</summary>
    public double BottomLeft { get; }

    /// <summary>Gets a value indicating whether no corner is rounded.</summary>
    public bool IsEmpty => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;

    /// <inheritdoc />
    public bool Equals(CornerRadii other)
        => TopLeft.Equals(other.TopLeft) && TopRight.Equals(other.TopRight) && BottomRight.Equals(other.BottomRight) && BottomLeft.Equals(other.BottomLeft);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CornerRadii other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);

    /// <summary>Returns the radii, clockwise from the top-left corner.</summary>
    /// <returns>A culture-invariant string.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"[{TopLeft} {TopRight} {BottomRight} {BottomLeft}]");

    /// <summary>Determines whether two values have the same radii.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if the radii are equal.</returns>
    public static bool operator ==(CornerRadii left, CornerRadii right) => left.Equals(right);

    /// <summary>Determines whether two values have different radii.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if a radius differs.</returns>
    public static bool operator !=(CornerRadii left, CornerRadii right) => !left.Equals(right);
}
