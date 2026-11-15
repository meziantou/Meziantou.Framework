using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Barcodes.Internal;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to read the QR codes of an image: standard QR codes, Micro QR codes and rMQR codes.
/// </summary>
public static class QRCodeImageReader
{
    /// <summary>Reads the QR codes of an image.</summary>
    /// <param name="image">The image. For an animated image, its first frame is read.</param>
    /// <param name="options">The reading options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the reading.</param>
    /// <returns>The QR codes found, each once; empty when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The reader reads generated, printed and scanned images, and photographs. It finds the finder patterns of the
    /// symbols, which can be rotated, mirrored, scaled, blurred, unevenly lit or in perspective: the corners of the finder
    /// patterns and the alignment patterns give the perspective of a symbol photographed at an angle. It samples their
    /// modules and decodes them with <see cref="QRCodeDecoder"/>. A transparent background reads as white.
    /// </para>
    /// <para>
    /// A symbol needs its quiet zone and modules of at least 2 pixels. The pixels are classified as dark or light with a
    /// local threshold (from the blocks of 8x8 pixels around them, which follows shadows and gradients), then with the
    /// Otsu threshold of the image and a Sauvola threshold when no symbol is found.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs" ref:region="ImagingBarcodes_ReadQRCodes" ref:dedent="true">
    /// using var image = QRCode.Create("https://www.meziantou.net").ToImage(); // or Image.Load("photo.png")
    ///
    /// var results = image.ReadQRCodes();
    ///
    /// var result = Assert.Single(results);
    /// Assert.Equal("https://www.meziantou.net", result.Text);
    ///
    /// // The corners of the symbol in the image: top-left, top-right, bottom-right, bottom-left
    /// var topLeft = result.Corners[0];
    /// Assert.True(topLeft.DistanceTo(new PointD(40, 40)) &lt; 1); // after the quiet zone of 4 modules of 10 pixels
    /// </code>
    /// </example>
    public static IReadOnlyList<QRCodeImageResult> ReadQRCodes(this Image image, QRCodeReadingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new QRCodeReadingOptions();
        var types = options.Types;
        bool IsIncluded(QRCodeType type) => types is null || types.Contains(type);

        var results = new List<QRCodeImageResult>();
        BinarizationPasses.Run(image, options.TryInverted, binary =>
        {
            QRCodeLocator.Locate(binary, IsIncluded, results, options.MaximumResults, cancellationToken);
            return results.Count > 0;
        }, cancellationToken);

        return results;
    }
}
