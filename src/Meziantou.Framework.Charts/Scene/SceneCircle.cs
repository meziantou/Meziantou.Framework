namespace Meziantou.Framework.Charts;

/// <summary>A circle.</summary>
/// <param name="centerX">The x coordinate of the center.</param>
/// <param name="centerY">The y coordinate of the center.</param>
/// <param name="radius">The radius.</param>
public sealed class SceneCircle(double centerX, double centerY, double radius) : SceneShape
{
    /// <summary>Gets the x coordinate of the center.</summary>
    public double CenterX { get; } = centerX;

    /// <summary>Gets the y coordinate of the center.</summary>
    public double CenterY { get; } = centerY;

    /// <summary>Gets the radius.</summary>
    public double Radius { get; } = radius;
}
