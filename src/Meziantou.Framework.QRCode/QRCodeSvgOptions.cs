namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a QR code as SVG.
/// </summary>
public sealed class QRCodeSvgOptions
{
    /// <summary>Gets or sets the size of each module in SVG units. Default is 10.</summary>
    public int ModuleSize { get; set; } = 10;

    /// <summary>Gets or sets the number of quiet zone modules around the QR code. Default is 4.</summary>
    public int QuietZoneModules { get; set; } = 4;

    /// <summary>Gets or sets the color for dark modules. Default is "#000000".</summary>
    public string DarkColor { get; set; } = "#000000";

    /// <summary>Gets or sets the color for light modules. Default is "#ffffff".</summary>
    public string LightColor { get; set; } = "#ffffff";
}
