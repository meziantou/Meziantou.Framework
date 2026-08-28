using System.Text;

namespace Meziantou.Framework.AtlassianDataFormat;

internal static class MarkdownHelper
{
    /// <summary>Escapes the characters that would otherwise be read as Markdown syntax.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                // Always escape
                case '\\' or '*' or '`' or '[' or ']' or '<' or '>' or '|':
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                // '_' is only emphasis at a word boundary, so snake_case is left alone
                case '_':
                    if ((i == 0 || !char.IsLetterOrDigit(text[i - 1])) || (i + 1 >= text.Length || !char.IsLetterOrDigit(text[i + 1])))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // '~' is only strikethrough in '~~' sequences
                case '~':
                    if ((i + 1 < text.Length && text[i + 1] == '~') || (i > 0 && text[i - 1] == '~'))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // '-' starts a list item or a thematic break at the start of a line
                case '-':
                    if (i == 0 || (i + 1 < text.Length && text[i + 1] == '-') || (i > 0 && text[i - 1] == '-'))
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                // '#' is a heading only at the start of a line
                case '#':
                    if (i == 0)
                        sb.Append('\\');
                    sb.Append(c);
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Prefixes every line of <paramref name="content"/>, used for block quotes and list items.</summary>
    public static string PrefixLines(string content, string firstPrefix, string otherPrefix, string emptyPrefix)
    {
        var sb = new StringBuilder();
        var isFirst = true;
        foreach (var line in content.Split('\n'))
        {
            if (!isFirst)
            {
                sb.Append('\n');
            }

            if (isFirst)
            {
                sb.Append(firstPrefix);
                isFirst = false;
            }
            else if (line.Length == 0)
            {
                sb.Append(emptyPrefix);
            }
            else
            {
                sb.Append(otherPrefix);
            }

            sb.Append(line);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns a fence long enough to wrap <paramref name="content"/>: it must be longer than the
    /// longest run of the fence character inside the content.
    /// </summary>
    public static string CreateFence(string content, char fenceCharacter, int minimumLength)
    {
        var longest = 0;
        var current = 0;
        foreach (var c in content)
        {
            if (c == fenceCharacter)
            {
                current++;
                if (current > longest)
                {
                    longest = current;
                }
            }
            else
            {
                current = 0;
            }
        }

        return new string(fenceCharacter, Math.Max(minimumLength, longest + 1));
    }

    /// <summary>
    /// Wraps <paramref name="text"/> between two delimiters, keeping the leading and trailing
    /// whitespace outside: <c>** bold **</c> is not emphasis, <c> **bold** </c> is.
    /// </summary>
    public static string Delimit(string text, string delimiter) => Delimit(text, delimiter, delimiter);

    public static string Delimit(string text, string opening, string closing)
    {
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = text.Length;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (start == end)
            return text;

        return string.Concat(text[..start], opening, text[start..end], closing, text[end..]);
    }

    /// <summary>Wraps text in a code span, using a backtick fence long enough for the content.</summary>
    public static string CodeSpan(string text)
    {
        if (text.Length == 0)
            return "``";

        var fence = CreateFence(text, '`', minimumLength: 1);

        // A code span that starts or ends with a backtick or a space needs one space of padding.
        var padding = text[0] is '`' or ' ' || text[^1] is '`' or ' ' ? " " : "";
        return fence + padding + text + padding + fence;
    }

    /// <summary>Collapses a block into a single line, for contexts that cannot hold block content.</summary>
    public static string Flatten(string content)
    {
        var lines = content.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (sb.Length > 0)
            {
                sb.Append("<br>");
            }

            sb.Append(trimmed);
        }

        return sb.ToString();
    }

    /// <summary>Joins blocks with a blank line, dropping the empty ones.</summary>
    public static string JoinBlocks(IEnumerable<string> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            if (string.IsNullOrEmpty(block))
                continue;

            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }

            sb.Append(block);
        }

        return sb.ToString();
    }

    /// <summary>Formats a link destination, wrapping it in angle brackets when it needs them.</summary>
    public static string LinkDestination(string url)
    {
        foreach (var c in url)
        {
            if (char.IsWhiteSpace(c) || c is '(' or ')' or '<' or '>')
                return "<" + url.Replace("<", "%3C", StringComparison.Ordinal).Replace(">", "%3E", StringComparison.Ordinal) + ">";
        }

        return url;
    }

    /// <summary>Formats an inline link, including its optional title.</summary>
    public static string Link(string text, string url, string? title)
    {
        var destination = LinkDestination(url);
        if (title is not { Length: > 0 })
            return $"[{text}]({destination})";

        var escapedTitle = title
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"[{text}]({destination} \"{escapedTitle}\")";
    }
}
