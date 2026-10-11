using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>Represents an axis-aligned rectangle with sub-pixel precision (see <see cref="PointD"/> for the coordinate system).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct RectangleD : IEquatable<RectangleD>
{
    /// <summary>Initializes a new instance of the <see cref="RectangleD"/> struct.</summary>
    /// <param name="x">The left coordinate.</param>
    /// <param name="y">The top coordinate.</param>
    /// <param name="width">The width. Must be zero or positive, and finite.</param>
    /// <param name="height">The height. Must be zero or positive, and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is negative or not a finite number.</exception>
    public RectangleD(double x, double y, double width, double height)
    {
        if (!(width >= 0) || double.IsPositiveInfinity(width))
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be zero or positive, and finite.");

        if (!(height >= 0) || double.IsPositiveInfinity(height))
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be zero or positive, and finite.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets an empty rectangle located at the origin.</summary>
    public static RectangleD Empty => default;

    /// <summary>Gets the left coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the top coordinate.</summary>
    public double Y { get; }

    /// <summary>Gets the width.</summary>
    public double Width { get; }

    /// <summary>Gets the height.</summary>
    public double Height { get; }

    /// <summary>Gets the left coordinate.</summary>
    public double Left => X;

    /// <summary>Gets the top coordinate.</summary>
    public double Top => Y;

    /// <summary>Gets the right coordinate (<c>X + Width</c>).</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom coordinate (<c>Y + Height</c>).</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets a value indicating whether the rectangle has no area.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>Creates a rectangle from its edges.</summary>
    /// <param name="left">The left coordinate.</param>
    /// <param name="top">The top coordinate.</param>
    /// <param name="right">The right coordinate. Must not be less than <paramref name="left"/>.</param>
    /// <param name="bottom">The bottom coordinate. Must not be less than <paramref name="top"/>.</param>
    /// <returns>The rectangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bounds are inverted, or the size is not a finite number.</exception>
    public static RectangleD FromLTRB(double left, double top, double right, double bottom)
    {
        if (!(right >= left))
            throw new ArgumentOutOfRangeException(nameof(right), right, "The right bound must not be less than the left bound.");

        if (!(bottom >= top))
            throw new ArgumentOutOfRangeException(nameof(bottom), bottom, "The bottom bound must not be less than the top bound.");

        return new RectangleD(left, top, right - left, bottom - top);
    }

    /// <summary>Converts a rectangle of pixels to the area those pixels cover.</summary>
    /// <param name="rectangle">The rectangle of pixels.</param>
    /// <returns>The area.</returns>
    public static RectangleD FromRectangle(Rectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    /// <inheritdoc />
    public bool Equals(RectangleD other) => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RectangleD other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>Returns the rectangle formatted as <c>{X},{Y} {Width}x{Height}</c>.</summary>
    /// <returns>A string representation of the rectangle.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y} {Width}x{Height}");

    /// <summary>Converts a rectangle of pixels to the area those pixels cover. The conversion is lossless.</summary>
    /// <param name="rectangle">The rectangle of pixels.</param>
    public static implicit operator RectangleD(Rectangle rectangle) => FromRectangle(rectangle);

    /// <summary>Compares two rectangles for equality.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> if both rectangles are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RectangleD left, RectangleD right) => left.Equals(right);

    /// <summary>Compares two rectangles for inequality.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> if the rectangles differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RectangleD left, RectangleD right) => !left.Equals(right);
}
