using System.Buffers;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Fuzzing;

/// <summary>
/// Converts the bytes of an input to a text that can be any sequence of UTF-16 code units, unpaired surrogates included:
/// valid UTF-8 decodes as usual, a surrogate encoded as UTF-8 (<c>ED A0..BF 80..BF</c>, as in WTF-8) decodes to that
/// surrogate, and every other invalid byte <c>b</c> decodes to the unpaired surrogate <c>U+DC00 + b</c>. The seeds stay
/// readable UTF-8 files while the fuzzer can still reach unpaired surrogates.
/// </summary>
public static class FuzzText
{
    public static string Decode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder(data.Length);
        Span<char> pair = stackalloc char[2];
        while (!data.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(data, out var rune, out var consumed) is OperationStatus.Done)
            {
                if (rune.IsBmp)
                {
                    builder.Append((char)rune.Value);
                }
                else
                {
                    builder.Append(pair[..rune.EncodeToUtf16(pair)]);
                }

                data = data[consumed..];
            }
            else if (data.Length >= 3 && data[0] is 0xED && data[1] is >= 0xA0 and <= 0xBF && data[2] is >= 0x80 and <= 0xBF)
            {
                builder.Append((char)(0xD000 | ((data[1] & 0x3F) << 6) | (data[2] & 0x3F)));
                data = data[3..];
            }
            else
            {
                builder.Append((char)(0xDC00 | data[0]));
                data = data[1..];
            }
        }

        return builder.ToString();
    }

    /// <summary>Encodes a text so that <see cref="Decode"/> returns it unchanged: UTF-8, with unpaired surrogates encoded as three bytes.</summary>
    public static byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new List<byte>(text.Length);
        Span<byte> buffer = stackalloc byte[4];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var length = new Rune(c, text[i + 1]).EncodeToUtf8(buffer);
                result.AddRange(buffer[..length]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                result.Add(0xED);
                result.Add((byte)(0x80 | ((c >> 6) & 0x3F)));
                result.Add((byte)(0x80 | (c & 0x3F)));
            }
            else
            {
                var length = new Rune(c).EncodeToUtf8(buffer);
                result.AddRange(buffer[..length]);
            }
        }

        return [.. result];
    }

    /// <summary>Quotes a text for a message, escaping the control characters, the backslash, the quote and the unpaired surrogates.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c) || char.IsSurrogate(c))
                    {
                        builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
