using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The TIFF sample layout one working pixel format is written as, losslessly: the number of
/// samples, their width, the photometric interpretation, and the pixel format the rows are converted to before they are
/// serialized in the byte order of the file.
/// </summary>
/// <param name="SamplesPerPixel">1 (grayscale), 3 (RGB) or 4 (RGB and straight alpha).</param>
/// <param name="BitsPerSample">8 or 16.</param>
/// <param name="Photometric">1 (<c>BlackIsZero</c>) or 2 (<c>RGB</c>).</param>
/// <param name="SourcePixelFormat">The pixel format the frame rows are converted to before serialization.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TiffSampleLayout(int SamplesPerPixel, int BitsPerSample, int Photometric, PixelFormat SourcePixelFormat)
{
    /// <summary>Gets a value indicating whether the layout stores a straight alpha channel (<c>ExtraSamples = 2</c>).</summary>
    public bool HasAlpha => SamplesPerPixel == 4;

    /// <summary>Gets the number of stored bytes of one pixel.</summary>
    public int BytesPerPixel => SamplesPerPixel * BitsPerSample / 8;

    /// <summary>Selects the layout of a pixel format.</summary>
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
}
