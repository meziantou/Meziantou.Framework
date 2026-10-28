namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The default working representation of each source layout: the pixel format of untyped loads
/// and of <see cref="ImageInfo.PixelFormat"/>. Every value is lossless for the encoded samples, and the smallest built-in
/// format that is: gray images with alpha are <see cref="PixelFormat.La16"/>/<see cref="PixelFormat.La32"/>, and 16-bit
/// color images without alpha <see cref="PixelFormat.Rgb48"/>.
/// </summary>
/// <remarks>
/// The decoders produce the rows of these formats in the layout of <see cref="PixelFormatWidening.GetWidenedFormat"/>
/// (gray replicated in RGBA, 16-bit RGB with an opaque alpha), which their frame sink converts exactly.
/// </remarks>
internal static class DefaultPixelFormats
{
    /// <summary>Selects the default pixel format of an AVIF image.</summary>
    /// <param name="isMonochrome">Whether the color image has a single plane.</param>
    /// <param name="hasAlpha">Whether the image has an alpha plane.</param>
    /// <param name="bitDepth">The largest bit depth of the color and alpha planes (8, 10 or 12).</param>
    /// <returns>The pixel format: 8-bit or 16-bit (deep) gray, gray with alpha, RGB or RGBA.</returns>
    public static PixelFormat ForAvif(bool isMonochrome, bool hasAlpha, int bitDepth) => Select(isMonochrome, hasAlpha, bitDepth > 8);

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
            // Grayscale: Gray8 for 1/2/4/8 bits, Gray16 for 16 bits; a tRNS key makes pixels transparent: gray with alpha
            0 => Select(isGray: true, hasTransparencyChunk, is16Bit),

            // RGB: a tRNS key makes pixels transparent
            2 => Select(isGray: false, hasTransparencyChunk, is16Bit),

            // Palette expansion (any tRNS) is 8-bit RGBA
            3 => PixelFormat.Rgba32,

