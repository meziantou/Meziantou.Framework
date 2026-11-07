using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A gradient along a line: the color is constant on the perpendiculars of that line.</summary>
/// <remarks>
/// The parameter of a position is its projection on the line from <see cref="Start"/> (0) to <see cref="End"/> (1). When
/// both points are the same, the whole canvas has the color of the last stop.
/// </remarks>
public sealed class LinearGradientBrush : GradientBrush
{
    /// <summary>Initializes a new instance of the <see cref="LinearGradientBrush"/> class.</summary>
    /// <param name="start">The position of offset 0, in canvas coordinates.</param>
    /// <param name="end">The position of offset 1, in canvas coordinates.</param>
    /// <param name="stops">The stops, in increasing order of offset. They are copied.</param>
    /// <exception cref="ArgumentException">There is no stop, the offsets decrease, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public LinearGradientBrush(PointD start, PointD end, ReadOnlySpan<GradientStop> stops)
        : base(stops)
    {
        DrawingGuard.Point(start, nameof(start));
        DrawingGuard.Point(end, nameof(end));
        Start = start;
        End = end;
    }

    /// <summary>Gets the position of offset 0.</summary>
    public PointD Start { get; }

    /// <summary>Gets the position of offset 1.</summary>
    public PointD End { get; }
}
