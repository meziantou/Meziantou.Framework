using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The paint of a run of pixels: one color, or one color per pixel (four samples each, laid out as <see cref="DrawingColor"/>),
/// the blend mode that mixes it with the pixels, the operator that combines it with them, and whether they are
/// combined in linear light.
/// </summary>
internal readonly ref struct DrawingSource
{
    public DrawingSource(DrawingColor color, CompositeMode mode = CompositeMode.SourceOver, BlendMode blend = BlendMode.Normal, byte transfer = 0, LinearLightCurves? curves = null)
    {
        Color = color;
        Colors = default;
        Mode = mode;
        Blend = blend;
        Transfer = transfer;
        Curves = curves;
    }

    public DrawingSource(ReadOnlySpan<ushort> colors, CompositeMode mode = CompositeMode.SourceOver, BlendMode blend = BlendMode.Normal, byte transfer = 0, LinearLightCurves? curves = null)
    {
        Color = default;
        Colors = colors;
        Mode = mode;
        Blend = blend;
        Transfer = transfer;
        Curves = curves;
    }

    /// <summary>Gets the color of every pixel when <see cref="Colors"/> is empty.</summary>
    public DrawingColor Color { get; }

    /// <summary>Gets the colors of the pixels, or an empty span for a single color.</summary>
    public ReadOnlySpan<ushort> Colors { get; }

    /// <summary>Gets the operator that combines the paint with the pixels.</summary>
    public CompositeMode Mode { get; }

    /// <summary>Gets the blend mode that mixes the color of the paint with the color of the pixels, before the operator.</summary>
    public BlendMode Blend { get; }

    /// <summary>
    /// Gets the transfer characteristics (<see cref="CicpTransfer"/>) whose function decodes the samples to the linear
    /// light they are combined in, or 0 when they are combined as they are stored.
    /// </summary>
    public byte Transfer { get; }

    /// <summary>
    /// Gets the tone curves of the ICC profile of the image, which decode the samples to the linear light they are
    /// combined in, or <see langword="null"/> when <see cref="Transfer"/> says how they are combined.
    /// </summary>
    public LinearLightCurves? Curves { get; }

    /// <summary>Gets a value indicating whether the paint is combined with the pixels in linear light.</summary>
    public bool IsLinearLight => Transfer != 0 || Curves is not null;

    /// <summary>Gets the paint of a part of the run: the same paint for a single color, the colors of those pixels otherwise.</summary>
    /// <param name="start">The first pixel of the part.</param>
    /// <param name="length">The number of pixels of the part.</param>
    /// <returns>The paint of the part.</returns>
    public DrawingSource Slice(int start, int length)
        => Colors.IsEmpty ? this : new DrawingSource(Colors.Slice(start * 4, length * 4), Mode, Blend, Transfer, Curves);
}
