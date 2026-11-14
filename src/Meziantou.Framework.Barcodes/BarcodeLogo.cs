namespace Meziantou.Framework.Barcodes;

/// <summary>
/// A logo drawn over the center of a QR code (<see cref="QRCodeRenderOptions.Logo"/>).
/// </summary>
/// <remarks>
/// The logo hides the modules under it: the error correction of the QR code must recover them, so create the QR code
/// with <see cref="ErrorCorrectionLevel.H"/> and keep the logo small. The image keeps its proportions and is centered
/// in a square of <see cref="SizePercent"/> of the side of the drawing.
/// </remarks>
public sealed class BarcodeLogo
{
    /// <summary>Initializes a new instance of the <see cref="BarcodeLogo"/> class.</summary>
    /// <param name="data">The bytes of a PNG, JPEG, GIF or WebP file.</param>
    public BarcodeLogo(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    /// <summary>Gets the bytes of the image: a PNG, JPEG, GIF or WebP file.</summary>
    /// <remarks>
    /// SVG output embeds the bytes as a data URI, and image and PDF output decode them, so every output draws the same
    /// logo. Rendering throws an <see cref="ArgumentException"/> when the data is not one of these formats.
    /// </remarks>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets or sets the size of the logo, as a percentage of the smallest side of the drawing, quiet zone included. Default is 20.</summary>
    /// <remarks>Valid values are between 1 and 100.</remarks>
    public int SizePercent { get; set; } = 20;
}
