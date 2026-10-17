using Meziantou.Framework.Imaging;

namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a QR code as an <see cref="Image{TPixel}"/>.
/// </summary>
public sealed class QRCodeImageOptions
{
    /// <summary>Gets or sets the size of each module in pixels. Default is 10.</summary>
    public int ModuleSize { get; set; } = 10;

    /// <summary>Gets or sets the number of quiet zone modules around the QR code. Default is 4.</summary>
    public int QuietZoneModules { get; set; } = 4;

    /// <summary>Gets or sets the color for dark modules. Default is <see cref="Colors.Black"/>.</summary>
    public Rgba32 DarkColor { get; set; } = Colors.Black;

    /// <summary>Gets or sets the color for light modules and the quiet zone. Default is <see cref="Colors.White"/>.</summary>
    public Rgba32 LightColor { get; set; } = Colors.White;
}
