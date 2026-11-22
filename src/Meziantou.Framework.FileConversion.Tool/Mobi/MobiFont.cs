using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>A font of a KF8 book: a FONT record whose font file can be compressed (zlib) and obfuscated (XOR with a key).</summary>
internal static class MobiFont
{
    // The maximum size of a decompressed font, and the number of bytes the key obfuscates
    private const int MaxFontLength = 32 * 1024 * 1024;
    private const int ObfuscatedLength = 1040;

    private const uint CompressedFlag = 0b01;
    private const uint ObfuscatedFlag = 0b10;

    public static bool IsFont(ReadOnlySpan<byte> record) => record.StartsWith("FONT"u8);

    /// <summary>Decodes the font file of a FONT record.</summary>
    /// <exception cref="InvalidDataException">The record is not valid.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> record)
    {
        if (record.Length < 24 || !IsFont(record))
            throw new InvalidDataException("The FONT record of the book is not valid");

        var length = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
        var flags = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
        var dataStart = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
        var keyLength = BinaryPrimitives.ReadUInt32BigEndian(record[16..]);
        var keyStart = BinaryPrimitives.ReadUInt32BigEndian(record[20..]);
        if (dataStart > record.Length)
            throw new InvalidDataException("The FONT record of the book is truncated");

        var data = record[(int)dataStart..].ToArray();
        if ((flags & ObfuscatedFlag) is not 0)
        {
            if (keyLength is 0 || keyStart > record.Length || keyLength > record.Length - keyStart)
                throw new InvalidDataException("The key of the FONT record of the book is not valid");

            var key = record.Slice((int)keyStart, (int)keyLength);
            for (var i = 0; i < Math.Min(ObfuscatedLength, data.Length); i++)
            {
                data[i] ^= key[i % key.Length];
            }
        }

        if ((flags & CompressedFlag) is 0)
            return data;

        if (length > MaxFontLength)
            throw new InvalidDataException("The font of the book is too large");

        using var stream = new ZLibStream(new MemoryStream(data, writable: false), CompressionMode.Decompress);
        var result = new byte[length];
        var read = stream.ReadAtLeast(result, result.Length, throwOnEndOfStream: false);
        if (read != result.Length || stream.ReadByte() >= 0)
            throw new InvalidDataException("The font of the book does not have the length of its FONT record");

        return result;
    }

    /// <summary>Gets the media type of a font file from its signature.</summary>
    public static string GetMediaType(ReadOnlySpan<byte> font)
    {
        if (font.StartsWith("OTTO"u8))
            return "font/otf";

        if (font.StartsWith((ReadOnlySpan<byte>)[0, 1, 0, 0]) || font.StartsWith("true"u8))
            return "font/ttf";

        if (font.StartsWith("wOFF"u8))
            return "font/woff";

        if (font.StartsWith("wOF2"u8))
            return "font/woff2";

        return "application/octet-stream";
    }
}
