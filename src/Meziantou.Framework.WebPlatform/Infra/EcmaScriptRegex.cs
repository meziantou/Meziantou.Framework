using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>
/// Compiles an ECMAScript regular expression with the <c>v</c> flag (unicodeSets, https://tc39.es/ecma262/#sec-patterns) to a .NET
/// regular expression with the same matching semantics: the pattern is validated with the ECMAScript grammar (invalid patterns
/// return null, as RegExpCreate throws a SyntaxError), and translated to a .NET pattern that matches code points rather than
/// UTF-16 code units, the ECMAScript character class escapes (\d, \w, \s), the class set operations (<c>--</c>, <c>&amp;&amp;</c>,
/// nested classes, <c>\q{}</c>) and keeps the numbering of the capturing groups of ECMAScript (named groups are numbered in
/// source order, as the unnamed groups).
/// </summary>
/// <remarks>
/// Known differences: the Unicode property escapes support the General_Category values and the Any, ASCII, Assigned,
/// ASCII_Hex_Digit and Hex_Digit properties (other properties, such as scripts, make the pattern invalid), and they only match
/// BMP code points; a backreference to a group that has not participated in the match fails instead of matching the empty string.
/// </remarks>
internal static class EcmaScriptRegex
{
    // A code point that starts with a high surrogate: a surrogate pair, or a lone high surrogate. The low surrogate is not optional,
    // so backtracking never splits a surrogate pair.
    private const string HighSurrogateCodePoint = "[\\uD800-\\uDBFF](?:[\\uDC00-\\uDFFF]|(?![\\uDC00-\\uDFFF]))";

    // Any code point: a surrogate pair, or any other single code unit (including lone surrogates)
    private const string AnyCodePoint = "(?:[^\\uD800-\\uDBFF]|" + HighSurrogateCodePoint + ")";
    private const string LineTerminators = "\\n\\r\\u2028\\u2029";

    // https://tc39.es/ecma262/#sec-compiletocharset (\s: WhiteSpace and LineTerminator)
    private const string WhiteSpaceClass = "\\t\\n\\u000B\\f\\r\\u0020\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000\\uFEFF";
    private const string DigitClass = "0-9";
    private const string WordClass = "a-zA-Z0-9_";

    /// <summary>RegExpCreate(pattern, "v" or "vi"): returns the regular expression, or null if the pattern is not valid.</summary>
    /// <remarks>
    /// ECMAScript regular expressions have no timeout: the default timeout of the application (REGEX_DEFAULT_MATCH_TIMEOUT) applies.
    /// </remarks>
    public static Regex? Create(string pattern, bool ignoreCase) => Create(pattern, ignoreCase, Regex.InfiniteMatchTimeout);

