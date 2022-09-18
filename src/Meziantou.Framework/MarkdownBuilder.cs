using System.Text;

namespace Meziantou.Framework;

public static class MarkdownBuilder
{
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
}
