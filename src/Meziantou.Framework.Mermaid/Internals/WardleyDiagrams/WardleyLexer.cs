namespace Meziantou.Framework.Mermaid.Internals.WardleyDiagrams;

/// <summary>
/// Reads the tokens of a Wardley map in the order the Langium lexer of Mermaid tries them: the punctuation, the
/// terminals of the grammar of the Wardley maps (numbers, arrows, labels, strategies, keywords, names), then the ones of
/// the common grammar (titles, integers, strings, identifiers). The first that matches wins, so a name that starts
/// with a keyword, such as <c>builder</c>, is the keyword <c>build</c> followed by a name.
/// </summary>
internal static class WardleyLexer
{
    private static readonly (string Text, WardleyTokenKind Kind)[] Keywords =
    [
        ("wardley-beta", WardleyTokenKind.KeywordWardley),
        ("size", WardleyTokenKind.KeywordSize),
        ("evolution", WardleyTokenKind.KeywordEvolution),
        ("anchor", WardleyTokenKind.KeywordAnchor),
        ("component", WardleyTokenKind.KeywordComponent),
        ("label", WardleyTokenKind.KeywordLabel),
        ("inertia", WardleyTokenKind.KeywordInertia),
        ("evolve", WardleyTokenKind.KeywordEvolve),
        ("pipeline", WardleyTokenKind.KeywordPipeline),
        ("note", WardleyTokenKind.KeywordNote),
        ("annotations", WardleyTokenKind.KeywordAnnotations),
        ("annotation", WardleyTokenKind.KeywordAnnotation),
        ("accelerator", WardleyTokenKind.KeywordAccelerator),
        ("deaccelerator", WardleyTokenKind.KeywordDeaccelerator),
    ];

    private static readonly string[] Strategies = ["build", "buy", "outsource", "market"];

    /// <summary>Reads the token at a position, after the spaces, the tabs and a <c>%%</c> comment.</summary>
    public static WardleyToken Next(string text, int position)
    {
        while (true)
        {
            while (position < text.Length && text[position] is ' ' or '\t')
            {
                position++;
            }

            if (position + 1 < text.Length && text[position] == '%' && text[position + 1] == '%')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }

                continue;
            }

