using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// A text laid out with a font at a size: the glyphs of the text and their positions. It is what
/// <see cref="DrawingCanvas.FillText(Brush, TextLayout, PointD)"/> draws, and what measures a text before it is drawn.
/// </summary>
/// <remarks>
/// <para>
/// A layout is immutable and can be used by several threads at the same time, and drawn any number of times. Creating
/// it does all the work that depends on the text: drawing it allocates nothing.
/// </para>
/// <para>
/// <b>Lines.</b> The text is one line, or several when it contains line breaks: a line feed, a carriage return (alone
/// or followed by a line feed), a vertical tab, a form feed, a next line (U+0085), a line separator (U+2028) or a
/// paragraph separator (U+2029). A line break at the end of the text starts an empty line. Lines are not wrapped.
/// </para>
/// <para>
/// <b>Geometry.</b> Positions are in pixels from the top-left corner of the layout, with the y axis down. A line is as
/// tall as the line height of the font (ascent, descent and line gap); the line gap is split above and below the
/// glyphs, so the first baseline is half the line gap plus the ascent below the top. Glyphs are placed at fractions of
/// a pixel: nothing is rounded to the pixel grid and there is no hinting.
/// </para>
/// <para>
/// <b>Glyphs.</b> Each character has the glyph the character map of the font gives it, with the glyph of a variation
/// sequence when the font has one, and pairs of glyphs are moved by the kerning table of the font. A character the
/// font has no glyph for is drawn with the missing glyph of the font and reported by <see cref="HasMissingGlyphs"/>.
/// Control and format characters are not drawn; a tabulation moves to the next multiple of eight spaces.
/// </para>
/// <para>
/// A layout is validated when it is created: the outline of every glyph is decoded once, so drawing a layout only fails
/// when a limit of the image is reached or when the operation is canceled.
/// </para>
/// </remarks>
public sealed class TextLayout
{
    private const int TabSize = 8;

    private readonly Font _font;
    private readonly double _size;
    private readonly int[] _glyphs;

    // The origin of each glyph, on its baseline: x then y, from the top-left corner of the layout.
    private readonly double[] _positions;

    private TextLayout(Font font, double size, int[] glyphs, double[] positions, double width, int lineCount, RectangleD inkBounds, bool hasMissingGlyphs)
    {
        _font = font;
        _size = size;
        _glyphs = glyphs;
        _positions = positions;
        var metrics = font.GetMetrics(size);
        Width = width;
        Height = lineCount * metrics.LineHeight;
        Baseline = (metrics.LineGap / 2) + metrics.Ascent;
        LineCount = lineCount;
        InkBounds = inkBounds;
        HasMissingGlyphs = hasMissingGlyphs;
    }

    /// <summary>Gets the width of the layout, in pixels: the largest advance of its lines, spaces included.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the layout, in pixels: the height of a line times the number of lines.</summary>
    public double Height { get; }

    /// <summary>Gets the distance from the top of the layout to the baseline of its first line, in pixels.</summary>
    public double Baseline { get; }

    /// <summary>Gets the number of lines.</summary>
    public int LineCount { get; }

    /// <summary>
    /// Gets a rectangle that contains everything the layout draws when it is filled, relative to its top-left corner;
    /// empty when the layout draws nothing.
    /// </summary>
    /// <remarks>
    /// The rectangle contains the control points of the outlines. It is the exact bounding box of the glyphs of a font
    /// that has a point at each extremum of its curves, as fonts do. It can extend beyond the box of the layout: glyphs
    /// overhang their advance, and marks rise above the ascent.
    /// </remarks>
    public RectangleD InkBounds { get; }

    /// <summary>Gets a value indicating whether a character of the text has no glyph in the font.</summary>
    public bool HasMissingGlyphs { get; }

    /// <summary>Lays out a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size of the font, in pixels per em: a positive number up to 65536.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a positive number up to 65536.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of the font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">
    /// The text, its glyphs or one of its glyphs exceed <see cref="ImageResourceLimits.MaxTextLength"/>,
    /// <see cref="ImageResourceLimits.MaxTextGlyphs"/> or <see cref="ImageResourceLimits.MaxGlyphComplexity"/>. The limits
    /// are those of the configuration the font was loaded with.
    /// </exception>
    public static TextLayout Create(string text, Font font, double size)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        Font.ValidateSize(size, nameof(size));

        var face = font.Face;
        var limits = face.Data.Limits;
        if (text.Length > limits.MaxTextLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.TextLength, limits.MaxTextLength, text.Length);

