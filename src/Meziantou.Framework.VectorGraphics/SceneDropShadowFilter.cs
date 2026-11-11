namespace Meziantou.Framework.VectorGraphics;

/// <summary>A shadow drawn under the element: its shape, blurred, moved and filled with a color, as the <c>feDropShadow</c> of SVG.</summary>
/// <param name="offsetX">The horizontal offset of the shadow, in the coordinates of the parent of the element.</param>
/// <param name="offsetY">The vertical offset of the shadow, in the coordinates of the parent of the element.</param>
/// <param name="standardDeviation">The standard deviation of the blur of the shadow: 0 for a sharp shadow.</param>
/// <param name="color">The color of the shadow; its alpha is multiplied by the alpha of the element.</param>
public sealed class SceneDropShadowFilter(double offsetX, double offsetY, double standardDeviation, SceneColor color) : SceneFilter
{
    /// <summary>Gets the horizontal offset of the shadow.</summary>
    public double OffsetX { get; } = offsetX;

    /// <summary>Gets the vertical offset of the shadow.</summary>
    public double OffsetY { get; } = offsetY;

    /// <summary>Gets the standard deviation of the blur of the shadow.</summary>
    public double StandardDeviation { get; } = standardDeviation;

    /// <summary>Gets the color of the shadow.</summary>
    public SceneColor Color { get; } = color;
}
