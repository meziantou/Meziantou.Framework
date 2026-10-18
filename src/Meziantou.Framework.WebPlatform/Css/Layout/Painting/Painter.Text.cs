using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the text fragments: one glyph run per font run of the shaped text, at the positions given by the advances
// and the offsets of the managed text shaping (the spacing of the line is already in the advances, #357), with the shadows,
// the strokes, the highlights, the decorations and the emphasis marks (Painter.TextDecorations.cs).
internal sealed partial class Painter
{
    /// <summary>The paint of the glyphs instead of the colors of the texts (e.g. the mask of <c>background-clip: text</c>), or null.</summary>
    private DisplayPaint? _textPaintOverride;

    /// <summary>
    /// A glyph run of a text fragment, drawn in a coordinate system (rotated for the sideways glyphs of vertical text), with
    /// the palette of its color glyphs when its font has some.
    /// </summary>
    private readonly record struct PositionedGlyphRun(GlyphRun Run, DisplayMatrix? Transform, FontPalette? Palette = null);

    /// <summary>
    /// Paints a text fragment (https://drafts.csswg.org/css2/#elaborate-stacking-contexts, step 7.2.1.4.1.1.1), in the painting
    /// order of https://drafts.csswg.org/css-text-decor-4/#painting-order: the text shadows, the backgrounds of the highlights
    /// (the selection, the custom highlights, the block caret over a character) and the shadows of their text, the underlines and overlines, the glyphs (filled with <c>-webkit-text-fill-color</c>,
    /// which is <c>color</c> by default, and stroked with <c>-webkit-text-stroke</c>), the emphasis marks, the line-throughs,
    /// then the decorations of the highlights and the glyphs they recolor (the glyphs are painted once: the characters recolored
    /// by a highlight are not painted in the color of the text).
    /// </summary>
    private void PaintText(TextFragment fragment, PhysicalOffset offset)
    {
        var style = fragment.Style;
        if (style.Layout.Visibility != Visibility.Visible)
            return;

        var source = (Dom.Node?)fragment.Run.Node ?? fragment.Run.Element;
        var runs = fragment.ShapedText is not null && fragment.GlyphCount > 0 ? BuildGlyphRuns(fragment, offset) : [];
        if (_textPaintOverride is { } mask)
        {
            // The mask of background-clip: text is "the geometry of the text including any decorations and emphasis marks"
            var maskLine = GetTextLine(fragment, offset);
            PaintDecorations(fragment, maskLine, GetAppliedDecorations(fragment), TextDecorationLines.Underline | TextDecorationLines.Overline | TextDecorationLines.LineThrough, mask, source);
            DrawGlyphRuns(runs, mask, source);
            PaintEmphasisMarks(fragment, offset, maskLine, source, mask);
            return;
        }

        var decorations = GetAppliedDecorations(fragment);
        if (runs.Count == 0 && decorations is null)
            return;

        var line = GetTextLine(fragment, offset);
        var fill = GetColor(style, "-webkit-text-fill-color");

        // https://drafts.csswg.org/css-text-decor-3/#text-shadow-property: the shadows of the glyphs and of the decorations, the
        // first one on top
        if (GetShadows(style, "text-shadow") is { } shadows)
        {
            for (var i = shadows.Count - 1; i >= 0; i--)
            {
                var shadow = shadows[i];
                if (shadow.Color.IsTransparent)
                    continue;

                _builder.Save();
                _builder.Transform(DisplayMatrix.Translation(shadow.X, shadow.Y));
                var shadowPaint = new DisplayPaint(shadow.Color) { BlurSigma = shadow.Blur / 2 };
                PaintDecorations(fragment, line, decorations, TextDecorationLines.Underline | TextDecorationLines.Overline, shadowPaint, source);
                DrawGlyphRuns(runs, shadowPaint, source);
                PaintDecorations(fragment, line, decorations, TextDecorationLines.LineThrough, shadowPaint, source);
                _builder.Restore();
            }
        }

        // https://svgwg.org/svg2-draft/painting.html#PaintOrder: the stroke is painted over the fill unless paint-order puts it first
        var textStroke = TextStroke.From(style).ToPaint();
        var strokeFirst = style.GetComputedValue("paint-order").StartsWith("stroke", StringComparison.Ordinal);

        // https://drafts.csswg.org/css-pseudo-4/#highlight-painting: the background of each highlight overlay, in the order of
        // the overlays, and over it the shadows of its text ("Any text-shadow applying to a highlight pseudo-element is drawn
        // over its corresponding highlight overlay background [...] Such text shadows also stack over each other", over the
        // shadows of the originating text, which are painted for all the text; the background of an overlay above may cover
        // the shadows of the overlays below). The shadows are under the text of the whole fragment, also the text that is not
        // highlighted (https://drafts.csswg.org/css-text-decor-4/#text-shadow-property: "UAs should avoid painting text
        // shadows over text in adjacent elements"; as Blink when several highlights overlap)
        var highlights = GetHighlights(fragment, offset);
        if (_caret is not null)
        {
            highlights = AddBlockCaret(highlights, fragment, offset);
        }

        if (highlights is not null)
        {
            foreach (var highlight in highlights)
            {
                if (!highlight.Background.IsTransparent)
                {
                    _builder.Add(new DrawRectItem(highlight.Rect, new DisplayPaint(highlight.Background)) { Source = source });
                }

                if (highlight.Shadows is not { } highlightShadows || runs.Count == 0)
                    continue;

                // The shadows of the highlighted characters, the first one on top
                var bounds = highlight.Bounds;
                var (rangeStart, rangeEnd) = line.Horizontal ? (bounds.X, bounds.Right) : (bounds.Y, bounds.Bottom);
                for (var i = highlightShadows.Count - 1; i >= 0; i--)
                {
                    var shadow = highlightShadows[i];
                    if (shadow.Color.IsTransparent)
                        continue;

                    _builder.Save();
                    _builder.Transform(DisplayMatrix.Translation(shadow.X, shadow.Y));
                    PaintGlyphsBetween(rangeStart, rangeEnd, shadow.Color, stroke: null, new DisplayPaint(shadow.Color) { BlurSigma = shadow.Blur / 2 });
                    _builder.Restore();
                }
            }
        }

        PaintDecorations(fragment, line, decorations, TextDecorationLines.Underline | TextDecorationLines.Overline, paint: null, source);

        // https://drafts.csswg.org/css-pseudo-4/#highlight-painting: the glyphs are painted once, in the color of the topmost
        // highlight that sets a color for the characters it covers and in the color of the text for the other ones (glyphs
        // painted twice would have darker antialiased edges)
        var colorSegments = runs.Count == 0 ? null : GetHighlightColorSegments(highlights, line.Horizontal);
        if (colorSegments is null)
        {
            PaintGlyphs(runs, fill, textStroke);
        }
        else
        {
            // The characters that are not recolored: before, between and after the segments
            var position = double.NegativeInfinity;
            foreach (var segment in colorSegments)
            {
                PaintGlyphsBetween(position, segment.Start, fill, textStroke);
                position = segment.End;
            }

            PaintGlyphsBetween(position, double.PositiveInfinity, fill, textStroke);
        }

        PaintEmphasisMarks(fragment, offset, line, source);
        PaintDecorations(fragment, line, decorations, TextDecorationLines.LineThrough, paint: null, source);

        // The decorations of the highlights, and the glyphs they recolor
        if (highlights is not null)
        {
            for (var i = 0; i < highlights.Count; i++)
            {
                var highlight = highlights[i];
                PaintOriginatingDecorations(highlight, TextDecorationLines.Underline | TextDecorationLines.Overline);
                PaintHighlightDecorations(highlight, TextDecorationLines.Underline | TextDecorationLines.Overline);
                if (colorSegments is not null)
                {
                    foreach (var segment in colorSegments)
                    {
                        if (segment.Highlight == i)
                        {
                            PaintGlyphsBetween(segment.Start, segment.End, highlight.Color!.Value, highlight.Stroke.ToPaint());
                        }
                    }
                }

                PaintOriginatingDecorations(highlight, TextDecorationLines.LineThrough);
                PaintHighlightDecorations(highlight, TextDecorationLines.LineThrough);
            }
        }

        // https://drafts.csswg.org/css-pseudo-4/#highlight-painting: "The element's own text decorations [...] are thus drawn in
        // the pseudo-element's own color when that is not currentColor, regardless of their original color"
        void PaintOriginatingDecorations(in TextHighlight highlight, TextDecorationLines lines)
        {
            if (!highlight.Recolors || highlight.Color is not { } color || decorations is null)
                return;

            var clip = highlight.Rect;
            _builder.Save();
            _builder.Add(new ClipRectItem(line.Horizontal ? new PhysicalRect(clip.X, clip.Y - UnclippedExtent, clip.Width, clip.Height + (2 * UnclippedExtent)) : new PhysicalRect(clip.X - UnclippedExtent, clip.Y, clip.Width + (2 * UnclippedExtent), clip.Height)));
            PaintDecorations(fragment, line, decorations, lines, new DisplayPaint(color), source);
            _builder.Restore();
        }

        void PaintHighlightDecorations(in TextHighlight highlight, TextDecorationLines lines)
        {
            if (highlight.Decorations is null)
                return;

            // The lines of the highlighted characters, as the lines of an inline box of these characters (a wavy line starts at
            // the first highlighted character and is not cut at the last one)
            var bounds = highlight.Bounds;
            PaintDecorations(fragment, line, highlight.Decorations, lines, paint: null, source, line.Horizontal ? (bounds.X, bounds.Right) : (bounds.Y, bounds.Bottom));
        }

        // Paints the glyphs between two positions of the inline axis (physical coordinates): the glyphs whose advance is between
        // the positions are painted whole (their ink may be outside of their advance), and the glyphs that are partially between
        // the positions (e.g. a ligature of highlighted and not highlighted characters) are clipped
        void PaintGlyphsBetween(double start, double end, DisplayColor color, DisplayPaint? stroke, DisplayPaint? shadow = null)
        {
            if (end <= start)
                return;

            var coverage = GetGlyphCoverage(fragment, offset, start, end);
            if (coverage is null)
            {
                PaintClipped(runs);
                return;
            }

            // (a shadow is the shape of the glyphs in the paint of the shadow)
            void PaintRuns(List<PositionedGlyphRun> glyphRuns)
            {
                if (shadow is not null)
                {
                    DrawGlyphRuns(glyphRuns, shadow, source);
                }
                else
                {
                    PaintGlyphs(glyphRuns, color, stroke);
                }
            }

            // The consecutive glyphs covered the same way are painted together
            for (var i = 0; i < coverage.Length;)
            {
                var first = i;
                for (i++; i < coverage.Length && coverage[i] == coverage[first]; i++)
                {
                }

                if (coverage[first] == GlyphCoverage.Full)
                {
                    PaintRuns(BuildGlyphRuns(fragment, offset, first, i));
                }
                else if (coverage[first] == GlyphCoverage.Partial)
                {
                    PaintClipped(BuildGlyphRuns(fragment, offset, first, i));
                }
            }

            void PaintClipped(List<PositionedGlyphRun> glyphRuns)
            {
                var from = double.IsNegativeInfinity(start) ? -UnclippedExtent : SnapToDevicePixels(start);
                var to = double.IsPositiveInfinity(end) ? UnclippedExtent : SnapToDevicePixels(end);
                _builder.Save();
                _builder.Add(new ClipRectItem(line.Horizontal ? new PhysicalRect(from, -UnclippedExtent, to - from, 2 * UnclippedExtent) : new PhysicalRect(-UnclippedExtent, from, 2 * UnclippedExtent, to - from)));
                PaintRuns(glyphRuns);
                _builder.Restore();
            }
        }

        void PaintGlyphs(List<PositionedGlyphRun> glyphRuns, DisplayColor color, DisplayPaint? stroke)
        {
            if (stroke is not null && strokeFirst)
            {
                DrawGlyphRuns(glyphRuns, stroke, source);
            }

            if (!color.IsTransparent)
            {
                DrawGlyphRuns(glyphRuns, new DisplayPaint(color), source, colorize: true);
            }
            else if (stroke is null)
            {
                // The glyphs of a transparent text draw nothing, but the text is painted: it is contentful for the paint timing
                // (https://w3c.github.io/paint-timing/#contentful, see ContentfulPaintScanner)
                DrawGlyphRuns(glyphRuns, new DisplayPaint(DisplayColor.Transparent), source);
            }

            if (stroke is not null && !strokeFirst)
            {
                DrawGlyphRuns(glyphRuns, stroke, source);
            }
        }
    }

