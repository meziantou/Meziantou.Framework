using System.Text;

namespace Meziantou.Framework;

public static class MarkdownBuilder
{
    // https://spec.commonmark.org/0.30/#preliminaries
    public static string Escape(string value)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                // https://spec.commonmark.org/0.30/#backslash-escapes
                // Any ASCII punctuation character may be backslash-escaped:
                // -./:;\<\=\>\?\@\[\\\]\^\_\`\{\|\}\~
                case '!':
                case '"':
                case '#':
                case '$':
                case '%':
                case '&':
                case '\'':
                case '(':
                case ')':
                case '*':
                case '+':
                case ',':
                case '-':
                case '.':
                case '/':
                case ':':
                case ';':
                case '<':
                case '=':
                case '>':
                case '?':
                case '@':
                case '[':
                case '\\':
                case ']':
                case '^':
                case '_':
                case '`':
                case '{':
                case '|':
                case '}':
                case '~':
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

    // https://spec.commonmark.org/0.30/#code-spans
    public static string CreateCodeSpan(string content)
    {
        var needSpace = content.StartsWith('`') || content.EndsWith('`');
        var openCount = CountMaximumConsecutiveCharacters(content, '`') + 1;

        var sb = new StringBuilder();
        sb.Append('`', openCount);

        if (needSpace)
        {
            sb.Append(' ');
        }

        sb.Append(content);

        if (needSpace)
        {
            sb.Append(' ');
        }

        sb.Append('`', openCount);
        return sb.ToString();
    }

    private static int CountMaximumConsecutiveCharacters(string str, char characterToCount)
    {
        var maximumConsecutiveCharacters = 0;
        var consecutiveCharacters = 0;
        foreach (var character in str)
        {
            if (character == characterToCount)
            {
                consecutiveCharacters++;
            }
            else
            {
                maximumConsecutiveCharacters = Math.Max(maximumConsecutiveCharacters, consecutiveCharacters);
                consecutiveCharacters = 0;
            }
        }

        return Math.Max(maximumConsecutiveCharacters, consecutiveCharacters);
    }
}
