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

    /// <summary>Gets or sets the color for dark modules. Default is <see cref="Color.Black"/>.</summary>
    public Color DarkColor { get; set; } = Color.Black;

    /// <summary>Gets or sets the color for light modules. Default is <see cref="Color.White"/>.</summary>
    public Color LightColor { get; set; } = Color.White;

    /// <summary>Gets or sets the image source for a centered logo in the SVG output (for example, a URL or a data URI).</summary>
    /// <remarks>Logo rendering is only supported by the SVG renderer.</remarks>
    public string? LogoImageHref { get; set; }

    /// <summary>Gets or sets the logo size as a percentage of the smallest SVG side. Default is 20.</summary>
    /// <remarks>Used only when <see cref="LogoImageHref"/> is set. Valid values are between 1 and 100.</remarks>
    public int LogoSizePercent { get; set; } = 20;
}
