namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The colors a primitive of a <see cref="FilterGraph"/> computes with: <c>color-interpolation-filters</c> of SVG.</summary>
public enum FilterColorSpace
{
    /// <summary>The light of sRGB: the samples are decoded by the transfer function of sRGB before the primitive, and encoded after it. This is the default of SVG.</summary>
    LinearRgb = 0,

    /// <summary>The stored samples, as they are.</summary>
    Srgb = 1,
}
