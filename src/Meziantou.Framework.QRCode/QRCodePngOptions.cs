namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a QR code as PNG.
/// </summary>
public sealed class QRCodePngOptions
{
    /// <summary>Gets or sets the size of each module in pixels. Default is 10.</summary>
    public int ModuleSize { get; set; } = 10;

    /// <summary>Gets or sets the number of quiet zone modules around the QR code. Default is 4.</summary>
    public int QuietZoneModules { get; set; } = 4;

    /// <summary>Gets or sets a value indicating whether dark and light modules are inverted.</summary>
    public bool InvertColors { get; set; }
}
