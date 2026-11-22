using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Writes content lines: the parameters quoted when they need it, the lines folded at 75 bytes, and the values of vCard 2.1 that are not ASCII in quoted-printable.</summary>
internal static class ContentLineWriter
{
    private const int MaxLineLength = 75;

    public static void Write(StringBuilder output, ContentLine line, bool vcard21)
    {
        if (vcard21 && !IsAscii(line.Value))
        {
            WriteQuotedPrintable(output, line);
            return;
        }

        Fold(output, Format(line, vcard21));
    }

    public static string Format(ContentLine line, bool vcard21)
    {
        var builder = new StringBuilder();
        if (line.Group is not null)
        {
            builder.Append(line.Group).Append('.');
        }

        builder.Append(line.Name);
        foreach (var parameter in line.Parameters)
        {
            builder.Append(';');
            if (parameter.Name is null)
            {
                // A parameter without a name (vCard 2.1) is written as it was read
                builder.AppendJoin(';', parameter.Values);
                continue;
            }

            if (vcard21 && parameter.Name == "TYPE")
            {
                // vCard 2.1 writes the types without a name (TEL;WORK;VOICE), except the ones that are not words
                builder.AppendJoin(';', parameter.Values.Select(value => IsWord(value) ? value.ToUpperInvariant() : "TYPE=" + FormatParameterValue(value)));
                continue;
            }

            builder.Append(parameter.Name).Append('=');
            builder.AppendJoin(',', parameter.Values.Select(FormatParameterValue));
        }

        builder.Append(':').Append(line.Value.Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal));
        return builder.ToString();
    }

    /// <summary>Replaces the line breaks (CRLF, CR and LF, not the other characters <see cref="string.ReplaceLineEndings()"/> knows, such as the form feed).</summary>
    internal static string ReplaceLineBreaks(string value, string replacement) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", replacement, StringComparison.Ordinal);

    /// <summary>Replaces the escaped line breaks of a text (<c>\n</c>, not <c>\\n</c>) by CRLF, and keeps the other escapes: quoted-printable writes the line breaks as <c>=0D=0A</c>.</summary>
    private static string UnescapeLineBreaks(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                if (next is 'n' or 'N')
                {
                    builder.Append("\r\n");
                }
                else
                {
                    builder.Append('\\').Append(next);
                }
            }
            else
            {
                builder.Append(value[i]);
            }
        }

        return ReplaceLineBreaks(builder.ToString(), "\r\n");
    }

    /// <summary>Gets whether a type can be written without a name in vCard 2.1: a word that is not the name of an encoding, which a parameter without a name can also be.</summary>
    private static bool IsWord(string value) => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && !value.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) && !value.Equals("BASE64", StringComparison.OrdinalIgnoreCase)
        && !value.Equals("8BIT", StringComparison.OrdinalIgnoreCase) && !value.Equals("7BIT", StringComparison.OrdinalIgnoreCase);

    private static string FormatParameterValue(string value)
    {
        if (value.Contains('^', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal))
        {
            // [RFC 6868]; vCard 2.1 has no escaping, so the reader decodes it in all the versions
            value = value.Replace("^", "^^", StringComparison.Ordinal).Replace("\n", "^n", StringComparison.Ordinal).Replace("\"", "^'", StringComparison.Ordinal);
        }

        return value.AsSpan().IndexOfAny(":;,") >= 0 ? "\"" + value + "\"" : value;
    }

    /// <summary>Folds a line: a line break then a space every 75 bytes of UTF-8, without splitting a character.</summary>
    private static void Fold(StringBuilder output, string line)
    {
        var length = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var size = char.IsHighSurrogate(c) && i + 1 < line.Length ? 4 : Encoding.UTF8.GetByteCount([c]);
            if (length + size > MaxLineLength)
            {
                output.Append("\r\n ");
                length = 1;
            }

            output.Append(c);
            if (size == 4)
            {
                output.Append(line[++i]);
            }

            length += size;
        }

        output.Append("\r\n");
    }

    /// <summary>Writes a value of vCard 2.1 in quoted-printable, in UTF-8, with soft line breaks.</summary>
    private static void WriteQuotedPrintable(StringBuilder output, ContentLine line)
    {
        var copy = new ContentLine(line.Name, "") { Group = line.Group };
        foreach (var parameter in line.Parameters)
        {
            copy.Parameters.Add(parameter);
        }

        copy.SetParameter("CHARSET", "UTF-8");
        copy.SetParameter("ENCODING", "QUOTED-PRINTABLE");
        var head = Format(copy, vcard21: true);
        output.Append(head);
        var length = head.Length;
        var value = UnescapeLineBreaks(line.Value);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var encoded = b is >= 33 and <= 126 and not (byte)'=' ? ((char)b).ToString() : "=" + b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
            if (length + encoded.Length > MaxLineLength)
            {
                output.Append("=\r\n");
                length = 0;
            }

            output.Append(encoded);
            length += encoded.Length;
        }

        output.Append("\r\n");
    }

    private static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 0x7E || (c < 0x20 && c != '\t'))
            {
                return false;
            }
        }

        return true;
    }
}
