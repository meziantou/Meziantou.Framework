using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// Converts the colors of the input to alpha (the luminance masks, https://drafts.fxtf.org/css-masking/#MaskValues): the alpha of
/// the result is the luminance of the premultiplied color (the luminance of the color multiplied by its alpha), its color is black.
/// </summary>
internal sealed record LuminanceToAlphaFilter : DisplayFilter
{
    public static LuminanceToAlphaFilter Instance { get; } = new();
}

/// <summary>
/// The filter of an SVG filter element (https://drafts.fxtf.org/filter-effects/#FilterElement): a graph of filter primitives,
/// each one reading the results of the previous ones (or the source graphic), in the coordinates of the layer. The result is
/// the result of the last primitive, clipped to <see cref="Region"/>.
/// </summary>
/// <param name="Primitives">The primitives in document order; the result of the graph is the result of the last one.</param>
/// <param name="Region">The filter region (https://drafts.fxtf.org/filter-effects/#FilterEffectsRegion) in the coordinates of the layer.</param>
internal sealed record FilterGraph(FilterPrimitive[] Primitives, PhysicalRect Region) : DisplayFilter;

/// <summary>The special inputs of a filter primitive (https://drafts.fxtf.org/filter-effects/#element-attrdef-filter-primitive-in); a non-negative input is the index of the result of a previous primitive.</summary>
internal static class FilterInput
{
    /// <summary>The graphic the filter is applied to.</summary>
    public const int SourceGraphic = -1;

    /// <summary>The alpha of the graphic the filter is applied to (black).</summary>
    public const int SourceAlpha = -2;

    /// <summary>A transparent black image (BackgroundImage, FillPaint and StrokePaint are not supported).</summary>
    public const int Transparent = -3;
}

/// <summary>The edge modes of the convolutions (https://drafts.fxtf.org/filter-effects/#element-attrdef-fegaussianblur-edgemode).</summary>
internal enum FilterEdgeMode : byte
{
    None,
    Duplicate,
    Wrap,
}

/// <summary>
/// A filter primitive (https://drafts.fxtf.org/filter-effects/#FilterPrimitivesOverview): it reads its inputs, computes its
/// result in its color space (sRGB or linearRGB, https://drafts.fxtf.org/filter-effects/#ColorInterpolationFiltersProperty)
/// and clips it to its subregion. The lengths are in the coordinates of the layer.
/// </summary>
internal abstract record FilterPrimitive
{
    /// <summary>The inputs (see <see cref="FilterInput"/>).</summary>
    public int[] Inputs { get; init; } = [FilterInput.SourceGraphic];

    /// <summary>The filter primitive subregion (https://drafts.fxtf.org/filter-effects/#FilterPrimitiveSubRegion), in the coordinates of the layer.</summary>
    public PhysicalRect Subregion { get; init; }

    /// <summary>Whether the primitive operates on linearRGB components (else sRGB).</summary>
    public bool LinearRgb { get; init; }
}

/// <summary>
/// feImage (https://drafts.fxtf.org/filter-effects/#feImageElement): the drawing of an image or of an element, in the coordinates of
/// the layer (sRGB), clipped to the subregion.
/// </summary>
internal sealed record ImagePrimitive(DisplayList Content) : FilterPrimitive;

/// <summary>feFlood: fills the subregion with a color (sRGB).</summary>
internal sealed record FloodPrimitive(DisplayColor Color) : FilterPrimitive;

/// <summary>feGaussianBlur.</summary>
internal sealed record GaussianBlurPrimitive(double DeviationX, double DeviationY, FilterEdgeMode EdgeMode) : FilterPrimitive;

/// <summary>feOffset.</summary>
internal sealed record OffsetPrimitive(double Dx, double Dy) : FilterPrimitive;

/// <summary>feDropShadow (the color is sRGB).</summary>
internal sealed record DropShadowPrimitive(double Dx, double Dy, double DeviationX, double DeviationY, DisplayColor Color) : FilterPrimitive;

/// <summary>feColorMatrix (a 4x5 matrix on unpremultiplied components).</summary>
internal sealed record ColorMatrixPrimitive(float[] Matrix) : FilterPrimitive;

/// <summary>feComponentTransfer: a lookup table of 256 values per channel (null for the identity), on unpremultiplied components.</summary>
internal sealed record ComponentTransferPrimitive(byte[]? Red, byte[]? Green, byte[]? Blue, byte[]? Alpha) : FilterPrimitive;

/// <summary>The operators of feComposite (https://drafts.fxtf.org/filter-effects/#element-attrdef-fecomposite-operator).</summary>
internal enum CompositeOperator : byte
{
    Over,
    In,
    Out,
    Atop,
    Xor,
    Lighter,
    Arithmetic,
}

/// <summary>feComposite: the first input composited over the second one (with the coefficients of arithmetic).</summary>
internal sealed record CompositePrimitive(CompositeOperator Operator, double K1, double K2, double K3, double K4) : FilterPrimitive;

/// <summary>feBlend: the first input blended over the second one.</summary>
internal sealed record BlendPrimitive(BlendMode Mode) : FilterPrimitive;

/// <summary>feMerge: the inputs composited over each other, the first one at the bottom.</summary>
internal sealed record MergePrimitive : FilterPrimitive;

/// <summary>feMorphology.</summary>
internal sealed record MorphologyPrimitive(bool Dilate, double RadiusX, double RadiusY) : FilterPrimitive;

/// <summary>feTurbulence (https://drafts.fxtf.org/filter-effects/#feTurbulenceElement), in the user space mapped to the layer by <see cref="Transform"/>.</summary>
internal sealed record TurbulencePrimitive(double BaseFrequencyX, double BaseFrequencyY, int Octaves, double Seed, bool StitchTiles, bool FractalNoise, DisplayMatrix Transform) : FilterPrimitive;

/// <summary>feTile: repeats the subregion of its input (<see cref="InputSubregion"/>) in its subregion.</summary>
internal sealed record TilePrimitive(PhysicalRect InputSubregion) : FilterPrimitive;

/// <summary>feConvolveMatrix.</summary>
internal sealed record ConvolveMatrixPrimitive(int OrderX, int OrderY, float[] Kernel, double Divisor, double Bias, int TargetX, int TargetY, FilterEdgeMode EdgeMode, bool PreserveAlpha) : FilterPrimitive;

/// <summary>feDisplacementMap: the first input displaced by the channels (0 = R, 1 = G, 2 = B, 3 = A) of the second one.</summary>
internal sealed record DisplacementMapPrimitive(double Scale, int XChannel, int YChannel) : FilterPrimitive;

/// <summary>The light sources of the lighting primitives.</summary>
internal abstract record FilterLight;

/// <summary>feDistantLight (the angles in degrees).</summary>
internal sealed record DistantLight(double Azimuth, double Elevation) : FilterLight;

/// <summary>fePointLight (in the coordinates of the layer).</summary>
internal sealed record PointLight(double X, double Y, double Z) : FilterLight;

/// <summary>feSpotLight (in the coordinates of the layer; the cone angle in degrees, or null).</summary>
internal sealed record SpotLight(double X, double Y, double Z, double PointsAtX, double PointsAtY, double PointsAtZ, double SpecularExponent, double? LimitingConeAngle) : FilterLight;

/// <summary>feDiffuseLighting and feSpecularLighting (the color is sRGB).</summary>
internal sealed record LightingPrimitive(bool Specular, FilterLight Light, DisplayColor Color, double SurfaceScale, double Constant, double SpecularExponent) : FilterPrimitive;
