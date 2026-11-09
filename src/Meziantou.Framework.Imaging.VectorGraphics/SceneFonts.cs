using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.VectorGraphics;

/// <summary>
/// Liberation Sans, the fonts of the scenes the package draws: their widths are those of Helvetica and Arial, so a
/// chart or a diagram drawn as an image lays out like its SVG.
/// </summary>
/// <remarks>
/// The fonts are Liberation Sans 2.1.5 (SIL Open Font License 1.1), subset to the Latin characters. Pass other fonts
/// with <see cref="SceneImageOptions.Font"/> to draw other scripts.
/// </remarks>
public static class SceneFonts
{
    private static readonly Lazy<Font> SansFont = new(() => Load("LiberationSans-Regular.woff2"));
    private static readonly Lazy<Font> SansBoldFont = new(() => Load("LiberationSans-Bold.woff2"));
    private static readonly Lazy<Font> SansItalicFont = new(() => Load("LiberationSans-Italic.woff2"));
    private static readonly Lazy<Font> SansBoldItalicFont = new(() => Load("LiberationSans-BoldItalic.woff2"));

    /// <summary>Gets Liberation Sans Regular.</summary>
    public static Font Sans => SansFont.Value;

    /// <summary>Gets Liberation Sans Bold.</summary>
    public static Font SansBold => SansBoldFont.Value;

    /// <summary>Gets Liberation Sans Italic.</summary>
    public static Font SansItalic => SansItalicFont.Value;

    /// <summary>Gets Liberation Sans Bold Italic.</summary>
    public static Font SansBoldItalic => SansBoldItalicFont.Value;

    private static Font Load(string fileName)
    {
        using var stream = typeof(SceneFonts).Assembly.GetManifestResourceStream("Meziantou.Framework.Imaging.VectorGraphics.Fonts." + fileName)
            ?? throw new InvalidOperationException($"The font '{fileName}' is not embedded in the assembly.");
        return Font.Load(stream);
    }
}
