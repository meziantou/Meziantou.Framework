using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>The encodings of MIME: base64 and quoted-printable ([RFC 2045] 6), encoded words ([RFC 2047]) and charsets.</summary>
internal static class MimeEncoding
{
    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>Returns the encoding of a charset name, or <see langword="null"/> when .NET does not know it.</summary>
    public static Encoding? GetCharset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Trim().Trim('"');
        if (name.Equals("utf8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        if (name.Equals("us-ascii", StringComparison.OrdinalIgnoreCase) || name.Equals("ascii", StringComparison.OrdinalIgnoreCase))
        {
            // Many messages declare us-ascii and contain Latin-1 characters
            return Encoding.Latin1;
        }

        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(name) ?? Encoding.GetEncoding(name);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Decodes text: with the charset when it is known, as UTF-8 when the bytes are valid UTF-8, as Latin-1 otherwise.</summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes, string? charset)
    {
        var encoding = GetCharset(charset);
        if (encoding is not null and not UTF8Encoding)
        {
            return encoding.GetString(bytes);
        }

        return DecodeUtf8OrLatin1(bytes);
    }

    public static string DecodeUtf8OrLatin1(ReadOnlySpan<byte> bytes)
    {
        if (System.Text.Unicode.Utf8.IsValid(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>Decodes base64, ignoring the characters that are not part of the alphabet (line breaks, spaces, garbage).</summary>
    public static byte[] DecodeBase64(ReadOnlySpan<byte> data)
    {
        var output = new byte[(data.Length * 3 / 4) + 3];
        var length = 0;
        var buffer = 0;
        var bits = 0;
        foreach (var c in data)
        {
            int value;
            if (c is >= (byte)'A' and <= (byte)'Z')
            {
                value = c - 'A';
            }
            else if (c is >= (byte)'a' and <= (byte)'z')
            {
                value = c - 'a' + 26;
            }
            else if (c is >= (byte)'0' and <= (byte)'9')
            {
                value = c - '0' + 52;
            }
            else if (c is (byte)'+' or (byte)'-')
            {
                value = 62;
            }
            else if (c is (byte)'/' or (byte)'_')
            {
                value = 63;
            }
            else if (c == '=')
            {
                // Padding ends the current group
                bits = 0;
                buffer = 0;
                continue;
            }
            else
            {
                continue;
            }

            buffer = (buffer << 6) | value;
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                output[length++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return output.AsSpan(0, length).ToArray();
    }

    /// <summary>Encodes base64 in lines of 76 characters separated by CRLF.</summary>
    public static byte[] EncodeBase64(ReadOnlySpan<byte> data)
    {
        var text = Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks);
        return Encoding.ASCII.GetBytes(text.Length == 0 ? "" : text + "\r\n");
    }

    /// <summary>Decodes quoted-printable: soft line breaks are removed, invalid escapes are kept as they are.</summary>
    public static byte[] DecodeQuotedPrintable(ReadOnlySpan<byte> data, bool encodedWord = false)
    {
        var output = new byte[data.Length];
        var length = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c == '=')
            {
                if (i + 2 < data.Length && IsHex(data[i + 1]) && IsHex(data[i + 2]))
                {
                    output[length++] = (byte)((HexValue(data[i + 1]) << 4) | HexValue(data[i + 2]));
                    i += 2;
                    continue;
                }

                // Soft line break: '=' followed by optional spaces and a line break
                var j = i + 1;
                while (j < data.Length && data[j] is (byte)' ' or (byte)'\t')
                {
                    j++;
                }

                if (j < data.Length && data[j] is (byte)'\r' or (byte)'\n')
                {
                    if (data[j] == '\r' && j + 1 < data.Length && data[j + 1] == '\n')
                    {
                        j++;
                    }

                    i = j;
                    continue;
                }

                if (j == data.Length)
                {
                    break;
                }
            }
            else if (encodedWord && c == '_')
            {
                output[length++] = (byte)' ';
                continue;
            }

            output[length++] = c;
        }

        return output.AsSpan(0, length).ToArray();
    }

    /// <summary>Encodes quoted-printable with CRLF line breaks and lines of at most 76 characters.</summary>
    public static byte[] EncodeQuotedPrintable(ReadOnlySpan<byte> data)
    {
        var output = new ArrayBufferWriter<byte>(Math.Max(16, data.Length + (data.Length / 3)));
        var lineLength = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c == '\r' && i + 1 < data.Length && data[i + 1] == '\n')
            {
                WriteBytes(output, "\r\n"u8);
                lineLength = 0;
                i++;
                continue;
            }

            if (c == '\n')
            {
                WriteBytes(output, "\r\n"u8);
                lineLength = 0;
                continue;
            }

            // A space or a tab at the end of a line must be encoded
            var isLineEnd = i + 1 == data.Length || data[i + 1] is (byte)'\r' or (byte)'\n';
            var literal = (c is >= 33 and <= 126 && c != '=') || (c is (byte)' ' or (byte)'\t' && !isLineEnd);
            var size = literal ? 1 : 3;
            if (lineLength + size > 75)
            {
                WriteBytes(output, "=\r\n"u8);
                lineLength = 0;
            }

            if (literal)
            {
                WriteBytes(output, [c]);
            }
            else
            {
                WriteBytes(output, [(byte)'=', (byte)HexDigits[c >> 4], (byte)HexDigits[c & 0xF]]);
            }

            lineLength += size;
        }

        return output.WrittenSpan.ToArray();
    }

    /// <summary>Decodes the encoded words of a header value ([RFC 2047]). Whitespace between two encoded words is removed.</summary>
    public static string DecodeHeaderValue(string value)
    {
        if (!value.Contains("=?", StringComparison.Ordinal))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        var position = 0;
        var previousWasEncoded = false;
        while (position < value.Length)
        {
            var start = value.IndexOf("=?", position, StringComparison.Ordinal);
            if (start < 0)
            {
                result.Append(value, position, value.Length - position);
                break;
            }

            if (!TryDecodeEncodedWord(value, start, out var decoded, out var end))
            {
                result.Append(value, position, start + 2 - position);
                position = start + 2;
                previousWasEncoded = false;
                continue;
            }

            var between = value.AsSpan(position, start - position);
            if (!(previousWasEncoded && between.IsWhiteSpace()))
            {
                result.Append(between);
            }

            result.Append(decoded);
            position = end;
            previousWasEncoded = true;
        }

        return result.ToString();
    }

    private static bool TryDecodeEncodedWord(string value, int start, out string decoded, out int end)
    {
        decoded = "";
        end = 0;

        // =?charset?encoding?text?=
        var charsetEnd = value.IndexOf("?", start + 2, StringComparison.Ordinal);
        if (charsetEnd < 0 || charsetEnd + 2 >= value.Length || value[charsetEnd + 2] != '?')
        {
            return false;
        }

        var encodingChar = char.ToUpperInvariant(value[charsetEnd + 1]);
        if (encodingChar is not ('B' or 'Q'))
        {
            return false;
        }

        var textStart = charsetEnd + 3;
        var textEnd = value.IndexOf("?=", textStart, StringComparison.Ordinal);
        if (textEnd < 0)
        {
            return false;
        }

        var charset = value[(start + 2)..charsetEnd];
        var language = charset.IndexOf('*', StringComparison.Ordinal);
        if (language >= 0)
        {
            charset = charset[..language];
        }

        var text = Encoding.Latin1.GetBytes(value[textStart..textEnd]);
        var bytes = encodingChar == 'B' ? DecodeBase64(text) : DecodeQuotedPrintable(text, encodedWord: true);
        decoded = DecodeText(bytes, charset);
        end = textEnd + 2;
        return true;
    }

    /// <summary>Encodes a text as encoded words when it is not printable ASCII ([RFC 2047]), in words of at most 75 characters.</summary>
    public static string EncodeHeaderText(string text)
    {
        if (IsPrintableAscii(text) && !text.Contains("=?", StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder();
        var chunkStart = 0;
        var position = 0;
        while (position < text.Length)
        {
            // Never split a surrogate pair: each word must be a complete UTF-8 sequence
            var length = char.IsHighSurrogate(text[position]) && position + 1 < text.Length ? 2 : 1;
            var candidate = Encoding.UTF8.GetByteCount(text.AsSpan(chunkStart, position + length - chunkStart));
            if (candidate > 45 && position > chunkStart)
            {
                AppendWord(result, text.AsSpan(chunkStart, position - chunkStart));
                chunkStart = position;
            }

            position += length;
        }

        AppendWord(result, text.AsSpan(chunkStart));
        return result.ToString();

        static void AppendWord(StringBuilder builder, ReadOnlySpan<char> chunk)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append("=?utf-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk.ToString()))).Append("?=");
        }
    }

    public static bool IsPrintableAscii(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is < ' ' or > '~' && c != '\t')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHex(byte c) => c is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'A' and <= (byte)'F') or (>= (byte)'a' and <= (byte)'f');

    private static int HexValue(byte c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static void WriteBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> bytes) => writer.Write(bytes);
}
