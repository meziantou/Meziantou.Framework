using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework.JavaScriptEngine.Parsing;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Parses a pattern (https://tc39.es/ecma262/#sec-patterns, with the web-compatibility grammar of Annex B when the
/// pattern has neither the "u" nor the "v" flag), reports its early errors, and builds the <see cref="RegExpNode"/> tree,
/// applying the compile-time semantics of the specification: CompileToCharSet, CharacterComplement,
/// MaybeSimpleCaseFolding, the property escapes...
/// </summary>
internal sealed class RegExpParser
{
    /// <summary>The message of the error reported for a pattern nested too deeply to be parsed on the stack.</summary>
    public const string TooLargeMessage = "Regular expression too large";

    private const int MaxNestingDepth = 256;

    // The punctuators a backslash may escape in a class of the "v" flag (ClassSetReservedPunctuator)
    private const string ClassSetReservedPunctuators = "&-!#%,:;<=>@`~";

    // The characters that start a ClassSetReservedDoublePunctuator when they are doubled
    private const string ClassSetDoublePunctuators = "&!#$%*+,.:;<=>?@^`~";

    private static CodePointSet? s_unicodeSetsIgnoreCaseAllCharacters;
    private static CodePointSet? s_whiteSpace;

    private readonly string _pattern;
    private readonly bool _unicode;
    private readonly bool _unicodeSets;
    private readonly Dictionary<string, List<int>> _namedGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(int Disjunction, int Alternative)[]>> _declaredNames = new(StringComparer.Ordinal);
    private readonly List<(int Disjunction, int Alternative)> _alternativePath = [];
    private int _position;
    private int _totalGroupCount;
    private bool _hasNamedGroups;
    private int _groupCount;
    private int _disjunctionCount;
    private int _depth;
    private bool _ignoreCase;
    private bool _multiline;
    private bool _dotAll;

    private RegExpParser(string pattern, RegExpFlags flags)
    {
        _pattern = pattern;
        _unicode = flags.HasEitherUnicodeFlag();
        _unicodeSets = (flags & RegExpFlags.UnicodeSets) != 0;
        _ignoreCase = (flags & RegExpFlags.IgnoreCase) != 0;
        _multiline = (flags & RegExpFlags.Multiline) != 0;
        _dotAll = (flags & RegExpFlags.DotAll) != 0;
    }

    /// <summary>Parses a pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="flags">The flags.</param>
    /// <param name="result">The pattern, its groups and its group names.</param>
    /// <param name="error">
    /// The message of the SyntaxError when the pattern is not valid; <see cref="TooLargeMessage"/> when it is nested too
    /// deeply to be parsed.
    /// </param>
    public static bool TryParse(string pattern, RegExpFlags flags, out RegExpParseResult? result, out string? error)
    {
        var parser = new RegExpParser(pattern, flags);
        try
        {
            result = parser.Parse();
            error = null;
            return true;
        }
        catch (RegExpSyntaxException exception)
        {
            result = null;
            error = exception.Message;
            return false;
        }
    }

    private RegExpParseResult Parse()
    {
        CountGroups();

        var root = ParseDisjunction();
        if (_position < _pattern.Length)
        {
            // The disjunction stops at a ")" that closes no group
            throw Error("Unmatched ')'");
        }

        if (_groupCount != _totalGroupCount)
            throw Error("Invalid capture group");

        var groupNames = new string?[_groupCount + 1];
        foreach (var (name, numbers) in _namedGroups)
        {
            foreach (var number in numbers)
            {
                groupNames[number] = name;
            }
        }

        return new RegExpParseResult(root, _groupCount, groupNames);
    }

    private static RegExpSyntaxException Error(string message) => new(message);

    private char Current => _position < _pattern.Length ? _pattern[_position] : '\0';

    private bool IsAtEnd => _position >= _pattern.Length;

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _pattern.Length ? _pattern[index] : '\0';
    }

    private bool HasAt(int offset, char value)
    {
        var index = _position + offset;
        return index < _pattern.Length && _pattern[index] == value;
    }

    /// <summary>
    /// Counts the capturing groups and reads the group names before the pattern is parsed: a decimal escape of the
    /// web-compatibility grammar is a backreference only when there are that many groups (CountLeftCapturingParensWithin),
    /// a named backreference may refer to a group defined after it, and a group name makes <c>\k</c> a named
    /// backreference (the NamedCaptureGroups parameter of Annex B). The count is exact for a valid pattern; for an invalid
    /// one, the parser reports the error.
    /// </summary>
    private void CountGroups()
    {
        var pattern = _pattern;
        var classDepth = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i++;
                continue;
            }

            if (classDepth > 0)
            {
                if (c == ']')
                {
                    classDepth--;
                }
                else if (c == '[' && _unicodeSets)
                {
                    classDepth++;
                }

                continue;
            }

            if (c == '[')
            {
                classDepth = 1;
            }
            else if (c == '(')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                {
                    if (i + 3 < pattern.Length && pattern[i + 2] == '<' && pattern[i + 3] is not ('=' or '!'))
                    {
                        _totalGroupCount++;
                        _hasNamedGroups = true;
                        var position = i + 3;
                        if (TryReadGroupName(ref position, out var name) && position < pattern.Length && pattern[position] == '>')
                        {
                            if (!_namedGroups.TryGetValue(name, out var numbers))
                            {
                                numbers = [];
                                _namedGroups.Add(name, numbers);
                            }

                            numbers.Add(_totalGroupCount);
                        }
                    }
                }
                else
                {
                    _totalGroupCount++;
                }
            }
        }
    }

    /// <summary>Disjunction :: Alternative ( | Alternative )*</summary>
    private RegExpNode ParseDisjunction()
    {
        var disjunction = ++_disjunctionCount;
        RegExpNode? first = null;
        List<RegExpNode>? alternatives = null;
        var index = 0;
        while (true)
        {
            _alternativePath.Add((disjunction, index));
            var alternative = ParseAlternative();
            _alternativePath.RemoveAt(_alternativePath.Count - 1);
            if (first is null)
            {
                first = alternative;
            }
            else
            {
                alternatives ??= [first];
                alternatives.Add(alternative);
            }

            if (Current != '|')
                break;

            _position++;
            index++;
        }

        return alternatives is null ? first : new RegExpAlternationNode([.. alternatives]);
    }

    /// <summary>Alternative :: Term*</summary>
    private RegExpNode ParseAlternative()
    {
        List<RegExpNode>? items = null;
        while (!IsAtEnd && Current is not ('|' or ')'))
        {
            var node = ParseTerm();
            if (node is RegExpSequenceNode nested)
            {
                items ??= [];
                items.AddRange(nested.Items);
            }
            else if (node is not RegExpEmptyNode)
            {
                items ??= [];
                items.Add(node);
            }
        }

        return items?.Count switch
        {
            null or 0 => RegExpEmptyNode.Instance,
            1 => items[0],
            _ => new RegExpSequenceNode([.. items]),
        };
    }

    private RegExpNode ParseTerm()
    {
        var c = Current;
        switch (c)
        {
            case '^':
                _position++;
                return new RegExpAssertionNode(RegExpAssertionKind.Start, _multiline, _ignoreCase);

            case '$':
                _position++;
                return new RegExpAssertionNode(RegExpAssertionKind.End, _multiline, _ignoreCase);

            case '\\' when Peek(1) is 'b':
                _position += 2;
                return new RegExpAssertionNode(RegExpAssertionKind.WordBoundary, _multiline, _ignoreCase);

            case '\\' when Peek(1) is 'B':
                _position += 2;
                return new RegExpAssertionNode(RegExpAssertionKind.NotWordBoundary, _multiline, _ignoreCase);

            case '(' when Peek(1) is '?' && (Peek(2) is '=' or '!' || (Peek(2) is '<' && Peek(3) is '=' or '!')):
                {
                    var behind = Peek(2) is '<';
                    var negative = (behind ? Peek(3) : Peek(2)) is '!';
                    var groupsBefore = _groupCount;
                    _position += behind ? 4 : 3;
                    var body = ParseGroupBody();
                    var lookaround = new RegExpLookaroundNode(behind, negative, body, groupsBefore + 1, _groupCount - groupsBefore);

                    // QuantifiableAssertion (Annex B): only a lookahead, and only without the "u" or "v" flag
                    return behind || _unicode ? lookaround : ParseQuantifier(lookaround, groupsBefore);
                }

            case '*' or '+' or '?':
                throw Error("Nothing to repeat");

            case '{':
                if (_unicode)
                    throw Error(TryReadBracedQuantifier(out _, out _, out _) ? "Nothing to repeat" : "Lone quantifier brackets");

                // InvalidBracedQuantifier: a "{" that would be a quantifier is not a literal
                if (TryReadBracedQuantifier(out _, out _, out _))
                    throw Error("Nothing to repeat");

                break;
        }

        var before = _groupCount;
        var atom = ParseAtom();
        return ParseQuantifier(atom, before);
    }

    private RegExpNode ParseQuantifier(RegExpNode atom, int groupsBefore)
    {
        int min;
        int max;
        switch (Current)
        {
            case '*':
                min = 0;
                max = RegExpQuantifierNode.Infinite;
                _position++;
                break;

            case '+':
                min = 1;
                max = RegExpQuantifierNode.Infinite;
                _position++;
                break;

            case '?':
                min = 0;
                max = 1;
                _position++;
                break;

            case '{':
                if (!TryReadBracedQuantifier(out min, out max, out var length))
                    return atom; // a literal "{" (Annex B), or the error of the next term

                _position += length;
                break;

            default:
                return atom;
        }

        var greedy = true;
        if (Current == '?')
        {
            greedy = false;
            _position++;
        }

        return new RegExpQuantifierNode(atom, min, max, greedy, groupsBefore + 1, _groupCount - groupsBefore);
    }

    /// <summary>
    /// Reads <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c> at the current position. The bounds are clamped to
    /// <see cref="int.MaxValue"/>; they are compared before, so <c>{2147483649,2147483648}</c> is still out of order.
    /// </summary>
    private bool TryReadBracedQuantifier(out int min, out int max, out int length)
    {
        min = 0;
        max = 0;
        length = 0;
        var pattern = _pattern;
        var i = _position + 1;
        var minStart = i;
        while (i < pattern.Length && char.IsAsciiDigit(pattern[i]))
        {
            i++;
        }

        var minDigits = pattern.AsSpan(minStart, i - minStart);
        if (minDigits.IsEmpty)
            return false;

        ReadOnlySpan<char> maxDigits;
        var unbounded = false;
        if (i < pattern.Length && pattern[i] == ',')
        {
            i++;
            var maxStart = i;
            while (i < pattern.Length && char.IsAsciiDigit(pattern[i]))
            {
                i++;
            }

            maxDigits = pattern.AsSpan(maxStart, i - maxStart);
            unbounded = maxDigits.IsEmpty;
        }
        else
        {
            maxDigits = minDigits;
        }

        if (i >= pattern.Length || pattern[i] != '}')
            return false;

        if (!unbounded && CompareDecimal(minDigits, maxDigits) > 0)
            throw Error("numbers out of order in {} quantifier");

        min = ParseClampedDecimal(minDigits);
        max = unbounded ? RegExpQuantifierNode.Infinite : ParseClampedDecimal(maxDigits);
        length = i + 1 - _position;
        return true;
    }

    private static int CompareDecimal(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        if (left.Length != right.Length)
            return left.Length.CompareTo(right.Length);

        return left.SequenceCompareTo(right);
    }

    private static int ParseClampedDecimal(ReadOnlySpan<char> digits)
    {
        long value = 0;
        foreach (var c in digits)
        {
            value = Math.Min((value * 10) + (c - '0'), int.MaxValue);
        }

        return (int)value;
    }

    /// <summary>Atom, and ExtendedAtom of Annex B</summary>
    private RegExpNode ParseAtom()
    {
        var c = Current;
        switch (c)
        {
            case '.':
                _position++;
                return new RegExpDotNode(_dotAll);

            case '(':
                return ParseGroup();

            case '[':
                return ParseCharacterClass();

            case '\\':
                return ParseAtomEscape();

            case ']' or '}' when _unicode:
                throw Error("Lone quantifier brackets");

            default:
                return new RegExpCharNode(ReadPatternCodePoint(), _ignoreCase);
        }
    }

    /// <summary>Reads a code point of the source: a surrogate pair is one code point with the "u" or "v" flag.</summary>
    private int ReadPatternCodePoint()
    {
        var c = _pattern[_position++];
        if (_unicode && char.IsHighSurrogate(c) && _position < _pattern.Length && char.IsLowSurrogate(_pattern[_position]))
            return char.ConvertToUtf32(c, _pattern[_position++]);

        return c;
    }

    private RegExpNode ParseGroup()
    {
        // The lookarounds are parsed by ParseTerm
        if (Peek(1) != '?')
        {
            _position++;
            var index = ++_groupCount;
            return new RegExpGroupNode(index, ParseGroupBody());
        }

        switch (Peek(2))
        {
            case ':':
                _position += 3;
                return ParseGroupBody();

            case '<':
                {
                    _position += 3;
                    if (!TryReadGroupName(ref _position, out var name) || Current != '>')
                        throw Error("Invalid capture group name");

                    _position++;
                    var index = ++_groupCount;
                    if (!_namedGroups.TryGetValue(name, out var numbers) || !numbers.Contains(index))
                        throw Error("Invalid capture group");

                    DeclareGroupName(name);
                    return new RegExpGroupNode(index, ParseGroupBody());
                }

            default:
                return ParseModifiersGroup();
        }
    }

    /// <summary>
    /// Two groups may have the same name only when they are in different alternatives of a disjunction, so at most one
    /// of them participates in a match (MightBothParticipate).
    /// </summary>
    private void DeclareGroupName(string name)
    {
        var path = _alternativePath.ToArray();
        if (!_declaredNames.TryGetValue(name, out var declared))
        {
            _declaredNames.Add(name, [path]);
            return;
        }

        foreach (var other in declared)
        {
            if (MightBothParticipate(other, path))
                throw Error("Duplicate capture group name");
        }

        declared.Add(path);
    }

    private static bool MightBothParticipate((int Disjunction, int Alternative)[] left, (int Disjunction, int Alternative)[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var i = 0; i < length; i++)
        {
            if (left[i] != right[i])
                return left[i].Disjunction != right[i].Disjunction;
        }

        return true;
    }

    /// <summary><c>(?ims-ims:Disjunction)</c></summary>
    private RegExpNode ParseModifiersGroup()
    {
        _position += 2;
        var (ignoreCase, multiline, dotAll) = (_ignoreCase, _multiline, _dotAll);
        var seen = 0;
        var removing = false;
        var count = 0;
        while (true)
        {
            var c = Current;
            if (c == '-' && !removing)
            {
                removing = true;
                _position++;
                continue;
            }

            var flag = c switch
            {
                'i' => 1,
                'm' => 2,
                's' => 4,
                _ => 0,
            };

            if (flag == 0)
                break;

            if ((seen & flag) != 0)
                throw Error("Repeated flag in modifiers group");

            seen |= flag;
            count++;
            var value = !removing;
            switch (c)
            {
                case 'i':
                    _ignoreCase = value;
                    break;
                case 'm':
                    _multiline = value;
                    break;
                default:
                    _dotAll = value;
                    break;
            }

            _position++;
        }

        if (Current != ':' || count == 0)
            throw Error("Invalid group");

        _position++;
        var body = ParseGroupBody();
        (_ignoreCase, _multiline, _dotAll) = (ignoreCase, multiline, dotAll);
        return body;
    }

    /// <summary>Parses the disjunction of a group and its closing parenthesis.</summary>
    private RegExpNode ParseGroupBody()
    {
        EnterNesting();
        var body = ParseDisjunction();
        if (Current != ')')
            throw Error("Unterminated group");

        _position++;
        _depth--;
        return body;
    }

    private void EnterNesting()
    {
        if (++_depth > MaxNestingDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw Error(TooLargeMessage);
    }

    /// <summary>
    /// Reads a RegExpIdentifierName, whose characters may be written as <c>\u</c> escapes (always with the escapes of the
    /// Unicode mode: <c>\u{...}</c> and a pair of <c>\uXXXX</c>).
    /// </summary>
    private bool TryReadGroupName(ref int position, out string name)
    {
        var builder = new StringBuilder();
        while (TryReadIdentifierCodePoint(position, out var codePoint, out var length))
        {
            var valid = builder.Length == 0 ? Lexer.IsIdentifierStartCodePoint(codePoint) : Lexer.IsIdentifierPartCodePoint(codePoint);
            if (!valid)
                break;

            AppendCodePoint(builder, codePoint);
            position += length;
        }

        name = builder.ToString();
        return name.Length > 0;
    }

    private bool TryReadIdentifierCodePoint(int position, out int codePoint, out int length)
    {
        var pattern = _pattern;
        codePoint = 0;
        length = 0;
        if (position >= pattern.Length)
            return false;

        var c = pattern[position];
        if (c != '\\')
        {
            if (char.IsHighSurrogate(c) && position + 1 < pattern.Length && char.IsLowSurrogate(pattern[position + 1]))
            {
                codePoint = char.ConvertToUtf32(c, pattern[position + 1]);
                length = 2;
            }
            else
            {
                codePoint = c;
                length = 1;
            }

            return true;
        }

        if (position + 1 >= pattern.Length || pattern[position + 1] != 'u')
            return false;

        if (!TryReadUnicodeEscapeBody(position + 2, unicode: true, out codePoint, out var escapeLength))
            return false;

        length = escapeLength + 2;
        return true;
    }

    /// <summary>
    /// Reads the part of a RegExpUnicodeEscapeSequence after <c>\u</c>: <c>XXXX</c>, and with the Unicode escapes
    /// <c>{X...}</c> and a pair of surrogates <c>XXXX\uXXXX</c>.
    /// </summary>
    private bool TryReadUnicodeEscapeBody(int position, bool unicode, out int codePoint, out int length)
    {
        var pattern = _pattern;
        codePoint = 0;
        length = 0;
        if (unicode && position < pattern.Length && pattern[position] == '{')
        {
            var i = position + 1;
            long value = 0;
            while (i < pattern.Length && char.IsAsciiHexDigit(pattern[i]))
            {
                value = Math.Min((value * 16) + HexValue(pattern[i]), CodePointSet.MaxCodePoint + 1L);
                i++;
            }

            if (i == position + 1 || i >= pattern.Length || pattern[i] != '}' || value > CodePointSet.MaxCodePoint)
                return false;

            codePoint = (int)value;
            length = i + 1 - position;
            return true;
        }

        if (!TryReadHex(position, 4, out var unit))
            return false;

        codePoint = unit;
        length = 4;
        if (unicode && char.IsHighSurrogate((char)unit) && position + 10 <= pattern.Length && pattern[position + 4] == '\\' && pattern[position + 5] == 'u' &&
            TryReadHex(position + 6, 4, out var low) && char.IsLowSurrogate((char)low))
        {
            codePoint = char.ConvertToUtf32((char)unit, (char)low);
            length = 10;
        }

        return true;
    }

    private bool TryReadHex(int position, int count, out int value)
    {
        value = 0;
        if (position + count > _pattern.Length)
            return false;

        for (var i = 0; i < count; i++)
        {
            var c = _pattern[position + i];
            if (!char.IsAsciiHexDigit(c))
                return false;

            value = (value * 16) + HexValue(c);
        }

        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    /// <summary><c>\</c> AtomEscape, and <c>\</c> [lookahead = c] of Annex B</summary>
    private RegExpNode ParseAtomEscape()
    {
        var c = Peek(1);
        if (_position + 1 >= _pattern.Length)
            throw Error("\\ at end of pattern");

        switch (c)
        {
            case >= '1' and <= '9':
                {
                    // DecimalEscape: a backreference. Without the "u" or "v" flag, only when there are that many groups;
                    // otherwise it is a legacy octal escape, or an identity escape of 8 and 9
                    var start = _position + 1;
                    var end = start;
                    while (end < _pattern.Length && char.IsAsciiDigit(_pattern[end]))
                    {
                        end++;
                    }

                    var number = ParseClampedDecimal(_pattern.AsSpan(start, end - start));
                    if (number <= _totalGroupCount)
                    {
                        _position = end;
                        return new RegExpBackreferenceNode([number], _ignoreCase);
                    }

                    if (_unicode)
                        throw Error("Invalid escape");

                    break;
                }

            case 'd' or 'D' or 's' or 'S' or 'w' or 'W':
                _position += 2;
                return new RegExpSetNode(GetClassEscapeSet(c, _ignoreCase), invert: false, _ignoreCase);

            case 'p' or 'P' when _unicode:
                {
                    _position += 2;
                    var (set, strings) = ParsePropertyEscape(negated: c == 'P', _ignoreCase, out _);
                    return CreateClassMatcher(set, strings, invert: false, _ignoreCase);
                }

            case 'k' when _unicode || _hasNamedGroups:
                {
                    _position += 2;
                    if (Current != '<')
                        throw Error("Invalid named reference");

                    _position++;
                    if (!TryReadGroupName(ref _position, out var name) || Current != '>')
                        throw Error("Invalid capture group name");

                    _position++;
                    if (!_namedGroups.TryGetValue(name, out var numbers))
                        throw Error("Invalid named capture referenced");

                    return new RegExpBackreferenceNode([.. numbers], _ignoreCase);
                }
        }

        var codePoint = ParseCharacterEscape(inClass: false);
        return new RegExpCharNode(codePoint, _ignoreCase);
    }

    /// <summary>
    /// CharacterEscape, after the backslash (the current position is the backslash). Returns the character; the Annex B
    /// <c>\c</c> not followed by a control letter is the backslash itself, and the <c>c</c> is read next.
    /// </summary>
    private int ParseCharacterEscape(bool inClass)
    {
        var c = Peek(1);
        if (_position + 1 >= _pattern.Length)
            throw Error("\\ at end of pattern");

        switch (c)
        {
            case 'f':
                _position += 2;
                return '\f';
            case 'n':
                _position += 2;
                return '\n';
            case 'r':
                _position += 2;
                return '\r';
            case 't':
                _position += 2;
                return '\t';
            case 'v':
                _position += 2;
                return '\v';

            case 'c':
                {
                    var letter = Peek(2);
                    if (char.IsAsciiLetter(letter) && _position + 2 < _pattern.Length)
                    {
                        _position += 3;
                        return letter % 32;
                    }

                    if (_unicode)
                        throw Error("Invalid unicode escape");

                    // ClassControlLetter (Annex B): a digit or "_" in a class
                    if (inClass && (char.IsAsciiDigit(letter) || letter == '_') && _position + 2 < _pattern.Length)
                    {
                        _position += 3;
                        return letter % 32;
                    }

                    _position++;
                    return '\\';
                }

            case 'x':
                if (TryReadHex(_position + 2, 2, out var hex))
                {
                    _position += 4;
                    return hex;
                }

                if (_unicode)
                    throw Error("Invalid escape");

                _position += 2;
                return 'x';

            case 'u':
                if (TryReadUnicodeEscapeBody(_position + 2, _unicode, out var codePoint, out var length))
                {
                    _position += 2 + length;
                    return codePoint;
                }

                if (_unicode)
                    throw Error("Invalid Unicode escape");

                _position += 2;
                return 'u';

            case '0' when !char.IsAsciiDigit(Peek(2)) || _position + 2 >= _pattern.Length:
                _position += 2;
                return 0;

            case >= '0' and <= '9':
                if (_unicode)
                    throw Error(inClass ? "Invalid class escape" : "Invalid decimal escape");

                if (c >= '8')
                {
                    _position += 2;
                    return c;
                }

                return ParseLegacyOctalEscape();

            default:
                if (_unicode)
                {
                    if (IsSyntaxCharacter(c) || c == '/' || (inClass && c == '-') || (inClass && _unicodeSets && ClassSetReservedPunctuators.Contains(c, StringComparison.Ordinal)))
                    {
                        _position += 2;
                        return c;
                    }

                    throw Error("Invalid escape");
                }

                // IdentityEscape of Annex B: any character but "c", and but "k" when the pattern has a group name
                if (c == 'k' && _hasNamedGroups)
                    throw Error("Invalid escape");

                _position += 2;
                return c;
        }
    }

    /// <summary>LegacyOctalEscapeSequence: up to three octal digits starting with 0 to 3, up to two otherwise.</summary>
    private int ParseLegacyOctalEscape()
    {
        _position++;
        var first = _pattern[_position];
        var maxDigits = first <= '3' ? 3 : 2;
        var value = 0;
        var count = 0;
        while (count < maxDigits && _position < _pattern.Length && _pattern[_position] is >= '0' and <= '7')
        {
            value = (value * 8) + (_pattern[_position] - '0');
            _position++;
            count++;
        }

        return value;
    }

    private static bool IsSyntaxCharacter(char c) => c is '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|';

    /// <summary>
    /// Reads <c>{Name}</c> or <c>{Name=Value}</c> after <c>\p</c> or <c>\P</c>, and returns its characters (the
    /// complement for <c>\P</c>) and, under the "v" flag, the strings of a property of strings.
    /// </summary>
    private (CodePointSet Set, IReadOnlyCollection<string> Strings) ParsePropertyEscape(bool negated, bool ignoreCase, out bool mayContainStrings)
    {
        if (Current != '{')
            throw Error("Invalid property name");

        var start = _position + 1;
        var end = start;
        var equals = -1;
        while (end < _pattern.Length && _pattern[end] != '}')
        {
            var c = _pattern[end];
            if (c == '=' && equals < 0)
            {
                equals = end;
            }
            else if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw Error("Invalid property name");
            }

            end++;
        }

        if (end >= _pattern.Length || end == start || equals == start || equals == end - 1)
            throw Error("Invalid property name");

        var text = _pattern[start..end];
        _position = end + 1;

        var kind = RegExpUnicodeProperties.Resolve(text, _unicodeSets, out var set, out var strings);
        if (kind == RegExpUnicodeProperties.PropertyKind.Unknown)
            throw Error("Invalid property name");

        mayContainStrings = kind == RegExpUnicodeProperties.PropertyKind.Strings;
        if (negated)
        {
            if (mayContainStrings)
                throw Error("Invalid property name");

            // CharacterComplement(rer, CompileToCharSet of UnicodePropertyValueExpression)
            var expression = kind == RegExpUnicodeProperties.PropertyKind.GeneralCategory ? set : MaybeSimpleCaseFolding(set, ignoreCase);
            return (CharacterComplement(expression, ignoreCase), []);
        }

        if (kind == RegExpUnicodeProperties.PropertyKind.GeneralCategory)
            return (set, []);

        if (strings.Length == 0)
            return (MaybeSimpleCaseFolding(set, ignoreCase), []);

        var foldedStrings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in strings)
        {
            foldedStrings.Add(MaybeSimpleCaseFolding(value, ignoreCase));
        }

        return (MaybeSimpleCaseFolding(set, ignoreCase), foldedStrings);
    }

    /// <summary>Enumerates the characters of a value: the code points under "u" or "v", else the code units.</summary>
    private IEnumerable<int> EnumerateCharacters(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (_unicode && char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                yield return char.ConvertToUtf32(value[i], value[i + 1]);
                i++;
            }
            else
            {
                yield return value[i];
            }
        }
    }

    /// <summary>AllCharacters(rer)</summary>
    private CodePointSet AllCharacters(bool ignoreCase)
    {
        if (_unicodeSets && ignoreCase)
            return s_unicodeSetsIgnoreCaseAllCharacters ?? CreateUnicodeSetsIgnoreCaseAllCharacters();

        return CodePointSet.Range(0, _unicode ? CodePointSet.MaxCodePoint : 0xFFFF);

        static CodePointSet CreateUnicodeSetsIgnoreCaseAllCharacters()
        {
            var folded = RegExpCaseFolding.GetSimpleCaseFoldings().CodePoints;
            var set = new CodePointSet();
            foreach (var codePoint in folded)
            {
                set.Add(codePoint);
            }

            var result = set.Complement(CodePointSet.MaxCodePoint);
            _ = result.RangeCount;
            Volatile.Write(ref s_unicodeSetsIgnoreCaseAllCharacters, result);
            return result;
        }
    }

    /// <summary>CharacterComplement(rer, set)</summary>
    private CodePointSet CharacterComplement(CodePointSet set, bool ignoreCase) => CodePointSet.Subtract(AllCharacters(ignoreCase), set);

    /// <summary>MaybeSimpleCaseFolding(rer, set) for the single characters.</summary>
    private CodePointSet MaybeSimpleCaseFolding(CodePointSet set, bool ignoreCase)
    {
        if (!_unicodeSets || !ignoreCase)
            return set;

        // scf only changes the code points that have a mapping; the others stay
        var (codePoints, folded) = RegExpCaseFolding.GetSimpleCaseFoldings();
        var result = set.Clone();
        var changed = new CodePointSet();
        var removed = new CodePointSet();
        for (var i = 0; i < codePoints.Length; i++)
        {
            if (set.Contains(codePoints[i]))
            {
                removed.Add(codePoints[i]);
                changed.Add(folded[i]);
            }
        }

        if (removed.IsEmpty)
            return result;

        result = CodePointSet.Subtract(result, removed);
        result.AddSet(changed);
        return result;
    }

    private string MaybeSimpleCaseFolding(string value, bool ignoreCase)
    {
        if (!_unicodeSets || !ignoreCase)
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var codePoint in EnumerateCharacters(value))
        {
            AppendCodePoint(builder, RegExpCaseFolding.CanonicalizeUnicode(codePoint));
        }

        return builder.ToString();
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
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

    /// <summary>WordCharacters(rer): the ASCII word characters, and the characters whose canonical value is one of them.</summary>
    private CodePointSet WordCharacters(bool ignoreCase)
    {
        var set = CreateBasicWordCharacters();
        if (_unicode && ignoreCase)
        {
            var (codePoints, folded) = RegExpCaseFolding.GetSimpleCaseFoldings();
            for (var i = 0; i < codePoints.Length; i++)
            {
                if (IsBasicWordCharacter(folded[i]) && !IsBasicWordCharacter(codePoints[i]))
                {
                    set.Add(codePoints[i]);
                }
            }
        }

        return set;
    }

    private static CodePointSet CreateBasicWordCharacters()
    {
        var set = new CodePointSet();
        set.AddRange('0', '9');
        set.AddRange('A', 'Z');
        set.Add('_');
        set.AddRange('a', 'z');
        return set;
    }

    public static bool IsBasicWordCharacter(int c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    /// <summary>The characters of WhiteSpace and LineTerminator (a shared set: it must not be modified).</summary>
    public static CodePointSet CreateWhiteSpaceSet() => Volatile.Read(ref s_whiteSpace) ?? CreateWhiteSpaceSetCore();

    private static CodePointSet CreateWhiteSpaceSetCore()
    {
        var set = new CodePointSet();
        set.AddRange(0x09, 0x0D); // TAB, LF, VT, FF, CR
        set.Add(0x20);
        set.Add(0xA0);
        set.Add(0x1680);
        set.AddRange(0x2000, 0x200A);
        set.AddRange(0x2028, 0x2029);
        set.Add(0x202F);
        set.Add(0x205F);
        set.Add(0x3000);
        set.Add(0xFEFF);

        // The other characters of the Zs category, if a later version of Unicode adds some
        for (var c = 0; c <= 0xFFFF; c++)
        {
            if (UnicodeProperties.GetGeneralCategory(c) == UnicodeCategory.SpaceSeparator)
            {
                set.Add(c);
            }
        }

        _ = set.RangeCount;
        Volatile.Write(ref s_whiteSpace, set);
        return set;
    }

    /// <summary>CompileToCharSet of CharacterClassEscape :: d, D, s, S, w, W</summary>
    private CodePointSet GetClassEscapeSet(char letter, bool ignoreCase)
    {
        return letter switch
        {
            'd' => CodePointSet.Range('0', '9'),
            'D' => CharacterComplement(CodePointSet.Range('0', '9'), ignoreCase),
            's' => CreateWhiteSpaceSet(),
            'S' => CharacterComplement(CreateWhiteSpaceSet(), ignoreCase),
            'w' => MaybeSimpleCaseFolding(WordCharacters(ignoreCase), ignoreCase),
            _ => CharacterComplement(MaybeSimpleCaseFolding(WordCharacters(ignoreCase), ignoreCase), ignoreCase),
        };
    }

    private RegExpNode ParseCharacterClass()
    {
        _position++;
        var negated = Current == '^';
        if (negated)
        {
            _position++;
        }

        var ignoreCase = _ignoreCase;
        if (_unicodeSets)
        {
            EnterNesting();
            var value = ParseClassSetContents(ignoreCase, out var mayContainStrings);
            _depth--;
            if (negated)
            {
                if (mayContainStrings)
                    throw Error("Negated character class may contain strings");

                return new RegExpSetNode(CharacterComplement(value.Singles, ignoreCase), invert: false, ignoreCase);
            }

            return CreateClassMatcher(value.Singles, value.Strings, invert: false, ignoreCase);
        }

        var set = new CodePointSet();
        while (true)
        {
            if (IsAtEnd)
                throw Error("Unterminated character class");

            if (Current == ']')
            {
                _position++;
                break;
            }

            var start = ParseClassAtom(ignoreCase, out var startSet);
            if (Current == '-' && _position + 1 < _pattern.Length && Peek(1) != ']')
            {
                _position++;
                var end = ParseClassAtom(ignoreCase, out var endSet);
                if (startSet is not null || endSet is not null)
                {
                    // A class escape cannot be the end of a range; Annex B makes the range the union of its two ends and "-"
                    if (_unicode)
                        throw Error("Invalid character class");

                    AddClassAtom(set, start, startSet);
                    set.Add('-');
                    AddClassAtom(set, end, endSet);
                    continue;
                }

                if (start > end)
                    throw Error("Range out of order in character class");

                set.AddRange(start, end);
                continue;
            }

            AddClassAtom(set, start, startSet);
        }

        return new RegExpSetNode(set, negated, ignoreCase);
    }

    private static void AddClassAtom(CodePointSet set, int value, CodePointSet? values)
    {
        if (values is null)
        {
            set.Add(value);
        }
        else
        {
            set.AddSet(values);
        }
    }

    /// <summary>ClassAtom: a character, or the set of a class escape (<paramref name="set"/>).</summary>
    private int ParseClassAtom(bool ignoreCase, out CodePointSet? set)
    {
        set = null;
        if (Current != '\\')
            return ReadPatternCodePoint();

        var c = Peek(1);
        switch (c)
        {
            case 'b':
                _position += 2;
                return '\b';

            case '-' when _unicode:
                _position += 2;
                return '-';

            case 'd' or 'D' or 's' or 'S' or 'w' or 'W':
                _position += 2;
                set = GetClassEscapeSet(c, ignoreCase);
                return 0;

            case 'p' or 'P' when _unicode:
                _position += 2;
                set = ParsePropertyEscape(negated: c == 'P', ignoreCase, out _).Set;
                return 0;

            default:
                return ParseCharacterEscape(inClass: true);
        }
    }

    /// <summary>
    /// ClassSetExpression (the contents of a class under the "v" flag, after "[" or "[^") and its closing bracket:
    /// a union of ranges and operands, or an intersection or a subtraction of operands.
    /// </summary>
    private ClassSetValue ParseClassSetContents(bool ignoreCase, out bool mayContainStrings)
    {
        if (Current == ']')
        {
            _position++;
            mayContainStrings = false;
            return new ClassSetValue();
        }

        var result = ParseClassSetOperand(ignoreCase, allowRange: true, out mayContainStrings, out var isRange);
        if (!isRange && IsAt("&&"))
        {
            // ClassIntersection: may contain strings when all its operands may
            while (IsAt("&&"))
            {
                _position += 2;
                if (Current == '&')
                    throw Error("Invalid set operation in character class");

                var operand = ParseClassSetOperand(ignoreCase, allowRange: false, out var operandMayContainStrings, out _);
                result = ClassSetValue.Intersect(result, operand);
                mayContainStrings &= operandMayContainStrings;
            }
        }
        else if (!isRange && IsAt("--"))
        {
            // ClassSubtraction: may contain strings when its first operand may
            while (IsAt("--"))
            {
                _position += 2;
                var operand = ParseClassSetOperand(ignoreCase, allowRange: false, out _, out _);
                result = ClassSetValue.Subtract(result, operand);
            }
        }
        else
        {
            // ClassUnion
            while (!IsAtEnd && Current != ']')
            {
                if (IsAt("&&") || IsAt("--"))
                    throw Error("Invalid set operation in character class");

                var operand = ParseClassSetOperand(ignoreCase, allowRange: true, out var operandMayContainStrings, out _);
                result.UnionWith(operand);
                mayContainStrings |= operandMayContainStrings;
            }
        }

        if (IsAtEnd)
            throw Error("Unterminated character class");

        if (Current != ']')
            throw Error("Invalid set operation in character class");

        _position++;
        return result;
    }

    private bool IsAt(string value) => _pattern.AsSpan(_position).StartsWith(value, StringComparison.Ordinal);

    /// <summary>ClassSetOperand (NestedClass, ClassStringDisjunction, ClassSetCharacter), or ClassSetRange when allowed.</summary>
    private ClassSetValue ParseClassSetOperand(bool ignoreCase, bool allowRange, out bool mayContainStrings, out bool isRange)
    {
        mayContainStrings = false;
        isRange = false;
        if (IsAtEnd)
            throw Error("Unterminated character class");

        var c = Current;
        if (c == '[')
        {
            _position++;
            var negated = Current == '^';
            if (negated)
            {
                _position++;
            }

            EnterNesting();
            var value = ParseClassSetContents(ignoreCase, out var nestedMayContainStrings);
            _depth--;
            if (!negated)
            {
                mayContainStrings = nestedMayContainStrings;
                return value;
            }

            if (nestedMayContainStrings)
                throw Error("Negated character class may contain strings");

            return new ClassSetValue(CharacterComplement(value.Singles, ignoreCase));
        }

        if (c == '\\')
        {
            var letter = Peek(1);
            switch (letter)
            {
                case 'q' when Peek(2) == '{':
                    _position += 3;
                    return ParseClassStringDisjunction(ignoreCase, out mayContainStrings);

                case 'd' or 'D' or 's' or 'S' or 'w' or 'W':
                    _position += 2;
                    return new ClassSetValue(GetClassEscapeSet(letter, ignoreCase));

                case 'p' or 'P':
                    {
                        _position += 2;
                        var (set, strings) = ParsePropertyEscape(negated: letter == 'P', ignoreCase, out mayContainStrings);
                        var result = new ClassSetValue(set);
                        foreach (var value in strings)
                        {
                            result.Add(value);
                        }

                        return result;
                    }
            }
        }

        var start = ParseClassSetCharacter();
        if (Current == '-' && Peek(1) != '-')
        {
            if (!allowRange)
                throw Error("Invalid set operation in character class");

            _position++;
            if (IsAtEnd)
                throw Error("Unterminated character class");

            if (Current == '\\' && Peek(1) is 'q' or 'd' or 'D' or 's' or 'S' or 'w' or 'W' or 'p' or 'P')
                throw Error("Invalid character class");

            var end = ParseClassSetCharacter();
            if (start > end)
                throw Error("Range out of order in character class");

            isRange = true;
            return new ClassSetValue(MaybeSimpleCaseFolding(CodePointSet.Range(start, end), ignoreCase));
        }

        return new ClassSetValue(MaybeSimpleCaseFolding(CodePointSet.Single(start), ignoreCase));
    }

    /// <summary>ClassSetCharacter</summary>
    private int ParseClassSetCharacter()
    {
        if (IsAtEnd)
            throw Error("Unterminated character class");

        var c = Current;
        if (c == '\\')
        {
            if (Peek(1) == 'b' && _position + 1 < _pattern.Length)
            {
                _position += 2;
                return '\b';
            }

            return ParseCharacterEscape(inClass: true);
        }

        if (c is '(' or ')' or '[' or ']' or '{' or '}' or '/' or '-' or '|')
            throw Error("Invalid character in character class");

        if (ClassSetDoublePunctuators.Contains(c, StringComparison.Ordinal) && HasAt(1, c))
            throw Error("Invalid set operation in character class");

        return ReadPatternCodePoint();
    }

    /// <summary><c>\q{ClassString|...}</c>, after <c>\q{</c>.</summary>
    private ClassSetValue ParseClassStringDisjunction(bool ignoreCase, out bool mayContainStrings)
    {
        mayContainStrings = false;
        var result = new ClassSetValue();
        var builder = new StringBuilder();
        var length = 0;
        while (true)
        {
            if (IsAtEnd)
                throw Error("Unterminated character class");

            var c = Current;
            if (c is '|' or '}')
            {
                // A string that is not one character long
                mayContainStrings |= length != 1;
                result.Add(MaybeSimpleCaseFolding(builder.ToString(), ignoreCase));
                builder.Clear();
                length = 0;
                _position++;
                if (c == '}')
                    return result;

                continue;
            }

            AppendCodePoint(builder, ParseClassSetCharacter());
            length++;
        }
    }

    /// <summary>Atom :: CharacterClass under the "v" flag: the strings, longest first, then the single characters, then the empty string.</summary>
    private static RegExpNode CreateClassMatcher(CodePointSet singles, IReadOnlyCollection<string> strings, bool invert, bool ignoreCase)
    {
        if (strings.Count == 0)
            return new RegExpSetNode(singles, invert, ignoreCase);

        var sequences = new List<int[]>();
        var hasEmpty = false;
        foreach (var value in strings)
        {
            if (value.Length == 0)
            {
                hasEmpty = true;
                continue;
            }

            var codePoints = new List<int>();
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    codePoints.Add(char.ConvertToUtf32(value[i], value[i + 1]));
                    i++;
                }
                else
                {
                    codePoints.Add(value[i]);
                }
            }

            sequences.Add([.. codePoints]);
        }

        // Longest first; the order of the strings of the same length does not change the result
        sequences.Sort((a, b) => b.Length.CompareTo(a.Length));
        var alternatives = new List<RegExpNode>(sequences.Count + 2);
        foreach (var sequence in sequences)
        {
            var items = new RegExpNode[sequence.Length];
            for (var i = 0; i < sequence.Length; i++)
            {
                items[i] = new RegExpCharNode(sequence[i], ignoreCase);
            }

            alternatives.Add(items.Length == 1 ? items[0] : new RegExpSequenceNode(items));
        }

        alternatives.Add(new RegExpSetNode(singles, invert: false, ignoreCase));
        if (hasEmpty)
        {
            alternatives.Add(RegExpEmptyNode.Instance);
        }

        return new RegExpAlternationNode([.. alternatives]);
    }

    /// <summary>A CharSet under the "v" flag: single characters, and the strings that are not one character long.</summary>
    private sealed class ClassSetValue
    {
        public ClassSetValue()
            : this(new CodePointSet())
        {
        }

        public ClassSetValue(CodePointSet singles)
        {
            Singles = singles;
        }

        public CodePointSet Singles { get; private set; }

        public HashSet<string> Strings { get; } = new(StringComparer.Ordinal);

        public void Add(string value)
        {
            if (value.Length == 1)
            {
                AddSingle(value[0]);
            }
            else if (value.Length == 2 && char.IsSurrogatePair(value[0], value[1]))
            {
                AddSingle(char.ConvertToUtf32(value[0], value[1]));
            }
            else
            {
                Strings.Add(value);
            }
        }

        private void AddSingle(int value)
        {
            Singles = Singles.Clone();
            Singles.Add(value);
        }

        public void UnionWith(ClassSetValue other)
        {
            Singles = CodePointSet.Union(Singles, other.Singles);
            Strings.UnionWith(other.Strings);
        }

        public static ClassSetValue Intersect(ClassSetValue left, ClassSetValue right)
        {
            var result = new ClassSetValue(CodePointSet.Intersect(left.Singles, right.Singles));
            foreach (var value in left.Strings)
            {
                if (right.Strings.Contains(value))
                {
                    result.Strings.Add(value);
                }
            }

            return result;
        }

        public static ClassSetValue Subtract(ClassSetValue left, ClassSetValue right)
        {
            var result = new ClassSetValue(CodePointSet.Subtract(left.Singles, right.Singles));
            foreach (var value in left.Strings)
            {
                if (!right.Strings.Contains(value))
                {
                    result.Strings.Add(value);
                }
            }

            return result;
        }
    }

    /// <summary>Stops the parsing at the first early error of the pattern.</summary>
#pragma warning disable CA1064 // It never leaves the parser: TryParse returns its message
    private sealed class RegExpSyntaxException : Exception
#pragma warning restore CA1064
    {
        public RegExpSyntaxException()
        {
        }

        public RegExpSyntaxException(string message)
            : base(message)
        {
        }

        public RegExpSyntaxException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

/// <summary>A parsed pattern: the root node, the number of capturing groups and the name of each group (index 0 unused).</summary>
internal sealed record RegExpParseResult(RegExpNode Root, int GroupCount, string?[] GroupNames);
