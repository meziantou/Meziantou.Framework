using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

internal enum CollationStrength
{
    Primary = 1,
    Secondary = 2,
    Tertiary = 3,
    Quaternary = 4,
    Identical = 5,
}

/// <summary>A relation of a tailoring rule: <c>&lt;</c> (primary), <c>&lt;&lt;</c>, <c>&lt;&lt;&lt;</c>, <c>&lt;&lt;&lt;&lt;</c> or <c>=</c> (identical), with an optional prefix (<c>prefix|text</c>) and extension (<c>text/extension</c>).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CollationRelation(CollationStrength Strength, string Text, string Prefix = "", string Extension = "");

/// <summary>
/// A chain of rules: a reset (<c>&amp;text</c>, or a special position such as "last regular" for
/// <c>&amp;[last regular]</c>), with an optional <c>[before n]</c>, followed by relations.
/// </summary>
internal sealed record CollationRuleChain(string Reset, string? ResetPosition, CollationStrength? Before, IReadOnlyList<CollationRelation> Relations);

/// <summary>
/// A parsed CLDR tailoring (UTS 35 part 5 section 3): the settings and the rules. The imports of the CLDR data are
/// already inlined by the generator. The settings are null when the rules do not set them.
/// </summary>
internal sealed class CollationTailoring
{
    public CollationStrength? Strength { get; init; }

    /// <summary>Gets the alternate setting: "shifted" or "non-ignorable".</summary>
    public string? Alternate { get; init; }

    /// <summary>Gets whether the secondary weights are compared backwards (<c>[backwards 2]</c>, French).</summary>
    public bool? Backwards { get; init; }

    public bool? Normalization { get; init; }

    public bool? CaseLevel { get; init; }

    /// <summary>Gets the case first setting: "upper", "lower" or "off".</summary>
    public string? CaseFirst { get; init; }

    public bool? HiraganaQuaternary { get; init; }

    public bool? NumericOrdering { get; init; }

    /// <summary>Gets the maxVariable setting: "space", "punct", "symbol" or "currency".</summary>
    public string? MaxVariable { get; init; }

    /// <summary>Gets the script reordering (<c>[reorder Grek Latn]</c>): script codes and the special groups space, punct, symbol, currency, digit, others.</summary>
    public IReadOnlyList<string> Reorder { get; init; } = [];

    /// <summary>Gets the UnicodeSets of <c>[suppressContractions [...]]</c>, as written in the rules.</summary>
    public IReadOnlyList<string> SuppressContractions { get; init; } = [];

    /// <summary>Gets the UnicodeSets of <c>[optimize [...]]</c>, as written in the rules.</summary>
    public IReadOnlyList<string> Optimize { get; init; } = [];

    public IReadOnlyList<CollationRuleChain> Chains { get; init; } = [];

    /// <summary>Parses tailoring rules. Throws a <see cref="FormatException"/> for invalid rules.</summary>
    public static CollationTailoring Parse(string rules) => new CollationRuleParser(rules).Parse();
}

/// <summary>The parser of the syntax of the CLDR collation rules (UTS 35 part 5 section 3, as implemented by ICU).</summary>
internal sealed class CollationRuleParser(string rules)
{
    private const int RangeMarker = 0x10FFFF + 1;

    private readonly string _rules = rules;
    private int _position;

