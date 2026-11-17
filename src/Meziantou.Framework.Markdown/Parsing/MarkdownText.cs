using System.Text;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>Reads the values of the text of the tokens.</summary>
internal static class MarkdownText
{
    /// <summary>Removes the backslash escapes and decodes the entities.</summary>
    public static string Unescape(string text)
    {
        if (!text.AsSpan().ContainsAny('\\', '&'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && Scanners.IsEscapable(text[i + 1]))
            {
                builder.Append(text[i + 1]);
                i++;
            }
            else if (c == '&' && Scanners.EntityRegex().Match(text, i, text.Length - i) is { Success: true } match)
            {
                builder.Append(HtmlEntities.Decode(match.Value));
                i += match.Length - 1;
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Gets the text of a line without its line ending.</summary>
    public static ReadOnlySpan<char> TrimLineEnding(string text) => text.AsSpan().TrimEnd("\r\n");
}
