using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.RailroadDiagrams;

/// <summary>
/// Parses the railroad diagrams of Mermaid in their four notations: the constructors of <c>railroad-beta</c>, and the
/// grammars of <c>railroad-ebnf-beta</c>, <c>railroad-abnf-beta</c> and <c>railroad-peg-beta</c>. As in Mermaid, every
/// notation becomes the same expressions, white space and line feeds do not matter, and a rule ends with a semicolon.
/// </summary>
internal sealed class RailroadDiagramParser
{
    private static readonly string[] PrimitiveKeywords = ["sequence", "choice", "optional", "oneOrMore", "zeroOrMore", "terminal", "nonterminal", "special"];

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly RailroadDiagram _diagram = new();
    private readonly RailroadNotation _notation;
    private int _depth;
    private int _expressionCount;

    private RailroadDiagramParser(ParseContext context, int position, RailroadNotation notation)
    {
        _context = context;
        _scanner = new Scanner(context, position);
        _notation = notation;
        _diagram.Notation = notation;
    }

    public static RailroadDiagram Parse(ParseContext context, int position, RailroadNotation notation) => new RailroadDiagramParser(context, position, notation).Parse();

    internal static string GetKeyword(RailroadNotation notation) => notation switch
    {
        RailroadNotation.Ebnf => "railroad-ebnf-beta",
        RailroadNotation.Abnf => "railroad-abnf-beta",
        RailroadNotation.Peg => "railroad-peg-beta",
        _ => "railroad-beta",
    };

    private RailroadDiagram Parse()
    {
        var keyword = GetKeyword(_notation);
        var start = _scanner.Position;
        if (!_scanner.TryConsume(keyword))
            throw _scanner.Error(start, $"'{keyword}' is expected.");

        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "railroad");
        while (true)
        {
            SkipTrivia();
            if (_scanner.IsEnd)
                break;

            if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
                continue;

            if (_scanner.StartsWith("title"))
            {
                ParseTitle();
                continue;
            }

            ParseRule();
        }

