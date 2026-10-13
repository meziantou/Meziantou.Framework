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
    /// <summary>The shortest pattern of dashes and gaps: 1/1024 of a pixel.</summary>
    internal const double MinDashPatternLength = 1.0 / 1024;

    private readonly double[] _dashPattern = [];

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

    /// <summary>
    /// Gets the lengths, in pixels, of the dashes and of the gaps of the stroke, alternately and starting with a dash.
    /// Empty (the default) for a continuous stroke.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pattern is repeated along each contour of the path, which starts <see cref="DashOffset"/> into it; lengths are
    /// measured along the path as it is drawn. A pattern of an odd number of lengths is used twice, so that <c>[3]</c>
    /// is dashes of 3 and gaps of 3. Each dash is stroked as an open contour: it gets <see cref="LineCap"/> at both ends
    /// and <see cref="LineJoin"/> at the vertices of the path it goes through. A dash of length zero is drawn by its
    /// caps alone: a dot with <see cref="Drawing.LineCap.Round"/>, a square with <see cref="Drawing.LineCap.Square"/>.
    /// </para>
    /// <para>
    /// When a closed contour ends in the middle of a dash, that dash continues with the first dash of the contour, as one
    /// dash. The time to draw a dashed stroke grows with the number of dashes on the canvas.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A length is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative or exceeds 16777216, or the dashes and the gaps of a pattern that is not empty add up to less than 1/1024.</exception>
    public ReadOnlySpan<double> DashPattern
    {
        get => _dashPattern;
        init
        {
            double total = 0;
            foreach (var length in value)
            {
                DrawingGuard.Length(length, nameof(value));
                total += length;
            }

            if (value.IsEmpty)
            {
                _dashPattern = [];
                Dashes = [];
                DashLength = 0;
                return;
            }

            // A pattern of an odd number of lengths is used twice
            var isOdd = (value.Length & 1) != 0;
            if (isOdd)
            {
                total *= 2;
            }

            if (total < MinDashPatternLength)
                throw new ArgumentOutOfRangeException(nameof(value), total, "The dashes and the gaps of a dash pattern must add up to 1/1024 at least.");

            _dashPattern = value.ToArray();
            Dashes = isOdd ? [.. value, .. value] : _dashPattern;
            DashLength = total;
        }
    }

    /// <summary>Gets the distance into <see cref="DashPattern"/> at which each contour starts. Defaults to 0; a negative distance starts before the pattern.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public double DashOffset
    {
        get;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentException("The dash offset must be a finite number.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets the pattern as a whole number of dashes and gaps; empty for a continuous stroke.</summary>
    internal double[] Dashes { get; private init; } = [];

    /// <summary>Gets the sum of the lengths of <see cref="Dashes"/>.</summary>
    internal double DashLength { get; private init; }

    /// <summary>Gets a value indicating whether the stroke is cut into dashes.</summary>
    internal bool IsDashed => Dashes.Length != 0;
}
