using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// Places the parts of a text, as SVG places the characters of a <c>text</c> (SVG 2, 11.8.2): the positions and the
/// shifts of the spans, the text chunks and their anchors, the baselines, the length of the text and its direction. The
/// renderers that measure the texts (images, PDF) link this file, so they place the texts the same way.
/// </summary>
internal static class TextPlacement
{
    // The largest magnitude of a spacing and of a scale: the limit of the spacing of the image renderer
    private const double MaxSpacing = 65536;

    /// <summary>A part of a text: characters of one span that are measured and drawn together.</summary>
    /// <param name="Span">The index of the span in <see cref="SceneText.Spans"/>, or -1 for a text without spans.</param>
    /// <param name="Width">The natural advance of the part, its spacing included.</param>
    /// <param name="Clusters">The number of grapheme clusters of the part.</param>
    /// <param name="BaselineOffset">The distance from the alphabetic baseline of the font of the part up to the baseline of the text (<see cref="GetBaselineOffset"/>).</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Item(int Span, double Width, int Clusters, double BaselineOffset);

    /// <summary>Where the parts of a text are drawn.</summary>
    internal sealed class Result
    {
        public Result(int count)
        {
            X = new double[count];
            Y = new double[count];
            Width = new double[count];
        }

        /// <summary>Gets the left edge of each part; on a path, the distance along the path of the start of each part.</summary>
        public double[] X { get; }

        /// <summary>Gets the alphabetic baseline of each part; on a path, the distance of the baseline below the path.</summary>
        public double[] Y { get; }

        /// <summary>Gets the advance of each part as it is placed: scaled, and with the spacing of the length of the text.</summary>
        public double[] Width { get; }

        /// <summary>Gets the space added after each grapheme cluster to fit the length of the text, except after the last cluster of the text.</summary>
        public double GapSpacing { get; set; }

        /// <summary>Gets the index of the part that has the last cluster of the text, which gets no <see cref="GapSpacing"/>, or -1.</summary>
        public int LastItem { get; set; } = -1;

        /// <summary>Gets the horizontal scale of the glyphs, from the left edge of each part.</summary>
        public double Scale { get; set; } = 1;
    }

    /// <summary>Places the parts of a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="items">The parts, in the order of the text.</param>
    /// <returns>The positions of the parts.</returns>
    /// <remarks>
    /// The current position starts at the anchor of the text (at <see cref="SceneText.PathStartOffset"/> on a path). The
    /// first part of a span moves it to the <see cref="SceneTextSpan.X"/> and <see cref="SceneTextSpan.Y"/> of the span,
    /// then by its <see cref="SceneTextSpan.Dx"/> and <see cref="SceneTextSpan.Dy"/>; each part advances it by its width,
    /// to the left from right to left. A span with an absolute position starts a new text chunk; each chunk is then moved
    /// so that its anchor (its start, its middle or its end, from the extent of its parts) is at the position of its first
    /// part, as SVG 2 does.
    /// </remarks>
    public static Result Place(SceneText text, IReadOnlyList<Item> items)
    {
        var result = new Result(items.Count);
        var natural = 0.0;
        var clusters = 0;
        for (var i = 0; i < items.Count; i++)
        {
            natural += Finite(items[i].Width);
            clusters += Math.Max(0, items[i].Clusters);
            if (items[i].Clusters > 0)
            {
                result.LastItem = i;
            }
        }

        if (text.TextLength is { } length && length >= 0 && double.IsFinite(length))
        {
            if (!IsFittedBySpacing(text))
            {
                if (natural > 0)
                {
                    result.Scale = Math.Min(length / natural, MaxSpacing);
                }
            }
            else if (clusters > 1)
            {
                result.GapSpacing = Math.Clamp((length - natural) / (clusters - 1), -MaxSpacing, MaxSpacing);
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var spacing = result.GapSpacing * (Math.Max(0, items[i].Clusters) - (i == result.LastItem ? 1 : 0));
            result.Width[i] = (Finite(items[i].Width) * result.Scale) + spacing;
        }

        var onPath = text.Path is not null;
        var rightToLeft = text.Direction is SceneTextDirection.RightToLeft;
        var x = onPath ? Finite(text.PathStartOffset) : text.X;
        var y = onPath ? 0 : text.Y;
        var chunkStart = 0;
        var anchor = x;
        var previousSpan = int.MinValue;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            SceneTextSpan? span = item.Span >= 0 && item.Span < text.Spans.Count ? text.Spans[item.Span] : null;
            if (span is not null && item.Span != previousSpan)
            {
                var absoluteX = onPath ? null : FiniteOrNull(span.X);
                var absoluteY = onPath ? null : FiniteOrNull(span.Y);
                if ((absoluteX is not null || absoluteY is not null) && i > chunkStart)
                {
                    AnchorChunk(text, result, chunkStart, i, anchor, rightToLeft);
                    chunkStart = i;
                }

                x = absoluteX ?? x;
                y = absoluteY ?? y;
                x += Finite(span.Dx);
                y += Finite(span.Dy);
            }

            previousSpan = item.Span;
            if (i == chunkStart)
            {
                anchor = x;
            }

            var width = result.Width[i];
            result.X[i] = rightToLeft ? x - width : x;
            x = rightToLeft ? x - width : x + width;
            result.Y[i] = y + Finite(item.BaselineOffset) - Finite(span?.BaselineShift ?? 0);
        }

        if (items.Count > 0)
        {
            AnchorChunk(text, result, chunkStart, items.Count, anchor, rightToLeft);
        }

        return result;
    }

