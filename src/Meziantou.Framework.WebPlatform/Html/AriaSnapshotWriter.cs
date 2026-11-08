using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// Writes an accessibility tree in the YAML format of the aria snapshots of Playwright (renderAriaSnapshotAsYaml of
/// packages/isomorphic/ariaSnapshotRenderer.ts, with the quoting rules of packages/isomorphic/yaml.ts).
/// </summary>
internal static class AriaSnapshotWriter
{
    private const int MaxNameLength = 900;

    public static string Write(AccessibilityNode root)
    {
        var builder = new StringBuilder();
        if (root.Properties.Role is "" or "none" or "generic")
        {
            foreach (var child in root.Children)
            {
                WriteNode(builder, child, 0);
            }
        }
        else
        {
            WriteNode(builder, root, 0);
        }

        // The lines are separated by a line feed, without a final one
        if (builder.Length > 0)
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>The role, the name and the states of a node (e.g. <c>checkbox "Accept" [checked]</c>).</summary>
    public static string GetKey(AccessibilityNode node)
    {
        var properties = node.Properties;
        if (properties.Role == "text")
            return "text: " + properties.Name;

        var builder = new StringBuilder();

        // Playwright uses the role names of WAI-ARIA, where Element.ComputedRole uses the names of WebDriver
        builder.Append(properties.Role == "image" ? "img" : properties.Role);
        if (properties.Name.Length is > 0 and <= MaxNameLength)
        {
            builder.Append(' ');
            AppendJsonString(builder, properties.Name);
        }

        switch (properties.Checked)
        {
            case AccessibilityCheckedState.Mixed:
                builder.Append(" [checked=mixed]");
                break;
            case AccessibilityCheckedState.True:
                builder.Append(" [checked]");
                break;
        }

        if (properties.Disabled)
        {
            builder.Append(" [disabled]");
        }

        if (properties.Expanded is true)
        {
            builder.Append(" [expanded]");
        }

        if (properties.Level is { } level)
        {
            builder.Append(" [level=").Append(level.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        switch (properties.Pressed)
        {
            case AccessibilityCheckedState.Mixed:
                builder.Append(" [pressed=mixed]");
                break;
            case AccessibilityCheckedState.True:
                builder.Append(" [pressed]");
                break;
        }

        if (properties.Selected is true)
        {
            builder.Append(" [selected]");
        }

        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, AccessibilityNode node, int depth)
    {
        if (node.Properties.Role == "text")
        {
            WriteText(builder, node.Properties.Name, depth);
            return;
        }

        AppendIndent(builder, depth);
        builder.Append("- ").Append(EscapeKeyIfNeeded(GetKey(node)));

        var url = node.Properties.Role == "link" && node.Node is Element { } link && link.GetAttributeValue("href") is { } href ? TruncateDataUrl(href) : null;
        var placeholder = node.Properties.Role == "textbox" && node.Node is Element { } textbox && textbox.GetAttributeValue("placeholder") is { } value && value != node.Properties.Name ? value : null;
        var hasProps = url is not null || placeholder is not null;
        var singleText = node.Children is [{ Properties.Role: "text" } textNode] ? textNode.Properties.Name : null;

        if (!hasProps && node.Children.Count == 0)
        {
            builder.Append('\n');
            return;
        }

        if (!hasProps && singleText is not null)
        {
            builder.Append(": ").Append(EscapeValueIfNeeded(singleText)).Append('\n');
            return;
        }

        builder.Append(":\n");
        if (url is not null)
        {
            WriteProp(builder, "url", url, depth + 1);
        }

        if (placeholder is not null)
        {
            WriteProp(builder, "placeholder", placeholder, depth + 1);
        }

        foreach (var child in node.Children)
        {
            WriteNode(builder, child, depth + 1);
        }
    }

    private static void WriteText(StringBuilder builder, string text, int depth)
    {
        AppendIndent(builder, depth);
        builder.Append("- text: ").Append(EscapeValueIfNeeded(text)).Append('\n');
    }

    private static void WriteProp(StringBuilder builder, string name, string value, int depth)
    {
        AppendIndent(builder, depth);
        builder.Append("- /").Append(name).Append(": ").Append(EscapeValueIfNeeded(value)).Append('\n');
    }

    private static void AppendIndent(StringBuilder builder, int depth) => builder.Append(' ', depth * 2);

    private static string TruncateDataUrl(string url)
    {
        if (!url.StartsWith("data:", StringComparison.Ordinal))
            return url;

        var comma = url.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? url : string.Concat(url.AsSpan(0, comma + 1), "\u2026");
    }

    /// <summary>The normalization of the texts of Playwright: the zero width spaces and soft hyphens are removed, the white space is collapsed and trimmed.</summary>
    public static string NormalizeWhiteSpace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (c is '\u200B' or '\u00AD')
                continue;

            if (IsJavaScriptWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>JSON.stringify of a string: the quotes, the backslashes and the control characters are escaped, the other characters are written as is.</summary>
    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
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
                    // A lone surrogate is escaped (well-formed JSON.stringify)
                    var isLoneSurrogate = (char.IsHighSurrogate(c) && (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])))
                        || (char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(value[i - 1])));
                    if (c < ' ' || isLoneSurrogate)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static string EscapeKeyIfNeeded(string value)
    {
        if (!NeedsQuotes(value))
            return value;

        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string EscapeValueIfNeeded(string value)
    {
        if (!NeedsQuotes(value))
            return value;

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
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
                case <= '\x1F' or (>= '\x7F' and <= '\x9F'):
                    builder.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>yamlStringNeedsQuotes of Playwright.</summary>
    private static bool NeedsQuotes(string value)
    {
        if (value.Length == 0)
            return true;

        if (IsJavaScriptWhiteSpace(value[0]) || IsJavaScriptWhiteSpace(value[^1]))
            return true;

        if (value[0] is '-' or '&' or '*' or ']' or ',' or '?' or '!' or '>' or '|' or '@' or '"' or '\'' or '#' or '%' or '[')
            return true;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case <= '\x08' or '\x0B' or '\x0C' or (>= '\x0E' and <= '\x1F') or (>= '\x7F' and <= '\x9F'):
                case '\n' or '\r':
                case '{' or '}' or '`':
                    return true;
                case ':' when i + 1 == value.Length || IsJavaScriptWhiteSpace(value[i + 1]):
                    return true;
                case '#' when i > 0 && IsJavaScriptWhiteSpace(value[i - 1]):
                    return true;
            }
        }

        if (IsJavaScriptNumber(value))
            return true;

        return InfraAscii.ToAsciiLowercase(value) is "y" or "n" or "yes" or "no" or "true" or "false" or "on" or "off" or "null";
    }

    /// <summary>Whether <c>Number(value)</c> is not NaN, for a value without leading and trailing white space and that does not start with '-'.</summary>
    private static bool IsJavaScriptNumber(string value)
    {
        var span = value.AsSpan();
        if (span.Length > 2 && span[0] == '0' && span[1] is 'x' or 'X' or 'b' or 'B' or 'o' or 'O')
        {
            foreach (var c in span[2..])
            {
                var isDigit = span[1] switch
                {
                    'x' or 'X' => char.IsAsciiHexDigit(c),
                    'b' or 'B' => c is '0' or '1',
                    _ => c is >= '0' and <= '7',
                };

                if (!isDigit)
                    return false;
            }

            return true;
        }

        if (span[0] == '+')
        {
            span = span[1..];
        }

        if (span is "Infinity")
            return true;

        var index = 0;
        var digits = 0;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
        {
            index++;
            digits++;
        }

        if (index < span.Length && span[index] == '.')
        {
            index++;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                index++;
                digits++;
            }
        }

        if (digits == 0)
            return false;

        if (index < span.Length && span[index] is 'e' or 'E')
        {
            index++;
            if (index < span.Length && span[index] is '+' or '-')
            {
                index++;
            }

            var exponentDigits = 0;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                index++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
                return false;
        }

        return index == span.Length;
    }

    /// <summary>The white space of the \s class of the JavaScript regular expressions.</summary>
    private static bool IsJavaScriptWhiteSpace(char c)
        => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';
}
