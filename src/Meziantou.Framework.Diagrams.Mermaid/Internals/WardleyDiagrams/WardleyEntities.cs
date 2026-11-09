using System.Globalization;
using System.Net;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.WardleyDiagrams;

/// <summary>
/// The entity codes of the texts of the Wardley maps: Mermaid replaces <c>#name;</c> and <c>#123;</c> by the character
/// when it draws the text, so the writer writes the characters it cannot write as is as entity codes.
/// </summary>
internal static class WardleyEntities
{
    /// <summary>Replaces the entity codes <c>#name;</c> and <c>#123;</c> by their characters; an unknown code stays as it is.</summary>
    public static string Decode(string text)
    {
        if (!text.Contains('#', StringComparison.Ordinal))
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '#')
            {
                var end = i + 1;
                while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_'))
                {
                    end++;
                }

                if (end > i + 1 && end < text.Length && text[end] == ';')
                {
                    var name = text[(i + 1)..end];
                    var entity = name.All(char.IsAsciiDigit) ? "&#" + name + ";" : "&" + name + ";";
                    var decoded = WebUtility.HtmlDecode(entity);
                    if (!string.Equals(decoded, entity, StringComparison.Ordinal))
                    {
                        builder.Append(decoded);
                        i = end;
                        continue;
                    }
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Encodes a text for a quoted string: <c>#</c> and <c>%%</c>, which would start an entity code or a directive, become entity codes.</summary>
    public static string Encode(string text)
    {
        if (text.AsSpan().IndexOfAny('#', '%') < 0)
            return text;

        var builder = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '#' || (c == '%' && i + 1 < text.Length && text[i + 1] == '%'))
            {
                AppendCode(builder, c);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Encodes the label of a link, written after a semicolon to the end of the line and trimmed: the line breaks and the white space at its ends become entity codes too.</summary>
    public static string EncodeLabel(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && WardleyLexer.IsJavaScriptWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && WardleyLexer.IsJavaScriptWhiteSpace(text[end - 1]))
        {
            end--;
        }

        var builder = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i < start || i >= end || c is '#' or '\n' or '\r' || (c == '%' && i + 1 < text.Length && text[i + 1] == '%'))
            {
                AppendCode(builder, c);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Removes the white space and the line terminators that <c>String.prototype.trim</c> removes.</summary>
    public static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && WardleyLexer.IsJavaScriptWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && WardleyLexer.IsJavaScriptWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    private static void AppendCode(StringBuilder builder, char c) => builder.Append('#').Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append(';');
}
