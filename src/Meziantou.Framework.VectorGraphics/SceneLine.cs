namespace Meziantou.Framework.VectorGraphics;

/// <summary>A straight line segment.</summary>
/// <param name="x1">The x coordinate of the start.</param>
/// <param name="y1">The y coordinate of the start.</param>
/// <param name="x2">The x coordinate of the end.</param>
/// <param name="y2">The y coordinate of the end.</param>
public sealed class SceneLine(double x1, double y1, double x2, double y2) : SceneShape
{
    /// <summary>Gets the x coordinate of the start.</summary>
    public double X1 { get; } = x1;

    /// <summary>Gets the y coordinate of the start.</summary>
    public double Y1 { get; } = y1;

    /// <summary>Gets the x coordinate of the end.</summary>
    public double X2 { get; } = x2;

    /// <summary>Gets the y coordinate of the end.</summary>
    public double Y2 { get; } = y2;
}
