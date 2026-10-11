using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded layout of one TGA image: the color map (when the image is color-mapped) and the expansion of one stored row
/// to the lossless working representation (<see cref="PixelFormat.Gray8"/> for grayscale images,
/// <see cref="PixelFormat.Rgba32"/> when the image descriptor declares alpha bits, otherwise <see cref="PixelFormat.Rgb24"/>).
/// </summary>
/// <remarks>
/// The image descriptor is authoritative for alpha: a 32-bit payload whose descriptor declares 0 alpha bits has an
/// unspecified fourth byte, which is discarded instead of being read as transparency. Right-to-left storage (descriptor
/// bit 4) is resolved here; the row order (bit 5) is resolved by the parser, which knows the displayed row index.
/// </remarks>
internal sealed class TgaPixelLayout
{
    private readonly byte[]? _palette;
    private readonly int _paletteStride;
    private readonly int _paletteFirstIndex;
    private readonly int _paletteLastIndex;

    private TgaPixelLayout(TgaHeader header, PixelFormat sourcePixelFormat, byte[]? palette, int paletteStride, int paletteFirstIndex, int paletteLastIndex)
    {
        Header = header;
        SourcePixelFormat = sourcePixelFormat;
        _palette = palette;
        _paletteStride = paletteStride;
        _paletteFirstIndex = paletteFirstIndex;
        _paletteLastIndex = paletteLastIndex;
    }

    /// <summary>Gets the parsed header.</summary>
    public TgaHeader Header { get; }

    /// <summary>Gets the layout of the rows produced by <see cref="ExpandRow"/>.</summary>
    public PixelFormat SourcePixelFormat { get; }

    /// <summary>Gets a value indicating whether the image descriptor declares a real alpha channel.</summary>
    public bool HasAlpha => Header.AlphaBits > 0;

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel => Header.IsGrayscale
        ? ImageColorModel.Grayscale
        : Header.IsColorMapped ? ImageColorModel.Indexed : (HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb);

    /// <summary>Gets the number of bytes of one expanded row.</summary>
    public int SourceRowLength => Header.Width * PixelFormats.GetBytesPerPixel(SourcePixelFormat);

    /// <summary>Gets the number of bytes of one stored row.</summary>
    public int StoredRowLength => Header.Width * Header.BytesPerStoredPixel;

    /// <summary>Creates the layout of a parsed header.</summary>
    public static TgaPixelLayout Create(TgaHeader header)
    {
        var sourceFormat = DefaultPixelFormats.ForTga(header.IsGrayscale, header.AlphaBits > 0);
        if (!header.IsColorMapped)
            return new TgaPixelLayout(header, sourceFormat, palette: null, 0, 0, -1);

        // Only 8-bit indexes are decoded, so entries addressing an index above 255 are consumed but never stored
        var stride = PixelFormats.GetBytesPerPixel(sourceFormat);
        var last = Math.Min(255, header.ColorMapFirstEntry + header.ColorMapLength - 1);
        return new TgaPixelLayout(header, sourceFormat, new byte[256 * stride], stride, header.ColorMapFirstEntry, last);
    }

    /// <summary>Stores one color-map entry.</summary>
    /// <param name="index">The index the entry defines (<see cref="TgaHeader.ColorMapFirstEntry"/> and up).</param>
    /// <param name="entry">The stored entry (<see cref="TgaHeader.BytesPerColorMapEntry"/> bytes).</param>
    public void SetColorMapEntry(int index, ReadOnlySpan<byte> entry)
    {
        if (index > 255 || _palette is null)
            return;

        var destination = _palette.AsSpan(index * _paletteStride, _paletteStride);
        if (Header.ColorMapEntryBits is 15 or 16)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            destination[0] = TgaSamples.Expand5((value >> 10) & 0x1F);
            destination[1] = TgaSamples.Expand5((value >> 5) & 0x1F);
            destination[2] = TgaSamples.Expand5(value & 0x1F);
            if (destination.Length == 4)
            {
                destination[3] = (value & 0x8000) != 0 ? byte.MaxValue : (byte)0;
            }

            return;
        }

