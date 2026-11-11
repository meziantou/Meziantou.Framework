namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A radial gradient, in the coordinates of the shape it paints: the color of offset 0 is at the focal point, and the
/// color of offset 1 on the circle of <see cref="CenterX"/>, <see cref="CenterY"/> and <see cref="Radius"/>. Outside
/// the circle, the color of the last stop is used.
/// </summary>
public sealed class RadialGradientPaint : ScenePaint
{
    /// <summary>Initializes a new instance of the <see cref="RadialGradientPaint"/> class whose focal point is the center of the circle.</summary>
    /// <param name="centerX">The x coordinate of the center of the circle.</param>
    /// <param name="centerY">The y coordinate of the center of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="stops">The color stops, by increasing offset.</param>
    public RadialGradientPaint(double centerX, double centerY, double radius, IEnumerable<SceneGradientStop> stops)
        : this(centerX, centerY, radius, centerX, centerY, stops)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RadialGradientPaint"/> class.</summary>
    /// <param name="centerX">The x coordinate of the center of the circle.</param>
    /// <param name="centerY">The y coordinate of the center of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="focalX">The x coordinate of the focal point, the position of offset 0.</param>
    /// <param name="focalY">The y coordinate of the focal point, the position of offset 0.</param>
    /// <param name="stops">The color stops, by increasing offset.</param>
    public RadialGradientPaint(double centerX, double centerY, double radius, double focalX, double focalY, IEnumerable<SceneGradientStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
        FocalX = focalX;
        FocalY = focalY;
        Stops = [.. stops];
    }

    /// <summary>Gets the x coordinate of the center of the circle of offset 1.</summary>
    public double CenterX { get; }

    /// <summary>Gets the y coordinate of the center of the circle of offset 1.</summary>
    public double CenterY { get; }

    /// <summary>Gets the radius of the circle of offset 1.</summary>
    public double Radius { get; }

    /// <summary>Gets the x coordinate of the focal point, the position of offset 0.</summary>
    public double FocalX { get; }

    /// <summary>Gets the y coordinate of the focal point, the position of offset 0.</summary>
    public double FocalY { get; }

    /// <summary>Gets the color stops.</summary>
    public IReadOnlyList<SceneGradientStop> Stops { get; }
}
