namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>
/// Minimal lexer for Gemfiles and gemspecs. It only distinguishes what the RubyGems scanner needs (identifiers, strings,
/// line breaks and punctuation), and drops comments, <c>=begin</c>/<c>=end</c> blocks and heredoc bodies.
/// Symbols, regular expressions, word arrays and heredocs are reported as <see cref="RubyTokenKind.Other"/>.
/// </summary>
internal static class RubyLexer
{
    public static List<RubyToken> Tokenize(string text)
    {
        var tokens = new List<RubyToken>();
        var pendingHeredocs = new List<(string Terminator, bool AllowIndentation)>();
        var i = 0;
        var isLineStart = true;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (isLineStart)
            {
                isLineStart = false;
                if (IsLineKeyword(text, i, "=begin"))
                {
                    i = SkipEmbeddedDocument(text, i);
                    continue;
                }

                if (IsLineKeyword(text, i, "__END__"))
                    break;
            }

            if (c is '\r' or '\n')
            {
                i += c == '\r' && next == '\n' ? 2 : 1;
                if (tokens.Count == 0 || tokens[^1].Kind is not RubyTokenKind.NewLine)
                {
                    tokens.Add(new RubyToken(RubyTokenKind.NewLine, i - 1, i, i - 1, i, IsVerbatim: true));
                }

                if (pendingHeredocs.Count > 0)
                {
                    i = SkipHeredocBodies(text, i, pendingHeredocs);
                    pendingHeredocs.Clear();
                }

                isLineStart = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Line continuation
            if (c == '\\' && next is '\r' or '\n')
            {
                i += next == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 3 : 2;
                continue;
            }

            if (c == '#')
            {
                var end = text.AsSpan(i).IndexOfAny('\r', '\n');
                i = end < 0 ? text.Length : i + end;
                continue;
            }

            if (c is '"' or '\'' or '`')
            {
                i = ScanString(text, i, i + 1, c, isInterpolated: c is not '\'', RubyTokenKind.String, tokens);
                continue;
            }

            if (c == '%' && TryScanPercentLiteral(text, i, tokens, out var percentEnd))
            {
                i = percentEnd;
                continue;
            }

            if (c == '<' && next == '<' && TryReadHeredoc(text, i, tokens, out var heredocEnd, out var heredoc))
            {
                pendingHeredocs.Add(heredoc);
                tokens.Add(new RubyToken(RubyTokenKind.Other, i, heredocEnd, i, heredocEnd, IsVerbatim: false));
                i = heredocEnd;
                continue;
            }

            if (c == '/' && IsValueExpected(text, tokens))
            {
                // Regular expression
                i = ScanString(text, i, i + 1, '/', isInterpolated: true, RubyTokenKind.Other, tokens);
                continue;
            }

            if (c == ':')
            {
                if (next == ':')
                {
                    tokens.Add(new RubyToken(RubyTokenKind.Other, i, i + 2, i, i + 2, IsVerbatim: true));
                    i += 2;
                    continue;
                }

                // :"symbol" and :'symbol'
                if (next is '"' or '\'')
                {
                    i = ScanString(text, i, i + 2, next, isInterpolated: next == '"', RubyTokenKind.Other, tokens);
                    continue;
                }

                // :symbol
                if (IsIdentifierStart(next))
                {
                    var end = ReadIdentifier(text, i + 1);
                    tokens.Add(new RubyToken(RubyTokenKind.Other, i, end, i, end, IsVerbatim: true));
                    i = end;
                    continue;
                }
            }

            if (IsIdentifierStart(c))
            {
                var end = ReadIdentifier(text, i);

                // A label, such as `require:` in `gem "x", require: false`, is not a method call
                var kind = RubyTokenKind.Identifier;
                if (end < text.Length && text[end] == ':' && (end + 1 >= text.Length || text[end + 1] != ':'))
                {
                    kind = RubyTokenKind.Other;
                    end++;
                }

                tokens.Add(new RubyToken(kind, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            // Instance, class and global variables
            if (c is '@' or '$')
            {
                var end = i + 1;
                if (end < text.Length && text[end] == '@')
                {
                    end++;
                }

                end = end < text.Length && IsIdentifierStart(text[end]) ? ReadIdentifier(text, end) : Math.Min(end + 1, text.Length);
                tokens.Add(new RubyToken(RubyTokenKind.Other, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                var end = i + 1;
                while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '_' || (text[end] == '.' && end + 1 < text.Length && char.IsAsciiDigit(text[end + 1]))))
                {
                    end++;
                }

                tokens.Add(new RubyToken(RubyTokenKind.Other, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            if (c == '&' && next == '.')
            {
                tokens.Add(new RubyToken(RubyTokenKind.Punctuation, i, i + 2, i, i + 2, IsVerbatim: true));
                i += 2;
                continue;
            }

            if (c == '.' && next == '.')
            {
                var end = i + 2;
                if (end < text.Length && text[end] == '.')
                {
                    end++;
                }

                tokens.Add(new RubyToken(RubyTokenKind.Other, i, end, i, end, IsVerbatim: true));
                i = end;
                continue;
            }

            var kindOfCharacter = c is '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';' or '.' or '|' ? RubyTokenKind.Punctuation : RubyTokenKind.Other;
            tokens.Add(new RubyToken(kindOfCharacter, i, i + 1, i, i + 1, IsVerbatim: true));
            i++;
        }

        return tokens;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c is '_';

    private static int ReadIdentifier(string text, int i)
    {
        var end = i;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_'))
        {
            end++;
        }

        // Method names can end with ? or !, but not when it is part of `!=` or of a ternary operator
        if (end < text.Length && text[end] is '?' or '!' && (end + 1 >= text.Length || text[end + 1] is not '=' and not ' '))
        {
            end++;
        }

        return end;
    }

    /// <summary>Whether a token that starts with <c>/</c> or <c>%</c> is a literal rather than a division or a modulo.</summary>
    private static bool IsValueExpected(string text, List<RubyToken> tokens)
    {
        if (tokens.Count == 0)
            return true;

        var previous = tokens[^1];
        return previous.Kind switch
        {
            RubyTokenKind.NewLine => true,
            RubyTokenKind.Punctuation => !previous.Is(text, RubyTokenKind.Punctuation, ")") && !previous.Is(text, RubyTokenKind.Punctuation, "]") && !previous.Is(text, RubyTokenKind.Punctuation, "}"),
            RubyTokenKind.Other => text[previous.End - 1] is '=' or '>' or '<' or '!' or '&' or '|' or '+' or '-' or '*' or '~' or ':' or '?',
            _ => false,
        };
    }

    private static bool TryScanPercentLiteral(string text, int i, List<RubyToken> tokens, out int end)
    {
        end = i;
        if (i + 1 >= text.Length)
            return false;

        // %q(...) and %w[...] are literals wherever they are, %(...) only where a value is expected
        var type = text[i + 1];
        var delimiterIndex = type is 'q' or 'Q' or 'w' or 'W' or 'i' or 'I' ? i + 2 : i + 1;
        if (delimiterIndex == i + 1 && !IsValueExpected(text, tokens))
            return false;

        if (delimiterIndex >= text.Length)
            return false;

        var open = text[delimiterIndex];
        if (char.IsLetterOrDigit(open) || char.IsWhiteSpace(open))
            return false;

        var kind = type is 'w' or 'W' or 'i' or 'I' ? RubyTokenKind.Other : RubyTokenKind.String;
        end = ScanString(text, i, delimiterIndex + 1, open, isInterpolated: type is not 'q' and not 'w' and not 'i', kind, tokens);
        return true;
    }

    /// <summary>Scans a string whose content starts at <paramref name="contentStart"/>, and adds its token.</summary>
    /// <returns>The offset after the string.</returns>
    private static int ScanString(string text, int start, int contentStart, char open, bool isInterpolated, RubyTokenKind kind, List<RubyToken> tokens)
    {
        var close = open switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '<' => '>',
            _ => open,
        };

        var depth = 0;
        var isVerbatim = true;
        var i = contentStart;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\')
            {
                isVerbatim = false;
                i += 2;
                continue;
            }

            if (c == close && close != open && depth > 0)
            {
                depth--;
            }
            else if (c == close)
            {
                tokens.Add(new RubyToken(kind, start, i + 1, contentStart, i, isVerbatim));
                return i + 1;
            }
            else if (c == open && close != open)
            {
                depth++;
            }
            else if (c is '\r' or '\n')
            {
                isVerbatim = false;
            }
            else if (c == '#' && isInterpolated && i + 1 < text.Length && text[i + 1] is '{' or '@' or '$')
            {
                isVerbatim = false;
                if (text[i + 1] == '{')
                {
                    i = SkipInterpolation(text, i + 2);
                    continue;
                }
            }

            i++;
        }

        // Unterminated string: it runs to the end of the file
        tokens.Add(new RubyToken(kind, start, text.Length, contentStart, text.Length, IsVerbatim: false));
        return text.Length;
    }

    /// <summary>Skips the expression of a <c>#{...}</c> interpolation, whose braces and strings may nest.</summary>
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

    /// <summary>Reads the start of a heredoc, such as <c>&lt;&lt;~EOS</c>, <c>&lt;&lt;-EOS</c>, <c>&lt;&lt;EOS</c> or <c>&lt;&lt;~'EOS'</c>.</summary>
    private static bool TryReadHeredoc(string text, int i, List<RubyToken> tokens, out int end, out (string Terminator, bool AllowIndentation) heredoc)
    {
        end = i;
        heredoc = default;

        var index = i + 2;
        var allowIndentation = index < text.Length && text[index] is '~' or '-';
        if (allowIndentation)
        {
            index++;
        }

        if (index >= text.Length)
            return false;

        // `array << Constant` is an append: a heredoc without ~ or - must follow a place where a value is expected
        if (!allowIndentation && tokens.Count > 0 && tokens[^1].Kind is RubyTokenKind.Identifier or RubyTokenKind.String && !(i > 0 && text[i - 1] == ' ' && (index >= text.Length || text[index] != ' ')))
            return false;

        string terminator;
        if (text[index] is '\'' or '"' or '`')
        {
            var quote = text[index];
            var closeIndex = text.IndexOf(quote, index + 1, StringComparison.Ordinal);
            var lineEnd = text.AsSpan(index).IndexOfAny('\r', '\n');
            if (closeIndex < 0 || (lineEnd >= 0 && closeIndex > index + lineEnd))
                return false;

            terminator = text[(index + 1)..closeIndex];
            end = closeIndex + 1;
        }
        else if (IsIdentifierStart(text[index]) && (allowIndentation || char.IsUpper(text[index])))
        {
            end = index;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_'))
            {
                end++;
            }

            terminator = text[index..end];
        }
        else
        {
            return false;
        }

        heredoc = (terminator, allowIndentation);
        return terminator.Length > 0;
    }

    private static int SkipHeredocBodies(string text, int i, List<(string Terminator, bool AllowIndentation)> heredocs)
    {
        foreach (var (terminator, allowIndentation) in heredocs)
        {
            while (i < text.Length)
            {
                var lineEnd = TextLineMap.GetLineEnd(text, i);
                var line = text.AsSpan(i, lineEnd - i);
                i = lineEnd < text.Length && text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : Math.Min(lineEnd + 1, text.Length);
                if ((allowIndentation ? line.Trim() : line.TrimEnd()).SequenceEqual(terminator))
                    break;
            }
        }

        return i;
    }

    private static bool IsLineKeyword(string text, int i, string keyword)
    {
        if (!text.AsSpan(i).StartsWith(keyword, StringComparison.Ordinal))
            return false;

        var end = i + keyword.Length;
        return end >= text.Length || char.IsWhiteSpace(text[end]);
    }

    /// <summary>Skips an embedded document, from a line starting with <c>=begin</c> to the end of the line starting with <c>=end</c>.</summary>
    private static int SkipEmbeddedDocument(string text, int i)
    {
        while (i < text.Length)
        {
            var lineEnd = TextLineMap.GetLineEnd(text, i);
            var isEnd = IsLineKeyword(text, i, "=end");
            i = lineEnd;
            if (isEnd)
                return i;

            i = lineEnd < text.Length && text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : Math.Min(lineEnd + 1, text.Length);
        }

        return text.Length;
    }
}
