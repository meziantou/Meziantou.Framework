using Meziantou.Framework.Imaging.Typography;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.VectorGraphics;

/// <summary>The options of <see cref="SceneImageRenderer"/>.</summary>
public sealed class SceneImageOptions
{
    private double _scale = 1;

    /// <summary>Gets or sets the number of pixels per unit of the drawing: 2 draws a drawing of 640 by 400 as an image of 1280 by 800, for high-density screens. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Scale
    {
        get => _scale;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scale must be a positive finite number.");

            _scale = value;
        }
    }

    /// <summary>
    /// Gets or sets the fonts of the texts, or <see langword="null"/> to use <see cref="Font"/>, <see cref="BoldFont"/>,
    /// <see cref="ItalicFont"/> and <see cref="BoldItalicFont"/>. The layout measures the texts with them, and each text is
    /// drawn with the face of its family (<see cref="SceneFont.Family"/>). The fonts of a scene (<see cref="Scene.Fonts"/>)
    /// take precedence over them.
    /// </summary>
    public SceneFontCollection? Fonts { get; set; }

    /// <summary>Gets or sets the font of the regular texts, or <see langword="null"/> for <see cref="SceneFonts.Sans"/>. The layout measures the texts with it.</summary>
    public Font? Font { get; set; }

    /// <summary>Gets or sets the font of the bold texts (the titles), or <see langword="null"/> for <see cref="SceneFonts.SansBold"/> when <see cref="Font"/> is not set, and <see cref="Font"/> otherwise.</summary>
    public Font? BoldFont { get; set; }

    /// <summary>Gets or sets the font of the italic texts, or <see langword="null"/> for <see cref="SceneFonts.SansItalic"/> when <see cref="Font"/> is not set, and <see cref="Font"/> otherwise.</summary>
    public Font? ItalicFont { get; set; }

    /// <summary>
    /// Gets or sets the font of the bold italic texts, or <see langword="null"/> for <see cref="SceneFonts.SansBoldItalic"/>
    /// when <see cref="Font"/> is not set, and otherwise <see cref="BoldFont"/>, then <see cref="ItalicFont"/>, then <see cref="Font"/>.
    /// </summary>
    public Font? BoldItalicFont { get; set; }

    internal Font GetFont(SceneFont font)
    {
        return (font.IsBold, font.Italic) switch
        {
            (false, false) => Font ?? SceneFonts.Sans,
            (true, false) => BoldFont ?? (Font is null ? SceneFonts.SansBold : Font),
            (false, true) => ItalicFont ?? (Font is null ? SceneFonts.SansItalic : Font),
            _ => BoldItalicFont ?? (Font is null ? SceneFonts.SansBoldItalic : BoldFont ?? ItalicFont ?? Font),
        };
    }
}
