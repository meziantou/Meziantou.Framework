namespace Meziantou.Framework.Charts;

/// <summary>Measures text, so the layout of a chart (margins, label rotation, truncation, legend wrapping) fits the font that draws it.</summary>
public interface ITextMeasurer
{
    /// <summary>Gets the font family the measured text is drawn with, as a CSS <c>font-family</c> value.</summary>
    string FontFamily { get; }

    /// <summary>Measures a line of text.</summary>
    /// <param name="text">The text, on one line.</param>
    /// <param name="font">The size and the weight of the font.</param>
    /// <returns>The dimensions of the text.</returns>
    TextMetrics Measure(string text, ChartFont font);
}
