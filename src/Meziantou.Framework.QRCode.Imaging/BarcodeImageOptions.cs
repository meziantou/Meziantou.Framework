using Meziantou.Framework.Imaging;

namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a barcode as an <see cref="Image{TPixel}"/>.
/// </summary>
public sealed class BarcodeImageOptions
{
    /// <summary>Gets or sets the width of each module in pixels. Default is 2.</summary>
    public int ModuleWidth { get; set; } = 2;

    /// <summary>Gets or sets the height of each module in pixels. Default is 80.</summary>
    public int ModuleHeight { get; set; } = 80;

    /// <summary>Gets or sets the number of quiet zone modules on the left and right sides of the barcode. Default is 10.</summary>
    public int QuietZoneModules { get; set; } = 10;

    /// <summary>Gets or sets the color for dark modules. Default is <see cref="Colors.Black"/>.</summary>
    public Rgba32 DarkColor { get; set; } = Colors.Black;

    /// <summary>Gets or sets the color for light modules and the quiet zone. Default is <see cref="Colors.White"/>.</summary>
    public Rgba32 LightColor { get; set; } = Colors.White;
}
