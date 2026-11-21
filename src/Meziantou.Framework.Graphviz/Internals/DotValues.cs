using System.Globalization;
using System.Net;
using System.Text;

namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>Reads the values of the attributes of Graphviz: labels and their escapes, HTML-like and record labels, colors and numbers.</summary>
internal static class DotValues
{
    private const char NoBreakSpace = (char)0xA0;

    // The X11 colors of Graphviz whose value differs from the CSS color of the same name, and the X11 names CSS does not have
    private static readonly Dictionary<string, SceneColor> X11Colors = new(StringComparer.Ordinal)
    {
        ["gray"] = new(190, 190, 190),
        ["grey"] = new(190, 190, 190),
        ["green"] = new(0, 255, 0),
        ["maroon"] = new(176, 48, 96),
        ["purple"] = new(160, 32, 240),
        ["lightgoldenrod"] = new(238, 221, 130),
        ["navyblue"] = new(0, 0, 128),
        ["violetred"] = new(208, 32, 144),
        ["lightslateblue"] = new(132, 112, 255),
        ["none"] = SceneColor.Transparent,
        ["invis"] = SceneColor.Transparent,
    };

    /// <summary>Replaces the escapes of a label (<c>\n</c>, <c>\l</c>, <c>\r</c>, <c>\N</c>, <c>\G</c>, <c>\E</c>, <c>\T</c>, <c>\H</c>, <c>\\</c>), and removes the line break that ends the last line.</summary>
    public static string Unescape(string value, string? nodeName = null, string? graphName = null, string? tailName = null, string? headName = null, string? edgeName = null)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return RemoveFinalLineBreak(value);

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is not '\\' || i + 1 >= value.Length)
            {
                builder.Append(c);
                continue;
            }

            var next = value[++i];
            switch (next)
            {
                case 'n' or 'l' or 'r':
                    builder.Append('\n');
                    break;
                case 'N':
                    builder.Append(nodeName);
                    break;
                case 'G':
                    builder.Append(graphName);
                    break;
                case 'E':
                    builder.Append(edgeName);
                    break;
                case 'T':
                    builder.Append(tailName);
                    break;
                case 'H':
                    builder.Append(headName);
                    break;
                case 'L':
                    break;
                default:
                    builder.Append(next);
                    break;
            }
        }

        return RemoveFinalLineBreak(builder.ToString());
    }

    private static string RemoveFinalLineBreak(string value) => value.EndsWith("\n", StringComparison.Ordinal) ? value[..^1] : value;

    /// <summary>Converts an HTML-like label to text: the line breaks and the rows of the tables start new lines, the cells are separated by spaces, and the other tags are removed.</summary>
    public static string HtmlToText(string html)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        var text = new StringBuilder();
        var i = 0;
        while (i < html.Length)
        {
            var c = html[i];
            if (c is '<')
            {
                var end = FindTagEnd(html, i);
                var tag = html[(i + 1)..end].Trim();
                i = Math.Min(end + 1, html.Length);
                if (tag.StartsWith("!--", StringComparison.Ordinal))
                    continue;

                var name = GetTagName(tag);
                if (name.Equals("br", StringComparison.OrdinalIgnoreCase) || name.Equals("/tr", StringComparison.OrdinalIgnoreCase))
                {
                    FlushText(text, line);
                    lines.Add(line.ToString());
                    line.Clear();
                }
                else if (name.Equals("/td", StringComparison.OrdinalIgnoreCase) || name.Equals("hr", StringComparison.OrdinalIgnoreCase) || name.Equals("vr", StringComparison.OrdinalIgnoreCase))
                {
                    FlushText(text, line);
                    line.Append(' ');
                }

                continue;
            }

            text.Append(c);
            i++;
        }

        FlushText(text, line);
        lines.Add(line.ToString());

        var result = new StringBuilder();
        foreach (var item in lines)
        {
            var trimmed = CollapseSpaces(item);
            if (trimmed.Length is 0)
                continue;

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(trimmed);
        }

        return result.ToString();

        static void FlushText(StringBuilder text, StringBuilder line)
        {
            if (text.Length is 0)
                return;

            line.Append(WebUtility.HtmlDecode(text.ToString()));
            text.Clear();
        }
    }

    private static int FindTagEnd(string html, int start)
    {
        var quote = '\0';
        for (var i = start + 1; i < html.Length; i++)
        {
            var c = html[i];
            if (quote is not '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '>')
            {
                return i;
            }
        }

        return html.Length;
    }

    private static string GetTagName(string tag)
    {
        var end = tag.Length > 0 && tag[0] is '/' ? 1 : 0;
        while (end < tag.Length && !char.IsWhiteSpace(tag[end]) && tag[end] is not '/')
        {
            end++;
        }

        return tag[..end];
    }

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) && c is not NoBreakSpace)
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts the label of a record to lines: each field is a line, the ports (<c>&lt;port&gt;</c>) and the braces that
    /// flip the fields are removed, and <c>\{</c>, <c>\}</c>, <c>\|</c>, <c>\&lt;</c>, <c>\&gt;</c> are the characters.
    /// </summary>
    public static string RecordToText(string label, Func<string, string> unescape)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inPort = false;
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (c is '\\' && i + 1 < label.Length)
            {
                var next = label[i + 1];
                i++;
                if (inPort)
                    continue;

                if (next is '{' or '}' or '|' or '<' or '>' or ' ')
                {
                    field.Append(next);
                }
                else
                {
                    // Kept for the escapes of the labels
                    field.Append('\\').Append(next);
                }

                continue;
            }

            if (inPort)
            {
                if (c is '>')
                {
                    inPort = false;
                }

                continue;
            }

            switch (c)
            {
                case '<':
                    inPort = true;
                    break;
                case '{' or '}' or '|':
                    AddField(fields, field, unescape);
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        AddField(fields, field, unescape);
        return string.Join('\n', fields);

        static void AddField(List<string> fields, StringBuilder field, Func<string, string> unescape)
        {
            var text = unescape(field.ToString()).Trim();
            field.Clear();
            if (text.Length > 0)
            {
                fields.Add(text);
            }
        }
    }

    /// <summary>
    /// Reads a color of Graphviz: <c>#rrggbb</c>, <c>#rrggbbaa</c>, a color name of X11 or CSS, <c>grayN</c>, three numbers
    /// from 0 to 1 for a color in HSV, and the first color of a list (<c>red:blue</c>).
    /// </summary>
    public static bool TryParseColor(string value, out SceneColor color)
    {
        color = default;
        var text = value;
        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator >= 0)
        {
            text = text[..separator];
        }

        separator = text.IndexOf(';', StringComparison.Ordinal);
        if (separator >= 0)
        {
            text = text[..separator];
        }

        text = text.Trim();
        if (text.Length is 0)
            return false;

        if (text[0] is '#')
            return SceneColor.TryParse(text, out color);

        if (text[0] is '/')
        {
            // "/x11/red" or "//red": the default scheme; "/red" has no scheme
            var slash = text.LastIndexOf("/", StringComparison.Ordinal);
            var scheme = slash > 0 ? text[1..slash].Trim('/') : string.Empty;
            if (scheme.Length > 0 && !scheme.Equals("x11", StringComparison.OrdinalIgnoreCase))
                return false;

            text = text[(slash + 1)..];
        }

        if (TryParseHsv(text, out color))
            return true;

        var name = text.ToLowerInvariant();
        if (X11Colors.TryGetValue(name, out color))
            return true;

        if ((name.StartsWith("gray", StringComparison.Ordinal) || name.StartsWith("grey", StringComparison.Ordinal)) && name.Length > 4
            && int.TryParse(name.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level <= 100)
        {
            var channel = (byte)Math.Round(level * 255 / 100.0, MidpointRounding.AwayFromZero);
            color = new SceneColor(channel, channel, channel);
            return true;
        }

        // The CSS functions (rgb(), hsl()) are not colors of Graphviz
        if (name.Contains('(', StringComparison.Ordinal))
            return false;

        return SceneColor.TryParse(name, out color);
    }

    private static bool TryParseHsv(string text, out SceneColor color)
    {
        color = default;
        var parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not 3 and not 4)
            return false;

        Span<double> values = stackalloc double[4];
        values[3] = 1;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryParseNumber(parts[i], out var number))
                return false;

            values[i] = Math.Clamp(number, 0, 1);
        }

        var hue = values[0] * 6;
        var saturation = values[1];
        var brightness = values[2];
        var sector = (int)Math.Floor(hue) % 6;
        var fraction = hue - Math.Floor(hue);
        var p = brightness * (1 - saturation);
        var q = brightness * (1 - (saturation * fraction));
        var t = brightness * (1 - (saturation * (1 - fraction)));
        var (r, g, b) = sector switch
        {
            0 => (brightness, t, p),
            1 => (q, brightness, p),
            2 => (p, brightness, t),
            3 => (p, q, brightness),
            4 => (t, p, brightness),
            _ => (brightness, p, q),
        };

        color = new SceneColor(ToByte(r), ToByte(g), ToByte(b), ToByte(values[3]));
        return true;

        static byte ToByte(double value) => (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
    }

    /// <summary>Reads a finite number, as the attributes of Graphviz write it.</summary>
    public static bool TryParseNumber(string value, out double number)
    {
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number))
            return true;

        number = 0;
        return false;
    }

    /// <summary>Reads a boolean of Graphviz: <c>true</c>, <c>yes</c> or a non-zero integer.</summary>
    public static bool ParseBoolean(string? value)
    {
        if (value is null)
            return false;

        value = value.Trim();
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;

        return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number != 0;
    }

    public static string FormatNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
