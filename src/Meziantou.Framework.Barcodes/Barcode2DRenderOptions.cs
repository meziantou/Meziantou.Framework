using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for rendering a two-dimensional barcode (Data Matrix, PDF417, Aztec): as a scene, as SVG, and as an image or
/// a PDF with the bridge packages.
/// </summary>
/// <remarks>The sizes are in the units of the scene: pixels in an image at the default scale, CSS pixels in SVG.</remarks>
public sealed class Barcode2DRenderOptions
{
    /// <summary>Gets or sets the size of each module. Default is 4.</summary>
    public double ModuleSize { get; set; } = 4;

    /// <summary>Gets or sets the number of quiet zone modules around the symbol. Default is 2.</summary>
    public int QuietZoneModules { get; set; } = 2;

    /// <summary>Gets or sets the paint of the dark modules. Default is black.</summary>
    /// <remarks>A gradient is in the coordinates of the scene: (0, 0) is the top-left corner of the quiet zone.</remarks>
    public ScenePaint DarkPaint { get; set; } = SceneColor.Black;

    /// <summary>Gets or sets the paint of the light modules and of the quiet zone. Default is white.</summary>
    /// <remarks><see langword="null"/> draws no background: it is transparent, or the content under the drawing shows through.</remarks>
    public ScenePaint? LightPaint { get; set; } = SceneColor.White;

    /// <summary>Gets or sets a value indicating whether the module size and the positions are rounded to whole units. Default is <see langword="true"/>.</summary>
    /// <remarks>
    /// Rounding gives every module the same size and sharp edges in images, which scanners read best. The module size
    /// rounds to the nearest whole number, at least 1. Set <see langword="false"/> to draw at the exact size.
    /// </remarks>
    public bool SnapToPixels { get; set; } = true;
}