    /// <summary>
    /// Returns the parts of the inline axis of a text fragment whose glyphs are painted in the color of a highlight: disjoint
    /// segments in order (positions of the layout, not snapped), each with the index of the topmost highlight that sets a color
    /// for it. Returns null when no highlight sets a color.
    /// </summary>
    private static List<(double Start, double End, int Highlight)>? GetHighlightColorSegments(List<TextHighlight>? highlights, bool horizontal)
    {
        if (highlights is null || !highlights.Exists(highlight => highlight.Color is not null))
            return null;

        var boundaries = new SortedSet<double>();
        foreach (var highlight in highlights)
        {
            if (highlight.Color is null)
                continue;

            var (start, end) = GetRange(highlight);
            boundaries.Add(start);
            boundaries.Add(end);
        }

        var result = new List<(double Start, double End, int Highlight)>();
        double? previous = null;
        foreach (var boundary in boundaries)
        {
            if (previous is { } segmentStart)
            {
                // The highlights are in their painting order: the last one that covers the segment is on top
                var top = -1;
                for (var i = 0; i < highlights.Count; i++)
                {
                    if (highlights[i].Color is null)
                        continue;

                    var (start, end) = GetRange(highlights[i]);
                    if (start <= segmentStart && boundary <= end)
                    {
                        top = i;
                    }
                }

                if (top >= 0)
                {
                    if (result.Count > 0 && result[^1].Highlight == top && result[^1].End == segmentStart)
                    {
                        result[^1] = (result[^1].Start, boundary, top);
                    }
                    else
                    {
                        result.Add((segmentStart, boundary, top));
                    }
                }
            }

            previous = boundary;
        }

        return result;

        (double Start, double End) GetRange(in TextHighlight highlight) => horizontal ? (highlight.Bounds.Left, highlight.Bounds.Right) : (highlight.Bounds.Top, highlight.Bounds.Bottom);
    }

