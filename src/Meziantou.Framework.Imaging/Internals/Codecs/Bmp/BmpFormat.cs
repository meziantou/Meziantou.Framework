namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the BMP/DIB formats (Microsoft Windows GDI <c>BITMAPFILEHEADER</c> and <c>BITMAPINFOHEADER</c> family,
/// and the OS/2 Presentation Manager <c>BITMAPCOREHEADER</c> and <c>BITMAPCOREHEADER2</c>) shared by the decoder and the
/// encoder.
/// </summary>
/// <remarks>
/// Only the file header is specific to a standalone <c>.bmp</c> file: everything from the DIB header on is the payload an
/// ICO/CUR directory entry stores too, which is why <see cref="BmpInfoHeader"/> and <see cref="BmpDibLayout"/> never
/// look at it.
/// </remarks>
internal static class BmpFormat
{
    /// <summary>The length of <c>BITMAPFILEHEADER</c>: <c>BM</c>, file size, two reserved fields and the pixel-data offset.</summary>
    public const int FileHeaderLength = 14;

    /// <summary>The length of the <c>biSize</c> field that selects the DIB header variant.</summary>
    public const int HeaderSizeFieldLength = 4;

    /// <summary>The length of <c>BITMAPCOREHEADER</c> (OS/2 1.x): 16-bit dimensions, no compression and no color count.</summary>
    public const int CoreHeaderLength = 12;

    /// <summary>
    /// The shortest <c>BITMAPCOREHEADER2</c> (OS/2 2.x): the header may stop after its bit count or after any later field,
    /// and the fields it leaves out are zero.
    /// </summary>
    public const int Os2V2MinHeaderLength = 16;

    /// <summary>The length of <c>BITMAPINFOHEADER</c>.</summary>
    public const int InfoHeaderLength = 40;

    /// <summary>The length of <c>BITMAPV2INFOHEADER</c> (<c>BITMAPINFOHEADER</c> and the three color masks).</summary>
    public const int V2HeaderLength = 52;

    /// <summary>The length of <c>BITMAPV3INFOHEADER</c> (<c>BITMAPV2INFOHEADER</c> and the alpha mask).</summary>
    public const int V3HeaderLength = 56;

    /// <summary>The length of a complete <c>BITMAPCOREHEADER2</c> (OS/2 2.x).</summary>
    public const int Os2V2HeaderLength = 64;

    /// <summary>The length of <c>BITMAPV4HEADER</c>.</summary>
    public const int V4HeaderLength = 108;

    /// <summary>The length of <c>BITMAPV5HEADER</c>, the longest supported DIB header.</summary>
    public const int V5HeaderLength = 124;

    /// <summary>The length of one <c>RGBQUAD</c> palette entry: blue, green, red and a reserved byte.</summary>
    public const int PaletteEntryLength = 4;

    /// <summary>The length of one <c>RGBTRIPLE</c> palette entry, used after a <c>BITMAPCOREHEADER</c>: blue, green and red.</summary>
    public const int CorePaletteEntryLength = 3;

    /// <summary>The number of extra mask bytes that follow a 40-byte header with <see cref="BmpDibCompression.BitFields"/>.</summary>
    public const int BitFieldsMaskLength = 12;

    /// <summary>The number of extra mask bytes that follow a 40-byte header with <see cref="BmpDibCompression.AlphaBitFields"/>.</summary>
    public const int AlphaBitFieldsMaskLength = 16;

    /// <summary>The stored compression value of uncompressed samples (<c>BI_RGB</c>, <c>BCA_UNCOMP</c>).</summary>
    public const uint CompressionRgb = 0;

    /// <summary>The stored compression value of 8-bit run-length encoding (<c>BI_RLE8</c>, <c>BCA_RLE8</c>).</summary>
    public const uint CompressionRle8 = 1;

    /// <summary>The stored compression value of 4-bit run-length encoding (<c>BI_RLE4</c>, <c>BCA_RLE4</c>).</summary>
    public const uint CompressionRle4 = 2;

    /// <summary>The stored compression value of explicit color masks in a Windows header (<c>BI_BITFIELDS</c>).</summary>
    public const uint CompressionBitFields = 3;

    /// <summary>The stored compression value of an embedded JPEG payload in a Windows header (<c>BI_JPEG</c>).</summary>
    public const uint CompressionJpeg = 4;

    /// <summary>The stored compression value of an embedded PNG payload in a Windows header (<c>BI_PNG</c>).</summary>
    public const uint CompressionPng = 5;

    /// <summary>The stored compression value of explicit color and alpha masks in a Windows header (<c>BI_ALPHABITFIELDS</c>).</summary>
    public const uint CompressionAlphaBitFields = 6;

    /// <summary>The stored compression value of CCITT Group 3 one-dimensional coding in an OS/2 2.x header (<c>BCA_HUFFMAN1D</c>).</summary>
    public const uint Os2CompressionHuffman1D = 3;

    /// <summary>The stored compression value of 24-bit run-length encoding in an OS/2 2.x header (<c>BCA_RLE24</c>).</summary>
    public const uint Os2CompressionRle24 = 4;

    /// <summary>The second byte of the run-length escape that ends the current row.</summary>
    public const byte RunLengthEndOfLine = 0;

    /// <summary>The second byte of the run-length escape that ends the bitmap.</summary>
    public const byte RunLengthEndOfBitmap = 1;

    /// <summary>The second byte of the run-length escape that moves the current position right and up by two following bytes.</summary>
    public const byte RunLengthDelta = 2;

    /// <summary>The <c>bV4CSType</c>/<c>bV5CSType</c> value <c>LCS_sRGB</c> (<c>'sRGB'</c> read as a little-endian 32-bit value).</summary>
    public const uint ColorSpaceSrgb = 0x7352_4742;

    /// <summary>The <c>bV4CSType</c>/<c>bV5CSType</c> value <c>LCS_WINDOWS_COLOR_SPACE</c> (<c>'Win '</c>).</summary>
    public const uint ColorSpaceWindows = 0x5769_6E20;

    /// <summary>The <c>bV5CSType</c> value <c>PROFILE_LINKED</c> (<c>'LINK'</c>).</summary>
    public const uint ColorSpaceProfileLinked = 0x4C49_4E4B;

    /// <summary>The <c>bV5CSType</c> value <c>PROFILE_EMBEDDED</c> (<c>'MBED'</c>).</summary>
    public const uint ColorSpaceProfileEmbedded = 0x4D42_4544;

    /// <summary>The <c>BM</c> signature of a standalone BMP file.</summary>
    public static ReadOnlySpan<byte> Magic => "BM"u8;

    /// <summary>Gets the number of bytes of one row, padded to a multiple of four bytes.</summary>
    /// <param name="width">The positive width, in pixels.</param>
    /// <param name="bitsPerPixel">The number of bits per pixel.</param>
    /// <returns>The row length, in bytes.</returns>
    public static long GetRowLength(int width, int bitsPerPixel) => ((((long)width * bitsPerPixel) + 31) / 32) * 4;

    /// <summary>Creates the exception reported for malformed BMP data.</summary>
    public static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Bmp);

    /// <summary>Creates the exception reported for a recognized but unsupported BMP variant.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Bmp, feature);
}
