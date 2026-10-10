using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>Builds byte strings field by field (the struct.pack equivalents).</summary>
internal sealed class ByteBuilder
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public ByteBuilder U8(int value)
    {
        _bytes.Add(checked((byte)value));
        return this;
    }

    public ByteBuilder U16BE(int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder U16LE(int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, checked((ushort)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder I16LE(int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, checked((short)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder U32BE(long value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, checked((uint)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder U32LE(long value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, checked((uint)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder I32BE(long value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, checked((int)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder I32LE(long value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, checked((int)value));
        _bytes.AddRange(buffer);
        return this;
    }

    public ByteBuilder Bytes(ReadOnlySpan<byte> value)
    {
        _bytes.AddRange(value);
        return this;
    }

    public ByteBuilder Bytes(IEnumerable<byte> value)
    {
        _bytes.AddRange(value);
        return this;
    }

    /// <summary>ASCII (or Latin-1) text, one byte per character.</summary>
    public ByteBuilder Ascii(string value) => Bytes(Infrastructure.Bytes.Latin1(value));

    public ByteBuilder Zeros(int count)
    {
        for (var i = 0; i < count; i++)
            _bytes.Add(0);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}

internal static class Bytes
{
    public static byte[] Concat(params IEnumerable<byte[]> parts)
    {
        var result = new List<byte>();
        foreach (var part in parts)
            result.AddRange(part);
        return [.. result];
    }

    /// <summary>Python slice semantics: negative indices count from the end, out-of-range bounds are clamped.</summary>
    public static byte[] Slice(byte[] data, int? start, int? end = null)
    {
        var length = data.Length;
        var s = Clamp(start ?? 0, length);
        var e = Clamp(end ?? length, length);
        return e <= s ? [] : data[s..e];

        static int Clamp(int index, int length)
        {
            if (index < 0)
                index += length;
            return Math.Clamp(index, 0, length);
        }
    }

    public static byte[] Latin1(string value) => Encoding.Latin1.GetBytes(value);

    public static string Latin1(ReadOnlySpan<byte> value) => Encoding.Latin1.GetString(value);

    public static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    public static bool StartsWith(byte[] data, ReadOnlySpan<byte> prefix) => data.AsSpan().StartsWith(prefix);

    public static bool Equal(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.SequenceEqual(b);

    public static int Find(ReadOnlySpan<byte> data, ReadOnlySpan<byte> value, int start = 0)
    {
        var index = data[start..].IndexOf(value);
        return index < 0 ? -1 : index + start;
    }

    public static ushort U16BE(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    public static ushort U16LE(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    public static uint U32BE(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);

    public static uint U32LE(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static int I32BE(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32BigEndian(data[offset..]);

    public static int I32LE(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>zlib.decompress.</summary>
    public static byte[] ZlibDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static readonly uint[] CrcTable = CreateCrcTable();

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    /// <summary>zlib.crc32 (ISO-HDLC CRC-32).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>zlib.adler32.</summary>
    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }
}