    /// <summary>How the advance of a glyph is covered by a part of the inline axis.</summary>
    private enum GlyphCoverage : byte
    {
        None,
        Partial,
        Full,
    }

    /// <summary>
    /// Returns how the advance of each glyph of a text fragment is covered by a part of the inline axis (physical coordinates,
    /// in the coordinate system of <paramref name="offset"/>). A glyph without advance (a combining mark) is covered as the
    /// glyph before it. Returns null when the glyphs are not laid out along the line (a composition combined upright).
    /// </summary>
    private static GlyphCoverage[]? GetGlyphCoverage(TextFragment fragment, PhysicalOffset offset, double start, double end)
    {
        if (fragment.ShapedText is not { IsCombinedUpright: false })
            return null;

        // The tolerance of the comparisons: the positions are sums of the same advances in different orders
        const double Epsilon = 1e-3;
        var glyphs = fragment.Glyphs;
        var result = new GlyphCoverage[glyphs.Length];
        var size = fragment.Size;
        var inlineStart = fragment.WritingDirection.InlineStart;
        var pen = 0d;
        var previous = GlyphCoverage.None;
        for (var i = 0; i < glyphs.Length; i++)
        {
            var advance = glyphs[i].Advance;
            if (advance > 0 || i == 0)
            {
                var low = inlineStart switch
                {
                    PhysicalSide.Left => offset.X + pen,
                    PhysicalSide.Right => offset.X + size.Width - pen - advance,
                    PhysicalSide.Top => offset.Y + pen,
                    _ => offset.Y + size.Height - pen - advance,
                };
                var high = low + advance;
                previous = low >= start - Epsilon && high <= end + Epsilon && (advance > 0 || high > start)
                    ? GlyphCoverage.Full
                    : high > start + Epsilon && low < end - Epsilon ? GlyphCoverage.Partial : GlyphCoverage.None;
            }

            result[i] = previous;
            pen += advance;
        }

        return result;
    }

