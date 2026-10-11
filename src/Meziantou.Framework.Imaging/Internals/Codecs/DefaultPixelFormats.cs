namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The default working representation of each source layout: the pixel format of untyped loads
/// and of <see cref="ImageInfo.PixelFormat"/>. Every value is lossless for the encoded samples.
/// </summary>
internal static class DefaultPixelFormats
{
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
    /// <param name="componentCount">The number of frame components (1 for grayscale, 3 for color).</param>
    /// <returns><see cref="PixelFormat.Gray8"/> or <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForJpeg(int componentCount) => componentCount == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24;

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
    /// <returns><see cref="PixelFormat.Gray8"/> for grayscale images, <see cref="PixelFormat.Rgba32"/> when alpha is declared, otherwise <see cref="PixelFormat.Rgb24"/>.</returns>
    public static PixelFormat ForTga(bool isGrayscale, bool hasAlpha) => isGrayscale ? PixelFormat.Gray8 : (hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24);

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

    /// <summary>Selects the default pixel format of one TIFF page.</summary>
    /// <param name="samplesPerPixel">The number of samples of one pixel: 1, 2, 3 or 4.</param>
    /// <param name="bitsPerSample">The width of one sample: 8 or 16.</param>
    /// <returns>
    /// The lossless working representation: <see cref="PixelFormat.Gray8"/>/<see cref="PixelFormat.Gray16"/> for grayscale
    /// pages without alpha, <see cref="PixelFormat.Rgb24"/> for 8-bit RGB, and <see cref="PixelFormat.Rgba32"/> or
    /// <see cref="PixelFormat.Rgba64"/> otherwise (the library has no gray-with-alpha and no 16-bit RGB pixel format, so
    /// 16-bit RGB pages are widened to <see cref="PixelFormat.Rgba64"/> with an opaque alpha channel).
    /// </returns>
    public static PixelFormat ForTiff(int samplesPerPixel, int bitsPerSample)
        => ForPnm(isGrayscale: samplesPerPixel <= 2, hasAlpha: samplesPerPixel is 2 or 4, is16Bit: bitsPerSample == 16);

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
}
