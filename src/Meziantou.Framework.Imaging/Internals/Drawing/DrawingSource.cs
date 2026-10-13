using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The paint of a run of pixels: one color, or one color per pixel (four samples each, laid out as <see cref="DrawingColor"/>),
/// and the operator that combines it with the pixels.
/// </summary>
internal readonly ref struct DrawingSource
{
    public DrawingSource(DrawingColor color, CompositeMode mode = CompositeMode.SourceOver)
    {
        Color = color;
        Colors = default;
        Mode = mode;
    }

    public DrawingSource(ReadOnlySpan<ushort> colors, CompositeMode mode = CompositeMode.SourceOver)
    {
        Color = default;
        Colors = colors;
        Mode = mode;
    }

    /// <summary>Gets the color of every pixel when <see cref="Colors"/> is empty.</summary>
    public DrawingColor Color { get; }

    /// <summary>Gets the colors of the pixels, or an empty span for a single color.</summary>
    public ReadOnlySpan<ushort> Colors { get; }

    /// <summary>Gets the operator that combines the paint with the pixels.</summary>
    public CompositeMode Mode { get; }
}