    /// <summary>
    /// Draws glyph runs with a paint. The color glyphs are drawn with the colors of their palette when
    /// <paramref name="colorize"/> (the fill of the text), else with the paint (a shadow, a mask); they are not stroked.
    /// </summary>
    private void DrawGlyphRuns(List<PositionedGlyphRun> runs, DisplayPaint paint, Dom.Node? source, bool colorize = false)
    {
        foreach (var run in runs)
        {
            if (run.Transform is { } transform)
            {
                _builder.Save();
                _builder.Transform(transform);
                AddGlyphRun(run.Run, run.Palette, paint, colorize, source);
                _builder.Restore();
            }
            else
            {
                AddGlyphRun(run.Run, run.Palette, paint, colorize, source);
            }
        }
    }

    /// <summary>
    /// Builds the glyph runs of a text fragment, or of a range of its glyphs (<paramref name="glyphStart"/> and
    /// <paramref name="glyphEnd"/> are indexes in the glyphs of the fragment): one glyph run per font run of the shaped text.
    /// </summary>
    /// <remarks>
    /// In horizontal text, the glyphs are on the baseline of the fragment, from its left edge (its right edge for a right-to-left
    /// run). In vertical text, the glyphs that are not upright are drawn in a coordinate system rotated by 90 degrees clockwise
    /// (counter-clockwise for <c>sideways-lr</c>) whose x axis is the inline axis, on their alphabetic baseline (below the central
    /// baseline of the line for <c>text-orientation: mixed</c>); the upright glyphs are centered on the central baseline. A
    /// composition combined upright is drawn horizontally, compressed by its scale.
    /// </remarks>
    private List<PositionedGlyphRun> BuildGlyphRuns(TextFragment fragment, PhysicalOffset offset, int glyphStart = 0, int glyphEnd = int.MaxValue)
    {
        var result = new List<PositionedGlyphRun>(1);
        var style = fragment.Style;
        var shaped = fragment.ShapedText!;
        var glyphs = fragment.Glyphs;
        var size = fragment.Size;
        var writingDirection = fragment.WritingDirection;
        var horizontal = writingDirection.IsHorizontal;
        var inlineSize = horizontal ? size.Width : size.Height;

        // The dominant baseline across the fragment, from its block-start edge: the alphabetic baseline, or the central baseline
        // of vertical text (https://drafts.csswg.org/css-inline-3/#dominant-baseline-property)
        var isCentral = !horizontal && style.Layout.HasCentralDominantBaseline;
        var baseline = GetPhysicalBaseline(fragment);

        // The pen positions along the inline axis, in the order of the glyphs
        var pens = new double[glyphs.Length];
        var pen = 0d;
        for (var i = 0; i < glyphs.Length; i++)
        {
            pens[i] = pen;
            pen += glyphs[i].Advance;
        }

        foreach (var run in shaped.Runs)
        {
            var start = Math.Max(run.GlyphStart, fragment.GlyphStart + glyphStart);
            var end = Math.Min(run.GlyphStart + run.GlyphCount, fragment.GlyphStart + Math.Min(fragment.GlyphCount, glyphEnd));
            if (start >= end)
                continue;

            var font = DisplayFont.From(run.Font, run.EmSize);
            var palette = GetFontPalette(style, run.Font);
            var text = GetText(fragment, shaped, start, end);
            if (shaped.IsCombinedUpright)
            {
                // https://drafts.csswg.org/css-writing-modes-4/#text-combine-layout: the composition is centered in the 1em
                // square of the line, "similar to the contents of an inline-block box with a horizontal writing mode and a
                // line-height of 1em": its baseline is the one of a line of 1em (the half-leading of the ascent side is floored
                // to whole pixels, see InlineLayoutAlgorithm.GetHalfLeadings)
                var metrics = run.Font.GetMetricsForEmSize(run.BaseEmSize);
                var x = offset.X + (isCentral ? baseline : size.Width / 2) - (shaped.Width / 2);
                var y = SnapToDevicePixels(offset.Y + Math.Floor((size.Height - (metrics.Ascent + metrics.Descent)) / 2) + metrics.Ascent);
                var combined = CreateRun(font, glyphs, pens, start - fragment.GlyphStart, end - fragment.GlyphStart, shaped.Width, forward: !shaped.Options.IsRightToLeft, new PhysicalOffset(x, y), text);
                Add(combined is null ? null : combined with { ScaleX = shaped.CombinedScale }, null, palette);
            }
            else if (horizontal)
            {
                var forward = writingDirection.InlineStart == PhysicalSide.Left;

                // The baseline is snapped to the device pixels (the glyphs keep their subpixel positions along the line)
                Add(CreateRun(font, glyphs, pens, start - fragment.GlyphStart, end - fragment.GlyphStart, inlineSize, forward, new PhysicalOffset(offset.X, SnapToDevicePixels(offset.Y + baseline)), text), null, palette);
            }
            else if (run.IsUpright)
            {
                var center = isCentral ? baseline : size.Width / 2;

                // https://drafts.csswg.org/css-fonts-4/#font-synthesis-style: the synthesized oblique of the upright glyphs is a
                // vertical skew (along the inline axis) around the center of their column, as in Firefox (WPT
                // css/css-fonts/test-synthetic-italic-2.html): the backend slants the glyphs horizontally
                DisplayMatrix? transform = null;
                if (font.SyntheticObliqueAngle != 0)
                {
                    var tan = Math.Tan(font.SyntheticObliqueAngle * Math.PI / 180);
                    transform = new DisplayMatrix(1, tan, 0, 1, 0, -tan * (offset.X + center));
                    font = font with { SyntheticObliqueAngle = 0 };
                }

                Add(CreateUprightRun(run, font, glyphs, pens, start - fragment.GlyphStart, end - fragment.GlyphStart, size, center, forward: writingDirection.InlineStart == PhysicalSide.Top, offset, text), transform, palette);
            }
            else
            {
                // The glyphs are rotated so that their ascent is on the line-over side
                var clockwise = writingDirection.LineOver == PhysicalSide.Right;
                var forward = writingDirection.InlineStart == (clockwise ? PhysicalSide.Top : PhysicalSide.Bottom);
                var alphabetic = baseline;
                if (isCentral)
                {
                    // The alphabetic baseline of the font is below its central baseline, towards the line-under side (the
                    // synthesized small capitals and subscripts and superscripts are on the baseline of the font of the text)
                    var metrics = run.Font.GetMetricsForEmSize(run.BaseEmSize);
                    alphabetic += (clockwise ? -1 : 1) * InlineLayoutAlgorithm.GetCentralBaseline(run.Font, metrics);
                }

                var origin = clockwise ? new PhysicalOffset(SnapToDevicePixels(offset.X + alphabetic), offset.Y) : new PhysicalOffset(SnapToDevicePixels(offset.X + alphabetic), offset.Y + size.Height);
                var transform = DisplayMatrix.Translation(origin.X, origin.Y).PreConcat(DisplayMatrix.Rotation(clockwise ? 90 : -90));
                Add(CreateRun(font, glyphs, pens, start - fragment.GlyphStart, end - fragment.GlyphStart, inlineSize, forward, PhysicalOffset.Zero, text), transform, palette);
            }
        }

        return result;

        void Add(GlyphRun? run, DisplayMatrix? transform, FontPalette? palette)
        {
            if (run is not null)
            {
                result.Add(new PositionedGlyphRun(run, transform, palette));
            }
        }
    }

