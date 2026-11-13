using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Barcodes.Internal;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to read the linear barcodes of an image: Code 39, Code 93, Code 128, GS1-128, EAN, UPC, Codabar, ITF
/// and GS1 DataBar.
/// </summary>
public static class BarcodeImageReader
{
    /// <summary>Reads the linear barcodes of an image.</summary>
    /// <param name="image">The image. For an animated image, its first frame is read.</param>
    /// <param name="options">The reading options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the reading.</param>
    /// <returns>The barcodes found, each once; empty when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The reader is made for clean images: generated, printed or scanned barcodes, photographed straight on. Parallel scan
    /// lines cross the image horizontally, vertically and at 30°, 60°, 120° and 150°, and
    /// <see cref="BarcodeDecoder.DecodeScanLines"/> reads the barcodes they cross: a barcode turned by another angle is read
    /// when one of the lines crosses all its bars. Code 39, Codabar and ITF, which have no check character, must be crossed
    /// by two lines that agree. A transparent background reads as white.
    /// </para>
    /// <para>
    /// A barcode needs its quiet zones and narrow bars of at least 2 pixels. The pixels are classified as dark or light
    /// with the Otsu threshold of the image, then with a local threshold (Sauvola) when no barcode is found.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs" ref:region="ImagingBarcodes_ReadBarcodes" ref:dedent="true">
    /// using var image = Barcode.CreateEan13("590123412345").ToImage(); // or Image.Load("product.jpg")
    ///
    /// var results = image.ReadBarcodes();
    ///
    /// var result = Assert.Single(results);
    /// Assert.Equal((BarcodeType.Ean13, "5901234123457"), (result.Result.Type, result.Text));
    /// </code>
    /// </example>
    public static IReadOnlyList<BarcodeImageResult> ReadBarcodes(this Image image, BarcodeReadingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new BarcodeReadingOptions();
        var types = options.Types;
        bool IsIncluded(BarcodeType type) => types is null || types.Contains(type);

        var results = new List<BarcodeImageResult>();
        using var luminance = BinaryImage.GetLuminance(image);
        foreach (var method in (ReadOnlySpan<ThresholdMethod>)[ThresholdMethod.Otsu, ThresholdMethod.Sauvola])
        {
            var binary = BinaryImage.Create(luminance, QRCodeImageReader.GetThresholdOptions(method, luminance), cancellationToken);
            LinearLocator.Locate(binary, IsIncluded, results, options.MaximumResults, cancellationToken);
            if (options.TryInverted && results.Count is 0)
            {
                LinearLocator.Locate(binary.Invert(), IsIncluded, results, options.MaximumResults, cancellationToken);
            }

            if (results.Count > 0)
                break;
        }

        return results;
    }
}
