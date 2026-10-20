namespace Meziantou.Framework.Fonts;

/// <summary>
/// A glyph of a color font as a display list: what the COLR table describes as a graph of paints, flattened into
/// operations that are run in order.
/// </summary>
/// <remarks>
/// The list is built once, when a text is laid out, from the font data, which it validates; drawing it reads the
/// outlines of the glyphs it fills and allocates nothing. Coordinates are font units with the y axis up.
/// </remarks>
public sealed class ColorGlyph
{
    private readonly ColorOp[] _ops;
    private readonly ColorPaint[] _paints;
    private readonly ColorStop[] _stops;
    private readonly double[] _numbers;

    internal ColorGlyph(ColorOp[] ops, ColorPaint[] paints, ColorStop[] stops, double[] numbers)
    {
        _ops = ops;
        _paints = paints;
        _stops = stops;
        _numbers = numbers;
    }

    /// <summary>Gets the operations of the display list, in the order they are run.</summary>
    public ReadOnlySpan<ColorOp> Ops => _ops;

    /// <summary>Gets the paints the operations fill with.</summary>
    public ReadOnlySpan<ColorPaint> Paints => _paints;

    /// <summary>Gets the color stops of the gradients, and the colors of the solid paints.</summary>
    public ReadOnlySpan<ColorStop> Stops => _stops;

    /// <summary>Gets the transformations (six numbers each), the geometry of the gradients and the boxes the operations and the paints refer to.</summary>
    public ReadOnlySpan<double> Numbers => _numbers;

    /// <summary>Gets the left of the box that contains everything the glyph draws.</summary>
    public double XMin { get; internal init; }

    /// <summary>Gets the bottom of the box that contains everything the glyph draws.</summary>
    public double YMin { get; internal init; }

    /// <summary>Gets the right of the box that contains everything the glyph draws.</summary>
    public double XMax { get; internal init; }

    /// <summary>Gets the top of the box that contains everything the glyph draws.</summary>
    public double YMax { get; internal init; }

    /// <summary>Gets the greatest number of layers that are open at the same time, the surface of the glyph included.</summary>
    public int LayerDepth { get; internal init; }

    /// <summary>Gets the greatest number of clips that are open at the same time.</summary>
    public int ClipDepth { get; internal init; }

    /// <summary>Gets the image of a glyph the font gives as a bitmap, which the display list draws; <see langword="null"/> for a glyph made of paints.</summary>
    public ColorImage? Image { get; private init; }

    /// <summary>Gets a value indicating whether the glyph draws nothing.</summary>
    public bool IsEmpty => _ops.Length == 0 || !(XMax > XMin) || !(YMax > YMin);

    /// <summary>Creates the display list of a glyph the font gives as a bitmap: its image, drawn in a rectangle.</summary>
    /// <param name="image">The decoded image of the glyph.</param>
    /// <param name="left">The left of the rectangle, in font units.</param>
    /// <param name="top">The top of the rectangle, in font units.</param>
    /// <param name="width">The width of the rectangle, in font units.</param>
    /// <param name="height">The height of the rectangle, in font units.</param>
    /// <exception cref="InvalidFontException">The rectangle is outside the coordinates an outline can have.</exception>
    public static ColorGlyph FromImage(ColorImage image, double left, double top, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        FontErrors.EnsureCoordinates(left, top);
        FontErrors.EnsureCoordinates(left + width, top - height);

        // The numbers are the left and the top of the image, and the size of one of its pixels.
        ColorOp[] ops = [new ColorOp(ColorOpKind.FillImage, 0, -1, 0)];
        ColorPaint[] paints = [new ColorPaint(ColorPaintKind.Image, -1, 0, 0, 0, 0)];
        return new ColorGlyph(ops, paints, [], [left, top, width / image.Width, height / image.Height])
        {
            XMin = left,
            YMin = top - height,
            XMax = left + width,
            YMax = top,
            LayerDepth = 1,
            ClipDepth = 0,
            Image = image,
        };
    }
}
