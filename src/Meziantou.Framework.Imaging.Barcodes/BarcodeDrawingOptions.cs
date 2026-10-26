using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Options for drawing a barcode on a <see cref="DrawingCanvas"/>.
/// </summary>
/// <remarks>The sizes are in the units of the canvas: pixels, unless a transform is applied.</remarks>
public sealed class BarcodeDrawingOptions
{
    /// <summary>Gets or sets the width of each module. Default is 2.</summary>
    public double ModuleWidth { get; set; } = 2;

    /// <summary>Gets or sets the height of each module, which is the height of the bars. Default is 80.</summary>
    public double ModuleHeight { get; set; } = 80;

    /// <summary>Gets or sets the number of quiet zone modules on the left and right sides of the barcode. Default is 10.</summary>
    public int QuietZoneModules { get; set; } = 10;

    /// <summary>Gets or sets the brush of the bars. Default is a black <see cref="SolidBrush"/>.</summary>
    /// <remarks>The brush is in the coordinates of the canvas: a gradient spans the canvas, not the barcode.</remarks>
    public Brush DarkBrush { get; set; } = new SolidBrush(Colors.Black);

    /// <summary>Gets or sets the brush of the spaces, of the quiet zone and of the background of the text. Default is a white <see cref="SolidBrush"/>.</summary>
    /// <remarks><see langword="null"/> leaves the pixels under the spaces, the quiet zone and the text unchanged.</remarks>
    public Brush? LightBrush { get; set; } = new SolidBrush(Colors.White);

    /// <summary>Gets or sets a value indicating whether the module sizes and the positions are rounded to whole units. Default is <see langword="true"/>.</summary>
    /// <remarks>
    /// Rounding gives every bar of one width the same width and sharp edges, which scanners read best. The module sizes
    /// round to the nearest whole number, at least 1. Set <see langword="false"/> to draw at the exact sizes and position.
    /// </remarks>
    public bool SnapToPixels { get; set; } = true;

    /// <summary>Gets or sets the font of the human-readable text (<see cref="Barcode.Text"/>) printed under the bars. Default is <see langword="null"/>: no text.</summary>
    /// <remarks><see cref="BarcodeFonts.OcrB"/> is the font usually printed under EAN and UPC barcodes.</remarks>
    public Font? Font { get; set; }

    /// <summary>Gets or sets the size of the font, in units per em. Default is 14.</summary>
    public double FontSize { get; set; } = 14;

    /// <summary>Gets or sets the brush of the text. Default is <see langword="null"/>: the text is painted with <see cref="DarkBrush"/>.</summary>
    public Brush? TextBrush { get; set; }

    /// <summary>Gets or sets the space between the bottom of the bars and the top of the text. Default is 2.</summary>
    public double TextSpacing { get; set; } = 2;
}
