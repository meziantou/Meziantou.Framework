namespace Meziantou.Framework.VectorGraphics;

/// <summary>The colors a primitive of a <see cref="SceneFilterGraph"/> computes with, as the SVG <c>color-interpolation-filters</c> property.</summary>
public enum SceneFilterColorSpace
{
    /// <summary>Linear light: the colors are decoded from sRGB, computed, and encoded again (<c>linearRGB</c>). The default of SVG.</summary>
    LinearRgb,

    /// <summary>The sRGB values as they are (<c>sRGB</c>), as the other filters of the scene compute.</summary>
    Srgb,
}
