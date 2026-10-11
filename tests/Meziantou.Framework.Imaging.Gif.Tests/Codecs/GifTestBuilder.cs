using System.Buffers;
using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// Assembles GIF files with real LZW image data for the GIF decoder tests. The LZW encoder is a test-only,
/// dictionary-based implementation written from the GIF89a specification (independent of the library decoder) with knobs
/// for the edge cases decoders must handle: no initial clear code, several leading clear codes, clear codes every N table
/// entries, a full table without clear code (deferred clear), a missing end code, and codes after the end code.
/// </summary>
internal sealed class GifTestBuilder
{
    private readonly ArrayBufferWriter<byte> _output = new();

    public GifTestBuilder(int width, int height, IReadOnlyList<Rgba32>? globalPalette = null, byte backgroundIndex = 0, string version = "89a")
    {
        Append(Encoding.ASCII.GetBytes("GIF" + version));
        Span<byte> screen = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(screen, (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(screen[2..], (ushort)height);
        var table = globalPalette is null ? null : ColorTable(globalPalette);
        screen[4] = globalPalette is null ? (byte)0 : (byte)(0x80 | 0x70 | GetSizeField(globalPalette.Count));
        screen[5] = backgroundIndex;
        Append(screen);
        if (table is not null)
        {
            Append(table);
        }
    }

    public GifTestBuilder Loop(ushort count, string identifier = "NETSCAPE2.0") => Application(identifier, [1, (byte)count, (byte)(count >> 8)]);

    public GifTestBuilder Application(string identifier, params byte[][] blocks)
    {
        Append([0x21, 0xFF, 11, .. Encoding.ASCII.GetBytes(identifier)]);
        foreach (var block in blocks)
        {
            Append([(byte)block.Length, .. block]);
        }

        Append([0]);
        return this;
    }

    public GifTestBuilder GraphicControl(int disposal, ushort delay, int? transparentIndex = null)
    {
        Append([0x21, 0xF9, 4, (byte)((disposal << 2) | (transparentIndex is null ? 0 : 1)), (byte)delay, (byte)(delay >> 8), (byte)(transparentIndex ?? 0), 0]);
        return this;
    }

    public GifTestBuilder Comment(string text)
    {
        Append([0x21, 0xFE]);
        AppendSubBlocks(Encoding.Latin1.GetBytes(text), 255);
        return this;
    }

    public GifTestBuilder Extension(byte label, params byte[][] blocks)
    {
        Append([0x21, label]);
        foreach (var block in blocks)
        {
            Append([(byte)block.Length, .. block]);
        }

        Append([0]);
        return this;
    }

    /// <summary>Appends an image: descriptor, optional local color table, LZW minimum code size and data sub-blocks.</summary>
    /// <param name="indices">The color indices in display order (row-major); interlaced images are reordered here.</param>
    public GifTestBuilder Image(int left, int top, int width, int height, ReadOnlySpan<byte> indices, int minimumCodeSize = 8, IReadOnlyList<Rgba32>? localPalette = null, bool interlaced = false, GifLzwTestOptions? lzw = null, int subBlockSize = 255)
    {
        if (indices.Length != width * height)
            throw new ArgumentException("One index per pixel is required.", nameof(indices));

        Span<byte> descriptor = stackalloc byte[10];
        descriptor[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[1..], (ushort)left);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[3..], (ushort)top);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[5..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[7..], (ushort)height);
        descriptor[9] = (byte)((interlaced ? 0x40 : 0) | (localPalette is null ? 0 : 0x80 | GetSizeField(localPalette.Count)));
        Append(descriptor);
        if (localPalette is not null)
        {
            Append(ColorTable(localPalette));
        }

        var ordered = interlaced ? Interlace(indices, width, height) : indices.ToArray();
        Append([(byte)minimumCodeSize]);
        AppendSubBlocks(GifLzwTestEncoder.Encode(ordered, minimumCodeSize, lzw ?? new GifLzwTestOptions()), subBlockSize);
        return this;
    }

    /// <summary>Appends an image whose LZW data is given as raw (code, size) pairs (malformed streams).</summary>
    public GifTestBuilder ImageCodes(int left, int top, int width, int height, int minimumCodeSize, params (int Code, int Size)[] codes)
    {
        Span<byte> descriptor = stackalloc byte[10];
        descriptor[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[1..], (ushort)left);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[3..], (ushort)top);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[5..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[7..], (ushort)height);
        Append(descriptor);
        Append([(byte)minimumCodeSize]);
        AppendSubBlocks(GifLzwTestEncoder.Pack(codes), 255);
        return this;
    }

    public GifTestBuilder Raw(ReadOnlySpan<byte> bytes)
    {
        Append(bytes);
        return this;
    }

    public byte[] ToArray(bool trailer = true)
    {
        var data = _output.WrittenSpan.ToArray();
        return trailer ? [.. data, 0x3B] : data;
    }

    /// <summary>The display row stored at each position of an interlaced image (passes 0+8k, 4+8k, 2+4k, 1+2k).</summary>
    public static int[] InterlacedRowOrder(int height)
    {
        var rows = new List<int>();
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var y = start; y < height; y += step)
            {
                rows.Add(y);
            }
        }

        return [.. rows];
    }

    private static byte[] Interlace(ReadOnlySpan<byte> indices, int width, int height)
    {
        var result = new byte[indices.Length];
        var position = 0;
        foreach (var row in InterlacedRowOrder(height))
        {
            indices.Slice(row * width, width).CopyTo(result.AsSpan(position));
            position += width;
        }

        return result;
    }

    private static int GetSizeField(int entries)
    {
        var field = 0;
        while ((2 << field) < entries)
        {
            field++;
        }

        return field;
    }

    private static byte[] ColorTable(IReadOnlyList<Rgba32> colors)
    {
        var sizeField = GetSizeField(colors.Count);
        var table = new byte[(2 << sizeField) * 3];
        for (var i = 0; i < colors.Count; i++)
        {
            table[i * 3] = colors[i].R;
            table[(i * 3) + 1] = colors[i].G;
            table[(i * 3) + 2] = colors[i].B;
        }

        return table;
    }

    private void AppendSubBlocks(ReadOnlySpan<byte> data, int subBlockSize)
    {
        while (!data.IsEmpty)
        {
            var length = Math.Min(subBlockSize, data.Length);
            Append([(byte)length]);
            Append(data[..length]);
            data = data[length..];
        }

        Append([0]);
    }

    private void Append(ReadOnlySpan<byte> data) => _output.Write(data);
}
