namespace Meziantou.Framework.Fonts;

/// <summary>
/// A glyph of a color font as a display list: what the COLR table describes as a graph of paints, flattened into
/// operations that are run in order.
/// </summary>
/// <remarks>
/// The list is built once, when a text is laid out, from the font data, which it validates; drawing it reads the
/// outlines of the glyphs it fills and allocates nothing. Coordinates are font units with the y axis up.
/// </remarks>
internal sealed class ColorGlyph
{
    public required ColorOp[] Ops { get; init; }

    public required ColorPaint[] Paints { get; init; }

    public required ColorStop[] Stops { get; init; }

    /// <summary>Gets the transformations (six numbers each), the geometry of the gradients and the boxes the operations and the paints refer to.</summary>
    public required double[] Numbers { get; init; }

    /// <summary>Gets the left of the box that contains everything the glyph draws.</summary>
    public required double XMin { get; init; }

    public required double YMin { get; init; }

    public required double XMax { get; init; }

    public required double YMax { get; init; }

    /// <summary>Gets the greatest number of layers that are open at the same time, the surface of the glyph included.</summary>
    public required int LayerDepth { get; init; }

    /// <summary>Gets the greatest number of clips that are open at the same time.</summary>
    public required int ClipDepth { get; init; }

    /// <summary>Gets the image of a glyph the font gives as a bitmap, which the display list draws; <see langword="null"/> for a glyph made of paints.</summary>
    public ColorImage? Image { get; init; }

    public bool IsEmpty => Ops.Length == 0 || !(XMax > XMin) || !(YMax > YMin);
}
