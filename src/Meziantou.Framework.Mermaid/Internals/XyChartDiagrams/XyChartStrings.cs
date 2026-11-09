using System.Globalization;
using System.Net;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;

/// <summary>The texts of the XY charts: trimmed as JavaScript does, with the entity codes of Mermaid, and quoted to be read back.</summary>
internal static class XyChartStrings
{
    // A keyword is read as a whole word, except the orientations which also start a word
    private static readonly string[] Keywords = ["title", "xychart", "line", "bar"];
    private static readonly string[] Prefixes = ["vertical", "horizontal", "acctitle", "accdescr"];

    /// <summary>Removes the spaces of JavaScript at both ends, as <c>String.prototype.trim</c>.</summary>
    public static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && XyChartLexer.IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && XyChartLexer.IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    /// <summary>Decodes the entity codes of Mermaid, <c>#name;</c> and <c>#123;</c>; an unknown name is shown as <c>&amp;name;</c>, as the browser does.</summary>
    public static string Decode(string value)
    {
        if (!value.Contains('#', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '#' || !XyChartLexer.IsEntityCode(value, i))
            {
                builder.Append(value[i]);
                continue;
            }

            var end = value.IndexOfOrdinal(';', i);
            var name = value[(i + 1)..end];
            if (name.All(char.IsAsciiDigit))
            {
                AppendCodePoint(builder, name);
            }
            else
            {
                var entity = "&" + name + ";";
                builder.Append(WebUtility.HtmlDecode(entity));
            }

            i = end;
        }

        return builder.ToString();
    }

    private static void AppendCodePoint(StringBuilder builder, string digits)
    {
        if (digits.Length <= 7 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var codePoint) && codePoint <= 0x10FFFF && codePoint is < 0xD800 or > 0xDFFF)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            builder.Append('\ufffd');
        }
    }

    /// <summary>Writes a text in quotes: the characters the reading changes become entity codes.</summary>
    public static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        var trimmed = Trim(value);
        var first = trimmed.Length is 0 ? value.Length : value.IndexOf(trimmed, StringComparison.Ordinal);
        var last = first + trimmed.Length;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            // The spaces at the ends would be trimmed, the line breaks normalized, and %% read as a directive or a comment
            var encode = c is '"' or '#' || char.IsControl(c) || c is '\u2028' or '\u2029' or '\ufeff'
                || (c == '%' && i + 1 < value.Length && value[i + 1] == '%')
                || i < first || i >= last;
            if (encode)
            {
                builder.Append('#').Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append(';');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Writes a text as a word when it reads back as is, in quotes otherwise.</summary>
    /// <param name="value">The text.</param>
    /// <param name="allowDigits">Whether the word can contain digits: after <c>x-axis</c> and <c>y-axis</c>, they are numbers.</param>
    public static string Format(string value, bool allowDigits)
    {
        if (value.Length > 0 && char.IsAsciiLetter(value[0]) && value.All(c => char.IsAsciiLetter(c) || (allowDigits && char.IsAsciiDigit(c))) && !IsKeyword(value))
            return value;

        return Quote(value);
    }

    private static bool IsKeyword(string value)
    {
        foreach (var keyword in Keywords)
        {
            if (value.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var prefix in Prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
