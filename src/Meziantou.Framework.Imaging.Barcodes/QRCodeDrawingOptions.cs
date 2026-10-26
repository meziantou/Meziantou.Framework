using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Options for drawing a QR code on a <see cref="DrawingCanvas"/>.
/// </summary>
/// <remarks>The sizes are in the units of the canvas: pixels, unless a transform is applied.</remarks>
public sealed class QRCodeDrawingOptions
{
    /// <summary>Gets or sets the size of each module. Default is 10.</summary>
    public double ModuleSize { get; set; } = 10;

    /// <summary>Gets or sets the number of quiet zone modules around the QR code. Default is 4.</summary>
    public int QuietZoneModules { get; set; } = 4;

    /// <summary>Gets or sets the brush of the dark modules. Default is a black <see cref="SolidBrush"/>.</summary>
    /// <remarks>The brush is in the coordinates of the canvas: a gradient spans the canvas, not the QR code.</remarks>
    public Brush DarkBrush { get; set; } = new SolidBrush(Colors.Black);

    /// <summary>Gets or sets the brush of the light modules and of the quiet zone. Default is a white <see cref="SolidBrush"/>.</summary>
    /// <remarks><see langword="null"/> leaves the pixels under the light modules and the quiet zone unchanged.</remarks>
    public Brush? LightBrush { get; set; } = new SolidBrush(Colors.White);

    /// <summary>Gets or sets a value indicating whether the module size and the position are rounded to whole units. Default is <see langword="true"/>.</summary>
    /// <remarks>
    /// Rounding gives every module the same size and sharp edges, which scanners read best. The module size rounds to
    /// the nearest whole number, at least 1. Set <see langword="false"/> to draw at the exact size and position.
    /// </remarks>
    public bool SnapToPixels { get; set; } = true;
}
