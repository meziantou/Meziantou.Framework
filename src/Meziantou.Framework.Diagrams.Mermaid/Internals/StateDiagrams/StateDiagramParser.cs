using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.StateDiagrams;

/// <summary>Parses the <c>stateDiagram</c> and <c>stateDiagram-v2</c> diagrams of Mermaid.</summary>
internal sealed class StateDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly StateDiagram _diagram = new();
    private readonly Dictionary<string, StateDiagramState> _states = new(StringComparer.Ordinal);
    private readonly Stack<Scope> _scopes = new();
    private readonly Dictionary<StateDiagramState, IList<StateDiagramState>> _containers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<StateDiagramState> _declared = new(ReferenceEqualityComparer.Instance);

    private StateDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
        _scopes.Push(new Scope(Composite: null, RegionIndex: 0));
    }

    public static StateDiagram Parse(ParseContext context, int position) => new StateDiagramParser(context, position).Parse();

    private Scope Current => _scopes.Peek();

    private StateDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        var keyword = ReadWord();
        if (keyword is not ("stateDiagram" or "stateDiagram-v2"))
            throw _scanner.Error(keywordStart, $"'{keyword}' is not a state diagram.");

        _scanner.ExpectStatementEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "state");
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_scopes.Count > 1)
            throw _scanner.Error(_scanner.Text.Length, $"The composite state '{Current.Composite!.Id}' is not closed by '}}'.");

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        if (_scanner.Current == '}')
        {
            if (_scopes.Count == 1)
                throw _scanner.Error("'}' closes no composite state.");

            _scanner.Position++;
            _scopes.Pop();
            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWith("--") && IsAlone(2))
        {
            if (_scopes.Count == 1)
                throw _scanner.Error("'--' separates the regions of a composite state.");

            _scanner.Position += 2;
            var composite = Current.Composite!;
            composite.Regions.Add(new StateDiagramRegion());
            _scopes.Pop();
            _scopes.Push(new Scope(composite, composite.Regions.Count - 1));
            return;
        }

        if (_scanner.StartsWithKeyword("direction"))
        {
            _scanner.Position += "direction".Length;
            _scanner.SkipSpaces();
            var start = _scanner.Position;
            var direction = MermaidCommon.ParseDirection(ReadWord()) ?? throw _scanner.Error(start, "A direction is expected: TB, TD, BT, LR or RL.");
            if (Current.Composite is { } composite)
            {
                composite.Direction = direction;
            }
            else
            {
                _diagram.Direction = direction;
            }

            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("state"))
        {
            ParseStateDeclaration();
            return;
        }

        if (_scanner.StartsWithKeyword("note"))
        {
            ParseNote();
            return;
        }

        if (_scanner.StartsWithKeyword("classDef"))
        {
            var start = _scanner.Position;
            _scanner.Position += "classDef".Length;
            _scanner.SkipSpaces();
            var names = ReadWord();
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
            }

            return;
        }

        if (_scanner.StartsWithKeyword("class"))
        {
            var start = _scanner.Position;
            _scanner.Position += "class".Length;
            _scanner.SkipSpaces();
            var words = _scanner.ReadStatement().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
                throw _scanner.Error(start, "The states and a class name are expected.");

            var classNames = words[^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var id in string.Join(' ', words[..^1]).Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var className in classNames)
                {
                    GetState(id, start).CssClasses.Add(className);
                }
            }

            return;
        }

        if (_scanner.StartsWithKeyword("style"))
        {
            var start = _scanner.Position;
            _scanner.Position += "style".Length;
            _scanner.SkipSpaces();
            var state = GetState(ReadId(), start);
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            state.Style = state.Style is null ? style : state.Style.Merge(style);
            return;
        }

        ParseStateStatement();
    }

    /// <summary>Parses a transition (<c>a --&gt; b : label</c>), a description (<c>a : text</c>), or a state alone.</summary>
    private void ParseStateStatement()
    {
        var start = _scanner.Position;
        var source = ReadStateReference(asTarget: false);
        _scanner.SkipSpaces();
        if (_scanner.TryConsume("-->"))
        {
            _scanner.SkipSpaces();
            var target = ReadStateReference(asTarget: true);
            _scanner.SkipSpaces();
            DiagramText? label = null;
            if (_scanner.Current == ':')
            {
                _scanner.Position++;
                var text = _scanner.ReadRestOfLine().Trim();
                label = text.Length is 0 ? null : new DiagramText(MermaidText.DecodePlain(text));
            }
            else
            {
                _scanner.ExpectStatementEnd();
            }

            if (_diagram.Transitions.Count >= _context.Options.MaxEdges)
                throw _scanner.Error(start, $"The state diagram has more than {_context.Options.MaxEdges} transitions.");

            _diagram.Transitions.Add(new StateTransition(source, target, label));
            return;
        }

        if (_scanner.Current == ':')
        {
            _scanner.Position++;
            AddDescription(source, _scanner.ReadRestOfLine());
            return;
        }

        _scanner.ExpectStatementEnd();
    }

    private static void AddDescription(StateDiagramState state, string text)
    {
        var description = MermaidText.DecodePlain(text.Trim());
        if (description.Length is 0)
            return;

        state.Label = state.Label is null ? new DiagramText(description) : new DiagramText(state.Label.Text + "\n" + description);
    }

    private void ParseStateDeclaration()
    {
        var start = _scanner.Position;
        _scanner.Position += "state".Length;
        _scanner.SkipSpaces();
        StateDiagramState state;
        if (_scanner.Current == '"')
        {
            var description = MermaidText.DecodePlain(_scanner.ReadQuoted());
            _scanner.SkipSpaces();
            if (!_scanner.StartsWithKeyword("as"))
                throw _scanner.Error("'as' and an identifier are expected after the description.");

            _scanner.Position += 2;
            _scanner.SkipSpaces();
            state = GetState(ReadId(), start);
            state.Label = description.Length is 0 ? null : new DiagramText(description);
        }
        else
        {
            state = GetState(ReadId(), start);
        }

        Declare(state);
        ReadClasses(state);
        _scanner.SkipSpaces();
        if (_scanner.StartsWith("<<"))
        {
            var close = _scanner.Text.IndexOf(">>", _scanner.Position, StringComparison.Ordinal);
            if (close < 0)
                throw _scanner.Error("'>>' is expected.");

            var kind = _scanner.Text[(_scanner.Position + 2)..close].Trim();
            state.Kind = kind switch
            {
                "choice" => StateKind.Choice,
                "fork" => StateKind.Fork,
                "join" => StateKind.Join,
                _ => throw _scanner.Error($"'{kind}' is not a kind of state: use choice, fork or join."),
            };
            _scanner.Position = close + 2;
            _scanner.SkipSpaces();
        }

        if (_scanner.Current == ':')
        {
            _scanner.Position++;
            AddDescription(state, _scanner.ReadRestOfLine());
            return;
        }

        if (_scanner.Current == '{')
        {
            if (_scopes.Count > _context.Options.MaxDepth)
                throw _scanner.Error($"The composite states are nested deeper than {_context.Options.MaxDepth} levels.");

            _scanner.Position++;
            if (state.Regions.Count is 0)
            {
                state.Regions.Add(new StateDiagramRegion());
            }

            _scopes.Push(new Scope(state, state.Regions.Count - 1));
        }

        _scanner.ExpectStatementEnd();
    }

    private void ParseNote()
    {
        var start = _scanner.Position;
        _scanner.Position += "note".Length;
        _scanner.SkipSpaces();
        StateNotePosition position;
        if (_scanner.StartsWithKeyword("left"))
        {
            position = StateNotePosition.Left;
            _scanner.Position += 4;
        }
        else if (_scanner.StartsWithKeyword("right"))
        {
            position = StateNotePosition.Right;
            _scanner.Position += 5;
        }
        else
        {
            throw _scanner.Error("'left of' or 'right of' is expected.");
        }

        _scanner.SkipSpaces();
        if (!_scanner.StartsWithKeyword("of"))
            throw _scanner.Error("'of' is expected.");

        _scanner.Position += 2;
        _scanner.SkipSpaces();
        var state = GetState(ReadId(), start);
        _scanner.SkipSpaces();
        string text;
        if (_scanner.Current == ':')
        {
            _scanner.Position++;
            text = _scanner.ReadRestOfLine();
        }
        else
        {
            _scanner.ExpectStatementEnd();
            var lines = new List<string>();
            while (true)
            {
                if (_scanner.IsEnd)
                    throw _scanner.Error(start, "The note is not closed by 'end note'.");

                if (_scanner.Current == '\n')
                {
                    _scanner.Position++;
                }

                var line = _scanner.ReadLine();
                if (line.Trim() is "end note")
                    break;

                lines.Add(line.Trim());
            }

            text = string.Join('\n', lines);
        }

        // Decoded first, so a <br> at an end is trimmed as the line feed it is
        _diagram.Notes.Add(new StateNote(state, new DiagramText(MermaidText.DecodePlain(text).Trim()), position));
    }

    /// <summary>Reads a state, or <c>[*]</c>: the start state of the region when it is the source of a transition, its end state otherwise.</summary>
    private StateDiagramState ReadStateReference(bool asTarget)
    {
        var start = _scanner.Position;
        StateDiagramState state;
        if (_scanner.TryConsume("[*]"))
        {
            state = GetSpecialState(asTarget ? StateKind.End : StateKind.Start);
        }
        else
        {
            state = GetState(ReadId(), start);
        }

        ReadClasses(state);
        return state;
    }

    private StateDiagramState GetSpecialState(StateKind kind)
    {
        var scope = Current;
        var prefix = scope.Composite is null ? "root" : scope.Composite.Id + (scope.RegionIndex > 0 ? "/" + scope.RegionIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
        var id = "[*]" + prefix + (kind is StateKind.Start ? "/start" : "/end");
        if (_states.TryGetValue(id, out var existing))
            return existing;

        var state = new StateDiagramState(id, kind: kind);
        _states[id] = state;
        AddToScope(state);
        return state;
    }

    private void ReadClasses(StateDiagramState state)
    {
        while (_scanner.TryConsume(":::"))
        {
            var start = _scanner.Position;
            while (!_scanner.IsEnd && (char.IsLetterOrDigit(_scanner.Current) || _scanner.Current is '_' or '-'))
            {
                _scanner.Position++;
            }

            if (_scanner.Position == start)
                throw _scanner.Error("A class name is expected after ':::'.");

            state.CssClasses.Add(_scanner.Text[start.._scanner.Position]);
        }
    }

    /// <summary>Reads an identifier: letters, digits, <c>_</c>, <c>.</c> and <c>-</c> that does not start an arrow.</summary>
    private string ReadId()
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        var i = start;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '.' || (c == '-' && i + 1 < text.Length && text[i + 1] is not ('-' or '>')))
            {
                i++;
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i += 2;
                continue;
            }

            break;
        }

        if (i == start)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a state is expected.");

        _scanner.Position = i;
        return text[start..i];
    }

    /// <summary>Indicates whether an identifier has only the characters <see cref="ReadId"/> reads.</summary>
    internal static bool IsValidId(string id)
    {
        if (id.Length is 0)
            return false;

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '.' || (c == '-' && i + 1 < id.Length && id[i + 1] is not ('-' or '>')))
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < id.Length && char.IsLowSurrogate(id[i + 1]))
            {
                i++;
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>Indicates whether an identifier starts a statement, and cannot name a state.</summary>
    internal static bool IsKeyword(string id) => id is "state" or "note" or "direction" or "class" or "classDef" or "style" || id.StartsWith("accTitle", StringComparison.Ordinal) || id.StartsWith("accDescr", StringComparison.Ordinal);

    private StateDiagramState GetState(string id, int offset)
    {
        if (_states.TryGetValue(id, out var state))
            return state;

        if (IsKeyword(id))
            throw _scanner.Error(offset, $"'{id}' is a keyword and cannot be a state.");

        if (!IsValidId(id))
            throw _scanner.Error(offset, $"'{id}' is not a valid state identifier.");

        if (_states.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The state diagram has more than {_context.Options.MaxNodes} states.");

        state = new StateDiagramState(id);
        _states[id] = state;
        AddToScope(state);
        return state;
    }

    private IList<StateDiagramState> CurrentStates => Current.Composite is { } composite ? composite.Regions[Current.RegionIndex].States : _diagram.States;

    private void AddToScope(StateDiagramState state)
    {
        var states = CurrentStates;
        states.Add(state);
        _containers[state] = states;
    }

    /// <summary>
    /// Declares a state in the current region: the first declaration moves a state that was only mentioned before, in
    /// another region, so a state belongs to the region that declares it whatever the order of the statements.
    /// </summary>
    private void Declare(StateDiagramState state)
    {
        if (!_declared.Add(state))
            return;

        var states = CurrentStates;
        if (_containers.TryGetValue(state, out var container) && ReferenceEquals(container, states))
            return;

        // A state cannot move into itself
        foreach (var scope in _scopes)
        {
            if (scope.Composite == state)
                return;
        }

        container?.Remove(state);
        states.Add(state);
        _containers[state] = states;
    }

    private bool IsAlone(int length)
    {
        var end = _scanner.Position + length;
        while (end < _scanner.Text.Length && _scanner.Text[end] is ' ' or '\t')
        {
            end++;
        }

        return end >= _scanner.Text.Length || _scanner.Text[end] is '\n' or ';';
    }

    private string ReadWord()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && !char.IsWhiteSpace(_scanner.Current) && _scanner.Current != ';')
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private sealed record Scope(StateDiagramState? Composite, int RegionIndex);
}
