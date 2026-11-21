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

        var text = line.ToString();
        var colon = FindValueStart(text);
        return colon > 0 && text.AsSpan(0, colon).Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);
    }

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
        string? encoding = null;
        ContentLineParameter? encodingParameter = null;
        string? charset = null;
        for (var i = 1; i < parts.Count; i++)
        {
            var part = parts[i];
            var equal = part.IndexOf('=', StringComparison.Ordinal);
            ContentLineParameter parameter;
            if (equal < 0)
            {
                // vCard 2.1: a parameter without a name (TEL;WORK;VOICE), or an encoding (ENCODING is implied)
                parameter = new ContentLineParameter(null, part.Trim());
            }
            else
            {
                var parameterName = part[..equal].Trim().ToUpperInvariant();
                parameter = new ContentLineParameter(parameterName, SplitValues(part[(equal + 1)..]));
            }

            result.Parameters.Add(parameter);
            if (parameter.Name is "ENCODING" || (parameter.Name is null && parameter.Values is [var bare] && bare.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase)))
            {
                encoding = parameter.Values.FirstOrDefault();
                encodingParameter = parameter;
            }
            else if (parameter.Name is "CHARSET")
            {
                charset = parameter.Values.FirstOrDefault();
            }
        }

        if (string.Equals(encoding, "QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
        {
            // The value becomes a text of vCard 3.0: the bytes in the charset, with ";" and "," kept as they are
            var bytes = MimeEncoding.DecodeQuotedPrintable(Encoding.Latin1.GetBytes(result.Value));
            result.Value = ContentLineWriter.ReplaceLineBreaks((MimeEncoding.GetCharset(charset) ?? Encoding.UTF8).GetString(bytes), "\\n");
            result.Parameters.Remove(encodingParameter!);
            result.SetParameter("CHARSET", value: null);
        }

        return result;
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
