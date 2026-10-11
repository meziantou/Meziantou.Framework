namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The pixel layout written by <see cref="BmpEncoder"/>.</summary>
public enum BmpPixelLayout
{
    /// <summary>
    /// <see cref="Bgra32"/> for pixel formats with alpha, <see cref="Bgr24"/> otherwise. This is the default: no sample and
    /// no alpha value is ever discarded. The indexed and 16-bit layouts are never selected automatically.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// 24 bits per pixel, blue, green and red, in a <c>BITMAPINFOHEADER</c> with <c>BI_RGB</c>: the most widely readable BMP
    /// layout. Alpha is discarded, so a pixel format with alpha requires <see cref="BmpEncoder.BackgroundColor"/>.
    /// </summary>
    Bgr24 = 1,

    /// <summary>
    /// 32 bits per pixel, blue, green, red and straight alpha, in a <c>BITMAPV4HEADER</c> with <c>BI_BITFIELDS</c> and an
    /// explicit alpha mask, so that readers never have to guess whether the fourth byte is transparency or padding.
    /// </summary>
    Bgra32 = 2,

    /// <summary>
    /// 1 bit per pixel: indexes into a palette of at most 2 colors, in a <c>BITMAPINFOHEADER</c>. The palette holds the
    /// colors of the image exactly when they fit; otherwise <see cref="BmpEncoder.AllowColorReduction"/> is required. Alpha
    /// is discarded, so a pixel format with alpha requires <see cref="BmpEncoder.BackgroundColor"/>.
    /// </summary>
    Indexed1 = 3,

    /// <summary>
    /// 4 bits per pixel: indexes into a palette of at most 16 colors, in a <c>BITMAPINFOHEADER</c>, with the rules of
    /// <see cref="Indexed1"/>. Can be run-length encoded (<see cref="BmpCompression.RunLength"/>).
    /// </summary>
    Indexed4 = 4,

    /// <summary>
    /// 8 bits per pixel: indexes into a palette of at most 256 colors, in a <c>BITMAPINFOHEADER</c>, with the rules of
    /// <see cref="Indexed1"/>. Can be run-length encoded (<see cref="BmpCompression.RunLength"/>).
    /// </summary>
    Indexed8 = 5,

    /// <summary>
    /// 16 bits per pixel, 5 bits of red, 6 of green and 5 of blue, in a <c>BITMAPINFOHEADER</c> with <c>BI_BITFIELDS</c>
    /// and its three masks. 8-bit samples lose their low bits (nearest rounding), so
    /// <see cref="BmpEncoder.AllowBitDepthReduction"/> is required. Alpha is discarded, so a pixel format with alpha
    /// requires <see cref="BmpEncoder.BackgroundColor"/>.
    /// </summary>
    Bgr565 = 6,

    /// <summary>
    /// 16 bits per pixel, 5 bits per channel and one unused bit, in a <c>BITMAPINFOHEADER</c> with <c>BI_RGB</c> (the
    /// implicit 16-bit masks of the format), with the rules of <see cref="Bgr565"/>.
    /// </summary>
    Bgr555 = 7,
}
