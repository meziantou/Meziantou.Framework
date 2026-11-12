namespace Meziantou.Framework.VectorGraphics;

/// <summary>Extension methods for <see cref="ITextMeasurer"/>.</summary>
public static class TextMeasurerExtensions
{
    /// <summary>
    /// Returns a measurer whose family is <paramref name="fontFamily"/>: it measures the texts that name no family
    /// (<see cref="SceneFont.Family"/> is <see langword="null"/>) as texts of that family. Use its
    /// <see cref="ITextMeasurer.FontFamily"/> as the family of the scene (<see cref="Scene.FontFamily"/>), so the renderers
    /// draw these texts with the family that measured them.
    /// </summary>
    /// <param name="measurer">The measurer.</param>
    /// <param name="fontFamily">The CSS <c>font-family</c> of the texts, or <see langword="null"/> to keep the family of the measurer.</param>
    /// <returns>The measurer.</returns>
    /// <remarks>
    /// With fonts (<see cref="ITextMeasurer.Fonts"/>), the texts are measured with the first family of the list the fonts
    /// have, or their default family. A measurer without fonts, such as <see cref="AfmTextMeasurer"/>, measures every
    /// family with its own metrics.
    /// </remarks>
    public static ITextMeasurer WithFontFamily(this ITextMeasurer measurer, string? fontFamily)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        if (string.IsNullOrWhiteSpace(fontFamily) || string.Equals(fontFamily, measurer.FontFamily, StringComparison.Ordinal))
            return measurer;

        return new FamilyTextMeasurer(measurer is FamilyTextMeasurer family ? family.Inner : measurer, fontFamily);
    }

    /// <summary>
    /// Measures a line of text with a letter spacing and a word spacing (<see cref="SceneText.LetterSpacing"/>,
    /// <see cref="SceneText.WordSpacing"/>), as the renderers draw it: see
    /// <see cref="ITextMeasurer.Measure(string, SceneFont, double, double)"/>.
    /// </summary>
    /// <param name="measurer">The measurer.</param>
    /// <param name="text">The text, on one line.</param>
    /// <param name="font">The font.</param>
    /// <param name="letterSpacing">The space added after each grapheme cluster, in pixels.</param>
    /// <param name="wordSpacing">The space added to each space (U+0020 and U+00A0), in pixels.</param>
    /// <returns>The dimensions of the text: its width includes the spacing.</returns>
    public static TextMetrics Measure(this ITextMeasurer measurer, string text, SceneFont font, double letterSpacing, double wordSpacing)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        return measurer.Measure(text, font, letterSpacing, wordSpacing);
    }

    /// <summary>Measures a text made of spans: the spans one after the other, on the same baseline.</summary>
    /// <param name="measurer">The measurer.</param>
    /// <param name="spans">The spans.</param>
    /// <param name="font">The font of the spans that set none.</param>
    /// <returns>The sum of the widths of the spans, with their spacing, and the largest ascent and descent.</returns>
    /// <remarks>A span without spacing (<see cref="SceneTextSpan.LetterSpacing"/>, <see cref="SceneTextSpan.WordSpacing"/>) has none: measure a <see cref="SceneText"/> to apply its spacing.</remarks>
    public static TextMetrics Measure(this ITextMeasurer measurer, IEnumerable<SceneTextSpan> spans, SceneFont font)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(spans);
        return MeasureSpans(measurer, spans, font, 0, 0);
    }

    /// <summary>Measures a text as the renderers draw it: its spans one after the other, with their fonts and their spacing.</summary>
    /// <param name="measurer">The measurer.</param>
    /// <param name="text">The text.</param>
    /// <returns>
    /// The sum of the widths of the spans, with their spacing, and the largest ascent and descent. A text fitted to a
    /// length (<see cref="SceneText.TextLength"/>), or whose spans are moved horizontally (<see cref="SceneTextSpan.X"/>,
    /// <see cref="SceneTextSpan.Dx"/>), has the width from the left of its leftmost span to the right of its rightmost one.
    /// </returns>
    public static TextMetrics Measure(this ITextMeasurer measurer, SceneText text)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(text);
        var isPlaced = text.TextLength is not null || text.Spans.Any(span => span.X is not null || span.Dx != 0);
        if (text.Spans.Count is 0 && !isPlaced)
            return measurer.Measure(text.Text, text.Font, text.LetterSpacing, text.WordSpacing);

        if (!isPlaced)
            return MeasureSpans(measurer, text.Spans, text.Font, text.LetterSpacing, text.WordSpacing);

        // The spans are placed as the renderers place them: the extent of their parts
        var (ascent, descent) = (0.0, 0.0);
        var items = new List<TextPlacement.Item>();
        if (text.Spans.Count is 0)
        {
            var metrics = measurer.Measure(text.Text, text.Font, text.LetterSpacing, text.WordSpacing);
            (ascent, descent) = (metrics.Ascent, metrics.Descent);
            items.Add(new TextPlacement.Item(-1, metrics.Width, TextPlacement.CountClusters(text.Text), 0));
        }
        else
        {
            for (var i = 0; i < text.Spans.Count; i++)
            {
                var span = text.Spans[i];
                if (span.Text.Length is 0)
                    continue;

                var metrics = measurer.Measure(span.Text, span.Font ?? text.Font, span.LetterSpacing ?? text.LetterSpacing, span.WordSpacing ?? text.WordSpacing);
                (ascent, descent) = (Math.Max(ascent, metrics.Ascent), Math.Max(descent, metrics.Descent));
                items.Add(new TextPlacement.Item(i, metrics.Width, TextPlacement.CountClusters(span.Text), 0));
            }
        }

        var placement = TextPlacement.Place(text, items);
        var (left, right) = (double.PositiveInfinity, double.NegativeInfinity);
        for (var i = 0; i < items.Count; i++)
        {
            left = Math.Min(left, Math.Min(placement.X[i], placement.X[i] + placement.Width[i]));
            right = Math.Max(right, Math.Max(placement.X[i], placement.X[i] + placement.Width[i]));
        }

        return new TextMetrics(items.Count is 0 || !double.IsFinite(right - left) ? 0 : right - left, ascent, descent);
    }

    private static TextMetrics MeasureSpans(ITextMeasurer measurer, IEnumerable<SceneTextSpan> spans, SceneFont font, double letterSpacing, double wordSpacing)
    {
        var (width, ascent, descent) = (0.0, 0.0, 0.0);
        foreach (var span in spans)
        {
            var metrics = measurer.Measure(span.Text, span.Font ?? font, span.LetterSpacing ?? letterSpacing, span.WordSpacing ?? wordSpacing);
            width += metrics.Width;
            ascent = Math.Max(ascent, metrics.Ascent);
            descent = Math.Max(descent, metrics.Descent);
        }

        return new TextMetrics(width, ascent, descent);
    }

    private sealed class FamilyTextMeasurer(ITextMeasurer inner, string fontFamily) : ITextMeasurer
    {
        public ITextMeasurer Inner { get; } = inner;

        public string FontFamily { get; } = fontFamily;

        public SceneFontCollection? Fonts => Inner.Fonts;

        public TextMetrics Measure(string text, SceneFont font) => Inner.Measure(text, font.Family is null ? font with { Family = FontFamily } : font);

        public TextMetrics Measure(string text, SceneFont font, double letterSpacing, double wordSpacing) => Inner.Measure(text, font.Family is null ? font with { Family = FontFamily } : font, letterSpacing, wordSpacing);
    }
}
