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
    /// The reader reads generated, printed and scanned images, and photographs: blurred, unevenly lit, slightly tilted.
    /// Parallel scan lines cross the image horizontally, vertically and at 30°, 60°, 120° and 150°, and
    /// <see cref="BarcodeDecoder.DecodeScanLines"/> reads the barcodes they cross: a barcode turned by another angle is read
    /// when one of the lines crosses all its bars. The edges of the bars are first found in the grayscale image, between
    /// the pixels, with a threshold that follows the lighting; then the image classified as dark or light (a local
    /// threshold, then the Otsu threshold of the image) is read along the same lines. A transparent background and the
    /// outside of the image read as white: a barcode cropped at its first and last bars has its quiet zones.
    /// </para>
    /// <para>
    /// A barcode needs narrow bars of about 2 pixels or more, and two scan lines that agree. Code 39, Codabar and ITF, whose
    /// check is weak or missing, must be read over a height of at least 4% of their length; a Codabar symbol needs 4
    /// characters or more (start and stop characters included), an ITF symbol 6 digits or more.
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
        bool Locate(GrayScaleScanLineSource source)
        {
            LinearLocator.Locate(source, LinearLocator.Angles, IsIncluded, results, options.MaximumResults, cancellationToken);
            return results.Count > 0;
        }

        // The edges of the bars in the grayscale image first, at every angle: they keep the widths of the narrow bars of a
        // blurred photograph. Then the binary images of the local and global thresholds (and their inverse for the barcodes
        // printed light on dark), at every angle too, for the bars that the grayscale lines cannot separate.
        using var luminance = BinaryImage.GetLuminance(image);
        if (Locate(GrayScaleScanLineSource.Create(luminance)))
            return results;

        BinarizationPasses.Run(luminance, options.TryInverted, binary =>
        {
            LinearLocator.Locate(new BinaryScanLineSource(binary), LinearLocator.Angles, IsIncluded, results, options.MaximumResults, cancellationToken);
            return results.Count > 0;
        }, cancellationToken, includeSauvola: false);

        return results;
    }
}
