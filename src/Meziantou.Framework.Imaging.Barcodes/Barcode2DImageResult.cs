using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>Represents a Data Matrix, PDF417 or Aztec symbol found in an image: its content and where it is.</summary>
public sealed class Barcode2DImageResult
{
    internal Barcode2DImageResult(Barcode2DDecodeResult result, IReadOnlyList<PointD> corners)
    {
        Result = result;
        Corners = corners;
    }

    /// <summary>Gets the content of the symbol.</summary>
    public Barcode2DDecodeResult Result { get; }

    /// <summary>Gets the text of the symbol: <see cref="Barcode2DDecodeResult.Text"/>.</summary>
    public string Text => Result.Text;

    /// <summary>
    /// Gets the corners of the symbol in the image, in pixels, quiet zone excluded: clockwise in the image, from the corner
    /// nearest to its top-left.
    /// </summary>
    public IReadOnlyList<PointD> Corners { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
