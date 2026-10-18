using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>A node of a value definition (https://drafts.csswg.org/css-values-4/#value-defs).</summary>
internal abstract class GrammarNode
{
    /// <summary>The minimum number of repetitions.</summary>
    public int Min { get; set; } = 1;

    /// <summary>The maximum number of repetitions (int.MaxValue for unbounded).</summary>
    public int Max { get; set; } = 1;

    /// <summary>Whether the repetitions are separated by commas (the # multiplier).</summary>
    public bool CommaSeparated { get; set; }

    /// <summary>The group must not be empty (the ! multiplier).</summary>
    public bool Required { get; set; }
}

/// <summary>A keyword, matched ASCII case-insensitively.</summary>
internal sealed class KeywordNode(string keyword) : GrammarNode
{
    public string Keyword { get; } = keyword;

    public override string ToString() => Keyword;
}

/// <summary>A literal character (, / : ; ...).</summary>
internal sealed class LiteralNode(string literal) : GrammarNode
{
    public string Literal { get; } = literal;

    public override string ToString() => Literal;
}

/// <summary>A type (&lt;length&gt;), with an optional range, or a property reference (&lt;'margin-top'&gt;).</summary>
internal sealed class TypeNode(string name, bool isProperty) : GrammarNode
{
    public string Name { get; } = name;

    public bool IsProperty { get; } = isProperty;

    public double? RangeMin { get; set; }

    public double? RangeMax { get; set; }

    /// <summary>The name of the type in the grammars of the types (e.g. &lt;length&gt;, or calc-size() for a functional notation).</summary>
    public string GrammarName => field ??= Name.EndsWith("()", StringComparison.Ordinal) ? Name : "<" + Name + ">";

    public override string ToString() => IsProperty ? "<'" + Name + "'>" : "<" + Name + ">";
}

/// <summary>A functional notation: name( grammar ).</summary>
internal sealed class FunctionNode(string name, GrammarNode? arguments) : GrammarNode
{
    public string Name { get; } = name;

    public GrammarNode? Arguments { get; } = arguments;

    public override string ToString() => Name + "(" + Arguments + ")";
}

internal enum GrammarCombinator
{
    /// <summary>Juxtaposition: all the components, in order.</summary>
    Sequence,

    /// <summary>&amp;&amp;: all the components, in any order.</summary>
    AllInAnyOrder,

    /// <summary>||: one or more of the components, in any order.</summary>
    AnyInAnyOrder,

    /// <summary>|: exactly one of the components.</summary>
    OneOf,
}

/// <summary>A group of components ([ ... ] or the implicit groups of the combinators).</summary>
internal sealed class GroupNode(GrammarCombinator combinator, List<GrammarNode> children) : GrammarNode
{
    public GrammarCombinator Combinator { get; } = combinator;

    public List<GrammarNode> Children { get; } = children;

    public override string ToString()
    {
        var separator = Combinator switch
        {
            GrammarCombinator.Sequence => " ",
            GrammarCombinator.AllInAnyOrder => " && ",
            GrammarCombinator.AnyInAnyOrder => " || ",
            _ => " | ",
        };

        return "[ " + string.Join(separator, Children) + " ]";
    }
}

/// <summary>Parses the value definition syntax (https://drafts.csswg.org/css-values-4/#value-defs).</summary>
internal sealed class ValueGrammarParser
{
    private readonly string _input;
    private int _position;

    private ValueGrammarParser(string input)
    {
        _input = input;
    }

    public static GrammarNode Parse(string grammar)
    {
        var parser = new ValueGrammarParser(grammar);
        var node = parser.ParseOneOf();
        parser.SkipWhitespace();
        if (parser._position < parser._input.Length)
            throw new FormatException($"Unexpected '{parser._input[parser._position..]}' in grammar '{grammar}'");

        return node;
    }

    private void SkipWhitespace()
    {
        while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
        {
            _position++;
        }
    }

    private bool TryConsume(string text)
    {
        SkipWhitespace();
        if (string.CompareOrdinal(_input, _position, text, 0, text.Length) != 0)
            return false;

        // "|" must not be the first character of "||"
        if (text == "|" && _position + 1 < _input.Length && _input[_position + 1] == '|')
            return false;

        _position += text.Length;
        return true;
    }

    private GrammarNode ParseOneOf() => ParseCombination(GrammarCombinator.OneOf, "|", ParseAnyInAnyOrder);

    private GrammarNode ParseAnyInAnyOrder() => ParseCombination(GrammarCombinator.AnyInAnyOrder, "||", ParseAllInAnyOrder);

    private GrammarNode ParseAllInAnyOrder() => ParseCombination(GrammarCombinator.AllInAnyOrder, "&&", ParseSequence);

    private GrammarNode ParseCombination(GrammarCombinator combinator, string separator, Func<GrammarNode> parseChild)
    {
        var children = new List<GrammarNode> { parseChild() };
        while (TryConsume(separator))
        {
            children.Add(parseChild());
        }

        return children.Count == 1 ? children[0] : new GroupNode(combinator, children);
    }

    private GrammarNode ParseSequence()
    {
        var children = new List<GrammarNode>();
        while (true)
        {
            SkipWhitespace();
            if (_position >= _input.Length || _input[_position] is ']' or ')' || StartsWith("|") || StartsWith("&&"))
                break;

            children.Add(ParseTerm());
        }

        if (children.Count == 0)
            throw new FormatException($"Empty sequence in grammar '{_input}'");

        return children.Count == 1 ? children[0] : new GroupNode(GrammarCombinator.Sequence, children);
    }

    private bool StartsWith(string text) => string.CompareOrdinal(_input, _position, text, 0, text.Length) == 0;

    private GrammarNode ParseTerm()
    {
        SkipWhitespace();
        GrammarNode node;
        var c = _input[_position];
        if (c == '[')
        {
            _position++;
            node = ParseOneOf();
            if (!TryConsume("]"))
                throw new FormatException($"Missing ']' in grammar '{_input}'");

            // A group is always a group, so the multiplier applies to the group (and not to the multiplied group it contains,
            // e.g. [ [ <integer> | infinite ]{2} ]#)
            if (node is not GroupNode || node.Min != 1 || node.Max != 1 || node.CommaSeparated || node.Required)
            {
                node = new GroupNode(GrammarCombinator.Sequence, [node]);
            }
        }
        else if (c == '<')
        {
            node = ParseType();
        }
        else if (c == '\'')
        {
            var end = _input.IndexOf('\'', _position + 1, StringComparison.Ordinal);
            node = new LiteralNode(_input[(_position + 1)..end]);
            _position = end + 1;
        }
        else if (IsNameStart(c))
        {
            var start = _position;
            while (_position < _input.Length && IsNameChar(_input[_position]))
            {
                _position++;
            }

            var name = _input[start.._position];
            if (_position < _input.Length && _input[_position] == '(')
            {
                _position++;
                SkipWhitespace();
                GrammarNode? arguments = null;
                if (_position < _input.Length && _input[_position] != ')')
                {
                    arguments = ParseOneOf();
                }

                if (!TryConsume(")"))
                    throw new FormatException($"Missing ')' in grammar '{_input}'");

                node = new FunctionNode(name, arguments);
            }
            else
            {
                node = new KeywordNode(name);
            }
        }
        else
        {
            // A literal character (, / : ; = ...)
            _position++;
            node = new LiteralNode(c.ToString());
        }

        return ParseMultipliers(node);
    }

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c is '-' or '_' || c > 0x7F;

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c > 0x7F;

    private TypeNode ParseType()
    {
        _position++;
        SkipWhitespace();
        TypeNode node;
        if (_input[_position] == '\'')
        {
            var end = _input.IndexOf('\'', _position + 1, StringComparison.Ordinal);
            node = new TypeNode(_input[(_position + 1)..end], isProperty: true);
            _position = end + 1;
        }
        else
        {
            var start = _position;
            while (_position < _input.Length && _input[_position] is not ('>' or ' ' or '['))
            {
                _position++;
            }

            node = new TypeNode(_input[start.._position], isProperty: false);
        }

        SkipWhitespace();
        if (_position < _input.Length && _input[_position] == '[')
        {
            // A range [min,max]
            var end = _input.IndexOf(']', _position, StringComparison.Ordinal);
            var parts = _input[(_position + 1)..end].Split(',');
            if (parts.Length == 2)
            {
                node.RangeMin = ParseRangeBound(parts[0]);
                node.RangeMax = ParseRangeBound(parts[1]);
            }

            _position = end + 1;
            SkipWhitespace();
        }

        // Skip to the end of the type (e.g. <boolean-expr[ ... ]>)
        var depth = 0;
        while (_position < _input.Length && !(_input[_position] == '>' && depth == 0))
        {
            if (_input[_position] == '[')
            {
                depth++;
            }
            else if (_input[_position] == ']')
            {
                depth--;
            }

            _position++;
        }

        _position++;
        return node;
    }

    private static double? ParseRangeBound(string value)
    {
        var text = value.Trim();
        if (text.Contains('∞', StringComparison.Ordinal))
            return text.StartsWith('-', StringComparison.Ordinal) || text.StartsWith('−', StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;

        // The bound can have a unit (0s, 0deg): only the number is kept
        var end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] is '.' or '-' or '+' or 'e'))
        {
            end++;
        }

        return double.TryParse(text[..end].Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    /// <summary>
    /// Parses the multipliers of a component. A repetition multiplier that follows another one applies to the repeated
    /// component (https://drafts.csswg.org/css-values-4/#mult-num-range: e.g. [ a | b ]+# is a comma-separated list of
    /// space-separated lists): the component is wrapped in a group.
    /// </summary>
    private GrammarNode ParseMultipliers(GrammarNode node)
    {
        var repeated = false;
        var commaSeparated = false;
        while (_position < _input.Length)
        {
            var c = _input[_position];

            // The braces that follow # are the number of items of the comma-separated list (e.g. <number>#{2})
            if (c is '*' or '+' or '#' or '{' && repeated && !(c == '{' && commaSeparated))
            {
                node = new GroupNode(GrammarCombinator.Sequence, [node]);
                repeated = false;
            }

            commaSeparated = c == '#';

            switch (c)
            {
                case '*':
                    node.Min = 0;
                    node.Max = int.MaxValue;
                    repeated = true;
                    _position++;
                    break;
                case '+':
                    node.Min = Math.Max(1, node.Min);
                    node.Max = int.MaxValue;
                    repeated = true;
                    _position++;
                    break;
                case '?':
                    node.Min = 0;
                    _position++;
                    break;
                case '#':
                    node.CommaSeparated = true;
                    node.Min = Math.Max(1, node.Min);
                    node.Max = int.MaxValue;
                    repeated = true;
                    _position++;
                    break;
                case '!':
                    node.Required = true;
                    _position++;
                    break;
                case '{':
                {
                    var end = _input.IndexOf('}', _position, StringComparison.Ordinal);
                    var parts = _input[(_position + 1)..end].Split(',');
                    node.Min = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                    node.Max = parts.Length == 1 ? node.Min : parts[1].Trim().Length == 0 ? int.MaxValue : int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                    repeated = true;
                    _position = end + 1;
                    break;
                }

                default:
                    return node;
            }
        }

        return node;
    }

    public static string Describe(GrammarNode node)
    {
        var builder = new StringBuilder(node.ToString());
        if (node.CommaSeparated)
        {
            builder.Append('#');
        }

        return builder.ToString();
    }
}