    /// <summary>Returns the distance of the dominant baseline of a text fragment from its left (vertical text) or top (horizontal text) edge.</summary>
    private static double GetPhysicalBaseline(TextFragment fragment)
    {
        var size = fragment.Size;
        return fragment.WritingDirection.BlockStart switch
        {
            PhysicalSide.Top => fragment.Baseline,
            PhysicalSide.Bottom => size.Height - fragment.Baseline,
            PhysicalSide.Left => fragment.Baseline,
            _ => size.Width - fragment.Baseline,
        };
    }

    /// <summary>
    /// Creates a run of glyphs along a horizontal line: from <paramref name="origin"/> (the line-left end of the baseline of the
    /// fragment) to the right when <paramref name="forward"/>, else from its right end (<paramref name="inlineSize"/>) to the
    /// left. The invisible glyphs are skipped. Returns null when no glyph is visible.
    /// </summary>
    private static GlyphRun? CreateRun(DisplayFont font, ReadOnlySpan<ShapedGlyph> glyphs, double[] pens, int start, int end, double inlineSize, bool forward, PhysicalOffset origin, string text)
    {
        var ids = new List<ushort>(end - start);
        var positions = new List<PhysicalOffset>(end - start);
        for (var i = start; i < end; i++)
        {
            var glyph = glyphs[i];
            if (glyph.IsInvisible)
                continue;

            var x = forward ? pens[i] : inlineSize - pens[i] - glyph.Advance;
            ids.Add(glyph.GlyphId);
            positions.Add(new PhysicalOffset(origin.X + x + glyph.OffsetX, origin.Y + glyph.OffsetY));
        }

        if (!forward)
        {
            OrderFromLineLeft(ids, positions);
        }

        return ids.Count == 0 ? null : new GlyphRun(font, [.. ids], [.. positions], text);
    }

