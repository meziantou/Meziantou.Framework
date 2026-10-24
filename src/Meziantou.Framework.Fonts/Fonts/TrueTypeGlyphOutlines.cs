namespace Meziantou.Framework.Fonts;

/// <summary>
/// The TrueType outlines of the <c>glyf</c> and <c>loca</c> tables of a font program that <see cref="FontLoader"/> cannot
/// load because it lacks tables, such as the programs embedded in PDF documents (<c>/FontFile2</c>), which often have only
/// the tables a PDF reader needs (ISO 32000-2, 9.9).
/// </summary>
/// <remarks>The outlines are those of the default instance of a variable font. The object is immutable and can be used from several threads.</remarks>
public sealed class TrueTypeGlyphOutlines
{
    private readonly ReadOnlyMemory<byte> _glyf;
    private readonly ReadOnlyMemory<byte> _loca;
    private readonly ReadOnlyMemory<byte> _horizontalMetrics;
    private readonly bool _hasLongLocations;
    private readonly int _horizontalMetricCount;

    /// <summary>Creates the outlines of the tables of a font program.</summary>
    /// <param name="glyf">The <c>glyf</c> table.</param>
    /// <param name="loca">The <c>loca</c> table.</param>
    /// <param name="hasLongLocations">Whether the offsets of the <c>loca</c> table are 32-bit numbers (the <c>indexToLocFormat</c> of the <c>head</c> table is 1).</param>
    /// <param name="glyphCount">The number of glyphs (the <c>numGlyphs</c> of the <c>maxp</c> table, or what the <c>loca</c> table gives).</param>
    /// <param name="horizontalMetrics">The <c>hmtx</c> table, or empty: the horizontal metrics of the glyphs.</param>
    /// <param name="horizontalMetricCount">The number of glyphs that have their own advance in <paramref name="horizontalMetrics"/> (the <c>numberOfHMetrics</c> of the <c>hhea</c> table).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphCount"/> or <paramref name="horizontalMetricCount"/> is negative.</exception>
    public TrueTypeGlyphOutlines(ReadOnlyMemory<byte> glyf, ReadOnlyMemory<byte> loca, bool hasLongLocations, int glyphCount, ReadOnlyMemory<byte> horizontalMetrics = default, int horizontalMetricCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(glyphCount);
        ArgumentOutOfRangeException.ThrowIfNegative(horizontalMetricCount);
        _glyf = glyf;
        _loca = loca;
        _hasLongLocations = hasLongLocations;
        GlyphCount = glyphCount;
        _horizontalMetrics = horizontalMetrics;
        _horizontalMetricCount = horizontalMetricCount;
    }

    /// <summary>Gets the number of glyphs.</summary>
    public int GlyphCount { get; }

    /// <summary>Decodes the outline of a glyph, in font units. A glyph identifier that is not a glyph of the font draws nothing.</summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="sink">The receiver of the contours.</param>
    /// <param name="scratch">The buffers of the decoding (the points of the glyph and of its components).</param>
    /// <param name="budget">The limits of the work of the decoding (the points, the components).</param>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public void DecodeOutline(int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(scratch);
        if ((uint)glyphId >= (uint)GlyphCount)
            return;

        var context = new GlyfContext
        {
            Loca = _loca.Span,
            Glyf = _glyf.Span,
            Hmtx = _horizontalMetrics.Span,
            HasLongLocations = _hasLongLocations,
            GlyphCount = GlyphCount,
            MetricCount = _horizontalMetricCount,
        };
        GlyfOutlineReader.Decode(context, glyphId, sink, scratch, ref budget);
    }
}