        // The glyphs and their horizontal positions, in font units: whole numbers, which add up without error.
        var glyphs = new List<int>(text.Length);
        var units = new List<long>(text.Length);
        var lines = new List<int>(text.Length);
        var spaceAdvance = GetSpaceAdvance(face);
        long pen = 0;
        long widest = 0;
        var line = 0;
        var previous = -1;
        var hasMissingGlyphs = false;
        var index = 0;
        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            index += consumed;
            var codePoint = rune.Value;
            if (IsLineBreak(codePoint))
            {
                // A carriage return and the line feed that follows it are one line break.
                if (codePoint == '\r' && index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                widest = Math.Max(widest, pen);
                pen = 0;
                line++;
                previous = -1;
                continue;
            }

            if (codePoint == '\t')
            {
                var tab = (long)TabSize * spaceAdvance;
                if (tab > 0)
                {
                    pen = ((pen / tab) + 1) * tab;
                }

                previous = -1;
                continue;
            }

            if (IsNotDrawn(codePoint))
                continue;

            // The glyph of the character, or the one of the variation sequence it starts when the font has it.
            var glyph = face.MapCodePoint(codePoint);
            if (index < text.Length && Rune.DecodeFromUtf16(text.AsSpan(index), out var next, out var nextConsumed) == System.Buffers.OperationStatus.Done && IsVariationSelector(next.Value))
            {
                index += nextConsumed;
                if (face.MapVariation(codePoint, next.Value, out var variation) == CmapVariation.Glyph)
                {
                    glyph = variation;
                }
            }

            hasMissingGlyphs |= glyph == 0;
            if (glyphs.Count >= limits.MaxTextGlyphs)
                throw new ImageResourceLimitException(ImageResourceLimitKind.TextGlyphs, limits.MaxTextGlyphs, (long)glyphs.Count + 1);

            if (previous >= 0)
            {
                pen += face.GetLegacyKerning(previous, glyph);
            }

            glyphs.Add(glyph);
            units.Add(pen);
            lines.Add(line);
            pen += face.GetAdvanceWidth(glyph);
            previous = glyph;
        }

        widest = Math.Max(widest, pen);

        var metrics = font.GetMetrics(size);
        var scale = size / face.UnitsPerEm;
        var baseline = (metrics.LineGap / 2) + metrics.Ascent;
        var positions = new double[glyphs.Count * 2];
        for (var i = 0; i < glyphs.Count; i++)
        {
            positions[i * 2] = units[i] * scale;
            positions[(i * 2) + 1] = baseline + (lines[i] * metrics.LineHeight);
        }

