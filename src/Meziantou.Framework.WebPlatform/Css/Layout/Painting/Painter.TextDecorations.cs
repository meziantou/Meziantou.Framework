using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The decorations of the texts (https://drafts.csswg.org/css-text-decor-4/): the underlines, overlines and line-throughs
// propagated from the decorating boxes, the emphasis marks, the shadows (also used by box-shadow) and the highlights (the
// selection and the custom highlights, https://drafts.csswg.org/css-highlight-api-1/).
internal sealed partial class Painter
{
    private Dictionary<LayoutBox, List<AppliedDecoration>?>? _appliedDecorations;
    private Dictionary<LineBoxFragment, (double Start, double End)>? _lineContentExtents;

    /// <summary>The lines of <c>text-decoration-line</c>.</summary>
    [Flags]
    private enum TextDecorationLines : byte
    {
        None = 0,
        Underline = 1,
        Overline = 2,
        LineThrough = 4,
    }

    /// <summary>A decoration applied to the texts of a decorating box and of its in-flow descendants (https://drafts.csswg.org/css-text-decor-3/#line-decoration).</summary>
    /// <param name="Lines">The lines.</param>
    /// <param name="Style">The value of <c>text-decoration-style</c>.</param>
    /// <param name="Color">The color of the lines.</param>
    /// <param name="DecoratingStyle">The style of the decorating box (or of the highlight pseudo-element).</param>
    /// <param name="DecoratingBox">The decorating box, or null for the decorations of a highlight (drawn at the baseline of the text).</param>
    private sealed record AppliedDecoration(TextDecorationLines Lines, string Style, DisplayColor Color, ComputedStyle DecoratingStyle, ContainerBox? DecoratingBox);

    /// <summary>A shadow of <c>text-shadow</c> or <c>box-shadow</c> (https://drafts.csswg.org/css-backgrounds/#box-shadow), in zoomed px.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct Shadow(DisplayColor Color, double X, double Y, double Blur, double Spread, bool Inset);

    /// <summary>
    /// A highlighted range of a text fragment: its rectangle snapped to the device pixels, its background, the color of its text
    /// when the highlight paints the glyphs (null when the glyphs are the ones of the text below), its decorations and its
    /// rectangle before the snapping (the positions of the layout, compared with the positions of the glyphs).
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct TextHighlight(PhysicalRect Rect, DisplayColor Background, DisplayColor? Color, List<AppliedDecoration>? Decorations, PhysicalRect Bounds)
    {
        /// <summary>The shadows of the text of the highlight (<c>text-shadow</c> of the highlight pseudo-element), or null.</summary>
        public List<Shadow>? Shadows { get; init; }

        /// <summary>Whether the color of the text of the highlight is not the color of the text below: the decorations of the text are painted in it.</summary>
        public bool Recolors { get; init; }

        /// <summary>The stroke of the glyphs painted by the highlight.</summary>
        public TextStroke Stroke { get; init; }
    }

    /// <summary>The stroke of the glyphs of a text (<c>-webkit-text-stroke-width</c> and <c>-webkit-text-stroke-color</c>); the default value is no stroke.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct TextStroke(double Width, DisplayColor Color)
    {
        /// <summary>Returns the stroke of the text of a style.</summary>
        public static TextStroke From(ComputedStyle style)
        {
            var width = BoxModelStyle.ParseFixedLength(style.GetComputedValue("-webkit-text-stroke-width"), style.Layout.EffectiveZoom);
            if (width <= 0)
                return default;

            var color = GetColor(style, "-webkit-text-stroke-color");
            return color.IsTransparent ? default : new TextStroke(width, color);
        }

        /// <summary>Returns the paint of the stroke, or null when there is no stroke.</summary>
        public DisplayPaint? ToPaint() => Width <= 0 ? null : DisplayPaint.ForStroke(Color, new StrokeStyle(Width, Join: LineJoin.Miter));
    }

    /// <summary>
    /// The line of a text fragment for its decorations: the position of its alphabetic baseline along the block axis (y for
    /// horizontal text, x for vertical text), the extent of the fragment along the inline axis, the direction of the line-under
    /// side (+1 when it is towards the increasing coordinates) and the metrics of its first available font.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct TextLine(bool Horizontal, double Baseline, double InlineStart, double InlineEnd, double UnderSign, UsedFontMetrics Metrics, double FontSize)
    {
        /// <summary>Returns the rectangle between two distances from the baseline (positive towards the line-under side) along the inline extent of the line.</summary>
        public PhysicalRect GetBand(double from, double to)
        {
            var a = Baseline + (UnderSign * from);
            var b = Baseline + (UnderSign * to);
            var low = Math.Min(a, b);
            var size = Math.Abs(b - a);
            return Horizontal ? new PhysicalRect(InlineStart, low, InlineEnd - InlineStart, size) : new PhysicalRect(low, InlineStart, size, InlineEnd - InlineStart);
        }
    }

    private TextLine GetTextLine(TextFragment fragment, PhysicalOffset offset)
    {
        var style = fragment.Style;
        var layout = style.Layout;
        var metrics = style.Fonts.PrimaryFont.GetMetrics(layout.FontSize);
        var writingDirection = fragment.WritingDirection;
        var baseline = GetPhysicalBaseline(fragment);
        if (writingDirection.IsHorizontal)
            return new TextLine(Horizontal: true, SnapToDevicePixels(offset.Y + baseline), offset.X, offset.X + fragment.Size.Width, writingDirection.LineOver == PhysicalSide.Top ? 1 : -1, metrics, layout.FontSize);

        // The decorations of vertical text are relative to the alphabetic baseline of the sideways glyphs
        var clockwise = writingDirection.LineOver == PhysicalSide.Right;
        if (layout.HasCentralDominantBaseline)
        {
            baseline += (clockwise ? -1 : 1) * InlineLayoutAlgorithm.GetCentralBaseline(style.Fonts.PrimaryFont, metrics);
        }

        return new TextLine(Horizontal: false, SnapToDevicePixels(offset.X + baseline), offset.Y, offset.Y + fragment.Size.Height, clockwise ? -1 : 1, metrics, layout.FontSize);
    }

    /// <summary>
    /// Returns the decorations applied to the text of a fragment (https://drafts.csswg.org/css-text-decor-3/#line-decoration): the
    /// decorations of its ancestor boxes, which propagate to their in-flow descendants except into the atomic inlines, the
    /// floats and the out-of-flow boxes.
    /// </summary>
    private List<AppliedDecoration>? GetAppliedDecorations(TextFragment fragment)
    {
        if (fragment.Run.Parent is not { } parent)
            return null;

        _appliedDecorations ??= [];
        if (_appliedDecorations.TryGetValue(parent, out var cached))
            return cached;

        List<AppliedDecoration>? result = null;
        var quirks = _document.Mode == DocumentMode.Quirks;
        for (var box = parent; box is not null && box.Kind != ContainerBoxKind.Viewport; box = box.SplitInlineParent ?? box.Parent)
        {
            // https://quirks.spec.whatwg.org/#the-text-decoration-does-not-propagate-into-tables-quirk (not specified, as in the
            // browsers): the decorations of the ancestors of a table element do not propagate into it
            if (quirks && box.Element is HtmlTableElement && box.PseudoElement is null)
                break;

            var style = box.Style;
            var lines = ParseDecorationLines(style);
            {
                if (lines != TextDecorationLines.None)
                {
                    (result ??= []).Add(new AppliedDecoration(lines, style.GetComputedValue("text-decoration-style"), GetColor(style, "text-decoration-color"), style, box));
                }
            }

            if (box.IsAtomicInline || box.IsOutOfFlow || box.Style.Layout.IsFloating || box.IsBlockLevel && box.Kind == ContainerBoxKind.Replaced)
                break;

            // https://drafts.csswg.org/css-text-decor-3/#line-decoration: "When specified on or propagated to a ruby container, text
            // decorations are propagated only to the ruby bases": the annotations get the decorations of their annotation container
            if (box.Display.Internal == DisplayInternal.RubyTextContainer || (box.Display.Internal == DisplayInternal.RubyText && box.Parent?.Display.Internal != DisplayInternal.RubyTextContainer))
                break;
        }

        // The outermost decorations are painted first
        result?.Reverse();
        _appliedDecorations[parent] = result;
        return result;
    }

    /// <summary>Returns the lines of <c>text-decoration-line</c>.</summary>
    private static TextDecorationLines ParseDecorationLines(ComputedStyle style)
    {
        var value = style.GetComputedValue("text-decoration-line");
        if (value is "none" or "")
            return TextDecorationLines.None;

        var lines = TextDecorationLines.None;
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            lines |= keyword switch
            {
                "underline" => TextDecorationLines.Underline,
                "overline" => TextDecorationLines.Overline,
                "line-through" => TextDecorationLines.LineThrough,
                _ => TextDecorationLines.None,
            };
        }

        return lines;
    }

    /// <summary>
    /// Paints the decoration lines of a text fragment (https://drafts.csswg.org/css-text-decor-4/#text-decoration-line-property):
    /// the underline below the alphabetic baseline (<c>text-underline-position</c>, <c>text-underline-offset</c>), the overline at
    /// the ascent, the line-through at the strikeout position of the font, with the thickness of <c>text-decoration-thickness</c>
    /// and the style of <c>text-decoration-style</c>. The lines span the fragment, or <paramref name="range"/> (physical
    /// coordinates of the inline axis): the characters of a highlight, decorated as an inline box would be. They skip the
    /// spaces of <c>text-decoration-skip-spaces</c>.
    /// </summary>
    private void PaintDecorations(TextFragment fragment, in TextLine textLine, List<AppliedDecoration>? decorations, TextDecorationLines lines, DisplayPaint? paint, Node? source, (double Start, double End)? range = null)
    {
        if (decorations is null || textLine.InlineEnd <= textLine.InlineStart)
            return;

        var (decoratedStart, decoratedEnd, skippedSpaces) = GetSkippedSpaces(fragment, textLine);
        foreach (var decoration in decorations)
        {
            var style = decoration.DecoratingStyle;
            var applied = decoration.Lines & lines;
            if (applied == TextDecorationLines.None)
                continue;

            var line = GetDecoratingLine(fragment, textLine, decoration.DecoratingBox);
            if (range is var (rangeStart, rangeEnd))
            {
                if (rangeEnd <= rangeStart)
                    continue;

                line = line with { InlineStart = rangeStart, InlineEnd = rangeEnd };
            }

            var metrics = line.Metrics;
            var fontUnits = metrics.FontUnitMetrics;

            var color = paint?.Color ?? decoration.Color;
            if (color.IsTransparent)
                continue;

            var decorationPaint = paint ?? new DisplayPaint(color);
            var thickness = GetDecorationThickness(decoration.DecoratingStyle, metrics, line.FontSize, fragment.Style.UsedFontScale);

            // text-decoration-inset moves the ends of the lines of the decorating box, from the ends of the text that it
            // decorates (without the skipped spaces at the start and at the end of the line)
            var inset = range is null ? ApplyDecorationInset(fragment, textLine, decoration, thickness, ref line) : null;
            if (inset == false)
                continue;

            if (inset is null && (decoratedStart > line.InlineStart || decoratedEnd < line.InlineEnd))
            {
                line = line with { InlineStart = Math.Max(line.InlineStart, decoratedStart), InlineEnd = Math.Min(line.InlineEnd, decoratedEnd) };
                if (line.InlineEnd <= line.InlineStart)
                    continue;
            }

            // https://drafts.csswg.org/css-text-decor-4/#text-underline-position-property: in the vertical typographic mode (not
            // for the sideways text), the underline is at the left or at the right edge of the text; when that edge is on the
            // line-over side, "an overline also switches sides and is drawn on the under side". As in the browsers, auto is
            // one of the two edges: the right one for the languages of the default style sheet of the specification (Japanese,
            // Korean and Mongolian), else the left one
            var position = style.GetComputedValue("text-underline-position");
            var under = position.Contains("under", StringComparison.Ordinal);
            var flipped = false;
            if (!line.Horizontal && fragment.Style.Layout.HasCentralDominantBaseline)
            {
                var left = position.Contains("left", StringComparison.Ordinal);
                var right = position.Contains("right", StringComparison.Ordinal);
                if (position is "auto" or "")
                {
                    right = IsRightUnderlineLanguage(decoration.DecoratingBox?.Element ?? fragment.Run.Element);
                    left = !right;
                }

                if (left || right)
                {
                    under = left == (line.UnderSign < 0);
                    flipped = !under;
                }
            }

            if ((applied & TextDecorationLines.Underline) != TextDecorationLines.None)
            {
                // https://drafts.csswg.org/css-text-decor-4/#underline-offset: the offset is added to the zero position of the
                // underline (the alphabetic baseline, the position of the font for from-font, the edge of the text for under,
                // left and right), away from the text
                var offsetValue = style.GetComputedValue("text-underline-offset");
                var hasOffset = offsetValue is not ("auto" or "");
                var length = hasOffset && LengthPercentage.TryParse(offsetValue, style.Layout.EffectiveZoom, nonNegative: false, out var parsed) ? parsed.Resolve(line.FontSize) : 0;
                double top;
                if (flipped)
                {
                    top = -metrics.Ascent - length - thickness;
                }
                else if (under)
                {
                    top = metrics.Descent + length;
                }
                else if (!hasOffset)
                {
                    top = fontUnits.UnderlinePosition != 0 ? -fontUnits.UnderlinePosition * metrics.Scale : Math.Max(1, Math.Ceiling(thickness / 2));
                }
                else
                {
                    top = (position.Contains("from-font", StringComparison.Ordinal) ? -fontUnits.UnderlinePosition * metrics.Scale : 0) + length;
                }

                PaintDecorationLine(line, top, thickness, decoration.Style, decorationPaint, outwards: flipped ? -1 : 1, source, MergeIntervals(GetLineInkSkipIntervals(fragment, textLine, line, top, thickness), skippedSpaces));
            }

            if ((applied & TextDecorationLines.Overline) != TextDecorationLines.None)
            {
                // The overline is outside of the over edge of the text: a thick line grows away from the text
                var top = flipped ? metrics.Descent : -metrics.Ascent - thickness;
                PaintDecorationLine(line, top, thickness, decoration.Style, decorationPaint, outwards: flipped ? 1 : -1, source, MergeIntervals(GetLineInkSkipIntervals(fragment, textLine, line, top, thickness), skippedSpaces));
            }

            if ((applied & TextDecorationLines.LineThrough) != TextDecorationLines.None)
            {
                var center = fontUnits.StrikeoutPosition != 0
                    ? -(fontUnits.StrikeoutPosition * metrics.Scale) + (fontUnits.StrikeoutThickness * metrics.Scale / 2)
                    : -metrics.XHeight / 2;
                PaintDecorationLine(line, center - (thickness / 2), thickness, decoration.Style, decorationPaint, outwards: 1, source, skippedSpaces);
            }
        }
    }

    /// <summary>
    /// Whether the underlines of the vertical text of the language of an element are on the right of the text by default
    /// (https://drafts.csswg.org/css-text-decor-4/#default-stylesheet: Japanese, Korean and Mongolian).
    /// </summary>
    private static bool IsRightUnderlineLanguage(Element? element)
    {
        if (element is null || Selectors.HtmlElementStates.GetLanguage(element) is not { Length: >= 2 } language || (language.Length > 2 && language[2] != '-'))
            return false;

        var primary = language.AsSpan(0, 2);
        return primary.Equals("ja", StringComparison.OrdinalIgnoreCase) || primary.Equals("ko", StringComparison.OrdinalIgnoreCase) || primary.Equals("mn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the line of a decoration of a text fragment (https://drafts.csswg.org/css-text-decor-4/#line-decoration): the
    /// decorations propagated from a decorating box are drawn at the baseline and with the font metrics of that box (the
    /// baseline of the line box for a block container), so the subscripts and superscripts do not move them.
    /// </summary>
    private TextLine GetDecoratingLine(TextFragment fragment, in TextLine textLine, ContainerBox? decoratingBox)
    {
        if (decoratingBox is null || !textLine.Horizontal || ReferenceEquals(fragment.Run.Parent, decoratingBox))
            return textLine;

        // (the boxes of a line scaled by text-fit have the used font sizes of the line: the scaling factor of the text)
        var fontScale = fragment.Style.UsedFontScale;
        var style = decoratingBox.Style;
        var metrics = style.Fonts.PrimaryFont.GetMetrics(style.Layout.FontSize * fontScale);
        for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is LineBoxFragment lineBox)
            {
                if (decoratingBox.IsInlineBox)
                    break;

                // "the underlining for the blockquote element is propagated to an anonymous inline box" of each block container
                // of its descendants: the line is the one of the root inline box of the block container of the line box
                if (lineBox.Parent is BoxFragment { Box: { } container } && !ReferenceEquals(container, decoratingBox))
                {
                    style = container.Style;
                    metrics = style.Fonts.PrimaryFont.GetMetrics(style.Layout.FontSize * fontScale);
                }

                var lineOffset = lineBox.GetOffsetInRoot() - _group.Origin;
                return textLine with { Baseline = SnapToDevicePixels(lineOffset.Y + lineBox.Baseline), Metrics = metrics, FontSize = style.Layout.FontSize * fontScale };
            }

            if (ancestor is BoxFragment box && ReferenceEquals(box.Box, decoratingBox))
            {
                var boxOffset = box.GetOffsetInRoot() - _group.Origin;
                // The content box starts at the ascent, or at the over edge of text-box-edge when text-box-trim trims it
                var over = metrics.Ascent;
                var text = style.Layout.Text;
                if ((text.TextBoxTrim & TextBoxTrim.TrimStart) != TextBoxTrim.None)
                {
                    over = InlineLayoutAlgorithm.GetTextBoxOverEdge(style, text.TextBoxEdge.Over);
                }

                var baseline = boxOffset.Y + box.Border.Top + box.Padding.Top + over;
                return textLine with { Baseline = SnapToDevicePixels(baseline), Metrics = metrics, FontSize = style.Layout.FontSize * fontScale };
            }
        }

        return textLine with { Metrics = metrics, FontSize = style.Layout.FontSize * fontScale };
    }

    /// <summary>
    /// The text that a decorating box decorates on a line (https://drafts.csswg.org/css-text-decor-4/#text-decoration-inset-property):
    /// the extent along the inline axis, from the left (or top) edge of the line box, of the text fragments that the decorations
    /// of the box propagate to (between the spacers that <c>text-decoration-skip-spaces</c> skips at the start and at the end of
    /// the line). It is a fragment of the decorating box for the insets of its decoration lines: a fragment of the inline box,
    /// or of the anonymous inline box that the decorations of a block container are propagated to in each block container of its
    /// descendants (https://drafts.csswg.org/css-text-decor-4/#line-decoration).
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private record struct DecoratedSegment(LineBoxFragment Line, WritingMode WritingMode, double Low, double High, double SizeBefore);

    /// <summary>The segments of a decorating box in the lines of a block container, in the order of the lines, and the sum of their inline sizes.</summary>
    private sealed record DecoratedRun(List<DecoratedSegment> Segments)
    {
        public double TotalSize { get; set; }
    }

    private Dictionary<(ContainerBox DecoratingBox, ContainerBox? BlockContainer), DecoratedRun?>? _decoratedRuns;

    /// <summary>
    /// Applies <c>text-decoration-inset</c> to the part of a decoration line of a decorating box that a text fragment draws
    /// (https://drafts.csswg.org/css-text-decor-4/#text-decoration-inset-property): the insets move the endpoints of the line
    /// inward (positive) or outward (negative) from the start and from the end of the text that the box decorates. With
    /// <c>box-decoration-break: slice</c>, the decorated text of the lines is a single run: the percentages refer to its size, the
    /// start inset applies from the start of the first line and the end inset from the end of the last line (an inset longer
    /// than a line continues on the next one); with <c>clone</c>, the insets apply to each line, and the percentages refer to the
    /// size of the decorated text of the line. The text fragment at an end of the decorated text of a line draws the part of
    /// the line that a negative inset adds. Returns null when the decoration has no inset on the line of the fragment, and
    /// false when nothing of the line is left along the text fragment.
    /// </summary>
    private bool? ApplyDecorationInset(TextFragment fragment, in TextLine textLine, AppliedDecoration decoration, double thickness, ref TextLine line)
    {
        if (decoration.DecoratingBox is not { } decoratingBox)
            return null;

        var style = decoration.DecoratingStyle;
        var value = style.GetComputedValue("text-decoration-inset");
        if (value is "0px" or "0" or "")
            return null;

        // The line box of the fragment, and the position of the fragment in it
        var horizontal = textLine.Horizontal;
        var position = 0d;
        LineBoxFragment? lineBox = null;
        for (Fragment current = fragment; current.Parent is { } parent; current = parent)
        {
            position += horizontal ? current.Offset.X : current.Offset.Y;
            if (parent is LineBoxFragment parentLine)
            {
                lineBox = parentLine;
                break;
            }
        }

        if (lineBox is null)
            return null;

        if (GetDecoratedRun(decoratingBox, lineBox) is not { } run)
            return null;

        var index = -1;
        for (var i = 0; i < run.Segments.Count; i++)
        {
            if (ReferenceEquals(run.Segments[i].Line, lineBox))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return null;

        var segment = run.Segments[index];
        var size = segment.High - segment.Low;
        var clones = style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
        double start;
        double end;
        if (value == "auto")
        {
            // "The UA chooses an inset amount that ensures that if two identical underlined elements appear side-by-side they do
            // not appear to have a single underline": half the thickness of the line on each side, one pixel at least
            start = end = GetAutoDecorationInset(thickness, style.Layout.EffectiveZoom);
        }
        else
        {
            var insets = ParseDecorationInset(value, style.Layout.EffectiveZoom);
            if (insets is not var (startInset, endInset))
                return null;

            var basis = clones ? size : run.TotalSize;
            start = startInset.Resolve(basis);
            end = endInset.Resolve(basis);
        }

        if (!clones)
        {
            // slice: an inset is consumed by the decorated text of the lines before (after) this one, and a negative inset only
            // extends the first (last) line
            var sizeAfter = run.TotalSize - segment.SizeBefore - size;
            start = start > 0 ? Math.Max(0, start - segment.SizeBefore) : index == 0 ? start : 0;
            end = end > 0 ? Math.Max(0, end - sizeAfter) : index == run.Segments.Count - 1 ? end : 0;
        }

        if (start == 0 && end == 0)
            return null;

        // The start of the decorating box is on the left (or top) side of the line, or on the other side
        var startIsLow = new WritingDirection(fragment.Run.Style.Layout.WritingMode, style.Layout.Direction).InlineStart is PhysicalSide.Left or PhysicalSide.Top;
        var origin = textLine.InlineStart - position;
        var low = origin + segment.Low + (startIsLow ? start : end);
        var high = origin + segment.High - (startIsLow ? end : start);
        if (high <= low)
            return false;

        // The fragment draws the line along itself; the fragments at the ends of the decorated text also draw what is beyond them
        const double Tolerance = 0.01;
        var fragmentSize = horizontal ? fragment.Size.Width : fragment.Size.Height;
        var isFirst = position <= segment.Low + Tolerance;
        var isLast = position + fragmentSize >= segment.High - Tolerance;
        var from = isFirst ? low : Math.Max(line.InlineStart, low);
        var to = isLast ? high : Math.Min(line.InlineEnd, high);
        if (to <= from)
            return false;

        line = line with { InlineStart = from, InlineEnd = to };
        return true;
    }

    /// <summary>
    /// Returns the inset of <c>text-decoration-inset: auto</c> for a line of a thickness: half the thickness, one (zoomed) pixel
    /// at least (the specification leaves the amount to the user agent).
    /// </summary>
    internal static double GetAutoDecorationInset(double thickness, double zoom) => Math.Max(zoom, thickness / 2);

    /// <summary>Parses a computed value of <c>text-decoration-inset</c> that is not <c>auto</c>: the start inset and the end inset (the same when a single value is given).</summary>
    private static (LengthPercentage Start, LengthPercentage End)? ParseDecorationInset(string value, double zoom)
    {
        if (LengthPercentage.TryParse(value, zoom, nonNegative: false, out var single))
            return (single, single);

        LengthPercentage? first = null;
        LengthPercentage? second = null;
        foreach (var component in CssParser.ParseListOfComponentValues(value))
        {
            if (component is CssPreservedToken { Type: CssTokenType.Whitespace })
                continue;

            if (second is not null || !LengthPercentage.TryParse(component, zoom, nonNegative: false, out var parsed))
                return null;

            if (first is null)
            {
                first = parsed;
            }
            else
            {
                second = parsed;
            }
        }

        return first is { } startInset && second is { } endInset ? (startInset, endInset) : null;
    }

    /// <summary>
    /// Returns the text that a decorating box decorates on each line of the block container of a line (see <see cref="DecoratedSegment"/>),
    /// or null when it decorates no text there.
    /// </summary>
    private DecoratedRun? GetDecoratedRun(ContainerBox decoratingBox, LineBoxFragment line)
    {
        var blockContainer = (line.Parent as BoxFragment)?.Box;
        _decoratedRuns ??= [];
        if (_decoratedRuns.TryGetValue((decoratingBox, blockContainer), out var run))
            return run;

        // The fragments of the box (of all the parts of an inline box split around blocks), in the order of the fragment tree
        var first = decoratingBox;
        while (first.PreviousContinuation is { } previous)
        {
            first = previous;
        }

        var segments = new List<DecoratedSegment>();
        var skipSpaces = decoratingBox.Style.GetComputedValue("text-decoration-skip-spaces");
        for (var part = first; part is not null; part = part.Continuation)
        {
            for (var fragment = _engine.GetLaidOutFirstFragment(part); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                // A fragment of an inline box is in a line box
                LineBoxFragment? lineBox = null;
                var x = 0d;
                var y = 0d;
                for (Fragment current = fragment; part.IsInlineBox && current.Parent is { } parent; current = parent)
                {
                    x += current.Offset.X;
                    y += current.Offset.Y;
                    if (parent is LineBoxFragment parentLine)
                    {
                        lineBox = parentLine;
                        break;
                    }
                }

                Visit(fragment, lineBox, lineBox is null ? 0 : x, lineBox is null ? 0 : y, isRoot: true);
            }
        }

        // The lines of each block container are the fragments of an inline box
        _decoratedRuns[(decoratingBox, blockContainer)] = null;
        foreach (var item in segments)
        {
            var segment = item;
            var container = (segment.Line.Parent as BoxFragment)?.Box;
            if (!_decoratedRuns.TryGetValue((decoratingBox, container), out var containerRun) || containerRun is null)
            {
                containerRun = new DecoratedRun([]);
                _decoratedRuns[(decoratingBox, container)] = containerRun;
            }

            ClampToLineContent(ref segment);
            segment.SizeBefore = containerRun.TotalSize;
            containerRun.TotalSize += Math.Max(0, segment.High - segment.Low);
            containerRun.Segments.Add(segment);
        }

        return _decoratedRuns[(decoratingBox, blockContainer)];

        void Visit(Fragment parent, LineBoxFragment? lineBox, double x, double y, bool isRoot)
        {
            if (parent is TextFragment text)
            {
                if (lineBox is null || text.Style.Layout.Visibility == Visibility.Collapse)
                    return;

                var horizontal = text.WritingDirection.IsHorizontal;
                var low = horizontal ? x : y;
                var high = low + (horizontal ? text.Size.Width : text.Size.Height);
                if (segments.Count > 0 && ReferenceEquals(segments[^1].Line, lineBox))
                {
                    var last = segments[^1];
                    segments[^1] = last with { Low = Math.Min(last.Low, low), High = Math.Max(last.High, high) };
                }
                else
                {
                    segments.Add(new DecoratedSegment(lineBox, text.WritingDirection.WritingMode, low, high, 0));
                }

                return;
            }

            // The decorations do not propagate to the atomic inlines, to the floats and to the out-of-flow boxes
            if (!isRoot && parent is BoxFragment { Box: { } box } && (box.IsAtomicInline || box.IsOutOfFlow || box.Style.Layout.IsFloating || (box.IsBlockLevel && box.Kind == ContainerBoxKind.Replaced)))
                return;

            foreach (var child in parent.ChildrenSpan)
            {
                if (child is LineBoxFragment childLine)
                {
                    Visit(child, childLine, 0, 0, isRoot: false);
                }
                else if (lineBox is null)
                {
                    Visit(child, null, 0, 0, isRoot: false);
                }
                else
                {
                    Visit(child, lineBox, x + child.Offset.X, y + child.Offset.Y, isRoot: false);
                }
            }
        }

        // The spacers at the start and at the end of a line are not decorated (text-decoration-skip-spaces): the insets are
        // measured from the ends of the lines that are drawn
        void ClampToLineContent(ref DecoratedSegment segment)
        {
            if (skipSpaces is "none")
                return;

            var all = skipSpaces == "all";
            var skipsStart = all || skipSpaces.Contains("start", StringComparison.Ordinal);
            var skipsEnd = all || skipSpaces.Contains("end", StringComparison.Ordinal);
            var line = segment.Line;
            var horizontal = segment.WritingMode == WritingMode.HorizontalTb;
            _lineContentExtents ??= [];
            if (!_lineContentExtents.TryGetValue(line, out var extent))
            {
                extent = GetLineContentExtent(line, horizontal);
                _lineContentExtents.Add(line, extent);
            }

            if (extent.End < extent.Start)
                return;

            var startIsLow = new WritingDirection(segment.WritingMode, line.IsRightToLeft ? Direction.Rtl : Direction.Ltr).InlineStart is PhysicalSide.Left or PhysicalSide.Top;
            if (startIsLow ? skipsStart : skipsEnd)
            {
                segment.Low = Math.Max(segment.Low, extent.Start);
            }

            if (startIsLow ? skipsEnd : skipsStart)
            {
                segment.High = Math.Min(segment.High, extent.End);
            }
        }
    }

    /// <summary>
    /// Returns the thickness of the decorations of a decorating box (https://drafts.csswg.org/css-text-decor-4/#text-decoration-width-property):
    /// the thickness of the underline of the font for auto and from-font (a tenth of the font size when the font does not define
    /// it), floored to the device pixels, at least one device pixel.
    /// </summary>
    private double GetDecorationThickness(ComputedStyle decoratingStyle, UsedFontMetrics metrics, double fontSize, double fontScale)
    {
        var value = decoratingStyle.GetComputedValue("text-decoration-thickness");
        double thickness;
        if (value is "auto" or "from-font" or "")
        {
            thickness = metrics.FontUnitMetrics.UnderlineThickness > 0 ? metrics.FontUnitMetrics.UnderlineThickness * metrics.Scale : fontSize / 10;
        }
        else
        {
            thickness = LengthPercentage.TryParse(value, decoratingStyle.Layout.EffectiveZoom, nonNegative: true, out var parsed) ? parsed.Resolve(decoratingStyle.Layout.FontSize * fontScale) : fontSize / 10;
        }

        // As in the browsers, the thickness is floored to the device pixels
        var pixel = 1 / _devicePixelRatio;
        return Math.Max(pixel, Math.Floor((thickness * _devicePixelRatio) + 1e-6) / _devicePixelRatio);
    }

    /// <summary>
    /// Paints a decoration line from a distance <paramref name="top"/> of the baseline (positive towards the line-under side) with
    /// a thickness, in a style (https://drafts.csswg.org/css-text-decor-3/#text-decoration-style-property): the second line of
    /// <c>double</c> is further from the text (<paramref name="outwards"/> is the direction away from the text). The line is
    /// interrupted in the intervals of the inline axis of <paramref name="skipped"/> (sorted and disjoint).
    /// </summary>
    private void PaintDecorationLine(in TextLine line, double top, double thickness, string style, DisplayPaint paint, int outwards, Node? source, List<(double Start, double End)>? skipped = null)
    {
        var saveCount = _builder.SaveCount;
        if (skipped is { Count: > 0 })
        {
            // The parts of the line that cross the glyphs (text-decoration-skip-ink) and the skipped spaces
            // (text-decoration-skip-spaces: all) are clipped out: holes in a rectangle that contains the line and what its style
            // draws around it (the wave, the second line, the caps of the dots)
            var clip = new DisplayPath { FillRule = FillRule.EvenOdd };
            var band = line.GetBand(top - (3 * thickness) - 1, top + (4 * thickness) + 1);
            var extent = line.Horizontal
                ? new PhysicalRect(band.X - thickness, band.Y, band.Width + (2 * thickness), band.Height)
                : new PhysicalRect(band.X, band.Y - thickness, band.Width, band.Height + (2 * thickness));
            clip.AddRect(extent);
            var (low, high) = line.Horizontal ? (extent.X, extent.Right) : (extent.Y, extent.Bottom);
            foreach (var (start, end) in skipped)
            {
                // The ends of the gaps are on the device pixels (they are not antialiased: lines painted over each other have the
                // same gaps)
                var from = Math.Max(SnapToDevicePixels(start), low);
                var to = Math.Min(SnapToDevicePixels(end), high);
                if (to > from)
                {
                    clip.AddRect(line.Horizontal ? new PhysicalRect(from, extent.Y, to - from, extent.Height) : new PhysicalRect(extent.X, from, extent.Width, to - from));
                }
            }

            _builder.Save();
            _builder.Add(new ClipPathItem(clip));
        }

        PaintDecorationLineCore(line, top, thickness, style, paint, outwards, source);
        _builder.RestoreTo(saveCount);
    }

    /// <summary>
    /// The largest distance, in CSS px, by which the ink of a glyph is extended on each side for <c>text-decoration-skip-ink</c>:
    /// the gap around the glyphs does not grow with the thick lines (the value of Chrome).
    /// </summary>
    private const double MaxInkSkipDilation = 13;

    /// <summary>
    /// Returns the intervals of the inline axis (physical coordinates, sorted and disjoint) where an underline or an overline
    /// crosses the glyphs of a text fragment (https://drafts.csswg.org/css-text-decor-4/#text-decoration-skip-ink-property,
    /// <c>auto</c>): the ink of the glyphs (their bounding boxes) that intersect the line, extended by the thickness of the line.
    /// <paramref name="textLine"/> is the line of the fragment (the baseline of its glyphs), <paramref name="line"/> the line of
    /// the decoration. The upright glyphs of vertical text are not skipped (as in the browsers). Returns null when the line is not
    /// interrupted.
    /// </summary>
    private static List<(double Start, double End)>? GetInkSkipIntervals(TextFragment fragment, in TextLine textLine, in TextLine line, double top, double thickness)
    {
        // The property is inherited: the value of the element of the text applies, not the one of the decorating box
        if (fragment.ShapedText is not { IsCombinedUpright: false } shaped || fragment.GlyphCount == 0 || fragment.Style.GetComputedValue("text-decoration-skip-ink") == "none")
            return null;

        // The glyphs are laid out along the x axis of a frame whose y axis points to the line-under side: the physical frame for
        // the horizontal text, a frame rotated as the sideways glyphs for the vertical text (see BuildGlyphRuns), where the x
        // axis points upwards when the glyphs are rotated counter-clockwise (sideways-lr)
        var glyphs = fragment.Glyphs;
        var writingDirection = fragment.WritingDirection;
        var clockwise = writingDirection.LineOver == PhysicalSide.Right;
        var forward = textLine.Horizontal ? writingDirection.InlineStart == PhysicalSide.Left : writingDirection.InlineStart == (clockwise ? PhysicalSide.Top : PhysicalSide.Bottom);
        var upwards = !textLine.Horizontal && !clockwise;
        var inlineSize = textLine.InlineEnd - textLine.InlineStart;

        // The band of the line, from the baseline of the glyphs
        var bandTop = ((line.Baseline - textLine.Baseline) * line.UnderSign) + top;
        var bandBottom = bandTop + thickness;
        var dilation = Math.Min(thickness, MaxInkSkipDilation);
        List<(double Start, double End)>? result = null;
        var pen = 0d;
        foreach (var run in shaped.Runs)
        {
            var start = Math.Max(run.GlyphStart, fragment.GlyphStart);
            var end = Math.Min(run.GlyphStart + run.GlyphCount, fragment.GlyphStart + fragment.GlyphCount);
            var scale = run.EmSize / run.Font.Font.UnitsPerEm;
            for (var i = start; i < end; i++)
            {
                var glyph = glyphs[i - fragment.GlyphStart];
                var origin = forward ? pen : inlineSize - pen - glyph.Advance;
                pen += glyph.Advance;
                if (run.IsUpright || glyph.IsInvisible || !run.Font.Font.TryGetGlyphBounds(glyph.GlyphId, out var bounds) || bounds.XMax <= bounds.XMin)
                    continue;

                var inkTop = glyph.OffsetY - (bounds.YMax * scale);
                var inkBottom = glyph.OffsetY - (bounds.YMin * scale);
                if (inkBottom <= bandTop || inkTop >= bandBottom)
                    continue;

                var low = origin + glyph.OffsetX + (bounds.XMin * scale) - dilation;
                var high = origin + glyph.OffsetX + (bounds.XMax * scale) + dilation;
                (result ??= []).Add(upwards ? (textLine.InlineEnd - high, textLine.InlineEnd - low) : (textLine.InlineStart + low, textLine.InlineStart + high));
            }
        }

        // The intervals of the neighboring glyphs overlap
        return MergeIntervals(result, null);
    }

    /// <summary>
    /// Returns the intervals where an underline or an overline drawn by a text fragment crosses glyphs (see <see cref="GetInkSkipIntervals"/>):
    /// its own glyphs, and the ones of the text fragments before and after it on the line, whose ink (and the gap around it)
    /// may extend along the fragment, or which the line extends along (<c>text-decoration-inset</c>).
    /// </summary>
    private List<(double Start, double End)>? GetLineInkSkipIntervals(TextFragment fragment, in TextLine textLine, in TextLine line, double top, double thickness)
    {
        var result = GetInkSkipIntervals(fragment, textLine, line, top, thickness);
        for (var side = 0; side < 2; side++)
        {
            if (GetAdjacentText(fragment, next: side == 1) is not { ShapedText: not null, GlyphCount: > 0 } adjacent || adjacent.WritingDirection.IsHorizontal != textLine.Horizontal)
                continue;

            var adjacentLine = GetTextLine(adjacent, adjacent.GetOffsetInRoot() - _group.Origin);
            if (GetInkSkipIntervals(adjacent, adjacentLine, line, top, thickness) is not { } intervals)
                continue;

            // (only the gaps that reach the line of the fragment)
            var (lineStart, lineEnd) = (line.InlineStart, line.InlineEnd);
            intervals.RemoveAll(interval => interval.End <= lineStart || interval.Start >= lineEnd);
            result = MergeIntervals(result, intervals);
        }

        return result;

        // The text fragment before or after a fragment in its line box (in the order of the fragment tree), or null when
        // another content is there
        static TextFragment? GetAdjacentText(Fragment fragment, bool next)
        {
            for (Fragment current = fragment; current.Parent is { } parent && current is not LineBoxFragment; current = parent)
            {
                var siblings = parent.ChildrenSpan;
                var index = siblings.IndexOf(current);
                index += next ? 1 : -1;
                if (index < 0 || index >= siblings.Length)
                    continue;

                // The first (or last) leaf of the sibling
                var leaf = siblings[index];
                while (leaf is BoxFragment { Box.IsInlineBox: true } box && box.ChildrenSpan is { Length: > 0 } children)
                {
                    leaf = next ? children[0] : children[^1];
                }

                return leaf as TextFragment;
            }

            return null;
        }
    }

    /// <summary>Returns the union of the intervals of two lists (either can be null), sorted and disjoint; the first list is modified.</summary>
    private static List<(double Start, double End)>? MergeIntervals(List<(double Start, double End)>? first, List<(double Start, double End)>? second)
    {
        if (first is null)
            return second;

        if (second is not null)
        {
            first.AddRange(second);
        }

        if (first.Count <= 1)
            return first;

        first.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var count = 0;
        for (var i = 1; i < first.Count; i++)
        {
            if (first[i].Start <= first[count].End)
            {
                first[count] = (first[count].Start, Math.Max(first[count].End, first[i].End));
            }
            else
            {
                first[++count] = first[i];
            }
        }

        first.RemoveRange(count + 1, first.Count - count - 1);
        return first;
    }

    /// <summary>
    /// Returns what the decorations of a text fragment skip for <c>text-decoration-skip-spaces</c>
    /// (https://drafts.csswg.org/css-text-decor-4/#text-decoration-skip-spaces-property; the property is inherited: the value
    /// of the element of the text applies): the part of the inline axis (physical coordinates) between the spacers at the
    /// start and at the end of the line (<c>start</c>, <c>end</c>; infinite when nothing is skipped on a side), and for
    /// <c>all</c> the intervals (sorted and disjoint) of the other spacers and word separators of the fragment, with the
    /// letter-spacing before them.
    /// </summary>
    private (double Start, double End, List<(double Start, double End)>? Spaces) GetSkippedSpaces(TextFragment fragment, in TextLine textLine)
    {
        var value = fragment.Style.GetComputedValue("text-decoration-skip-spaces");
        if (value is "none" || fragment.IsEllipsis || fragment.GlyphCount == 0)
            return (double.NegativeInfinity, double.PositiveInfinity, null);

        var all = value == "all";
        var skipsStart = all || value.Contains("start", StringComparison.Ordinal);
        var skipsEnd = all || value.Contains("end", StringComparison.Ordinal);
        var low = double.NegativeInfinity;
        var high = double.PositiveInfinity;

        // The line box of the fragment, and the position of the fragment in it
        var horizontal = textLine.Horizontal;
        var position = 0d;
        LineBoxFragment? lineBox = null;
        for (Fragment current = fragment; current.Parent is { } parent; current = parent)
        {
            position += horizontal ? current.Offset.X : current.Offset.Y;
            if (parent is LineBoxFragment line)
            {
                lineBox = line;
                break;
            }
        }

        if (lineBox is not null)
        {
            _lineContentExtents ??= [];
            if (!_lineContentExtents.TryGetValue(lineBox, out var extent))
            {
                extent = GetLineContentExtent(lineBox, horizontal);
                _lineContentExtents.Add(lineBox, extent);
            }

            var origin = textLine.InlineStart - position;
            var startIsLow = new WritingDirection(fragment.Run.Style.Layout.WritingMode, lineBox.IsRightToLeft ? Direction.Rtl : Direction.Ltr).InlineStart is PhysicalSide.Left or PhysicalSide.Top;
            if (startIsLow ? skipsStart : skipsEnd)
            {
                low = origin + extent.Start;
            }

            if (startIsLow ? skipsEnd : skipsStart)
            {
                high = origin + extent.End;
            }
        }

        List<(double Start, double End)>? spaces = null;
        if (all)
        {
            var glyphs = fragment.Glyphs;
            var forward = fragment.WritingDirection.InlineStart is PhysicalSide.Left or PhysicalSide.Top;
            var letterSpacing = Math.Max(0, fragment.ShapedText?.Options.LetterSpacing ?? 0);
            var pen = 0d;
            for (var i = 0; i < glyphs.Length; i++)
            {
                var advance = glyphs[i].Advance;
                var start = pen;
                pen += advance;
                if (advance <= 0 || glyphs[i].IsInvisible || !IsSkippedSpace(fragment, i, wordSeparators: true))
                    continue;

                // (the letter-spacing after the previous character is adjacent to the space)
                if (i > 0 && (glyphs[i - 1].Flags & ShapedGlyphFlags.LetterSpaced) != ShapedGlyphFlags.None)
                {
                    start -= Math.Min(letterSpacing, glyphs[i - 1].Advance);
                }

                (spaces ??= []).Add(forward ? (textLine.InlineStart + start, textLine.InlineStart + pen) : (textLine.InlineEnd - pen, textLine.InlineEnd - start));
            }

            spaces = MergeIntervals(spaces, null);
        }

        return (low, high, spaces);
    }

    /// <summary>
    /// Returns the extent of the content of a line box between the spacers at its start and at its end, along the inline axis
    /// from the left (or top) edge of the line box: the glyphs of its texts that are not spacers (without the letter-spacing
    /// after the last one, which is adjacent to the spacers) and its atomic inlines. The extent is empty (the start is after the
    /// end) when the line has only spacers.
    /// </summary>
    private static (double Start, double End) GetLineContentExtent(LineBoxFragment lineBox, bool horizontal)
    {
        var start = double.PositiveInfinity;
        var end = double.NegativeInfinity;
        Visit(lineBox, 0);
        return (start, end);

        void Visit(Fragment parent, double origin)
        {
            foreach (var child in parent.ChildrenSpan)
            {
                var position = origin + (horizontal ? child.Offset.X : child.Offset.Y);
                var size = horizontal ? child.Size.Width : child.Size.Height;
                if (child is TextFragment text)
                {
                    var glyphs = text.Glyphs;
                    var forward = text.WritingDirection.InlineStart is PhysicalSide.Left or PhysicalSide.Top;
                    var letterSpacing = Math.Max(0, text.ShapedText?.Options.LetterSpacing ?? 0);
                    var pen = 0d;
                    for (var i = 0; i < glyphs.Length; i++)
                    {
                        var advance = glyphs[i].Advance;
                        var glyphStart = pen;
                        pen += advance;
                        if (advance <= 0 || glyphs[i].IsInvisible || IsSkippedSpace(text, i, wordSeparators: false))
                            continue;

                        // (the last glyph of a line has no letter-spacing)
                        var glyphEnd = pen;
                        if (i < glyphs.Length - 1 && (glyphs[i].Flags & ShapedGlyphFlags.LetterSpaced) != ShapedGlyphFlags.None)
                        {
                            glyphEnd -= Math.Min(letterSpacing, advance);
                        }

                        start = Math.Min(start, forward ? position + glyphStart : position + size - glyphEnd);
                        end = Math.Max(end, forward ? position + glyphEnd : position + size - glyphStart);
                    }
                }
                else if (child is BoxFragment { Box: { } box })
                {
                    // The inline boxes and the boxes of the ruby are in the line; the floats and the out-of-flow boxes are not
                    if (box.IsAtomicInline)
                    {
                        start = Math.Min(start, position);
                        end = Math.Max(end, position + size);
                    }
                    else if (!box.IsOutOfFlow && !box.Style.Layout.IsFloating)
                    {
                        Visit(child, position);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether the character of a glyph of a text fragment is a spacer of <c>text-decoration-skip-spaces</c>: "any typographic
    /// character unit from Unicode General Category Zs except U+202F NARROW NO-BREAK SPACE", or also a word separator
    /// (https://drafts.csswg.org/css-text-4/#word-separator).
    /// </summary>
    private static bool IsSkippedSpace(TextFragment fragment, int glyphIndex, bool wordSeparators)
    {
        if (wordSeparators && (fragment.Glyphs[glyphIndex].Flags & ShapedGlyphFlags.WordSeparator) != ShapedGlyphFlags.None)
            return true;

        var offset = GetCharacterOffset(fragment, glyphIndex);
        if (offset < 0)
            return false;

        var codePoint = GetCodePoint(fragment.Run.Data, offset);
        return codePoint != 0x202F && CharUnicodeInfo.GetUnicodeCategory(codePoint) == UnicodeCategory.SpaceSeparator;
    }

    /// <summary>
    /// Returns the offset, in the data of the text run of a text fragment, of the first character of the typographic character
    /// unit of one of its glyphs (the character before the text transformation), or -1 when the glyph has no character of the
    /// text (the hyphen of a hyphenated line).
    /// </summary>
    private static int GetCharacterOffset(TextFragment fragment, int glyphIndex)
    {
        if (fragment.IsHyphen)
            return -1;

        var glyphs = fragment.Glyphs;
        var index = glyphs[glyphIndex].Cluster - glyphs[0].Cluster;
        var offset = fragment.DomOffsets is { } map ? ((uint)index < (uint)map.Length ? map[index] : -1) : fragment.Start + index;
        return (uint)offset < (uint)fragment.Run.Data.Length ? offset : -1;
    }

    private static int GetCodePoint(string text, int index)
        => char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];

    private void PaintDecorationLineCore(in TextLine line, double top, double thickness, string style, DisplayPaint paint, int outwards, Node? source)
    {
        var band = SnapToDevicePixels(line.GetBand(top, top + thickness));
        switch (style)
        {
            case "double":
            {
                _builder.Add(new DrawRectItem(band, paint) { Source = source });
                var second = top + (outwards * thickness * 2);
                _builder.Add(new DrawRectItem(SnapToDevicePixels(line.GetBand(second, second + thickness)), paint) { Source = source });
                break;
            }

            case "dotted" or "dashed":
            {
                var center = top + (thickness / 2);
                var (start, end) = GetDecorationAxis(line, center);
                // The pattern is anchored to the coordinates of the group, so the fragments of a decorating box (split by the
                // bidi reordering or by the inline boxes) have a continuous pattern
                var period = style == "dotted" ? thickness * 2 : thickness * 5;
                var phase = line.InlineStart % period;
                if (phase < 0)
                {
                    phase += period;
                }

                var stroke = style == "dotted"
                    ? new StrokeStyle(thickness, LineCap.Round, Dashes: [0, thickness * 2], DashOffset: phase - (thickness / 2))
                    : new StrokeStyle(thickness, Dashes: [thickness * 3, thickness * 2], DashOffset: phase);
                _builder.Add(new DrawLineItem(start, end, paint with { Style = PaintStyle.Stroke, Stroke = stroke }) { Source = source });
                break;
            }

            case "wavy":
            {
                // A wave along the line, of an amplitude and a wavelength proportional to the thickness
                var amplitude = Math.Max(1, thickness * 1.5);
                var step = Math.Max(2, thickness * 3);
                var center = top + amplitude;
                var path = new DisplayPath();
                var length = line.InlineEnd - line.InlineStart;
                var (start, _) = GetDecorationAxis(line, center);
                path.MoveTo(start.X, start.Y);
                var sign = -1;
                for (var x = 0d; x < length; x += step)
                {
                    var control = Point(line, x + (step / 2), center + (sign * amplitude * 2));
                    var next = Point(line, Math.Min(length, x + step), center);
                    path.QuadTo(control.X, control.Y, next.X, next.Y);
                    sign = -sign;
                }

                _builder.Add(new DrawPathItem(path, paint with { Style = PaintStyle.Stroke, Stroke = new StrokeStyle(thickness) }) { Source = source });
                break;
            }

            default:
                _builder.Add(new DrawRectItem(band, paint) { Source = source });
                break;
        }

        static PhysicalOffset Point(in TextLine line, double inline, double distance)
        {
            var block = line.Baseline + (line.UnderSign * distance);
            return line.Horizontal ? new PhysicalOffset(line.InlineStart + inline, block) : new PhysicalOffset(block, line.InlineStart + inline);
        }
    }

    private static (PhysicalOffset Start, PhysicalOffset End) GetDecorationAxis(in TextLine line, double distance)
    {
        var block = line.Baseline + (line.UnderSign * distance);
        return line.Horizontal
            ? (new PhysicalOffset(line.InlineStart, block), new PhysicalOffset(line.InlineEnd, block))
            : (new PhysicalOffset(block, line.InlineStart), new PhysicalOffset(block, line.InlineEnd));
    }

    /// <summary>
    /// Parses the shadows of a computed value of <c>text-shadow</c> or <c>box-shadow</c> (a color, 2 to 4 lengths and inset),
    /// or returns null for none.
    /// </summary>
    private static List<Shadow>? GetShadows(ComputedStyle style, string property, DisplayColor? currentColor = null)
    {
        var value = style.GetComputedValue(property);
        if (value is "none" or "")
            return null;

        var zoom = style.Layout.EffectiveZoom;
        List<Shadow>? result = null;
        Span<double> lengths = stackalloc double[4];
        foreach (var layer in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))))
        {
            DisplayColor? color = null;
            var inset = false;
            var count = 0;
            foreach (var component in layer)
            {
                if (component is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "inset"))
                {
                    inset = true;
                }
                else if (count < 4 && LengthPercentage.TryParse(component, zoom, nonNegative: false, out var length) && !length.HasPercentage)
                {
                    lengths[count++] = length.Length;
                }
                else if (currentColor is not null && component is CssPreservedToken { Type: CssTokenType.Ident } keyword && InfraAscii.EqualsIgnoreAsciiCase(keyword.Token.Value, "currentcolor"))
                {
                    color = currentColor;
                }
                else if (ValueMatcher.IsColor(component) || component is CssPreservedToken { Type: CssTokenType.Ident })
                {
                    color = ResolveColor(style, component);
                }
            }

            if (count < 2)
                continue;

            (result ??= []).Add(new Shadow(color ?? currentColor ?? GetColor(style, "color"), lengths[0], lengths[1], count > 2 ? Math.Max(0, lengths[2]) : 0, count > 3 ? lengths[3] : 0, inset));
        }

        return result;
    }

    /// <summary>
    /// Returns the highlighted ranges of a text fragment (https://drafts.csswg.org/css-pseudo-4/#highlight-pseudos), in painting
    /// order (https://drafts.csswg.org/css-pseudo-4/#highlight-painting): the custom highlights of <c>CSS.highlights</c>
    /// (<c>::highlight(name)</c>) by priority, the target text of the text directives (<c>::target-text</c>), then the
    /// selection of the document (<c>::selection</c>, the highlight color of the browsers when it is not styled). The ranges
    /// are split where the highlights below start and end: the <c>color</c> of a highlight that is <c>currentcolor</c> (the
    /// default) is the color of the text in the highlight overlay below (or of the originating text), and
    /// <c>currentcolor</c> in its other properties is its own color. Returns null when nothing is highlighted.
    /// </summary>
    private List<TextHighlight>? GetHighlights(TextFragment fragment, PhysicalOffset offset)
    {
        var engine = _document.GetStyleEngine();
        if (fragment.Run.Node is not Text node || fragment.Run.Element is not { } element)
        {
            // The selection of the value of a text control (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#textFieldSelection)
            // is highlighted in its inner editor, with the ::selection pseudo-element of the control
            return fragment.Run.Node is null && TextControlLayout.GetSelectedRange(fragment) is var (control, controlStart, controlEnd) && !FocusModel.IsInert(control)
                ? GetHighlights(fragment, offset, [(engine.GetStyle(control, "selection"), controlStart, controlEnd, "highlight", null)], coversLine: true)
                : null;
        }

        // The layers that intersect the fragment, in painting order
        List<(ComputedStyle Style, int Start, int End, string? DefaultBackground, string? DefaultColor)>? layers = null;
        if (_document.HasHighlights)
        {
            var highlights = new List<(int Priority, int Index, string Name, Css.Om.Highlight Highlight)>();
            var index = 0;
            foreach (var (name, highlight) in _document.Highlights)
            {
                highlights.Add((highlight.Priority, index++, name, highlight));
            }

            highlights.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Index.CompareTo(b.Index));
            List<(int Start, int End)>? ranges = null;
            foreach (var (_, _, name, highlight) in highlights)
            {
                ranges?.Clear();
                foreach (var range in highlight)
                {
                    // https://drafts.csswg.org/css-highlight-api-1/#range-invalidation: the static ranges that are not valid are ignored
                    if (range is StaticRange { IsValid: false })
                        continue;

                    if (GetIntersection(range, node, fragment) is { } intersection)
                    {
                        (ranges ??= []).Add(intersection);
                    }
                }

                if (ranges is not { Count: > 0 })
                    continue;

                // The characters of a highlight are highlighted once, also where its ranges overlap: the union of its ranges.
                // The name of the pseudo-element is serialized as the selectors are (an identifier, with its escapes)
                var style = engine.GetStyle(element, "highlight(" + CssSerializer.SerializeIdentifier(name) + ")");
                ranges.Sort(static (a, b) => a.Start.CompareTo(b.Start));
                var (start, end) = ranges[0];
                for (var i = 1; i < ranges.Count; i++)
                {
                    if (ranges[i].Start <= end)
                    {
                        end = Math.Max(end, ranges[i].End);
                        continue;
                    }

                    (layers ??= []).Add((style, start, end, null, null));
                    (start, end) = ranges[i];
                }

                (layers ??= []).Add((style, start, end, null, null));
            }
        }

        if (_document.TargetTextRanges is { } targetTextRanges)
        {
            foreach (var range in targetTextRanges)
            {
                if (GetIntersection(range, node, fragment) is var (start, end))
                {
                    (layers ??= []).Add((engine.GetStyle(element, "target-text"), start, end, "mark", "marktext"));
                }
            }
        }

        // https://html.spec.whatwg.org/multipage/interaction.html#inert-subtrees: the user agent "may prevent the user from
        // selecting text in that node": as in the browsers, the text of an inert node in the selection is not highlighted
        // (the selection may cross the boundaries of the shadow trees: its boundary points are compared in shadow-including tree
        // order). https://drafts.csswg.org/css-ui-4/#valdef-user-select-none: neither is the text of an element that cannot be
        // selected
        if (_document.GetSelection()?.GetRenderedBoundaryPoints() is var (selectionStartNode, selectionStartOffset, selectionEndNode, selectionEndOffset)
            && GetIntersection(selectionStartNode, selectionStartOffset, selectionEndNode, selectionEndOffset, node, fragment) is var (selectionStart, selectionEnd) && !FocusModel.IsInert(node)
            && !IsUserSelectNone(engine, element))
        {
            (layers ??= []).Add((engine.GetStyle(element, "selection"), selectionStart, selectionEnd, "highlight", null));
        }

        return layers is null ? null : GetHighlights(fragment, offset, layers);
    }

    /// <summary>Returns the highlighted ranges of a text fragment from the layers of the highlights that intersect it, in painting order (see <see cref="GetHighlights(TextFragment, PhysicalOffset)"/>).</summary>
    /// <remarks>
    /// The overlay of a highlight covers the characters along the line and the fragment of the text (the ascent and the descent
    /// of its font) across it; with <paramref name="coversLine"/>, the line box (https://drafts.csswg.org/css-pseudo-4/#highlight-bounds:
    /// "may extend further above/below the em box to the line box edges"): the selection of the value of a text control, as in the browsers.
    /// </remarks>
    private List<TextHighlight>? GetHighlights(TextFragment fragment, PhysicalOffset offset, List<(ComputedStyle Style, int Start, int End, string? DefaultBackground, string? DefaultColor)> layers, bool coversLine = false)
    {
        // The segments between the boundaries of the layers, and the color and the stroke of the text in each of them under the
        // current layer
        var boundaries = new SortedSet<int>();
        foreach (var layer in layers)
        {
            boundaries.Add(layer.Start);
            boundaries.Add(layer.End);
        }

        var positions = boundaries.ToArray();
        var textColor = GetColor(fragment.Style, "color");
        var colors = new DisplayColor[positions.Length - 1];
        Array.Fill(colors, textColor);
        var textStroke = TextStroke.From(fragment.Style);
        var strokes = new TextStroke[positions.Length - 1];
        Array.Fill(strokes, textStroke);

        List<TextHighlight>? result = null;
        foreach (var (style, start, end, defaultBackground, defaultColor) in layers)
        {
            // (the declarations of the colors of a highlight of a visited link are the ones of its visited style)
            var colorStyle = style.VisitedOrSelf;
            var ownColor = colorStyle.GetComputedValue("color") == "currentcolor" ? (DisplayColor?)null : GetColor(style, "color");

            // https://drafts.csswg.org/css-pseudo-4/#highlight-styling: the stroke of the text of a highlight is the one of the
            // highlight pseudo-element, which does not inherit it from the text and where -webkit-text-stroke does not apply: its
            // text has no stroke. As in Chrome, a highlight without any declaration (e.g. the default selection) keeps the
            // stroke of the text
            var hasStyle = HasHighlightDeclarations(colorStyle);

            // The underlines and the overlines of a highlight are under its text: the highlight paints the glyphs
            var decoratesUnderText = (ParseDecorationLines(style) & (TextDecorationLines.Underline | TextDecorationLines.Overline)) != TextDecorationLines.None;
            var first = Array.BinarySearch(positions, start);
            var last = Array.BinarySearch(positions, end);
            for (var i = first; i < last;)
            {
                var (color, background) = GetLayerColors(colors[i]);
                var stroke = hasStyle ? default : strokes[i];
                var recolors = color != colors[i];
                var restrokes = stroke != strokes[i];

                // The consecutive segments painted the same way are one range
                var segmentEnd = i + 1;
                while (segmentEnd < last && GetLayerColors(colors[segmentEnd]) == (color, background) && (color != colors[segmentEnd]) == recolors
                    && (hasStyle ? default : strokes[segmentEnd]) == stroke && (stroke != strokes[segmentEnd]) == restrokes)
                {
                    segmentEnd++;
                }

                var decorations = GetHighlightDecorations(style, color);
                var paintsText = recolors || restrokes || decoratesUnderText;

                // https://drafts.csswg.org/css-pseudo-4/#highlight-text: currentcolor in the shadows of a highlight is the color
                // of its text
                var shadows = hasStyle ? GetShadows(style, "text-shadow", color) : null;
                if (!background.IsTransparent || paintsText || decorations is not null || shadows is not null)
                {
                    var bounds = fragment.GetRangeRect(positions[i], positions[segmentEnd]);
                    if (coversLine && fragment.Parent is LineBoxFragment line)
                    {
                        bounds = fragment.WritingDirection.IsHorizontal
                            ? new PhysicalRect(bounds.X, -fragment.Offset.Y, bounds.Width, line.Size.Height)
                            : new PhysicalRect(-fragment.Offset.X, bounds.Y, line.Size.Width, bounds.Height);
                    }

                    bounds = bounds.Translate(offset);
                    (result ??= []).Add(new TextHighlight(SnapToDevicePixels(bounds), background, paintsText ? color : null, decorations, bounds) { Recolors = recolors, Stroke = stroke, Shadows = shadows });
                }

                for (; i < segmentEnd; i++)
                {
                    colors[i] = color;
                    strokes[i] = stroke;
                }
            }

            // The color of the text and of the background of the layer over a text of a color
            (DisplayColor Color, DisplayColor Background) GetLayerColors(DisplayColor below)
            {
                var color = ownColor ?? below;
                var background = GetHighlightColor(style, "background-color", color);
                if (defaultBackground is not null && background.IsTransparent && ownColor is null && !IsHighlightStyled(colorStyle))
                {
                    // https://drafts.csswg.org/css-pseudo-4/#paired-defaults: the selection or the target text is not styled (no
                    // color nor background-color is specified): the highlight colors of the browsers (the system colors
                    // Highlight for the selection, which keeps the color of the text, Mark and MarkText for the target text).
                    // In forced colors mode, the text of the selection has the HighlightText system color: the forced color of
                    // the text may not be legible on Highlight
                    background = DisplayColor.Parse(CssValueComputer.GetSystemColor(fragment.Style, defaultBackground)) ?? DisplayColor.Transparent;
                    var pairedColor = defaultColor ?? (style.IsForcedColorsAdjusted ? "highlighttext" : null);
                    if (pairedColor is not null && DisplayColor.Parse(CssValueComputer.GetSystemColor(fragment.Style, pairedColor)) is { } systemColor)
                    {
                        color = systemColor;
                    }
                }

                return (color, background);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the used value of <c>user-select</c> of an element is <c>none</c> (https://drafts.csswg.org/css-ui-4/#content-selection):
    /// its computed value, or for <c>auto</c> the used value of its parent when it is <c>none</c>.
    /// </summary>
    private static bool IsUserSelectNone(StyleEngine engine, Element element)
    {
        for (var current = element; current is not null; current = StyleEngine.GetInheritanceParent(current))
        {
            var value = engine.GetStyle(current).GetComputedValue("user-select");
            if (value is not ("auto" or ""))
                return value == "none";
        }

        return false;
    }

    /// <summary>Whether a declaration applies to a highlight pseudo-element (also to the highlight pseudo-element of an ancestor, which it inherits from).</summary>
    private static bool HasHighlightDeclarations(ComputedStyle style)
    {
        for (var current = style; current is not null; current = current.HighlightParent)
        {
            if (current.HasCascadedValues)
                return true;
        }

        return false;
    }

    /// <summary>Whether <c>color</c> or <c>background-color</c> is specified for a highlight pseudo-element (also inherited from the highlight pseudo-element of an ancestor).</summary>
    private static bool IsHighlightStyled(ComputedStyle style)
    {
        for (var current = style; current is not null; current = current.HighlightParent)
        {
            if (IsSpecified(current, "color") || IsSpecified(current, "background-color"))
                return true;
        }

        return false;

        // (a revert that rolls back to the user agent origin, which has no declaration, gives the default colors)
        static bool IsSpecified(ComputedStyle style, string property) => style.TryGetCascadedValue(property, out var cascaded) && cascaded.Position >= 0;
    }

    /// <summary>Returns a color of a highlight pseudo-element: <c>currentcolor</c> is the color of its text (https://drafts.csswg.org/css-pseudo-4/#highlight-text).</summary>
    private static DisplayColor GetHighlightColor(ComputedStyle style, string property, DisplayColor currentColor)
        => style.VisitedOrSelf.GetComputedValue(property) == "currentcolor" ? currentColor : GetColor(style, property);

    /// <summary>Returns the decorations of a highlight pseudo-element (https://drafts.csswg.org/css-pseudo-4/#highlight-styling), or null.</summary>
    private static List<AppliedDecoration>? GetHighlightDecorations(ComputedStyle style, DisplayColor currentColor)
    {
        var lines = ParseDecorationLines(style);
        return lines == TextDecorationLines.None ? null : [new AppliedDecoration(lines, style.GetComputedValue("text-decoration-style"), GetHighlightColor(style, "text-decoration-color", currentColor), style, DecoratingBox: null)];
    }

    /// <summary>Returns the characters of a text fragment in a range, or null when the range does not intersect it.</summary>
    private static (int Start, int End)? GetIntersection(AbstractRange range, Text node, TextFragment fragment)
        => GetIntersection(range.StartContainer, range.StartOffset, range.EndContainer, range.EndOffset, node, fragment);

    /// <summary>
    /// Returns the characters of a text fragment between two boundary points, or null when there is none. The text of the
    /// shadow trees of the shadow hosts between the boundary points is between them: the points are compared in
    /// shadow-including tree order.
    /// </summary>
    private static (int Start, int End)? GetIntersection(Node startContainer, uint startOffset, Node endContainer, uint endOffset, Text node, TextFragment fragment)
    {
        var length = (uint)node.Length;
        if (AbstractRange.CompareComposedPoints(startContainer, startOffset, node, length) == AbstractRange.BoundaryPointPosition.After
            || AbstractRange.CompareComposedPoints(endContainer, endOffset, node, 0) == AbstractRange.BoundaryPointPosition.Before)
        {
            return null;
        }

        var start = ReferenceEquals(startContainer, node) ? (int)startOffset : 0;
        var end = ReferenceEquals(endContainer, node) ? (int)endOffset : (int)length;
        start = Math.Max(start, fragment.Start);
        end = Math.Min(end, fragment.End);
        return end > start ? (start, end) : null;
    }

    /// <summary>
    /// Paints the emphasis marks of a text fragment (https://drafts.csswg.org/css-text-decor-3/#emphasis-marks): a mark of half
    /// the font size centered over (or under) each character that is not a space, in <c>text-emphasis-color</c> (or in
    /// <paramref name="paintOverride"/>, the paint of the mask of <c>background-clip: text</c>). In vertical text, the marks are
    /// to the right (or to the left) of the characters: upright, their content area next to the content area of the text (as
    /// the ruby annotations), or rotated with the sideways text.
    /// </summary>
    private void PaintEmphasisMarks(TextFragment fragment, PhysicalOffset offset, in TextLine line, Node? source, DisplayPaint? paintOverride = null)
    {
        var style = fragment.Style;
        var value = style.GetComputedValue("text-emphasis-style");

        // The ellipsis of a line has no emphasis marks (as in the browsers: it is not a character of the content)
        if (value is "none" or "" || fragment.ShapedText is null || fragment.IsEllipsis)
            return;

        var mark = GetEmphasisMark(value, line.Horizontal);
        if (mark.Length == 0)
            return;

        var color = GetColor(style, "text-emphasis-color");
        if (color.IsTransparent && paintOverride is null)
            return;

        var options = TextShapingOptions.FromStyle(style) with { FontSize = line.FontSize / 2, LetterSpacing = 0, WordSpacing = 0, IsRightToLeft = false, Orientation = TextShapingOrientation.Horizontal };
        var markText = TextShaper.Shape(mark, style.Fonts, options);
        if (markText.Runs.Length == 0)
            return;

        var markRun = markText.Runs[0];
        // The content area of the marks is the one of the first available font at half the font size (as a ruby annotation), whatever
        // the font of the glyph of the mark
        var markMetrics = style.Fonts.PrimaryFont.GetMetrics(line.FontSize / 2);
        var glyphMetrics = markRun.Font.GetMetricsForEmSize(markRun.EmSize);
        var position = style.GetComputedValue("text-emphasis-position");
        var writingDirection = fragment.WritingDirection;
        var upright = !line.Horizontal && style.Layout.HasCentralDominantBaseline;

        // The marks are laid out along the inline axis of a frame: the physical one for the horizontal text and the upright marks, a
        // frame rotated as the sideways glyphs (the inline axis is its x axis, its line-over side is above, see BuildGlyphRuns)
        DisplayMatrix? transform = null;
        double inlineStart;
        bool forward;
        double markBaseline;
        var markCenter = 0d;
        if (line.Horizontal || !upright)
        {
            var over = line.Horizontal ? !position.Contains("under", StringComparison.Ordinal) : !position.Contains("left", StringComparison.Ordinal) == (writingDirection.LineOver == PhysicalSide.Right);
            if (line.Horizontal)
            {
                inlineStart = line.InlineStart;
                forward = writingDirection.InlineStart == PhysicalSide.Left;
                // The baseline of the marks is snapped to the device pixels, as the baselines of the texts; the marks are outside of the
                // ruby annotations of the text on their side
                var annotationEdge = GetRubyAnnotationsEdge(fragment, offset, over ? PhysicalSide.Top : PhysicalSide.Bottom);
                markBaseline = annotationEdge is { } edge
                    ? SnapToDevicePixels(over ? edge - markMetrics.Descent : edge + markMetrics.Ascent)
                    : SnapToDevicePixels(over ? line.Baseline - line.Metrics.Ascent - markMetrics.Descent : line.Baseline + line.Metrics.Descent + markMetrics.Ascent);
            }
            else
            {
                // The content area of the marks next to the content area of the text (in the rotated frame)
                var clockwise = writingDirection.LineOver == PhysicalSide.Right;
                var origin = clockwise ? new PhysicalOffset(line.Baseline, line.InlineStart) : new PhysicalOffset(line.Baseline, line.InlineEnd);
                transform = DisplayMatrix.Translation(origin.X, origin.Y).PreConcat(DisplayMatrix.Rotation(clockwise ? 90 : -90));
                inlineStart = 0;
                forward = writingDirection.InlineStart == (clockwise ? PhysicalSide.Top : PhysicalSide.Bottom);
                markBaseline = over ? -(line.Metrics.Ascent + markMetrics.Descent) : line.Metrics.Descent + markMetrics.Ascent;

                // The baseline of the marks is snapped to the device pixels in the physical frame (the x axis)
                var sign = clockwise ? -1 : 1;
                markBaseline = sign * (SnapToDevicePixels(origin.X + (sign * markBaseline)) - origin.X);
            }
        }
        else
        {
            // The upright marks: their content area centered next to the content area of the text (the fragment)
            inlineStart = line.InlineStart;
            forward = writingDirection.InlineStart == PhysicalSide.Top;
            markBaseline = 0;
            // (the central baseline of the content area of the marks is placed as the one of a text: the extra pixel of an odd height
            // is on the ascent side, the line-over side)
            var markSize = markMetrics.Ascent + markMetrics.Descent;
            var markDescent = markSize == Math.Floor(markSize) ? Math.Floor(markSize / 2) : markSize / 2;
            var overIsRight = writingDirection.LineOver == PhysicalSide.Right;
            var right = !position.Contains("left", StringComparison.Ordinal);
            var towardsOver = right == overIsRight;
            var distance = towardsOver ? markDescent : markSize - markDescent;
            var annotationEdge = GetRubyAnnotationsEdge(fragment, offset, right ? PhysicalSide.Right : PhysicalSide.Left);
            markCenter = right ? (annotationEdge ?? (offset.X + fragment.Size.Width)) + distance : (annotationEdge ?? offset.X) - distance;
        }

        var inlineEnd = inlineStart + (line.InlineEnd - line.InlineStart);
        var glyphs = fragment.Glyphs;
        var letterSpacing = fragment.ShapedText?.Options.LetterSpacing ?? 0;
        var ids = new List<ushort>();
        var positions = new List<PhysicalOffset>();
        var pen = 0d;
        var markCentral = InlineLayoutAlgorithm.GetCentralBaseline(markRun.Font, glyphMetrics);
        for (var i = 0; i < glyphs.Length; i++)
        {
            // The marks are centered on the glyphs, without the letter-spacing added after them (the last glyph of a line has none)
            var glyph = glyphs[i];
            var spacing = i < glyphs.Length - 1 && (glyph.Flags & ShapedGlyphFlags.LetterSpaced) != ShapedGlyphFlags.None ? letterSpacing : 0;
            var center = pen + ((glyph.Advance - spacing) / 2);
            pen += glyph.Advance;
            if ((glyph.Flags & ShapedGlyphFlags.ClusterStart) == ShapedGlyphFlags.None || (glyph.Flags & ShapedGlyphFlags.Invisible) != ShapedGlyphFlags.None || glyph.Advance <= 0)
                continue;

            if (!HasEmphasisMark(fragment, i))
                continue;

            var inline = forward ? inlineStart + center : inlineEnd - center;
            var markPen = (upright ? markCenter : inline) - (markText.Width / 2);
            var baseline = upright ? inline + markCentral : markBaseline;
            foreach (var markGlyph in markText.Glyphs)
            {
                ids.Add(markGlyph.GlyphId);
                positions.Add(new PhysicalOffset(markPen + markGlyph.OffsetX, baseline + markGlyph.OffsetY));
                markPen += markGlyph.Advance;
            }
        }

        if (ids.Count > 0)
        {
            var run = new GlyphRun(DisplayFont.From(markRun.Font, markRun.EmSize), [.. ids], [.. positions], Text: "");
            DrawGlyphRuns([new PositionedGlyphRun(run, transform, GetFontPalette(style, markRun.Font))], paintOverride ?? new DisplayPaint(color), source, colorize: paintOverride is null);
        }
    }

    /// <summary>
    /// Whether an emphasis mark is drawn for the typographic character unit of a glyph of a text fragment
    /// (https://drafts.csswg.org/css-text-decor-4/#text-emphasis-style-property): not for the word separators and the other
    /// separators (Z*, unless a combining character combines with them), the control codes and the unassigned characters (Cc,
    /// Cf, Cn), nor for the punctuation (P*) other than the symbols listed by the specification.
    /// </summary>
    private static bool HasEmphasisMark(TextFragment fragment, int glyphIndex)
    {
        // The hyphen of a hyphenated line is a punctuation character
        if (fragment.IsHyphen)
            return false;

        var data = fragment.Run.Data;
        var offset = GetCharacterOffset(fragment, glyphIndex);
        if (offset < 0)
            return true;

        var codePoint = GetCodePoint(data, offset);
        switch (CharUnicodeInfo.GetUnicodeCategory(codePoint))
        {
            case UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator:
            {
                // "emphasis marks are drawn for a space that combines with any combining characters"
                var next = offset + (codePoint > 0xFFFF ? 2 : 1);
                return next < data.Length && Unicode.UnicodeProperties.IsMark(GetCodePoint(data, next));
            }

            case UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned:
                return false;

            case UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation:
                // The punctuation characters that have a mark: # % U+2030 U+2031 U+066A U+0609 U+060A & U+204A @ U+00A7 U+00B6
                // U+204B U+2053 U+303D, and the characters that are equivalent to them under NFKD (the small and fullwidth forms
                // of # % & @)
                return codePoint is '#' or '%' or '&' or '@' or 0xA7 or 0xB6 or 0x609 or 0x60A or 0x66A or 0x2030 or 0x2031 or 0x204A or 0x204B or 0x2053 or 0x303D
                    or 0xFE5F or 0xFE60 or 0xFE6A or 0xFE6B or 0xFF03 or 0xFF05 or 0xFF06 or 0xFF20;

            default:
                // (the other word separators of https://drafts.csswg.org/css-text-4/#word-separator)
                return !TextShaper.IsWordSeparator(codePoint);
        }
    }

    /// <summary>
    /// Returns the outer edge, on a side, of the ruby annotations of a text in a ruby base, or null: "If emphasis marks are applied
    /// to characters for which ruby is drawn in the same position as the emphasis mark, the emphasis marks are placed outside the
    /// ruby" (https://drafts.csswg.org/css-text-decor-4/#text-emphasis-position-property). The annotations are the ones of the ruby
    /// container of the base that overlap the text in the inline axis and are on that side of it.
    /// </summary>
    private double? GetRubyAnnotationsEdge(TextFragment fragment, PhysicalOffset offset, PhysicalSide side)
    {
        BoxFragment? rubyBase = null;
        for (var ancestor = fragment.Parent; ancestor is BoxFragment box; ancestor = box.Parent)
        {
            if (box.Box.Display.Internal == DisplayInternal.RubyBase)
            {
                rubyBase = box;
                break;
            }
        }

        if (rubyBase?.Parent is not BoxFragment ruby)
            return null;

        if (ruby.Box.Display.Internal == DisplayInternal.RubyBaseContainer && ruby.Parent is BoxFragment container)
        {
            ruby = container;
        }

        var text = new PhysicalRect(offset.X, offset.Y, fragment.Size.Width, fragment.Size.Height);
        double? edge = null;
        foreach (var child in ruby.ChildrenSpan)
        {
            if (child is not BoxFragment { Box.Display.Internal: DisplayInternal.RubyText or DisplayInternal.RubyTextContainer } annotation)
                continue;

            var position = annotation.GetOffsetInRoot() - _group.Origin;
            var rect = new PhysicalRect(position.X, position.Y, annotation.Size.Width, annotation.Size.Height);
            var (overlaps, onSide) = side switch
            {
                PhysicalSide.Top => (rect.X < text.Right && rect.Right > text.X, rect.Bottom <= text.Y + 0.5),
                PhysicalSide.Bottom => (rect.X < text.Right && rect.Right > text.X, rect.Y >= text.Bottom - 0.5),
                PhysicalSide.Right => (rect.Y < text.Bottom && rect.Bottom > text.Y, rect.X >= text.Right - 0.5),
                _ => (rect.Y < text.Bottom && rect.Bottom > text.Y, rect.Right <= text.X + 0.5),
            };

            if (!overlaps || !onSide)
                continue;

            edge = side switch
            {
                PhysicalSide.Top => Math.Min(edge ?? double.PositiveInfinity, rect.Y),
                PhysicalSide.Bottom => Math.Max(edge ?? double.NegativeInfinity, rect.Bottom),
                PhysicalSide.Right => Math.Max(edge ?? double.NegativeInfinity, rect.Right),
                _ => Math.Min(edge ?? double.PositiveInfinity, rect.X),
            };
        }

        return edge;
    }

    /// <summary>Returns the string of an emphasis mark (https://drafts.csswg.org/css-text-decor-3/#text-emphasis-style-property).</summary>
    private static string GetEmphasisMark(string value, bool horizontal)
    {
        if (value.Length >= 2 && value[0] is '"' or '\'')
            return CssParser.ParseListOfComponentValues(value) is [CssPreservedToken { Type: CssTokenType.String } text, ..] ? text.Token.Value : "";

        var open = value.Contains("open", StringComparison.Ordinal);
        var shape = value.Replace("filled", "", StringComparison.Ordinal).Replace("open", "", StringComparison.Ordinal).Trim();
        if (shape.Length == 0)
        {
            shape = horizontal ? "circle" : "sesame";
        }

        return shape switch
        {
            "dot" => open ? "\U000025E6" : "\U00002022",
            "circle" => open ? "\U000025CB" : "\U000025CF",
            "double-circle" => open ? "\U000025CE" : "\U000025C9",
            "triangle" => open ? "\U000025B3" : "\U000025B2",
            "sesame" => open ? "\U0000FE46" : "\U0000FE45",
            _ => "",
        };
    }
}
