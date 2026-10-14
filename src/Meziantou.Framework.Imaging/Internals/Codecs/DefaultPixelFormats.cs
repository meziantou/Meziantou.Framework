namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The default working representation of each source layout: the pixel format of untyped loads
/// and of <see cref="ImageInfo.PixelFormat"/>. Every value is lossless for the encoded samples.
/// </summary>
internal static class DefaultPixelFormats
{
    /// <summary>Selects the default pixel format of an AVIF image.</summary>
    /// <param name="isMonochrome">Whether the color image has a single plane.</param>
    /// <param name="hasAlpha">Whether the image has an alpha plane.</param>
    /// <param name="bitDepth">The largest bit depth of the color and alpha planes (8, 10 or 12).</param>
    /// <returns>The pixel format: there is no 16-bit RGB format, so that deep color images without alpha are opaque <see cref="PixelFormat.Rgba64"/>.</returns>
    public static PixelFormat ForAvif(bool isMonochrome, bool hasAlpha, int bitDepth)
    {
        if (bitDepth > 8)
            return isMonochrome && !hasAlpha ? PixelFormat.Gray16 : PixelFormat.Rgba64;

        if (hasAlpha)
            return PixelFormat.Rgba32;

        return isMonochrome ? PixelFormat.Gray8 : PixelFormat.Rgb24;
    }

    /// <summary>Selects the default pixel format of an HEIC image: the one of an AVIF image with the same planes.</summary>
    /// <param name="isMonochrome">Whether the color image has a single plane.</param>
    /// <param name="hasAlpha">Whether the image has an alpha plane.</param>
    /// <param name="bitDepth">The largest bit depth of the color and alpha planes (8, 10 or 12).</param>
    /// <returns>The pixel format.</returns>
    public static PixelFormat ForHeic(bool isMonochrome, bool hasAlpha, int bitDepth) => ForAvif(isMonochrome, hasAlpha, bitDepth);

