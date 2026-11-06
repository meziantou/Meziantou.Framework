namespace Meziantou.Framework.Charts;

/// <summary>A paint of a single color.</summary>
/// <param name="color">The color.</param>
public sealed class SolidPaint(ChartColor color) : ScenePaint
{
    /// <summary>Gets the color.</summary>
    public ChartColor Color { get; } = color;
}
