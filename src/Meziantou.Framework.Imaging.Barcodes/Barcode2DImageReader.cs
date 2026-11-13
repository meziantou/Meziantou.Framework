using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Barcodes.Internal;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to read the Data Matrix, PDF417 and Aztec symbols of an image.
/// </summary>
public static class Barcode2DImageReader
{
    /// <summary>Reads the Data Matrix, PDF417 and Aztec symbols of an image.</summary>
    /// <param name="image">The image. For an animated image, its first frame is read.</param>
    /// <param name="options">The reading options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the reading.</param>
    /// <returns>The symbols found, each once; empty when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The reader is made for clean images: generated, printed or scanned symbols, photographed straight on. It finds the
    /// solid sides of the finder pattern of a Data Matrix symbol, the bullseye of an Aztec symbol and the start and stop
    /// patterns of the rows of a PDF417 symbol, samples their modules and decodes them with <see cref="Barcode2DDecoder"/>.
    /// Data Matrix and Aztec symbols can be rotated or mirrored; the rows of a PDF417 symbol must be about horizontal or
    /// vertical. A transparent background reads as white.
    /// </para>
    /// <para>
    /// A symbol needs its quiet zone and modules of at least 2 pixels. The pixels are classified as dark or light with
    /// the Otsu threshold of the image, then with a local threshold (Sauvola) when no symbol is found.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Imaging.Barcodes.Tests/ImageRendererTests.cs" ref:region="ImagingBarcodes_ReadBarcodes2D" ref:dedent="true">
    /// using var image = Barcode2D.CreateDataMatrix("https://www.meziantou.net").ToImage(); // or Image.Load("label.png")
    ///
    /// var results = image.ReadBarcodes2D();
    ///
    /// var result = Assert.Single(results);
    /// Assert.Equal((Barcode2DType.DataMatrix, "https://www.meziantou.net"), (result.Result.Type, result.Text));
    /// </code>
    /// </example>
    public static IReadOnlyList<Barcode2DImageResult> ReadBarcodes2D(this Image image, Barcode2DReadingOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new Barcode2DReadingOptions();
        var types = options.Types;
        bool IsIncluded(Barcode2DType type) => types is null || types.Contains(type);

        var results = new List<Barcode2DImageResult>();
        using var luminance = BinaryImage.GetLuminance(image);
        foreach (var method in (ReadOnlySpan<ThresholdMethod>)[ThresholdMethod.Otsu, ThresholdMethod.Sauvola])
        {
            var binary = BinaryImage.Create(luminance, QRCodeImageReader.GetThresholdOptions(method, luminance), cancellationToken);
            Barcode2DLocator.Locate(binary, IsIncluded, results, options.MaximumResults, cancellationToken);
            if (options.TryInverted && results.Count < options.MaximumResults)
            {
                Barcode2DLocator.Locate(binary.Invert(), IsIncluded, results, options.MaximumResults, cancellationToken);
            }

            if (results.Count > 0)
                break;
        }

        return results;
    }
}
