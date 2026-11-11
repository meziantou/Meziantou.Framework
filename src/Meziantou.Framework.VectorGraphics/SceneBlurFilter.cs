namespace Meziantou.Framework.VectorGraphics;

/// <summary>A Gaussian blur, as the <c>feGaussianBlur</c> of SVG.</summary>
/// <param name="standardDeviation">The standard deviation of the blur, in the coordinates of the parent of the element: 0 for no blur.</param>
public sealed class SceneBlurFilter(double standardDeviation) : SceneFilter
{
    /// <summary>Gets the standard deviation of the blur, in the coordinates of the parent of the element.</summary>
    public double StandardDeviation { get; } = standardDeviation;
}
