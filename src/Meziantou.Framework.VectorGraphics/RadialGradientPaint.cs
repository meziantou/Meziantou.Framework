namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A radial gradient, in the coordinates of the shape it paints: the color of offset 0 is at the focal point, and the
/// color of offset 1 on the circle of <see cref="CenterX"/>, <see cref="CenterY"/> and <see cref="Radius"/>. Outside
/// the circle, the gradient spreads as <see cref="SceneGradientPaint.Spread"/> says.
/// </summary>
public sealed class RadialGradientPaint : SceneGradientPaint
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
        : base(stops)
    {
        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
        FocalX = focalX;
        FocalY = focalY;
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

    /// <summary>
    /// Gets the radius of the circle of offset 0 around the focal point, as the SVG 2 <c>fr</c> attribute. The default
    /// is 0: offset 0 is the focal point itself.
    /// </summary>
    /// <remarks>
    /// The circle of an offset <c>t</c> is centered <c>t</c> of the way from the focal point to the center, and its radius
    /// goes from <see cref="FocalRadius"/> to <see cref="Radius"/>. Inside the circle of the focal point, the gradient
    /// has the color of its first stop when it is padded. A value that is negative or not finite is 0.
    /// </remarks>
    public double FocalRadius { get; init; }
}
