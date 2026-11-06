using Meziantou.Framework.Charts;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Charts;

/// <summary>Measures the texts of a chart with the fonts of <c>Meziantou.Framework.Imaging</c>: the widths are the ones <see cref="TextLayout"/> draws.</summary>
public sealed class ImageTextMeasurer : ITextMeasurer
{
    private readonly Font _regular;
    private readonly Font _bold;

    /// <summary>Initializes a new instance of the <see cref="ImageTextMeasurer"/> class.</summary>
    /// <param name="regular">The font of the regular texts.</param>
    /// <param name="bold">The font of the bold texts, or <see langword="null"/> for <paramref name="regular"/>.</param>
    public ImageTextMeasurer(Font regular, Font? bold = null)
    {
        ArgumentNullException.ThrowIfNull(regular);
        _regular = regular;
        _bold = bold ?? regular;
    }

    /// <summary>Gets the family name of the regular font.</summary>
    public string FontFamily => _regular.FamilyName;

    /// <inheritdoc/>
    public TextMetrics Measure(string text, ChartFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        var face = GetFont(font);
        var metrics = face.GetMetrics(font.Size);
        var width = text.Length is 0 ? 0 : TextLayout.Create(text, face, font.Size).Width;
        return new TextMetrics(width, metrics.Ascent, metrics.Descent);
    }

    internal Font GetFont(ChartFont font) => font.Bold ? _bold : _regular;
}