    /// <summary>
    /// Reverses the glyphs of a run whose text goes from the line-right to the line-left end of its line: the glyphs of a run
    /// are painted from the line-left end, as the text fragments of a line are. The antialiased edges of two glyphs that
    /// overlap (the joined letters of Arabic) are composited one after the other, and the rounding of the result depends on
    /// the order: a text paints the same pixels whether it is one run or several ones (the same text in several inline
    /// boxes).
    /// </summary>
    private static void OrderFromLineLeft(List<ushort> ids, List<PhysicalOffset> positions)
    {
        ids.Reverse();
        positions.Reverse();
    }

    /// <summary>
    /// Creates a run of upright glyphs of vertical text: each glyph is centered horizontally on <paramref name="center"/> (the
    /// central baseline), and its em box (centered on the central baseline of its font) is centered vertically in its vertical
    /// advance in the font, downwards when <paramref name="forward"/>.
    /// </summary>
    private static GlyphRun? CreateUprightRun(ShapedFontRun run, DisplayFont font, ReadOnlySpan<ShapedGlyph> glyphs, double[] pens, int start, int end, PhysicalSize size, double center, bool forward, PhysicalOffset offset, string text)
    {
        var central = InlineLayoutAlgorithm.GetCentralBaseline(run.Font, run.Font.GetMetricsForEmSize(run.EmSize));
        var ids = new List<ushort>(end - start);
        var positions = new List<PhysicalOffset>(end - start);
        for (var i = start; i < end; i++)
        {
            var glyph = glyphs[i];
            if (glyph.IsInvisible)
                continue;

            var top = forward ? pens[i] : size.Height - pens[i] - glyph.Advance;
            // The vertical origin is centered in the vertical advance of the glyph in the font: the adjustments of the advance
            // (spacing, trimming, GPOS) do not move the glyph
            var horizontalAdvance = run.Font.GetAdvance(glyph.GlyphId, run.EmSize, vertical: false);
            var verticalAdvance = run.Font.GetAdvance(glyph.GlyphId, run.EmSize, vertical: true);
            var x = center - (horizontalAdvance / 2) + glyph.OffsetY;
            var y = top + (verticalAdvance / 2) + central + glyph.OffsetX;
            ids.Add(glyph.GlyphId);
            positions.Add(new PhysicalOffset(offset.X + x, offset.Y + y));
        }

        if (!forward)
        {
            OrderFromLineLeft(ids, positions);
        }

        return ids.Count == 0 ? null : new GlyphRun(font, [.. ids], [.. positions], text);
    }

