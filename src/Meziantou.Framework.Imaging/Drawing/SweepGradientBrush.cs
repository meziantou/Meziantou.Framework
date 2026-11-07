using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A gradient around a point: the color is constant on the half-lines that start at that point.</summary>
/// <remarks>
/// <para>
/// The parameter of a position is the angle of the half-line from <see cref="Center"/> through it, measured clockwise on
/// the image from <see cref="StartAngle"/>, divided by the angle from <see cref="StartAngle"/> to <see cref="EndAngle"/>:
/// 0 on the half-line at <see cref="StartAngle"/>, 1 on the one at <see cref="EndAngle"/>. With the default angles, the
/// gradient goes once around the center, from its right. Where the angle is beyond <see cref="EndAngle"/>,
/// <see cref="GradientBrush.SpreadMode"/> gives the colors. When both angles are the same, the whole canvas has the color
/// of the last stop.
/// </para>
/// <para>
/// The colors change abruptly on the half-line at <see cref="StartAngle"/> unless the gradient ends on the color it
/// starts with: like every gradient, it is evaluated at the center of each pixel, and that edge is not antialiased.
/// The angle of a position does not come from the platform: the pixels are the same everywhere.
/// </para>
/// </remarks>
public sealed class SweepGradientBrush : GradientBrush
{
    /// <summary>Initializes a new instance of the <see cref="SweepGradientBrush"/> class that goes once around its center, starting on its right.</summary>
    /// <param name="center">The point the half-lines start at, in canvas coordinates.</param>
    /// <param name="stops">The stops, in increasing order of offset. They are copied.</param>
    /// <exception cref="ArgumentException">There is no stop, the offsets decrease, or a coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public SweepGradientBrush(PointD center, ReadOnlySpan<GradientStop> stops)
        : this(center, 0, 360, stops)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SweepGradientBrush"/> class between two angles.</summary>
    /// <param name="center">The point the half-lines start at, in canvas coordinates.</param>
    /// <param name="startAngle">The angle of offset 0, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="endAngle">The angle of offset 1, in degrees. Must not be less than <paramref name="startAngle"/>.</param>
    /// <param name="stops">The stops, in increasing order of offset. They are copied.</param>
    /// <exception cref="ArgumentException">There is no stop, the offsets decrease, or a coordinate or an angle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, or <paramref name="endAngle"/> is less than <paramref name="startAngle"/>.</exception>
    public SweepGradientBrush(PointD center, double startAngle, double endAngle, ReadOnlySpan<GradientStop> stops)
        : base(stops)
    {
        DrawingGuard.Point(center, nameof(center));
        DrawingGuard.Angle(startAngle, nameof(startAngle));
        DrawingGuard.Angle(endAngle, nameof(endAngle));
        if (endAngle < startAngle)
            throw new ArgumentOutOfRangeException(nameof(endAngle), endAngle, "The end angle must not be less than the start angle.");

        Center = center;
        StartAngle = startAngle;
        EndAngle = endAngle;
    }

    /// <summary>Gets the point the half-lines start at.</summary>
    public PointD Center { get; }

    /// <summary>Gets the angle of offset 0, in degrees from the positive x axis, clockwise on the image.</summary>
    public double StartAngle { get; }

    /// <summary>Gets the angle of offset 1, in degrees.</summary>
    public double EndAngle { get; }
}
