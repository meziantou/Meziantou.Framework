using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded layout of one DIB: the resolved channel masks or palette, the row length, and the lossless working
/// representation the rows are expanded to (<see cref="PixelFormat.Rgb24"/>, or <see cref="PixelFormat.Rgba32"/> when the
/// layout defines a real alpha channel).
/// </summary>
/// <remarks>
/// <para>
/// A 32-bit <c>BI_RGB</c> layout has no alpha mask: its fourth byte is unspecified padding (<c>BGRX</c>), not transparency,
/// and is discarded. Alpha is only decoded when an explicit non-zero alpha mask is present (<c>BI_ALPHABITFIELDS</c>, or a
/// <c>BITMAPV3INFOHEADER</c>/<c>BITMAPV4HEADER</c>/<c>BITMAPV5HEADER</c> alpha mask with <c>BI_BITFIELDS</c>).
/// </para>
/// <para>This type never looks at the standalone-file header, so the ICO/CUR payload parser can reuse it as is.</para>
/// </remarks>
internal sealed class BmpDibLayout
{
    private readonly BmpChannelMask _red;
    private readonly BmpChannelMask _green;
    private readonly BmpChannelMask _blue;
    private readonly BmpChannelMask _alpha;
    private byte[]? _palette;

    private BmpDibLayout(BmpInfoHeader header, BmpChannelMask red, BmpChannelMask green, BmpChannelMask blue, BmpChannelMask alpha)
    {
        Header = header;
        _red = red;
        _green = green;
        _blue = blue;
        _alpha = alpha;
    }

    /// <summary>Gets the parsed DIB header.</summary>
    public BmpInfoHeader Header { get; }

    /// <summary>Gets a value indicating whether the layout defines a real (non-padding) alpha channel.</summary>
    public bool HasAlpha => _alpha.IsPresent;

    /// <summary>Gets the layout of the rows produced by <see cref="ExpandRow"/>.</summary>
    public PixelFormat SourcePixelFormat => DefaultPixelFormats.ForBmp(HasAlpha);

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel => Header.IsIndexed ? ImageColorModel.Indexed : (HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb);

    /// <summary>
    /// Gets the reported precision of the encoded samples: the index width for indexed layouts, otherwise 8 (the channels of
    /// a 16-bit layout are 5 or 6 bits wide and are expanded to 8).
    /// </summary>
    public int BitsPerComponent => Header.IsIndexed ? Header.BitsPerPixel : 8;

    /// <summary>Resolves and validates the channel masks of a DIB header.</summary>
    /// <param name="header">The parsed header, including any mask bytes that followed it.</param>
    /// <returns>The layout; <see cref="SetPalette"/> must be called for indexed layouts.</returns>
    /// <exception cref="InvalidImageContentException">A mask is not contiguous, overlaps another, or a required mask is missing.</exception>
    /// <exception cref="UnsupportedImageFeatureException">A channel is wider than 8 bits.</exception>
    public static BmpDibLayout Create(BmpInfoHeader header)
    {
        if (header.IsIndexed)
            return new BmpDibLayout(header, default, default, default, default);

        uint redMask = header.RedMask, greenMask = header.GreenMask, blueMask = header.BlueMask, alphaMask = header.AlphaMask;
        if (header.Compression is BmpCompression.BitFields or BmpCompression.AlphaBitFields)
        {
            if (redMask == 0 || greenMask == 0 || blueMask == 0)
                throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP declares explicit color masks but the red, green or blue mask is zero (0x{redMask:X8}, 0x{greenMask:X8}, 0x{blueMask:X8})."));
        }
        else
        {
            // BI_RGB defaults: 5-5-5 for 16 bits, BGRX for 32 bits (the fourth byte is padding), BGR bytes for 24 bits
            (redMask, greenMask, blueMask) = header.BitsPerPixel switch
            {
                16 => (0x7C00u, 0x03E0u, 0x001Fu),
                32 => (0x00FF_0000u, 0x0000_FF00u, 0x0000_00FFu),
                _ => (0u, 0u, 0u),
            };

            alphaMask = 0;
        }

        if (header.BitsPerPixel == 24)
            return new BmpDibLayout(header, default, default, default, default);

        var red = BmpChannelMask.Create(redMask, "red", header.BitsPerPixel);
        var green = BmpChannelMask.Create(greenMask, "green", header.BitsPerPixel);
        var blue = BmpChannelMask.Create(blueMask, "blue", header.BitsPerPixel);
        var alpha = BmpChannelMask.Create(alphaMask, "alpha", header.BitsPerPixel);
        var overlap = (red.Mask & green.Mask) | (red.Mask & blue.Mask) | (green.Mask & blue.Mask) | (alpha.Mask & (red.Mask | green.Mask | blue.Mask));
        if (overlap != 0)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP color masks overlap (0x{red.Mask:X8}, 0x{green.Mask:X8}, 0x{blue.Mask:X8}, 0x{alpha.Mask:X8})."));

        return new BmpDibLayout(header, red, green, blue, alpha);
    }