    public CollationTailoring Parse()
    {
        CollationStrength? strength = null;
        string? alternate = null, caseFirst = null, maxVariable = null;
        bool? backwards = null, normalization = null, caseLevel = null, hiraganaQ = null, numeric = null;
        var reorder = new List<string>();
        var suppressContractions = new List<string>();
        var optimize = new List<string>();
        var chains = new List<CollationRuleChain>();
        List<CollationRelation>? relations = null;

        while (true)
        {
            SkipWhitespaceAndComments();
            if (_position >= _rules.Length)
                break;

            var c = _rules[_position];
            if (c == '&')
            {
                _position++;
                SkipWhitespaceAndComments();
                CollationStrength? before = null;
                string? resetPosition = null;
                if (TryReadOption(out var option))
                {
                    if (option.StartsWith("before ", StringComparison.Ordinal))
                    {
                        before = option[7..].Trim() switch
                        {
                            "1" => CollationStrength.Primary,
                            "2" => CollationStrength.Secondary,
                            "3" => CollationStrength.Tertiary,
                            _ => throw Error("Invalid [before]"),
                        };
                        SkipWhitespaceAndComments();
                        if (TryReadOption(out var position))
                        {
                            resetPosition = ValidatePosition(position);
                        }
                    }
                    else
                    {
                        resetPosition = ValidatePosition(option);
                    }
                }

                var reset = resetPosition is null ? ReadText() : "";
                relations = [];
                chains.Add(new CollationRuleChain(reset, resetPosition, before, relations));
                continue;
            }

            if (c is '<' or '=')
            {
                if (relations is null)
                    throw Error("A relation must follow a reset");

                var relationStrength = ReadRelationOperator(out var isStar);
                SkipWhitespaceAndComments();
                if (isStar)
                {
                    foreach (var item in ReadStarText())
                    {
                        relations.Add(new CollationRelation(relationStrength, item));
                    }

                    continue;
                }

                var text = ReadText();
                var prefix = "";
                var extension = "";
                SkipWhitespaceAndComments();
                if (_position < _rules.Length && _rules[_position] == '|')
                {
                    _position++;
                    SkipWhitespaceAndComments();
                    prefix = text;
                    text = ReadText();
                    SkipWhitespaceAndComments();
                }

                if (_position < _rules.Length && _rules[_position] == '/')
                {
                    _position++;
                    SkipWhitespaceAndComments();
                    extension = ReadText();
                }

                relations.Add(new CollationRelation(relationStrength, text, prefix, extension));
                continue;
            }

            if (c == '[')
            {
                if (!TryReadOption(out var option))
                    throw Error("Invalid option");

                var separator = option.IndexOf(' ', StringComparison.Ordinal);
                var name = separator < 0 ? option : option[..separator];
                var value = separator < 0 ? "" : option[(separator + 1)..].Trim();
                switch (name)
                {
                    case "strength":
                        strength = value switch
                        {
                            "1" => CollationStrength.Primary,
                            "2" => CollationStrength.Secondary,
                            "3" => CollationStrength.Tertiary,
                            "4" => CollationStrength.Quaternary,
                            "I" => CollationStrength.Identical,
                            _ => throw Error("Invalid strength"),
                        };
                        break;
                    case "alternate" when value is "shifted" or "non-ignorable":
                        alternate = value;
                        break;
                    case "backwards" when value is "2":
                        backwards = true;
                        break;
                    case "normalization":
                        normalization = ParseOnOff(value);
                        break;
                    case "caseLevel":
                        caseLevel = ParseOnOff(value);
                        break;
                    case "caseFirst" when value is "upper" or "lower" or "off":
                        caseFirst = value;
                        break;
                    case "hiraganaQ":
                        hiraganaQ = ParseOnOff(value);
                        break;
                    case "numericOrdering":
                        numeric = ParseOnOff(value);
                        break;
                    case "maxVariable" when value is "space" or "punct" or "symbol" or "currency":
                        maxVariable = value;
                        break;
                    case "reorder":
                        reorder.AddRange(value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                        break;
                    case "suppressContractions":
                        suppressContractions.Add(value);
                        break;
                    case "optimize":
                        optimize.Add(value);
                        break;
                    case "import":
                        throw Error("The imports must be resolved before parsing: " + value);
                    default:
                        throw Error("Unknown option: " + option);
                }

                continue;
            }

            throw Error("Unexpected character '" + c + "'");
        }

        return new CollationTailoring
        {
            Strength = strength,
            Alternate = alternate,
            Backwards = backwards,
            Normalization = normalization,
            CaseLevel = caseLevel,
            CaseFirst = caseFirst,
            HiraganaQuaternary = hiraganaQ,
            NumericOrdering = numeric,
            MaxVariable = maxVariable,
            Reorder = reorder,
            SuppressContractions = suppressContractions,
            Optimize = optimize,
            Chains = chains,
        };

        bool ParseOnOff(string value) => value switch
        {
            "on" => true,
            "off" => false,
            _ => throw Error("Invalid on/off value: " + value),
        };
    }

    private static string ValidatePosition(string position)
    {
        return position switch
        {
            "first tertiary ignorable" or "last tertiary ignorable" or "first secondary ignorable" or "last secondary ignorable" or
            "first primary ignorable" or "last primary ignorable" or "first variable" or "last variable" or "first regular" or
            "last regular" or "first implicit" or "first trailing" or "last trailing" => position,
            _ => throw new FormatException("Invalid reset position: [" + position + "]"),
        };
    }

    private CollationStrength ReadRelationOperator(out bool isStar)
    {
        CollationStrength strength;
        if (_rules[_position] == '=')
        {
            _position++;
            strength = CollationStrength.Identical;
        }
        else
        {
            var count = 0;
            while (_position < _rules.Length && _rules[_position] == '<' && count < 4)
            {
                _position++;
                count++;
            }

            strength = (CollationStrength)count;
        }

        isStar = _position < _rules.Length && _rules[_position] == '*';
        if (isStar)
        {
            _position++;
        }

        return strength;
    }

    // The text of a star relation: each code point is a relation; "a-d" (an unquoted '-') is a range
    private List<string> ReadStarText()
    {
        var codePoints = new List<int>();
        ReadText(codePoints, allowRangeDash: true);
        var result = new List<string>();
        for (var i = 0; i < codePoints.Count; i++)
        {
            if (codePoints[i] == RangeMarker)
                throw Error("Invalid range in a star relation");

            if (i + 2 < codePoints.Count && codePoints[i + 1] == RangeMarker && codePoints[i + 2] != RangeMarker)
            {
                for (var codePoint = codePoints[i]; codePoint <= codePoints[i + 2]; codePoint++)
                {
                    result.Add(char.ConvertFromUtf32(codePoint));
                }

                i += 2;
                continue;
            }

            result.Add(char.ConvertFromUtf32(codePoints[i]));
        }

        return result;
    }

    private string ReadText()
    {
        var codePoints = new List<int>();
        ReadText(codePoints, allowRangeDash: false);
        var builder = new StringBuilder(codePoints.Count);
        foreach (var codePoint in codePoints)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        return builder.ToString();
    }

    private void ReadText(List<int> output, bool allowRangeDash)
    {
        var start = output.Count;
        while (_position < _rules.Length)
        {
            var c = _rules[_position];
            if (c == '\'')
            {
                _position++;
                if (_position < _rules.Length && _rules[_position] == '\'')
                {
                    output.Add('\'');
                    _position++;
                    continue;
                }

                var quoted = new StringBuilder();
                while (true)
                {
                    if (_position >= _rules.Length)
                        throw Error("Unterminated quote");

                    if (_rules[_position] == '\'')
                    {
                        if (_position + 1 < _rules.Length && _rules[_position + 1] == '\'')
                        {
                            quoted.Append('\'');
                            _position += 2;
                            continue;
                        }

                        _position++;
                        break;
                    }

                    // The escapes of the CLDR XML (\uXXXX) are unescaped in quoted text too (ICU's data is unescaped before parsing)
                    if (_rules[_position] == '\\')
                    {
                        quoted.Append(ReadEscape());
                        continue;
                    }

                    quoted.Append(_rules[_position++]);
                }

                AddCodePoints(output, quoted.ToString());
                continue;
            }

            if (c == '\\')
            {
                AddCodePoints(output, ReadEscape());
                continue;
            }

            if (allowRangeDash && c == '-')
            {
                output.Add(RangeMarker);
                _position++;
                continue;
            }

            if (IsWhitespace(c) || IsSyntaxCharacter(c))
                break;

            if (char.IsHighSurrogate(c) && _position + 1 < _rules.Length && char.IsLowSurrogate(_rules[_position + 1]))
            {
                output.Add(char.ConvertToUtf32(c, _rules[_position + 1]));
                _position += 2;
                continue;
            }

            output.Add(c);
            _position++;
        }

        if (output.Count == start)
            throw Error("Missing text");

        static void AddCodePoints(List<int> output, string value)
        {
            foreach (var rune in value.EnumerateRunes())
            {
                output.Add(rune.Value);
            }
        }
    }

    private string ReadEscape()
    {
        _position++;
        if (_position >= _rules.Length)
            throw Error("Invalid escape");

        var c = _rules[_position++];
        switch (c)
        {
            case 'u':
                return ReadHex(4);
            case 'U':
                return ReadHex(8);
            case 'x' when _position < _rules.Length && _rules[_position] == '{':
                var end = _rules.IndexOf('}', _position, StringComparison.Ordinal);
                if (end < 0)
                    throw Error("Invalid escape");

                var value = Convert.ToInt32(_rules[(_position + 1)..end], 16);
                _position = end + 1;
                return char.ConvertFromUtf32(value);
            case 'x':
                return ReadHex(2);
            case 't':
                return "\t";
            case 'n':
                return "\n";
            case 'r':
                return "\r";
            default:
                if (char.IsHighSurrogate(c) && _position < _rules.Length)
                    return string.Concat(c.ToString(), _rules[_position++].ToString());

                return c.ToString();
        }

        string ReadHex(int length)
        {
            if (_position + length > _rules.Length)
                throw Error("Invalid escape");

            var result = Convert.ToInt32(_rules.Substring(_position, length), 16);
            _position += length;
            return char.ConvertFromUtf32(result);
        }
    }

    private bool TryReadOption(out string option)
    {
        if (_position >= _rules.Length || _rules[_position] != '[')
        {
            option = "";
            return false;
        }

        // Options may contain UnicodeSets with nested brackets: [suppressContractions [a-z]]
        var depth = 0;
        var start = _position + 1;
        while (_position < _rules.Length)
        {
            var c = _rules[_position];
            if (c == '\\')
            {
                _position += 2;
                continue;
            }

            if (c == '\'')
            {
                var end = _rules.IndexOf('\'', _position + 1, StringComparison.Ordinal);
                if (end < 0)
                    throw Error("Unterminated quote");

                _position = end + 1;
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    option = string.Join(' ', _rules[start.._position].Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
                    _position++;
                    return true;
                }
            }

            _position++;
        }

        throw Error("Unterminated option");
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _rules.Length)
        {
            var c = _rules[_position];
            if (IsWhitespace(c))
            {
                _position++;
            }
            else if (c == '#')
            {
                while (_position < _rules.Length && _rules[_position] is not ('\n' or '\r'))
                {
                    _position++;
                }
            }
            else
            {
                break;
            }
        }
    }

    // Pattern_White_Space
    private static bool IsWhitespace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u0085' or '\u200E' or '\u200F' or '\u2028' or '\u2029';

    // The syntax characters of ICU: ASCII punctuation and symbols
    private static bool IsSyntaxCharacter(char c) => c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    private FormatException Error(string message) => new(message + " at offset " + _position.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of the collation rules");
}
