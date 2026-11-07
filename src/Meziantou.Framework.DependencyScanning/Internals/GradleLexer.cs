namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>
/// Minimal lexer for Gradle build scripts written in Groovy or Kotlin. It only distinguishes what the Java scanner needs
/// (identifiers, strings, punctuation and operators) and drops comments and whitespace.
/// </summary>
internal static class GradleLexer
{
    public static List<GradleToken> Tokenize(string text, bool isKotlin)
    {
        var tokens = new List<GradleToken>();
        var i = 0;

        // Groovy scripts may start with a shebang line
        if (text.StartsWith("#!", StringComparison.Ordinal))
        {
            i = SkipLineComment(text, i);
        }

        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && next == '/')
            {
                i = SkipLineComment(text, i);
                continue;
            }

            if (c == '/' && next == '*')
            {
                i = SkipBlockComment(text, i, allowNesting: isKotlin);
                continue;
            }

            if (c is '"' or '\'')
            {
                i = ScanString(text, i, isKotlin, tokens);
                continue;
            }

            if (char.IsLetter(c) || c is '_')
            {
                var end = i + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_'))
                {
                    end++;
                }

                tokens.Add(new GradleToken(GradleTokenKind.Identifier, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                var end = i + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' || (text[end] is '.' && end + 1 < text.Length && char.IsAsciiDigit(text[end + 1]))))
                {
                    end++;
                }

                tokens.Add(new GradleToken(GradleTokenKind.Other, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            if (IsOperatorCharacter(text, i))
            {
                var end = i + 1;
                while (end < text.Length && IsOperatorCharacter(text, end))
                {
                    end++;
                }

                tokens.Add(new GradleToken(GradleTokenKind.Operator, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            var kind = c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or ':' or ';' or '.' ? GradleTokenKind.Punctuation : GradleTokenKind.Other;
            tokens.Add(new GradleToken(kind, i, i + 1, i, i + 1, IsVerbatim: true));
            i++;
        }

        return tokens;
    }

    private static int ScanString(string text, int start, bool isKotlin, List<GradleToken> tokens)
    {
        var quote = text[start];
        var isTripleQuoted = text.AsSpan(start).StartsWith(quote == '"' ? "\"\"\"" : "'''", StringComparison.Ordinal);
        var delimiterLength = isTripleQuoted ? 3 : 1;

        // A Kotlin raw string ("""...""") has no escape sequences. Groovy single-quoted strings and Kotlin characters are not interpolated.
        var hasEscapes = !(isKotlin && isTripleQuoted);
        var hasInterpolation = quote == '"';

        var contentStart = start + delimiterLength;
        var isVerbatim = true;
        var i = contentStart;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == quote && (!isTripleQuoted || text.AsSpan(i).StartsWith(quote == '"' ? "\"\"\"" : "'''", StringComparison.Ordinal)))
            {
                // A Kotlin raw string can end with extra quotes, which are part of its content
                var contentEnd = i;
                if (isTripleQuoted)
                {
                    while (i + 3 < text.Length && text[i + 3] == quote)
                    {
                        i++;
                        contentEnd++;
                    }
                }

                var end = i + delimiterLength;
                tokens.Add(new GradleToken(GradleTokenKind.String, start, end, contentStart, contentEnd, isVerbatim));
                return end;
            }

            if (c is '\r' or '\n')
            {
                if (!isTripleQuoted)
                {
                    // Unterminated string: the token is not reported
                    return i;
                }

                isVerbatim = false;
                i++;
                continue;
            }

            if (c == '\\' && hasEscapes)
            {
                isVerbatim = false;
                i += 2;
                continue;
            }

            if (c == '$' && hasInterpolation && i + 1 < text.Length && (text[i + 1] is '{' || char.IsLetter(text[i + 1]) || text[i + 1] is '_'))
            {
                isVerbatim = false;
                if (text[i + 1] is '{')
                {
                    i = SkipInterpolation(text, i + 2);
                    continue;
                }
            }

            i++;
        }

        return text.Length;
    }

    /// <summary>Skips the expression of a <c>${...}</c> interpolation, whose braces and strings may nest.</summary>
    private static int SkipInterpolation(string text, int i)
    {
        var depth = 1;
        while (i < text.Length)
        {
            var c = text[i];
            switch (c)
            {
                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;
                    if (depth == 0)
                        return i + 1;

                    break;

                case '"' or '\'':
                    // A string nested in the expression: skip it, so its braces are not counted
                    var end = text.IndexOf(c, i + 1, StringComparison.Ordinal);
                    if (end < 0)
                        return text.Length;

                    i = end;
                    break;
            }

            i++;
        }

        return text.Length;
    }

    private static int SkipLineComment(string text, int i)
    {
        var end = text.AsSpan(i).IndexOfAny('\r', '\n');
        return end < 0 ? text.Length : i + end;
    }

    private static int SkipBlockComment(string text, int i, bool allowNesting)
    {
        // Kotlin block comments nest, Groovy block comments do not
        var depth = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                depth = allowNesting || depth == 0 ? depth + 1 : depth;
                i += 2;
            }
            else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0)
                    return i;
            }
            else
            {
                i++;
            }
        }

        return text.Length;
    }

    private static bool IsOperatorCharacter(string text, int i)
    {
        var c = text[i];
        if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            return false;

        return c is '/' or '=' or '-' or '+' or '!' or '*' or '%' or '<' or '>' or '&' or '|' or '^' or '~' or '?';
    }
}
