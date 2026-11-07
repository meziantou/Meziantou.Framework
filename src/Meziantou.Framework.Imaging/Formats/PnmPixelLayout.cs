namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The pixel layout written by <see cref="PnmEncoder"/>.</summary>
public enum PnmPixelLayout
{
    /// <summary>
    /// The variant follows the pixel format: a portable graymap for gray pixel formats, a portable pixmap for pixel formats
    /// without alpha and a portable arbitrary map for pixel formats with alpha. This is the default: no sample and no alpha
    /// value is ever discarded.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// A portable bitmap, one bit per pixel (<c>P4</c>, or <c>P1</c> with <see cref="PnmEncoding.Plain"/>). Every pixel must
    /// be exactly black or exactly white, whatever the pixel format: any other pixel is rejected with an
    /// <see cref="UnsupportedImageFeatureException"/>, because the encoder never thresholds nor dithers. Alpha is discarded,
    /// so a pixel format with alpha requires <see cref="PnmEncoder.BackgroundColor"/> unless every pixel is opaque.
    /// </summary>
    Bitmap = 1,
}
