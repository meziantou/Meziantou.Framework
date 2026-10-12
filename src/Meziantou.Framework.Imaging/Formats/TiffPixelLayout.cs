namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The sample layout written by <see cref="TiffEncoder"/>.</summary>
public enum TiffPixelLayout
{
    /// <summary>
    /// The layout follows the pixel format, losslessly: gray pixel formats are 8- or 16-bit <c>BlackIsZero</c> grayscale,
    /// <see cref="PixelFormat.Rgb24"/> is 8-bit RGB and the pixel formats with alpha are RGB with an unassociated alpha
    /// sample. This is the default: no sample and no alpha value is ever discarded. With
    /// <see cref="TiffCompression.Jpeg"/> the color pixel formats are stored as YCbCr.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// One bit per pixel (<c>WhiteIsZero</c>). Every pixel must be opaque and exactly black or exactly white, whatever the
    /// pixel format: any other pixel is rejected with an <see cref="UnsupportedImageFeatureException"/> before anything
    /// of the page is written, because the encoder never thresholds nor dithers. This is the layout the CCITT
    /// compressions require. A bilevel page is read back as <see cref="PixelFormat.Gray8"/> with one bit per component.
    /// </summary>
    Bilevel = 1,

    /// <summary>
    /// Palette color: 8-bit indexes into a <c>ColorMap</c> of at most 256 colors. The page is stored exactly: every pixel
    /// must be opaque and the page must have at most 256 distinct colors, otherwise it is rejected with an
    /// <see cref="UnsupportedImageFeatureException"/> before anything of it is written (the encoder never reduces
    /// colors). The colors of a 16-bit pixel format keep their 16 bits. A palette page is read back as
    /// <see cref="PixelFormat.Rgb24"/>, or as <see cref="PixelFormat.Rgba64"/> when its colors need 16 bits.
    /// One set of 16-bit colors is rejected too: values that are all multiples of 256 without being all multiples of
    /// 257, which readers take for 8-bit colors (some software stores an 8-bit color <c>c</c> as <c>256 c</c>).
    /// </summary>
    Indexed = 2,
}
