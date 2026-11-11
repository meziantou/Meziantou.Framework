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
    /// Gets the fonts the text is measured with, or <see langword="null"/> when the measurer does not have font files (for
    /// instance, the metrics of <see cref="AfmTextMeasurer"/>). A chart or a diagram sets them as the fonts of its scene
    /// (<see cref="Scene.Fonts"/>), so the renderers draw the text with the fonts that measured it.
    /// </summary>
    SceneFontCollection? Fonts => null;
}
