namespace Meziantou.Framework.VectorGraphics;

/// <summary>Measures text, so the layout of a chart or a diagram (margins, label rotation, truncation, wrapping) fits the font that draws it.</summary>
public interface ITextMeasurer
{
    /// <summary>Gets the font family the measured text is drawn with, as a CSS <c>font-family</c> value.</summary>
    string FontFamily { get; }

    /// <summary>Measures a line of text.</summary>
    /// <param name="text">The text, on one line.</param>
    /// <param name="font">The size, the weight and the style of the font.</param>
    /// <returns>The dimensions of the text.</returns>
    TextMetrics Measure(string text, SceneFont font);

    /// <summary>
    /// Measures a line of text with a letter spacing and a word spacing (<see cref="SceneText.LetterSpacing"/>,
    /// <see cref="SceneText.WordSpacing"/>), as the renderers draw it.
    /// </summary>
    /// <param name="text">The text, on one line.</param>
    /// <param name="font">The size, the weight and the style of the font.</param>
    /// <param name="letterSpacing">The space added after each character, in pixels.</param>
    /// <param name="wordSpacing">The space added after each space (U+0020 and U+00A0), in pixels.</param>
    /// <returns>The dimensions of the text: its width includes the spacing.</returns>
    /// <remarks>
    /// The default implementation adds the spacing after each grapheme cluster to <see cref="Measure(string, SceneFont)"/>.
    /// The measurers that shape the text measure it as the renderers draw it with a letter spacing: without the optional
    /// ligatures, and with the spacing after each cluster of glyphs.
    /// </remarks>
    TextMetrics Measure(string text, SceneFont font, double letterSpacing, double wordSpacing)
    {
        ArgumentNullException.ThrowIfNull(text);
        var metrics = Measure(text, font);
        return metrics with { Width = metrics.Width + TextSpacing.GetWidth(text, letterSpacing, wordSpacing) };
    }

    /// <summary>
    /// Gets the fonts the text is measured with, or <see langword="null"/> when the measurer does not have font files (for
    /// instance, the metrics of <see cref="AfmTextMeasurer"/>). A chart or a diagram sets them as the fonts of its scene
    /// (<see cref="Scene.Fonts"/>), so the renderers draw the text with the fonts that measured it.
    /// </summary>
    SceneFontCollection? Fonts => null;
}
