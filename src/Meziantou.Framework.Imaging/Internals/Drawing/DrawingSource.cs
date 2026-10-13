using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The paint of a run of pixels: one color, or one color per pixel (four samples each, laid out as <see cref="DrawingColor"/>),
/// the blend mode that mixes it with the pixels, the operator that combines it with them, and whether they are
/// combined in linear light.
/// </summary>
internal readonly ref struct DrawingSource
{
    public DrawingSource(DrawingColor color, CompositeMode mode = CompositeMode.SourceOver, BlendMode blend = BlendMode.Normal, bool isLinear = false)
    {
        Color = color;
        Colors = default;
        Mode = mode;
        Blend = blend;
        IsLinear = isLinear;
    }

    public DrawingSource(ReadOnlySpan<ushort> colors, CompositeMode mode = CompositeMode.SourceOver, BlendMode blend = BlendMode.Normal, bool isLinear = false)
    {
        Color = default;
        Colors = colors;
        Mode = mode;
        Blend = blend;
        IsLinear = isLinear;
    }

    /// <summary>Gets the color of every pixel when <see cref="Colors"/> is empty.</summary>
    public DrawingColor Color { get; }

    /// <summary>Gets the colors of the pixels, or an empty span for a single color.</summary>
    public ReadOnlySpan<ushort> Colors { get; }

    /// <summary>Gets the operator that combines the paint with the pixels.</summary>
    public CompositeMode Mode { get; }

    /// <summary>Gets the blend mode that mixes the color of the paint with the color of the pixels, before the operator.</summary>
    public BlendMode Blend { get; }

    /// <summary>Gets a value indicating whether the samples are sRGB values that are combined in linear light, instead of as they are stored.</summary>
    public bool IsLinear { get; }
}
