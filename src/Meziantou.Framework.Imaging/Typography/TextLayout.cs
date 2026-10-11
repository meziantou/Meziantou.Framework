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
/// <b>Shaping.</b> The text is shaped with the OpenType Layout tables of the font, one run per script: characters are
/// put in the form the font has glyphs for (composed or decomposed), the <c>GSUB</c> table substitutes glyphs
/// (ligatures, contextual forms) and the <c>GPOS</c> table positions them (kerning, marks), with the features a client
/// applies by default to horizontal text; a font without <c>GPOS</c> kerning is kerned by its <c>kern</c> table. Text
/// is laid out from left to right.
/// </para>
/// <para>
/// A character the font has no glyph for is drawn with the missing glyph of the font and reported by
/// <see cref="HasMissingGlyphs"/>. Control characters are not drawn, format characters are invisible, and a tabulation
/// moves to the next multiple of eight spaces.
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
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of the font, is malformed.</exception>
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

        // The glyphs and their positions, in font units: whole numbers, which add up without error. The text is cut
        // into lines at its line breaks, and each line at its tabulations; what is between is shaped, one run per script.
        var layout = new Builder(face, limits);
        var segmentStart = 0;
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);
            index += consumed;
            var codePoint = rune.Value;
            if (codePoint != '\t' && !IsLineBreak(codePoint))
                continue;

            layout.AddSegment(text.AsSpan(segmentStart, start - segmentStart));
            if (codePoint == '\t')
            {
                layout.AddTabulation();
            }
            else
            {
                // A carriage return and the line feed that follows it are one line break.
                if (codePoint == '\r' && index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                layout.AddLineBreak();
            }

            segmentStart = index;
        }

        layout.AddSegment(text.AsSpan(segmentStart));
        layout.AddLineBreak();

        var metrics = font.GetMetrics(size);
        var scale = size / face.UnitsPerEm;
        var baseline = (metrics.LineGap / 2) + metrics.Ascent;
        var count = layout.Glyphs.Count;
        var positions = new double[count * 2];
        for (var i = 0; i < count; i++)
        {
            // The y axis of a font goes up, the one of the layout goes down.
            positions[i * 2] = layout.X[i] * scale;
            positions[(i * 2) + 1] = (baseline + (layout.Lines[i] * metrics.LineHeight)) - (layout.Y[i] * scale);
        }

        var glyphArray = layout.Glyphs.ToArray();
        var inkBounds = Measure(face, scale, glyphArray, positions);
        return new TextLayout(font, size, glyphArray, positions, layout.Widest * scale, layout.Line, inkBounds, layout.HasMissingGlyphs);
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

    /// <summary>Determines whether a character is a control character, which has no glyph and takes no space.</summary>
    private static bool IsControl(int codePoint) => codePoint is < 0x20 or (>= 0x7F and <= 0x9F);

    /// <summary>The glyphs of a layout while it is created.</summary>
    private sealed class Builder
    {
        private readonly FontFace _face;
        private readonly ImageResourceLimits _limits;
        private readonly GlyphBuffer _buffer;
        private readonly int _tabulation;
        private long _pen;

        public Builder(FontFace face, ImageResourceLimits limits)
        {
            _face = face;
            _limits = limits;
            _buffer = new GlyphBuffer(limits);
            _tabulation = TabSize * GetSpaceAdvance(face);
        }

        public List<int> Glyphs { get; } = [];

        /// <summary>Gets the horizontal position of each glyph from the start of its line, in font units.</summary>
        public List<long> X { get; } = [];

        /// <summary>Gets the vertical position of each glyph above its baseline, in font units.</summary>
        public List<long> Y { get; } = [];

        /// <summary>Gets the line of each glyph.</summary>
        public List<int> Lines { get; } = [];

        /// <summary>Gets the number of lines that are complete.</summary>
        public int Line { get; private set; }

        /// <summary>Gets the advance of the widest line, in font units.</summary>
        public long Widest { get; private set; }

        public bool HasMissingGlyphs { get; private set; }

        public void AddLineBreak()
        {
            Widest = Math.Max(Widest, _pen);
            _pen = 0;
            Line++;
        }

        public void AddTabulation()
        {
            if (_tabulation > 0)
            {
                _pen = ((_pen / _tabulation) + 1) * _tabulation;
            }
        }

        /// <summary>Shapes a part of a line that has no line break and no tabulation, and adds its glyphs at the pen.</summary>
        public void AddSegment(ReadOnlySpan<char> text)
        {
            foreach (var run in Unicode.EnumerateScriptRuns(text))
            {
                _buffer.Clear();
                var characters = text.Slice(run.Index, run.Length);
                var index = 0;
                while (index < characters.Length)
                {
                    Rune.DecodeFromUtf16(characters[index..], out var rune, out var consumed);
                    if (!IsControl(rune.Value))
                    {
                        _buffer.Add(rune.Value, run.Index + index);
                    }

                    index += consumed;
                }

                if (_buffer.Length == 0)
                    continue;

                TextShaper.Shape(_buffer, _face, run.Script, language: 0, isRightToLeft: false, features: default);
                var info = _buffer.Info;
                var positions = _buffer.Positions;
                for (var i = 0; i < info.Length; i++)
                {
                    // The glyph of a character without visible form draws nothing and moves nothing.
                    if (!info[i].IsIgnorable)
                    {
                        if (Glyphs.Count >= _limits.MaxTextGlyphs)
                            throw new ImageResourceLimitException(ImageResourceLimitKind.TextGlyphs, _limits.MaxTextGlyphs, (long)Glyphs.Count + 1);

                        HasMissingGlyphs |= info[i].Glyph == 0;
                        Glyphs.Add(info[i].Glyph);
                        X.Add(_pen + positions[i].XOffset);
                        Y.Add(positions[i].YOffset);
                        Lines.Add(Line);
                    }

                    _pen += positions[i].XAdvance;
                }
            }
        }
    }
}
