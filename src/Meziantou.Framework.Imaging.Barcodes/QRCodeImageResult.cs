using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>Represents a QR code found in an image: its content and where it is.</summary>
public sealed class QRCodeImageResult
{
    internal QRCodeImageResult(QRCodeDecodeResult result, IReadOnlyList<PointD> corners)
    {
        Result = result;
        Corners = corners;
    }

    /// <summary>Gets the content of the QR code.</summary>
    public QRCodeDecodeResult Result { get; }

    /// <summary>Gets the text of the QR code: <see cref="QRCodeDecodeResult.Text"/>.</summary>
    public string Text => Result.Text;

    /// <summary>
    /// Gets the corners of the symbol in the image, in pixels, quiet zone excluded: its top-left, top-right, bottom-right and
    /// bottom-left corners, in the orientation of the symbol.
    /// </summary>
    public IReadOnlyList<PointD> Corners { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
