using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// A text laid out: its lines, its glyphs and their positions. It is what
/// <see cref="DrawingCanvas.FillText(Brush, TextLayout, PointD)"/> draws, and what measures a text before it is drawn.
/// </summary>
/// <remarks>
/// <para>
/// A layout is immutable and can be used by several threads at the same time, and drawn any number of times. Creating
/// it does all the work that depends on the text: drawing it allocates nothing.
/// </para>
/// <para>
/// <b>Paragraphs and lines.</b> A paragraph ends at a line break of the text: a line feed, a carriage return (alone or
/// followed by a line feed), a vertical tab, a form feed, a next line (U+0085), a line separator (U+2028) or a
/// paragraph separator (U+2029); a line break at the end of the text starts an empty line. With a
/// <see cref="TextLayoutOptions.MaxWidth"/>, the lines of a paragraph are wrapped where the Unicode line breaking
/// algorithm (UAX #14) allows it, at soft hyphens and, on request, where hyphenation patterns allow it. The white
/// space that ends a wrapped line hangs outside its width.
/// </para>
/// <para>
/// <b>Direction.</b> Each paragraph is ordered by the Unicode Bidirectional Algorithm (UAX #9): left-to-right and
/// right-to-left text can be mixed, and each line is put in display order.
/// </para>
/// <para>
/// <b>Geometry.</b> Positions are in pixels from the top-left corner of the layout, with the y axis down. A line is as
/// tall as its fonts ask (the largest ascent and descent, each with half of the line gap), or as
/// <see cref="TextLayoutOptions.LineHeight"/> says. Glyphs are placed at fractions of a pixel: nothing is rounded to
/// the pixel grid and there is no hinting.
/// </para>
/// <para>
/// <b>Shaping.</b> The text is shaped with the OpenType Layout tables of its fonts, one run per script, direction and
/// font: characters are put in the form the font has glyphs for (composed or decomposed), the <c>GSUB</c> table
/// substitutes glyphs (ligatures, contextual forms) and the <c>GPOS</c> table positions them (kerning, marks), with
/// the features a client applies by default to horizontal text and the ones of the style; a font without <c>GPOS</c>
/// kerning is kerned by its <c>kern</c> table. A change of paint or decoration inside a word does not interrupt
/// shaping.
/// </para>
/// <para>
/// A character is drawn with the first font of the stack of its style that has it; a character no font has is drawn
/// with the missing glyph of the primary font and reported by <see cref="HasMissingGlyphs"/>. Control characters are
/// not drawn, format characters are invisible, and a tabulation moves to the next tabulation stop.
/// </para>
/// <para>
/// A layout is validated when it is created: the outline of every glyph is decoded once, so drawing a layout only fails
/// when a limit of the image is reached or when the operation is canceled.
/// </para>
/// </remarks>
public sealed class TextLayout
{
    private readonly Font[] _fonts;
    private readonly Brush?[] _brushes;
    private readonly GlyphRun[] _runs;
    private readonly int[] _glyphs;

    // The origin of each glyph, on its baseline: x then y, from the top-left corner of the layout.
    private readonly double[] _positions;
    private readonly double[] _anchors;
    private readonly DecorationRectangle[] _decorations;
    private readonly TextLine[] _lines;

    private TextLayout(TextLayoutData data)
    {
        _fonts = data.Fonts;
        _brushes = data.Brushes;
        _runs = data.Runs;
        _glyphs = data.Glyphs;
        _positions = data.Positions;
        _anchors = data.Anchors;
        _decorations = data.Decorations;
        _lines = data.Lines;
        Lines = new ReadOnlyCollection<TextLine>(_lines);
        Width = data.Width;
        Height = data.Height;
        IsTruncated = data.IsTruncated;
        HasMissingGlyphs = data.HasMissingGlyphs;
        InkBounds = Measure();
    }

    /// <summary>Gets the width of the text, in pixels: the width of its widest line, with the indent of a first line.</summary>
    /// <remarks>
    /// The white space that ends a wrapped line hangs and does not count; the spaces that end a paragraph do. Lines are
    /// aligned in <see cref="TextLayoutOptions.MaxWidth"/> when it is set, which can be wider than the text.
    /// </remarks>
    public double Width { get; }

