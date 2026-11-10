namespace Meziantou.Framework.VectorGraphics;

/// <summary>A paint of a single color.</summary>
/// <param name="color">The color.</param>
public sealed class SolidPaint(SceneColor color) : ScenePaint
{
    /// <summary>Gets the color.</summary>
    public SceneColor Color { get; } = color;
}
