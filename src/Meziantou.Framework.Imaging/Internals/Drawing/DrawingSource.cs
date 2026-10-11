namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The paint of a run of pixels: one color, or one color per pixel (four samples each, laid out as <see cref="DrawingColor"/>).</summary>
internal readonly ref struct DrawingSource
{
    public DrawingSource(DrawingColor color)
    {
        Color = color;
        Colors = default;
    }

    public DrawingSource(ReadOnlySpan<ushort> colors)
    {
        Color = default;
        Colors = colors;
    }

    /// <summary>Gets the color of every pixel when <see cref="Colors"/> is empty.</summary>
    public DrawingColor Color { get; }

    /// <summary>Gets the colors of the pixels, or an empty span for a single color.</summary>
    public ReadOnlySpan<ushort> Colors { get; }
}
