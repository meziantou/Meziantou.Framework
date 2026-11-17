using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// The style of the human-readable text (<see cref="Barcode.Text"/>) printed under the bars of a barcode
/// (<see cref="BarcodeRenderOptions.Text"/>).
/// </summary>
/// <remarks>
/// The text is measured with the metrics of <see cref="Fonts"/>, and the fonts become the fonts of the scene
/// (<see cref="Scene.Fonts"/>), so the SVG, image and PDF renderers draw it with the fonts that measured it. Set
/// <see cref="SvgRenderOptions.EmbedFonts"/> to embed the font in an SVG document. The text is one line: its control
/// characters (such as the new lines a Code 128 barcode can encode) are drawn as <c>?</c>.
/// </remarks>
public sealed class BarcodeTextOptions
{
    /// <summary>Gets or sets the fonts of the text. Default is <see cref="BarcodeFonts.OcrB"/>, the font usually printed under EAN and UPC barcodes.</summary>
    public SceneFontCollection Fonts { get; set; } = BarcodeFonts.OcrB;

    /// <summary>Gets or sets the size of the font, in units per em. Default is 14.</summary>
    /// <remarks>The digits of EAN and UPC barcodes are smaller when they do not fit between the guard bars at this size.</remarks>
    public double FontSize { get; set; } = 14;

    /// <summary>Gets or sets the paint of the text. Default is <see langword="null"/>: the text is painted with <see cref="BarcodeRenderOptions.DarkPaint"/>.</summary>
    public ScenePaint? Paint { get; set; }

    /// <summary>Gets or sets the space between the bottom of the bars and the top of the text. Default is 2.</summary>
    /// <remarks>
    /// For EAN and UPC barcodes, it is the space between the bottom of the data bars (the guard bars are longer) and the
    /// digits, and between the digits of the add-on and its bars.
    /// </remarks>
    public double Spacing { get; set; } = 2;
}
