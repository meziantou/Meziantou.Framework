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
/// <para>
/// The two circles can be anywhere, as for <c>createRadialGradient</c> of the HTML canvas: the circles of the
/// parameters <c>t</c> (all of them, not only those from 0 to 1) whose radius <c>r(t)</c> is not negative form a cone,
/// and a position has the largest <c>t</c> whose circle goes through it. A position that no such circle goes through
/// (outside the cone) has no color: nothing is painted there, whatever the spread mode. When the first circle is
/// inside the second one, every position has a parameter, and this is the gradient described above. Two circles of
/// the same center and the same radius make no cone: with <see cref="GradientSpreadMode.Pad"/>, the inside of the
/// circle has the color of the first stop and the rest of the canvas the color of the last one; with the other
/// spread modes, nothing is painted.
/// </para>
/// <para>
/// With a <see cref="RadiusY"/> that is not <see cref="Radius"/>, the color is constant on ellipses instead: the circles
/// above are stretched along y around <see cref="Center"/> by <c>RadiusY / Radius</c>, before <see cref="GradientBrush.Transform"/>,
/// which can still turn them. This is the radial gradient with a <see cref="GradientBrush.Transform"/> that scales y
/// around the center, written with the half-axes of the ellipse.
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
        RadiusY = radius;
        FocalPoint = center;
    }

    /// <summary>Initializes a new instance of the <see cref="RadialGradientBrush"/> class whose color is constant on ellipses around a point.</summary>
    /// <param name="center">The position of offset 0, in canvas coordinates.</param>
    /// <param name="radiusX">The half-axis along x of the ellipse of offset 1. Must be zero or positive.</param>
    /// <param name="radiusY">The half-axis along y of the ellipse of offset 1. Must be zero or positive.</param>
    /// <param name="stops">The stops, in increasing order of offset. They are copied.</param>
    /// <remarks>
    /// The gradient is the circular gradient of radius <paramref name="radiusX"/>, stretched along y around
    /// <paramref name="center"/> by <c>radiusY / radiusX</c>, before <see cref="GradientBrush.Transform"/>: the parameter of
    /// a position <c>p</c> is <c>sqrt(dx^2 + (dy radiusX / radiusY)^2) / radiusX</c>, with <c>(dx, dy) = p - center</c>.
    /// When one of the half-axes is zero and the other is not, the whole canvas has the color of the last stop.
    /// </remarks>
    /// <exception cref="ArgumentException">There is no stop, the offsets decrease, or a coordinate or a half-axis is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or a half-axis is negative or exceeds 16777216.</exception>
    public RadialGradientBrush(PointD center, double radiusX, double radiusY, ReadOnlySpan<GradientStop> stops)
        : base(stops)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        Center = center;
        Radius = radiusX;
        RadiusY = radiusY;
        FocalPoint = center;
    }

    /// <summary>Gets the center of the circle of offset 1; the position of offset 0 unless there is a <see cref="FocalPoint"/>.</summary>
    public PointD Center { get; }

    /// <summary>Gets the position of offset 0: the point the gradient spreads from, or the center of the circle it starts on. Defaults to <see cref="Center"/>.</summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public PointD FocalPoint
    {
        get;
        init
        {
            DrawingGuard.Point(value, nameof(value));
            field = value;
        }
    }

    /// <summary>
    /// Gets the radius of the circle of offset 0, around <see cref="FocalPoint"/>: the gradient starts on that circle.
    /// Defaults to 0: the gradient starts at the focal point.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 16777216.</exception>
    public double FocalRadius
    {
        get;
        init
        {
            DrawingGuard.Length(value, nameof(value));
            field = value;
        }
    }

    /// <summary>Gets the distance from the center at which the offset is 1: the radius of the circle of offset 1, or the half-axis along x of its ellipse.</summary>
    public double Radius { get; }

    /// <summary>Gets the half-axis along y of the ellipse of offset 1: <see cref="Radius"/> for a circular gradient.</summary>
    /// <remarks>
    /// When it differs from <see cref="Radius"/>, every circle of the gradient is stretched along y around
    /// <see cref="Center"/> by <c>RadiusY / Radius</c>: <see cref="FocalPoint"/> is the position of offset 0 after that
    /// stretch, and <see cref="FocalRadius"/> the half-axis along x of the ellipse of offset 0.
    /// </remarks>
    public double RadiusY { get; }

    /// <summary>Gets a value indicating whether the circles of the gradient are stretched into ellipses.</summary>
    internal bool IsElliptic => RadiusY != Radius;

    /// <summary>Gets the focal point before the stretch of an elliptic gradient: the one of the circular gradient it comes from.</summary>
    internal PointD CircleFocalPoint => IsElliptic && RadiusY != 0 ? new PointD(FocalPoint.X, Center.Y + ((FocalPoint.Y - Center.Y) * Radius / RadiusY)) : FocalPoint;
}
