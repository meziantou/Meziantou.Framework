using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>Represents a linear barcode found in an image: its content and where it is.</summary>
public sealed class BarcodeImageResult
{
    internal BarcodeImageResult(BarcodeDecodeResult result, IReadOnlyList<PointD> corners)
    {
        Result = result;
        Corners = corners;
    }

    /// <summary>Gets the content of the barcode.</summary>
    public BarcodeDecodeResult Result { get; }

    /// <summary>Gets the text of the barcode: <see cref="BarcodeDecodeResult.Text"/>.</summary>
    public string Text => Result.Text;

    /// <summary>
    /// Gets the corners of the part of the barcode that the scan lines crossed, in pixels: clockwise in the image, from
    /// the corner nearest to its top-left. A barcode crossed by a single line has a zero height.
    /// </summary>
    public IReadOnlyList<PointD> Corners { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
