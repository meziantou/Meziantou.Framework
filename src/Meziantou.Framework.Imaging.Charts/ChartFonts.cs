using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Charts;

/// <summary>
/// Liberation Sans, the fonts of the charts the package draws: their widths are those of Helvetica and Arial, so a
/// chart drawn as an image lays out like its SVG.
/// </summary>
/// <remarks>
/// The fonts are Liberation Sans 2.1.5 (SIL Open Font License 1.1), subset to the Latin characters. Pass another font
/// with <see cref="ChartImageOptions.Font"/> to draw other scripts.
/// </remarks>
public static class ChartFonts
{
    private static readonly Lazy<Font> SansFont = new(() => Load("LiberationSans-Regular.woff2"));
    private static readonly Lazy<Font> SansBoldFont = new(() => Load("LiberationSans-Bold.woff2"));

    /// <summary>Gets Liberation Sans Regular.</summary>
    public static Font Sans => SansFont.Value;

    /// <summary>Gets Liberation Sans Bold.</summary>
    public static Font SansBold => SansBoldFont.Value;

    private static Font Load(string fileName)
    {
        using var stream = typeof(ChartFonts).Assembly.GetManifestResourceStream("Meziantou.Framework.Imaging.Charts.Fonts." + fileName)
            ?? throw new InvalidOperationException($"The font '{fileName}' is not embedded in the assembly.");
        return Font.Load(stream);
    }
}
