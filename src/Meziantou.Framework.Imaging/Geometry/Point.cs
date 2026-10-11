using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>Represents a pixel coordinate. The origin is the top-left corner of the stored pixels; Y grows downward.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct Point : IEquatable<Point>
{
    /// <summary>Initializes a new instance of the <see cref="Point"/> struct.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the point whose coordinates are zero.</summary>
    public static Point Empty => default;

    /// <summary>Gets the horizontal coordinate.</summary>
    public int X { get; }

    /// <summary>Gets the vertical coordinate.</summary>
    public int Y { get; }

    /// <summary>Deconstructs the point into its coordinates.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    public void Deconstruct(out int x, out int y)
    {
        x = X;
        y = Y;
    }

    /// <inheritdoc />
    public bool Equals(Point other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Point other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Returns the point formatted as <c>({X}, {Y})</c>.</summary>
    /// <returns>A string representation of the point.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");

    /// <summary>Compares two points for equality.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> if both points are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Point left, Point right) => left.Equals(right);

    /// <summary>Compares two points for inequality.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> if the points differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Point left, Point right) => !left.Equals(right);
}
