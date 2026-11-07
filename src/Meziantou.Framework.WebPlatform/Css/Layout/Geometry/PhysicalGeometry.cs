using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The physical geometry of the fragments (https://drafts.csswg.org/css-writing-modes-4/#physical-dimensions): x grows to the
// right and y grows downwards, in CSS px.

/// <summary>A physical side of a box (https://drafts.csswg.org/css-writing-modes-4/#physical-dimensions).</summary>
internal enum PhysicalSide : byte
{
    Top,
    Right,
    Bottom,
    Left,
}

/// <summary>A physical position (x to the right, y downwards), in CSS px.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PhysicalOffset(double X, double Y)
{
    public static PhysicalOffset Zero => default;

    public static PhysicalOffset operator +(PhysicalOffset left, PhysicalOffset right) => new(left.X + right.X, left.Y + right.Y);

    public static PhysicalOffset operator -(PhysicalOffset left, PhysicalOffset right) => new(left.X - right.X, left.Y - right.Y);

    public static PhysicalOffset operator -(PhysicalOffset value) => new(-value.X, -value.Y);

    public PhysicalOffset Add(PhysicalOffset other) => this + other;

    public PhysicalOffset Subtract(PhysicalOffset other) => this - other;

    public PhysicalOffset Negate() => -this;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}

/// <summary>A physical size (width and height), in CSS px.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PhysicalSize(double Width, double Height)
{
    public static PhysicalSize Zero => default;

    /// <summary>Whether the size has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}

/// <summary>A physical rectangle (its top-left corner and its size), in CSS px.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PhysicalRect(double X, double Y, double Width, double Height)
{
    public PhysicalRect(PhysicalOffset offset, PhysicalSize size)
        : this(offset.X, offset.Y, size.Width, size.Height)
    {
    }

    public static PhysicalRect Empty => default;

    public PhysicalOffset Offset => new(X, Y);

    public PhysicalSize Size => new(Width, Height);

    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Whether the rectangle has no area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Returns the rectangle moved by an offset.</summary>
    public PhysicalRect Translate(PhysicalOffset offset) => new(X + offset.X, Y + offset.Y, Width, Height);

    /// <summary>Returns the rectangle with its sides moved outwards by the given amounts (inwards when they are negative).</summary>
    public PhysicalRect Expand(Sides<double> sides) => new(X - sides.Left, Y - sides.Top, Math.Max(0, Width + sides.Left + sides.Right), Math.Max(0, Height + sides.Top + sides.Bottom));

    /// <summary>Returns the rectangle with its sides moved inwards by the given amounts.</summary>
    public PhysicalRect Contract(Sides<double> sides) => Expand(new Sides<double>(-sides.Top, -sides.Right, -sides.Bottom, -sides.Left));

    /// <summary>Returns the smallest rectangle that contains both rectangles (an empty rectangle is ignored).</summary>
    public PhysicalRect Union(PhysicalRect other)
    {
        if (other.IsEmpty)
            return this;

        if (IsEmpty)
            return other;

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new PhysicalRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    /// <summary>Returns whether a point is in the rectangle (the left and top edges are included, the right and bottom edges are not).</summary>
    public bool Contains(double x, double y) => x >= X && y >= Y && x < Right && y < Bottom;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Width}x{Height})");
}

/// <summary>
/// A physical quadrilateral (https://drafts.fxtf.org/geometry/#domquad): a rectangle mapped by transforms, its corners in the
/// order top-left, top-right, bottom-right, bottom-left of the rectangle.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PhysicalQuad(PhysicalOffset P1, PhysicalOffset P2, PhysicalOffset P3, PhysicalOffset P4)
{
    public PhysicalQuad(PhysicalRect rect)
        : this(new PhysicalOffset(rect.Left, rect.Top), new PhysicalOffset(rect.Right, rect.Top), new PhysicalOffset(rect.Right, rect.Bottom), new PhysicalOffset(rect.Left, rect.Bottom))
    {
    }

    /// <summary>The smallest rectangle that contains the four points (https://drafts.fxtf.org/geometry/#dom-domquad-getbounds).</summary>
    public PhysicalRect Bounds
    {
        get
        {
            var left = Math.Min(Math.Min(P1.X, P2.X), Math.Min(P3.X, P4.X));
            var top = Math.Min(Math.Min(P1.Y, P2.Y), Math.Min(P3.Y, P4.Y));
            var right = Math.Max(Math.Max(P1.X, P2.X), Math.Max(P3.X, P4.X));
            var bottom = Math.Max(Math.Max(P1.Y, P2.Y), Math.Max(P3.Y, P4.Y));
            return new PhysicalRect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>Returns the quad moved by an offset.</summary>
    public PhysicalQuad Translate(PhysicalOffset offset) => new(P1 + offset, P2 + offset, P3 + offset, P4 + offset);

    /// <summary>Returns the quad with the coordinates of its points multiplied by a factor.</summary>
    public PhysicalQuad Scale(double factor) => new(Scale(P1, factor), Scale(P2, factor), Scale(P3, factor), Scale(P4, factor));

    private static PhysicalOffset Scale(PhysicalOffset point, double factor) => new(point.X * factor, point.Y * factor);
}