    // SVG 2, "Apply anchoring": the extent of the chunk is moved so that its start, its middle or its end is at the
    // position of its first character
    private static void AnchorChunk(SceneText text, Result result, int start, int end, double anchor, bool rightToLeft)
    {
        var (left, right) = (double.PositiveInfinity, double.NegativeInfinity);
        for (var i = start; i < end; i++)
        {
            left = Math.Min(left, Math.Min(result.X[i], result.X[i] + result.Width[i]));
            right = Math.Max(right, Math.Max(result.X[i], result.X[i] + result.Width[i]));
        }

        var shift = (text.Anchor, rightToLeft) switch
        {
            (SceneTextAnchor.Middle, _) => anchor - ((left + right) / 2),
            (SceneTextAnchor.Start, false) or (SceneTextAnchor.End, true) => anchor - left,
            _ => anchor - right,
        };

        if (!double.IsFinite(shift))
            return;

        for (var i = start; i < end; i++)
        {
            result.X[i] += shift;
        }
    }

    /// <summary>
    /// Gets the distance from the alphabetic baseline up to a baseline, from the metrics of a font: the ascent and the
    /// descent are distances (the descent is positive), and an x-height of 0 or less is half the ascent.
    /// </summary>
    public static double GetBaselineOffset(SceneTextBaseline baseline, double ascent, double descent, double xHeight)
    {
        var offset = baseline switch
        {
            SceneTextBaseline.Middle => (xHeight > 0 ? xHeight : ascent / 2) / 2,
            SceneTextBaseline.Central => (ascent - descent) / 2,
            SceneTextBaseline.Hanging => 0.8 * ascent,
            SceneTextBaseline.Mathematical => ascent / 2,
            SceneTextBaseline.Ideographic or SceneTextBaseline.TextBottom => -descent,
            SceneTextBaseline.TextTop => ascent,
            _ => 0,
        };

        return Finite(offset);
    }

    /// <summary>Gets the paint of the glyphs of a span: a paint, or <see langword="null"/> to paint them with the color.</summary>
    public static (ScenePaint? Paint, SceneColor Color) GetFill(SceneText text, SceneTextSpan? span)
    {
        if (span?.Fill is { } spanFill)
            return (spanFill, span.Color ?? text.Color);

        if (span?.Color is { } spanColor)
            return (null, spanColor);

        return (text.Fill, text.Color);
    }

    /// <summary>Gets the paint of the outline of the glyphs of a span, or <see langword="null"/> for none.</summary>
    public static ScenePaint? GetStroke(SceneText text, SceneTextSpan? span) => HasStrokeWidth(text) ? span?.Stroke ?? text.Stroke : null;

    /// <summary>Gets the paint of the lines of a span: a paint, or <see langword="null"/> to paint them with the color.</summary>
    public static (ScenePaint? Paint, SceneColor Color) GetDecorationFill(SceneText text, SceneTextSpan? span)
    {
        if ((span?.DecorationFill ?? text.DecorationFill) is { } decoration)
            return (decoration, text.Color);

        return GetFill(text, span);
    }

    /// <summary>Gets a value that indicates whether two paints, each a paint or a color when the paint is <see langword="null"/>, paint the same way.</summary>
    public static bool IsSamePaint((ScenePaint? Paint, SceneColor Color) first, (ScenePaint? Paint, SceneColor Color) second)
    {
        var firstColor = first.Paint is null ? first.Color : (first.Paint as SolidPaint)?.Color;
        var secondColor = second.Paint is null ? second.Color : (second.Paint as SolidPaint)?.Color;
        if (firstColor is { } a && secondColor is { } b)
            return a == b;

        return ReferenceEquals(first.Paint, second.Paint);
    }

    /// <summary>Gets a value that indicates whether the outline of a text has a width that draws.</summary>
    public static bool HasStrokeWidth(SceneText text) => text.StrokeWidth > 0 && double.IsFinite(text.StrokeWidth);

    /// <summary>Gets a value that indicates whether a text or one of its spans has an outline.</summary>
    public static bool HasStroke(SceneText text) => HasStrokeWidth(text) && (text.Stroke is not null || text.Spans.Any(span => span.Stroke is not null));

    /// <summary>Gets a value that indicates whether the stroke of a text is painted before its fill.</summary>
    public static bool IsStrokeFirst(ScenePaintOrder order) => order is ScenePaintOrder.StrokeFillMarkers or ScenePaintOrder.StrokeMarkersFill or ScenePaintOrder.MarkersStrokeFill;

    /// <summary>Gets the rotation of each character of a span, 0 when it is not finite.</summary>
    public static double GetGlyphRotation(SceneTextSpan? span) => span is null ? 0 : Finite(span.GlyphRotation);

    /// <summary>
    /// Gets a value that indicates whether a text is fitted to its length by its spacing: its length is valid, and it
    /// adjusts the spacing only, or it is on a path, where the glyphs are not scaled.
    /// </summary>
    public static bool IsFittedBySpacing(SceneText text) => text.TextLength is { } length && length >= 0 && double.IsFinite(length) && (text.LengthAdjust is SceneTextLengthAdjust.Spacing || text.Path is not null);

    /// <summary>Gets the grapheme clusters of a text.</summary>
    public static List<string> GetClusters(string text)
    {
        var clusters = new List<string>();
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            clusters.Add(text[range]);
        }

        return clusters;
    }

    /// <summary>Gets the number of grapheme clusters of a text.</summary>
    public static int CountClusters(string text)
    {
        var count = 0;
        foreach (var _ in Unicode.EnumerateGraphemeClusters(text))
        {
            count++;
        }

        return count;
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

    private static double? FiniteOrNull(double? value) => value is { } number && double.IsFinite(number) ? number : null;
}
