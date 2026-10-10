using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A parsed and validated DIB header (<c>BITMAPINFOHEADER</c>, <c>BITMAPV2INFOHEADER</c>, <c>BITMAPV3INFOHEADER</c>,
/// <c>BITMAPV4HEADER</c> or <c>BITMAPV5HEADER</c>), without the standalone-file <c>BITMAPFILEHEADER</c>.
/// </summary>
/// <remarks>
/// The ICO/CUR payload of a device-independent bitmap is the same structure, so nothing here
/// assumes a file header, a pixel-data offset, or that the stored height is the displayed height.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BmpInfoHeader
{
    private BmpInfoHeader(int headerLength, int width, int height, bool isTopDown, int bitsPerPixel, BmpCompression compression, int paletteEntries, int horizontalPixelsPerMeter, int verticalPixelsPerMeter, uint redMask, uint greenMask, uint blueMask, uint alphaMask, bool hasHeaderMasks, uint colorSpaceType)
    {
        HeaderLength = headerLength;
        Width = width;
        Height = height;
        IsTopDown = isTopDown;
        BitsPerPixel = bitsPerPixel;
        Compression = compression;
        PaletteEntries = paletteEntries;
        HorizontalPixelsPerMeter = horizontalPixelsPerMeter;
        VerticalPixelsPerMeter = verticalPixelsPerMeter;
        RedMask = redMask;
        GreenMask = greenMask;
        BlueMask = blueMask;
        AlphaMask = alphaMask;
        HasHeaderMasks = hasHeaderMasks;
        ColorSpaceType = colorSpaceType;
    }

    /// <summary>Gets the length of the DIB header, in bytes (40, 52, 56, 108 or 124).</summary>
    public int HeaderLength { get; }

    /// <summary>Gets the positive width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the positive number of rows (the absolute value of <c>biHeight</c>).</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the first stored row is the top row (<c>biHeight</c> was negative).</summary>
    public bool IsTopDown { get; }

    /// <summary>Gets the number of bits per pixel (1, 4, 8, 16, 24 or 32).</summary>
    public int BitsPerPixel { get; }

    /// <summary>Gets the compression.</summary>
    public BmpCompression Compression { get; }

    /// <summary>Gets the number of palette entries stored before the pixel data (0 for layouts without a palette).</summary>
    public int PaletteEntries { get; }

    /// <summary>Gets <c>biXPelsPerMeter</c>.</summary>
    public int HorizontalPixelsPerMeter { get; }

    /// <summary>Gets <c>biYPelsPerMeter</c>.</summary>
    public int VerticalPixelsPerMeter { get; }

    /// <summary>Gets the red mask, or 0 when the layout has no explicit mask.</summary>
    public uint RedMask { get; }

    /// <summary>Gets the green mask, or 0 when the layout has no explicit mask.</summary>
    public uint GreenMask { get; }

    /// <summary>Gets the blue mask, or 0 when the layout has no explicit mask.</summary>
    public uint BlueMask { get; }

    /// <summary>Gets the alpha mask, or 0 when the layout defines no alpha channel.</summary>
    public uint AlphaMask { get; }

    /// <summary>Gets a value indicating whether the masks were read from the header itself (52-byte header and longer).</summary>
    public bool HasHeaderMasks { get; }

    /// <summary>Gets <c>bV4CSType</c>/<c>bV5CSType</c>, or 0 for headers that have no color-space field.</summary>
    public uint ColorSpaceType { get; }

    /// <summary>Gets a value indicating whether the pixels are palette indexes.</summary>
    public bool IsIndexed => BitsPerPixel <= 8;

    /// <summary>Gets the number of palette bytes stored before the pixel data.</summary>
    public int PaletteLength => PaletteEntries * BmpFormat.PaletteEntryLength;

    /// <summary>Gets the number of mask bytes stored between a 40-byte header and the palette or pixel data.</summary>
    public int TrailingMaskLength => HasHeaderMasks
        ? 0
        : Compression switch
        {
            BmpCompression.BitFields => BmpFormat.BitFieldsMaskLength,
            BmpCompression.AlphaBitFields => BmpFormat.AlphaBitFieldsMaskLength,
            _ => 0,
        };

    /// <summary>Gets the number of bytes of one padded row.</summary>
    public long RowLength => BmpFormat.GetRowLength(Width, BitsPerPixel);

    /// <summary>Determines whether a DIB header length is one of the supported variants.</summary>
    /// <param name="headerLength">The <c>biSize</c> value.</param>
    /// <returns><see langword="true"/> for 40, 52, 56, 108 and 124.</returns>
    public static bool IsSupportedHeaderLength(uint headerLength)
        => headerLength is BmpFormat.InfoHeaderLength or BmpFormat.V2HeaderLength or BmpFormat.V3HeaderLength or BmpFormat.V4HeaderLength or BmpFormat.V5HeaderLength;

    /// <summary>Parses and validates a DIB header, excluding any mask bytes that follow a 40-byte header.</summary>
    /// <param name="header">Exactly <paramref name="headerLength"/> bytes, starting at <c>biSize</c>.</param>
    /// <param name="headerLength">The already-read <c>biSize</c> value (<see cref="IsSupportedHeaderLength"/> must be true).</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="InvalidImageContentException">A field is out of range or inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The variant is recognized but not supported.</exception>
    public static BmpInfoHeader Parse(ReadOnlySpan<byte> header, int headerLength)
    {
        var width = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
        var compressionValue = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        var horizontalPixelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
        var verticalPixelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(header[28..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);

        if (width <= 0)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP width is {width}: a positive width is expected."));

        if (storedHeight == 0)
            throw BmpFormat.Invalid("The BMP height is 0: a non-zero height is expected.");

        if (storedHeight == int.MinValue)
            throw BmpFormat.Invalid("The BMP height is -2147483648, whose absolute value is not representable.");

        if (planes != 1)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP plane count is {planes}; 1 is the only legal value."));

        var compression = (BmpCompression)compressionValue;
        switch (compression)
        {
            case BmpCompression.Rgb:
            case BmpCompression.BitFields:
            case BmpCompression.AlphaBitFields:
                break;

            case BmpCompression.Rle8:
            case BmpCompression.Rle4:
                throw BmpFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The BMP uses run-length encoding (biCompression {compressionValue}), which this version does not decode."), "Compression: RLE");

            case BmpCompression.Jpeg:
            case BmpCompression.Png:
                throw BmpFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The BMP embeds a {(compression == BmpCompression.Jpeg ? "JPEG" : "PNG")} payload (biCompression {compressionValue}), which this version does not decode."), "Compression: embedded codec");

            default:
                throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP compression {compressionValue} is not a defined biCompression value."));
        }

        if (bitsPerPixel is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            if (bitsPerPixel is 2)
                throw BmpFormat.Unsupported("The BMP stores 2 bits per pixel (a Windows CE extension), which this version does not decode.", "Bit depth: 2");

            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP bit count is {bitsPerPixel}; 1, 4, 8, 16, 24 or 32 is expected."));
        }

        if (compression is BmpCompression.BitFields or BmpCompression.AlphaBitFields && bitsPerPixel is not (16 or 32))
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP declares explicit color masks with {bitsPerPixel} bits per pixel; masks are only defined for 16 and 32 bits."));

        var isIndexed = bitsPerPixel <= 8;
        var maximumPaletteEntries = 1 << bitsPerPixel;
        if (!isIndexed)
        {
            // A palette after a 16/24/32-bit header is the optional "color importance" table; it is never used to decode
            maximumPaletteEntries = 256;
        }

        if (colorsUsed > (uint)maximumPaletteEntries)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP declares {colorsUsed} palette entries, more than the {maximumPaletteEntries} entries a {bitsPerPixel}-bit layout can use."));

        var paletteEntries = colorsUsed != 0 ? (int)colorsUsed : (isIndexed ? 1 << bitsPerPixel : 0);

        uint redMask = 0, greenMask = 0, blueMask = 0, alphaMask = 0, colorSpaceType = 0;
        var hasHeaderMasks = headerLength >= BmpFormat.V2HeaderLength;
        if (hasHeaderMasks)
        {
            redMask = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
            greenMask = BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);
            blueMask = BinaryPrimitives.ReadUInt32LittleEndian(header[48..]);
            if (headerLength >= BmpFormat.V3HeaderLength)
            {
                alphaMask = BinaryPrimitives.ReadUInt32LittleEndian(header[52..]);
            }

            if (headerLength >= BmpFormat.V4HeaderLength)
            {
                colorSpaceType = BinaryPrimitives.ReadUInt32LittleEndian(header[56..]);
            }

            // BITMAPV4HEADER and BITMAPV5HEADER always store the masks, but they only describe the pixels with BI_BITFIELDS
            if (compression is not (BmpCompression.BitFields or BmpCompression.AlphaBitFields))
            {
                redMask = greenMask = blueMask = alphaMask = 0;
            }
        }

        return new BmpInfoHeader(
            headerLength,
            width,
            storedHeight > 0 ? storedHeight : -storedHeight,
            isTopDown: storedHeight < 0,
            bitsPerPixel,
            compression,
            paletteEntries,
            horizontalPixelsPerMeter,
            verticalPixelsPerMeter,
            redMask,
            greenMask,
            blueMask,
            alphaMask,
            hasHeaderMasks,
            colorSpaceType);
    }

    /// <summary>
    /// Returns a copy that reads the fourth byte of a 32-bit <c>BI_RGB</c> layout as a straight alpha channel, which is
    /// the ICO/CUR convention and <em>not</em> the BMP one: in a standalone bitmap that byte is unspecified
    /// padding, and only a declared alpha mask is transparency.
    /// </summary>
    /// <returns>The header with <c>BI_ALPHABITFIELDS</c> and the <c>BGRA</c> masks of a 32-bit icon payload.</returns>
    /// <exception cref="InvalidOperationException">The layout is not 32-bit <c>BI_RGB</c>.</exception>
    public BmpInfoHeader WithIconAlphaChannel()
    {
        if (BitsPerPixel != 32 || Compression != BmpCompression.Rgb)
            throw new InvalidOperationException("Only a 32-bit BI_RGB layout has an implicit icon alpha channel.");

        return new BmpInfoHeader(HeaderLength, Width, Height, IsTopDown, BitsPerPixel, BmpCompression.AlphaBitFields, PaletteEntries, HorizontalPixelsPerMeter, VerticalPixelsPerMeter, 0x00FF_0000, 0x0000_FF00, 0x0000_00FF, 0xFF00_0000, hasHeaderMasks: true, ColorSpaceType);
    }

    /// <summary>Returns a copy with the masks read from the bytes that follow a 40-byte <c>BI_BITFIELDS</c> header.</summary>
    /// <param name="masks">Exactly <see cref="TrailingMaskLength"/> bytes.</param>
    /// <returns>The header with its masks.</returns>
    public BmpInfoHeader WithTrailingMasks(ReadOnlySpan<byte> masks)
    {
        var red = BinaryPrimitives.ReadUInt32LittleEndian(masks);
        var green = BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]);
        var blue = BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]);
        var alpha = masks.Length >= BmpFormat.AlphaBitFieldsMaskLength ? BinaryPrimitives.ReadUInt32LittleEndian(masks[12..]) : 0u;
        return new BmpInfoHeader(HeaderLength, Width, Height, IsTopDown, BitsPerPixel, Compression, PaletteEntries, HorizontalPixelsPerMeter, VerticalPixelsPerMeter, red, green, blue, alpha, hasHeaderMasks: true, ColorSpaceType);
    }
}
