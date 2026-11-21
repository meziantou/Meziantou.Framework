using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Parses and formats the parameters of Content-Type and Content-Disposition ([RFC 2045] 5.1, [RFC 2231]).</summary>
internal static class MimeParameters
{
    /// <summary>Parses the parameters that follow the value, after the first ';'. Continuations and charsets ([RFC 2231]) are resolved, and encoded words ([RFC 2047]) are decoded.</summary>
    public static Dictionary<string, string> Parse(ref MimeTokenizer tokenizer)
    {
        var simple = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = new Dictionary<string, SortedDictionary<int, (string Value, bool Encoded)>>(StringComparer.OrdinalIgnoreCase);
        while (!tokenizer.IsAtEnd)
        {
            if (!tokenizer.TryConsume(';'))
            {
                // Garbage between parameters: skip to the next ';'
                tokenizer.SkipCfws();
                if (tokenizer.IsAtEnd)
                {
                    break;
                }

                tokenizer.ReadValue(";");
                continue;
            }

            var name = tokenizer.ReadToken("=;");
            if (name.Length == 0 || !tokenizer.TryConsume('='))
            {
                continue;
            }

            var value = tokenizer.ReadValue(";");

            // name*=charset'language'value, name*0=value, name*1*=value
            var star = name.IndexOf('*', StringComparison.Ordinal);
            if (star < 0)
            {
                simple.TryAdd(name, value);
                continue;
            }

            var baseName = name[..star];
            var rest = name[(star + 1)..];
            var encoded = rest.EndsWith('*', StringComparison.Ordinal) || rest.Length == 0;
            var sectionText = rest.TrimEnd('*');
            var section = 0;
            if (sectionText.Length > 0 && !int.TryParse(sectionText, NumberStyles.None, CultureInfo.InvariantCulture, out section))
            {
                continue;
            }

            if (!sections.TryGetValue(baseName, out var parts))
            {
                parts = [];
                sections[baseName] = parts;
            }

            if (parts.Count < 1000)
            {
                parts.TryAdd(section, (value, encoded));
            }
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, parts) in sections)
        {
            result[name] = Combine(parts);
        }

        foreach (var (name, value) in simple)
        {
            result.TryAdd(name, MimeEncoding.DecodeHeaderValue(value));
        }

        return result;
    }

    private static string Combine(SortedDictionary<int, (string Value, bool Encoded)> parts)
    {
        // The charset is in the first section: charset'language'value
        string? charset = null;
        var bytes = new List<byte>();
        var first = true;
        foreach (var (_, (value, encoded)) in parts)
        {
            var text = value;
            if (first && encoded)
            {
                var firstQuote = text.IndexOf('\'', StringComparison.Ordinal);
                var secondQuote = firstQuote < 0 ? -1 : text.IndexOf("'", firstQuote + 1, StringComparison.Ordinal);
                if (secondQuote >= 0)
                {
                    charset = text[..firstQuote];
                    text = text[(secondQuote + 1)..];
                }
            }

            first = false;
            if (encoded)
            {
                for (var i = 0; i < text.Length; i++)
                {
                    if (text[i] == '%' && i + 2 < text.Length && byte.TryParse(text.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    {
                        bytes.Add(b);
                        i += 2;
                    }
                    else
                    {
                        bytes.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
                    }
                }
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(text));
            }
        }

        return MimeEncoding.DecodeText(bytes.ToArray(), charset);
    }

    /// <summary>Formats parameters: quoted when needed, encoded as UTF-8 ([RFC 2231]) when they are not ASCII.</summary>
    public static void Format(StringBuilder builder, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            builder.Append("; ");
            // A value that looks like an encoded word is encoded too, as readers decode encoded words in parameters
            if (MimeEncoding.IsPrintableAscii(value) && !value.Contains("=?", StringComparison.Ordinal))
            {
                builder.Append(name).Append('=').Append(MimeTokenizer.Quote(value));
            }
            else
            {
                builder.Append(name).Append("*=utf-8''");
                foreach (var b in Encoding.UTF8.GetBytes(value))
                {
                    if (b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9') or (byte)'-' or (byte)'.' or (byte)'_')
                    {
                        builder.Append((char)b);
                    }
                    else
                    {
                        builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                    }
                }
            }
        }
    }
}