    /// <summary>Adopts the palette stored before the pixel data.</summary>
    /// <param name="palette">Exactly <see cref="BmpInfoHeader.PaletteLength"/> bytes of <c>RGBQUAD</c> entries (blue, green, red, reserved).</param>
    public void SetPalette(ReadOnlySpan<byte> palette)
    {
        var entries = Header.PaletteEntries;
        var table = new byte[entries * 3];
        for (var i = 0; i < entries; i++)
        {
            var source = palette.Slice(i * BmpFormat.PaletteEntryLength, BmpFormat.PaletteEntryLength);
            table[(i * 3) + 0] = source[2];
            table[(i * 3) + 1] = source[1];
            table[(i * 3) + 2] = source[0];
        }

        _palette = table;
    }

    /// <summary>Expands one stored row to <see cref="SourcePixelFormat"/>.</summary>
    /// <param name="source">Exactly <see cref="BmpInfoHeader.RowLength"/> bytes (padding included).</param>
    /// <param name="destination">Exactly <c>Width</c> pixels of <see cref="SourcePixelFormat"/>.</param>
    /// <exception cref="InvalidImageContentException">A palette index is outside the palette.</exception>
    public void ExpandRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var width = Header.Width;
        switch (Header.BitsPerPixel)
        {
            case 1:
            case 4:
            case 8:
                ExpandIndexedRow(source, destination, width, Header.BitsPerPixel);
                break;

            case 16:
            {
                var hasAlpha = HasAlpha;
                var step = hasAlpha ? 4 : 3;
                for (var x = 0; x < width; x++)
                {
                    uint pixel = BinaryPrimitives.ReadUInt16LittleEndian(source[(x * 2)..]);
                    var offset = x * step;
                    destination[offset] = _red.Read(pixel);
                    destination[offset + 1] = _green.Read(pixel);
                    destination[offset + 2] = _blue.Read(pixel);
                    if (hasAlpha)
                    {
                        destination[offset + 3] = _alpha.Read(pixel);
                    }
                }

                break;
            }

            case 24:
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 3;
                    destination[offset] = source[offset + 2];
                    destination[offset + 1] = source[offset + 1];
                    destination[offset + 2] = source[offset];
                }

                break;

            default:
            {
                var hasAlpha = HasAlpha;
                var step = hasAlpha ? 4 : 3;
                for (var x = 0; x < width; x++)
                {
                    var pixel = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
                    var offset = x * step;
                    destination[offset] = _red.Read(pixel);
                    destination[offset + 1] = _green.Read(pixel);
                    destination[offset + 2] = _blue.Read(pixel);
                    if (hasAlpha)
                    {
                        destination[offset + 3] = _alpha.Read(pixel);
                    }
                }

                break;
            }
        }
    }

    private void ExpandIndexedRow(ReadOnlySpan<byte> source, Span<byte> destination, int width, int bitsPerPixel)
    {
        var palette = _palette ?? throw new InvalidOperationException("The BMP palette was not read.");
        var entries = palette.Length / 3;
        for (var x = 0; x < width; x++)
        {
            int index;
            switch (bitsPerPixel)
            {
                case 1:
                    index = (source[x >> 3] >> (7 - (x & 7))) & 1;
                    break;

                case 4:
                    index = (x & 1) == 0 ? source[x >> 1] >> 4 : source[x >> 1] & 0x0F;
                    break;

                default:
                    index = source[x];
                    break;
            }

            if (index >= entries)
                throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP palette index {index} at column {x} is outside the {entries}-entry palette."));

            palette.AsSpan(index * 3, 3).CopyTo(destination[(x * 3)..]);
        }
    }
}
