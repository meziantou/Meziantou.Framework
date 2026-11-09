using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.QuadrantChartDiagrams;

/// <summary>
/// Splits the text of a quadrant chart into tokens with the rules of the jison lexer of Mermaid, in their order: the first
/// rule that matches at a position wins, the keywords ignore the case, and <c>title</c>, <c>x-axis</c> or <c>%%</c> start
/// their statement or their comment wherever a token starts.
/// </summary>
internal static class QuadrantChartLexer
{
    public static List<QuadrantChartToken> Tokenize(string text, int start, int end)
    {
        var tokens = new List<QuadrantChartToken>();
        var position = start;
        while (true)
        {
            if (position >= end)
            {
                tokens.Add(new QuadrantChartToken(QuadrantChartTokenKind.End, end, end, string.Empty));
                return tokens;
            }

            var token = Next(text, position, end);
            if (token.Kind is not QuadrantChartTokenKind.Skip)
            {
                tokens.Add(token);
            }

            if (token.Kind is QuadrantChartTokenKind.Error)
                return tokens;

            position = token.End;
        }
    }

    private static QuadrantChartToken Next(string text, int position, int end)
    {
        var c = text[position];

        // %% comments: the rule [^\}]%% also takes the character before the comment
        if (c == '%' && At(text, position + 1, end, '%'))
            return Token(QuadrantChartTokenKind.Skip, position, LineEnd(text, position + 2, end), string.Empty);

        if (c != '}' && At(text, position + 1, end, '%') && At(text, position + 2, end, '%'))
            return Token(QuadrantChartTokenKind.Skip, position, LineEnd(text, position + 3, end), string.Empty);

        if (c is '\n' or '\r')
        {
            var newLineEnd = position;
            while (newLineEnd < end && text[newLineEnd] is '\n' or '\r')
            {
                newLineEnd++;
            }

            return Token(QuadrantChartTokenKind.NewLine, position, newLineEnd, text[position..newLineEnd]);
        }

        if (StartsWithIgnoreCase(text, position, end, "title"))
        {
            var valueEnd = LineEnd(text, position + 5, end);
            return Token(QuadrantChartTokenKind.Title, position, valueEnd, text[(position + 5)..valueEnd]);
        }

        if (TryAccessibility(text, position, end, out var accessibility))
            return accessibility;

        var spaces = position;
        while (spaces < end && text[spaces] == ' ')
        {
            spaces++;
        }

        if (TryKeywordWithSpaces(text, position, spaces, end, "x-axis", QuadrantChartTokenKind.XAxis, out var keyword)
            || TryKeywordWithSpaces(text, position, spaces, end, "y-axis", QuadrantChartTokenKind.YAxis, out keyword)
            || TryKeywordWithSpaces(text, position, spaces, end, "quadrant-1", QuadrantChartTokenKind.Quadrant1, out keyword)
            || TryKeywordWithSpaces(text, position, spaces, end, "quadrant-2", QuadrantChartTokenKind.Quadrant2, out keyword)
            || TryKeywordWithSpaces(text, position, spaces, end, "quadrant-3", QuadrantChartTokenKind.Quadrant3, out keyword)
            || TryKeywordWithSpaces(text, position, spaces, end, "quadrant-4", QuadrantChartTokenKind.Quadrant4, out keyword))
        {
            return keyword;
        }

        // The delimiter of the texts of an axis: spaces, two dashes or more, '>', spaces
        if (At(text, spaces, end, '-') && At(text, spaces + 1, end, '-'))
        {
            var dashes = spaces + 2;
            while (dashes < end && text[dashes] == '-')
            {
                dashes++;
            }

            if (At(text, dashes, end, '>'))
                return Token(QuadrantChartTokenKind.Delimiter, position, SkipSpaces(text, dashes + 1, end), text[position..SkipSpaces(text, dashes + 1, end)]);
        }

        if (StartsWithIgnoreCase(text, position, end, "classDef"))
            return Token(QuadrantChartTokenKind.ClassDef, position, position + 8, text.Substring(position, 8));

        if (c == '"')
            return ReadString(text, position, end);

        if (c == ':' && At(text, position + 1, end, ':') && At(text, position + 2, end, ':'))
        {
            var nameEnd = position + 3;
            while (nameEnd < end && (char.IsAsciiLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
            {
                nameEnd++;
            }

            return nameEnd == position + 3
                ? Error(position + 3, "A class name is expected after ':::'.")
                : Token(QuadrantChartTokenKind.ClassName, position, nameEnd, text[(position + 3)..nameEnd]);
        }

        if (TryPoint(text, position, end, out var point))
            return point;

        if (TryKeywordWithSpaces(text, position, spaces, end, "quadrantChart", QuadrantChartTokenKind.Keyword, out keyword))
            return keyword;

        if (char.IsAsciiLetter(c))
            return Run(text, position, end, QuadrantChartTokenKind.Alpha, char.IsAsciiLetter);

        if (c > '\x7F')
            return Run(text, position, end, QuadrantChartTokenKind.Unicode, static c => c > '\x7F');

        if (char.IsAsciiDigit(c))
            return Run(text, position, end, QuadrantChartTokenKind.Num, char.IsAsciiDigit);

        var kind = c switch
        {
            ':' => QuadrantChartTokenKind.Colon,
            '+' => QuadrantChartTokenKind.Plus,
            ',' => QuadrantChartTokenKind.Comma,
            '=' => QuadrantChartTokenKind.Equals,
            '*' => QuadrantChartTokenKind.Mult,
            '#' => QuadrantChartTokenKind.Hash,
            '_' => QuadrantChartTokenKind.Underscore,
            '.' => QuadrantChartTokenKind.Dot,
            '&' => QuadrantChartTokenKind.Amp,
            '-' => QuadrantChartTokenKind.Minus,
            ' ' or '\t' or '\v' or '\f' => QuadrantChartTokenKind.Space,
            ';' => QuadrantChartTokenKind.Semicolon,
            '!' or '$' or '%' or '\'' or '`' or '?' or '\\' or '/' => QuadrantChartTokenKind.Punctuation,
            _ => QuadrantChartTokenKind.Error,
        };

        return kind is QuadrantChartTokenKind.Error
            ? Error(position, $"Unexpected character '{Describe(c)}'.")
            : Token(kind, position, position + 1, c.ToString());
    }

    private static bool TryAccessibility(string text, int position, int end, out QuadrantChartToken token)
    {
        token = default;
        if (StartsWithIgnoreCase(text, position, end, "accTitle"))
        {
            var i = SkipWhiteSpace(text, position + 8, end);
            if (At(text, i, end, ':'))
            {
                var valueStart = SkipWhiteSpace(text, i + 1, end);
                var valueEnd = LineEnd(text, valueStart, end);
                token = Token(QuadrantChartTokenKind.AccTitle, position, valueEnd, text[valueStart..valueEnd]);
                return true;
            }
        }

        if (StartsWithIgnoreCase(text, position, end, "accDescr"))
        {
            var i = SkipWhiteSpace(text, position + 8, end);
            if (At(text, i, end, ':'))
            {
                var valueStart = SkipWhiteSpace(text, i + 1, end);
                var valueEnd = LineEnd(text, valueStart, end);
                token = Token(QuadrantChartTokenKind.AccDescr, position, valueEnd, text[valueStart..valueEnd]);
                return true;
            }

            if (At(text, i, end, '{'))
            {
                var valueStart = SkipWhiteSpace(text, i + 1, end);
                var close = text.IndexOf('}', valueStart, StringComparison.Ordinal);
                if (close < 0 || close >= end)
                {
                    token = Error(position, "The description is not closed by '}'.");
                    return true;
                }

                token = Token(QuadrantChartTokenKind.AccDescrMultiline, position, close + 1, text[valueStart..close]);
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads <c>"text"</c>, or <c>"`markdown`"</c>; an empty string makes no token, as in Mermaid.</summary>
    private static QuadrantChartToken ReadString(string text, int position, int end)
    {
        if (At(text, position + 1, end, '`'))
        {
            var contentStart = position + 2;
            var contentEnd = contentStart;
            while (contentEnd < end && text[contentEnd] is not ('`' or '"'))
            {
                contentEnd++;
            }

            if (!At(text, contentEnd, end, '`') || !At(text, contentEnd + 1, end, '"'))
                return Error(position, "The markdown string is not closed by '`\"'.");

            return contentEnd == contentStart
                ? Token(QuadrantChartTokenKind.Skip, position, contentEnd + 2, string.Empty)
                : Token(QuadrantChartTokenKind.MarkdownString, position, contentEnd + 2, text[contentStart..contentEnd]);
        }

        var close = text.IndexOf('"', position + 1, StringComparison.Ordinal);
        if (close < 0 || close >= end)
            return Error(position, "The string is not closed.");

        return close == position + 1
            ? Token(QuadrantChartTokenKind.Skip, position, close + 1, string.Empty)
            : Token(QuadrantChartTokenKind.String, position, close + 1, text[(position + 1)..close]);
    }

    /// <summary>Reads the position of a point: <c>: [x, y]</c>, each coordinate <c>0</c>, <c>1</c> or <c>0.</c> and digits, and the spaces after it.</summary>
    private static bool TryPoint(string text, int position, int end, out QuadrantChartToken token)
    {
        token = default;
        var i = SkipWhiteSpace(text, position, end);
        if (!At(text, i, end, ':'))
            return false;

        i = SkipWhiteSpace(text, i + 1, end);
        if (!At(text, i, end, '['))
            return false;

        i = SkipWhiteSpace(text, i + 1, end);
        if (!TryCoordinate(text, ref i, end, out var x))
        {
            token = Error(i, "The x coordinate of the point is expected: a number from 0 to 1, such as 0.25.");
            return true;
        }

        i = SkipWhiteSpace(text, i, end);
        if (!At(text, i, end, ','))
        {
            token = Error(i, "',' is expected between the coordinates of the point.");
            return true;
        }

        i = SkipWhiteSpace(text, i + 1, end);
        if (!TryCoordinate(text, ref i, end, out var y))
        {
            token = Error(i, "The y coordinate of the point is expected: a number from 0 to 1, such as 0.25.");
            return true;
        }

        i = SkipWhiteSpace(text, i, end);
        if (!At(text, i, end, ']'))
        {
            token = Error(i, "']' is expected after the coordinates of the point.");
            return true;
        }

        i++;
        while (i < end && text[i] == ' ')
        {
            i++;
        }

        token = new QuadrantChartToken(QuadrantChartTokenKind.Point, position, i, text[position..i], x, y);
        return true;
    }

    private static bool TryCoordinate(string text, ref int position, int end, out double value)
    {
        value = 0;
        if (At(text, position, end, '1'))
        {
            value = 1;
            position++;
            return true;
        }

        if (!At(text, position, end, '0'))
            return false;

        var digitsStart = position + 2;
        var digitsEnd = digitsStart;
        while (digitsEnd < end && char.IsAsciiDigit(text[digitsEnd]))
        {
            digitsEnd++;
        }

        // Mermaid reads 0 and any character before the digits: only the decimal point makes a number
        if (digitsEnd == digitsStart || text[position + 1] is '\n' or '\r' or '\u2028' or '\u2029')
        {
            position++;
            return true;
        }

        if (text[position + 1] != '.')
            return false;

        value = double.Parse(text.AsSpan(position, digitsEnd - position), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        position = digitsEnd;
        return true;
    }

    private static bool TryKeywordWithSpaces(string text, int position, int spaces, int end, string keyword, QuadrantChartTokenKind kind, out QuadrantChartToken token)
    {
        token = default;
        if (!StartsWithIgnoreCase(text, spaces, end, keyword))
            return false;

        var keywordEnd = spaces + keyword.Length;
        while (keywordEnd < end && text[keywordEnd] == ' ')
        {
            keywordEnd++;
        }

        token = Token(kind, position, keywordEnd, text[position..keywordEnd]);
        return true;
    }

    private static QuadrantChartToken Run(string text, int position, int end, QuadrantChartTokenKind kind, Func<char, bool> predicate)
    {
        var runEnd = position + 1;
        while (runEnd < end && predicate(text[runEnd]))
        {
            runEnd++;
        }

        return Token(kind, position, runEnd, text[position..runEnd]);
    }

    private static QuadrantChartToken Token(QuadrantChartTokenKind kind, int start, int end, string value) => new(kind, start, end, value);

    private static QuadrantChartToken Error(int position, string message) => new(QuadrantChartTokenKind.Error, position, position, message);

    private static bool At(string text, int position, int end, char c) => position < end && text[position] == c;

    private static bool StartsWithIgnoreCase(string text, int position, int end, string value) => position + value.Length <= end && string.Compare(text, position, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static int LineEnd(string text, int position, int end)
    {
        var lineEnd = position;
        while (lineEnd < end && text[lineEnd] != '\n')
        {
            lineEnd++;
        }

        return lineEnd;
    }

    private static int SkipSpaces(string text, int position, int end)
    {
        while (position < end && text[position] == ' ')
        {
            position++;
        }

        return position;
    }

    private static int SkipWhiteSpace(string text, int position, int end)
    {
        while (position < end && IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static string Describe(char c) => char.IsControl(c) ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : c.ToString();

    /// <summary>Indicates whether a character is white space for JavaScript (<c>\s</c>, and what <c>trim</c> removes).</summary>
    public static bool IsWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';

    /// <summary>Removes the white space of JavaScript at both ends of a text, as <c>String.prototype.trim</c>.</summary>
    public static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return start == 0 && end == value.Length ? value : value[start..end];
    }
}