    /// <summary>Gets the height of the layout, in pixels: the sum of the heights of its lines.</summary>
    public double Height { get; }

    /// <summary>Gets the distance from the top of the layout to the baseline of its first line, in pixels.</summary>
    public double Baseline => _lines[0].Baseline;

    /// <summary>Gets the number of lines.</summary>
    public int LineCount => _lines.Length;

    /// <summary>Gets the lines, from the top: the part of the text each one holds and where it is.</summary>
    public IReadOnlyList<TextLine> Lines { get; }

    /// <summary>
    /// Gets a rectangle that contains everything the layout draws when it is filled, relative to its top-left corner;
    /// empty when the layout draws nothing.
    /// </summary>
    /// <remarks>
    /// The rectangle contains the control points of the outlines and the decorations. It is the exact bounding box of
    /// the glyphs of a font that has a point at each extremum of its curves, as fonts do. It can extend beyond the box
    /// of the layout: glyphs overhang their advance, and marks rise above the ascent.
    /// </remarks>
    public RectangleD InkBounds { get; }

    /// <summary>Gets a value indicating whether a character of the text has no glyph in the fonts of its style.</summary>
    public bool HasMissingGlyphs { get; }

    /// <summary>
    /// Gets a value indicating whether a part of the text is not in the layout: it did not fit
    /// <see cref="TextLayoutOptions.MaxLines"/> or <see cref="TextLayoutOptions.MaxHeight"/>, or a line was trimmed to
    /// <see cref="TextLayoutOptions.MaxWidth"/>.
    /// </summary>
    public bool IsTruncated { get; }

    internal int BrushCount => _brushes.Length;

