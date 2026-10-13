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

    // The display list of each distinct color glyph, and the one of each glyph: -1 for a glyph that is drawn with its
    // outline. No array when the layout has no color glyph.
    private readonly ColorGlyph[] _colorGlyphs;
    private readonly int[]? _colorIndexes;

    // The clusters of each line, from its left, with the characters each one holds: the positions of the text.
    private readonly TextHitBox[] _hitBoxes;
    private readonly int[] _hitBoxStarts;
    private readonly int[] _characterBoundaries;
    private readonly int _textLength;

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
        _hitBoxes = data.HitBoxes;
        _hitBoxStarts = data.HitBoxStarts;
        _characterBoundaries = data.CharacterBoundaries;
        _textLength = data.TextLength;
        foreach (var run in _runs)
        {
            HasEmboldenedGlyphs |= run.EmboldenWidth > 0;
        }

        InkBounds = Measure(out _colorGlyphs, out _colorIndexes);
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

    /// <summary>Gets a value indicating whether a glyph of the layout is drawn with its own colors.</summary>
    internal bool HasColorGlyphs => _colorIndexes is not null;

    internal int RunCount => _runs.Length;

    /// <summary>Gets a run of glyphs of one font, one size and one paint.</summary>
    internal void GetRun(int index, out FontFace face, out double scale, out int start, out int count, out int brush)
    {
        var run = _runs[index];
        face = _fonts[run.Font].Face;
        scale = run.Size / face.UnitsPerEm;
        start = run.Start;
        count = run.Count;
        brush = run.Brush;
    }

    /// <summary>Gets the display list of a glyph that is drawn with its own colors, or <see langword="null"/> for a glyph that is drawn with its outline.</summary>
    internal ColorGlyph? GetColorGlyph(int glyph) => _colorIndexes is null || _colorIndexes[glyph] < 0 ? null : _colorGlyphs[_colorIndexes[glyph]];

    /// <summary>Gets the transformation from the units of a glyph to the pixels of a canvas the layout is drawn on at a point.</summary>
    internal Affine2D GetGlyphTransform(int glyph, double scale, double originX, double originY)
        => Affine2D.ForGlyph(scale, originX + _positions[glyph * 2], originY + _positions[(glyph * 2) + 1]);

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
                sink.Begin(Affine2D.ForGlyph(scale, origin.X + _positions[i * 2], origin.Y + _positions[(i * 2) + 1], run.Shear));
                var budget = new GlyphBudget(face.Data.Limits);
                face.DecodeOutline(_glyphs[i], run.Size, sink, scratch, ref budget);
            }
        }

        foreach (var decoration in _decorations)
        {
            builder.AddRectangle(new RectangleD(origin.X + decoration.X, origin.Y + decoration.Y, decoration.Width, decoration.Height));
        }

        return builder.Build();
    }

    /// <summary>Finds the character at a point of the layout, or the nearest one: what a click selects.</summary>
    /// <remarks>
    /// <para>
    /// The line is the one whose rows contain the point; above the first line it is the first one, and below the last
    /// line the last one. On that line, the character is the one whose advance contains the point (the space from
    /// where the pen is before it to where it is after it, not its ink); beside the text, it is the first or the last
    /// one of the line as it is displayed. <see cref="TextHit.IsInside"/> tells the two apart.
    /// </para>
    /// <para>
    /// A character is a grapheme cluster. The characters a font draws as one glyph (a ligature) share its width
    /// equally. In a text that mixes directions, the characters of a line are not in the order of the text: the hit
    /// gives the index in the text, and <see cref="TextHit.IsTrailing"/> is relative to the direction of the character.
    /// </para>
    /// </remarks>
    /// <param name="point">The point, from the top-left corner of the layout: the point of the canvas, less the origin the layout is drawn at.</param>
    /// <returns>The character and the side of it the point is on.</returns>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="point"/> is not a number.</exception>
    public TextHit HitTest(PointD point)
    {
        if (double.IsNaN(point.X) || double.IsNaN(point.Y))
            throw new ArgumentException("The coordinates must be numbers.", nameof(point));

        // The line whose rows contain the point; the nearest one above the first line and below the last one
        var lineIndex = 0;
        while (lineIndex < _lines.Length - 1 && point.Y >= _lines[lineIndex].Y + _lines[lineIndex].Height)
        {
            lineIndex++;
        }

        var line = _lines[lineIndex];
        var isInLine = point.Y >= line.Y && point.Y < line.Y + line.Height;
        var boxes = _hitBoxes.AsSpan(_hitBoxStarts[lineIndex], _hitBoxStarts[lineIndex + 1] - _hitBoxStarts[lineIndex]);
        if (boxes.IsEmpty)
            return new TextHit(lineIndex, line.Start, 0, isTrailing: false, isInside: false);

        // The cluster whose advance contains the point; the nearest one beside the text
        var index = 0;
        while (index < boxes.Length - 1 && point.X >= boxes[index].Right)
        {
            index++;
        }

        ref readonly var box = ref boxes[index];
        var width = box.Right - box.Left;
        var isInside = isInLine && point.X >= box.Left && point.X < box.Right;

        // How far the point is in the cluster, in the direction of its text: from 0 at its leading edge to 1
        var position = width > 0 ? Math.Clamp((point.X - box.Left) / width, 0, 1) : point.X < box.Left ? 0 : 1;
        if (box.IsRightToLeft)
        {
            position = 1 - position;
        }

        // The character of the cluster the point is on, and the half of it
        var scaled = position * box.CharacterCount;
        var character = Math.Min((int)scaled, box.CharacterCount - 1);
        var start = GetCharacterStart(box, character);
        var end = GetCharacterStart(box, character + 1);
        return new TextHit(lineIndex, box.Start + start, end - start, scaled - character >= 0.5, isInside);
    }

    /// <summary>Gets where a caret is drawn before a character of the text: a rectangle without width, as tall as its line.</summary>
    /// <remarks>
    /// <para>
    /// The caret is on the leading edge of the character: its left in a left-to-right text, its right in a
    /// right-to-left one. At the end of a line (a line break, the end of the text), it is after the last character of
    /// the line; on a line without character, it is where the line starts. The position between two lines of a wrapped
    /// paragraph is the start of the second one.
    /// </para>
    /// <para>An index inside a character (between a letter and its mark, inside a surrogate pair) gives the caret of that character.</para>
    /// </remarks>
    /// <param name="textIndex">The index in the text of the character the caret is before, in UTF-16 code units: from 0 to the length of the text, which is after its last character.</param>
    /// <returns>The rectangle, from the top-left corner of the layout: its left is the position of the caret, and its width is 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textIndex"/> is negative or greater than the length of the text.</exception>
    public RectangleD GetCaretRectangle(int textIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(textIndex, _textLength);

        // The line that holds the character; the last one for the end of the text
        var lineIndex = 0;
        while (lineIndex < _lines.Length - 1 && textIndex >= _lines[lineIndex].Start + _lines[lineIndex].Length)
        {
            lineIndex++;
        }

        var line = _lines[lineIndex];
        var boxes = _hitBoxes.AsSpan(_hitBoxStarts[lineIndex], _hitBoxStarts[lineIndex + 1] - _hitBoxStarts[lineIndex]);

        // The cluster that holds the character, or the last one before it in the text: the caret is then after it
        var found = -1;
        for (var i = 0; i < boxes.Length; i++)
        {
            if (boxes[i].Start <= textIndex && (found < 0 || boxes[i].Start > boxes[found].Start))
            {
                found = i;
            }
        }

        double x;
        if (found < 0)
        {
            // Before every character of the line: where the line starts
            x = boxes.IsEmpty ? (line.IsRightToLeft ? line.X + line.Width : line.X) : GetEdge(boxes[GetFirst(boxes)], 0);
        }
        else
        {
            ref readonly var box = ref boxes[found];
            x = GetEdge(box, textIndex >= box.Start + box.Length ? 1 : (double)GetCharacterIndex(box, textIndex - box.Start) / box.CharacterCount);
        }

        return new RectangleD(x, line.Y, 0, line.Height);

        static int GetFirst(ReadOnlySpan<TextHitBox> boxes)
        {
            var first = 0;
            for (var i = 1; i < boxes.Length; i++)
            {
                if (boxes[i].Start < boxes[first].Start)
                {
                    first = i;
                }
            }

            return first;
        }
    }

    /// <summary>Gets the rectangles that cover a range of the text: what is highlighted when it is selected.</summary>
    /// <remarks>
    /// Each rectangle is as tall as its line and covers the advances of characters that follow each other on it. A
    /// range that spans several lines has at least one rectangle per line; in a text that mixes directions, a range
    /// can be several rectangles on one line. Line breaks and the characters that have no glyph take no space, and a
    /// part of a ligature takes its share of the width of the ligature.
    /// </remarks>
    /// <param name="start">The index in the text of the first character of the range, in UTF-16 code units.</param>
    /// <param name="length">The number of UTF-16 code units of the range.</param>
    /// <returns>The rectangles, from the top-left corner of the layout, line by line and from left to right on each line; none for an empty range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is negative, or the range goes beyond the end of the text.</exception>
    public IReadOnlyList<RectangleD> GetSelectionRectangles(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _textLength - start);
        var end = start + length;
        var rectangles = new List<RectangleD>();
        for (var lineIndex = 0; lineIndex < _lines.Length && length > 0; lineIndex++)
        {
            var line = _lines[lineIndex];
            if (line.Start >= end || line.Start + line.Length <= start)
                continue;

            var first = rectangles.Count;
            foreach (ref readonly var box in _hitBoxes.AsSpan(_hitBoxStarts[lineIndex], _hitBoxStarts[lineIndex + 1] - _hitBoxStarts[lineIndex]))
            {
                var from = Math.Max(start, box.Start);
                var to = Math.Min(end, box.Start + box.Length);
                if (from >= to)
                    continue;

                // The characters of the cluster that are in the range, from its leading edge
                var leading = GetEdge(box, (double)GetCharacterIndex(box, from - box.Start) / box.CharacterCount);
                var trailing = GetEdge(box, to == box.Start + box.Length ? 1 : (double)GetCharacterIndex(box, to - box.Start) / box.CharacterCount);
                var left = Math.Min(leading, trailing);
                var right = Math.Max(leading, trailing);
                if (rectangles.Count > first && Math.Abs(rectangles[^1].Right - left) <= 1e-9)
                {
                    // It continues the rectangle before it
                    rectangles[^1] = RectangleD.FromLTRB(rectangles[^1].Left, line.Y, right, line.Y + line.Height);
                }
                else
                {
                    rectangles.Add(RectangleD.FromLTRB(left, line.Y, right, line.Y + line.Height));
                }
            }
        }

        return rectangles;
    }

    /// <summary>Gets the position of a fraction of a cluster, from its leading edge (0) to its trailing edge (1).</summary>
    private static double GetEdge(in TextHitBox box, double fraction)
        => box.IsRightToLeft ? box.Right - ((box.Right - box.Left) * fraction) : box.Left + ((box.Right - box.Left) * fraction);

    /// <summary>Gets where a character of a cluster starts, from the start of the cluster; the length of the cluster for the character after the last one.</summary>
    private int GetCharacterStart(in TextHitBox box, int character)
        => character <= 0 ? 0 : character >= box.CharacterCount ? box.Length : _characterBoundaries[box.FirstBoundary + character - 1];

    /// <summary>Gets the character of a cluster that holds an offset from the start of the cluster.</summary>
    private int GetCharacterIndex(in TextHitBox box, int offset)
    {
        var character = 0;
        while (character < box.CharacterCount - 1 && _characterBoundaries[box.FirstBoundary + character] <= offset)
        {
            character++;
        }

        return character;
    }

    /// <summary>Gets a value indicating whether glyphs of the layout are thickened by a synthetic bold when they are filled.</summary>
    internal bool HasEmboldenedGlyphs { get; }

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
    /// <param name="skipColorGlyphs">Whether the glyphs the font draws with its own colors are left out: they are drawn on their own.</param>
    /// <param name="emboldener">
    /// The stroker of the fill, which shares its edges, or <see langword="null"/> for a stroke: it adds to the glyphs
    /// of a synthetic bold the stroke of their outline.
    /// </param>
    /// <returns><see langword="true"/> when the stroke of a synthetic bold covers the whole frame: the edges are then meaningless.</returns>
    internal bool Emit(PathSink target, GlyphPathSink glyphSink, GlyphScratch scratch, double originX, double originY, EdgeBuilder? edges, int brush, bool skipColorGlyphs = false, Stroker? emboldener = null)
    {
        var coversFrame = false;
        foreach (var run in _runs)
        {
            if (brush >= 0 && run.Brush != brush)
                continue;

            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            var limits = face.Data.Limits;
            var isEmboldened = emboldener is not null && run.EmboldenWidth > 0;
            if (isEmboldened)
            {
                emboldener!.Configure(run.EmboldenWidth, LineJoin.Miter, GlyphRun.BoldMiterLimit);
            }

            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                // A color glyph is not a shape of the paint of the text: it is drawn on its own, and is never thickened.
                var isColorGlyph = _colorIndexes is not null && _colorIndexes[i] >= 0;
                if (isColorGlyph && skipColorGlyphs)
                    continue;

                var first = edges?.Count ?? 0;
                var transform = Affine2D.ForGlyph(scale, originX + _positions[i * 2], originY + _positions[(i * 2) + 1], run.Shear);
                glyphSink.Begin(target, transform);
                var budget = new GlyphBudget(limits);
                face.DecodeOutline(_glyphs[i], run.Size, glyphSink, scratch, ref budget);

                // The contours of the glyph are complete: each one was closed. TrueType and CFF outlines turn in opposite
                // directions; the ones that turn the other way are reversed.
                if (edges is not null && glyphSink.SignedArea < 0)
                {
                    edges.NegateDirections(first);
                }

                if (isEmboldened && !isColorGlyph)
                {
                    // The fill of the glyph is complete before the edges of its stroke follow it.
                    target.End();
                    coversFrame |= Embolden(emboldener!, face, i, run.Size, transform, glyphSink, scratch);
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
        return coversFrame;
    }

    /// <summary>
    /// Thickens a glyph that was just filled: its outline is sent again, to a stroker that adds the pieces of its
    /// stroke to the edges of the fill. The pieces have the orientation the glyphs of a fill are given, so the stroke
    /// and the glyph add up, and what is filled is the glyph grown by half the width of the stroke.
    /// </summary>
    /// <returns><see langword="true"/> when a piece of the stroke covers the whole frame.</returns>
    private bool Embolden(Stroker emboldener, FontFace face, int glyph, double size, in Affine2D transform, GlyphPathSink glyphSink, GlyphScratch scratch)
    {
        glyphSink.Begin(emboldener, transform);
        var budget = new GlyphBudget(face.Data.Limits);
        face.DecodeOutline(_glyphs[glyph], size, glyphSink, scratch, ref budget);
        emboldener.End();
        return emboldener.CoversFrame;
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
    internal bool EmitOnPath(PathSink target, GlyphPathSink glyphSink, GlyphScratch scratch, PathMeasure measure, TextPathOptions options, EdgeBuilder? edges, int brush, bool skipColorGlyphs = false, Stroker? emboldener = null)
    {
        var coversFrame = false;
        var length = measure.Length;
        if (!(length > 0))
        {
            target.End();
            return false;
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
            var isEmboldened = emboldener is not null && run.EmboldenWidth > 0;
            if (isEmboldened)
            {
                emboldener!.Configure(run.EmboldenWidth, LineJoin.Miter, GlyphRun.BoldMiterLimit);
            }

            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                var isColorGlyph = _colorIndexes is not null && _colorIndexes[i] >= 0;
                if (isColorGlyph && skipColorGlyphs)
                    continue;

                if (!TryGetGlyphTransformOnPath(i, scale, measure, options, out var transform))
                    continue;

                // A slanted glyph is slanted before it is placed and turned.
                if (run.Shear != 0)
                {
                    transform = transform.Then([1, 0, run.Shear, 1, 0, 0]);
                }

                var first = edges?.Count ?? 0;
                glyphSink.Begin(target, transform);
                var budget = new GlyphBudget(limits);
                face.DecodeOutline(_glyphs[i], run.Size, glyphSink, scratch, ref budget);
                if (edges is not null && glyphSink.SignedArea < 0)
                {
                    edges.NegateDirections(first);
                }

                if (isEmboldened && !isColorGlyph)
                {
                    // The fill of the glyph is complete before the edges of its stroke follow it.
                    target.End();
                    coversFrame |= Embolden(emboldener!, face, i, run.Size, transform, glyphSink, scratch);
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
        return coversFrame;

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

    /// <summary>
    /// Gets the transformation from the units of a glyph to the pixels of a canvas the layout is drawn on along a path:
    /// the glyph is placed, with its cluster, where the middle of the advance of the cluster is on the path, and turned
    /// to the direction of the path there.
    /// </summary>
    /// <returns><see langword="false"/> when the glyph is not drawn: it is beyond an end of a path that clips.</returns>
    internal bool TryGetGlyphTransformOnPath(int glyph, double scale, PathMeasure measure, TextPathOptions options, out Affine2D transform)
    {
        var length = measure.Length;
        transform = default;
        if (!(length > 0))
            return false;

        var shift = options.StartOffset + options.Alignment switch
        {
            TextPathAlignment.Center => (length - Width) / 2,
            TextPathAlignment.End => length - Width,
            _ => 0,
        };
        var baseline = _lines[0].Y + _lines[0].Baseline + options.BaselineOffset;
        if (!TryLocate(measure, shift + _anchors[glyph], options.Side == TextPathSide.Right, options.Overflow == TextPathOverflow.Clip, out var x, out var y, out var directionX, out var directionY))
            return false;

        // The origin of the glyph from the point of its cluster: along the path, then across it. The side the
        // layout grows down to is on the right of the direction of the path.
        var along = _positions[glyph * 2] - _anchors[glyph];
        var across = _positions[(glyph * 2) + 1] - baseline;
        var originX = (x + (along * directionX)) - (across * directionY);
        var originY = (y + (along * directionY)) + (across * directionX);
        transform = new Affine2D(scale * directionX, scale * directionY, scale * directionY, -(scale * directionX), originX, originY);
        return true;
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
    private RectangleD Measure(out ColorGlyph[] colorGlyphs, out int[]? colorIndexes)
    {
        // The display list of each distinct color glyph of a font and a palette.
        var colors = new List<ColorGlyph>();
        var colorKeys = new Dictionary<(int Font, int Glyph, int Palette, double Size), int>();
        colorIndexes = null;
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        // The bounds of each distinct glyph of a font, in font units: four values, or none for a glyph without outline.
        var bounds = new Dictionary<(int Font, int Glyph, double Size), (double MinX, double MinY, double MaxX, double MaxY)?>();
        var sink = new GlyphBoundsSink();
        foreach (var run in _runs)
        {
            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            using var scratch = new GlyphScratch(AllocationScope.Create(face.Data.Configuration, "TextLayout"));
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                if (!bounds.TryGetValue((run.Font, _glyphs[i], run.Size), out var glyphBounds))
                {
                    sink.Reset();
                    var budget = new GlyphBudget(face.Data.Limits);
                    face.DecodeOutline(_glyphs[i], run.Size, sink, scratch, ref budget);
                    glyphBounds = sink.IsEmpty ? null : (sink.MinX, sink.MinY, sink.MaxX, sink.MaxY);
                    bounds.Add((run.Font, _glyphs[i], run.Size), glyphBounds);
                }

                // A glyph the font draws with its own colors: its display list is built once, which validates what it
                // refers to, and its box replaces the box of its outline.
                // A glyph the font draws with its own colors: its display list is built once, which validates what it
                // refers to, and its box replaces the box of its outline. A glyph that is an image depends on the size
                // it is drawn at.
                if (face.HasColorGlyphs)
                {
                    var isPainted = face.IsColorGlyph(_glyphs[i]);
                    var key = (run.Font, _glyphs[i], isPainted ? run.Palette : 0, isPainted ? 0 : run.Size);
                    if (!colorKeys.TryGetValue(key, out var colorIndex))
                    {
                        var budget = new GlyphBudget(face.Data.Limits);
                        ColorGlyph? colorGlyph = null;
                        if (isPainted)
                        {
                            colorGlyph = face.BuildColorGlyph(_glyphs[i], run.Palette, scratch, ref budget);
                        }
                        else if (face.TryGetBitmapGlyph(_glyphs[i], run.Size, out var bitmap))
                        {
                            colorGlyph = ColorImage.CreateGlyph(face, bitmap, run.Size);
                        }

                        colorIndex = -1;
                        if (colorGlyph is not null)
                        {
                            colorIndex = colors.Count;
                            colors.Add(colorGlyph);
                        }

                        colorKeys.Add(key, colorIndex);
                    }

                    if (colorIndex >= 0)
                    {
                        if (colorIndexes is null)
                        {
                            colorIndexes = new int[_glyphs.Length];
                            colorIndexes.AsSpan().Fill(-1);
                        }

                        // A color glyph whose paints draw nothing draws nothing: its outline is not a fallback.
                        colorIndexes[i] = colorIndex;
                        var color = colors[colorIndex];
                        glyphBounds = color.IsEmpty ? null : (color.XMin, color.YMin, color.XMax, color.YMax);
                    }
                }

                if (glyphBounds is not { } box)
                    continue;

                // The corners, placed as the points of the outline are: the y axis of a font goes up. A glyph the font
                // draws with its own colors is neither slanted nor thickened. The box of a slanted glyph is the box of
                // its slanted box; the stroke of a thickened glyph stays within its width of the outline, at the tip of
                // the sharpest corner it keeps.
                var isOutline = colorIndexes is null || colorIndexes[i] < 0;
                var transform = Affine2D.ForGlyph(scale, _positions[i * 2], _positions[(i * 2) + 1], isOutline ? run.Shear : 0);
                var margin = isOutline ? run.EmboldenWidth * (GlyphRun.BoldMiterLimit / 2) : 0;
                left = Math.Min(left, Math.Min(transform.TransformX(box.MinX, box.MinY), transform.TransformX(box.MinX, box.MaxY)) - margin);
                right = Math.Max(right, Math.Max(transform.TransformX(box.MaxX, box.MinY), transform.TransformX(box.MaxX, box.MaxY)) + margin);
                top = Math.Min(top, transform.TransformY(box.MinX, box.MaxY) - margin);
                bottom = Math.Max(bottom, transform.TransformY(box.MinX, box.MinY) + margin);
            }
        }

        foreach (var decoration in _decorations)
        {
            left = Math.Min(left, decoration.X);
            top = Math.Min(top, decoration.Y);
            right = Math.Max(right, decoration.X + decoration.Width);
            bottom = Math.Max(bottom, decoration.Y + decoration.Height);
        }

        colorGlyphs = [.. colors];
        return left <= right ? RectangleD.FromLTRB(left, top, right, bottom) : RectangleD.Empty;
    }
}
