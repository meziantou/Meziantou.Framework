using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Compiles the syllable machines of the complex-script shapers of HarfBuzz (the Ragel scanners of
/// <c>tests/Meziantou.Framework.WebPlatform.SpecData/harfbuzz/src/hb-ot-shaper-*-machine.rl</c>, MIT license) to deterministic automata. The subset of Ragel used
/// by these files is supported: the <c>export</c> constants (the categories of the characters), the definitions, the
/// concatenation (<c>.</c> or juxtaposition), the union (<c>|</c>), the repetitions (<c>*</c>, <c>+</c>, <c>?</c>),
/// <c>any</c>, and the longest-match scanner of <c>main := |* pattern => { found_syllable (type); }; ... *|;</c>. The
/// patterns are compiled to an NFA (Thompson), then to a minimal DFA whose accepting states give the syllable type of the
/// first pattern that matches (Ragel's rule when several patterns match the longest token).
/// </summary>
internal static partial class SyllableMachineGenerator
{
    /// <summary>The number of category values of the class table (the categories are smaller).</summary>
    private const int CategoryCount = 64;

    /// <summary>Reads a machine from its Ragel file.</summary>
    public static RagelMachine Read(string path, string name)
    {
        var source = File.ReadAllText(path);

        // The syllable types: the enum of the C code ("enum indic_syllable_type_t { indic_consonant_syllable, ... };")
        var enumMatch = Regex.Match(source, @"enum\s+" + name + @"_syllable_type_t\s*\{(?<values>[^}]*)\}", RegexOptions.CultureInvariant);
        if (!enumMatch.Success)
            throw new InvalidOperationException($"No syllable types in {path}");

        var types = enumMatch.Groups["values"].Value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

        // The Ragel code is in the %%{ ... }%% blocks
        var ragel = new StringBuilder();
        foreach (Match block in Regex.Matches(source, @"%%\{(?<code>.*?)\}%%", RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            ragel.Append(block.Groups["code"].Value).Append('\n');
        }

        var machine = new RagelMachine(name, types);
        new Parser(Tokenize(ragel.ToString()), machine).ParseStatements();
        if (machine.Patterns.Count == 0)
            throw new InvalidOperationException($"No scanner in {path}");

        return machine;
    }

    /// <summary>Writes the tables of a machine and the enum of its syllable types.</summary>
    public static void Write(CodeWriter writer, RagelMachine machine, string className, string typeEnum, string description)
    {
        var dfa = Compile(machine);

        writer.Line($"/// <summary>The syllable types of the {description} (HarfBuzz's <c>{machine.Name}_syllable_type_t</c>).</summary>");
        writer.Line($"internal enum {typeEnum} : byte");
        writer.Line("{");
        foreach (var type in machine.SyllableTypes)
        {
            writer.Line("    " + PascalCase(type[(machine.Name.Length + 1)..]) + ",");
        }

        writer.Line("}").Line();

        var broken = machine.Patterns.Where(p => p.Broken).Select(p => p.Type).Distinct().ToList();
        if (broken.Count != 1)
            throw new InvalidOperationException($"The machine {machine.Name} must have one broken syllable type");

        writer.Line("/// <summary>");
        writer.Line($"/// The syllable machine of the {description} (HarfBuzz's <c>{machine.Name}_syllable_machine</c>, compiled from");
        writer.Line($"/// hb-ot-shaper-{machine.Name}-machine.rl to a minimal DFA): the state after state <c>s</c> and a character of the class");
        writer.Line("/// <c>c</c> is <c>Transitions[(s * ClassCount) + c]</c> (0: no transition; the start state is 1), and <c>Accepts[s]</c> is");
        writer.Line($"/// the <see cref=\"{typeEnum}\"/> of the longest token that ends in the state plus one (0: not accepting).");
        writer.Line("/// </summary>");
        using (writer.Block($"internal static class {className}"))
        {
            writer.Line($"public const int ClassCount = {dfa.ClassCount};").Line();
            writer.Line($"/// <summary>The syllable type whose syllables are broken (a dotted circle is inserted in them).</summary>");
            writer.Line($"public const {typeEnum} BrokenSyllable = {typeEnum}.{PascalCase(machine.SyllableTypes[broken[0]][(machine.Name.Length + 1)..])};").Line();
            writer.Line("/// <summary>The class of each category.</summary>");
            WriteArray(writer, "Classes", dfa.Classes);
            WriteArray(writer, "Transitions", dfa.Transitions);
            WriteArray(writer, "Accepts", dfa.Accepts);
        }

        writer.Line();
    }

    /// <summary>Writes the categories exported by machines as constants.</summary>
    public static void WriteCategories(CodeWriter writer, string className, string summary, IEnumerable<RagelMachine> machines)
    {
        var categories = new List<(string Name, int Value)>();
        foreach (var machine in machines)
        {
            foreach (var (name, value) in machine.Exports)
            {
                var existing = categories.FindIndex(c => c.Name == name);
                if (existing >= 0)
                {
                    if (categories[existing].Value != value)
                        throw new InvalidOperationException($"The category {name} has several values");

                    continue;
                }

                categories.Add((name, value));
            }
        }

        writer.Line($"/// <summary>{summary}</summary>");
        using (writer.Block($"internal static class {className}"))
        {
            foreach (var (name, value) in categories)
            {
                writer.Line($"public const byte {CategoryName(name)} = {value.ToString(CultureInfo.InvariantCulture)};");
            }
        }

        writer.Line();
    }

    /// <summary>The C# name of a category: the acronyms of 3 letters and more are PascalCase (e.g. ZWNJ is Zwnj).</summary>
    public static string CategoryName(string name) => name switch
    {
        "PLACEHOLDER" => "Placeholder",
        "DOTTEDCIRCLE" => "DottedCircle",
        _ when name.Length >= 3 && name.All(char.IsUpper) => name[0] + name[1..].ToLowerInvariant(),
        _ => name,
    };

    private static string PascalCase(string value) => string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static void WriteArray(CodeWriter writer, string name, IEnumerable<byte> values)
    {
        writer.Line($"public static ReadOnlySpan<byte> {name} =>");
        writer.Line("[");
        writer.Numbers(values, perLine: 32);
        writer.Line("];").Line();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Compilation
    // ----------------------------------------------------------------------------------------------------------------

    private sealed record Dfa(int ClassCount, byte[] Classes, byte[] Transitions, byte[] Accepts);

    private static Dfa Compile(RagelMachine machine)
    {
        // The alphabet: the exported categories and one value for the other categories (matched by "any" only)
        var symbols = machine.Exports.Select(e => e.Value).Distinct().Order().ToList();
        if (symbols.Any(s => s >= CategoryCount))
            throw new InvalidOperationException($"A category of {machine.Name} is larger than {CategoryCount}");

        var other = Enumerable.Range(0, 256).First(v => !symbols.Contains(v));
        symbols.Add(other);

        // The NFA of the scanner: a start state with an epsilon transition to each pattern
        var nfa = new Nfa();
        var start = nfa.AddState();
        var accepts = new Dictionary<int, int>();
        for (var i = 0; i < machine.Patterns.Count; i++)
        {
            var (patternStart, patternEnd) = nfa.Build(machine.Patterns[i].Node);
            nfa.AddEpsilon(start, patternStart);
            accepts[patternEnd] = i;
        }

        // Subset construction
        var states = new List<int[]>();
        var stateIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var transitions = new List<int[]>();
        var outputs = new List<int>();
        int AddSubset(SortedSet<int> subset)
        {
            var key = string.Join(',', subset);
            if (stateIndexes.TryGetValue(key, out var index))
                return index;

            index = states.Count;
            stateIndexes[key] = index;
            states.Add([.. subset]);
            transitions.Add(new int[symbols.Count]);

            // The type of the first pattern that accepts
            var pattern = subset.Where(accepts.ContainsKey).Select(s => accepts[s]).DefaultIfEmpty(-1).Min();
            outputs.Add(pattern < 0 ? 0 : machine.Patterns[pattern].Type + 1);
            return index;
        }

        AddSubset(nfa.Closure([start]));
        for (var i = 0; i < states.Count; i++)
        {
            for (var s = 0; s < symbols.Count; s++)
            {
                var next = nfa.Closure(nfa.Move(states[i], symbols[s]));
                transitions[i][s] = next.Count == 0 ? -1 : AddSubset(next);
            }
        }

        // Minimization (Moore): the states are split by their output, then by the partitions of their transitions
        var partition = outputs.ToArray();
        var partitionCount = 0;
        while (true)
        {
            var signatures = new Dictionary<string, int>(StringComparer.Ordinal);
            var next = new int[states.Count];
            for (var i = 0; i < states.Count; i++)
            {
                var signature = partition[i].ToString(CultureInfo.InvariantCulture) + ":" + string.Join(',', transitions[i].Select(t => t < 0 ? -1 : partition[t]));
                if (!signatures.TryGetValue(signature, out var id))
                {
                    id = signatures.Count;
                    signatures[signature] = id;
                }

                next[i] = id;
            }

            var count = signatures.Count;
            partition = next;
            if (count == partitionCount)
                break;

            partitionCount = count;
        }

        // The minimal states, numbered in breadth-first order from the start state (1; 0 is "no transition")
        var numbers = new Dictionary<int, int> { [partition[0]] = 1 };
        var representatives = new List<int> { 0 };
        for (var i = 0; i < representatives.Count; i++)
        {
            foreach (var target in transitions[representatives[i]])
            {
                if (target >= 0 && !numbers.ContainsKey(partition[target]))
                {
                    numbers[partition[target]] = numbers.Count + 1;
                    representatives.Add(target);
                }
            }
        }

        var stateCount = representatives.Count + 1;
        if (stateCount > 256)
            throw new InvalidOperationException($"The machine {machine.Name} has too many states");

        // The classes: the symbols whose transitions are the same in every state
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var symbolClasses = new int[symbols.Count];
        for (var s = 0; s < symbols.Count; s++)
        {
            var column = string.Join(',', representatives.Select(r => transitions[r][s] < 0 ? 0 : numbers[partition[transitions[r][s]]]));
            if (!columns.TryGetValue(column, out var classIndex))
            {
                classIndex = columns.Count;
                columns[column] = classIndex;
            }

            symbolClasses[s] = classIndex;
        }

        var classCount = columns.Count;
        var classes = new byte[CategoryCount];
        for (var value = 0; value < CategoryCount; value++)
        {
            var symbol = symbols.IndexOf(value);
            classes[value] = (byte)symbolClasses[symbol >= 0 ? symbol : symbols.Count - 1];
        }

        var table = new byte[stateCount * classCount];
        var acceptTable = new byte[stateCount];
        for (var i = 0; i < representatives.Count; i++)
        {
            var state = i + 1;
            acceptTable[state] = (byte)outputs[representatives[i]];
            for (var s = 0; s < symbols.Count; s++)
            {
                var target = transitions[representatives[i]][s];
                table[(state * classCount) + symbolClasses[s]] = (byte)(target < 0 ? 0 : numbers[partition[target]]);
            }
        }

        return new Dfa(classCount, classes, table, acceptTable);
    }

    private sealed class Nfa
    {
        private readonly List<List<(int[]? Symbols, int Target)>> _edges = [];

        public int AddState()
        {
            _edges.Add([]);
            return _edges.Count - 1;
        }

        public void AddEpsilon(int from, int to) => _edges[from].Add((null, to));

        public (int Start, int End) Build(Node node)
        {
            switch (node)
            {
                case SymbolNode symbol:
                {
                    var start = AddState();
                    var end = AddState();
                    _edges[start].Add((symbol.Values, end));
                    return (start, end);
                }

                case ConcatNode concat:
                {
                    var (aStart, aEnd) = Build(concat.Left);
                    var (bStart, bEnd) = Build(concat.Right);
                    AddEpsilon(aEnd, bStart);
                    return (aStart, bEnd);
                }

                case UnionNode union:
                {
                    var start = AddState();
                    var end = AddState();
                    var (aStart, aEnd) = Build(union.Left);
                    var (bStart, bEnd) = Build(union.Right);
                    AddEpsilon(start, aStart);
                    AddEpsilon(start, bStart);
                    AddEpsilon(aEnd, end);
                    AddEpsilon(bEnd, end);
                    return (start, end);
                }

                case RepeatNode repeat:
                {
                    var start = AddState();
                    var end = AddState();
                    var (aStart, aEnd) = Build(repeat.Inner);
                    AddEpsilon(start, aStart);
                    AddEpsilon(aEnd, end);
                    if (repeat.Kind != '+')
                    {
                        AddEpsilon(start, end);
                    }

                    if (repeat.Kind != '?')
                    {
                        AddEpsilon(aEnd, aStart);
                    }

                    return (start, end);
                }

                default:
                    throw new InvalidOperationException("Unknown node");
            }
        }

        public SortedSet<int> Closure(IEnumerable<int> states)
        {
            var result = new SortedSet<int>(states);
            var stack = new Stack<int>(result);
            while (stack.Count > 0)
            {
                foreach (var (symbols, target) in _edges[stack.Pop()])
                {
                    if (symbols is null && result.Add(target))
                    {
                        stack.Push(target);
                    }
                }
            }

            return result;
        }

        public List<int> Move(int[] states, int symbol)
        {
            var result = new List<int>();
            foreach (var state in states)
            {
                foreach (var (symbols, target) in _edges[state])
                {
                    if (symbols is not null && Array.IndexOf(symbols, symbol) >= 0)
                    {
                        result.Add(target);
                    }
                }
            }

            return result;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Parsing
    // ----------------------------------------------------------------------------------------------------------------

    internal abstract record Node;

    private sealed record SymbolNode(int[] Values) : Node;

    private sealed record ConcatNode(Node Left, Node Right) : Node;

    private sealed record UnionNode(Node Left, Node Right) : Node;

    private sealed record RepeatNode(Node Inner, char Kind) : Node;

    internal sealed class RagelMachine(string name, List<string> syllableTypes)
    {
        public string Name { get; } = name;

        public List<string> SyllableTypes { get; } = syllableTypes;

        public List<(string Name, int Value)> Exports { get; } = [];

        public Dictionary<string, Node> Definitions { get; } = new(StringComparer.Ordinal);

        public List<(Node Node, int Type, bool Broken)> Patterns { get; } = [];

        /// <summary>The value of an exported category.</summary>
        public int GetCategory(string name)
        {
            foreach (var export in Exports)
            {
                if (export.Name == name)
                    return export.Value;
            }

            throw new InvalidOperationException($"Unknown category {name} in the {Name} machine");
        }
    }

    private readonly record struct Token(string Text);

    private static List<Token> Tokenize(string code)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < code.Length;)
        {
            var c = code[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '#')
            {
                while (i < code.Length && code[i] != '\n')
                {
                    i++;
                }
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                var start = i;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(code[start..i]));
            }
            else if (c == '{')
            {
                // An action: the code between the braces
                var depth = 0;
                var start = i;
                do
                {
                    if (code[i] == '{')
                    {
                        depth++;
                    }
                    else if (code[i] == '}')
                    {
                        depth--;
                    }

                    i++;
                }
                while (depth > 0);
                tokens.Add(new Token(code[start..i]));
            }
            else if (code.AsSpan(i).StartsWith(":=", StringComparison.Ordinal) || code.AsSpan(i).StartsWith("=>", StringComparison.Ordinal))
            {
                tokens.Add(new Token(code.Substring(i, 2)));
                i += 2;
            }
            else
            {
                tokens.Add(new Token(c.ToString()));
                i++;
            }
        }

        return tokens;
    }

    private sealed partial class Parser(List<Token> tokens, RagelMachine machine)
    {
        private int _index;

        private string Peek(int offset = 0) => _index + offset < tokens.Count ? tokens[_index + offset].Text : "";

        private string Next() => tokens[_index++].Text;

        private void Expect(string text)
        {
            var token = Next();
            if (token != text)
                throw new InvalidOperationException($"Expected '{text}' but found '{token}' in the {machine.Name} machine");
        }

        public void ParseStatements()
        {
            while (_index < tokens.Count)
            {
                var keyword = Next();
                switch (keyword)
                {
                    case "machine" or "alphtype" or "write" or "getkey":
                        // Not needed: skip to the end of the statement
                        while (Next() != ";")
                        {
                        }

                        break;

                    case "export":
                    {
                        var name = Next();
                        Expect("=");
                        var value = int.Parse(Next(), CultureInfo.InvariantCulture);
                        Expect(";");
                        machine.Exports.Add((name, value));
                        machine.Definitions[name] = new SymbolNode([value]);
                        break;
                    }

                    default:
                    {
                        var op = Next();
                        if (op == "=")
                        {
                            machine.Definitions[keyword] = ParseUnion();
                            Expect(";");
                        }
                        else if (op == ":=" && keyword == "main")
                        {
                            ParseScanner();
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unexpected '{op}' after '{keyword}' in the {machine.Name} machine");
                        }

                        break;
                    }
                }
            }
        }

        private void ParseScanner()
        {
            Expect("|");
            Expect("*");
            while (!(Peek() == "*" && Peek(1) == "|"))
            {
                var pattern = ParseUnion();
                Expect("=>");
                var action = Next();
                Expect(";");
                var match = FoundSyllableRegex().Match(action);
                if (!match.Success)
                    throw new InvalidOperationException($"Unknown action {action} in the {machine.Name} machine");

                var type = machine.SyllableTypes.IndexOf(match.Groups["type"].Value);
                if (type < 0)
                    throw new InvalidOperationException($"Unknown syllable type {match.Groups["type"].Value} in the {machine.Name} machine");

                machine.Patterns.Add((pattern, type, action.Contains("HB_BUFFER_SCRATCH_FLAG_HAS_BROKEN_SYLLABLE", StringComparison.Ordinal)));
            }

            Expect("*");
            Expect("|");
            Expect(";");
        }

        // union: concat ('|' concat)*
        private Node ParseUnion()
        {
            var node = ParseConcat();
            while (Peek() == "|")
            {
                Next();
                node = new UnionNode(node, ParseConcat());
            }

            return node;
        }

        // concat: repeat (('.')? repeat)*
        private Node ParseConcat()
        {
            var node = ParseRepeat();
            while (true)
            {
                if (Peek() == ".")
                {
                    Next();
                    node = new ConcatNode(node, ParseRepeat());
                }
                else if (Peek() == "(" || IsIdentifier(Peek()))
                {
                    node = new ConcatNode(node, ParseRepeat());
                }
                else
                {
                    return node;
                }
            }
        }

        private Node ParseRepeat()
        {
            var node = ParsePrimary();
            while (Peek() is "*" or "+" or "?")
            {
                node = new RepeatNode(node, Next()[0]);
            }

            return node;
        }

        private Node ParsePrimary()
        {
            var token = Next();
            if (token == "(")
            {
                var node = ParseUnion();
                Expect(")");
                return node;
            }

            if (token == "any")
                return new SymbolNode([.. Enumerable.Range(0, 256)]);

            if (machine.Definitions.TryGetValue(token, out var definition))
                return definition;

            throw new InvalidOperationException($"Unknown name '{token}' in the {machine.Name} machine");
        }

        private static bool IsIdentifier(string token) => token.Length > 0 && (char.IsLetter(token[0]) || token[0] == '_');

        [GeneratedRegex(@"found_syllable\s*\(\s*(?<type>\w+)\s*\)", RegexOptions.CultureInvariant)]
        private static partial Regex FoundSyllableRegex();
    }
}
