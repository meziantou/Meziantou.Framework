using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>Immutable description of a stroke: its paint, its width and the shape of its corners and ends.</summary>
/// <remarks>
/// A stroke is centered on its path: it covers the points within half <see cref="Width"/> of the path, completed by the
/// joins at the vertices and by the caps at the ends of open contours. The vertices that the library creates when it
/// approximates a curve are always joined with <see cref="Drawing.LineJoin.Round"/>, so that a wide curved stroke stays
/// smooth; <see cref="LineJoin"/> applies to the vertices of the path itself.
/// </remarks>
public sealed class Pen
{
    /// <summary>Initializes a new instance of the <see cref="Pen"/> class.</summary>
    /// <param name="brush">The paint of the stroke. A color converts implicitly.</param>
    /// <param name="width">The width of the stroke, in pixels. Must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="width"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive, or exceeds 16777216.</exception>
    public Pen(Brush brush, double width)
    {
        ArgumentNullException.ThrowIfNull(brush);
        DrawingGuard.Length(width, nameof(width));
        if (width == 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");

        Brush = brush;
        Width = width;
    }

    /// <summary>Gets the paint of the stroke.</summary>
    public Brush Brush { get; }

    /// <summary>Gets the width of the stroke, in pixels.</summary>
    public double Width { get; }

    /// <summary>Gets the shape of the corners. Defaults to <see cref="Drawing.LineJoin.Miter"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Drawing.LineJoin"/>.</exception>
    public LineJoin LineJoin
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The line join is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the shape of the ends of open contours. Defaults to <see cref="Drawing.LineCap.Butt"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Drawing.LineCap"/>.</exception>
    public LineCap LineCap
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The line cap is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the limit of <see cref="Drawing.LineJoin.Miter"/> joins: the largest allowed ratio between the length of the
    /// miter (from the inner corner to the tip) and the width of the stroke. A sharper corner is joined with a
    /// <see cref="Drawing.LineJoin.Bevel"/>. Defaults to 4 (corners sharper than about 29 degrees are beveled); 1 bevels
    /// every corner.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1, greater than 1048576, or not a number.</exception>
    public double MiterLimit
    {
        get;
        init
        {
            if (!(value >= 1 && value <= 1048576))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The miter limit must be between 1 and 1048576.");

            field = value;
        }
    } = 4;
}
