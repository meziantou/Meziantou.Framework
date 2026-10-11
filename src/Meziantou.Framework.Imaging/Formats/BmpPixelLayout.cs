namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The uncompressed pixel layout written by <see cref="BmpEncoder"/>.</summary>
public enum BmpPixelLayout
{
    /// <summary>
    /// <see cref="Bgra32"/> for pixel formats with alpha, <see cref="Bgr24"/> otherwise. This is the default: no sample and
    /// no alpha value is ever discarded.
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
}
