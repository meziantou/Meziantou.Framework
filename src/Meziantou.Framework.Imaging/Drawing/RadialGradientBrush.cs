using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A gradient around a point: the color is constant on circles around that point.</summary>
/// <remarks>
/// <para>
/// The parameter of a position is its distance to <see cref="Center"/> divided by <see cref="Radius"/>. When the radius
/// is zero, the whole canvas has the color of the last stop.
/// </para>
/// <para>
/// With a <see cref="FocalPoint"/> that is not the center, the gradient spreads from the focal point to the circle of
/// <see cref="Center"/> and <see cref="Radius"/>: the parameter of a position is how far it is from the focal point,
/// as a fraction of the distance from the focal point to that circle in the same direction. The circle of a parameter
/// <c>t</c> is centered <c>t</c> of the way from the focal point to the center, with a radius of <c>t</c> times
/// <see cref="Radius"/>.
/// </para>
/// <para>
/// With a <see cref="FocalRadius"/>, the gradient starts on a circle around the focal point instead of at that point:
/// the gradient between two circles of SVG 2 (<c>fr</c>) and of the HTML canvas. The circle of a parameter <c>t</c> is
/// still centered <c>t</c> of the way from the focal point to the center, and its radius goes from
/// <see cref="FocalRadius"/> to <see cref="Radius"/>. Inside the circle of the focal point, the parameter is negative:
/// the color is the one of the first stop with <see cref="GradientSpreadMode.Pad"/>, and the gradient goes on toward
/// the point where its circles shrink to nothing with the other spread modes.
/// </para>
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
        FocalPoint = center;
    }

    /// <summary>Gets the center of the circle of offset 1; the position of offset 0 unless there is a <see cref="FocalPoint"/>.</summary>
    public PointD Center { get; }

    /// <summary>Gets the position of offset 0: the point the gradient spreads from. Defaults to <see cref="Center"/>.</summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The point, or the circle of <see cref="FocalRadius"/> around it, is not strictly inside the circle of <see cref="Center"/> and <see cref="Radius"/>, or the magnitude of a coordinate exceeds 16777216.</exception>
    public PointD FocalPoint
    {
        get;
        init
        {
            DrawingGuard.Point(value, nameof(value));
            if ((value != Center || FocalRadius != 0) && !(value.DistanceTo(Center) + FocalRadius < Radius))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The focal point must be strictly inside the circle of the gradient.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the radius of the circle of offset 0, around <see cref="FocalPoint"/>: the gradient starts on that circle.
    /// Defaults to 0: the gradient starts at the focal point.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, or the circle is not strictly inside the circle of <see cref="Center"/> and <see cref="Radius"/>.</exception>
    public double FocalRadius
    {
        get;
        init
        {
            DrawingGuard.Length(value, nameof(value));
            if (value != 0 && !(FocalPoint.DistanceTo(Center) + value < Radius))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The circle of the focal point must be strictly inside the circle of the gradient.");

            field = value;
        }
    }

    /// <summary>Gets the distance from the center at which the offset is 1.</summary>
    public double Radius { get; }
}
