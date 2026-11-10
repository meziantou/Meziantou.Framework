using Meziantou.Framework.Imaging.Typography;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.VectorGraphics;

/// <summary>Measures the texts of a scene with the fonts of <c>Meziantou.Framework.Imaging</c>: the widths are the ones <see cref="TextLayout"/> draws.</summary>
public sealed class ImageTextMeasurer : ITextMeasurer
{
    private readonly Font _regular;
    private readonly Font _bold;
    private readonly Font _italic;
    private readonly Font _boldItalic;

    /// <summary>Initializes a new instance of the <see cref="ImageTextMeasurer"/> class.</summary>
    /// <param name="regular">The font of the regular texts.</param>
    /// <param name="bold">The font of the bold texts, or <see langword="null"/> for <paramref name="regular"/>.</param>
    public ImageTextMeasurer(Font regular, Font? bold = null)
        : this(regular, bold, italic: null, boldItalic: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ImageTextMeasurer"/> class.</summary>
    /// <param name="regular">The font of the regular texts.</param>
    /// <param name="bold">The font of the bold texts, or <see langword="null"/> for <paramref name="regular"/>.</param>
    /// <param name="italic">The font of the italic texts, or <see langword="null"/> for <paramref name="regular"/>.</param>
    /// <param name="boldItalic">The font of the bold italic texts, or <see langword="null"/> for <paramref name="bold"/>, then <paramref name="italic"/>, then <paramref name="regular"/>.</param>
    public ImageTextMeasurer(Font regular, Font? bold, Font? italic, Font? boldItalic)
    {
        ArgumentNullException.ThrowIfNull(regular);
        _regular = regular;
        _bold = bold ?? regular;
        _italic = italic ?? regular;
        _boldItalic = boldItalic ?? bold ?? italic ?? regular;
    }

    /// <summary>Gets the family name of the regular font.</summary>
    public string FontFamily => _regular.FamilyName;

    /// <inheritdoc/>
    public TextMetrics Measure(string text, SceneFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        var face = GetFont(font);
        var metrics = face.GetMetrics(font.Size);
        var width = text.Length is 0 ? 0 : TextLayout.Create(text, face, font.Size).Width;
        return new TextMetrics(width, metrics.Ascent, metrics.Descent);
    }

    internal Font GetFont(SceneFont font)
    {
        return (font.Bold, font.Italic) switch
        {
            (true, true) => _boldItalic,
            (true, false) => _bold,
            (false, true) => _italic,
            _ => _regular,
        };
    }
}