    /// <summary>Lays out a text with one font, on one line per paragraph.</summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size of the font, in pixels per em: a positive number up to 65536.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="font"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a positive number up to 65536.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of the font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of the font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">
    /// The text, its glyphs, its lines or one of its glyphs exceed <see cref="ImageResourceLimits.MaxTextLength"/>,
    /// <see cref="ImageResourceLimits.MaxTextGlyphs"/>, <see cref="ImageResourceLimits.MaxTextLines"/> or
    /// <see cref="ImageResourceLimits.MaxGlyphComplexity"/>. The limits are those of the configuration the font was
    /// loaded with.
    /// </exception>
    public static TextLayout Create(string text, Font font, double size)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        Font.ValidateSize(size, nameof(size));
        return Create(text, new TextStyle(font, size), TextLayoutOptions.Default);
    }

    /// <summary>Lays out a text with one style.</summary>
    /// <param name="text">The text.</param>
    /// <param name="style">The fonts, the size and the other settings of the text.</param>
    /// <param name="options">How the text is wrapped and aligned, or <see langword="null"/> for <see cref="TextLayoutOptions.Default"/>.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="style"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of a font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of a font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">
    /// The text, its glyphs, its lines or one of its glyphs exceed a limit. The limits are those of the configuration
    /// the primary font of the style was loaded with.
    /// </exception>
    public static TextLayout Create(string text, TextStyle style, TextLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        return new TextLayout(TextLayoutEngine.Create(text, [new TextSpan(0, text.Length, style)], options ?? TextLayoutOptions.Default));
    }

    /// <summary>Lays out a text whose parts have their own style.</summary>
    /// <param name="text">The text and its spans.</param>
    /// <param name="options">How the text is wrapped and aligned, or <see langword="null"/> for <see cref="TextLayoutOptions.Default"/>.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> has no span: there is no font to lay out even an empty line.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of a font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of a font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">
    /// The text, its glyphs, its lines or one of its glyphs exceed a limit. The limits are those of the configuration
    /// the primary font of the first span was loaded with.
    /// </exception>
    public static TextLayout Create(RichText text, TextLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.SpanList.IsEmpty)
            throw new ArgumentException("The text has no span.", nameof(text));

        return new TextLayout(TextLayoutEngine.Create(text.Text, text.SpanList.ToArray(), options ?? TextLayoutOptions.Default));
    }

    /// <summary>Creates a path with the outlines of the glyphs and of the decorations: what filling the layout at a point draws.</summary>
    /// <param name="origin">The position of the top-left corner of the layout.</param>
    /// <returns>
    /// A path with one closed contour per contour of the glyphs and one per decoration, to fill with
    /// <see cref="FillRule.NonZero"/>. Filling or stroking it changes the same pixels, to the same values, as filling
    /// or stroking a layout of one paint at the same point.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="origin"/> is not finite or is too large.</exception>
    /// <exception cref="ImageResourceLimitException">Decoding the outlines exceeds the allocation limit.</exception>
    public VectorPath ToPath(PointD origin)
    {
        DrawingGuard.Point(origin, nameof(origin));
        var builder = new VectorPathBuilder();
        var sink = new GlyphPathBuilderSink(builder);
        foreach (var run in _runs)
        {
            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            using var scratch = new GlyphScratch(AllocationScope.Create(face.Data.Configuration, "TextLayout"));
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                sink.Begin(Affine2D.ForGlyph(scale, origin.X + _positions[i * 2], origin.Y + _positions[(i * 2) + 1]));
                var budget = new GlyphBudget(face.Data.Limits);
                face.DecodeOutline(_glyphs[i], sink, scratch, ref budget);
            }
        }

        foreach (var decoration in _decorations)
        {
            builder.AddRectangle(new RectangleD(origin.X + decoration.X, origin.Y + decoration.Y, decoration.Width, decoration.Height));
        }

        return builder.Build();
    }

    /// <summary>Gets a paint of the layout; <see langword="null"/> is the brush of the draw call.</summary>
    internal Brush? GetBrush(int index) => _brushes[index];

    /// <summary>Sends the outlines of the glyphs and of the decorations, placed on the canvas, to the sink of a draw call.</summary>
    /// <param name="target">The sink of the fill or of the stroke.</param>
    /// <param name="glyphSink">The sink that places a glyph.</param>
    /// <param name="scratch">The working storage of the outlines.</param>
    /// <param name="originX">The horizontal position of the top-left corner of the layout.</param>
    /// <param name="originY">The vertical position of the top-left corner of the layout.</param>
    /// <param name="edges">
    /// The edges of a fill, or <see langword="null"/> for a stroke. The glyphs of a fill are given one orientation, so
    /// that glyphs that overlap add up whatever the convention of their font.
    /// </param>
    /// <param name="brush">The paint whose glyphs and decorations are sent, or -1 for all of them.</param>
    internal void Emit(PathSink target, GlyphPathSink glyphSink, GlyphScratch scratch, double originX, double originY, EdgeBuilder? edges, int brush)
    {
        foreach (var run in _runs)
        {
            if (brush >= 0 && run.Brush != brush)
                continue;

            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            var limits = face.Data.Limits;
            for (var i = run.Start; i < run.Start + run.Count; i++)
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
        }

        // The decorations, in the orientation the glyphs are given.
        foreach (var decoration in _decorations)
        {
            if (brush >= 0 && decoration.Brush != brush)
                continue;

            var left = originX + decoration.X;
            var top = originY + decoration.Y;
            target.MoveTo(left, top);
            target.LineTo(left + decoration.Width, top, smooth: false);
            target.LineTo(left + decoration.Width, top + decoration.Height, smooth: false);
            target.LineTo(left, top + decoration.Height, smooth: false);
            target.Close(smooth: false);
        }

        target.End();
    }

    /// <summary>
    /// Sends the outlines of the glyphs and of the decorations, placed along a path, to the sink of a draw call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The horizontal axis of the layout is the path and its first baseline is on it. Each cluster (a character with
    /// its marks, a ligature) is placed as a whole, without being bent: the middle of its advance is on the path, and it
    /// is turned to the direction of the path there. A decoration is cut where the direction of the path changes, and
    /// each part is turned as the clusters are.
    /// </para>
    /// <para>
    /// The rotation is the unit vector of a chord of the path: no trigonometric function is involved, and a text on a
    /// horizontal path has the points of the same text drawn at a point.
    /// </para>
    /// </remarks>
    internal void EmitOnPath(PathSink target, GlyphPathSink glyphSink, GlyphScratch scratch, PathMeasure measure, TextPathOptions options, EdgeBuilder? edges, int brush)
    {
        var length = measure.Length;
        if (!(length > 0))
        {
            target.End();
            return;
        }

        var shift = options.StartOffset + options.Alignment switch
        {
            TextPathAlignment.Center => (length - Width) / 2,
            TextPathAlignment.End => length - Width,
            _ => 0,
        };
        var baseline = _lines[0].Y + _lines[0].Baseline + options.BaselineOffset;
        var isReversed = options.Side == TextPathSide.Right;
        var isClipped = options.Overflow == TextPathOverflow.Clip;
        foreach (var run in _runs)
        {
            if (brush >= 0 && run.Brush != brush)
                continue;

            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            var limits = face.Data.Limits;
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                if (!TryLocate(measure, shift + _anchors[i], isReversed, isClipped, out var x, out var y, out var directionX, out var directionY))
                    continue;

                // The origin of the glyph from the point of its cluster: along the path, then across it. The side the
                // layout grows down to is on the right of the direction of the path.
                var along = _positions[i * 2] - _anchors[i];
                var across = _positions[(i * 2) + 1] - baseline;
                var originX = (x + (along * directionX)) - (across * directionY);
                var originY = (y + (along * directionY)) + (across * directionX);
                var first = edges?.Count ?? 0;
                glyphSink.Begin(target, new Affine2D(scale * directionX, scale * directionY, scale * directionY, -(scale * directionX), originX, originY));
                var budget = new GlyphBudget(limits);
                face.DecodeOutline(_glyphs[i], glyphSink, scratch, ref budget);
                if (edges is not null && glyphSink.SignedArea < 0)
                {
                    edges.NegateDirections(first);
                }
            }
        }

        foreach (var decoration in _decorations)
        {
            if (brush >= 0 && decoration.Brush != brush)
                continue;

            // One quadrilateral per chord of the path the decoration runs along.
            var start = shift + decoration.X;
            var end = start + decoration.Width;
            var top = decoration.Y - baseline;
            var bottom = top + decoration.Height;
            if (measure.IsClosed)
            {
                // More than one turn covers the same pixels again: one turn is the whole decoration.
                var turns = Math.Floor(start / length) * length;
                start -= turns;
                end = Math.Min(end - turns, start + length);
            }
            else if (isClipped)
            {
                start = Math.Max(start, 0);
                end = Math.Min(end, length);
            }

            while (start < end)
            {
                // The path is followed backward on its right side: a distance from its end.
                var position = isReversed ? length - start : start;
                var pieceEnd = end;
                if (measure.IsClosed || (position >= 0 && position <= length))
                {
                    // Around a closed path, forward from the start of a chord or backward from its end.
                    var wrapped = position;
                    if (measure.IsClosed)
                    {
                        wrapped = isReversed
                            ? position - ((Math.Ceiling(position / length) - 1) * length)
                            : position - (Math.Floor(position / length) * length);
                    }

                    var room = isReversed ? wrapped - measure.GetChordStartBefore(wrapped) : measure.GetChordEnd(wrapped) - wrapped;
                    if (room > 0 && !double.IsInfinity(room))
                    {
                        pieceEnd = Math.Min(end, start + room);
                    }
                }
                else if (position < 0 && !isReversed)
                {
                    pieceEnd = Math.Min(end, start - position);
                }
                else if (position > length && isReversed)
                {
                    pieceEnd = Math.Min(end, start + (position - length));
                }

                // The direction is the one of the chord the piece is on: taken at its middle.
                if (TryLocate(measure, (start + pieceEnd) / 2, isReversed, isClipped: false, out var x, out var y, out var directionX, out var directionY))
                {
                    var half = (pieceEnd - start) / 2;
                    AddCorner(target, x, y, directionX, directionY, -half, top, isFirst: true);
                    AddCorner(target, x, y, directionX, directionY, half, top, isFirst: false);
                    AddCorner(target, x, y, directionX, directionY, half, bottom, isFirst: false);
                    AddCorner(target, x, y, directionX, directionY, -half, bottom, isFirst: false);
                    target.Close(smooth: false);
                }

                // A piece shorter than the precision of its position still moves forward.
                start = pieceEnd > start ? pieceEnd : Math.BitIncrement(start);
            }
        }

        target.End();

        static void AddCorner(PathSink target, double x, double y, double directionX, double directionY, double along, double across, bool isFirst)
        {
            var cornerX = (x + (along * directionX)) - (across * directionY);
            var cornerY = (y + (along * directionY)) + (across * directionX);
            if (isFirst)
            {
                target.MoveTo(cornerX, cornerY);
            }
            else
            {
                target.LineTo(cornerX, cornerY, smooth: false);
            }
        }
    }

    /// <summary>Finds the point of a path at a distance, on the side and with the overflow the options ask for.</summary>
    private static bool TryLocate(PathMeasure measure, double distance, bool isReversed, bool isClipped, out double x, out double y, out double directionX, out double directionY)
    {
        var length = measure.Length;
        if (measure.IsClosed)
        {
            // A closed path has no end: the text goes around it.
            distance -= Math.Floor(distance / length) * length;
        }
        else if (isClipped && (distance < 0 || distance > length))
        {
            x = y = directionX = directionY = 0;
            return false;
        }

        if (!isReversed)
            return measure.TryGetPoint(distance, out x, out y, out directionX, out directionY);

        // The other side: the path from its end to its start, with the opposite direction.
        var found = measure.TryGetPoint(length - distance, out x, out y, out directionX, out directionY);
        directionX = -directionX;
        directionY = -directionY;
        return found;
    }

    /// <summary>Decodes each glyph of the layout once, which validates it, and measures what the layout draws.</summary>
    private RectangleD Measure()
    {
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        // The bounds of each distinct glyph of a font, in font units: four values, or none for a glyph without outline.
        var bounds = new Dictionary<(int Font, int Glyph), (double MinX, double MinY, double MaxX, double MaxY)?>();
        var sink = new GlyphBoundsSink();
        foreach (var run in _runs)
        {
            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            using var scratch = new GlyphScratch(AllocationScope.Create(face.Data.Configuration, "TextLayout"));
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                if (!bounds.TryGetValue((run.Font, _glyphs[i]), out var glyphBounds))
                {
                    sink.Reset();
                    var budget = new GlyphBudget(face.Data.Limits);
                    face.DecodeOutline(_glyphs[i], sink, scratch, ref budget);
                    glyphBounds = sink.IsEmpty ? null : (sink.MinX, sink.MinY, sink.MaxX, sink.MaxY);
                    bounds.Add((run.Font, _glyphs[i]), glyphBounds);
                }

                if (glyphBounds is not { } box)
                    continue;

                // The corners, placed as the points of the outline are: the y axis of a font goes up.
                var transform = Affine2D.ForGlyph(scale, _positions[i * 2], _positions[(i * 2) + 1]);
                left = Math.Min(left, transform.TransformX(box.MinX, box.MinY));
                right = Math.Max(right, transform.TransformX(box.MaxX, box.MinY));
                top = Math.Min(top, transform.TransformY(box.MinX, box.MaxY));
                bottom = Math.Max(bottom, transform.TransformY(box.MinX, box.MinY));
            }
        }

        foreach (var decoration in _decorations)
        {
            left = Math.Min(left, decoration.X);
            top = Math.Min(top, decoration.Y);
            right = Math.Max(right, decoration.X + decoration.Width);
            bottom = Math.Max(bottom, decoration.Y + decoration.Height);
        }

        return left <= right ? RectangleD.FromLTRB(left, top, right, bottom) : RectangleD.Empty;
    }
}