        destination[0] = entry[2];
        destination[1] = entry[1];
        destination[2] = entry[0];
        if (destination.Length == 4)
        {
            destination[3] = entry[3];
        }
    }

    /// <summary>Expands one stored row to <see cref="SourcePixelFormat"/>, resolving right-to-left storage.</summary>
    /// <param name="stored">Exactly <see cref="StoredRowLength"/> bytes.</param>
    /// <param name="destination">Exactly <see cref="SourceRowLength"/> bytes.</param>
    /// <exception cref="InvalidImageContentException">A color-map index is outside the stored color map.</exception>
    public void ExpandRow(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var width = Header.Width;
        var hasAlpha = HasAlpha;
        if (Header.IsColorMapped)
        {
            ExpandColorMapped(stored, destination, width);
        }
        else
        {
            switch (Header.PixelDepth)
            {
                case 8:
                    stored[..width].CopyTo(destination);
                    break;

                case 15:
                case 16:
                    for (var x = 0; x < width; x++)
                    {
                        var value = BinaryPrimitives.ReadUInt16LittleEndian(stored[(x * 2)..]);
                        var target = destination[(x * (hasAlpha ? 4 : 3))..];
                        target[0] = TgaSamples.Expand5((value >> 10) & 0x1F);
                        target[1] = TgaSamples.Expand5((value >> 5) & 0x1F);
                        target[2] = TgaSamples.Expand5(value & 0x1F);
                        if (hasAlpha)
                        {
                            target[3] = (value & 0x8000) != 0 ? byte.MaxValue : (byte)0;
                        }
                    }

                    break;

                case 24:
                    for (var x = 0; x < width; x++)
                    {
                        var offset = x * 3;
                        destination[offset] = stored[offset + 2];
                        destination[offset + 1] = stored[offset + 1];
                        destination[offset + 2] = stored[offset];
                    }

                    break;

                default:
                    if (hasAlpha)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var offset = x * 4;
                            destination[offset] = stored[offset + 2];
                            destination[offset + 1] = stored[offset + 1];
                            destination[offset + 2] = stored[offset];
                            destination[offset + 3] = stored[offset + 3];
                        }
                    }
                    else
                    {
                        for (var x = 0; x < width; x++)
                        {
                            destination[x * 3] = stored[(x * 4) + 2];
                            destination[(x * 3) + 1] = stored[(x * 4) + 1];
                            destination[(x * 3) + 2] = stored[x * 4];
                        }
                    }

                    break;
            }
        }

        if (Header.IsRightToLeft)
        {
            ReverseRow(destination, width, PixelFormats.GetBytesPerPixel(SourcePixelFormat));
        }
    }

    private void ExpandColorMapped(ReadOnlySpan<byte> stored, Span<byte> destination, int width)
    {
        var stride = _paletteStride;
        for (var x = 0; x < width; x++)
        {
            var index = stored[x];
            if (index < _paletteFirstIndex || index > _paletteLastIndex)
                throw TgaFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TGA color-map index {index} at column {x} is outside the stored color map (entries {_paletteFirstIndex} to {_paletteLastIndex})."));

            _palette.AsSpan(index * stride, stride).CopyTo(destination[(x * stride)..]);
        }
    }

    private static void ReverseRow(Span<byte> row, int width, int bytesPerPixel)
    {
        for (int left = 0, right = width - 1; left < right; left++, right--)
        {
            var a = row.Slice(left * bytesPerPixel, bytesPerPixel);
            var b = row.Slice(right * bytesPerPixel, bytesPerPixel);
            for (var i = 0; i < bytesPerPixel; i++)
            {
                (a[i], b[i]) = (b[i], a[i]);
            }
        }
    }
}
