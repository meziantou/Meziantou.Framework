using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>Represents an axis-aligned rectangle of pixels with exclusive <see cref="Right"/> and <see cref="Bottom"/> bounds.</summary>
/// <remarks>
/// The constructor guarantees that <c>X + Width</c> and <c>Y + Height</c> are representable as <see cref="int"/>,
/// so <see cref="Right"/> and <see cref="Bottom"/> never overflow.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct Rectangle : IEquatable<Rectangle>
{
    /// <summary>Initializes a new instance of the <see cref="Rectangle"/> struct.</summary>
    /// <param name="x">The left coordinate (inclusive).</param>
    /// <param name="y">The top coordinate (inclusive).</param>
    /// <param name="width">The width. Must be zero or positive.</param>
    /// <param name="height">The height. Must be zero or positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is negative, or the exclusive right or bottom bound overflows <see cref="int"/>.
    /// </exception>
    public Rectangle(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if ((long)x + width > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), width, "The right bound of the rectangle overflows Int32.");

        if ((long)y + height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(height), height, "The bottom bound of the rectangle overflows Int32.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Initializes a new instance of the <see cref="Rectangle"/> struct.</summary>
    /// <param name="location">The top-left corner (inclusive).</param>
    /// <param name="size">The size.</param>
    /// <exception cref="ArgumentOutOfRangeException">The exclusive right or bottom bound overflows <see cref="int"/>.</exception>
    public Rectangle(Point location, Size size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    /// <summary>Gets an empty rectangle located at the origin.</summary>
    public static Rectangle Empty => default;

    /// <summary>Gets the left coordinate (inclusive).</summary>
    public int X { get; }

    /// <summary>Gets the top coordinate (inclusive).</summary>
    public int Y { get; }

    /// <summary>Gets the width.</summary>
    public int Width { get; }

    /// <summary>Gets the height.</summary>
    public int Height { get; }

    /// <summary>Gets the left coordinate (inclusive). Same as <see cref="X"/>.</summary>
    public int Left => X;

    /// <summary>Gets the top coordinate (inclusive). Same as <see cref="Y"/>.</summary>
    public int Top => Y;

    /// <summary>Gets the exclusive right bound (<c>X + Width</c>).</summary>
    public int Right => X + Width;

    /// <summary>Gets the exclusive bottom bound (<c>Y + Height</c>).</summary>
    public int Bottom => Y + Height;

    /// <summary>Gets the top-left corner.</summary>
    public Point Location => new(X, Y);

    /// <summary>Gets the size.</summary>
    public Size Size => new(Width, Height);

    /// <summary>Gets a value indicating whether the width or the height is zero.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Creates a rectangle from its inclusive left/top and exclusive right/bottom bounds.</summary>
    /// <param name="left">The left coordinate (inclusive).</param>
    /// <param name="top">The top coordinate (inclusive).</param>
    /// <param name="right">The right coordinate (exclusive). Must be greater than or equal to <paramref name="left"/>.</param>
    /// <param name="bottom">The bottom coordinate (exclusive). Must be greater than or equal to <paramref name="top"/>.</param>
    /// <returns>The rectangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A bound is inverted or the resulting size overflows <see cref="int"/>.</exception>
    public static Rectangle FromLTRB(int left, int top, int right, int bottom)
    {
        var width = (long)right - left;
        var height = (long)bottom - top;
        if (width is < 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(right), right, "The right bound must be greater than or equal to the left bound.");

        if (height is < 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(bottom), bottom, "The bottom bound must be greater than or equal to the top bound.");

        return new Rectangle(left, top, (int)width, (int)height);
    }

    /// <summary>Returns the intersection of two rectangles, or <see cref="Empty"/> if they do not overlap.</summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns>The intersection.</returns>
    public static Rectangle Intersect(Rectangle a, Rectangle b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
            return Empty;

        return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>Determines whether the specified point is inside this rectangle.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> if the point is inside; otherwise <see langword="false"/>.</returns>
    public bool Contains(Point point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>Determines whether the specified rectangle is entirely inside this rectangle.</summary>
    /// <param name="rectangle">The rectangle to test.</param>
    /// <returns><see langword="true"/> if <paramref name="rectangle"/> is entirely contained; otherwise <see langword="false"/>.</returns>
    public bool Contains(Rectangle rectangle) => rectangle.Left >= Left && rectangle.Right <= Right && rectangle.Top >= Top && rectangle.Bottom <= Bottom;

    /// <summary>Determines whether this rectangle overlaps the specified rectangle by at least one pixel.</summary>
    /// <param name="rectangle">The rectangle to test.</param>
    /// <returns><see langword="true"/> if the rectangles overlap; otherwise <see langword="false"/> (always for an empty rectangle).</returns>
    public bool IntersectsWith(Rectangle rectangle) => !IsEmpty && !rectangle.IsEmpty && rectangle.Left < Right && Left < rectangle.Right && rectangle.Top < Bottom && Top < rectangle.Bottom;

    /// <inheritdoc />
    public bool Equals(Rectangle other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Rectangle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>Returns the rectangle formatted as <c>{X},{Y} {Width}x{Height}</c>.</summary>
    /// <returns>A string representation of the rectangle.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y} {Width}x{Height}");

    /// <summary>Compares two rectangles for equality.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> if both rectangles are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);

    /// <summary>Compares two rectangles for inequality.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> if the rectangles differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(Rectangle left, Rectangle right) => !left.Equals(right);
}
