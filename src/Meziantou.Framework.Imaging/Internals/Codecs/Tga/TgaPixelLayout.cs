using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoded layout of one TGA image: the color map (when the image is color-mapped) and the expansion of one stored row
/// to the lossless working representation (<see cref="PixelFormat.Rgba32"/> when the image descriptor declares alpha bits,
/// otherwise <see cref="PixelFormat.Gray8"/> for grayscale images and <see cref="PixelFormat.Rgb24"/> for the others).
/// </summary>
/// <remarks>
/// <para>
/// The image descriptor is authoritative for alpha: a 32-bit payload whose descriptor declares 0 alpha bits has an
/// unspecified fourth byte, which is discarded instead of being read as transparency. A 16-bit grayscale pixel is read the
/// same way: an 8-bit gray sample followed by an 8-bit attribute, which is the alpha channel when the descriptor declares 8
/// alpha bits and is discarded otherwise. Right-to-left storage (descriptor bit 4) is resolved here; the row order (bit 5)
/// is resolved by the parser, which knows the displayed row index.
/// </para>
/// <para>
/// The color map holds up to 65,535 entries, so its storage is not allocated here: the parser rents
/// <see cref="ColorMapStorageLength"/> bytes from its allocation scope and lends them with <see cref="SetColorMapStorage"/>.
/// Without storage (header and full-scan walks), the entries are not kept and <see cref="ExpandRow"/> is never called.
/// </para>
/// </remarks>
internal sealed class TgaPixelLayout
{
    private readonly int _paletteStride;
    private readonly int _paletteFirstIndex;
    private readonly int _paletteLastIndex;
    private Memory<byte> _palette;

    private TgaPixelLayout(TgaHeader header, PixelFormat sourcePixelFormat, int paletteStride, int paletteFirstIndex, int paletteLastIndex)
    {
        Header = header;
        SourcePixelFormat = sourcePixelFormat;
        _paletteStride = paletteStride;
        _paletteFirstIndex = paletteFirstIndex;
        _paletteLastIndex = paletteLastIndex;
    }

    /// <summary>Gets the parsed header.</summary>
    public TgaHeader Header { get; }

    /// <summary>Gets the layout of the rows produced by <see cref="ExpandRow"/>: <see cref="DefaultPixelFormat"/>, as RGBA for gray with alpha.</summary>
    public PixelFormat SourcePixelFormat { get; }

    /// <summary>Gets the default working representation.</summary>
    public PixelFormat DefaultPixelFormat => DefaultPixelFormats.ForTga(Header.IsGrayscale, HasAlpha);

    /// <summary>Gets a value indicating whether the image descriptor declares a real alpha channel.</summary>
    public bool HasAlpha => Header.AlphaBits > 0;

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel => Header.IsGrayscale
        ? (HasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale)
        : Header.IsColorMapped ? ImageColorModel.Indexed : (HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb);

    /// <summary>Gets the number of bytes of one expanded row.</summary>
    public int SourceRowLength => Header.Width * PixelFormats.GetBytesPerPixel(SourcePixelFormat);

    /// <summary>Gets the number of bytes of one stored row.</summary>
    public int StoredRowLength => Header.Width * Header.BytesPerStoredPixel;

    /// <summary>
    /// Gets the number of bytes that hold the expanded color map (every stored entry, in <see cref="SourcePixelFormat"/>),
    /// or 0 when the image is not color-mapped. At most 65,535 entries of 4 bytes.
    /// </summary>
    public int ColorMapStorageLength => Header.IsColorMapped ? Header.ColorMapLength * _paletteStride : 0;

    /// <summary>Creates the layout of a parsed header.</summary>
    public static TgaPixelLayout Create(TgaHeader header)
    {
        var sourceFormat = PixelFormatWidening.GetWidenedFormat(DefaultPixelFormats.ForTga(header.IsGrayscale, header.AlphaBits > 0));
        if (!header.IsColorMapped)
            return new TgaPixelLayout(header, sourceFormat, 0, 0, -1);

        return new TgaPixelLayout(header, sourceFormat, PixelFormats.GetBytesPerPixel(sourceFormat), header.ColorMapFirstEntry, header.ColorMapFirstEntry + header.ColorMapLength - 1);
    }

    /// <summary>Lends the storage of the expanded color map.</summary>
    /// <param name="storage">Exactly <see cref="ColorMapStorageLength"/> bytes, owned by the caller for as long as rows are expanded.</param>
    public void SetColorMapStorage(Memory<byte> storage)
    {
        if (storage.Length != ColorMapStorageLength)
            throw new ArgumentException("The storage does not have the length of the expanded color map.", nameof(storage));

        _palette = storage;
    }

    /// <summary>Stores one color-map entry; a no-op when no storage was lent.</summary>
    /// <param name="index">The index the entry defines (<see cref="TgaHeader.ColorMapFirstEntry"/> and up).</param>
    /// <param name="entry">The stored entry (<see cref="TgaHeader.BytesPerColorMapEntry"/> bytes).</param>
    public void SetColorMapEntry(int index, ReadOnlySpan<byte> entry)
    {
        if (_palette.IsEmpty)
            return;

        var destination = _palette.Span.Slice((index - _paletteFirstIndex) * _paletteStride, _paletteStride);
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
        else if (Header.IsGrayscale)
        {
            ExpandGrayscale(stored, destination, width);
        }
        else
        {
            switch (Header.PixelDepth)
            {
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
                    PixelConverter.SwapRedAndBlue24(stored[..(width * 3)], destination);
                    break;

                default:
                    if (hasAlpha)
                    {
                        PixelConverter.ConvertRow(PixelFormat.Bgra32, stored[..(width * 4)], PixelFormat.Rgba32, destination);
                    }
                    else
                    {
                        PackedPixels.Bgrx32ToRgb24(stored[..(width * 4)], destination);
                    }

                    break;
            }
        }

        if (Header.IsRightToLeft)
        {
            ReverseRow(destination, width, PixelFormats.GetBytesPerPixel(SourcePixelFormat));
        }
    }

    private void ExpandGrayscale(ReadOnlySpan<byte> stored, Span<byte> destination, int width)
    {
        if (Header.PixelDepth == 8)
        {
            stored[..width].CopyTo(destination);
            return;
        }

        // 16-bit pixels: the gray sample, then the attribute byte
        if (!HasAlpha)
        {
            for (var x = 0; x < width; x++)
            {
                destination[x] = stored[x * 2];
            }

            return;
        }

        for (var x = 0; x < width; x++)
        {
            var gray = stored[x * 2];
            var target = destination[(x * 4)..];
            target[0] = gray;
            target[1] = gray;
            target[2] = gray;
            target[3] = stored[(x * 2) + 1];
        }
    }

    private void ExpandColorMapped(ReadOnlySpan<byte> stored, Span<byte> destination, int width)
    {
        var palette = _palette.Span;
        var stride = _paletteStride;
        var wideIndexes = Header.PixelDepth == 16;
        for (var x = 0; x < width; x++)
        {
            int index = wideIndexes ? BinaryPrimitives.ReadUInt16LittleEndian(stored[(x * 2)..]) : stored[x];
            if (index < _paletteFirstIndex || index > _paletteLastIndex)
                throw TgaFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TGA color-map index {index} at column {x} is outside the stored color map (entries {_paletteFirstIndex} to {_paletteLastIndex})."));

            palette.Slice((index - _paletteFirstIndex) * stride, stride).CopyTo(destination[(x * stride)..]);
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