        var glyphArray = glyphs.ToArray();
        var inkBounds = Measure(face, scale, glyphArray, positions);
        return new TextLayout(font, size, glyphArray, positions, widest * scale, line + 1, inkBounds, hasMissingGlyphs);
    }

    /// <summary>Creates a path with the outlines of the glyphs: what filling the layout at a point draws.</summary>
    /// <param name="origin">The position of the top-left corner of the layout.</param>
    /// <returns>
    /// A path with one closed contour per contour of the glyphs, to fill with <see cref="FillRule.NonZero"/>. Filling or
    /// stroking it changes the same pixels, to the same values, as filling or stroking the layout at the same point.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="origin"/> is not finite or is too large.</exception>
    /// <exception cref="ImageResourceLimitException">Decoding the outlines exceeds the allocation limit.</exception>
    public VectorPath ToPath(PointD origin)
    {
        DrawingGuard.Point(origin, nameof(origin));
        var builder = new VectorPathBuilder();
        var sink = new GlyphPathBuilderSink(builder);
        var face = _font.Face;
        var scale = _size / face.UnitsPerEm;
        using var scratch = new GlyphScratch(AllocationScope.Create(face.Data.Configuration, "TextLayout"));
        for (var i = 0; i < _glyphs.Length; i++)
        {
            sink.Begin(Affine2D.ForGlyph(scale, origin.X + _positions[i * 2], origin.Y + _positions[(i * 2) + 1]));
            var budget = new GlyphBudget(face.Data.Limits);
            face.DecodeOutline(_glyphs[i], sink, scratch, ref budget);
        }

        return builder.Build();
    }

    /// <summary>Sends the outlines of the glyphs, placed on the canvas, to the sink of a draw call.</summary>
    /// <param name="target">The sink of the fill or of the stroke.</param>
    /// <param name="glyphSink">The sink that places a glyph.</param>
    /// <param name="scratch">The working storage of the outlines.</param>
    /// <param name="originX">The horizontal position of the top-left corner of the layout.</param>
    /// <param name="originY">The vertical position of the top-left corner of the layout.</param>
    /// <param name="edges">
    /// The edges of a fill, or <see langword="null"/> for a stroke. The glyphs of a fill are given one orientation, so
    /// that glyphs that overlap add up whatever the convention of their font.
    /// </param>
    internal void Emit(PathSink target, GlyphPathSink glyphSink, GlyphScratch scratch, double originX, double originY, EdgeBuilder? edges)
    {
        var face = _font.Face;
        var scale = _size / face.UnitsPerEm;
        var limits = face.Data.Limits;
        for (var i = 0; i < _glyphs.Length; i++)
        {
            var first = edges?.Count ?? 0;
            glyphSink.Begin(target, Affine2D.ForGlyph(scale, originX + _positions[i * 2], originY + _positions[(i * 2) + 1]));
            var budget = new GlyphBudget(limits);
            face.DecodeOutline(_glyphs[i], glyphSink, scratch, ref budget);

            // The contours of the glyph are complete: each one was closed. TrueType and CFF outlines turn in opposite
            // directions; the ones that turn the other way are reversed.
            if (edges is not null && glyphSink.SignedArea < 0)
            {
                edges.NegateDirections(first);
            }
        }

        target.End();
    }

    /// <summary>Decodes each glyph of the text once, which validates it, and measures what the glyphs draw.</summary>
    private static RectangleD Measure(FontFace face, double scale, int[] glyphs, double[] positions)
    {
        if (glyphs.Length == 0)
            return RectangleD.Empty;

        // The bounds of each distinct glyph, in font units: four values, or none for a glyph without outline.
        var bounds = new Dictionary<int, (double MinX, double MinY, double MaxX, double MaxY)?>();
        var sink = new GlyphBoundsSink();
        using var scratch = new GlyphScratch(AllocationScope.Create(face.Data.Configuration, "TextLayout"));
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        for (var i = 0; i < glyphs.Length; i++)
        {
            if (!bounds.TryGetValue(glyphs[i], out var glyphBounds))
            {
                sink.Reset();
                var budget = new GlyphBudget(face.Data.Limits);
                face.DecodeOutline(glyphs[i], sink, scratch, ref budget);
                glyphBounds = sink.IsEmpty ? null : (sink.MinX, sink.MinY, sink.MaxX, sink.MaxY);
                bounds.Add(glyphs[i], glyphBounds);
            }

            if (glyphBounds is not { } box)
                continue;

            // The corners, placed as the points of the outline are: the y axis of a font goes up.
            var transform = Affine2D.ForGlyph(scale, positions[i * 2], positions[(i * 2) + 1]);
            left = Math.Min(left, transform.TransformX(box.MinX, box.MinY));
            right = Math.Max(right, transform.TransformX(box.MaxX, box.MinY));
            top = Math.Min(top, transform.TransformY(box.MinX, box.MaxY));
            bottom = Math.Max(bottom, transform.TransformY(box.MinX, box.MinY));
        }

        return left <= right ? RectangleD.FromLTRB(left, top, right, bottom) : RectangleD.Empty;
    }

    /// <summary>Gets the advance of a space, in font units; a quarter of an em for a font that has no space.</summary>
    private static int GetSpaceAdvance(FontFace face)
    {
        var space = face.MapCodePoint(' ');
        return space == 0 ? face.UnitsPerEm / 4 : face.GetAdvanceWidth(space);
    }

    private static bool IsLineBreak(int codePoint) => codePoint is '\n' or '\r' or 0x0B or 0x0C or 0x85 or 0x2028 or 0x2029;

    private static bool IsVariationSelector(int codePoint) => codePoint is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>
    /// Determines whether a character has no visible form: the control characters, and the format characters that only
    /// change how the characters around them behave.
    /// </summary>
    private static bool IsNotDrawn(int codePoint)
    {
        return codePoint is < 0x20
            or (>= 0x7F and <= 0x9F)
            or 0xAD
            or 0x034F
            or 0x061C
            or (>= 0x180B and <= 0x180F)
            or (>= 0x200B and <= 0x200F)
            or (>= 0x202A and <= 0x202E)
            or (>= 0x2060 and <= 0x206F)
            or (>= 0xFE00 and <= 0xFE0F)
            or 0xFEFF
            or (>= 0xE0000 and <= 0xE0FFF);
    }
}
