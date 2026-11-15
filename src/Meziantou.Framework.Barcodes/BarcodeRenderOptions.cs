using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for rendering a linear barcode: as a scene, as SVG, and as an image or a PDF with the bridge packages.
/// </summary>
/// <remarks>The sizes are in the units of the scene: pixels in an image at the default scale, CSS pixels in SVG.</remarks>
public sealed class BarcodeRenderOptions
{
    /// <summary>Gets or sets the width of each module. Default is 2.</summary>
    public double ModuleWidth { get; set; } = 2;

    /// <summary>Gets or sets the height of each module, which is the height of the bars. Default is 80.</summary>
    /// <remarks>The rows of a stacked barcode (<see cref="Barcode.IsStacked"/>) are <see cref="ModuleWidth"/> high instead.</remarks>
    public double ModuleHeight { get; set; } = 80;

    /// <summary>Gets or sets the number of quiet zone modules on the left and right sides of the barcode. Default is 10.</summary>
    public int QuietZoneModules { get; set; } = 10;

    /// <summary>Gets or sets the paint of the bars. Default is black.</summary>
    /// <remarks>A gradient is in the coordinates of the scene: (0, 0) is the top-left corner of the quiet zone.</remarks>
    public ScenePaint DarkPaint { get; set; } = SceneColor.Black;

    /// <summary>Gets or sets the paint of the spaces, of the quiet zone and of the background of the text. Default is white.</summary>
    /// <remarks><see langword="null"/> draws no background: it is transparent, or the content under the drawing shows through.</remarks>
    public ScenePaint? LightPaint { get; set; } = SceneColor.White;

    /// <summary>Gets or sets a value indicating whether the module sizes and the positions are rounded to whole units. Default is <see langword="true"/>.</summary>
    /// <remarks>
    /// Rounding gives every bar of one width the same width and sharp edges in images, which scanners read best. The module
    /// sizes round to the nearest whole number, at least 1. Set <see langword="false"/> to draw at the exact sizes.
    /// </remarks>
    public bool SnapToPixels { get; set; } = true;

    /// <summary>Gets or sets the style of the human-readable text (<see cref="Barcode.Text"/>) printed under the bars. Default is <see langword="null"/>: no text.</summary>
    /// <remarks>
    /// <para>
    /// EAN-8, EAN-13, UPC-A and UPC-E print their digits as the GS1 General Specifications do: the guard bars extend 5 modules
    /// below the other bars, the digits are grouped between them, the digits that have no bars of their own are printed
    /// outside the bars (widening the quiet zone when it is too narrow), and the digits of an add-on are printed above its
    /// bars, which start lower. The font shrinks when a group is wider than the bars it is printed under.
    /// </para>
    /// <para>
    /// The other symbologies print the text centered under the bars. A text wider than the barcode widens the drawing, and
    /// the barcode is centered above it.
    /// </para>
    /// </remarks>
    public BarcodeTextOptions? Text { get; set; }
}
