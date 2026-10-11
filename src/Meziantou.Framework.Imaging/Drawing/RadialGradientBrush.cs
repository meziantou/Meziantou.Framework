using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A gradient around a point: the color is constant on the circles centered on that point.</summary>
/// <remarks>
/// The parameter of a position is its distance to <see cref="Center"/> divided by <see cref="Radius"/>. When the radius
/// is zero, the whole canvas has the color of the last stop.
/// </remarks>
public sealed class RadialGradientBrush : GradientBrush
{
    /// <summary>Initializes a new instance of the <see cref="RadialGradientBrush"/> class.</summary>
    /// <param name="center">The position of offset 0, in canvas coordinates.</param>
    /// <param name="radius">The distance from the center at which the offset is 1. Must be zero or positive.</param>
    /// <param name="stops">The stops, in increasing order of offset. They are copied.</param>
    /// <exception cref="ArgumentException">There is no stop, the offsets decrease, or a coordinate or the radius is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or the radius is negative or exceeds 16777216.</exception>
    public RadialGradientBrush(PointD center, double radius, ReadOnlySpan<GradientStop> stops)
        : base(stops)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radius, nameof(radius));
        Center = center;
        Radius = radius;
    }

    /// <summary>Gets the position of offset 0.</summary>
    public PointD Center { get; }

    /// <summary>Gets the distance from the center at which the offset is 1.</summary>
    public double Radius { get; }
}