    /// <summary>RegExpCreate(pattern, "v" or "vi") with a match timeout: returns the regular expression, or null if the pattern is not valid.</summary>
    /// <remarks><see cref="Regex.InfiniteMatchTimeout"/> uses the default timeout of the application (REGEX_DEFAULT_MATCH_TIMEOUT).</remarks>
    [SuppressMessage("Security", "MA0009:Regular expressions should not be vulnerable to Denial of Service attacks", Justification = "Without an explicit timeout, the application default timeout applies")]
    public static Regex? Create(string pattern, bool ignoreCase, TimeSpan matchTimeout)
    {
        var translated = Translate(pattern);
        if (translated is null)
            return null;

        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            return matchTimeout == Regex.InfiniteMatchTimeout ? new Regex(translated, options) : new Regex(translated, options, matchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Translates a pattern (with the v flag) to a .NET pattern, or returns null if the pattern is not valid.</summary>
    public static string? Translate(string pattern)
    {
        try
        {
            return new Translator(pattern).Translate();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class Translator
    {
        private readonly int[] _input;
        private readonly Dictionary<string, int> _groupNames = new(StringComparer.Ordinal);
        private int _position;
        private int _groupCount;
        private int _helperGroupCount;
        private int _lookbehindDepth;
        private bool _dotAll;
        private bool _multiline;

        public Translator(string pattern)
        {
            var codePoints = new List<int>(pattern.Length);
            for (var i = 0; i < pattern.Length; i++)
            {
                if (char.IsHighSurrogate(pattern[i]) && i + 1 < pattern.Length && char.IsLowSurrogate(pattern[i + 1]))
                {
                    codePoints.Add(char.ConvertToUtf32(pattern[i], pattern[i + 1]));
                    i++;
                }
                else
                {
                    codePoints.Add(pattern[i]);
                }
            }

            _input = [.. codePoints];
        }

        private bool IsEof => _position >= _input.Length;

        private int Peek(int offset = 0) => _position + offset < _input.Length ? _input[_position + offset] : -1;

        private static FormatException Error() => new("Invalid regular expression");

        private void Expect(int c)
        {
            if (Peek() != c)
                throw Error();

            _position++;
        }

        public string Translate()
        {
            CollectGroups();
            var result = ParseDisjunction(out _);
            if (!IsEof)
                throw Error(); // e.g. an unmatched ")"

            return result;
        }

        /// <summary>Counts the capturing groups and collects the group names (needed by the backreferences, which may precede the group).</summary>
        private void CollectGroups()
        {
            var classDepth = 0;
            for (var i = 0; i < _input.Length; i++)
            {
                var c = _input[i];
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '[')
                {
                    classDepth++;
                }
                else if (c == ']' && classDepth > 0)
                {
                    classDepth--;
                }
                else if (c == '(' && classDepth == 0)
                {
                    if (i + 1 < _input.Length && _input[i + 1] == '?')
                    {
                        if (i + 2 < _input.Length && _input[i + 2] == '<' && i + 3 < _input.Length && _input[i + 3] is not '=' and not '!')
                        {
                            _groupCount++;
                            var end = Array.IndexOf(_input, '>', i + 3);
                            if (end < 0)
                                throw Error();

                            var name = CodePointsToString(_input.AsSpan(i + 3, end - i - 3));
                            if (!_groupNames.TryAdd(name, _groupCount))
                                throw Error(); // Duplicate group name
                        }
                    }
                    else
                    {
                        _groupCount++;
                    }
                }
            }
        }

        // Disjunction :: Alternative ( | Alternative )*
        // nullable: whether the pattern can match the empty string
        private string ParseDisjunction(out bool nullable)
        {
            var result = new StringBuilder();
            result.Append(ParseAlternative(out nullable));
            while (Peek() == '|')
            {
                _position++;
                result.Append('|').Append(ParseAlternative(out var alternativeNullable));
                nullable |= alternativeNullable;
            }

            return result.ToString();
        }

        private string ParseAlternative(out bool nullable)
        {
            var result = new StringBuilder();
            nullable = true;
            while (!IsEof && Peek() is not '|' and not ')')
            {
                nullable &= ParseTerm(result);
            }

            return result.ToString();
        }

        /// <summary>Parses a term and returns whether it can match the empty string.</summary>
        private bool ParseTerm(StringBuilder result)
        {
            var c = Peek();
            switch (c)
            {
                case '^':
                    _position++;
                    result.Append(_multiline ? "(?:^|(?<=[" + LineTerminators + "]))" : "^");
                    EnsureNoQuantifier();
                    return true;

                case '$':
                    _position++;
                    result.Append(_multiline ? "(?=[" + LineTerminators + "]|\\z)" : "\\z");
                    EnsureNoQuantifier();
                    return true;

                case '\\' when Peek(1) is 'b' or 'B':
                {
                    // \b is a word boundary for the ASCII word characters of \w
                    var isBoundary = Peek(1) == 'b';
                    _position += 2;
                    result.Append(isBoundary
                        ? "(?:(?<=[" + WordClass + "])(?![" + WordClass + "])|(?<![" + WordClass + "])(?=[" + WordClass + "]))"
                        : "(?:(?<=[" + WordClass + "])(?=[" + WordClass + "])|(?<![" + WordClass + "])(?![" + WordClass + "]))");
                    EnsureNoQuantifier();
                    return true;
                }

                case '(' when Peek(1) == '?' && (Peek(2) is '=' or '!' || (Peek(2) == '<' && Peek(3) is '=' or '!')):
                {
                    // Lookaround assertions: they cannot be quantified with the u and v flags
                    var isLookbehind = Peek(2) == '<';
                    var prefix = isLookbehind ? "(?<" + (char)Peek(3) : "(?" + (char)Peek(2);
                    _position += isLookbehind ? 4 : 3;
                    _lookbehindDepth += isLookbehind ? 1 : 0;
                    var body = ParseDisjunction(out _);
                    _lookbehindDepth -= isLookbehind ? 1 : 0;
                    Expect(')');
                    result.Append(prefix).Append(body).Append(')');
                    EnsureNoQuantifier();
                    return true;
                }
            }

            var (atom, isSingle, atomNullable) = ParseAtom();
            var quantifier = TryParseQuantifier(out var min);
            if (quantifier is null)
            {
                result.Append(atom);
                return atomNullable;
            }

            if (min == 0 && atomNullable && _lookbehindDepth == 0)
            {
                // RepeatMatcher (https://tc39.es/ecma262/#sec-runtime-semantics-repeatmatcher-abstract-operation): once the minimum
                // is reached, an iteration that matches the empty string fails (e.g. "(.*)?" leaves the group undefined at the end of
                // the input), while .NET accepts it. The iteration must not end where it started: the remaining input is captured
                // before the iteration, and must not be the remaining input after it. The helper groups are named, so the .NET
                // numbers of the ECMAScript groups are unchanged.
                var name = "e" + (++_helperGroupCount).ToString(CultureInfo.InvariantCulture);
                result.Append("(?:(?=(?<").Append(name).Append(">[\\s\\S]*))").Append(atom).Append("(?!\\k<").Append(name).Append(">\\z))").Append(quantifier);
            }
            else if (isSingle)
            {
                result.Append(atom).Append(quantifier);
            }
            else
            {
                result.Append("(?:").Append(atom).Append(')').Append(quantifier);
            }

            return min == 0 || atomNullable;
        }

        private void EnsureNoQuantifier()
        {
            if (Peek() is '*' or '+' or '?' or '{')
                throw Error();
        }

        // Quantifier :: QuantifierPrefix ?opt
        private string? TryParseQuantifier(out long min)
        {
            string prefix;
            min = 0;
            switch (Peek())
            {
                case '*' or '+' or '?':
                    min = Peek() == '+' ? 1 : 0;
                    prefix = ((char)Peek()).ToString();
                    _position++;
                    break;

                case '{':
                {
                    // With the u and v flags, a "{" that does not start a quantifier is an error
                    _position++;
                    min = ParseDecimalDigits() ?? throw Error();
                    long? max = min;
                    if (Peek() == ',')
                    {
                        _position++;
                        max = ParseDecimalDigits();
                    }

                    Expect('}');
                    if (max is not null && max < min)
                        throw Error();

                    var minValue = Math.Min(min, int.MaxValue);
                    prefix = max == min ? $"{{{minValue}}}"
                        : max is null ? $"{{{minValue},}}"
                        : $"{{{minValue},{Math.Min(max.Value, int.MaxValue)}}}";
                    break;
                }

                default:
                    return null;
            }

            if (Peek() == '?')
            {
                _position++;
                prefix += "?";
            }

            return prefix;
        }

        private long? ParseDecimalDigits()
        {
            if (Peek() is not (>= '0' and <= '9'))
                return null;

            long value = 0;
            while (Peek() is >= '0' and <= '9')
            {
                value = Math.Min(value * 10 + (Peek() - '0'), long.MaxValue / 20);
                _position++;
            }

            return value;
        }

        /// <summary>Parses an atom. Returns the .NET pattern, whether it can be quantified without a group, and whether it can match the empty string.</summary>
        private (string Pattern, bool IsSingle, bool Nullable) ParseAtom()
        {
            var c = Peek();
            switch (c)
            {
                case '.':
                    _position++;
                    return (_dotAll ? AnyCodePoint : CodePointNotIn(LineTerminators), true, false);

                case '(':
                {
                    var group = ParseGroup(out var nullable);
                    return (group, true, nullable);
                }

                case '[':
                    _position++;
                    return (EmitClass(ParseClassContents()), true, false);

                case '\\':
                {
                    _position++;
                    var isBackreference = Peek() is (>= '1' and <= '9') or 'k';
                    var (pattern, isSingle) = ParseAtomEscape();
                    return (pattern, isSingle, isBackreference);
                }

                // SyntaxCharacter that cannot start an atom (with the u and v flags, lone "{", "}" and "]" are errors)
                case '*' or '+' or '?' or '{' or '}' or ']' or ')' or '|':
                    throw Error();

                default:
                {
                    _position++;
                    var (pattern, isSingle) = EmitCodePoint(c);
                    return (pattern, isSingle, false);
                }
            }
        }

        private string ParseGroup(out bool nullable)
        {
            Expect('(');
            if (Peek() != '?')
            {
                var body = ParseDisjunction(out nullable);
                Expect(')');
                return "(" + body + ")";
            }

            _position++;
            if (Peek() == ':')
            {
                _position++;
                var body = ParseDisjunction(out nullable);
                Expect(')');
                return "(?:" + body + ")";
            }

            if (Peek() == '<')
            {
                // Named capturing group: an unnamed .NET group, so the group numbers are the ones of ECMAScript
                _position++;
                ParseGroupName();
                var body = ParseDisjunction(out nullable);
                Expect(')');
                return "(" + body + ")";
            }

            // Modifiers: (?ims-ims:Disjunction) (https://tc39.es/ecma262/#prod-RegularExpressionModifiers)
            var add = ParseModifiers();
            var remove = "";
            if (Peek() == '-')
            {
                _position++;
                remove = ParseModifiers();
                if (add.Length == 0 && remove.Length == 0)
                    throw Error();
            }
            else if (add.Length == 0)
            {
                throw Error();
            }

            if (add.Intersect(remove).Any())
                throw Error();

            Expect(':');
            var (dotAll, multiline) = (_dotAll, _multiline);
            if (add.Contains('s', StringComparison.Ordinal))
            {
                _dotAll = true;
            }

            if (remove.Contains('s', StringComparison.Ordinal))
            {
                _dotAll = false;
            }

            if (add.Contains('m', StringComparison.Ordinal))
            {
                _multiline = true;
            }

            if (remove.Contains('m', StringComparison.Ordinal))
            {
                _multiline = false;
            }

            var groupBody = ParseDisjunction(out nullable);
            Expect(')');
            (_dotAll, _multiline) = (dotAll, multiline);

            // The s and m flags are applied by the translation of "." "^" and "$"; the i flag is a .NET inline option
            var inline = (add.Contains('i', StringComparison.Ordinal) ? "i" : "") + (remove.Contains('i', StringComparison.Ordinal) ? "-i" : "");
            return "(?" + inline + ":" + groupBody + ")";
        }

        private string ParseModifiers()
        {
            var result = "";
            while (Peek() is 'i' or 'm' or 's')
            {
                var flag = (char)Peek();
                if (result.Contains(flag, StringComparison.Ordinal))
                    throw Error();

                result += flag;
                _position++;
            }

            return result;
        }

        // GroupName :: < RegExpIdentifierName >
        private string ParseGroupName()
        {
            var start = _position;
            while (!IsEof && Peek() != '>')
            {
                var c = Peek();
                var isValid = _position == start
                    ? c is '$' or '_' || UnicodeProperties.IsIdStart(c)
                    : c is '$' or 0x200C or 0x200D || UnicodeProperties.IsIdContinue(c);
                if (!isValid)
                    throw Error();

                _position++;
            }

            if (_position == start)
                throw Error();

            var name = CodePointsToString(_input.AsSpan(start, _position - start));
            Expect('>');
            return name;
        }

        // AtomEscape (with the u and v flags)
        private (string Pattern, bool IsSingle) ParseAtomEscape()
        {
            var c = Peek();
            if (c is >= '1' and <= '9')
            {
                var index = ParseDecimalDigits()!.Value;
                if (index > _groupCount)
                    throw Error();

                return ("(?:\\" + index.ToString(CultureInfo.InvariantCulture) + ")", true);
            }

            if (c == 'k')
            {
                _position++;
                Expect('<');
                var name = ParseGroupName();
                if (!_groupNames.TryGetValue(name, out var index))
                    throw Error();

                return ("(?:\\" + index.ToString(CultureInfo.InvariantCulture) + ")", true);
            }

            if (TryParseClassEscape() is { } classEscape)
                return (EmitClass(classEscape), true);

            return EmitCodePoint(ParseCharacterEscape(inClass: false));
        }

        // CharacterClassEscape :: d D s S w W p{} P{}
        private ClassNode? TryParseClassEscape()
        {
            var c = Peek();
            switch (c)
            {
                case 'd' or 'D':
                    _position++;
                    return new ClassNode(c == 'D', [new ClassItem(DigitClass)]);

                case 's' or 'S':
                    _position++;
                    return new ClassNode(c == 'S', [new ClassItem(WhiteSpaceClass)]);

                case 'w' or 'W':
                    _position++;
                    return new ClassNode(c == 'W', [new ClassItem(WordClass)]);

                case 'p' or 'P':
                {
                    _position++;
                    Expect('{');
                    var start = _position;
                    while (!IsEof && Peek() != '}')
                    {
                        _position++;
                    }

                    var expression = CodePointsToString(_input.AsSpan(start, _position - start));
                    Expect('}');
                    return new ClassNode(c == 'P', [GetUnicodePropertyItem(expression)]);
                }

                default:
                    return null;
            }
        }

        private static ClassItem GetUnicodePropertyItem(string expression)
        {
            var separator = expression.IndexOf('=', StringComparison.Ordinal);
            string value;
            if (separator >= 0)
            {
                var name = expression[..separator];
                if (name is not ("General_Category" or "gc"))
                    throw Error(); // Scripts and other properties with values are not supported

                value = expression[(separator + 1)..];
            }
            else
            {
                switch (expression)
                {
                    case "Any":
                        return new ClassItem(Pattern: AnyCodePoint);
                    case "ASCII":
                        return new ClassItem("\\u0000-\\u007F");
                    case "Assigned":
                        return new ClassItem(Pattern: CodePointNotIn("\\p{Cn}"));

                    // https://www.unicode.org/Public/UCD/latest/ucd/PropList.txt (the code points of these properties are stable)
                    case "ASCII_Hex_Digit" or "AHex":
                        return new ClassItem("0-9A-Fa-f");
                    case "Hex_Digit" or "Hex":
                        return new ClassItem("0-9A-Fa-f\\uFF10-\\uFF19\\uFF21-\\uFF26\\uFF41-\\uFF46");
                }

                value = expression;
            }

            var category = GetGeneralCategory(value) ?? throw Error();
            return category == "LC" ? new ClassItem("\\p{Lu}\\p{Ll}\\p{Lt}") : new ClassItem("\\p{" + category + "}");
        }

        // https://www.unicode.org/Public/UCD/latest/ucd/PropertyValueAliases.txt (gc)
        private static string? GetGeneralCategory(string value) => value switch
        {
            "C" or "Other" => "C",
            "Cc" or "Control" or "cntrl" => "Cc",
            "Cf" or "Format" => "Cf",
            "Cn" or "Unassigned" => "Cn",
            "Co" or "Private_Use" => "Co",
            "Cs" or "Surrogate" => "Cs",
            "L" or "Letter" => "L",
            "LC" or "Cased_Letter" => "LC",
            "Ll" or "Lowercase_Letter" => "Ll",
            "Lm" or "Modifier_Letter" => "Lm",
            "Lo" or "Other_Letter" => "Lo",
            "Lt" or "Titlecase_Letter" => "Lt",
            "Lu" or "Uppercase_Letter" => "Lu",
            "M" or "Mark" or "Combining_Mark" => "M",
            "Mc" or "Spacing_Mark" => "Mc",
            "Me" or "Enclosing_Mark" => "Me",
            "Mn" or "Nonspacing_Mark" => "Mn",
            "N" or "Number" => "N",
            "Nd" or "Decimal_Number" or "digit" => "Nd",
            "Nl" or "Letter_Number" => "Nl",
            "No" or "Other_Number" => "No",
            "P" or "Punctuation" or "punct" => "P",
            "Pc" or "Connector_Punctuation" => "Pc",
            "Pd" or "Dash_Punctuation" => "Pd",
            "Pe" or "Close_Punctuation" => "Pe",
            "Pf" or "Final_Punctuation" => "Pf",
            "Pi" or "Initial_Punctuation" => "Pi",
            "Po" or "Other_Punctuation" => "Po",
            "Ps" or "Open_Punctuation" => "Ps",
            "S" or "Symbol" => "S",
            "Sc" or "Currency_Symbol" => "Sc",
            "Sk" or "Modifier_Symbol" => "Sk",
            "Sm" or "Math_Symbol" => "Sm",
            "So" or "Other_Symbol" => "So",
            "Z" or "Separator" => "Z",
            "Zl" or "Line_Separator" => "Zl",
            "Zp" or "Paragraph_Separator" => "Zp",
            "Zs" or "Space_Separator" => "Zs",
            _ => null,
        };

        // CharacterEscape (with the u and v flags), after the "\"
        private int ParseCharacterEscape(bool inClass)
        {
            var c = Peek();
            _position++;
            switch (c)
            {
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'v':
                    return 0x0B;
                case 'b' when inClass:
                    return 0x08;
                case 'c':
                {
                    var letter = Peek();
                    if (letter is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z'))
                        throw Error();

                    _position++;
                    return letter % 32;
                }

                case '0':
                    if (Peek() is >= '0' and <= '9')
                        throw Error();

                    return 0;

                case 'x':
                    return ParseHexDigits(2);

                case 'u':
                {
                    if (Peek() == '{')
                    {
                        _position++;
                        var value = 0;
                        var count = 0;
                        while (Peek() != '}')
                        {
                            var digit = HexValue(Peek());
                            if (digit < 0)
                                throw Error();

                            value = (value * 16) + digit;
                            if (value > 0x10FFFF)
                                throw Error();

                            count++;
                            _position++;
                        }

                        _position++;
                        if (count == 0)
                            throw Error();

                        return value;
                    }

                    var codeUnit = ParseHexDigits(4);

                    // A surrogate pair written with two \u escapes is a single code point
                    if (codeUnit is >= 0xD800 and <= 0xDBFF && Peek() == '\\' && Peek(1) == 'u')
                    {
                        var position = _position;
                        _position += 2;
                        var trail = TryParseHexDigits(4);
                        if (trail is >= 0xDC00 and <= 0xDFFF)
                            return char.ConvertToUtf32((char)codeUnit, (char)trail.Value);

                        _position = position;
                    }

                    return codeUnit;
                }

                // IdentityEscape :: SyntaxCharacter or "/"
                case '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/':
                    return c;

                // ClassSetReservedPunctuator (in a class, with the v flag)
                case '&' or '-' or '!' or '#' or '%' or ',' or ':' or ';' or '<' or '=' or '>' or '@' or '`' or '~' when inClass:
                    return c;

                default:
                    throw Error();
            }
        }

        private int ParseHexDigits(int count) => TryParseHexDigits(count) ?? throw Error();

        private int? TryParseHexDigits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                var digit = HexValue(Peek());
                if (digit < 0)
                    return null;

                value = (value * 16) + digit;
                _position++;
            }

            return value;
        }

        private static int HexValue(int c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };

        // ClassSetExpression (https://tc39.es/ecma262/#prod-ClassSetExpression), after the "["
        private ClassNode ParseClassContents()
        {
            var negated = false;
            if (Peek() == '^')
            {
                negated = true;
                _position++;
            }

            if (Peek() == ']')
            {
                _position++;
                return new ClassNode(negated, []);
            }

            var first = ParseClassSetOperand(allowRange: true, out var firstIsRange);
            if (IsDoublePunctuator('&') || IsDoublePunctuator('-'))
            {
                // ClassIntersection or ClassSubtraction: operands are not ranges
                if (firstIsRange)
                    throw Error();

                var op = (char)Peek();
                var operands = new List<ClassItem> { first };
                while (IsDoublePunctuator(op))
                {
                    _position += 2;
                    if (op == '&' && Peek() == '&')
                        throw Error(); // "&&&"

                    operands.Add(ParseClassSetOperand(allowRange: false, out _));
                }

                Expect(']');
                var item = op == '&' ? new ClassItem(Intersection: operands) : new ClassItem(Subtraction: operands);
                return new ClassNode(negated, [item]);
            }

            // ClassUnion
            var items = new List<ClassItem> { first };
            while (Peek() != ']')
            {
                if (IsEof || IsDoublePunctuator('&') || IsDoublePunctuator('-'))
                    throw Error();

                items.Add(ParseClassSetOperand(allowRange: true, out _));
            }

            _position++;
            return new ClassNode(negated, items);
        }

        private bool IsDoublePunctuator(int c) => Peek() == c && Peek(1) == c;

        // ClassSetRange, ClassSetOperand (NestedClass, ClassStringDisjunction, ClassSetCharacter, CharacterClassEscape)
        private ClassItem ParseClassSetOperand(bool allowRange, out bool isRange)
        {
            isRange = false;
            var c = Peek();
            if (c == '[')
            {
                _position++;
                return new ClassItem(Nested: ParseClassContents());
            }

            if (c == '\\')
            {
                _position++;
                if (TryParseClassEscape() is { } classEscape)
                    return classEscape is { Negated: false, Items: [var single] } ? single : new ClassItem(Nested: classEscape);

                if (Peek() == 'q')
                {
                    _position++;
                    return ParseClassStringDisjunction();
                }

                _position--;
            }

            var from = ParseClassSetCharacter();
            if (allowRange && Peek() == '-' && Peek(1) != '-')
            {
                _position++;
                var to = ParseClassSetCharacter();
                if (to < from)
                    throw Error();

                isRange = true;
                return new ClassItem(Range: (from, to));
            }

            return new ClassItem(Range: (from, from));
        }

        // \q{ ClassString ( | ClassString )* }
        private ClassItem ParseClassStringDisjunction()
        {
            Expect('{');
            var strings = new List<List<int>> { new() };
            while (Peek() != '}')
            {
                if (IsEof)
                    throw Error();

                if (Peek() == '|')
                {
                    _position++;
                    strings.Add([]);
                    continue;
                }

                strings[^1].Add(ParseClassSetCharacter());
            }

            _position++;
            return new ClassItem(Strings: strings);
        }

        // ClassSetCharacter
        private int ParseClassSetCharacter()
        {
            var c = Peek();
            if (c < 0)
                throw Error();

            if (c == '\\')
            {
                _position++;
                return ParseCharacterEscape(inClass: true);
            }

            // ClassSetSyntaxCharacter and ClassSetReservedDoublePunctuator
            if (c is '(' or ')' or '[' or ']' or '{' or '}' or '/' or '-' or '|')
                throw Error();

            if (Peek(1) == c && c is '&' or '!' or '#' or '$' or '%' or '*' or '+' or ',' or '.' or ':' or ';' or '<' or '=' or '>' or '?' or '@' or '^' or '`' or '~')
                throw Error();

            _position++;
            return c;
        }

        /// <summary>A code point that is not in a set of BMP code points (the content of a .NET character class): the supplementary code points match.</summary>
        private static string CodePointNotIn(string classContent) => "(?:[^" + classContent + "\\uD800-\\uDBFF]|" + HighSurrogateCodePoint + ")";

        private static (string Pattern, bool IsSingle) EmitCodePoint(int codePoint)
        {
            if (codePoint <= 0xFFFF)
                return (EscapeCodeUnit(codePoint), true);

            var text = char.ConvertFromUtf32(codePoint);
            return (EscapeCodeUnit(text[0]) + EscapeCodeUnit(text[1]), false);
        }

        private static string EscapeCodeUnit(int value) => char.IsAsciiLetterOrDigit((char)value) || value == '_' ? ((char)value).ToString() : "\\u" + value.ToString("X4", CultureInfo.InvariantCulture);

        /// <summary>Returns a .NET pattern that matches one element (a code point, or a string of \q) of the class.</summary>
        private static string EmitClass(ClassNode node)
        {
            if (node.Items.Count == 0)
                return node.Negated ? AnyCodePoint : "(?!)";

            // The simple case: BMP ranges and escapes that are a .NET character class
            if (node.Items.TrueForAll(i => i.NetClass is not null || (i.Range is { } r && r.To <= 0xFFFF)))
            {
                var content = new StringBuilder();
                foreach (var item in node.Items)
                {
                    AppendClassContent(content, item);
                }

                // A negated class matches the code points that are not in the class, including the supplementary code points
                return node.Negated ? CodePointNotIn(content.ToString()) : "[" + content + "]";
            }

            var alternatives = new List<string>();
            var simple = new StringBuilder();
            foreach (var item in node.Items)
            {
                if (item.NetClass is not null || (item.Range is { } r && r.To <= 0xFFFF))
                {
                    AppendClassContent(simple, item);
                }
                else
                {
                    alternatives.Add(EmitItem(item));
                }
            }

            if (simple.Length > 0)
            {
                alternatives.Add("[" + simple + "]");
            }

            var union = "(?:" + string.Join('|', alternatives) + ")";
            return node.Negated ? "(?:(?!" + union + ")" + AnyCodePoint + ")" : union;
        }

        private static void AppendClassContent(StringBuilder content, ClassItem item)
        {
            if (item.NetClass is not null)
            {
                content.Append(item.NetClass);
                return;
            }

            var (from, to) = item.Range!.Value;
            content.Append(EscapeCodeUnit(from));
            if (to != from)
            {
                content.Append('-').Append(EscapeCodeUnit(to));
            }
        }

        private static string EmitItem(ClassItem item)
        {
            if (item.Pattern is not null)
                return item.Pattern;

            if (item.NetClass is not null)
                return "[" + item.NetClass + "]";

            if (item.Nested is not null)
                return EmitClass(item.Nested);

            if (item.Range is { } range)
                return EmitRange(range.From, range.To);

            if (item.Strings is not null)
            {
                // The longest strings first (https://tc39.es/ecma262/#sec-compiletocharset)
                return "(?:" + string.Join('|', item.Strings.OrderByDescending(s => s.Count).Select(s => s.Count == 0 ? "" : string.Concat(s.Select(c => "(?:" + EmitCodePoint(c).Pattern + ")")))) + ")";
            }

            if (item.Intersection is not null)
            {
                var lookaheads = string.Concat(item.Intersection.Skip(1).Select(o => "(?=" + EmitItem(o) + ")"));
                return "(?:" + lookaheads + EmitItem(item.Intersection[0]) + ")";
            }

            var exclusions = string.Concat(item.Subtraction!.Skip(1).Select(o => "(?!" + EmitItem(o) + ")"));
            return "(?:" + exclusions + EmitItem(item.Subtraction![0]) + ")";
        }

        /// <summary>A range of code points, with the supplementary code points matched as surrogate pairs.</summary>
        private static string EmitRange(int from, int to)
        {
            var alternatives = new List<string>();
            if (from <= 0xFFFF)
            {
                var bmpEnd = Math.Min(to, 0xFFFF);
                alternatives.Add("[" + EscapeCodeUnit(from) + (bmpEnd != from ? "-" + EscapeCodeUnit(bmpEnd) : "") + "]");
                from = 0x10000;
            }

            if (to >= 0x10000)
            {
                var (fromHigh, fromLow) = Split(from);
                var (toHigh, toLow) = Split(to);
                if (fromHigh == toHigh)
                {
                    alternatives.Add(EscapeCodeUnit(fromHigh) + "[" + EscapeCodeUnit(fromLow) + "-" + EscapeCodeUnit(toLow) + "]");
                }
                else
                {
                    alternatives.Add(EscapeCodeUnit(fromHigh) + "[" + EscapeCodeUnit(fromLow) + "-\\uDFFF]");
                    if (toHigh - fromHigh > 1)
                    {
                        alternatives.Add("[" + EscapeCodeUnit(fromHigh + 1) + "-" + EscapeCodeUnit(toHigh - 1) + "][\\uDC00-\\uDFFF]");
                    }

                    alternatives.Add(EscapeCodeUnit(toHigh) + "[\\uDC00-" + EscapeCodeUnit(toLow) + "]");
                }
            }

            // The surrogate pairs first, so a supplementary code point is not matched as a lone surrogate
            alternatives.Reverse();
            return "(?:" + string.Join('|', alternatives) + ")";

            static (int High, int Low) Split(int codePoint)
            {
                var text = char.ConvertFromUtf32(codePoint);
                return (text[0], text[1]);
            }
        }

        private static string CodePointsToString(ReadOnlySpan<int> codePoints)
        {
            var builder = new StringBuilder(codePoints.Length);
            foreach (var codePoint in codePoints)
            {
                if (codePoint > 0xFFFF)
                {
                    builder.Append(char.ConvertFromUtf32(codePoint));
                }
                else
                {
                    builder.Append((char)codePoint);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>A character class: its items are matched as a union, and the class may be negated.</summary>
    private sealed record ClassNode(bool Negated, List<ClassItem> Items);

    /// <summary>An operand of a class: one of the members is set.</summary>
    private sealed record ClassItem(
        string? NetClass = null,
        string? Pattern = null,
        (int From, int To)? Range = null,
        ClassNode? Nested = null,
        List<List<int>>? Strings = null,
        List<ClassItem>? Intersection = null,
        List<ClassItem>? Subtraction = null);
}
