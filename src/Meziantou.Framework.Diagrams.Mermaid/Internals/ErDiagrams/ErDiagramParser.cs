namespace Meziantou.Framework.Diagrams.Mermaid.Internals.ErDiagrams;

/// <summary>Parses the <c>erDiagram</c> diagrams of Mermaid.</summary>
internal sealed class ErDiagramParser
{
    // The aliases of the cardinalities, longest first
    private static readonly (string Text, ErCardinality Cardinality)[] CardinalityWords =
    [
        ("one or zero", ErCardinality.ZeroOrOne),
        ("one or more", ErCardinality.OneOrMore),
        ("one or many", ErCardinality.OneOrMore),
        ("zero or one", ErCardinality.ZeroOrOne),
        ("zero or more", ErCardinality.ZeroOrMore),
        ("zero or many", ErCardinality.ZeroOrMore),
        ("only one", ErCardinality.ExactlyOne),
        ("many(0)", ErCardinality.ZeroOrMore),
        ("many(1)", ErCardinality.OneOrMore),
        ("many", ErCardinality.ZeroOrMore),
        ("one", ErCardinality.ExactlyOne),
        ("1+", ErCardinality.OneOrMore),
        ("0+", ErCardinality.ZeroOrMore),
    ];

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly ErDiagram _diagram = new();
    private readonly Dictionary<string, ErEntity> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ErSubgraph> _subgraphs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _subgraphMembers = new(StringComparer.Ordinal);
    private readonly HashSet<ErSubgraph> _nested = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<Scope> _scopes = new();

    private ErDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static ErDiagram Parse(ParseContext context, int position) => new ErDiagramParser(context, position).Parse();

