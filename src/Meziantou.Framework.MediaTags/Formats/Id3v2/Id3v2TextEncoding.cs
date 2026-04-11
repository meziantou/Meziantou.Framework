using System.Text;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2TextEncoding
{
    // ID3v2 text encoding bytes
    public const byte Latin1 = 0x00;
    public const byte Utf16WithBom = 0x01;
    public const byte Utf16Be = 0x02;
    public const byte Utf8 = 0x03;

    public static string DecodeString(byte encodingByte, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return string.Empty;

        return encodingByte switch
        {
            Latin1 => Latin1Encoding.GetString(TrimNulls(data, 1)),
            Utf16WithBom => DecodeUtf16WithBom(data),
            Utf16Be => Encoding.BigEndianUnicode.GetString(TrimNulls(data, 2)),
            Utf8 => Encoding.UTF8.GetString(TrimNulls(data, 1)),
            _ => Latin1Encoding.GetString(TrimNulls(data, 1)),
        };
    }

    public static byte[] EncodeString(string value)
    {
        // Always encode as UTF-8 (encoding byte 0x03) for ID3v2.4
        var utf8Bytes = Encoding.UTF8.GetBytes(value);
        var result = new byte[1 + utf8Bytes.Length];
        result[0] = Utf8;
        utf8Bytes.CopyTo(result, 1);
        return result;
    }

    public static byte[] EncodeStringV23(string value)
    {
        // For ID3v2.3, use UTF-16 with BOM (encoding byte 0x01)
        var utf16Bytes = Encoding.Unicode.GetBytes(value);
        var result = new byte[1 + 2 + utf16Bytes.Length]; // encoding byte + BOM + string
        result[0] = Utf16WithBom;
        result[1] = 0xFF; // BOM LE
        result[2] = 0xFE;
        utf16Bytes.CopyTo(result, 3);
        return result;
    }

    public static int FindNullTerminator(ReadOnlySpan<byte> data, byte encodingByte, int startIndex)
    {
        if (encodingByte is Utf16WithBom or Utf16Be)
        {
            // Two-byte null terminator
            for (var i = startIndex; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                    return i;
            }
        }
        else
        {
            // Single-byte null terminator
            for (var i = startIndex; i < data.Length; i++)
            {
                if (data[i] == 0)
                    return i;
            }
        }

        return -1;
    }

    public static int NullTerminatorSize(byte encodingByte) => encodingByte is Utf16WithBom or Utf16Be ? 2 : 1;

    private static string DecodeUtf16WithBom(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return string.Empty;

        // Check BOM
        var encoding = data[0] == 0xFE && data[1] == 0xFF
            ? Encoding.BigEndianUnicode
            : Encoding.Unicode;

        return encoding.GetString(TrimNulls(data[2..], 2));
    }

    private static ReadOnlySpan<byte> TrimNulls(ReadOnlySpan<byte> data, int nullSize)
    {
        if (nullSize == 1)
        {
            // Trim trailing single null bytes
            while (data.Length > 0 && data[^1] == 0)
                data = data[..^1];
        }
        else if (nullSize == 2)
        {
            // Trim trailing double null bytes
            while (data.Length >= 2 && data[^2] == 0 && data[^1] == 0)
                data = data[..^2];
        }

        return data;
    }
}
