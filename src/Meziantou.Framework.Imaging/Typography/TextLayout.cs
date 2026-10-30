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
/// right-to-left text can be mixed, and each line is put in display order. A vertical tab, a form feed and a line
/// separator (U+2028) end a line but not the direction of the paragraph: with <see cref="TextDirection.Auto"/>, the
/// text after them takes the direction of the text before them, and the next direction is found after the other
/// line breaks.
/// </para>
/// <para>
/// <b>Geometry.</b> Positions are in pixels from the top-left corner of the layout, with the y axis down. A line is as
/// tall as its fonts ask (the largest ascent and descent, each with half of the line gap), or as
/// <see cref="TextLayoutOptions.LineHeight"/> says. Glyphs are placed at fractions of a pixel and their outlines are
/// scaled exactly, unless <see cref="TextLayoutOptions.Hinting"/> fits them to the pixel grid.
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
    private readonly DecorationRectangle[] _backgrounds;
    private readonly Pen[] _outlines;
    private readonly TextLine[] _lines;

    // The display list of each distinct color glyph, and the one of each glyph: -1 for a glyph that is drawn with its
    // outline. No array when the layout has no color glyph.
    private readonly ColorGlyph[] _colorGlyphs;
    private readonly int[]? _colorIndexes;

    // The outline of each distinct glyph of a font at a size, as the font gave it when the layout was created, and the
    // outline of each glyph: drawing the layout replays them instead of decoding the glyphs again.
    private readonly RecordedOutlines _recordedOutlines;
    private readonly int[] _outlineIndexes;

    // The clusters of each line, from its left, with the characters each one holds: the positions of the text.
    private readonly TextHitBox[] _hitBoxes;
    private readonly int[] _hitBoxStarts;
    private readonly int[] _characterBoundaries;
    private readonly int _textLength;

    // A vertical layout as it was laid out, each line as a horizontal one: what the positions of the text are found
    // on, and what follows a path. No value for a horizontal layout, which is that already.
    private readonly VerticalLayoutData? _vertical;

    private TextLayout(TextLayoutData data)
    {
        _fonts = data.Fonts;
        _brushes = data.Brushes;
        _runs = data.Runs;
        _glyphs = data.Glyphs;
        _positions = data.Positions;
        _anchors = data.Anchors;
        _decorations = data.Decorations;
        _backgrounds = data.Backgrounds;
        _outlines = data.Outlines;
        Placeholders = new ReadOnlyCollection<RectangleD>(data.Placeholders);
        Scale = data.Scale;
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
        _vertical = data.Vertical;
        foreach (var run in _runs)
        {
            HasEmboldenedGlyphs |= run.EmboldenWidth > 0;
        }

        InkBounds = Measure(data.HintedOutlines, out _colorGlyphs, out _colorIndexes, out _recordedOutlines, out _outlineIndexes);
    }

    /// <summary>Gets the width of the text, in pixels: the width of its widest line, with the indent of a first line.</summary>
    /// <remarks>
    /// The white space that ends a wrapped line hangs and does not count; the spaces that end a paragraph do. Lines are
    /// aligned in <see cref="TextLayoutOptions.MaxWidth"/> when it is set, which can be wider than the text. In a
    /// vertical layout (<see cref="TextLayoutOptions.WritingMode"/>), it is the sum of the widths of the lines.
    /// </remarks>
    public double Width { get; }

    /// <summary>Gets the height of the layout, in pixels: the sum of the heights of its lines, or the height of the tallest line of a vertical layout.</summary>
    public double Height { get; }

    /// <summary>
    /// Gets the distance from the top of the layout to the baseline of its first line, in pixels. In a vertical
    /// layout, it is the distance from the left of the layout to the central baseline of its first line, which is
    /// its rightmost or its leftmost one.
    /// </summary>
    public double Baseline => _vertical is null ? _lines[0].Baseline : _lines[0].X + _lines[0].Baseline;

    /// <summary>Gets the number of lines.</summary>
    public int LineCount => _lines.Length;

    /// <summary>
    /// Gets the lines, from the top: the part of the text each one holds and where it is. The lines of a vertical
    /// layout are its columns, from the first one: the rightmost one or the leftmost one.
    /// </summary>
    public IReadOnlyList<TextLine> Lines { get; }

    /// <summary>
    /// Gets a rectangle that contains everything the layout draws when it is filled, relative to its top-left corner;
    /// empty when the layout draws nothing.
    /// </summary>
    /// <remarks>
    /// The rectangle contains the control points of the outlines, the decorations and the backgrounds of the styles
    /// (not the strokes of their outlines, which reach half the width of a pen further: see <see cref="GetInkBounds(Pen)"/>). It is the exact bounding box of
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

    /// <summary>
    /// Gets what the sizes of the styles were multiplied by to lay out the text: 1, unless the layout was created by
    /// <see cref="CreateToFit(string, TextStyle, double, double, TextLayoutOptions?, double)"/>, which then gives the
    /// factor that makes the text fit its box.
    /// </summary>
    public double Scale { get; }

    /// <summary>
    /// Gets the rectangle of each placeholder of the text
    /// (<see cref="RichTextBuilder.AppendPlaceholder(double, double, double, TextStyle)"/>), in the order of the text,
    /// from the top-left corner of the layout: where the caller draws what the placeholder makes room for, once the
    /// origin the layout is drawn at is added.
    /// </summary>
    /// <remarks>
    /// The rectangle has the width and the height of the placeholder (multiplied by <see cref="Scale"/>), on its
    /// line: its top is the baseline of the line less the baseline of the placeholder. A placeholder that is not in
    /// the layout, because the text was cut before it, has an empty rectangle. The rectangles are those of a layout
    /// drawn at a point, not along a path.
    /// </remarks>
    public IReadOnlyList<RectangleD> Placeholders { get; }

    internal int BrushCount => _brushes.Length;

    /// <summary>Gets a value indicating whether a style of the layout has a background, painted before the text.</summary>
    internal bool HasBackgrounds => _backgrounds.Length != 0;

    /// <summary>Gets the number of pens that stroke glyphs of the layout once it is filled.</summary>
    internal int OutlineCount => _outlines.Length;

    /// <summary>Gets a pen that strokes glyphs of the layout once it is filled.</summary>
    internal Pen GetOutline(int index) => _outlines[index];

    /// <summary>Gets a value indicating whether a glyph of the layout is drawn with its own colors.</summary>
    internal bool HasColorGlyphs => _colorIndexes is not null;

    internal int RunCount => _runs.Length;

    /// <summary>Gets a run of glyphs of one font, one size, one paint and one orientation.</summary>
    internal void GetRun(int index, out FontFace face, out double scale, out int start, out int count, out int brush, out bool isSideways, out double widthScale)
    {
        var run = _runs[index];
        face = _fonts[run.Font].Face;
        scale = run.Size / face.UnitsPerEm;
        start = run.Start;
        count = run.Count;
        brush = run.Brush;
        isSideways = run.IsSideways;
        widthScale = run.WidthScale;
    }

    /// <summary>Gets the display list of a glyph that is drawn with its own colors, or <see langword="null"/> for a glyph that is drawn with its outline.</summary>
    internal ColorGlyph? GetColorGlyph(int glyph) => _colorIndexes is null || _colorIndexes[glyph] < 0 ? null : _colorGlyphs[_colorIndexes[glyph]];

    /// <summary>Gets the transformation from the units of a glyph to the pixels of a canvas the layout is drawn on at a point, for a glyph that is not slanted.</summary>
    internal Affine2D GetGlyphTransform(int glyph, double scale, bool isSideways, double widthScale, double originX, double originY)
        => isSideways
            ? Affine2D.ForSidewaysGlyph(scale, originX + _positions[glyph * 2], originY + _positions[(glyph * 2) + 1], shear: 0)
            : Affine2D.ForGlyph(scale, originX + _positions[glyph * 2], originY + _positions[(glyph * 2) + 1], shear: 0, widthScale);

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
        try
        {
            return new TextLayout(TextLayoutEngine.Create(text, [new TextSpan(0, text.Length, style)], options ?? TextLayoutOptions.Default));
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
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

        try
        {
            return new TextLayout(TextLayoutEngine.Create(text.Text, text.SpanList.ToArray(), options ?? TextLayoutOptions.Default));
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
    }

    /// <summary>Lays out a text with one style at the largest size, up to the size of the style, at which it fits a box.</summary>
    /// <param name="text">The text.</param>
    /// <param name="style">The fonts, the size and the other settings of the text. Its size is the largest one the text is given.</param>
    /// <param name="width">The width of the box, in pixels: lines are wrapped and aligned at it, unless the text is vertical. Positive, or <see cref="double.PositiveInfinity"/> for a text that is only fitted to a height.</param>
    /// <param name="height">The height of the box, in pixels: the columns of a vertical text are wrapped and aligned at it. Positive, or <see cref="double.PositiveInfinity"/> for a text that is only fitted to a width.</param>
    /// <param name="options">How the text is wrapped and aligned, or <see langword="null"/> for <see cref="TextLayoutOptions.Default"/>. Its <see cref="TextLayoutOptions.MaxWidth"/> and <see cref="TextLayoutOptions.MaxHeight"/> are replaced by the box.</param>
    /// <param name="minimumScale">The smallest fraction of its size the text is given, more than 0 and up to 1: a text that does not fit at that size is laid out at it, and cut as the options say.</param>
    /// <returns>The layout; <see cref="Scale"/> is the fraction of its size the text has.</returns>
    /// <remarks>See <see cref="CreateToFit(RichText, double, double, TextLayoutOptions?, double)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="style"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive, or <paramref name="minimumScale"/> is not more than 0 and up to 1.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of a font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of a font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The text, its glyphs, its lines or one of its glyphs exceed a limit.</exception>
    public static TextLayout CreateToFit(string text, TextStyle style, double width, double height, TextLayoutOptions? options = null, double minimumScale = 0.1)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        return CreateToFit(text, [new TextSpan(0, text.Length, style)], width, height, options, minimumScale);
    }

    /// <summary>Lays out a text whose parts have their own style at the largest sizes, up to those of its styles, at which it fits a box: a title that fills a card, a label that never overflows its button.</summary>
    /// <remarks>
    /// <para>
    /// Every length of the styles is multiplied by one factor, <see cref="Scale"/>: the sizes of the fonts, the letter
    /// and word spacings, and with them the indent of the first lines, a fixed line height and the placeholders. The
    /// widths of the pens of <see cref="TextStyle.Outline"/> are not. The factor is 1 when the text fits its box at
    /// the sizes of its styles: a text is made smaller, never larger.
    /// </para>
    /// <para>
    /// A text fits when none of it is cut (<see cref="IsTruncated"/>), no line is wider than
    /// <paramref name="width"/> (a word that cannot be broken can be) and its lines are not taller than
    /// <paramref name="height"/>. Lines are wrapped at <paramref name="width"/> as <paramref name="options"/> say:
    /// with <see cref="TextWrapping.NoWrap"/> or <see cref="TextLayoutOptions.MaxLines"/>, the text is fitted on the
    /// lines it is allowed. In a vertical <see cref="TextLayoutOptions.WritingMode"/>, the box is turned: the lines are
    /// columns, wrapped at <paramref name="height"/>, and they fit when they are not wider than <paramref name="width"/>
    /// together. The factor is found by trying the one the box is to the text as it is laid out at its
    /// sizes (the exact factor of a text whose lines stay the same), then by halving twelve times the range between
    /// a factor that fits and one that does not, each time with a whole layout: it is the largest one of those that
    /// were tried and fit, within 1/4096 of the range from <paramref name="minimumScale"/> to 1. A text that does not
    /// fit at <paramref name="minimumScale"/> is laid out at that factor in the box, and cut as the options say.
    /// </para>
    /// </remarks>
    /// <param name="text">The text and its spans.</param>
    /// <param name="width">The width of the box, in pixels: lines are wrapped and aligned at it, unless the text is vertical. Positive, or <see cref="double.PositiveInfinity"/>.</param>
    /// <param name="height">The height of the box, in pixels: the columns of a vertical text are wrapped and aligned at it. Positive, or <see cref="double.PositiveInfinity"/>.</param>
    /// <param name="options">How the text is wrapped and aligned, or <see langword="null"/> for <see cref="TextLayoutOptions.Default"/>. Its <see cref="TextLayoutOptions.MaxWidth"/> and <see cref="TextLayoutOptions.MaxHeight"/> are replaced by the box.</param>
    /// <param name="minimumScale">The smallest fraction of their sizes the styles are given, more than 0 and up to 1.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> has no span.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive, or <paramref name="minimumScale"/> is not more than 0 and up to 1.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text, or a layout table of a font, is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of a font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The text, its glyphs, its lines or one of its glyphs exceed a limit.</exception>
    public static TextLayout CreateToFit(RichText text, double width, double height, TextLayoutOptions? options = null, double minimumScale = 0.1)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.SpanList.IsEmpty)
            throw new ArgumentException("The text has no span.", nameof(text));

        return CreateToFit(text.Text, text.SpanList.ToArray(), width, height, options, minimumScale);
    }

    private static TextLayout CreateToFit(string text, TextSpan[] spans, double width, double height, TextLayoutOptions? options, double minimumScale)
    {
        // Written so that a value that is not a number is rejected too
        if (!(width > 0))
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width of the box must be positive.");

        if (!(height > 0))
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height of the box must be positive.");

        if (!(minimumScale > 0 && minimumScale <= 1))
            throw new ArgumentOutOfRangeException(nameof(minimumScale), minimumScale, "The smallest scale must be more than 0 and up to 1.");

        try
        {
            return CreateToFitCore(text, spans, width, height, options ?? TextLayoutOptions.Default, minimumScale);
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }
    }

    private static TextLayout CreateToFitCore(string text, TextSpan[] spans, double width, double height, TextLayoutOptions options, double minimumScale)
    {
        // The whole text, at a scale: it fits when none of it is cut and its lines stay in the box
        if (TryFit(scale: 1, out var data))
            return new TextLayout(data);

        // What the box is to the text as it is: the scale of a text whose lines do not change, which is tried first
        // so that such a text gets it exactly
        var proportional = Math.Min(width / data.Width, height / data.Height);
        if (!TryFit(minimumScale, out var best))
            return new TextLayout(TextLayoutEngine.Create(text, spans, options, minimumScale, width, height));

        // The largest scale that fits is between one that fits and one that does not
        var low = minimumScale;
        var high = 1.0;
        if (proportional > low && proportional < high && TryFit(proportional, out data))
        {
            low = proportional;
            best = data;
        }

        for (var i = 0; i < 12; i++)
        {
            var middle = (low + high) / 2;
            if (TryFit(middle, out data))
            {
                low = middle;
                best = data;
            }
            else
            {
                high = middle;
            }
        }

        return new TextLayout(best);

        bool TryFit(double scale, out TextLayoutData result)
        {
            // The lines are wrapped at the box and may take any room across it, which is then measured: the
            // columns of a vertical text are wrapped at its height and follow each other along its width
            result = options.WritingMode == TextWritingMode.Horizontal
                ? TextLayoutEngine.Create(text, spans, options, scale, width, double.PositiveInfinity)
                : TextLayoutEngine.Create(text, spans, options, scale, double.PositiveInfinity, height);
            return !result.IsTruncated && result.Width <= width && result.Height <= height;
        }
    }

    /// <summary>Creates a path with the outlines of the glyphs and of the decorations: what filling the layout at a point draws.</summary>
    /// <param name="origin">The position of the top-left corner of the layout.</param>
    /// <returns>
    /// A path with one closed contour per contour of the glyphs and one per decoration, to fill with
    /// <see cref="FillRule.NonZero"/>. Filling or stroking it changes the same pixels, to the same values, as filling
    /// or stroking a layout of one paint at the same point.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="origin"/> is not finite or is too large.</exception>
    public VectorPath ToPath(PointD origin)
    {
        DrawingGuard.Point(origin, nameof(origin));
        var builder = new VectorPathBuilder();
        var sink = new GlyphPathBuilderSink(builder);
        foreach (var run in _runs)
        {
            var scale = run.Size / _fonts[run.Font].Face.UnitsPerEm;
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                sink.Begin(run.GetTransform(scale, origin.X + _positions[i * 2], origin.Y + _positions[(i * 2) + 1]));
                _recordedOutlines.Replay(_outlineIndexes[i], sink);
            }
        }

        foreach (var decoration in _decorations)
        {
            builder.AddRectangle(new RectangleD(origin.X + decoration.X, origin.Y + decoration.Y, decoration.Width, decoration.Height));
        }

        return builder.Build();
    }

    /// <summary>Gets a rectangle that contains everything a pen paints when it strokes the layout, relative to its top-left corner.</summary>
    /// <remarks>
    /// The rectangle is <see cref="InkBounds"/> spread by how far the stroke can reach from the outlines, as
    /// <see cref="VectorPath.GetBounds(Pen)"/> spreads the bounds of a path: what
    /// <see cref="DrawingCanvas.StrokeText(Pen, TextLayout, PointD)"/> paints, or a filled layout outlined with that pen.
    /// A pen with markers strokes <see cref="ToPath(PointD)"/>, which allocates, and gives its exact bounds. Empty when
    /// the layout draws nothing.
    /// </remarks>
    /// <param name="pen">The stroke. Its brush is not used.</param>
    /// <returns>The rectangle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pen"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The stroke goes beyond the largest coordinate (16777216).</exception>
    public RectangleD GetInkBounds(Pen pen)
    {
        ArgumentNullException.ThrowIfNull(pen);
        if (InkBounds.IsEmpty)
            return InkBounds;

        if (pen.HasMarkers)
            return RectangleD.Union(InkBounds, ToPath(default).GetBounds(pen));

        return pen.GetStrokeBounds(InkBounds);
    }

    /// <summary>
    /// Gets the origin to draw the layout at so that a point of its box lands on an anchor: its center, a corner, the
    /// middle of a side, or a point of its first baseline.
    /// </summary>
    /// <remarks>
    /// The origin is the top-left corner of the layout, which <see cref="DrawingCanvas.FillText(Brush, TextLayout, PointD)"/>
    /// and <see cref="DrawingCanvas.StrokeText(Pen, TextLayout, PointD)"/> take. It is not rounded: glyphs are placed at
    /// fractions of a pixel, and rounding it is the choice of the caller.
    /// </remarks>
    /// <param name="anchor">Where the point of the box goes, on the canvas.</param>
    /// <param name="horizontal">The point of the box along x.</param>
    /// <param name="vertical">The point of the box along y.</param>
    /// <param name="bounds">The box: the box of the layout, or what is drawn.</param>
    /// <returns>The position of the top-left corner of the layout.</returns>
    /// <exception cref="ArgumentException">
    /// A coordinate of <paramref name="anchor"/> is not a finite number, or <paramref name="vertical"/> is
    /// <see cref="TextVerticalAnchor.Baseline"/> and the layout is vertical.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The magnitude of a coordinate of <paramref name="anchor"/> exceeds 16777216, or <paramref name="horizontal"/>,
    /// <paramref name="vertical"/> or <paramref name="bounds"/> is not a defined value.
    /// </exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs" ref:region="Imaging_PlaceText" ref:dedent="true">
    /// public static void DrawPayButton(string fontPath, string outputPath)
    /// {
    ///     var font = Font.Load(fontPath);
    ///     var label = TextLayout.Create("Pay now", font, size: 20);
    ///     var caption = TextLayout.Create("Total", font, size: 16);
    ///     var amount = TextLayout.Create("42.00", font, size: 24);
    ///
    ///     using var image = new Image&lt;Rgba32&gt;(240, 120, new Rgba32(255, 255, 255));
    ///     image.Draw(canvas =&gt;
    ///     {
    ///         // GetOrigin gives the top-left corner to draw a layout at so that a point of its box lands on an anchor.
    ///         // The ink box centers what is drawn, the tail of the y included; the layout box would center the line
    ///         var button = new RectangleD(20, 64, 200, 40);
    ///         canvas.FillRoundedRectangle(new Rgba32(30, 60, 160), button, radius: 8);
    ///         canvas.FillText(new Rgba32(255, 255, 255), label, label.GetOrigin(button, TextHorizontalAnchor.Center, TextVerticalAnchor.Center, TextBounds.Ink));
    ///
    ///         // Two sizes on one baseline: the caption from the left, the amount aligned to the right
    ///         var baseline = 44;
    ///         canvas.FillText(new Rgba32(90, 90, 90), caption, caption.GetOrigin(new PointD(20, baseline), TextHorizontalAnchor.Left, TextVerticalAnchor.Baseline));
    ///         canvas.FillText(new Rgba32(0, 0, 0), amount, amount.GetOrigin(new PointD(220, baseline), TextHorizontalAnchor.Right, TextVerticalAnchor.Baseline));
    ///     });
    ///
    ///     image.Save(outputPath);
    /// }
    /// </code>
    /// </example>
    public PointD GetOrigin(PointD anchor, TextHorizontalAnchor horizontal, TextVerticalAnchor vertical, TextBounds bounds = TextBounds.Layout)
    {
        DrawingGuard.Point(anchor, nameof(anchor));
        ValidateAnchors(horizontal, vertical, bounds);
        if (vertical == TextVerticalAnchor.Baseline && _vertical is not null)
            throw new ArgumentException("A vertical layout has no horizontal baseline.", nameof(vertical));

        var box = bounds == TextBounds.Ink && !InkBounds.IsEmpty ? InkBounds : new RectangleD(0, 0, Width, Height);
        var x = horizontal switch
        {
            TextHorizontalAnchor.Left => box.Left,
            TextHorizontalAnchor.Center => box.X + (box.Width / 2),
            _ => box.Right,
        };
        var y = vertical switch
        {
            TextVerticalAnchor.Top => box.Top,
            TextVerticalAnchor.Center => box.Y + (box.Height / 2),
            TextVerticalAnchor.Baseline => Baseline,
            _ => box.Bottom,
        };

        return new PointD(anchor.X - x, anchor.Y - y);
    }

    /// <summary>Gets the origin to draw the layout at so that its box is aligned in a rectangle: centered, or against a side or a corner.</summary>
    /// <remarks>
    /// The point of the box goes on the same point of the rectangle: its center on the center, its right side on the
    /// right side. A text larger than the rectangle overflows it, on both sides when it is centered. The origin is not rounded.
    /// </remarks>
    /// <param name="box">The rectangle, on the canvas.</param>
    /// <param name="horizontal">Where the text goes along x.</param>
    /// <param name="vertical">Where the text goes along y: not <see cref="TextVerticalAnchor.Baseline"/>, which a rectangle does not have.</param>
    /// <param name="bounds">The box of the text: the box of the layout, or what is drawn.</param>
    /// <returns>The position of the top-left corner of the layout.</returns>
    /// <exception cref="ArgumentException">An edge of <paramref name="box"/> is not a finite number, or <paramref name="vertical"/> is <see cref="TextVerticalAnchor.Baseline"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The magnitude of an edge of <paramref name="box"/> exceeds 16777216, or <paramref name="horizontal"/>,
    /// <paramref name="vertical"/> or <paramref name="bounds"/> is not a defined value.
    /// </exception>
    public PointD GetOrigin(RectangleD box, TextHorizontalAnchor horizontal, TextVerticalAnchor vertical, TextBounds bounds = TextBounds.Layout)
    {
        DrawingGuard.Rectangle(box, nameof(box));
        ValidateAnchors(horizontal, vertical, bounds);
        if (vertical == TextVerticalAnchor.Baseline)
            throw new ArgumentException("A rectangle has no baseline: place the text at a point of the baseline instead.", nameof(vertical));

        var x = horizontal switch
        {
            TextHorizontalAnchor.Left => box.Left,
            TextHorizontalAnchor.Center => box.X + (box.Width / 2),
            _ => box.Right,
        };
        var y = vertical switch
        {
            TextVerticalAnchor.Top => box.Top,
            TextVerticalAnchor.Center => box.Y + (box.Height / 2),
            _ => box.Bottom,
        };

        return GetOrigin(new PointD(x, y), horizontal, vertical, bounds);
    }

    private static void ValidateAnchors(TextHorizontalAnchor horizontal, TextVerticalAnchor vertical, TextBounds bounds)
    {
        if (!Enum.IsDefined(horizontal))
            throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "The horizontal anchor is not valid.");

        if (!Enum.IsDefined(vertical))
            throw new ArgumentOutOfRangeException(nameof(vertical), vertical, "The vertical anchor is not valid.");

        if (!Enum.IsDefined(bounds))
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "The bounds are not valid.");
    }

    /// <summary>Finds the character at a point of the layout, or the nearest one: what a click selects.</summary>
    /// <remarks>
    /// <para>
    /// The line is the one whose rows contain the point; above the first line it is the first one, and below the last
    /// line the last one. In a vertical layout, the line is the one whose columns contain the point, and a character
    /// is found down its line. On that line, the character is the one whose advance contains the point (the space from
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
        var along = point.X;
        bool isInLine;
        if (_vertical is null)
        {
            while (lineIndex < _lines.Length - 1 && point.Y >= _lines[lineIndex].Y + _lines[lineIndex].Height)
            {
                lineIndex++;
            }

            isInLine = point.Y >= _lines[lineIndex].Y && point.Y < _lines[lineIndex].Y + _lines[lineIndex].Height;
        }
        else
        {
            // The line whose columns contain the point, from the first one: the rightmost one or the leftmost one. A
            // character is then found down the line, as it was laid out.
            along = point.Y;
            while (lineIndex < _lines.Length - 1 && (_vertical.IsLeftToRight ? point.X >= _lines[lineIndex].X + _lines[lineIndex].Width : point.X < _lines[lineIndex].X))
            {
                lineIndex++;
            }

            isInLine = point.X >= _lines[lineIndex].X && point.X < _lines[lineIndex].X + _lines[lineIndex].Width;
        }

        var line = (_vertical?.Lines ?? _lines)[lineIndex];
        var boxes = _hitBoxes.AsSpan(_hitBoxStarts[lineIndex], _hitBoxStarts[lineIndex + 1] - _hitBoxStarts[lineIndex]);
        if (boxes.IsEmpty)
            return new TextHit(lineIndex, line.Start, 0, isTrailing: false, isInside: false);

        // The cluster whose advance contains the point; the nearest one beside the text
        var index = 0;
        while (index < boxes.Length - 1 && along >= boxes[index].Right)
        {
            index++;
        }

        ref readonly var box = ref boxes[index];
        var width = box.Right - box.Left;
        var isInside = isInLine && along >= box.Left && along < box.Right;

        // How far the point is in the cluster, in the direction of its text: from 0 at its leading edge to 1
        var position = width > 0 ? Math.Clamp((along - box.Left) / width, 0, 1) : along < box.Left ? 0 : 1;
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
    /// <para>
    /// In a vertical layout, the caret lies across its line: the rectangle is as wide as the line and has no height,
    /// and its top is the position of the caret.
    /// </para>
    /// </remarks>
    /// <param name="textIndex">The index in the text of the character the caret is before, in UTF-16 code units: from 0 to the length of the text, which is after its last character.</param>
    /// <returns>The rectangle, from the top-left corner of the layout: its left is the position of the caret, and its width is 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textIndex"/> is negative or greater than the length of the text.</exception>
    public RectangleD GetCaretRectangle(int textIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(textIndex, _textLength);

        // The line that holds the character; the last one for the end of the text
        var lines = _vertical?.Lines ?? _lines;
        var lineIndex = 0;
        while (lineIndex < lines.Length - 1 && textIndex >= lines[lineIndex].Start + lines[lineIndex].Length)
        {
            lineIndex++;
        }

        var line = lines[lineIndex];
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

        return GetLineRectangle(lineIndex, line, x, x);

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
    /// part of a ligature takes its share of the width of the ligature. In a vertical layout, each rectangle is as wide
    /// as its line and covers the characters that follow each other down it.
    /// </remarks>
    /// <param name="start">The index in the text of the first character of the range, in UTF-16 code units.</param>
    /// <param name="length">The number of UTF-16 code units of the range.</param>
    /// <returns>The rectangles, from the top-left corner of the layout, line by line and from left to right on each line (from top to bottom on the lines of a vertical layout); none for an empty range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="length"/> is negative, or the range goes beyond the end of the text.</exception>
    public IReadOnlyList<RectangleD> GetSelectionRectangles(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _textLength - start);
        var end = start + length;
        var rectangles = new List<RectangleD>();
        var lines = _vertical?.Lines ?? _lines;
        for (var lineIndex = 0; lineIndex < lines.Length && length > 0; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Start >= end || line.Start + line.Length <= start)
                continue;

            // Where the rectangle before this one starts and ends along the line, when there is one on the line
            var hasPrevious = false;
            var previousLeft = 0d;
            var previousRight = 0d;
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
                if (hasPrevious && Math.Abs(previousRight - left) <= 1e-9)
                {
                    // It continues the rectangle before it
                    previousRight = right;
                    rectangles[^1] = GetLineRectangle(lineIndex, line, previousLeft, previousRight);
                }
                else
                {
                    hasPrevious = true;
                    previousLeft = left;
                    previousRight = right;
                    rectangles.Add(GetLineRectangle(lineIndex, line, left, right));
                }
            }
        }

        return rectangles;
    }

    /// <summary>Gets the rectangle that covers a line from a position along it to another one, on the image.</summary>
    /// <param name="lineIndex">The index of the line.</param>
    /// <param name="line">The line as it was laid out: the one of a vertical layout is not turned.</param>
    /// <param name="start">Where the rectangle starts along the line.</param>
    /// <param name="end">Where the rectangle ends along the line.</param>
    private RectangleD GetLineRectangle(int lineIndex, TextLine line, double start, double end)
    {
        if (_vertical is null)
            return RectangleD.FromLTRB(start, line.Y, end, line.Y + line.Height);

        // A vertical line is a column: what is along it is down the image
        var right = _vertical.LineRights[lineIndex];
        return RectangleD.FromLTRB(right - line.Height, start, right, end);
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
    /// <param name="outline">The pen of <see cref="TextStyle.Outline"/> whose glyphs are sent, without the decorations, or -1 for the glyphs of every run.</param>
    internal bool Emit(PathSink target, GlyphPathSink glyphSink, double originX, double originY, EdgeBuilder? edges, int brush, bool skipColorGlyphs = false, Stroker? emboldener = null, int outline = -1)
    {
        var coversFrame = false;
        foreach (var run in _runs)
        {
            if ((brush >= 0 && run.Brush != brush) || (outline >= 0 && run.Outline != outline))
                continue;

            var scale = run.Size / _fonts[run.Font].Face.UnitsPerEm;
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
                var transform = run.GetTransform(scale, originX + _positions[i * 2], originY + _positions[(i * 2) + 1]);
                glyphSink.Begin(target, transform);
                _recordedOutlines.Replay(_outlineIndexes[i], glyphSink);

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
                    coversFrame |= Embolden(emboldener!, i, transform, glyphSink);
                }
            }
        }

        // The decorations, in the orientation the glyphs are given; the pen of a style strokes its glyphs only.
        if (outline < 0)
        {
            EmitRectangles(target, _decorations, originX, originY, brush);
        }

        target.End();
        return coversFrame;
    }

    /// <summary>Sends the rectangles of the backgrounds of one paint, placed on the canvas, to the sink of a fill.</summary>
    internal void EmitBackgrounds(PathSink target, double originX, double originY, int brush)
    {
        EmitRectangles(target, _backgrounds, originX, originY, brush);
        target.End();
    }

    /// <summary>Sends the rectangles of the backgrounds of one paint, placed along a path as the decorations are, to the sink of a fill.</summary>
    internal void EmitBackgroundsOnPath(PathSink target, PathMeasure measure, TextPathOptions options, int brush)
    {
        if (measure.Length > 0)
        {
            EmitRectanglesOnPath(target, _vertical?.Backgrounds ?? _backgrounds, measure, options, brush);
        }

        target.End();
    }

    private static void EmitRectangles(PathSink target, ReadOnlySpan<DecorationRectangle> rectangles, double originX, double originY, int brush)
    {
        foreach (ref readonly var rectangle in rectangles)
        {
            if (brush >= 0 && rectangle.Brush != brush)
                continue;

            var left = originX + rectangle.X;
            var top = originY + rectangle.Y;
            target.MoveTo(left, top);
            target.LineTo(left + rectangle.Width, top, smooth: false);
            target.LineTo(left + rectangle.Width, top + rectangle.Height, smooth: false);
            target.LineTo(left, top + rectangle.Height, smooth: false);
            target.Close(smooth: false);
        }
    }

    /// <summary>
    /// Thickens a glyph that was just filled: its outline is sent again, to a stroker that adds the pieces of its
    /// stroke to the edges of the fill. The pieces have the orientation the glyphs of a fill are given, so the stroke
    /// and the glyph add up, and what is filled is the glyph grown by half the width of the stroke.
    /// </summary>
    /// <returns><see langword="true"/> when a piece of the stroke covers the whole frame.</returns>
    private bool Embolden(Stroker emboldener, int glyph, in Affine2D transform, GlyphPathSink glyphSink)
    {
        glyphSink.Begin(emboldener, transform);
        _recordedOutlines.Replay(_outlineIndexes[glyph], glyphSink);
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
    /// A vertical layout follows the path in the coordinates of its lines (<see cref="VerticalLayoutData"/>): what is
    /// along a line is along the path, and an upright glyph stands across it.
    /// </para>
    /// <para>
    /// The rotation is the unit vector of a chord of the path: no trigonometric function is involved, and a text on a
    /// horizontal path has the points of the same text drawn at a point.
    /// </para>
    /// </remarks>
    internal bool EmitOnPath(PathSink target, GlyphPathSink glyphSink, PathMeasure measure, TextPathOptions options, EdgeBuilder? edges, int brush, bool skipColorGlyphs = false, Stroker? emboldener = null, int outline = -1)
    {
        var coversFrame = false;
        var length = measure.Length;
        if (!(length > 0))
        {
            target.End();
            return false;
        }

        foreach (var run in _runs)
        {
            if ((brush >= 0 && run.Brush != brush) || (outline >= 0 && run.Outline != outline))
                continue;

            var scale = run.Size / _fonts[run.Font].Face.UnitsPerEm;
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

                if (!TryGetGlyphTransformOnPath(i, scale, run.IsSideways, run.WidthScale, measure, options, out var transform))
                    continue;

                // A slanted glyph is slanted before it is placed and turned.
                if (run.Shear != 0)
                {
                    transform = transform.Then([1, 0, run.Shear, 1, 0, 0]);
                }

                var first = edges?.Count ?? 0;
                glyphSink.Begin(target, transform);
                _recordedOutlines.Replay(_outlineIndexes[i], glyphSink);
                if (edges is not null && glyphSink.SignedArea < 0)
                {
                    edges.NegateDirections(first);
                }

                if (isEmboldened && !isColorGlyph)
                {
                    // The fill of the glyph is complete before the edges of its stroke follow it.
                    target.End();
                    coversFrame |= Embolden(emboldener!, i, transform, glyphSink);
                }
            }
        }

        if (outline < 0)
        {
            EmitRectanglesOnPath(target, _vertical?.Decorations ?? _decorations, measure, options, brush);
        }

        target.End();
        return coversFrame;
    }

    /// <summary>Sends rectangles of the layout, placed along a path: one quadrilateral per chord of the path each one runs along, turned as the clusters are.</summary>
    private void EmitRectanglesOnPath(PathSink target, ReadOnlySpan<DecorationRectangle> rectangles, PathMeasure measure, TextPathOptions options, int brush)
    {
        var length = measure.Length;
        var shift = GetPathShift(length, options);
        var baseline = GetPathBaseline(options);
        var isReversed = options.Side == TextPathSide.Right;
        var isClipped = options.Overflow == TextPathOverflow.Clip;
        foreach (ref readonly var decoration in rectangles)
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
    internal bool TryGetGlyphTransformOnPath(int glyph, double scale, bool isSideways, double widthScale, PathMeasure measure, TextPathOptions options, out Affine2D transform)
    {
        var length = measure.Length;
        transform = default;
        if (!(length > 0))
            return false;

        var shift = GetPathShift(length, options);
        var baseline = GetPathBaseline(options);
        if (!TryLocate(measure, shift + _anchors[glyph], options.Side == TextPathSide.Right, options.Overflow == TextPathOverflow.Clip, out var x, out var y, out var directionX, out var directionY))
            return false;

        // The origin of the glyph from the point of its cluster: along the path, then across it. The side the
        // layout grows down to is on the right of the direction of the path. A vertical layout follows the path as
        // it was laid out: each line as a horizontal one, read from its top.
        var positions = _vertical?.Positions ?? _positions;
        var along = positions[glyph * 2] - _anchors[glyph];
        var across = positions[(glyph * 2) + 1] - baseline;
        var originX = (x + (along * directionX)) - (across * directionY);
        var originY = (y + (along * directionY)) + (across * directionX);

        // An upright glyph of a vertical line stands across the line: its top is where the line comes from. A
        // narrowed glyph is narrowed along its own baseline.
        transform = _vertical is not null && !isSideways
            ? new Affine2D(scale * widthScale * directionY, -(scale * widthScale * directionX), -(scale * directionX), -(scale * directionY), originX, originY)
            : new Affine2D(scale * directionX, scale * directionY, scale * directionY, -(scale * directionX), originX, originY);
        return true;
    }

    /// <summary>Gets how far along a path the text starts.</summary>
    private double GetPathShift(double length, TextPathOptions options)
    {
        // The length of the text along its lines: its width, or the height of a vertical layout
        var size = _vertical?.InlineSize ?? Width;
        return options.StartOffset + options.Alignment switch
        {
            TextPathAlignment.Center => (length - size) / 2,
            TextPathAlignment.End => length - size,
            _ => 0,
        };
    }

    /// <summary>Gets where the baseline of the first line is across the lines, as it follows a path.</summary>
    private double GetPathBaseline(TextPathOptions options)
    {
        var line = (_vertical?.Lines ?? _lines)[0];
        return line.Y + line.Baseline + options.BaselineOffset;
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

    /// <summary>Decodes each glyph of the layout once, which validates it, records its outline, and measures what the layout draws.</summary>
    private RectangleD Measure(HintedOutlines? hintedOutlines, out ColorGlyph[] colorGlyphs, out int[]? colorIndexes, out RecordedOutlines recordedOutlines, out int[] outlineIndexes)
    {
        // The display list of each distinct color glyph of a font and a palette.
        var colors = new List<ColorGlyph>();
        var colorKeys = new Dictionary<(int Font, int Glyph, int Palette, double Size), int>();
        colorIndexes = null;
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        // The outline and the bounds of each distinct glyph of a font at a size, in font units: four values, or none for a
        // glyph without outline.
        var outlines = new Dictionary<(int Font, int Glyph, double Size), (int Outline, (double MinX, double MinY, double MaxX, double MaxY)? Bounds)>();
        var recorder = new RecordedOutlines.Recorder();
        outlineIndexes = new int[_glyphs.Length];
        foreach (var run in _runs)
        {
            var face = _fonts[run.Font].Face;
            var scale = run.Size / face.UnitsPerEm;
            using var scratch = new GlyphScratch(face.Data.Environment.CreateAllocator("TextLayout"));
            for (var i = run.Start; i < run.Start + run.Count; i++)
            {
                if (!outlines.TryGetValue((run.Font, _glyphs[i], run.Size), out var recorded))
                {
                    recorder.Begin();
                    if (hintedOutlines is null || !hintedOutlines.TryReplay(_fonts[run.Font], _glyphs[i], run.Size, recorder))
                    {
                        var budget = new GlyphBudget(face.Data.Limits);
                        face.DecodeOutline(_glyphs[i], run.Size, recorder, scratch, ref budget);
                    }

                    var sink = recorder.Bounds;
                    recorded = (recorder.End(), sink.IsEmpty ? null : (sink.MinX, sink.MinY, sink.MaxX, sink.MaxY));
                    outlines.Add((run.Font, _glyphs[i], run.Size), recorded);
                }

                outlineIndexes[i] = recorded.Outline;
                var glyphBounds = recorded.Bounds;

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
                            colorGlyph = BitmapGlyphDecoder.CreateGlyph(face, bitmap, run.Size);
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
                var transform = run.GetTransform(scale, _positions[i * 2], _positions[(i * 2) + 1], isSlanted: isOutline);
                var margin = isOutline ? run.EmboldenWidth * (GlyphRun.BoldMiterLimit / 2) : 0;
                if (run.IsSideways)
                {
                    // On its side, the top of the glyph is on its right, and its left at its top
                    left = Math.Min(left, transform.TransformX(box.MinX, box.MinY) - margin);
                    right = Math.Max(right, transform.TransformX(box.MinX, box.MaxY) + margin);
                    top = Math.Min(top, Math.Min(transform.TransformY(box.MinX, box.MinY), transform.TransformY(box.MinX, box.MaxY)) - margin);
                    bottom = Math.Max(bottom, Math.Max(transform.TransformY(box.MaxX, box.MinY), transform.TransformY(box.MaxX, box.MaxY)) + margin);
                    continue;
                }

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

        foreach (var background in _backgrounds)
        {
            left = Math.Min(left, background.X);
            top = Math.Min(top, background.Y);
            right = Math.Max(right, background.X + background.Width);
            bottom = Math.Max(bottom, background.Y + background.Height);
        }

        colorGlyphs = [.. colors];
        recordedOutlines = recorder.Build();
        return left <= right ? RectangleD.FromLTRB(left, top, right, bottom) : RectangleD.Empty;
    }
}