            break;
        }

        if (position >= text.Length)
            return new WardleyToken(WardleyTokenKind.End, position, position);

        var c = text[position];
        var punctuation = c switch
        {
            '\n' => WardleyTokenKind.Newline,
            '[' => WardleyTokenKind.LeftBracket,
            ',' => WardleyTokenKind.Comma,
            ']' => WardleyTokenKind.RightBracket,
            '(' => WardleyTokenKind.LeftParenthesis,
            ')' => WardleyTokenKind.RightParenthesis,
            '{' => WardleyTokenKind.LeftBrace,
            '}' => WardleyTokenKind.RightBrace,
            '@' => WardleyTokenKind.At,
            '/' => WardleyTokenKind.Slash,
            _ => WardleyTokenKind.Unknown,
        };
        if (punctuation is not WardleyTokenKind.Unknown)
            return new WardleyToken(punctuation, position, position + 1);

        // The keyword '-' gives way to the longer arrows
        if (c == '-')
        {
            if (StartsWith(text, position, "->"))
                return new WardleyToken(WardleyTokenKind.Arrow, position, position + 2);

            var arrow = MatchLinkArrow(text, position);
            return arrow > 0 ? new WardleyToken(WardleyTokenKind.LinkArrow, position, position + arrow) : new WardleyToken(WardleyTokenKind.Minus, position, position + 1);
        }

        var number = MatchDecimal(text, position);
        if (number > 0)
            return new WardleyToken(WardleyTokenKind.Number, position, position + number);

        foreach (var port in (ReadOnlySpan<string>)["+<>", "+>", "+<"])
        {
            if (StartsWith(text, position, port))
                return new WardleyToken(WardleyTokenKind.LinkPort, position, position + port.Length);
        }

        var linkArrow = MatchLinkArrow(text, position);
        if (linkArrow > 0)
            return new WardleyToken(WardleyTokenKind.LinkArrow, position, position + linkArrow);

        if (c == ';')
        {
            var end = position + 1;
            while (end < text.Length && text[end] is not ('\n' or '\r'))
            {
                end++;
            }

            if (end > position + 1)
                return new WardleyToken(WardleyTokenKind.LinkLabel, position, end);
        }

        foreach (var strategy in Strategies)
        {
            if (StartsWith(text, position, strategy))
                return new WardleyToken(WardleyTokenKind.Strategy, position, position + strategy.Length);
        }

        foreach (var (keyword, kind) in Keywords)
        {
            if (StartsWith(text, position, keyword))
                return new WardleyToken(kind, position, position + keyword.Length);
        }

        var name = MatchName(text, position);
        if (name > 0)
            return new WardleyToken(WardleyTokenKind.Name, position, position + name);

        var accessibility = MatchAccessibility(text, position);
        if (accessibility is not WardleyTokenKind.Unknown)
            return new WardleyToken(accessibility, position, position);

        // A title is read by the parser, up to the end of the line or a comment
        if (StartsWith(text, position, "title"))
            return new WardleyToken(WardleyTokenKind.Title, position, position + 5);

        var integer = MatchInteger(text, position);
        if (integer > 0)
            return new WardleyToken(WardleyTokenKind.Integer, position, position + integer);

        var quoted = MatchString(text, position);
        if (quoted > 0)
            return new WardleyToken(WardleyTokenKind.String, position, position + quoted);

        var identifier = MatchIdentifier(text, position);
        if (identifier > 0)
            return new WardleyToken(WardleyTokenKind.Identifier, position, position + identifier);

        return new WardleyToken(WardleyTokenKind.Unknown, position, position + 1);
    }

    /// <summary>Indicates whether a name can be written without quotes: it reads back as a single name or identifier.</summary>
    public static bool IsBareName(string name)
    {
        if (name.Length is 0)
            return false;

        var token = Next(name + "\n", 0);
        return token is { Kind: WardleyTokenKind.Name or WardleyTokenKind.Identifier, Start: 0 } && token.End == name.Length;
    }

    /// <summary>Returns the value of a string: the text between its quotes, with the backslash escapes of Langium.</summary>
    public static string ReadString(string text, WardleyToken token)
    {
        var builder = new System.Text.StringBuilder(token.Length);
        for (var i = token.Start + 1; i < token.End - 1; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                i++;
                builder.Append(text[i] switch
                {
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    '0' => '\0',
                    var escaped => escaped,
                });
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Indicates whether JavaScript treats a character as white space (<c>\s</c>, <c>trim</c>).</summary>
    public static bool IsJavaScriptWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';

    private static bool StartsWith(string text, int position, string value) => string.CompareOrdinal(text, position, value, 0, value.Length) == 0 && position + value.Length <= text.Length;

    /// <summary>Matches <c>/[0-9]+\.[0-9]+/</c>.</summary>
    private static int MatchDecimal(string text, int position)
    {
        var end = position;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        if (end == position || end + 1 >= text.Length || text[end] != '.' || !char.IsAsciiDigit(text[end + 1]))
            return 0;

        end++;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return end - position;
    }

    /// <summary>Matches <c>'--&gt;' | '-.-&gt;' | '&gt;' | /\+'[^']*'&lt;&gt;/ | /\+'[^']*'&lt;/ | /\+'[^']*'&gt;/</c>.</summary>
    private static int MatchLinkArrow(string text, int position)
    {
        if (StartsWith(text, position, "-->"))
            return 3;

        if (StartsWith(text, position, "-.->"))
            return 4;

        if (StartsWith(text, position, ">"))
            return 1;

        if (!StartsWith(text, position, "+'"))
            return 0;

        var close = text.IndexOfOrdinal('\'', position + 2);
        if (close < 0)
            return 0;

        if (StartsWith(text, close + 1, "<>"))
            return close + 3 - position;

        return StartsWith(text, close + 1, "<") || StartsWith(text, close + 1, ">") ? close + 2 - position : 0;
    }

    /// <summary>
    /// Matches <c>NAME_WITH_SPACES</c>: a letter, then letters, digits, <c>_()&amp;</c> and hyphens not followed by
    /// <c>&gt;</c>, and more words after spaces; it cannot start with <c>title</c> and a space, <c>accTitle</c> or <c>accDescr</c>.
    /// </summary>
    private static int MatchName(string text, int position)
    {
        // Mermaid adds a line feed at the end of the text: "title" at the end is followed by white space
        if (StartsWith(text, position, "title") && (position + 5 >= text.Length || IsJavaScriptWhiteSpace(text[position + 5])))
            return 0;

        if (StartsWith(text, position, "accTitle") || StartsWith(text, position, "accDescr") || !char.IsAsciiLetter(text[position]))
            return 0;

        var end = MatchNameWord(text, position + 1);
        while (true)
        {
            var next = end;
            while (next < text.Length && text[next] is ' ' or '\t')
            {
                next++;
            }

            if (next == end || next >= text.Length || !(char.IsAsciiLetter(text[next]) || text[next] == '('))
                break;

            end = MatchNameWord(text, next + 1);
        }

        return end - position;
    }

    private static int MatchNameWord(string text, int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '(' or ')' or '&' || (c == '-' && (position + 1 >= text.Length || text[position + 1] != '>')))
            {
                position++;
                continue;
            }

            break;
        }

        return position;
    }

    /// <summary>Matches <c>ACC_DESCR</c> (<c>accDescr: text</c> or <c>accDescr { lines }</c>) and <c>ACC_TITLE</c> (<c>accTitle: text</c>).</summary>
    private static WardleyTokenKind MatchAccessibility(string text, int position)
    {
        if (StartsWith(text, position, "accDescr"))
        {
            var next = position + "accDescr".Length;
            var afterSpaces = next;
            while (afterSpaces < text.Length && text[afterSpaces] is ' ' or '\t')
            {
                afterSpaces++;
            }

            if (afterSpaces < text.Length && text[afterSpaces] == ':')
                return WardleyTokenKind.AccessibleDescription;

            while (next < text.Length && IsJavaScriptWhiteSpace(text[next]))
            {
                next++;
            }

            if (next < text.Length && text[next] == '{' && text.IndexOfOrdinal('}', next) > 0)
                return WardleyTokenKind.AccessibleDescription;
        }

        if (StartsWith(text, position, "accTitle"))
        {
            var next = position + "accTitle".Length;
            while (next < text.Length && text[next] is ' ' or '\t')
            {
                next++;
            }

            if (next < text.Length && text[next] == ':')
                return WardleyTokenKind.AccessibleTitle;
        }

        return WardleyTokenKind.Unknown;
    }

    /// <summary>Matches <c>/0|[1-9][0-9]*(?!\.)/</c>.</summary>
    private static int MatchInteger(string text, int position)
    {
        if (text[position] == '0')
            return 1;

        if (text[position] is < '1' or > '9')
            return 0;

        var end = position + 1;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        // The digits cannot be followed by a dot: the match shortens by one digit, which a digit follows
        if (end < text.Length && text[end] == '.')
        {
            end--;
        }

        return end - position;
    }

    /// <summary>Matches <c>/"([^"\\]|\\.)*"|'([^'\\]|\\.)*'/</c>: the escaped character cannot be a line terminator.</summary>
    private static int MatchString(string text, int position)
    {
        var quote = text[position];
        if (quote is not ('"' or '\''))
            return 0;

        var end = position + 1;
        while (end < text.Length)
        {
            var c = text[end];
            if (c == quote)
                return end + 1 - position;

            if (c == '\\')
            {
                if (end + 1 >= text.Length || text[end + 1] is '\n' or '\r' or '\u2028' or '\u2029')
                    return 0;

                end += 2;
                continue;
            }

            end++;
        }

        return 0;
    }

    /// <summary>Matches <c>/[\w]([-\w]*\w)?/</c>: an identifier cannot end with a hyphen.</summary>
    private static int MatchIdentifier(string text, int position)
    {
        if (!IsWordCharacter(text[position]))
            return 0;

        var end = position + 1;
        var last = end;
        while (end < text.Length && (IsWordCharacter(text[end]) || text[end] == '-'))
        {
            end++;
            if (IsWordCharacter(text[end - 1]))
            {
                last = end;
            }
        }

        return last - position;
    }

    private static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