    private ErDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("erDiagram"))
            throw _scanner.Error(keywordStart, "'erDiagram' is expected.");

        _scanner.ExpectStatementEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "er");
        if (_context.Configuration.GetNumber("er", "nodeSpacing") is >= 0 and <= 10_000 and var nodeSpacing)
        {
            _diagram.NodeSpacing = nodeSpacing;
        }

        if (_context.Configuration.GetNumber("er", "rankSpacing") is >= 0 and <= 10_000 and var rankSpacing)
        {
            _diagram.RankSpacing = rankSpacing;
        }

        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_scopes.Count > 0)
            throw _scanner.Error(_scanner.Text.Length, $"The subgraph '{_scopes.Peek().Id}' is not closed by 'end'.");

        // A subgraph hides the entity of the same name: the relationships to the entity go to the subgraph
        foreach (var (id, subgraph) in _subgraphs)
        {
            if (_entities.Remove(id, out var hidden))
            {
                _diagram.Entities.Remove(hidden);
                for (var i = 0; i < _diagram.Relationships.Count; i++)
                {
                    var relationship = _diagram.Relationships[i];
                    if (relationship.From == hidden || relationship.To == hidden)
                    {
                        _diagram.Relationships[i] = new ErRelationship(relationship.From == hidden ? subgraph : relationship.From, relationship.To == hidden ? subgraph : relationship.To)
                        {
                            FromCardinality = relationship.FromCardinality,
                            ToCardinality = relationship.ToCardinality,
                            IsIdentifying = relationship.IsIdentifying,
                            Label = relationship.Label,
                        };
                    }
                }

                foreach (var other in _subgraphs.Values)
                {
                    other.Entities.Remove(hidden);
                }
            }
        }

        foreach (var subgraph in _subgraphs.Values)
        {
            if (!_nested.Contains(subgraph))
            {
                _diagram.Subgraphs.Add(subgraph);
            }
        }

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (_scanner.StartsWithKeyword("direction"))
        {
            _scanner.Position += "direction".Length;
            _scanner.SkipSpaces();
            var direction = MermaidCommon.ParseDirection(ReadWord()) ?? throw _scanner.Error(start, "A direction is expected: TB, BT, LR or RL.");
            if (_scopes.Count > 0)
            {
                _scopes.Peek().Direction = direction;
            }
            else
            {
                _diagram.Direction = direction;
            }

            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("subgraph"))
        {
            ParseSubgraphHeader();
            return;
        }

        if (_scanner.StartsWithKeyword("end"))
        {
            if (_scopes.Count is 0)
                throw _scanner.Error("'end' closes no subgraph.");

            _scanner.Position += "end".Length;
            _scanner.ExpectStatementEnd();
            CloseSubgraph(_scopes.Pop());
            return;
        }

        if (_scanner.StartsWithKeyword("classDef"))
        {
            _scanner.Position += "classDef".Length;
            _scanner.SkipSpaces();
            var names = ReadWord();
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            foreach (var name in ValidateClassNames(SplitList(names), start))
            {
                _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
            }

            return;
        }

        if (_scanner.StartsWithKeyword("class"))
        {
            _scanner.Position += "class".Length;
            _scanner.SkipSpaces();
            var ids = ReadWord();
            _scanner.SkipSpaces();
            var classes = ReadWord();
            if (ids.Length is 0 || classes.Length is 0)
                throw _scanner.Error(start, "The entities and the class names are expected.");

            _scanner.ExpectStatementEnd();
            foreach (var id in SplitList(ids))
            {
                if (FindElement(id) is { } element)
                {
                    AddClasses(element, ValidateClassNames(SplitList(classes), start));
                }
                else
                {
                    _context.Warn(start, $"The element '{id}' is not defined: its classes are ignored.");
                }
            }

            return;
        }

        if (_scanner.StartsWithKeyword("style"))
        {
            _scanner.Position += "style".Length;
            _scanner.SkipSpaces();
            var ids = ReadWord();
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            foreach (var id in SplitList(ids))
            {
                if (FindElement(id) is { } element)
                {
                    element.Style = element.Style is null ? style : element.Style.Merge(style);
                }
                else
                {
                    _context.Warn(start, $"The element '{id}' is not defined: its style is ignored.");
                }
            }

            return;
        }

        ParseEntityStatement();
    }

    /// <summary>Parses an entity, its alias, its classes and its attributes, or a relationship.</summary>
    private void ParseEntityStatement()
    {
        var start = _scanner.Position;
        var name = ReadEntityName();
        _scanner.SkipSpaces();
        string? alias = null;
        if (_scanner.Current == '[')
        {
            _scanner.Position++;
            _scanner.SkipSpaces();
            alias = ReadEntityName();
            _scanner.SkipSpaces();
            _scanner.Expect("]");
        }

        var classes = ReadClasses();
        _scanner.SkipSpaces();
        if (alias is null && TryReadCardinality(isLeft: true, out var fromCardinality))
        {
            ParseRelationship(start, name, classes, fromCardinality);
            return;
        }

        var entity = GetEntity(name, start);
        if (alias is not null && entity.Label is null)
        {
            entity.Label = new DiagramText(alias);
        }

        AddClasses(entity, classes);
        Mention(name);
        if (_scanner.Current == '{')
        {
            _scanner.Position++;
            ParseAttributes(entity);
        }

        _scanner.ExpectStatementEnd();
    }

    private void ParseRelationship(int start, string fromName, List<string> fromClasses, ErCardinality fromCardinality)
    {
        _scanner.SkipSpaces();
        bool identifying;
        if (_scanner.TryConsume("--"))
        {
            identifying = true;
        }
        else if (_scanner.TryConsume("..") || _scanner.TryConsume(".-") || _scanner.TryConsume("-."))
        {
            identifying = false;
        }
        else if (TryConsumeWords("optionally to"))
        {
            identifying = false;
        }
        else if (TryConsumeWords("to"))
        {
            identifying = true;
        }
        else
        {
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': '--' or '..' is expected.");
        }

        _scanner.SkipSpaces();
        if (!TryReadCardinality(isLeft: false, out var toCardinality))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a cardinality is expected.");

        _scanner.SkipSpaces();
        var toStart = _scanner.Position;
        var toName = ReadEntityName();
        var toClasses = ReadClasses();
        _scanner.SkipSpaces();
        if (_scanner.Current != ':')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': ':' and the label of the relationship are expected.");

        _scanner.Position++;
        _scanner.SkipSpaces();
        string label;
        if (_scanner.Current == '"')
        {
            var quoteStart = _scanner.Position;
            label = _scanner.ReadQuoted();
            if (label.Contains('\n', StringComparison.Ordinal))
                throw _scanner.Error(quoteStart, "The label cannot span several lines.");
        }
        else
        {
            label = ReadNameCharacters();
            if (label.Length is 0)
                throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the label of the relationship is expected.");
        }

        _scanner.ExpectStatementEnd();
        if (_diagram.Relationships.Count >= _context.Options.MaxEdges)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxEdges} relationships.");

        var from = GetEndpoint(fromName, start);
        var to = GetEndpoint(toName, toStart);
        AddClasses(from, fromClasses);
        AddClasses(to, toClasses);
        Mention(fromName);
        Mention(toName);
        var text = MermaidText.DecodePlain(label);
        _diagram.Relationships.Add(new ErRelationship(from, to)
        {
            FromCardinality = fromCardinality,
            ToCardinality = toCardinality,
            IsIdentifying = identifying,
            Label = text.Length is 0 ? null : new DiagramText(text),
        });
    }

    /// <summary>Reads the attributes up to the closing brace: <c>type name [PK, FK] ["comment"]</c>.</summary>
    private void ParseAttributes(ErEntity entity)
    {
        var start = _scanner.Position;
        while (true)
        {
            SkipWhiteSpace();
            if (_scanner.IsEnd)
                throw _scanner.Error(start, "The attributes are not closed by '}'.");

            if (_scanner.Current == '}')
            {
                _scanner.Position++;
                return;
            }

            var attributeStart = _scanner.Position;
            var type = ReadAttributeWord() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the type of an attribute is expected.");
            _scanner.SkipSpaces();
            if (_scanner.Current == '?' && (_scanner.Peek() is ' ' or '\t' or '\n'))
            {
                type += "?";
                _scanner.Position++;
            }

            SkipWhiteSpace();
            var name = ReadAttributeWord() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the name of the attribute is expected.");
            var keys = ErAttributeKeys.None;
            while (true)
            {
                SkipWhiteSpace();
                if (_scanner.Current == ',')
                {
                    _scanner.Position++;
                    continue;
                }

                if (!TryReadKeys(ref keys))
                    break;
            }

            string? comment = null;
            if (_scanner.Current == '"')
            {
                var quoteStart = _scanner.Position;
                comment = _scanner.ReadQuoted();
                if (comment.Contains('\n', StringComparison.Ordinal))
                    throw _scanner.Error(quoteStart, "The comment cannot span several lines.");

                if (comment.Length is 0)
                {
                    comment = null;
                }
            }

            if (entity.Attributes.Count >= _context.Options.MaxNodes)
                throw _scanner.Error(attributeStart, $"The entity has more than {_context.Options.MaxNodes} attributes.");

            entity.Attributes.Add(new ErAttribute(type, name) { Keys = keys, Comment = comment });
        }
    }

    /// <summary>Reads a word of an attribute: a word between backticks, or characters up to a space, a quote or a brace.</summary>
    private string? ReadAttributeWord()
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        if (_scanner.Current == '`')
        {
            var end = text.IndexOfOrdinal('`', start + 1);
            if (end < 0)
                throw _scanner.Error(start, "The word is not closed by '`'.");

            var word = text[(start + 1)..end];
            if (word.Length is 0 || word.Contains('\n', StringComparison.Ordinal))
                throw _scanner.Error(start, "A word between backticks cannot be empty or span several lines.");

            _scanner.Position = end + 1;
            return word;
        }

        var i = start;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('"' or '`' or '{' or '}'))
        {
            i++;
        }

        if (i == start)
            return null;

        // As in Mermaid, a word with a generic type goes up to the last tilde of the line: Map~string, int~
        var firstTilde = text.IndexOf("~", start, i - start, StringComparison.Ordinal);
        if (firstTilde >= 0)
        {
            var lineEnd = text.IndexOfOrdinal('\n', start);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var lastTilde = text.LastIndexOf("~", lineEnd - 1, lineEnd - start, StringComparison.Ordinal);
            if (lastTilde > firstTilde && lastTilde >= i)
            {
                i = lastTilde + 1;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
            }
        }

        var result = text[start..i];
        if (result.Contains('`', StringComparison.Ordinal))
            throw _scanner.Error(start, "A word with a generic type cannot contain a backtick.");

        _scanner.Position = i;
        return result;
    }

    /// <summary>Reads keys: <c>PK</c>, <c>FK</c> or <c>UK</c>, possibly separated by commas without spaces.</summary>
    private bool TryReadKeys(ref ErAttributeKeys keys)
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        var i = start;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('"' or '`' or '{' or '}'))
        {
            i++;
        }

        if (i == start)
            return false;

        var result = keys;
        foreach (var part in text[start..i].Split(','))
        {
            if (part.Length is 0)
                continue;

            var key = ParseKey(part);
            if (key is ErAttributeKeys.None)
                return false;

            result |= key;
        }

        keys = result;
        _scanner.Position = i;
        return true;
    }

    internal static ErAttributeKeys ParseKey(string value) => value.ToUpperInvariant() switch
    {
        "PK" => ErAttributeKeys.PrimaryKey,
        "FK" => ErAttributeKeys.ForeignKey,
        "UK" => ErAttributeKeys.UniqueKey,
        _ => ErAttributeKeys.None,
    };

    private bool TryReadCardinality(bool isLeft, out ErCardinality cardinality)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        cardinality = default;
        if (position + 1 < text.Length)
        {
            var pair = text.AsSpan(position, 2);
            ErCardinality? symbol = pair switch
            {
                "||" => ErCardinality.ExactlyOne,
                "|o" or "o|" => ErCardinality.ZeroOrOne,
                "}o" or "o{" => ErCardinality.ZeroOrMore,
                "}|" or "|{" => ErCardinality.OneOrMore,
                _ => null,
            };
            if (symbol is { } value)
            {
                _scanner.Position += 2;
                cardinality = value;
                return true;
            }
        }

        // The parent of a master-detail relationship is a u before the line
        if (isLeft && _scanner.Current is 'u' && _scanner.Peek() is '-' or '.' or '|')
        {
            _scanner.Position++;
            cardinality = ErCardinality.Parent;
            return true;
        }

        foreach (var (word, value) in CardinalityWords)
        {
            if (TryConsumeWords(word))
            {
                cardinality = value;
                return true;
            }
        }

        // 1 is a cardinality before a space and a word, or before the line
        if (_scanner.Current == '1')
        {
            var next = _scanner.Peek();
            var afterSpaces = position + 1;
            while (afterSpaces < text.Length && text[afterSpaces] is ' ' or '\t')
            {
                afterSpaces++;
            }

            var isLine = string.CompareOrdinal(text, position + 1, "--", 0, 2) == 0 || string.CompareOrdinal(text, position + 1, "..", 0, 2) == 0 || string.CompareOrdinal(text, position + 1, ".-", 0, 2) == 0 || string.CompareOrdinal(text, position + 1, "-.", 0, 2) == 0;
            var isWord = next is ' ' or '\t' && afterSpaces < text.Length && (char.IsAsciiLetterOrDigit(text[afterSpaces]) || text[afterSpaces] is '_' or '"' or '\'');
            if (isLine || isWord)
            {
                _scanner.Position++;
                cardinality = ErCardinality.ExactlyOne;
                return true;
            }
        }

        return false;
    }

    /// <summary>Consumes words, ignoring the case, when they are not followed by a letter or a digit.</summary>
    private bool TryConsumeWords(string words)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        if (position + words.Length > text.Length || string.Compare(text, position, words, 0, words.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var end = position + words.Length;
        if (end < text.Length && char.IsLetterOrDigit(words[^1]) && IsNameCharacter(text[end]))
            return false;

        _scanner.Position = end;
        return true;
    }

    private void ParseSubgraphHeader()
    {
        var start = _scanner.Position;
        _scanner.Position += "subgraph".Length;
        _scanner.SkipSpaces();
        var id = ReadEntityName();
        _scanner.SkipSpaces();
        var title = id;
        if (_scanner.Current == '[')
        {
            _scanner.Position++;
            var parts = new List<string>();
            while (true)
            {
                _scanner.SkipSpaces();
                if (_scanner.Current == ']')
                {
                    _scanner.Position++;
                    break;
                }

                if (_scanner.IsAtStatementEnd())
                    throw _scanner.Error(start, "The title of the subgraph is not closed by ']'.");

                parts.Add(ReadEntityName());
            }

            if (parts.Count is 0)
                throw _scanner.Error(start, "The title of the subgraph is empty.");

            title = string.Join(' ', parts);
        }

        _scanner.ExpectStatementEnd();
        if (_scopes.Count >= _context.Options.MaxDepth)
            throw _scanner.Error(start, $"The subgraphs are nested deeper than {_context.Options.MaxDepth} levels.");

        if (_subgraphs.ContainsKey(id) || _scopes.Any(scope => string.Equals(scope.Id, id, StringComparison.Ordinal)))
            throw _scanner.Error(start, $"The subgraph '{id}' is already defined.");

        if (_subgraphs.Count + _scopes.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} subgraphs.");

        _scopes.Push(new Scope(id, title));
    }

    /// <summary>
    /// Creates a subgraph at its end, as Mermaid: its members are the entities and the subgraphs its statements
    /// mention, but those that an earlier subgraph already has.
    /// </summary>
    private void CloseSubgraph(Scope scope)
    {
        var subgraph = new ErSubgraph(scope.Id, string.Equals(scope.Title, scope.Id, StringComparison.Ordinal) ? null : new DiagramText(scope.Title)) { Direction = scope.Direction };
        foreach (var member in scope.Members)
        {
            if (!_subgraphMembers.Add(member))
                continue;

            if (_subgraphs.TryGetValue(member, out var child))
            {
                if (_nested.Add(child))
                {
                    subgraph.Subgraphs.Add(child);
                }
            }
            else if (_entities.TryGetValue(member, out var entity))
            {
                subgraph.Entities.Add(entity);
            }
        }

        _subgraphs[scope.Id] = subgraph;
        Mention(scope.Id);
    }

    private void Mention(string name)
    {
        if (_scopes.Count > 0)
        {
            var members = _scopes.Peek().Members;
            if (!members.Contains(name))
            {
                members.Add(name);
            }
        }
    }

    private ErElement GetEndpoint(string name, int offset) => _subgraphs.TryGetValue(name, out var subgraph) ? subgraph : GetEntity(name, offset);

    private ErElement? FindElement(string id) => _subgraphs.TryGetValue(id, out var subgraph) ? subgraph : _entities.GetValueOrDefault(id);

    private ErEntity GetEntity(string name, int offset)
    {
        if (_entities.TryGetValue(name, out var entity))
            return entity;

        if (_entities.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} entities.");

        entity = new ErEntity(name);
        _entities[name] = entity;
        _diagram.Entities.Add(entity);
        return entity;
    }

    private static void AddClasses(ErElement element, IEnumerable<string> classes)
    {
        foreach (var name in classes)
        {
            element.CssClasses.Add(name);
        }
    }

    private List<string> ReadClasses()
    {
        var result = new List<string>();
        if (!_scanner.TryConsume(":::"))
            return result;

        var start = _scanner.Position;
        while (!_scanner.IsEnd && (IsNameCharacter(_scanner.Current) || _scanner.Current == ','))
        {
            _scanner.Position++;
        }

        result.AddRange(SplitList(_scanner.Text[start.._scanner.Position]));
        if (result.Count is 0)
            throw _scanner.Error(start, "A class name is expected after ':::'.");

        return result;
    }

    /// <summary>Reads the name of an entity: a quoted name, or letters, digits, <c>_</c>, <c>-</c>, <c>*</c>, <c>.</c> and non-ASCII characters.</summary>
    private string ReadEntityName()
    {
        var start = _scanner.Position;
        if (_scanner.Current == '"')
        {
            var name = _scanner.ReadQuoted();
            if (!IsValidQuotedName(name))
                throw _scanner.Error(start, "A quoted name cannot be empty, nor contain '%', '\\' or a line break.");

            return name;
        }

        var result = ReadNameCharacters();
        if (result.Length is 0)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an entity is expected.");

        return result;
    }

    private string ReadNameCharacters()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && IsNameCharacter(_scanner.Current))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    internal static bool IsNameCharacter(char c) => c > '\u007F' || char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '*' or '.';

    internal static bool IsValidQuotedName(string name) => name.Length > 0 && name.AsSpan().IndexOfAny("%\\\r\n\v\b\"") < 0;

    private void SkipWhiteSpace()
    {
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.Current != '\n')
                return;

            _scanner.Position++;
        }
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

    private string[] ValidateClassNames(string[] names, int offset)
    {
        foreach (var name in names)
        {
            if (!IsValidClassName(name))
                throw _scanner.Error(offset, $"'{name}' is not a valid class name.");
        }

        return names;
    }

    internal static bool IsValidClassName(string name) => name.Length > 0 && name.All(IsNameCharacter);

    private static string[] SplitList(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class Scope(string id, string title)
    {
        public string Id { get; } = id;

        public string Title { get; } = title;

        public DiagramDirection? Direction { get; set; }

        public List<string> Members { get; } = [];
    }
}
