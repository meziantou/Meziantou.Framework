namespace Meziantou.Framework.VectorGraphics;

/// <summary>A linear gradient, between two points in the coordinates of the shape it paints.</summary>
public sealed class LinearGradientPaint : SceneGradientPaint
{
    /// <summary>Initializes a new instance of the <see cref="LinearGradientPaint"/> class.</summary>
    /// <param name="x1">The x coordinate of the start of the gradient.</param>
    /// <param name="y1">The y coordinate of the start of the gradient.</param>
    /// <param name="x2">The x coordinate of the end of the gradient.</param>
    /// <param name="y2">The y coordinate of the end of the gradient.</param>
    /// <param name="stops">The color stops, by increasing offset.</param>
    public LinearGradientPaint(double x1, double y1, double x2, double y2, IEnumerable<SceneGradientStop> stops)
        : base(stops)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>Gets the x coordinate of the start of the gradient.</summary>
    public double X1 { get; }

    /// <summary>Gets the y coordinate of the start of the gradient.</summary>
    public double Y1 { get; }

    /// <summary>Gets the x coordinate of the end of the gradient.</summary>
    public double X2 { get; }

    /// <summary>Gets the y coordinate of the end of the gradient.</summary>
    public double Y2 { get; }
}
