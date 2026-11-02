using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Filters.Jbig2;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfImage
{
    // The widths CCITTFaxDecode decodes (CcittFaxFilter)
    private const int MaxCcittColumns = 1 << 20;

    private readonly PdfDictionary? _decodeParameters;

    private PdfImage(int width, int height, ReadOnlyMemory<byte> data, PdfName? filter, PdfDictionary? decodeParameters)
        : this(width, height, PdfDeviceColorSpace.Gray, bitsPerComponent: 1, data, filter, iccProfile: null, softMask: null)
    {
        _decodeParameters = decodeParameters;
    }

    /// <summary>Creates a bilevel image (black and white, one bit per pixel in <c>DeviceGray</c>) from its pixels.</summary>
    /// <param name="pixels">
    /// The rows of the image, from top to bottom, each of <c>(width + 7) / 8</c> bytes: one bit per pixel, the most
    /// significant bit of a byte first, a set bit for a black pixel. The bits after the last pixel of a row are ignored.
    /// The pixels are compressed when the method is called.
    /// </param>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="compression">The compression: <see cref="PdfBilevelCompression.CcittGroup4"/> by default.</param>
    /// <returns>The image.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height is not positive, or the width is larger than 1048576 pixels with <see cref="PdfBilevelCompression.CcittGroup4"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is shorter than the rows of the image.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs" ref:region="PdfImage_FromBilevel" ref:dedent="true">
    /// // A 16 x 2 image, one bit per pixel: a set bit is a black pixel
    /// byte[] pixels = [0b1111_0000, 0b0000_1111, 0b1010_1010, 0b0101_0101];
    /// var image = PdfImage.FromBilevel(pixels, width: 16, height: 2, PdfBilevelCompression.CcittGroup4);
    ///
    /// var document = new PdfDocument();
    /// document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawImage(image, 50, 700, 160, 20);
    /// var data = document.ToByteArray();
    /// </code>
    /// </example>
    public static PdfImage FromBilevel(ReadOnlySpan<byte> pixels, int width, int height, PdfBilevelCompression compression = PdfBilevelCompression.CcittGroup4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (compression == PdfBilevelCompression.CcittGroup4)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxCcittColumns);
        }

        var stride = (width + 7L) / 8;
        if (pixels.Length < stride * height)
            throw new ArgumentException($"The pixels of a {width} x {height} bilevel image are {stride * height} bytes", nameof(pixels));

        var rows = pixels[..(int)(stride * height)];
        switch (compression)
        {
            case PdfBilevelCompression.CcittGroup4:
                // Group 4 codes the colors, not the bits: with /BlackIs1 false (the default), the decoded bits are 0 for black, as DeviceGray needs
                var parameters = new PdfDictionary
                {
                    [PdfNames.K] = PdfInteger.Get(-1),
                    [PdfNames.Columns] = PdfInteger.Get(width),
                    [PdfName.Create("Rows")] = PdfInteger.Get(height),
                };
                return new PdfImage(width, height, CcittFaxFilter.EncodeGroup4(rows, width, height), PdfNames.CCITTFaxDecode, parameters);

            case PdfBilevelCompression.Jbig2:
                // The JBIG2Decode filter decodes 0 for black (7.4.7)
                return new PdfImage(width, height, Jbig2Encoder.Encode(rows, width, height), PdfNames.JBIG2Decode, decodeParameters: null);

            case PdfBilevelCompression.Flate:
                // The samples of DeviceGray are 0 for black: the bits are inverted, and compressed when the document is saved
                var samples = rows.ToArray();
                CcittFaxFilter.Invert(samples);
                return new PdfImage(width, height, samples, filter: null, decodeParameters: null);

            default:
                throw new ArgumentOutOfRangeException(nameof(compression), compression, message: null);
        }
    }

    private void AddDecodeParameters(PdfDictionary dictionary)
    {
        if (_decodeParameters is null)
            return;

        // Each document gets its own dictionary: the image is shared
        var parameters = new PdfDictionary();
        foreach (var (key, value) in _decodeParameters)
        {
            parameters[key] = value;
        }

        dictionary[PdfNames.DecodeParms] = parameters;
    }
}
