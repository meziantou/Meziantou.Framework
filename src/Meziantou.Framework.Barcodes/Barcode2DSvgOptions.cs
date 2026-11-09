namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for rendering a two-dimensional barcode as SVG.
/// </summary>
public sealed class Barcode2DSvgOptions
{
    /// <summary>Gets or sets the size of each module in SVG units. Default is 4.</summary>
    public int ModuleSize { get; set; } = 4;

    /// <summary>Gets or sets the number of quiet zone modules around the barcode. Default is 2, the largest minimum of the supported symbologies (PDF417).</summary>
    public int QuietZoneModules { get; set; } = 2;

    /// <summary>Gets or sets the color for dark modules. Default is <see cref="Color.Black"/>.</summary>
    public Color DarkColor { get; set; } = Color.Black;

    /// <summary>Gets or sets the color for light modules. Default is <see cref="Color.White"/>.</summary>
    public Color LightColor { get; set; } = Color.White;
}
