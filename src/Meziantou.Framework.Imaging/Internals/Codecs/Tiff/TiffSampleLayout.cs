using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The TIFF sample layout one page is written as: the number of samples, their width, the photometric interpretation,
/// and the pixel format the rows are converted to before they are serialized in the byte order of the file.
/// </summary>
/// <param name="SamplesPerPixel">1 (grayscale, bilevel or palette indexes), 3 (RGB or YCbCr) or 4 (RGB and straight alpha).</param>
/// <param name="BitsPerSample">1 (bilevel), 8 or 16.</param>
/// <param name="Photometric">0 (<c>WhiteIsZero</c>, bilevel), 1 (<c>BlackIsZero</c>), 2 (<c>RGB</c>), 3 (palette color) or 6 (<c>YCbCr</c>, JPEG).</param>
/// <param name="SourcePixelFormat">
/// The pixel format the frame rows are converted to before serialization. The bilevel and palette layouts read the frame
/// rows as they are, and the rows of a JPEG page are converted by the JPEG encoder.
/// </param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffSampleLayout(int SamplesPerPixel, int BitsPerSample, int Photometric, PixelFormat SourcePixelFormat)
{
    /// <summary>Gets a value indicating whether the layout stores a straight alpha channel (<c>ExtraSamples = 2</c>).</summary>
    public bool HasAlpha => SamplesPerPixel == 4;

    /// <summary>Gets a value indicating whether the layout stores one bit per pixel.</summary>
    public bool IsBilevel => BitsPerSample == 1;

    /// <summary>Gets a value indicating whether the layout stores indexes into a color map.</summary>
    public bool IsIndexed => Photometric == TiffFormat.PhotometricPalette;

    /// <summary>Gets the number of stored bytes of one row, padded to a whole number of bytes.</summary>
    /// <param name="width">The number of pixels of a row.</param>
    /// <returns>The length.</returns>
    public long GetRowLength(int width) => (((long)width * SamplesPerPixel * BitsPerSample) + 7) / 8;

    /// <summary>Selects the layout of a page.</summary>
    /// <param name="encoder">The encoder settings.</param>
    /// <param name="format">The pixel format of the frame.</param>
    /// <returns>The layout.</returns>
    public static TiffSampleLayout Create(TiffEncoder encoder, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        return encoder.PixelLayout switch
        {
            TiffPixelLayout.Bilevel => new TiffSampleLayout(1, 1, TiffFormat.PhotometricWhiteIsZero, format),
            TiffPixelLayout.Indexed => new TiffSampleLayout(1, 8, TiffFormat.PhotometricPalette, format),

            // The component layout of a JPEG datastream follows the pixel format, like the one of a JPEG file
            _ when encoder.Compression == TiffCompression.Jpeg => PixelFormats.IsGrayscale(format)
                ? new TiffSampleLayout(1, 8, TiffFormat.PhotometricBlackIsZero, PixelFormat.Gray8)
                : new TiffSampleLayout(3, 8, TiffFormat.PhotometricYCbCr, PixelFormat.Rgb24),
            _ => ForPixelFormat(format),
        };
    }

    /// <summary>Selects the layout that stores a pixel format losslessly.</summary>
    /// <param name="format">The pixel format of the frames.</param>
    /// <returns>The layout. Nothing is quantized and alpha is never dropped, so every working pixel format has one.</returns>
    public static TiffSampleLayout ForPixelFormat(PixelFormat format) => format switch
    {
        PixelFormat.Gray8 => new TiffSampleLayout(1, 8, TiffFormat.PhotometricBlackIsZero, PixelFormat.Gray8),
        PixelFormat.Gray16 => new TiffSampleLayout(1, 16, TiffFormat.PhotometricBlackIsZero, PixelFormat.Gray16),
        PixelFormat.Rgb24 => new TiffSampleLayout(3, 8, TiffFormat.PhotometricRgb, PixelFormat.Rgb24),
        PixelFormat.Rgba64 => new TiffSampleLayout(4, 16, TiffFormat.PhotometricRgb, PixelFormat.Rgba64),

        // Bgra32 is reordered to Rgba32 by the row conversion; both store the same information
        _ => new TiffSampleLayout(4, 8, TiffFormat.PhotometricRgb, PixelFormat.Rgba32),
    };

    /// <summary>
    /// Gets the pixel format whose color space is the one of the written samples, which is what an ICC profile of the
    /// page describes: a bilevel page is grayscale and a palette page is RGB, whatever the pixel format of the frame.
    /// </summary>
    /// <param name="layout">The layout setting of the encoder.</param>
    /// <param name="format">The pixel format of the frame.</param>
    /// <returns>The pixel format a profile must be able to label.</returns>
    public static PixelFormat GetLabeledPixelFormat(TiffPixelLayout layout, PixelFormat format) => layout switch
    {
        TiffPixelLayout.Bilevel => PixelFormat.Gray8,
        TiffPixelLayout.Indexed => PixelFormat.Rgb24,
        _ => format,
    };
}