        return _diagram;
    }

    /// <summary>
    /// Parses a title: as the lexer of Mermaid, <c>title</c> followed by a space takes the rest of the line, and
    /// <c>title</c> followed by anything else is an empty title. A quoted title is unquoted.
    /// </summary>
    private void ParseTitle()
    {
        _scanner.Position += "title".Length;
        var title = string.Empty;
        if (_scanner.Current is ' ' or '\t')
        {
            title = _scanner.ReadRestOfLine().Trim();
            if (title.Length >= 2 && ((title[0] == '"' && title[^1] == '"') || (title[0] == '\'' && title[^1] == '\'')))
            {
                title = _notation is RailroadNotation.Abnf ? title[1..^1] : Unescape(title[1..^1]);
            }
        }

        _diagram.Title = title.Trim().Length is 0 ? null : title;
    }

    private void ParseRule()
    {
        var start = _scanner.Position;
        var name = ReadRuleName() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the name of a rule is expected.");
        SkipTrivia();
        var assignment = _notation switch
        {
            RailroadNotation.Peg => "<-",
            _ => "=",
        };
        if (!_scanner.TryConsume(assignment) && !(_notation is RailroadNotation.Ebnf && _scanner.TryConsume("::=")))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': '{assignment}' is expected after the name of the rule '{name}'.");

        _depth = 0;
        var definition = _notation switch
        {
            RailroadNotation.Ebnf => ParseEbnfChoice(),
            RailroadNotation.Abnf => ParseAbnfAlternation(),
            RailroadNotation.Peg => ParsePegChoice(),
            _ => ParsePrimitive(),
        };
        SkipTrivia();
        if (!TryReadTerminator())
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': ';' is expected at the end of the rule '{name}'.");

        if (_diagram.Rules.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} rules.");

        _diagram.Rules.Add(new RailroadRule(name, definition));
    }

    /// <summary>Reads the semicolon that ends a rule; in ABNF, a semicolon followed by text on its line starts a comment.</summary>
    private bool TryReadTerminator()
    {
        if (_scanner.Current != ';')
            return false;

        _scanner.Position++;
        return true;
    }

    // --- Constructors (railroad-beta)

    private RailroadExpression ParsePrimitive()
    {
        SkipTrivia();
        var start = _scanner.Position;
        var keyword = ReadIdentifier() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an expression such as 'terminal(\"text\")' is expected.");
        if (!PrimitiveKeywords.Contains(keyword, StringComparer.Ordinal))
            throw _scanner.Error(start, $"'{keyword}' is not an expression: use sequence, choice, optional, oneOrMore, zeroOrMore, terminal, nonterminal or special.");

        SkipTrivia();
        _scanner.Expect("(");
        Enter(start);
        RailroadExpression result;
        switch (keyword)
        {
            case "terminal":
                result = new RailroadTerminal(ReadQuotedString());
                break;
            case "nonterminal":
                result = new RailroadNonterminal(ReadQuotedString());
                break;
            case "special":
                result = new RailroadSpecial(ReadQuotedString());
                break;
            case "optional":
                result = new RailroadOptional(ParsePrimitive());
                break;
            case "oneOrMore":
                result = new RailroadRepetition(ParsePrimitive(), 1);
                break;
            case "zeroOrMore":
                result = new RailroadRepetition(ParsePrimitive(), 0);
                break;
            default:
                var items = new List<RailroadExpression> { ParsePrimitive() };
                while (true)
                {
                    SkipTrivia();
                    if (!_scanner.TryConsume(","))
                        break;

                    items.Add(ParsePrimitive());
                }

                result = items.Count == 1 ? items[0] : keyword == "sequence" ? new RailroadSequence(items) : new RailroadChoice(items);
                break;
        }

        SkipTrivia();
        _scanner.Expect(")");
        Leave();
        return result;
    }

    // --- EBNF

    private RailroadExpression ParseEbnfChoice()
    {
        Enter(_scanner.Position);
        var alternatives = new List<RailroadExpression> { ParseEbnfSequence() };
        while (true)
        {
            SkipTrivia();
            if (!_scanner.TryConsume("|"))
                break;

            alternatives.Add(ParseEbnfSequence());
        }

        Leave();
        return alternatives.Count == 1 ? alternatives[0] : new RailroadChoice(alternatives);
    }

    private RailroadExpression ParseEbnfSequence()
    {
        var items = new List<RailroadExpression> { ParseEbnfTerm() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an expression is expected.") };
        while (true)
        {
            SkipTrivia();
            var save = _scanner.Position;
            var comma = _scanner.TryConsume(",");
            var term = ParseEbnfTerm();
            if (term is null)
            {
                if (comma)
                    throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an expression is expected after ','.");

                _scanner.Position = save;
                break;
            }

            items.Add(term);
        }

        return items.Count == 1 ? items[0] : new RailroadSequence(items);
    }

    private RailroadExpression? ParseEbnfTerm()
    {
        var result = ParseEbnfPrimary();
        if (result is null)
            return null;

        while (true)
        {
            SkipTrivia();
            var c = _scanner.Current;
            if (c == '?' && TryMatchSpecial(out _, consume: false))
                return result;

            if (c is '?' or '*' or '+')
            {
                _scanner.Position++;
                result = c switch
                {
                    '?' => new RailroadOptional(result),
                    '*' => new RailroadRepetition(result, 0),
                    _ => new RailroadRepetition(result, 1),
                };
                CountExpression();
                continue;
            }

            if (c == '-')
            {
                // An exception, as Mermaid draws it: the expression, a terminal "-", and the excluded expression
                var start = _scanner.Position;
                _scanner.Position++;
                var except = ParseEbnfPrimary() ?? throw _scanner.Error(start, "An expression is expected after '-'.");
                result = new RailroadSequence(result, new RailroadTerminal("-"), except);
                CountExpression();
                continue;
            }

            return result;
        }
    }

    private RailroadExpression? ParseEbnfPrimary()
    {
        SkipTrivia();
        var start = _scanner.Position;
        var c = _scanner.Current;
        switch (c)
        {
            case '"' or '\'':
                CountExpression();
                return new RailroadTerminal(ReadQuotedString());
            case '?':
                if (!TryMatchSpecial(out var text, consume: true))
                    throw _scanner.Error("A special sequence is written '? text ?'.");

                CountExpression();
                return new RailroadSpecial(text);
            case '(' or '[' or '{':
                _scanner.Position++;
                var inner = ParseEbnfChoice();
                SkipTrivia();
                var close = c switch
                {
                    '(' => ")",
                    '[' => "]",
                    _ => "}",
                };
                if (!_scanner.TryConsume(close))
                    throw _scanner.Error(start, $"'{c}' is not closed by '{close}'.");

                CountExpression();
                return c switch
                {
                    '(' => inner,
                    '[' => new RailroadOptional(inner),
                    _ => new RailroadRepetition(inner, 0),
                };
        }

        var name = ReadIdentifier();
        if (name is null)
            return null;

        CountExpression();
        return new RailroadNonterminal(name);
    }

    /// <summary>Matches a special sequence: text between question marks, without a semicolon, with a character that is not white space.</summary>
    private bool TryMatchSpecial(out string text, bool consume)
    {
        text = string.Empty;
        var source = _scanner.Text;
        var start = _scanner.Position;
        if (start >= source.Length || source[start] != '?')
            return false;

        var end = start + 1;
        var hasText = false;
        while (end < source.Length && source[end] is not ('?' or ';'))
        {
            hasText |= !char.IsWhiteSpace(source[end]);
            end++;
        }

        if (end >= source.Length || source[end] != '?' || !hasText)
            return false;

        var content = source[(start + 1)..end];
        if (content.Contains('\n', StringComparison.Ordinal))
            throw _scanner.Error(start, "A special sequence cannot span several lines.");

        text = content.Trim();
        if (consume)
        {
            _scanner.Position = end + 1;
        }

        return true;
    }

    // --- ABNF

    private RailroadExpression ParseAbnfAlternation()
    {
        Enter(_scanner.Position);
        var alternatives = new List<RailroadExpression> { ParseAbnfConcatenation() };
        while (true)
        {
            SkipTrivia();
            if (!_scanner.TryConsume("/"))
                break;

            alternatives.Add(ParseAbnfConcatenation());
        }

        Leave();
        return alternatives.Count == 1 ? alternatives[0] : new RailroadChoice(alternatives);
    }

    private RailroadExpression ParseAbnfConcatenation()
    {
        var items = new List<RailroadExpression>();
        while (true)
        {
            SkipTrivia();
            var element = ParseAbnfElement();
            if (element is null)
                break;

            items.Add(element);
        }

        if (items.Count is 0)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an expression is expected.");

        return items.Count == 1 ? items[0] : new RailroadSequence(items);
    }

    private RailroadExpression? ParseAbnfElement()
    {
        var start = _scanner.Position;
        int? minimum = null;
        int? maximum = null;
        var hasRepeat = false;
        if (char.IsAsciiDigit(_scanner.Current) || _scanner.Current == '*')
        {
            hasRepeat = true;
            var first = ReadNumber(start);
            if (_scanner.TryConsume("*"))
            {
                minimum = first ?? 0;
                maximum = ReadNumber(start);
            }
            else
            {
                minimum = first;
                maximum = first;
            }

            SkipTrivia();
        }

        var primary = ParseAbnfPrimary();
        if (primary is null)
        {
            if (hasRepeat)
                throw _scanner.Error(start, "An expression is expected after the repetition.");

            return null;
        }

        if (!hasRepeat)
            return primary;

        CountExpression();
        return minimum is 0 && maximum is 1 ? new RailroadOptional(primary) : new RailroadRepetition(primary, minimum!.Value, maximum);
    }

    private RailroadExpression? ParseAbnfPrimary()
    {
        var start = _scanner.Position;
        var c = _scanner.Current;
        switch (c)
        {
            case '"':
                {
                    var end = _scanner.Text.IndexOfOrdinal('"', start + 1);
                    if (end < 0)
                        throw _scanner.Error(start, "The string is not closed.");

                    var value = _scanner.Text[(start + 1)..end];
                    if (value.Contains('\n', StringComparison.Ordinal))
                        throw _scanner.Error(start, "A string cannot span several lines.");

                    _scanner.Position = end + 1;
                    CountExpression();
                    return new RailroadTerminal(value);
                }

            case '%':
                {
                    var length = MatchNumericValue(_scanner.Text, start);
                    if (length is 0)
                        throw _scanner.Error(start, "A numeric value is written %x41, %d65 or %b1000001, with ranges such as %x30-39.");

                    _scanner.Position += length;
                    CountExpression();
                    return new RailroadTerminal(_scanner.Text.Substring(start, length));
                }

            case '(' or '[':
                _scanner.Position++;
                var inner = ParseAbnfAlternation();
                SkipTrivia();
                var close = c == '(' ? ")" : "]";
                if (!_scanner.TryConsume(close))
                    throw _scanner.Error(start, $"'{c}' is not closed by '{close}'.");

                CountExpression();
                return c == '(' ? inner : new RailroadOptional(inner);
        }

        var name = ReadIdentifier();
        if (name is null)
            return null;

        CountExpression();
        return new RailroadNonterminal(name);
    }

    /// <summary>Returns the length of a numeric value of ABNF at a position, such as <c>%x30-39</c> or <c>%d13.10</c>, or 0.</summary>
    internal static int MatchNumericValue(string text, int start)
    {
        if (start + 2 >= text.Length || text[start] != '%' || text[start + 1] is not ('x' or 'X' or 'd' or 'D' or 'b' or 'B'))
            return 0;

        var i = start + 2;
        var digits = CountHex(text, i);
        if (digits is 0)
            return 0;

        i += digits;
        while (i + 1 < text.Length && text[i] is '-' or '.')
        {
            var more = CountHex(text, i + 1);
            if (more is 0)
                break;

            i += 1 + more;
        }

        return i - start;

        static int CountHex(string text, int index)
        {
            var count = 0;
            while (index + count < text.Length && char.IsAsciiHexDigit(text[index + count]))
            {
                count++;
            }

            return count;
        }
    }

    private int? ReadNumber(int offset)
    {
        var start = _scanner.Position;
        while (char.IsAsciiDigit(_scanner.Current))
        {
            _scanner.Position++;
        }

        if (_scanner.Position == start)
            return null;

        var digits = _scanner.Text[start.._scanner.Position];
        if (digits.Length > 6 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > 100_000)
            throw _scanner.Error(offset, "A repetition is 100000 times at most.");

        return value;
    }

    // --- PEG

    private RailroadExpression ParsePegChoice()
    {
        Enter(_scanner.Position);
        var alternatives = new List<RailroadExpression> { ParsePegSequence() };
        while (true)
        {
            SkipTrivia();
            if (!_scanner.TryConsume("/"))
                break;

            alternatives.Add(ParsePegSequence());
        }

        Leave();
        return alternatives.Count == 1 ? alternatives[0] : new RailroadChoice(alternatives);
    }

    private RailroadExpression ParsePegSequence()
    {
        var items = new List<RailroadExpression>();
        while (true)
        {
            SkipTrivia();
            var item = ParsePegPrefix();
            if (item is null)
                break;

            items.Add(item);
        }

        if (items.Count is 0)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an expression is expected.");

        return items.Count == 1 ? items[0] : new RailroadSequence(items);
    }

    private RailroadExpression? ParsePegPrefix()
    {
        var start = _scanner.Position;
        var predicate = _scanner.Current is '&' or '!' ? _scanner.Current : '\0';
        if (predicate != '\0')
        {
            _scanner.Position++;
            SkipTrivia();
        }

        var suffix = ParsePegSuffix();
        if (suffix is null)
        {
            if (predicate != '\0')
                throw _scanner.Error(start, $"An expression is expected after '{predicate}'.");

            return null;
        }

        if (predicate == '\0')
            return suffix;

        // As Mermaid, a predicate is a special sequence labeled with the expression it looks at
        CountExpression();
        return new RailroadSpecial(predicate + GetPredicateLabel(suffix));
    }

    internal static string GetPredicateLabel(RailroadExpression expression) => expression switch
    {
        RailroadTerminal terminal => "\"" + terminal.Value + "\"",
        RailroadNonterminal nonterminal => nonterminal.Name,
        RailroadSpecial special => special.Text,
        _ => "(...)",
    };

    private RailroadExpression? ParsePegSuffix()
    {
        var primary = ParsePegPrimary();
        if (primary is null)
            return null;

        SkipTrivia();
        switch (_scanner.Current)
        {
            case '?':
                _scanner.Position++;
                CountExpression();
                return new RailroadOptional(primary);
            case '*':
                _scanner.Position++;
                CountExpression();
                return new RailroadRepetition(primary, 0);
            case '+':
                _scanner.Position++;
                CountExpression();
                return new RailroadRepetition(primary, 1);
            default:
                return primary;
        }
    }

    private RailroadExpression? ParsePegPrimary()
    {
        var start = _scanner.Position;
        switch (_scanner.Current)
        {
            case '"' or '\'':
                CountExpression();
                return new RailroadTerminal(ReadQuotedString());
            case '.':
                _scanner.Position++;
                CountExpression();
                return new RailroadSpecial(".");
            case '(':
                _scanner.Position++;
                var inner = ParsePegChoice();
                SkipTrivia();
                if (!_scanner.TryConsume(")"))
                    throw _scanner.Error(start, "'(' is not closed by ')'.");

                return inner;
        }

        // The arrow of the next rule ends the sequence: Name <- ...
        var save = _scanner.Position;
        var name = ReadIdentifier();
        if (name is null)
            return null;

        SkipTrivia();
        if (_scanner.StartsWith("<-"))
        {
            _scanner.Position = save;
            return null;
        }

        CountExpression();
        return new RailroadNonterminal(name);
    }

    // --- Shared

    /// <summary>Reads an identifier: <c>[A-Za-z_][A-Za-z0-9_-]*</c>, or <c>[A-Za-z][A-Za-z0-9-]*</c> in ABNF. As in Mermaid, an identifier cannot start with <c>title</c>.</summary>
    private string? ReadIdentifier()
    {
        var start = _scanner.Position;
        if (!IsIdentifierStart(_scanner.Current, _notation))
            return null;

        var end = start + 1;
        while (end < _scanner.Text.Length && IsIdentifierPart(_scanner.Text[end], _notation))
        {
            end++;
        }

        var identifier = _scanner.Text[start..end];
        if (identifier.StartsWith("title", StringComparison.Ordinal))
            throw _scanner.Error(start, $"'{identifier}' starts with the keyword 'title' and cannot be an identifier.");

        _scanner.Position = end;
        return identifier;
    }

    private string? ReadRuleName()
    {
        var start = _scanner.Position;
        var name = ReadIdentifier();
        if (name is not null && _notation is RailroadNotation.Primitives && PrimitiveKeywords.Contains(name, StringComparer.Ordinal))
            throw _scanner.Error(start, $"'{name}' is a keyword and cannot be the name of a rule.");

        return name;
    }

    internal static bool IsIdentifierStart(char c, RailroadNotation notation) => char.IsAsciiLetter(c) || (c == '_' && notation is not RailroadNotation.Abnf);

    internal static bool IsIdentifierPart(char c, RailroadNotation notation) => char.IsAsciiLetterOrDigit(c) || c == '-' || (c == '_' && notation is not RailroadNotation.Abnf);

    /// <summary>Indicates whether a name can be written as an identifier of a notation.</summary>
    internal static bool IsValidIdentifier(string name, RailroadNotation notation)
    {
        if (name.Length is 0 || !IsIdentifierStart(name[0], notation) || name.StartsWith("title", StringComparison.Ordinal))
            return false;

        foreach (var c in name)
        {
            if (!IsIdentifierPart(c, notation))
                return false;
        }

        return true;
    }

    /// <summary>Reads a string between double or single quotes, with the backslash escapes <c>\n</c>, <c>\r</c>, <c>\t</c> and <c>\x</c>.</summary>
    private string ReadQuotedString()
    {
        SkipTrivia();
        var start = _scanner.Position;
        var quote = _scanner.Current;
        if (quote is not ('"' or '\''))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a quoted string is expected.");

        var i = start + 1;
        var text = _scanner.Text;
        while (i < text.Length && text[i] != quote)
        {
            if (text[i] == '\\')
            {
                if (i + 1 >= text.Length || text[i + 1] is '\n')
                    throw _scanner.Error(start, "The string is not closed.");

                i++;
            }

            i++;
        }

        if (i >= text.Length)
            throw _scanner.Error(start, "The string is not closed.");

        _scanner.Position = i + 1;
        return Unescape(text[(start + 1)..i]);
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\\' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    var other => other,
                });
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Skips the white space, the line feeds and the comments: <c>%%</c>, and <c>/* */</c>, <c>(* *)</c>, <c># </c> or <c>; </c> by notation.</summary>
    private void SkipTrivia()
    {
        var text = _scanner.Text;
        while (!_scanner.IsEnd)
        {
            var c = _scanner.Current;
            if (char.IsWhiteSpace(c))
            {
                _scanner.Position++;
                continue;
            }

            if (_scanner.IsCommentAt(_scanner.Position) || (c == '#' && _notation is RailroadNotation.Peg))
            {
                SkipToLineEnd();
                continue;
            }

            // In ABNF, a semicolon followed by text on its line starts a comment; alone at the end of its line, it ends the rule
            if (c == ';' && _notation is RailroadNotation.Abnf && _scanner.Peek() is not ('\n' or '\0'))
            {
                SkipToLineEnd();
                continue;
            }

            if ((_scanner.StartsWith("/*") && _notation is RailroadNotation.Primitives or RailroadNotation.Ebnf) || (_scanner.StartsWith("(*") && _notation is RailroadNotation.Ebnf))
            {
                var close = c == '/' ? "*/" : "*)";
                var end = text.IndexOf(close, _scanner.Position + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw _scanner.Error("The comment is not closed by '" + close + "'.");

                _scanner.Position = end + 2;
                continue;
            }

            return;
        }
    }

    private void SkipToLineEnd()
    {
        while (!_scanner.IsEnd && _scanner.Current != '\n')
        {
            _scanner.Position++;
        }
    }

    private void Enter(int offset)
    {
        if (++_depth > _context.Options.MaxDepth)
            throw _scanner.Error(offset, $"The expressions are nested deeper than {_context.Options.MaxDepth} levels.");
    }

    private void Leave() => _depth--;

    private void CountExpression()
    {
        if (++_expressionCount > _context.Options.MaxNodes * 10)
            throw _scanner.Error($"The diagram has more than {_context.Options.MaxNodes * 10} expressions.");
    }
}
