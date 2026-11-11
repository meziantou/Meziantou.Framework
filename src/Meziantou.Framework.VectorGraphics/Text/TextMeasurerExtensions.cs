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

    private sealed class FamilyTextMeasurer(ITextMeasurer inner, string fontFamily) : ITextMeasurer
    {
        public ITextMeasurer Inner { get; } = inner;

        public string FontFamily { get; } = fontFamily;

        public SceneFontCollection? Fonts => Inner.Fonts;

        public TextMetrics Measure(string text, SceneFont font) => Inner.Measure(text, font.Family is null ? font with { Family = FontFamily } : font);
    }
}