            4 => is16Bit ? PixelFormat.La32 : PixelFormat.La16,
            6 => is16Bit ? PixelFormat.Rgba64 : PixelFormat.Rgba32,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType), colorType, "The PNG color type is not valid."),
        };
    }

    /// <summary>Gets the default pixel format of a GIF image (alpha-capable working pixels).</summary>
    public static PixelFormat Gif => PixelFormat.Rgba32;

    /// <summary>Selects the default pixel format of a JPEG image.</summary>
    /// <param name="componentCount">The number of frame components (1 for grayscale, 3 for color, 4 for inks, which are only loaded converted to color).</param>
    /// <param name="precision">The number of bits of a sample (8 or 12, or 2 to 16 for a lossless frame).</param>
    /// <returns>
    /// <see cref="PixelFormat.Gray8"/> or <see cref="PixelFormat.Rgb24"/> for 8-bit samples; for the other samples, which
    /// are scaled to 16 bits, <see cref="PixelFormat.Gray16"/> or <see cref="PixelFormat.Rgb48"/>.
    /// </returns>
    public static PixelFormat ForJpeg(int componentCount, int precision) => Select(componentCount == 1, hasAlpha: false, precision != 8);

    /// <summary>Selects the default pixel format of a WebP image.</summary>
    /// <param name="isAnimated">Whether the file is an animation (the canvas starts transparent).</param>
    /// <param name="hasAlpha">Whether the still image carries alpha (VP8X alpha flag, ALPH chunk or VP8L alpha hint).</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> for animations and images with alpha, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForWebP(bool isAnimated, bool hasAlpha) => isAnimated || hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of a QOI image.</summary>
    /// <param name="channels">The header channel count (3 or 4).</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> for RGBA streams, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForQoi(int channels) => channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of an OpenEXR image.</summary>
    /// <param name="halfOnly">Whether every channel holds HALF samples.</param>
    /// <returns><see cref="PixelFormat.RgbaHalf"/> for HALF samples, otherwise <see cref="PixelFormat.RgbaFloat"/> (FLOAT and UINT samples, which a half cannot hold).</returns>
    public static PixelFormat ForOpenExr(bool halfOnly) => halfOnly ? PixelFormat.RgbaHalf : PixelFormat.RgbaFloat;

    /// <summary>Selects the default pixel format of a BMP image.</summary>
    /// <param name="hasAlphaMask">Whether the DIB layout defines a real (non-padding) alpha channel.</param>
    /// <returns><see cref="PixelFormat.Rgba32"/> for layouts with an alpha mask, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForBmp(bool hasAlphaMask) => hasAlphaMask ? PixelFormat.Rgba32 : PixelFormat.Rgb24;

    /// <summary>Selects the default pixel format of a TGA image.</summary>
    /// <param name="isGrayscale">Whether the image type is grayscale (3 or 11).</param>
    /// <param name="hasAlpha">Whether the image descriptor declares a non-zero alpha-bit count.</param>
    /// <returns>
    /// <see cref="PixelFormat.Gray8"/>, <see cref="PixelFormat.La16"/>, <see cref="PixelFormat.Rgb24"/> or
    /// <see cref="PixelFormat.Rgba32"/>.
    /// </returns>
    public static PixelFormat ForTga(bool isGrayscale, bool hasAlpha) => Select(isGrayscale, hasAlpha, is16Bit: false);

    /// <summary>Selects the default pixel format of a Netpbm image.</summary>
    /// <param name="isGrayscale">Whether the tuple is grayscale (PBM, PGM, PAM <c>BLACKANDWHITE</c>/<c>GRAYSCALE</c> and their alpha forms).</param>
    /// <param name="hasAlpha">Whether the tuple has an alpha sample.</param>
    /// <param name="is16Bit">Whether <c>MAXVAL</c> is greater than 255.</param>
    /// <returns>The lossless working representation: 8-bit or 16-bit gray, gray with alpha, RGB or RGBA.</returns>
    public static PixelFormat ForPnm(bool isGrayscale, bool hasAlpha, bool is16Bit) => Select(isGrayscale, hasAlpha, is16Bit);

    /// <summary>Selects the default pixel format of one TIFF page whose samples are not palette indexes.</summary>
    /// <param name="isGrayscale">Whether the samples are grayscale (<c>WhiteIsZero</c> or <c>BlackIsZero</c>).</param>
    /// <param name="hasAlpha">Whether the page stores an unassociated alpha sample.</param>
    /// <param name="is16Bit">Whether the samples have 16 bits.</param>
    /// <returns>
    /// The lossless working representation: 8-bit or 16-bit gray, gray with alpha, RGB or RGBA (samples of 1, 2 and 4 bits
    /// are scaled exactly to 8 bits; YCbCr and CMYK pages are converted to RGB). A palette page is
    /// <see cref="PixelFormat.Rgb24"/>, or <see cref="PixelFormat.Rgb48"/> when its colors need 16 bits.
    /// </returns>
    public static PixelFormat ForTiff(bool isGrayscale, bool hasAlpha, bool is16Bit) => ForPnm(isGrayscale, hasAlpha, is16Bit);

    /// <summary>Selects the default pixel format of one TIFF page of floating-point samples (grayscale or RGB, with or without alpha).</summary>
    /// <param name="bitsPerSample">The number of bits of a sample: 16 (half precision), 24 or 32.</param>
    /// <returns>
    /// <see cref="PixelFormat.RgbaHalf"/> for half-precision samples and <see cref="PixelFormat.RgbaFloat"/> for the others,
    /// which it stores exactly (the library has no floating-point gray format, so gray samples are repeated in the three
    /// colors, and samples without alpha are opaque).
    /// </returns>
    public static PixelFormat ForTiffFloatingPoint(int bitsPerSample) => bitsPerSample == 16 ? PixelFormat.RgbaHalf : PixelFormat.RgbaFloat;

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
    /// <returns>The pixel format: 8-bit or 16-bit gray, gray with alpha, RGB or RGBA.</returns>
    public static PixelFormat ForJpegXl(bool isGray, bool hasAlpha, bool isSixteenBit) => Select(isGray, hasAlpha, isSixteenBit);

    /// <summary>Selects the default pixel format of a JPEG 2000 image.</summary>
    /// <param name="isGray">Whether the image has a single color channel.</param>
    /// <param name="hasAlpha">Whether the image has an opacity channel.</param>
    /// <param name="isSixteenBit">Whether a color channel or the opacity channel has more than 8 bits.</param>
    /// <returns>
    /// The pixel format: 8-bit or 16-bit gray, gray with alpha, RGB or RGBA. The inks of a CMYK image are converted to
    /// red, green and blue.
    /// </returns>
    public static PixelFormat ForJpeg2000(bool isGray, bool hasAlpha, bool isSixteenBit) => ForJpegXl(isGray, hasAlpha, isSixteenBit);

    /// <summary>Gets the smallest built-in format that stores samples of a color model exactly.</summary>
    /// <param name="isGray">Whether the samples are a single gray level.</param>
    /// <param name="hasAlpha">Whether the pixels have an alpha, or can be transparent.</param>
    /// <param name="is16Bit">Whether the samples need 16 bits.</param>
    /// <returns>The pixel format.</returns>
    private static PixelFormat Select(bool isGray, bool hasAlpha, bool is16Bit) => (isGray, hasAlpha, is16Bit) switch
    {
        (true, false, false) => PixelFormat.Gray8,
        (true, false, true) => PixelFormat.Gray16,
        (true, true, false) => PixelFormat.La16,
        (true, true, true) => PixelFormat.La32,
        (false, false, false) => PixelFormat.Rgb24,
        (false, false, true) => PixelFormat.Rgb48,
        (false, true, false) => PixelFormat.Rgba32,
        (false, true, true) => PixelFormat.Rgba64,
    };
}
