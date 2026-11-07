namespace Meziantou.Framework.Fonts;

/// <summary>
/// Decodes the outlines of the glyphs of a font fitted to the pixel grid of a size: hinted outlines are sharper at
/// small sizes, where an exactly scaled outline puts its edges across pixels.
/// </summary>
/// <remarks>
/// <para>
/// The outlines are given in font units, like those of <see cref="FontFace.DecodeOutline(int, OutlineSink, GlyphScratch, ref GlyphBudget)"/>:
/// multiplied by the size divided by <see cref="FontFace.UnitsPerEm"/>, their hinted coordinates are on the pixel
/// grid, with the origin of the glyph at a whole pixel. The y axis goes up.
/// </para>
/// <para>
/// <see cref="GlyphHinting.Full"/> runs the TrueType instructions of the font (OpenType specification, "TrueType
/// Instructions"), with the version 40 interpreter of FreeType hinting for black and white pixels
/// (<c>FT_LOAD_TARGET_MONO</c>) as the reference. A font program or a control value program that breaks a rule of
/// the specification makes the font unhinted; instructions of a glyph that break one make that glyph unhinted. The
/// work of the programs is bounded: each run charges every instruction to a <see cref="GlyphBudget"/>, and the
/// stack, the storage area, the control values, the functions and the twilight zone are sized from the font and
/// rented from the allocator.
/// </para>
/// <para>
/// A hinter keeps the state of the programs of the font at its size: it is used by one operation at a time, and must
/// be disposed to give its storage back to the allocator.
/// </para>
/// </remarks>
public sealed class GlyphHinter : IDisposable
{
    private readonly FontFace _face;
    private readonly TrueTypeHinter? _trueType;
    private readonly LightHinter? _light;
    private readonly double _unitsPerPixel;
    private bool _isDisposed;

    private GlyphHinter(FontFace face, double size, GlyphHinting hinting, TrueTypeHinter? trueType, LightHinter? light)
    {
        _face = face;
        Size = size;
        Hinting = hinting;
        _trueType = trueType;
        _light = light;
        _unitsPerPixel = face.UnitsPerEm / size;
    }

    /// <summary>Gets the size the outlines are hinted for, in pixels per em.</summary>
    public double Size { get; }

    /// <summary>
    /// Gets how the outlines are hinted: the mode asked for, or <see cref="GlyphHinting.Light"/> when
    /// <see cref="GlyphHinting.Full"/> was asked for a font whose instructions cannot be run.
    /// </summary>
    public GlyphHinting Hinting { get; }

    /// <summary>Creates a hinter for a font at a size: with <see cref="GlyphHinting.Full"/>, runs the font program and the control value program.</summary>
    /// <param name="face">The font.</param>
    /// <param name="size">The size, in pixels per em.</param>
    /// <param name="hinting">How the outlines are hinted.</param>
    /// <param name="scratch">The working storage of the glyphs the hinter measures to find the alignment zones of the font.</param>
    /// <param name="allocator">The storage of the state of the TrueType instructions.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a positive finite number, or <paramref name="hinting"/> is not a defined value.</exception>
    /// <exception cref="InvalidFontException">The data of the font is malformed.</exception>
    /// <exception cref="FontResourceLimitException">A program of the font exceeds the complexity limit, or its storage exceeds the allocation limit.</exception>
    public static GlyphHinter Create(FontFace face, double size, GlyphHinting hinting, GlyphScratch scratch, FontAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(scratch);
        ArgumentNullException.ThrowIfNull(allocator);
        if (!double.IsFinite(size) || size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size must be a positive finite number.");

        if (hinting is < GlyphHinting.None or > GlyphHinting.Full)
            throw new ArgumentOutOfRangeException(nameof(hinting), hinting, "The value is not a defined hinting mode.");

        if (hinting == GlyphHinting.Full && TrueTypeHinter.CanHint(face))
        {
#pragma warning disable CA2000 // The hinter owns it, and nothing can fail after it is created
            var trueType = TrueTypeHinter.Create(face, size, allocator);
            if (trueType is not null)
                return new GlyphHinter(face, size, GlyphHinting.Full, trueType, light: null);
#pragma warning restore CA2000
        }

        if (hinting == GlyphHinting.None)
            return new GlyphHinter(face, size, GlyphHinting.None, trueType: null, light: null);

        return new GlyphHinter(face, size, GlyphHinting.Light, trueType: null, LightHinter.Create(face, size, scratch));
    }

    /// <summary>Decodes the hinted outline of a glyph into a sink, in font units, and gives its advance.</summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="sink">The sink.</param>
    /// <param name="scratch">The working storage of the outline.</param>
    /// <param name="budget">The budget of the glyph, which its instructions are charged to.</param>
    /// <returns>
    /// The advance of the glyph, in pixels: a whole number with <see cref="GlyphHinting.Full"/>, the advance of the
    /// font scaled to the size otherwise.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The hinter is disposed.</exception>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public double DecodeOutline(int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(scratch);
        var advance = _face.GetAdvanceWidth(glyphId) / _unitsPerPixel;
        if ((uint)glyphId >= (uint)_face.GlyphCount || _face.TryDecodeMonochromeBitmap(glyphId, Size, sink, ref budget))
            return Hinting == GlyphHinting.Full ? Math.Round(advance, MidpointRounding.AwayFromZero) : advance;

        switch (Hinting)
        {
            case GlyphHinting.Full when _trueType!.TryHint(glyphId, scratch, ref budget):
                EmitHinted(sink, scratch);
                return _trueType.Advance / 64.0;

            case GlyphHinting.Full:
                // Instructions that break a rule of the specification: the glyph is drawn as the font draws it.
                _face.DecodeOutline(glyphId, sink, scratch, ref budget);
                return Math.Round(advance, MidpointRounding.AwayFromZero);

            case GlyphHinting.Light:
                _light!.Decode(_face, glyphId, sink, scratch, ref budget);
                return advance;

            default:
                _face.DecodeOutline(glyphId, sink, scratch, ref budget);
                return advance;
        }
    }

    /// <summary>Gives back the storage of the state of the instructions.</summary>
    public void Dispose()
    {
        _isDisposed = true;
        _trueType?.Dispose();
    }

    private void EmitHinted(OutlineSink sink, GlyphScratch scratch)
    {
        var hinted = _trueType!.Points;
        var contours = _trueType.Contours;
        var originX = _trueType.OriginX;
        scratch.Reset();
        var first = scratch.Add(hinted.Length);
        var points = scratch.Points[first..];
        var unit = _unitsPerPixel / 64;
        for (var i = 0; i < hinted.Length; i++)
        {
            points[i].X = (hinted[i].X - originX) * unit;
            points[i].Y = hinted[i].Y * unit;
            points[i].Flags = (hinted[i].Flags & HintPoint.OnCurve) != 0 ? GlyphPoint.OnCurve : 0;
        }

        foreach (var end in contours)
        {
            points[end].Flags |= GlyphPoint.EndOfContour;
        }

        GlyfOutlineReader.Emit(points, sink);
    }
}