    /// <summary>Selects the default pixel format of a PNG or APNG image.</summary>
    /// <param name="colorType">The IHDR color type (0, 2, 3, 4 or 6).</param>
    /// <param name="bitDepth">The IHDR bit depth.</param>
    /// <param name="hasTransparencyChunk">Whether a valid <c>tRNS</c> chunk applies.</param>
    /// <param name="isAnimated">Whether the file is an APNG (valid <c>acTL</c> before the first <c>IDAT</c>).</param>
    /// <returns>The pixel format.</returns>
    public static PixelFormat ForPng(byte colorType, byte bitDepth, bool hasTransparencyChunk, bool isAnimated)
    {
        var is16Bit = bitDepth == 16;

        // Animations composite on an alpha-capable canvas (APNG background disposal clears to transparent black)
        if (isAnimated)
            return is16Bit ? PixelFormat.Rgba64 : PixelFormat.Rgba32;

        return colorType switch
        {
            // Grayscale: Gray8 for 1/2/4/8 bits, Gray16 for 16 bits, unless a tRNS key makes pixels transparent
            0 when !hasTransparencyChunk => is16Bit ? PixelFormat.Gray16 : PixelFormat.Gray8,
            0 => is16Bit ? PixelFormat.Rgba64 : PixelFormat.Rgba32,

            // RGB 8 opaque -> Rgb24; RGB 16 (opaque or not) and RGB 8 with a tRNS key need alpha or 16 bits
            2 when !is16Bit && !hasTransparencyChunk => PixelFormat.Rgb24,
            2 => is16Bit ? PixelFormat.Rgba64 : PixelFormat.Rgba32,

            // Palette expansion (any tRNS) is 8-bit RGBA
            3 => PixelFormat.Rgba32,

            // Gray+alpha and RGBA
            4 or 6 => is16Bit ? PixelFormat.Rgba64 : PixelFormat.Rgba32,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType), colorType, "The PNG color type is not valid."),
        };
    }

    /// <summary>Gets the default pixel format of a GIF image (alpha-capable working pixels).</summary>
    public static PixelFormat Gif => PixelFormat.Rgba32;

    /// <summary>Selects the default pixel format of a JPEG image.</summary>
    /// <param name="componentCount">The number of frame components (1 for grayscale, 3 for color, 4 for inks, which are only loaded converted to color).</param>
    /// <param name="precision">The number of bits of a sample (8 or 12).</param>
    /// <returns>
    /// <see cref="PixelFormat.Gray8"/> or <see cref="PixelFormat.Rgb24"/> for 8-bit samples; for deeper samples
    /// <see cref="PixelFormat.Gray16"/> or, as there is no 16-bit RGB format, opaque <see cref="PixelFormat.Rgba64"/>.
    /// </returns>
    public static PixelFormat ForJpeg(int componentCount, int precision)
    {
        if (precision > 8)
            return componentCount == 1 ? PixelFormat.Gray16 : PixelFormat.Rgba64;

        return componentCount == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;
    }

    /// <summary>Selects the default pixel format of a WebP image.</summary>
    /// <param name="isAnimated">Whether the file is an animation (the canvas starts transparent).</param>
    /// <param name="hasAlpha">Whether the still image carries alpha (VP8X alpha flag, ALPH chunk or VP8L alpha hint).</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> for animations and images with alpha, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForWebP(bool isAnimated, bool hasAlpha) => isAnimated || hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of a QOI image.</summary>
    /// <param name="channels">The header channel count (3 or 4).</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> for RGBA streams, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForQoi(int channels) => channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of a BMP image.</summary>
    /// <param name="hasAlphaMask">Whether the DIB layout defines a real (non-padding) alpha channel.</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> for layouts with an alpha mask, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForBmp(bool hasAlphaMask) => hasAlphaMask ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of a TGA image.</summary>
    /// <param name="isGrayscale">Whether the image type is grayscale (3 or 11).</param>
    /// <param name="hasAlpha">Whether the image descriptor declares a non-zero alpha-bit count.</param>
    /// <returns>
    /// <see cref="PixelFormat.Rgba32"/> when alpha is declared (the library has no gray-with-alpha pixel format, so a
    /// grayscale image with alpha is widened), otherwise <see cref="PixelFormat.Gray8"/> for grayscale images and
    /// <see cref="PixelFormat.Rgb24"/> for the others.
    /// </returns>
    public static PixelFormat ForTga(bool isGrayscale, bool hasAlpha) => hasAlpha ? PixelFormat.Rgba32 : (isGrayscale ? PixelFormat.Gray8 : PixelFormat.Rgb24);

    /// <summary>Selects the default pixel format of a Netpbm image.</summary>
    /// <param name="isGrayscale">Whether the tuple is grayscale (PBM, PGM, PAM <c>BLACKANDWHITE</c>/<c>GRAYSCALE</c> and their alpha forms).</param>
    /// <param name="hasAlpha">Whether the tuple has an alpha sample.</param>
    /// <param name="is16Bit">Whether <c>MAXVAL</c> is greater than 255.</param>
    /// <returns>
    /// The lossless working representation: <see cref="PixelFormat.Gray8"/>/<see cref="PixelFormat.Gray16"/> for grayscale
    /// tuples without alpha, <see cref="PixelFormat.Rgb24"/> for 8-bit RGB, and <see cref="PixelFormat.Rgba32"/> or
    /// <see cref="PixelFormat.Rgba64"/> otherwise (the library has no gray-with-alpha and no 16-bit RGB pixel format).
    /// </returns>
    public static PixelFormat ForPnm(bool isGrayscale, bool hasAlpha, bool is16Bit)
    {
        if (isGrayscale && !hasAlpha)
            return is16Bit ? PixelFormat.Gray16 : PixelFormat.Gray8;

        if (is16Bit)
            return PixelFormat.Rgba64;

        return hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
    }

    /// <summary>Selects the default pixel format of one TIFF page whose samples are not palette indexes.</summary>
    /// <param name="isGrayscale">Whether the samples are grayscale (<c>WhiteIsZero</c> or <c>BlackIsZero</c>).</param>
    /// <param name="hasAlpha">Whether the page stores an unassociated alpha sample.</param>
    /// <param name="is16Bit">Whether the samples have 16 bits.</param>
    /// <returns>
    /// The lossless working representation: <see cref="PixelFormat.Gray8"/>/<see cref="PixelFormat.Gray16"/> for grayscale
    /// pages without alpha (samples of 1, 2 and 4 bits are scaled exactly to 8 bits), <see cref="PixelFormat.Rgb24"/> for
    /// 8-bit color (RGB, and YCbCr and CMYK pages, which are converted to RGB), and <see cref="PixelFormat.Rgba32"/> or
    /// <see cref="PixelFormat.Rgba64"/> otherwise (the library has no gray-with-alpha and no 16-bit RGB pixel format, so
    /// 16-bit color pages are widened to <see cref="PixelFormat.Rgba64"/> with an opaque alpha channel). A palette page is
    /// <see cref="PixelFormat.Rgb24"/>, or <see cref="PixelFormat.Rgba64"/> when its colors need 16 bits.
    /// </returns>
    public static PixelFormat ForTiff(bool isGrayscale, bool hasAlpha, bool is16Bit) => ForPnm(isGrayscale, hasAlpha, is16Bit);

    /// <summary>Selects the default pixel format of one DIB-backed ICO or CUR representation.</summary>
    /// <param name="hasMask">Whether the entry stores an AND mask or an alpha channel.</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> when transparency can be expressed, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForIconDib(bool hasMask) => hasMask ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of an animated cursor, whose frames are icon or cursor images.</summary>
    /// <param name="hasSixteenBitFrame">Whether a displayed frame is a PNG payload with 16-bit samples.</param>
    /// <returns>
    /// One format for the whole animation that stores every frame losslessly: <see cref="PixelFormat.Rgba32"/> (what every
    /// DIB-backed frame decodes to), or <see cref="PixelFormat.Rgba64"/> when a frame has 16-bit samples.
    /// </returns>
    public static PixelFormat ForAni(bool hasSixteenBitFrame) => hasSixteenBitFrame ? PixelFormat.Rgba64 : PixelFormat.Rgba32;

    /// <summary>Selects the default pixel format of a JPEG XL image.</summary>
    /// <param name="isGray">Whether the image has a single color channel.</param>
    /// <param name="hasAlpha">Whether the image has an alpha channel.</param>
    /// <param name="isSixteenBit">Whether the color channels or the alpha channel have more than 8 bits.</param>
    /// <returns>
    /// The pixel format: there is no gray-with-alpha and no 16-bit RGB format, so that grayscale images with alpha are
    /// stored as RGBA and deep color images without alpha are opaque <see cref="PixelFormat.Rgba64"/>.
    /// </returns>
    public static PixelFormat ForJpegXl(bool isGray, bool hasAlpha, bool isSixteenBit)
    {
        if (isSixteenBit)
            return isGray && !hasAlpha ? PixelFormat.Gray16 : PixelFormat.Rgba64;

        if (hasAlpha)
            return PixelFormat.Rgba32;

        return isGray ? PixelFormat.Gray8 : PixelFormat.Rgb24;
    }

    /// <summary>Selects the default pixel format of a JPEG 2000 image.</summary>
    /// <param name="isGray">Whether the image has a single color channel.</param>
    /// <param name="hasAlpha">Whether the image has an opacity channel.</param>
    /// <param name="isSixteenBit">Whether a color channel or the opacity channel has more than 8 bits.</param>
    /// <returns>
    /// The pixel format: there is no gray-with-alpha and no 16-bit RGB format, so that grayscale images with alpha are
    /// stored as RGBA and deep color images without alpha are opaque <see cref="PixelFormat.Rgba64"/>. The inks of a CMYK
    /// image are converted to red, green and blue.
    /// </returns>
    public static PixelFormat ForJpeg2000(bool isGray, bool hasAlpha, bool isSixteenBit) => ForJpegXl(isGray, hasAlpha, isSixteenBit);
}
