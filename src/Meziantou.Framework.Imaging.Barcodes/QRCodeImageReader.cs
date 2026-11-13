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
    /// The reader is made for clean images: generated, printed or scanned symbols, photographed straight on. It finds the
    /// finder patterns of the symbols, which can be rotated, scaled, slightly blurred or in mild perspective, samples their
    /// modules and decodes them with <see cref="QRCodeDecoder"/>. A transparent background reads as white.
    /// </para>
    /// <para>
    /// A symbol needs its quiet zone and modules of at least 2 pixels. The pixels are classified as dark or light with
    /// the Otsu threshold of the image, then with a local threshold (Sauvola) when no symbol is found.
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
        using var luminance = BinaryImage.GetLuminance(image);
        foreach (var method in (ReadOnlySpan<ThresholdMethod>)[ThresholdMethod.Otsu, ThresholdMethod.Sauvola])
        {
            var binary = BinaryImage.Create(luminance, GetThresholdOptions(method, luminance), cancellationToken);
            QRCodeLocator.Locate(binary, IsIncluded, results, options.MaximumResults, cancellationToken);
            if (options.TryInverted && results.Count < options.MaximumResults)
            {
                QRCodeLocator.Locate(binary.Invert(), IsIncluded, results, options.MaximumResults, cancellationToken);
            }

            if (results.Count > 0)
                break;
        }

        return results;
    }

    internal static ThresholdOptions GetThresholdOptions(ThresholdMethod method, Image image)
    {
        if (method is ThresholdMethod.Otsu)
            return new ThresholdOptions { Method = ThresholdMethod.Otsu };

        // The window spans a few modules of any reasonable size
        var window = Math.Clamp((Math.Min(image.Width, image.Height) / 8) | 1, 15, 201);
        return new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = window };
    }
}
