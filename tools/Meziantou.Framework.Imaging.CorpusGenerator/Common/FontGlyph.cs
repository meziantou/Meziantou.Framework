namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A glyph of a <see cref="FontBuilder"/>: contours, or components, or nothing for a space.</summary>
internal sealed class FontGlyph
{
    public int AdvanceWidth { get; set; }

    /// <summary>Gets or sets the left side bearing written to <c>hmtx</c>; the minimum x of the outline by default.</summary>
    public int? LeftSideBearing { get; set; }

    public List<List<FontPoint>> Contours { get; } = [];

    public List<FontComponent> Components { get; } = [];

    public byte[] Instructions { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the OVERLAP_SIMPLE flag is set on the first point.</summary>
    public bool OverlapSimple { get; set; }

    /// <summary>Gets or sets the bounding box written in the glyph header, when it is not the one of the points.</summary>
    public FontBounds? BoundsOverride { get; set; }

    /// <summary>Gets or sets the bytes of the glyph, written as given: for malformed glyphs.</summary>
    public byte[]? RawData { get; set; }

    /// <summary>
    /// Gets the tuple variations of the glyph, for the <c>gvar</c> table of a variable font. The points of a glyph are
    /// those of its contours in order, or one point per component, then four phantom points: the origin, the advance,
    /// the top and the bottom.
    /// </summary>
    public List<FontTupleVariation> Variations { get; } = [];

    /// <summary>Creates a glyph without outline, such as a space.</summary>
    public static FontGlyph Empty(int advanceWidth) => new() { AdvanceWidth = advanceWidth };

    /// <summary>Creates a glyph made of closed polygons, wound as given.</summary>
    public static FontGlyph Polygons(int advanceWidth, params (int X, int Y)[][] polygons)
    {
        var glyph = new FontGlyph { AdvanceWidth = advanceWidth };
        foreach (var polygon in polygons)
        {
            glyph.Contours.Add([.. polygon.Select(point => new FontPoint(point.X, point.Y))]);
        }

        return glyph;
    }

    /// <summary>Creates a rectangle wound clockwise, the direction of the outer contours of TrueType.</summary>
    public static FontGlyph Rectangle(int advanceWidth, int xMin, int yMin, int xMax, int yMax)
    {
        return Polygons(advanceWidth, [(xMin, yMin), (xMin, yMax), (xMax, yMax), (xMax, yMin)]);
    }

    /// <summary>Creates a rectangular frame: an outer rectangle and a hole wound the other way.</summary>
    public static FontGlyph Frame(int advanceWidth, int xMin, int yMin, int xMax, int yMax, int thickness)
    {
        return Polygons(
            advanceWidth,
            [(xMin, yMin), (xMin, yMax), (xMax, yMax), (xMax, yMin)],
            [(xMin + thickness, yMin + thickness), (xMax - thickness, yMin + thickness), (xMax - thickness, yMax - thickness), (xMin + thickness, yMax - thickness)]);
    }

    /// <summary>Creates a composite glyph.</summary>
    public static FontGlyph Composite(int advanceWidth, params FontComponent[] components)
    {
        var glyph = new FontGlyph { AdvanceWidth = advanceWidth };
        glyph.Components.AddRange(components);
        return glyph;
    }
}
