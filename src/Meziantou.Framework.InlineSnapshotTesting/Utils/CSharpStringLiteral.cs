using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
internal static class CSharpStringLiteral
{
    public static string Create(string? value, CSharpStringFormats formats, string indentationString, int startPosition, string eol)
    {
        if (value is null)
            return "null";

        var isMultiline = IsMultiline(value);

        if (formats.HasFlag(CSharpStringFormats.Quoted) && !isMultiline && !HasQuotedStringEscapableCharacters(value))
            return CreateQuotedString(value);

        if (formats.HasFlag(CSharpStringFormats.Verbatim) && !isMultiline && !HasVerbatimStringEscapableCharacters(value))
            return CreateVerbatimString(value);

        if (formats.HasFlag(CSharpStringFormats.LeftAlignedRaw))
            return CreateRawString(value, "", startPosition: -1, eol);

        if (formats.HasFlag(CSharpStringFormats.Raw))
            return CreateRawString(value, indentationString, startPosition, eol);

        if (formats.HasFlag(CSharpStringFormats.Verbatim))
            return CreateVerbatimString(value);

        if (formats.HasFlag(CSharpStringFormats.Quoted))
            return CreateQuotedString(value);

        throw new InlineSnapshotException("Allowed C# string format doesn't allow any value. Update the configuration to allow at least one format.");
    }

    private static string CreateQuotedString(string value)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;

                case '\t':
                    sb.Append(@"\t");
                    break;

                case '\r':
                    sb.Append(@"\r");
                    break;

                case '\n':
                    sb.Append(@"\n");
                    break;

                case '\a':
                    sb.Append(@"\a");
                    break;

                case '\b':
                    sb.Append(@"\b");
                    break;

                case '\f':
                    sb.Append(@"\f");
                    break;

                case '\v':
                    sb.Append(@"\v");
                    break;

                case '\0':
                    sb.Append(@"\0");
                    break;

                case '\\':
                    sb.Append(@"\\");
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string CreateVerbatimString(string value)
    {
        var sb = new StringBuilder();
        sb.Append("@\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\"\"");
                    break;

                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string CreateRawString(string value, string indentationString, int startPosition, string eol)
    {
        var maxQuotes = CountMaximumConsecutiveCharacters(value, '"');
        var openingQuotes = new string('"', Math.Max(3, maxQuotes + 1));

        var sb = new StringBuilder();
        sb.Append(openingQuotes).Append(eol);

        foreach (var line in StringUtils.EnumerateLines(value))
        {
            var trimmedLine = TrimEndWhitespace(line.Line);
            if (!trimmedLine.IsEmpty)
            {
                AppendIndentation(sb);
                sb.Append(line);
            }

            sb.Append(eol);
        }

        AppendIndentation(sb);
        sb.Append(openingQuotes);
        return sb.ToString();

        void AppendIndentation(StringBuilder sb)
        {
            if (startPosition == -1 || indentationString.Length == 0)
                return;

            for (var i = 0; i < startPosition + 1; i += indentationString.Length)
            {
                sb.Append(indentationString);
            }
        }
    }

    /// <summary>
    /// The Unicode Standard, Sec. 5.8, Recommendation R4 and Table 5-2 state that the CR, LF,
    /// CRLF, NEL, LS, FF, and PS sequences are considered newline functions. That section
    /// also specifically excludes VT from the list of newline functions, so we do not include
    /// it in the needle list.
    /// </summary>
    private static bool IsMultiline(string value)
    {
        foreach (var c in value)
        {
            if (c is '\n' or '\r' or '\f' or '\u0085' or '\u2028' or '\u2029')
                return true;
        }

        return false;
    }

    // https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/?WT.mc_id=DT-MVP-5003978#quoted-string-literals
    private static bool HasQuotedStringEscapableCharacters(string value)
    {
        foreach (var c in value)
        {
            if (c is '"' or '\\' or '\0' or '\a' or '\b' or '\f' or '\n' or '\r' or '\t' or '\v')
                return true;
        }

        return false;
    }

    private static bool HasVerbatimStringEscapableCharacters(string value)
    {
        return value.Contains('"', StringComparison.Ordinal);
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

    private static ReadOnlySpan<char> TrimEndWhitespace(ReadOnlySpan<char> span)
    {
        while (!span.IsEmpty && char.IsWhiteSpace(span[^1]))
        {
            span = span[..^1];
        }

        return span;
    }
}
