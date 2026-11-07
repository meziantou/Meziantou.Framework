using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Assembles Windows icon and cursor files byte by byte for the decoder tests, independently of the encoder:
/// the <c>ICONDIR</c> header, one <c>ICONDIRENTRY</c> per representation with full control over every field (so that
/// malformed offsets, lengths and hotspots can be built), and the DIB or PNG payloads.
/// </summary>
internal sealed class IcoFileBuilder
{
    private readonly List<IcoEntrySpec> _entries = [];

    /// <summary>Gets the <c>idType</c>: 1 for an icon, 2 for a cursor.</summary>
    public int Type { get; init; } = 1;

    /// <summary>Gets an override of the <c>idReserved</c> field.</summary>
    public ushort Reserved { get; init; }

    /// <summary>Gets an override of the declared entry count.</summary>
    public ushort? CountOverride { get; init; }

    public IcoFileBuilder Add(IcoEntrySpec entry)
    {
        _entries.Add(entry);
        return this;
    }

    public byte[] Build()
    {
        var directoryLength = 6 + (_entries.Count * 16);
        var payloads = new List<byte[]>();
        var buffer = new byte[directoryLength];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, Reserved);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)Type);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), CountOverride ?? (ushort)_entries.Count);

        var offset = directoryLength;
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var destination = buffer.AsSpan(6 + (i * 16), 16);
            destination[0] = entry.DeclaredWidth ?? (byte)(entry.Width == 256 ? 0 : entry.Width);
            destination[1] = entry.DeclaredHeight ?? (byte)(entry.Height == 256 ? 0 : entry.Height);
            destination[2] = entry.ColorCount;
            destination[3] = entry.Reserved;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], entry.PlanesOrHotspotX);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], entry.BitCountOrHotspotY);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], entry.LengthOverride ?? (uint)entry.Payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], entry.OffsetOverride ?? (uint)offset);
            offset += entry.Payload.Length;
            payloads.Add(entry.Payload);
        }

        var result = new List<byte>(buffer);
        foreach (var payload in payloads)
        {
            result.AddRange(payload);
        }

        return [.. result];
    }

    /// <summary>Builds a 32-bit <c>BI_RGB</c> DIB payload with the icon conventions: a doubled height, bottom-up rows and an AND mask.</summary>
    /// <param name="width">The displayed width.</param>
    /// <param name="height">The displayed height.</param>
    /// <param name="pixels">The pixels, top-down, row by row.</param>
    /// <param name="mask">The AND mask bits, top-down, one per pixel (<see langword="true"/> is transparent), or <see langword="null"/> for an all-zero mask.</param>
    /// <param name="includeMask"><see langword="false"/> to leave the mask out of the payload entirely.</param>
    public static byte[] Dib32(int width, int height, Rgba32[] pixels, bool[]? mask = null, bool includeMask = true)
    {
        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            var row = new byte[RowLength(width, 32)];
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(y * width) + x];
                row[x * 4] = pixel.B;
                row[(x * 4) + 1] = pixel.G;
                row[(x * 4) + 2] = pixel.R;
                row[(x * 4) + 3] = pixel.A;
            }

            rows[y] = row;
        }

        return Dib(width, height, 32, palette: null, rows, mask, includeMask);
    }

    /// <summary>Builds a 24-bit <c>BI_RGB</c> DIB payload.</summary>
    public static byte[] Dib24(int width, int height, Rgb24[] pixels, bool[]? mask = null)
    {
        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            var row = new byte[RowLength(width, 24)];
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(y * width) + x];
                row[x * 3] = pixel.B;
                row[(x * 3) + 1] = pixel.G;
                row[(x * 3) + 2] = pixel.R;
            }

            rows[y] = row;
        }

        return Dib(width, height, 24, palette: null, rows, mask, includeMask: true);
    }

    /// <summary>Builds an 8-bit palette DIB payload.</summary>
    public static byte[] Dib8(int width, int height, byte[] indexes, Rgb24[] palette, bool[]? mask = null)
    {
        var table = new byte[palette.Length * 4];
        for (var i = 0; i < palette.Length; i++)
        {
            table[i * 4] = palette[i].B;
            table[(i * 4) + 1] = palette[i].G;
            table[(i * 4) + 2] = palette[i].R;
        }

        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            var row = new byte[RowLength(width, 8)];
            indexes.AsSpan(y * width, width).CopyTo(row);
            rows[y] = row;
        }

        return Dib(width, height, 8, table, rows, mask, includeMask: true);
    }

    /// <summary>Builds a DIB payload from already padded, top-down rows.</summary>
    /// <param name="storedHeightOverride">An override of <c>biHeight</c>, for malformed files (an odd or non-doubled height).</param>
    public static byte[] Dib(int width, int height, int bitCount, byte[]? palette, byte[][] topDownRows, bool[]? mask, bool includeMask, int? storedHeightOverride = null)
    {
        var maskRowLength = RowLength(width, 1);
        var colorLength = topDownRows.Sum(static row => row.Length);
        var maskLength = includeMask ? maskRowLength * height : 0;
        var paletteLength = palette?.Length ?? 0;
        var payload = new byte[40 + paletteLength + colorLength + maskLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 40);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), storedHeightOverride ?? (height * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), (ushort)bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), (uint)(colorLength + maskLength));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32), (uint)(paletteLength / 4));
        palette?.CopyTo(payload.AsSpan(40));

        // Icon color rows are stored bottom-up
        var position = 40 + paletteLength;
        for (var y = height - 1; y >= 0; y--)
        {
            topDownRows[y].CopyTo(payload.AsSpan(position));
            position += topDownRows[y].Length;
        }

        if (includeMask && mask is not null)
        {
            for (var y = height - 1; y >= 0; y--)
            {
                var row = payload.AsSpan(position, maskRowLength);
                for (var x = 0; x < width; x++)
                {
                    if (mask[(y * width) + x])
                    {
                        row[x >> 3] |= (byte)(1 << (7 - (x & 7)));
                    }
                }

                position += maskRowLength;
            }
        }

        return payload;
    }

    private static int RowLength(int width, int bitsPerPixel) => (((width * bitsPerPixel) + 31) / 32) * 4;
}
