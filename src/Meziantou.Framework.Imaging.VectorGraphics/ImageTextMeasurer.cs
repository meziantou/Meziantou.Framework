using Meziantou.Framework.Imaging.Typography;
using Meziantou.Framework.Imaging.VectorGraphics.Internal;
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

    /// <summary>Initializes a new instance of the <see cref="ImageTextMeasurer"/> class that measures the texts with the fonts of a collection.</summary>
    /// <param name="fonts">The fonts. A chart or a diagram sets them as the fonts of its scene, so the renderers draw the texts with them.</param>
    /// <remarks>
    /// The family of a text is <see cref="SceneFont.Family"/>, or <see cref="SceneFontCollection.DefaultFamily"/>. The
    /// characters its face does not have are measured with the face that draws them. The ascent and the descent are the
    /// ones of the face of the family.
    /// </remarks>
    public ImageTextMeasurer(SceneFontCollection fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
        _regular = _bold = _italic = _boldItalic = SceneFontCache.GetStack(fonts, new SceneFont(1), defaultFamily: null).Primary;
    }

    /// <summary>Gets the family of the texts: the family name of the regular font, or the families of <see cref="Fonts"/>.</summary>
    public string FontFamily => Fonts?.FontFamily ?? _regular.FamilyName;

    /// <summary>Gets the fonts the texts are measured with, or <see langword="null"/> when the measurer was created from fonts of <c>Meziantou.Framework.Imaging</c>.</summary>
    public SceneFontCollection? Fonts { get; }

    /// <inheritdoc/>
    public TextMetrics Measure(string text, SceneFont font) => Measure(text, font, 0, 0);

    /// <inheritdoc/>
    /// <remarks>The width is the one of the <see cref="TextLayout"/> the image renderer draws: without the optional ligatures with a letter spacing, and with the spacing after each cluster of glyphs.</remarks>
    public TextMetrics Measure(string text, SceneFont font, double letterSpacing, double wordSpacing)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spacing = new SceneRenderer.Spacing(letterSpacing, wordSpacing);
        if (Fonts is not null)
        {
            var stack = SceneFontCache.GetStack(Fonts, font, defaultFamily: null);
            var primaryMetrics = stack.Primary.GetMetrics(font.Size);
            var layoutWidth = text.Length is 0 ? 0 : TextLayout.Create(text, SceneRenderer.CreateStyle(stack, font.Size, TextDecorations.None, spacing)).Width;
            return new TextMetrics(layoutWidth, primaryMetrics.Ascent, primaryMetrics.Descent);
        }

        var face = GetFont(font);
        var metrics = face.GetMetrics(font.Size);
        var width = text.Length is 0 ? 0 : spacing.IsNone ? TextLayout.Create(text, face, font.Size).Width : TextLayout.Create(text, SceneRenderer.CreateStyle(face, font.Size, TextDecorations.None, spacing)).Width;
        return new TextMetrics(width, metrics.Ascent, metrics.Descent);
    }

    internal Font GetFont(SceneFont font)
    {
        return (font.IsBold, font.Italic) switch
        {
            (true, true) => _boldItalic,
            (true, false) => _bold,
            (false, true) => _italic,
            _ => _regular,
        };
    }
}
