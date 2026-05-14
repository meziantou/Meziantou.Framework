namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a barcode as PNG.
/// </summary>
public sealed class BarcodePngOptions
{
    /// <summary>Gets or sets the width of each module in pixels. Default is 2.</summary>
    public int ModuleWidth { get; set; } = 2;

    /// <summary>Gets or sets the height of each module in pixels. Default is 80.</summary>
    public int ModuleHeight { get; set; } = 80;

    /// <summary>Gets or sets the number of quiet zone modules on the left and right sides of the barcode. Default is 10.</summary>
    public int QuietZoneModules { get; set; } = 10;

    /// <summary>Gets or sets the color for dark modules. Default is <see cref="Color.Black"/>.</summary>
    public Color DarkColor { get; set; } = Color.Black;

    /// <summary>Gets or sets the color for light modules. Default is <see cref="Color.White"/>.</summary>
    public Color LightColor { get; set; } = Color.White;
}
