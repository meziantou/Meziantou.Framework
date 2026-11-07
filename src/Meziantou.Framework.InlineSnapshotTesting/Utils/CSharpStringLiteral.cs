using Meziantou.Framework.HumanReadable.Utils;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
internal static class CSharpStringLiteral
{
    public static string Create(string? value, CSharpStringFormats formats, string indentationString, int startPosition, string eol, SnapshotComparer? comparer = null)
    {
        if (value is null)
            return "null";

        comparer ??= SnapshotComparer.Default;
        var isMultiline = IsMultiline(value);
        var hasCharactersRequiringEscaping = HasCharactersRequiringEscaping(value);
        var allowsQuoted = formats.HasFlag(CSharpStringFormats.Quoted);

        if (allowsQuoted && !isMultiline && !HasQuotedStringEscapableCharacters(value))
            return CreateQuotedString(value);

        if (formats.HasFlag(CSharpStringFormats.Verbatim) && !isMultiline && !HasVerbatimStringEscapableCharacters(value) && !hasCharactersRequiringEscaping)
            return CreateVerbatimString(value);

        // Only a quoted string can escape invisible characters. Verbatim and raw strings would write them as-is, and a
        // raw string would also turn the newline characters other than CR and LF into the end-of-line of the file.
        if (hasCharactersRequiringEscaping && allowsQuoted)
            return CreateQuotedString(value);

        // No encoding can store an unpaired surrogate: writing it literally would save U+FFFD instead of the value
        if (HasUnpairedSurrogates(value))
            throw new InlineSnapshotException($"The snapshot contains an unpaired UTF-16 surrogate, which only a quoted string can represent (as a \\uXXXX escape sequence). Add '{nameof(CSharpStringFormats.Quoted)}' to the allowed string formats.");

        // A raw string normalizes the line endings to the end-of-line of the file and drops the content of
        // whitespace-only lines. Use it only when the comparer cannot tell the difference.
        if (formats.HasFlag(CSharpStringFormats.LeftAlignedRaw))
        {
            var result = CreateRawString(value, "", startPosition: -1, eol);
            if (IsLossless(result, value, eol, comparer))
                return result;
        }

        if (formats.HasFlag(CSharpStringFormats.Raw))
        {
            var result = CreateRawString(value, indentationString, startPosition, eol);
            if (IsLossless(result, value, eol, comparer))
                return result;
        }

        if (formats.HasFlag(CSharpStringFormats.Verbatim))
        {
            var result = CreateVerbatimString(value);
            if (IsLossless(result, value, eol, comparer))
                return result;
        }

        if (allowsQuoted)
            return CreateQuotedString(value);

        if (formats.HasFlag(CSharpStringFormats.LeftAlignedRaw) || formats.HasFlag(CSharpStringFormats.Raw) || formats.HasFlag(CSharpStringFormats.Verbatim))
            throw new InlineSnapshotException($"The allowed string formats cannot represent the snapshot in a way the snapshot comparer considers equal to the actual value, for instance because of its line endings or whitespace-only lines. Add '{nameof(CSharpStringFormats.Quoted)}' to the allowed string formats.");

        throw new InlineSnapshotException("Allowed C# string format doesn't allow any value. Update the configuration to allow at least one format.");
    }

    /// <summary>
    /// Reports whether compiling the literal gives back a value the comparer considers equal to the original one.
    /// Git and editors may convert the line endings of the file, so the line breaks of the literal are first
    /// converted to the end-of-line of the file: a verbatim string must not depend on its line endings.
    /// </summary>
    private static bool IsLossless(string literal, string value, string eol, SnapshotComparer comparer)
    {
        if (SyntaxFactory.ParseExpression(ReplaceLineEndings(literal, eol)) is not LiteralExpressionSyntax expression || expression.ContainsDiagnostics)
            return false;

        var compiledValue = expression.Token.ValueText;
        if (compiledValue == value)
            return true;

        return comparer.AreEqual(comparer.NormalizeValue(value), comparer.NormalizeValue(compiledValue));
    }

    private static string ReplaceLineEndings(string value, string eol)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is '\r' or '\n')
            {
                if (c is '\r' && i + 1 < value.Length && value[i + 1] is '\n')
                {
                    i++;
                }

                sb.Append(eol);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool HasUnpairedSurrogates(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsSurrogate(value[i]) && RequiresEscaping(value, i))
                return true;
        }

        return false;
    }

    private static string CreateQuotedString(string value)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
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

                // NEL, LS and PS are C# newline characters (ECMA-334, 6.3.2): a regular string literal
                // cannot contain them literally, so they must be escaped to keep the source valid.
                case '\u0085':
                    sb.Append(@"\u0085");
                    break;

                case '\u2028':
                    sb.Append(@"\u2028");
                    break;

                case '\u2029':
                    sb.Append(@"\u2029");
                    break;

                default:
                    if (RequiresEscaping(value, i))
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }

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

        return HasCharactersRequiringEscaping(value);
    }

    /// <summary>
    /// Reports whether the value contains characters that cannot be written literally in a source file without being
    /// invisible or altered: control characters other than tab, CR and LF, format characters such as zero-width
    /// characters or the byte order mark, the Unicode newline characters, and unpaired surrogates that no encoding can store.
    /// </summary>
    private static bool HasCharactersRequiringEscaping(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (RequiresEscaping(value, i))
                return true;
        }

        return false;
    }

    private static bool RequiresEscaping(string value, int index)
    {
        var c = value[index];
        if (c is '\t' or '\r' or '\n')
            return false;

        if (char.IsHighSurrogate(c))
            return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);

        if (char.IsLowSurrogate(c))
            return index == 0 || !char.IsHighSurrogate(value[index - 1]);

        return char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
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
