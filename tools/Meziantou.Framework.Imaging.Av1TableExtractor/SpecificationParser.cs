using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.Av1TableExtractor;

/// <summary>The specification sources read by the tool, pinned by commit and content hash.</summary>
internal static class PinnedSpecification
{
    public const string Repository = "https://github.com/AOMediaCodec/av1-spec";
    public const string Commit = "5e04f3f75e73a5898d7616c47c52f032144b8f80";

    public const string Symbols = "03.symbols.md";
    public const string Syntax = "06.bitstream.syntax.md";
    public const string Semantics = "07.bitstream.semantics.md";
    public const string Decoding = "08.decoding.process.md";
    public const string Parsing = "09.parsing.process.md";
    public const string AdditionalTables = "10.additional.tables.md";

    public static IReadOnlyList<(string Name, string Sha256)> Files { get; } =
    [
        (Symbols, "374821830f3e0bcee8fa4dfcf1cac4c49883352a42d513d2c599ba033ee75af1"),
        (Syntax, "efe4cfac9368d8e5feff82a4dc95f1c71b37ae2353e5828f1849026be3b060b5"),
        (Semantics, "244da17d504ab7e6c9863df456f2e8816445b60d5a85ef7d66dce97435562196"),
        (Decoding, "1238128008ff673bceecc5e1e16bab0ee41ea59210953756ac61bd8ad990b64d"),
        (Parsing, "6423515792cf1118dbd79c25171b9be628adc839aae811678a83eb6923bbca95"),
        (AdditionalTables, "9d9a54994541adbd61e83f9af95aad818ea8e5608d090a9553d2178bbef52998"),
    ];

    /// <summary>
    /// Typographical errors of the pinned sources that prevent a table from being read. Each correction must match exactly once:
    /// a new revision of the specification that fixes one makes the tool fail instead of silently changing a table.
    /// </summary>
    public static IReadOnlyList<(string File, string Text, string Correction, string Reason)> Errata { get; } =
    [
        (AdditionalTables, "  TX_32X32,\n  TX_32X32\n  TX_4X8,", "  TX_32X32,\n  TX_32X32,\n  TX_4X8,", "Split_Tx_Size: missing comma between two elements"),
    ];

    public static string GetSha256(string file) => Files.Single(item => item.Name == file).Sha256;
}

internal sealed class FatalException(string message) : Exception(message);

/// <summary>A named integer constant: a symbol of the "Symbols" chapter, or a named value of a syntax element.</summary>
internal sealed record SpecConstant(string Name, long Value, string File);

/// <summary>A constant table, flattened in row-major order.</summary>
internal sealed record SpecTable(string Name, string Declaration, IReadOnlyList<int> Dimensions, IReadOnlyList<long> Values, string File, string Section);

internal sealed record Specification(IReadOnlyList<SpecConstant> Constants, IReadOnlyList<SpecTable> Tables);

internal static partial class SpecificationParser
{
    private static readonly string[] EnumerationFiles = [PinnedSpecification.Syntax, PinnedSpecification.Semantics, PinnedSpecification.Decoding, PinnedSpecification.Parsing];
    private static readonly string[] TableFiles = [PinnedSpecification.Syntax, PinnedSpecification.Decoding, PinnedSpecification.Parsing, PinnedSpecification.AdditionalTables];

    public static Specification Parse(IReadOnlyDictionary<string, string> sources)
    {
        var constants = new Dictionary<string, SpecConstant>(StringComparer.Ordinal);
        ParseSymbols(sources[PinnedSpecification.Symbols], constants);

        // A name listed with two different values (the same label reused by two syntax elements) cannot be a constant
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in EnumerationFiles)
        {
            ParseEnumerations(sources[file], file, constants, ambiguous);
        }

        foreach (var name in ambiguous)
        {
            constants.Remove(name);
        }

