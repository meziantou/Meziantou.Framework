using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A parsed and validated DIB header (<c>BITMAPINFOHEADER</c>, <c>BITMAPV2INFOHEADER</c>, <c>BITMAPV3INFOHEADER</c>,
/// <c>BITMAPV4HEADER</c>, <c>BITMAPV5HEADER</c>, or the OS/2 <c>BITMAPCOREHEADER</c> and <c>BITMAPCOREHEADER2</c>), without
/// the standalone-file <c>BITMAPFILEHEADER</c>.
/// </summary>
/// <remarks>
/// The ICO/CUR payload of a device-independent bitmap is the same structure, so nothing here
/// assumes a file header, a pixel-data offset, or that the stored height is the displayed height. Icon payloads only use
/// the Windows headers (<see cref="IsWindowsHeaderLength"/> and the two-argument <see cref="Parse(ReadOnlySpan{byte}, int)"/>).
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BmpInfoHeader
{
    private BmpInfoHeader(BmpHeaderKind kind, int headerLength, int width, int height, bool isTopDown, int bitsPerPixel, BmpDibCompression compression, int paletteEntries, int horizontalPixelsPerMeter, int verticalPixelsPerMeter, uint redMask, uint greenMask, uint blueMask, uint alphaMask, bool hasHeaderMasks, uint colorSpaceType)
    {
        Kind = kind;
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

    /// <summary>Gets the family of the header.</summary>
    public BmpHeaderKind Kind { get; }

    /// <summary>Gets the length of the DIB header, in bytes (12, 16 to 64, 108 or 124).</summary>
    public int HeaderLength { get; }

    /// <summary>Gets the positive width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the positive number of rows (the absolute value of <c>biHeight</c>).</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the first stored row is the top row (<c>biHeight</c> was negative).</summary>
    public bool IsTopDown { get; }

    /// <summary>Gets the number of bits per pixel (1, 4, 8, 16, 24 or 32).</summary>
    public int BitsPerPixel { get; }

    /// <summary>Gets the pixel storage.</summary>
    public BmpDibCompression Compression { get; }

    /// <summary>Gets the number of palette entries stored before the pixel data (0 for layouts without a palette).</summary>
    public int PaletteEntries { get; }

    /// <summary>Gets <c>biXPelsPerMeter</c>, or 0 when the header stores no resolution in pixels per meter.</summary>
    public int HorizontalPixelsPerMeter { get; }

    /// <summary>Gets <c>biYPelsPerMeter</c>, or 0 when the header stores no resolution in pixels per meter.</summary>
    public int VerticalPixelsPerMeter { get; }

    /// <summary>Gets the red mask, or 0 when the layout has no explicit mask.</summary>
    public uint RedMask { get; }

    /// <summary>Gets the green mask, or 0 when the layout has no explicit mask.</summary>
    public uint GreenMask { get; }

    /// <summary>Gets the blue mask, or 0 when the layout has no explicit mask.</summary>
    public uint BlueMask { get; }

    /// <summary>Gets the alpha mask, or 0 when the layout defines no alpha channel.</summary>
    public uint AlphaMask { get; }

    /// <summary>Gets a value indicating whether the masks were read from the header itself (Windows headers of 52 bytes and longer).</summary>
    public bool HasHeaderMasks { get; }

    /// <summary>Gets <c>bV4CSType</c>/<c>bV5CSType</c>, or 0 for headers that have no color-space field.</summary>
    public uint ColorSpaceType { get; }

    /// <summary>Gets a value indicating whether the pixels are palette indexes.</summary>
    public bool IsIndexed => BitsPerPixel <= 8;

    /// <summary>Gets a value indicating whether the pixel data is a run-length encoded stream instead of padded rows.</summary>
    public bool IsRunLength => Compression is BmpDibCompression.Rle8 or BmpDibCompression.Rle4 or BmpDibCompression.Rle24;

    /// <summary>
    /// Gets the length of one palette entry: an <c>RGBTRIPLE</c> after a <c>BITMAPCOREHEADER</c>, an <c>RGBQUAD</c> after
    /// every other header.
    /// </summary>
    public int PaletteEntryLength => Kind == BmpHeaderKind.Os2V1 ? BmpFormat.CorePaletteEntryLength : BmpFormat.PaletteEntryLength;

    /// <summary>Gets the number of palette bytes stored before the pixel data.</summary>
    public int PaletteLength => PaletteEntries * PaletteEntryLength;

    /// <summary>Gets the number of mask bytes stored between a 40-byte header and the palette or pixel data.</summary>
    public int TrailingMaskLength => HasHeaderMasks
        ? 0
        : Compression switch
        {
            BmpDibCompression.BitFields => BmpFormat.BitFieldsMaskLength,
            BmpDibCompression.AlphaBitFields => BmpFormat.AlphaBitFieldsMaskLength,
            _ => 0,
        };

    /// <summary>Gets the number of bytes of one padded row.</summary>
    public long RowLength => BmpFormat.GetRowLength(Width, BitsPerPixel);

    /// <summary>Determines whether a DIB header length is one of the Windows variants, the only ones an icon payload uses.</summary>
    /// <param name="headerLength">The <c>biSize</c> value.</param>
    /// <returns><see langword="true"/> for 40, 52, 56, 108 and 124.</returns>
    public static bool IsWindowsHeaderLength(uint headerLength)
        => headerLength is BmpFormat.InfoHeaderLength or BmpFormat.V2HeaderLength or BmpFormat.V3HeaderLength or BmpFormat.V4HeaderLength or BmpFormat.V5HeaderLength;

    /// <summary>Resolves the family of a DIB header from its length.</summary>
    /// <param name="headerLength">The <c>biSize</c> value.</param>
    /// <param name="kind">The family of the header.</param>
    /// <returns><see langword="false"/> when no known header has this length.</returns>
    public static bool TryGetKind(uint headerLength, out BmpHeaderKind kind)
    {
        if (IsWindowsHeaderLength(headerLength))
        {
            kind = BmpHeaderKind.Windows;
            return true;
        }

        if (headerLength == BmpFormat.CoreHeaderLength)
        {
            kind = BmpHeaderKind.Os2V1;
            return true;
        }

        // BITMAPCOREHEADER2 can stop after any field that follows the bit count. The lengths it shares with the Windows
        // headers were claimed above: the two structures agree on their first 40 bytes
        kind = BmpHeaderKind.Os2V2;
        return headerLength is >= BmpFormat.Os2V2MinHeaderLength and <= BmpFormat.Os2V2HeaderLength;
    }

    /// <summary>Parses and validates a DIB header of any family, excluding any mask bytes that follow a 40-byte header.</summary>
    /// <param name="header">Exactly <paramref name="headerLength"/> bytes, starting at the header size field.</param>
    /// <param name="headerLength">The already-read header size.</param>
    /// <param name="kind">The family <see cref="TryGetKind"/> resolved for <paramref name="headerLength"/>.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="InvalidImageContentException">A field is out of range or inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The variant is recognized but not supported.</exception>
    public static BmpInfoHeader Parse(ReadOnlySpan<byte> header, int headerLength, BmpHeaderKind kind) => kind switch
    {
        BmpHeaderKind.Os2V1 => ParseOs2V1(header),
        BmpHeaderKind.Os2V2 => ParseOs2V2(header, headerLength),
        _ => Parse(header, headerLength),
    };

    /// <summary>Parses and validates a Windows DIB header, excluding any mask bytes that follow a 40-byte header.</summary>
    /// <param name="header">Exactly <paramref name="headerLength"/> bytes, starting at <c>biSize</c>.</param>
    /// <param name="headerLength">The already-read <c>biSize</c> value (<see cref="IsWindowsHeaderLength"/> must be true).</param>
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

        ValidatePlanes(planes);

        BmpDibCompression compression;
        switch (compressionValue)
        {
            case BmpFormat.CompressionRgb:
                compression = BmpDibCompression.Rgb;
                break;

            case BmpFormat.CompressionRle8:
                compression = BmpDibCompression.Rle8;
                break;

            case BmpFormat.CompressionRle4:
                compression = BmpDibCompression.Rle4;
                break;

            case BmpFormat.CompressionBitFields:
                compression = BmpDibCompression.BitFields;
                break;

            case BmpFormat.CompressionAlphaBitFields:
                compression = BmpDibCompression.AlphaBitFields;
                break;

            case BmpFormat.CompressionJpeg:
            case BmpFormat.CompressionPng:
                throw BmpFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The BMP embeds a {(compressionValue == BmpFormat.CompressionJpeg ? "JPEG" : "PNG")} payload (biCompression {compressionValue}), which this version does not decode."), "Compression: embedded codec");

            default:
                throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP compression {compressionValue} is not a defined biCompression value."));
        }

        if (bitsPerPixel is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            if (bitsPerPixel is 2)
                throw BmpFormat.Unsupported("The BMP stores 2 bits per pixel (a Windows CE extension), which this version does not decode.", "Bit depth: 2");

            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP bit count is {bitsPerPixel}; 1, 4, 8, 16, 24 or 32 is expected."));
        }

        if (compression is BmpDibCompression.BitFields or BmpDibCompression.AlphaBitFields && bitsPerPixel is not (16 or 32))
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP declares explicit color masks with {bitsPerPixel} bits per pixel; masks are only defined for 16 and 32 bits."));

        ValidateRunLength(compression, bitsPerPixel, isTopDown: storedHeight < 0);
        var paletteEntries = GetPaletteEntries(colorsUsed, bitsPerPixel);

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
            if (compression is not (BmpDibCompression.BitFields or BmpDibCompression.AlphaBitFields))
            {
                redMask = greenMask = blueMask = alphaMask = 0;
            }
        }

        return new BmpInfoHeader(
            BmpHeaderKind.Windows,
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
    /// Returns a copy that stores fewer palette entries: a <c>BITMAPCOREHEADER</c> has no color count, so a file with a
    /// short palette only says so through the offset of its pixel data, which this type never sees.
    /// </summary>
    /// <param name="paletteEntries">The number of stored entries, from 1 to <see cref="PaletteEntries"/>.</param>
    /// <returns>The header with its palette length.</returns>
    public BmpInfoHeader WithPaletteEntries(int paletteEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(paletteEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(paletteEntries, PaletteEntries);
        return new BmpInfoHeader(Kind, HeaderLength, Width, Height, IsTopDown, BitsPerPixel, Compression, paletteEntries, HorizontalPixelsPerMeter, VerticalPixelsPerMeter, RedMask, GreenMask, BlueMask, AlphaMask, HasHeaderMasks, ColorSpaceType);
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
        if (BitsPerPixel != 32 || Compression != BmpDibCompression.Rgb)
            throw new InvalidOperationException("Only a 32-bit BI_RGB layout has an implicit icon alpha channel.");

        return new BmpInfoHeader(Kind, HeaderLength, Width, Height, IsTopDown, BitsPerPixel, BmpDibCompression.AlphaBitFields, PaletteEntries, HorizontalPixelsPerMeter, VerticalPixelsPerMeter, 0x00FF_0000, 0x0000_FF00, 0x0000_00FF, 0xFF00_0000, hasHeaderMasks: true, ColorSpaceType);
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
        return new BmpInfoHeader(Kind, HeaderLength, Width, Height, IsTopDown, BitsPerPixel, Compression, PaletteEntries, HorizontalPixelsPerMeter, VerticalPixelsPerMeter, red, green, blue, alpha, hasHeaderMasks: true, ColorSpaceType);
    }

    private static BmpInfoHeader ParseOs2V1(ReadOnlySpan<byte> header)
    {
        // BITMAPCOREHEADER: bcSize, then four 16-bit fields. The dimensions are unsigned, so the rows are always bottom-up
        int width = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);

        if (width == 0)
            throw BmpFormat.Invalid("The BMP width is 0: a positive width is expected.");

        if (height == 0)
            throw BmpFormat.Invalid("The BMP height is 0: a non-zero height is expected.");

        ValidatePlanes(planes);
        ValidateOs2BitCount(bitsPerPixel);
        return new BmpInfoHeader(
            BmpHeaderKind.Os2V1,
            BmpFormat.CoreHeaderLength,
            width,
            height,
            isTopDown: false,
            bitsPerPixel,
            BmpDibCompression.Rgb,
            paletteEntries: bitsPerPixel <= 8 ? 1 << bitsPerPixel : 0,
            horizontalPixelsPerMeter: 0,
            verticalPixelsPerMeter: 0,
            redMask: 0,
            greenMask: 0,
            blueMask: 0,
            alphaMask: 0,
            hasHeaderMasks: false,
            colorSpaceType: 0);
    }

    private static BmpInfoHeader ParseOs2V2(ReadOnlySpan<byte> header, int headerLength)
    {
        // BITMAPCOREHEADER2 lays out its first 40 bytes like BITMAPINFOHEADER, then the units, the recording and rendering
        // algorithms, two halftoning sizes, the color encoding and an identifier. A shorter header leaves the rest at zero
        Span<byte> fields = stackalloc byte[BmpFormat.Os2V2HeaderLength];
        fields.Clear();
        header[..headerLength].CopyTo(fields);

        var width = BinaryPrimitives.ReadInt32LittleEndian(fields[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(fields[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(fields[12..]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(fields[14..]);
        var compressionValue = BinaryPrimitives.ReadUInt32LittleEndian(fields[16..]);
        var horizontalResolution = BinaryPrimitives.ReadInt32LittleEndian(fields[24..]);
        var verticalResolution = BinaryPrimitives.ReadInt32LittleEndian(fields[28..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(fields[32..]);
        var units = BinaryPrimitives.ReadUInt16LittleEndian(fields[40..]);
        var recording = BinaryPrimitives.ReadUInt16LittleEndian(fields[44..]);
        var colorEncoding = BinaryPrimitives.ReadUInt32LittleEndian(fields[56..]);

        if (width <= 0)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP width is {width}: a positive width is expected."));

        if (height == 0)
            throw BmpFormat.Invalid("The BMP height is 0: a non-zero height is expected.");

        if (height < 0)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The OS/2 BMP height is {(uint)height}: the height of a BITMAPCOREHEADER2 is unsigned (its rows are always bottom-up) and at most 2147483647."));

        ValidatePlanes(planes);

        BmpDibCompression compression;
        switch (compressionValue)
        {
            case BmpFormat.CompressionRgb:
                compression = BmpDibCompression.Rgb;
                break;

            case BmpFormat.CompressionRle8:
                compression = BmpDibCompression.Rle8;
                break;

            case BmpFormat.CompressionRle4:
                compression = BmpDibCompression.Rle4;
                break;

            case BmpFormat.Os2CompressionRle24:
                compression = BmpDibCompression.Rle24;
                break;

            case BmpFormat.Os2CompressionHuffman1D:
                throw BmpFormat.Unsupported("The OS/2 BMP uses CCITT Group 3 one-dimensional (Huffman 1D) coding, which this version does not decode.", "Compression: Huffman 1D");

            default:
                throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The OS/2 BMP compression {compressionValue} is not a defined BITMAPCOREHEADER2 value."));
        }

        ValidateOs2BitCount(bitsPerPixel);
        ValidateRunLength(compression, bitsPerPixel, isTopDown: false);
        if (recording != 0)
            throw BmpFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OS/2 BMP declares the recording algorithm {recording}; only bottom-up, left-to-right rows (0) are defined."), "Header: OS/2 recording");

        if (colorEncoding != 0)
            throw BmpFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OS/2 BMP declares the color encoding 0x{colorEncoding:X8}; only RGB color tables (0) are decoded."), "Header: OS/2 color encoding");

        var paletteEntries = GetPaletteEntries(colorsUsed, bitsPerPixel);

        // The resolution is only in pixels per meter for the metric unit (0), the single unit OS/2 defines
        var isMetric = units == 0;
        return new BmpInfoHeader(
            BmpHeaderKind.Os2V2,
            headerLength,
            width,
            height,
            isTopDown: false,
            bitsPerPixel,
            compression,
            paletteEntries,
            isMetric ? horizontalResolution : 0,
            isMetric ? verticalResolution : 0,
            redMask: 0,
            greenMask: 0,
            blueMask: 0,
            alphaMask: 0,
            hasHeaderMasks: false,
            colorSpaceType: 0);
    }

    private static void ValidatePlanes(int planes)
    {
        if (planes != 1)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP plane count is {planes}; 1 is the only legal value."));
    }

    private static void ValidateOs2BitCount(int bitsPerPixel)
    {
        if (bitsPerPixel is not (1 or 4 or 8 or 24))
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The OS/2 BMP bit count is {bitsPerPixel}; 1, 4, 8 or 24 is expected."));
    }

    /// <summary>A run-length stream stores pixels of one fixed width, and its rows are always bottom-up.</summary>
    private static void ValidateRunLength(BmpDibCompression compression, int bitsPerPixel, bool isTopDown)
    {
        var expected = compression switch
        {
            BmpDibCompression.Rle8 => 8,
            BmpDibCompression.Rle4 => 4,
            BmpDibCompression.Rle24 => 24,
            _ => 0,
        };

        if (expected == 0)
            return;

        if (bitsPerPixel != expected)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP uses {expected}-bit run-length encoding with {bitsPerPixel} bits per pixel; {expected} is the only legal bit count."));

        if (isTopDown)
            throw BmpFormat.Invalid("The BMP uses run-length encoding with a negative height (top-down rows); a run-length encoded bitmap is always bottom-up.");
    }

    private static int GetPaletteEntries(uint colorsUsed, int bitsPerPixel)
    {
        var isIndexed = bitsPerPixel <= 8;
        var maximumPaletteEntries = 1 << bitsPerPixel;
        if (!isIndexed)
        {
            // A palette after a 16/24/32-bit header is the optional "color importance" table; it is never used to decode
            maximumPaletteEntries = 256;
        }

        if (colorsUsed > (uint)maximumPaletteEntries)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP declares {colorsUsed} palette entries, more than the {maximumPaletteEntries} entries a {bitsPerPixel}-bit layout can use."));

        return colorsUsed != 0 ? (int)colorsUsed : (isIndexed ? 1 << bitsPerPixel : 0);
    }
}
