namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a barcode as SVG.
/// </summary>
public sealed class BarcodeSvgOptions
{
    /// <summary>Gets or sets the width of each module in SVG units. Default is 2.</summary>
    public int ModuleWidth { get; set; } = 2;

    /// <summary>Gets or sets the height of each module in SVG units. Default is 80.</summary>
    public int ModuleHeight { get; set; } = 80;

    /// <summary>Gets or sets the number of quiet zone modules on the left and right sides of the barcode. Default is 10.</summary>
    public int QuietZoneModules { get; set; } = 10;

    /// <summary>Gets or sets the color for dark modules. Default is "#000000".</summary>
    public string DarkColor { get; set; } = "#000000";

    /// <summary>Gets or sets the color for light modules. Default is "#ffffff".</summary>
    public string LightColor { get; set; } = "#ffffff";
}