        var tables = new List<SpecTable>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in TableFiles)
        {
            var text = sources[file];
            foreach (var (_, erratum, correction, reason) in PinnedSpecification.Errata.Where(item => item.File == file))
            {
                var index = text.IndexOf(erratum, StringComparison.Ordinal);
                if (index < 0 || text.IndexOf(erratum, index + 1, StringComparison.Ordinal) >= 0)
                    throw new FatalException($"{file}: the erratum '{reason}' does not match exactly once.");

                text = text.Replace(erratum, correction, StringComparison.Ordinal);
            }

            foreach (var table in ParseTables(text, file, constants))
            {
                if (!names.Add(table.Name))
                    throw new FatalException($"Table {table.Name} is defined twice.");

                tables.Add(table);
            }
        }

        return new Specification([.. constants.Values], tables);
    }

    private static void ParseSymbols(string text, Dictionary<string, SpecConstant> constants)
    {
        var pending = new List<(string Name, string Expression)>();
        foreach (Match match in SymbolRowRegex().Matches(text))
        {
            pending.Add((match.Groups["name"].Value, match.Groups["value"].Value.Replace("\\", "", StringComparison.Ordinal).Trim()));
        }

        if (pending.Count is 0)
            throw new FatalException("No symbol found in " + PinnedSpecification.Symbols);

        // A symbol may be defined from a symbol declared after it
        while (pending.Count > 0)
        {
            var progress = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var (name, expression) = pending[i];
                if (!ExpressionEvaluator.TryEvaluate(expression, constants, out var value))
                    continue;

                // The specification lists a few symbols twice, with the same value
                if (!constants.TryAdd(name, new SpecConstant(name, value, PinnedSpecification.Symbols)) && constants[name].Value != value)
                    throw new FatalException($"Symbol {name} is defined twice with different values.");

                pending.RemoveAt(i);
                progress = true;
            }

            if (!progress)
                throw new FatalException("Cannot evaluate the symbols: " + string.Join(", ", pending.Select(item => item.Name)));
        }
    }

    private static void ParseEnumerations(string text, string file, Dictionary<string, SpecConstant> constants, HashSet<string> ambiguous)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            string name;
            long value;
            if (ValueNameRowRegex().Match(line) is { Success: true } valueFirst)
            {
                name = valueFirst.Groups["name"].Value;
                value = long.Parse(valueFirst.Groups["value"].Value, CultureInfo.InvariantCulture);
            }
            else if (NameValueRowRegex().Match(line) is { Success: true } nameFirst)
            {
                name = nameFirst.Groups["name"].Value;
                value = long.Parse(nameFirst.Groups["value"].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                continue;
            }

            if (constants.TryGetValue(name, out var existing))
            {
                if (existing.Value != value)
                    ambiguous.Add(name);
            }
            else
            {
                constants.Add(name, new SpecConstant(name, value, file));
            }
        }
    }

    private static IEnumerable<SpecTable> ParseTables(string text, string file, Dictionary<string, SpecConstant> constants)
    {
        var headings = HeadingRegex().Matches(text);
        foreach (Match match in TableDeclarationRegex().Matches(text))
        {
            var name = match.Groups["name"].Value;
            var open = match.Index + match.Length - 1;
            var close = FindClosingBrace(text, open, name);
            var body = StripComments(text[open..(close + 1)]);

            var dimensions = new List<int>();
            foreach (Capture capture in match.Groups["dimension"].Captures)
            {
                if (!ExpressionEvaluator.TryEvaluate(capture.Value, constants, out var dimension) || dimension <= 0)
                    throw new FatalException($"Table {name}: cannot evaluate the dimension '{capture.Value.Trim()}'.");

                dimensions.Add(checked((int)dimension));
            }

            var values = new List<long>();
            var position = 0;
            ParseInitializer(body, ref position, name, dimensions, level: 0, constants, values);
            SkipWhitespace(body, ref position);
            if (position != body.Length)
                throw new FatalException($"Table {name}: unexpected content after the initializer.");

            var expected = dimensions.Aggregate(1L, (product, dimension) => product * dimension);
            if (values.Count != expected)
                throw new FatalException($"Table {name}: {values.Count} values, expected {expected}.");

            var section = "";
            foreach (Match heading in headings)
            {
                if (heading.Index > match.Index)
                    break;

                section = heading.Groups["title"].Value.Trim();
            }

            var declaration = WhitespaceRegex().Replace(match.Value[..match.Value.LastIndexOf("=", StringComparison.Ordinal)].Trim(), " ");
            yield return new SpecTable(name, declaration, dimensions, values, file, section);
        }
    }

    // An initializer is either nested to the full rank of the table or flat for its innermost dimensions
    private static void ParseInitializer(string body, ref int position, string name, List<int> dimensions, int level, Dictionary<string, SpecConstant> constants, List<long> values)
    {
        SkipWhitespace(body, ref position);
        if (position >= body.Length || body[position] != '{')
            throw new FatalException($"Table {name}: '{{' expected.");

        position++;
        var count = 0;
        var elementsAreNested = false;
        while (true)
        {
            SkipWhitespace(body, ref position);
            if (position >= body.Length)
                throw new FatalException($"Table {name}: unterminated initializer.");

            if (body[position] == '}')
            {
                position++;
                break;
            }

            if (body[position] == '{')
            {
                if (level + 1 >= dimensions.Count)
                    throw new FatalException($"Table {name}: the initializer is nested deeper than the declaration.");

                elementsAreNested = true;
                ParseInitializer(body, ref position, name, dimensions, level + 1, constants, values);
            }
            else
            {
                if (elementsAreNested)
                    throw new FatalException($"Table {name}: mixed nested and flat elements.");

                var end = position;
                while (end < body.Length && body[end] is not (',' or '{' or '}'))
                {
                    end++;
                }

                var expression = body[position..end];
                if (!ExpressionEvaluator.TryEvaluate(expression, constants, out var value))
                    throw new FatalException($"Table {name}: cannot evaluate '{expression.Trim()}'.");

                values.Add(value);
                position = end;
            }

            count++;
            SkipWhitespace(body, ref position);
            if (position < body.Length && body[position] == ',')
                position++;
        }

        var expected = elementsAreNested
            ? dimensions[level]
            : dimensions.Skip(level).Aggregate(1L, (product, dimension) => product * dimension);
        if (count != expected)
            throw new FatalException($"Table {name}: {count} elements at nesting level {level}, expected {expected}.");
    }

    private static int FindClosingBrace(string text, int open, string name)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth is 0)
                    return i;
            }
        }

        throw new FatalException($"Table {name}: unterminated initializer.");
    }

    private static string StripComments(string text)
    {
        text = BlockCommentRegex().Replace(text, " ");
        return LineCommentRegex().Replace(text, " ");
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    [GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z_][A-Za-z_0-9]*)`\s*\|(?<value>[^|]*)\|", RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    private static partial Regex SymbolRowRegex();

    // The value is the cell before the name: "| lr_type | FrameRestorationType | Name" rows have a leading column
    [GeneratedRegex(@"^\|(.*\|)?\s*(?<value>\d+)\s*\|\s*(?<name>[A-Z][A-Z0-9_]{2,}[a-z]?)\.?\s*(\|.*)?$", RegexOptions.ExplicitCapture)]
    private static partial Regex ValueNameRowRegex();

    // Name-first rows are only read from two-column tables: wider tables use the name as a row label of unrelated numbers
    [GeneratedRegex(@"^\|\s*(?<name>[A-Z][A-Z0-9_]{2,}[a-z]?)\s*\|\s*(?<value>\d+)\s*\|?$", RegexOptions.ExplicitCapture)]
    private static partial Regex NameValueRowRegex();

    // A constant table has a capitalized name; lower-case names are local variables of the pseudo-code
    [GeneratedRegex(@"^[ \t]*(?<name>[A-Z][A-Za-z_0-9]*)[ \t]*(\[(?<dimension>[^\]\n]*)\][ \t]*)+=\s*\{", RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    private static partial Regex TableDeclarationRegex();

    [GeneratedRegex(@"^#{2,5}[ \t]+(?<title>.+)$", RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>Evaluates the integer constant expressions of the specification: literals, named constants, unary minus, <c>* / + - &lt;&lt; &gt;&gt;</c> and parentheses.</summary>
internal ref struct ExpressionEvaluator
{
    private readonly ReadOnlySpan<char> _text;
    private readonly Dictionary<string, SpecConstant> _constants;
    private int _position;
    private bool _failed;

    private ExpressionEvaluator(ReadOnlySpan<char> text, Dictionary<string, SpecConstant> constants)
    {
        _text = text;
        _constants = constants;
    }

    public static bool TryEvaluate(ReadOnlySpan<char> text, Dictionary<string, SpecConstant> constants, out long value)
    {
        var evaluator = new ExpressionEvaluator(text, constants);
        value = evaluator.ParseShift();
        evaluator.SkipWhitespace();
        return !evaluator._failed && evaluator._position == text.Length;
    }

    private long ParseShift()
    {
        var left = ParseAdditive();
        while (true)
        {
            if (TryConsume("<<"))
            {
                left <<= checked((int)ParseAdditive());
            }
            else if (TryConsume(">>"))
            {
                left >>= checked((int)ParseAdditive());
            }
            else
            {
                return left;
            }
        }
    }

    private long ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            if (TryConsume("+"))
            {
                left = checked(left + ParseMultiplicative());
            }
            else if (TryConsume("-"))
            {
                left = checked(left - ParseMultiplicative());
            }
            else
            {
                return left;
            }
        }
    }

    private long ParseMultiplicative()
    {
        var left = ParseUnary();
        while (true)
        {
            if (TryConsume("*"))
            {
                left = checked(left * ParseUnary());
            }
            else if (TryConsume("/"))
            {
                var right = ParseUnary();
                if (right is 0)
                {
                    _failed = true;
                    return 0;
                }

                left /= right;
            }
            else
            {
                return left;
            }
        }
    }

    private long ParseUnary()
    {
        if (TryConsume("-"))
            return -ParseUnary();

        if (TryConsume("("))
        {
            var value = ParseShift();
            if (!TryConsume(")"))
                _failed = true;

            return value;
        }

        SkipWhitespace();
        var start = _position;
        while (_position < _text.Length && (char.IsAsciiLetterOrDigit(_text[_position]) || _text[_position] == '_'))
        {
            _position++;
        }

        var token = _text[start.._position];
        if (token.IsEmpty)
        {
            _failed = true;
            return 0;
        }

        if (char.IsAsciiDigit(token[0]))
        {
            if (long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var literal))
                return literal;

            _failed = true;
            return 0;
        }

        if (_constants.TryGetValue(token.ToString(), out var constant))
            return constant.Value;

        _failed = true;
        return 0;
    }

    private bool TryConsume(string token)
    {
        SkipWhitespace();
        if (!_text[_position..].StartsWith(token, StringComparison.Ordinal))
            return false;

        _position += token.Length;
        return true;
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
        {
            _position++;
        }
    }
}