    /// <summary>Returns the characters of the text node rendered by glyphs of a fragment (for the dumps and the paint timing), or an empty string for a generated text without node.</summary>
    private static string GetText(TextFragment fragment, ShapedText shaped, int glyphStart, int glyphEnd)
    {
        var allGlyphs = shaped.Glyphs;
        var data = fragment.Run.Data;
        var shapedStart = allGlyphs[fragment.GlyphStart].Cluster;
        var startCluster = allGlyphs[glyphStart].Cluster;
        var endCluster = glyphEnd < allGlyphs.Length ? allGlyphs[glyphEnd].Cluster : shaped.Length;
        if (shaped.IsRightToLeft && endCluster < startCluster)
        {
            (startCluster, endCluster) = (endCluster, startCluster);
        }

        var start = ToDomOffset(startCluster - shapedStart);
        var end = glyphEnd == fragment.GlyphStart + fragment.GlyphCount ? fragment.End : ToDomOffset(endCluster - shapedStart);
        start = Math.Clamp(start, 0, data.Length);
        end = Math.Clamp(end, start, data.Length);
        return data[start..end];

        int ToDomOffset(int index)
        {
            if (fragment.DomOffsets is { } map)
                return index < map.Length ? map[Math.Max(0, index)] : fragment.End;

            return fragment.Start + index;
        }
    }
}
