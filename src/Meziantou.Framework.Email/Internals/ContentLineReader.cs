using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Reads the content lines of vCards and iCalendar objects: unfolds the lines, splits the group, the name, the parameters and the value, and decodes the values in quoted-printable of vCard 2.1.</summary>
internal static class ContentLineReader
{
    /// <summary>Decodes the bytes of a file: UTF-8 (with or without its byte order mark), or Latin-1 when the bytes are not UTF-8.</summary>
    public static string DecodeFile(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            data = data[3..];
        }

        return System.Text.Unicode.Utf8.IsValid(data) ? Encoding.UTF8.GetString(data) : Encoding.Latin1.GetString(data);
    }

    public static List<ContentLine> Parse(string text)
    {
        var result = new List<ContentLine>();
        foreach (var line in Unfold(text))
        {
            if (TryParseLine(line) is { } contentLine)
            {
                result.Add(contentLine);
            }
        }

        return result;
    }

    /// <summary>Joins the lines that start with a space or a tab to the previous line, and the soft line breaks (<c>=</c> at the end of a line) of the quoted-printable values of vCard 2.1.</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var lines = text.Split('\n');
        var current = new StringBuilder();
        var hasCurrent = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i] is [.., '\r'] ? lines[i][..^1] : lines[i];
            if (hasCurrent && line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }

            if (hasCurrent && IsQuotedPrintableContinuation(current))
            {
                current.Length--;
                current.Append(line);
                continue;
            }

            if (hasCurrent)
            {
                yield return current.ToString();
            }

            current.Clear().Append(line);
            hasCurrent = true;
        }

        if (hasCurrent)
        {
            yield return current.ToString();
        }
    }

    private static bool IsQuotedPrintableContinuation(StringBuilder line)
    {
        if (line.Length == 0 || line[^1] != '=')
        {
            return false;
        }

        // Only the encoding that decodes the value makes "=" a soft line break, not a parameter whose value is QUOTED-PRINTABLE (X-A=QUOTED-PRINTABLE)
        var text = line.ToString();
        var colon = FindValueStart(text);
        return colon > 0 && IsQuotedPrintable(SplitParameters(text[..colon]).Skip(1).Select(ParseParameter));
    }

    /// <summary>Gets whether the value of a line is in quoted-printable: its last encoding is <c>ENCODING=QUOTED-PRINTABLE</c> or <c>QUOTED-PRINTABLE</c> (vCard 2.1).</summary>
    private static bool IsQuotedPrintable(IEnumerable<ContentLineParameter> parameters) => parameters.LastOrDefault(IsEncoding) is { } encoding && IsQuotedPrintable(encoding);

    private static bool IsQuotedPrintable(ContentLineParameter parameter) => IsEncoding(parameter) && string.Equals(parameter.Values.FirstOrDefault(), "QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);

    private static bool IsEncoding(ContentLineParameter parameter) => parameter.Name is "ENCODING" || (parameter.Name is null && parameter.Values is [var bare] && bare.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the position of the colon that starts the value: the first one outside a quoted parameter value.</summary>
    private static int FindValueStart(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ':' && !quoted)
            {
                return i;
            }
        }

        return -1;
    }

    private static ContentLine? TryParseLine(string line)
    {
        var colon = FindValueStart(line);
        if (colon <= 0)
        {
            return null;
        }

        var head = line[..colon];
        var parts = SplitParameters(head);
        var name = parts[0].Trim();
        string? group = null;
        var dot = name.LastIndexOf(".", StringComparison.Ordinal);
        if (dot > 0)
        {
            group = name[..dot];
            name = name[(dot + 1)..];
        }

        if (name.Length == 0 || !IsName(name))
        {
            return null;
        }

        var result = new ContentLine(name, line[(colon + 1)..]) { Group = group };
        string? charset = null;
        for (var i = 1; i < parts.Count; i++)
        {
            var parameter = ParseParameter(parts[i]);
            result.Parameters.Add(parameter);
            if (parameter.Name is "CHARSET")
            {
                charset = parameter.Values.FirstOrDefault();
            }
        }

        if (IsQuotedPrintable(result.Parameters))
        {
            // The value becomes a text of vCard 3.0: the bytes in the charset, with ";" and "," kept as they are. All the
            // QUOTED-PRINTABLE encodings are removed: one left would decode the value again when it is read back
            var bytes = MimeEncoding.DecodeQuotedPrintable(Encoding.Latin1.GetBytes(result.Value));
            result.Value = ContentLineWriter.ReplaceLineBreaks((MimeEncoding.GetCharset(charset) ?? Encoding.UTF8).GetString(bytes), "\\n");
            for (var i = result.Parameters.Count - 1; i >= 0; i--)
            {
                if (IsQuotedPrintable(result.Parameters[i]))
                {
                    result.Parameters.RemoveAt(i);
                }
            }

            result.SetParameter("CHARSET", value: null);
        }

        return result;
    }

    private static ContentLineParameter ParseParameter(string part)
    {
        var equal = part.IndexOf('=', StringComparison.Ordinal);
        if (equal < 0 || part[..equal].Trim() is not { Length: > 0 } candidate || !IsName(candidate))
        {
            // vCard 2.1: a parameter without a name (TEL;WORK;VOICE), or an encoding (ENCODING is implied). A part
            // whose name is not a name (a quote, a semicolon...) is kept the same way, as it was read
            return new ContentLineParameter(null, part.Trim());
        }

        return new ContentLineParameter(candidate.ToUpperInvariant(), SplitValues(part[(equal + 1)..]));
    }

    private static bool IsName(string name)
    {
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Splits the name and the parameters on the semicolons outside quoted values.</summary>
    private static List<string> SplitParameters(string head)
    {
        var result = new List<string>();
        var quoted = false;
        var start = 0;
        for (var i = 0; i < head.Length; i++)
        {
            if (head[i] == '"')
            {
                quoted = !quoted;
            }
            else if (head[i] == ';' && !quoted)
            {
                result.Add(head[start..i]);
                start = i + 1;
            }
        }

        result.Add(head[start..]);
        return result;
    }

    /// <summary>Splits the values of a parameter on the commas outside quotes, removes the quotes and decodes the escapes of [RFC 6868] (<c>^n</c>, <c>^'</c>, <c>^^</c>).</summary>
    private static IEnumerable<string> SplitValues(string value)
    {
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                yield return builder.ToString();
                builder.Clear();
            }
            else if (c == '^' && i + 1 < value.Length && value[i + 1] is 'n' or '\'' or '^')
            {
                builder.Append(value[++i] switch { 'n' => '\n', '\'' => '"', _ => '^' });
            }
            else
            {
                builder.Append(c);
            }
        }

        yield return builder.ToString();
    }
}
