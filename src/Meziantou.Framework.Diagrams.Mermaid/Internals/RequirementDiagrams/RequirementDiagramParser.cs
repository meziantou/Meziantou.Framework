namespace Meziantou.Framework.Diagrams.Mermaid.Internals.RequirementDiagrams;

/// <summary>Parses the <c>requirementDiagram</c> diagrams of Mermaid.</summary>
internal sealed class RequirementDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly RequirementDiagram _diagram = new();
    private readonly Dictionary<string, RequirementDiagramNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<(string Source, string Target, RequirementRelationshipKind Kind, int Offset)> _relationships = [];

    private RequirementDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static RequirementDiagram Parse(ParseContext context, int position) => new RequirementDiagramParser(context, position).Parse();

    private RequirementDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("requirementDiagram"))
            throw _scanner.Error(keywordStart, "'requirementDiagram' is expected.");

        ExpectLineEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "requirement");
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        // The relationships can name the nodes before their definition
        foreach (var (source, target, kind, offset) in _relationships)
        {
            if (!_nodes.TryGetValue(source, out var sourceNode))
            {
                _context.Warn(offset, $"The node '{source}' is not defined: the relationship is ignored.");
                continue;
            }

            if (!_nodes.TryGetValue(target, out var targetNode))
            {
                _context.Warn(offset, $"The node '{target}' is not defined: the relationship is ignored.");
                continue;
            }

            _diagram.Relationships.Add(new RequirementRelationship(sourceNode, targetNode, kind));
        }

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (StartsWithKeyword("direction"))
        {
            _scanner.Position += "direction".Length;
            _scanner.SkipSpaces();
            var direction = MermaidCommon.ParseDirection(ReadWord()) ?? throw _scanner.Error(start, "A direction is expected: TB, BT, LR or RL.");
            _diagram.Direction = direction;
            ExpectLineEnd();
            return;
        }

        if (TryReadRequirementKind(out var kind))
        {
            ParseRequirement(start, kind);
            return;
        }

        if (StartsWithKeyword("element"))
        {
            _scanner.Position += "element".Length;
            ParseElement(start);
            return;
        }

        if (StartsWithKeyword("classDef"))
        {
            _scanner.Position += "classDef".Length;
            _scanner.SkipSpaces();
            var names = ReadNameList();
            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            foreach (var className in names)
            {
                ValidateClassName(className, start);
                _diagram.ClassDefinitions[className] = _diagram.ClassDefinitions.TryGetValue(className, out var existing) ? existing.Merge(style) : style;
            }

            return;
        }

        if (StartsWithKeyword("class"))
        {
            _scanner.Position += "class".Length;
            _scanner.SkipSpaces();
            var ids = ReadNameList();
            _scanner.SkipSpaces();
            var classes = ReadNameList();
            if (ids.Count is 0 || classes.Count is 0)
                throw _scanner.Error(start, "The nodes and the class names are expected.");

            ExpectLineEnd();
            ApplyClasses(ids, classes, start);
            return;
        }

        if (StartsWithKeyword("style"))
        {
            _scanner.Position += "style".Length;
            _scanner.SkipSpaces();
            var ids = ReadNameList();
            if (ids.Count is 0)
                throw _scanner.Error(start, "The nodes are expected.");

            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
            foreach (var id in ids)
            {
                if (_nodes.TryGetValue(id, out var node))
                {
                    node.Style = node.Style is null ? style : node.Style.Merge(style);
                }
                else
                {
                    _context.Warn(start, $"The node '{id}' is not defined: its style is ignored.");
                }
            }

            return;
        }

        // A relationship, or classes added to a node: name:::class
        var nodeName = ReadName();
        _scanner.SkipSpaces();
        if (_scanner.TryConsume(":::"))
        {
            var classes = ReadNameList();
            if (classes.Count is 0)
                throw _scanner.Error("A class name is expected after ':::'.");

            ExpectLineEnd();
            ApplyClasses([nodeName], classes, start);
            return;
        }

        if (_scanner.TryConsume("<-"))
        {
            _scanner.SkipSpaces();
            var relationship = ReadRelationshipKind();
            _scanner.SkipSpaces();
            _scanner.Expect("-");
            _scanner.SkipSpaces();
            var source = ReadName();
            ExpectLineEnd();
            AddRelationship(source, nodeName, relationship, start);
            return;
        }

        if (_scanner.Current == '-' && _scanner.Peek() != '>')
        {
            _scanner.Position++;
            _scanner.SkipSpaces();
            var relationship = ReadRelationshipKind();
            _scanner.SkipSpaces();
            _scanner.Expect("->");
            _scanner.SkipSpaces();
            var target = ReadName();
            ExpectLineEnd();
            AddRelationship(nodeName, target, relationship, start);
            return;
        }

        throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a relationship ('- satisfies ->') is expected.");
    }

    private void AddRelationship(string source, string target, RequirementRelationshipKind kind, int offset)
    {
        if (_relationships.Count >= _context.Options.MaxEdges)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxEdges} relationships.");

        _relationships.Add((source, target, kind, offset));
    }

    private void ApplyClasses(List<string> ids, List<string> classes, int offset)
    {
        foreach (var className in classes)
        {
            ValidateClassName(className, offset);
        }

        foreach (var id in ids)
        {
            if (_nodes.TryGetValue(id, out var node))
            {
                foreach (var className in classes)
                {
                    node.CssClasses.Add(className);
                }
            }
            else
            {
                _context.Warn(offset, $"The node '{id}' is not defined: its classes are ignored.");
            }
        }
    }

    private void ParseRequirement(int start, RequirementKind kind)
    {
        _scanner.SkipSpaces();
        var name = ReadName();
        var classes = ReadClassesShorthand();
        _scanner.SkipSpaces();
        _scanner.Expect("{");
        ExpectLineEnd();
        var requirement = new RequirementDiagramRequirement(name) { Kind = kind };
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                throw _scanner.Error(start, $"The requirement '{name}' is not closed by '}}'.");

            if (_scanner.Current == '}')
            {
                _scanner.Position++;
                ExpectLineEnd();
                break;
            }

            var fieldStart = _scanner.Position;
            var field = ReadWord(stopAtColon: true);
            _scanner.SkipSpaces();
            _scanner.Expect(":");
            _scanner.SkipSpaces();
            switch (field.ToUpperInvariant())
            {
                case "ID":
                    requirement.RequirementId = ReadValue()?.Text;
                    break;
                case "TEXT":
                    requirement.Text = ReadValue();
                    break;
                case "RISK":
                    requirement.Risk = ReadWord().ToUpperInvariant() switch
                    {
                        "LOW" => RequirementRisk.Low,
                        "MEDIUM" => RequirementRisk.Medium,
                        "HIGH" => RequirementRisk.High,
                        _ => throw _scanner.Error(fieldStart, "The risk must be low, medium or high."),
                    };
                    ExpectLineEnd();
                    break;
                case "VERIFYMETHOD":
                    requirement.VerificationMethod = ReadWord().ToUpperInvariant() switch
                    {
                        "ANALYSIS" => RequirementVerificationMethod.Analysis,
                        "INSPECTION" => RequirementVerificationMethod.Inspection,
                        "TEST" => RequirementVerificationMethod.Test,
                        "DEMONSTRATION" => RequirementVerificationMethod.Demonstration,
                        _ => throw _scanner.Error(fieldStart, "The verification method must be analysis, inspection, test or demonstration."),
                    };
                    ExpectLineEnd();
                    break;
                default:
                    throw _scanner.Error(fieldStart, $"'{field}' is not a field of a requirement: use id, text, risk or verifymethod.");
            }
        }

        AddNode(requirement, classes, start);
    }

    private void ParseElement(int start)
    {
        _scanner.SkipSpaces();
        var name = ReadName();
        var classes = ReadClassesShorthand();
        _scanner.SkipSpaces();
        _scanner.Expect("{");
        ExpectLineEnd();
        var element = new RequirementDiagramElement(name);
        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                throw _scanner.Error(start, $"The element '{name}' is not closed by '}}'.");

            if (_scanner.Current == '}')
            {
                _scanner.Position++;
                ExpectLineEnd();
                break;
            }

            var fieldStart = _scanner.Position;
            var field = ReadWord(stopAtColon: true);
            _scanner.SkipSpaces();
            _scanner.Expect(":");
            _scanner.SkipSpaces();
            switch (field.ToUpperInvariant())
            {
                case "TYPE":
                    element.Type = ReadValue();
                    break;
                case "DOCREF":
                    element.DocumentReference = ReadValue();
                    break;
                default:
                    throw _scanner.Error(fieldStart, $"'{field}' is not a field of an element: use type or docref.");
            }
        }

        AddNode(element, classes, start);
    }

    /// <summary>Adds a node; as in Mermaid, the first definition of a name wins.</summary>
    private void AddNode(RequirementDiagramNode node, List<string> classes, int offset)
    {
        if (_nodes.TryGetValue(node.Name, out var existing))
        {
            if (existing.GetType() != node.GetType())
                throw _scanner.Error(offset, $"'{node.Name}' is already defined as another kind of node.");

            _context.Warn(offset, $"'{node.Name}' is already defined: this definition is ignored.");
            ApplyClasses([node.Name], classes, offset);
            return;
        }

        if (_nodes.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} nodes.");

        _nodes[node.Name] = node;
        switch (node)
        {
            case RequirementDiagramRequirement requirement:
                _diagram.Requirements.Add(requirement);
                break;
            case RequirementDiagramElement element:
                _diagram.Elements.Add(element);
                break;
        }

        ApplyClasses([node.Name], classes, offset);
    }

    /// <summary>
    /// Reads the value of a field: a quoted text, which is markdown, or a text that starts with a letter, a digit or
    /// <c>_</c>, up to the end of the line or a <c>#</c> comment, without <c>:</c>, <c>,</c>, <c>{</c>, <c>&lt;</c>,
    /// <c>&gt;</c>, <c>-</c> nor <c>=</c>, as in Mermaid.
    /// </summary>
    private DiagramText? ReadValue()
    {
        var start = _scanner.Position;
        if (_scanner.Current == '"')
        {
            var text = _scanner.ReadQuoted();
            if (text.Contains('\n', StringComparison.Ordinal))
                throw _scanner.Error(start, "A value cannot span several lines.");

            ExpectLineEnd();
            return text.Length is 0 ? null : DiagramText.Markdown(text);
        }

        if (!char.IsLetterOrDigit(_scanner.Current) && _scanner.Current != '_')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a value is expected; quote it when it does not start with a letter or a digit.");

        while (!_scanner.IsEnd && _scanner.Current is not ('\n' or '#' or ':' or ',' or '{' or '<' or '>' or '-' or '=') && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        var value = _scanner.Text[start.._scanner.Position].Trim();
        if (Keywords.Contains(FirstWord(value)))
            throw _scanner.Error(start, $"The value cannot start with the keyword '{FirstWord(value)}': quote it.");

        ExpectLineEnd();
        return new DiagramText(value);
    }

    /// <summary>The words that Mermaid reads as keywords, ignoring the case, at the start of a value or a name.</summary>
    internal static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "text", "risk", "verifymethod", "requirement", "functionalRequirement", "interfaceRequirement", "performanceRequirement",
        "physicalRequirement", "designConstraint", "low", "medium", "high", "analysis", "demonstration", "inspection", "test", "element",
        "contains", "copies", "derives", "satisfies", "verifies", "refines", "traces", "type", "docref", "style", "classDef", "class",
        "direction", "title", "requirementDiagram", "accTitle", "accDescr",
    };

    internal static string FirstWord(string value)
    {
        var end = 0;
        while (end < value.Length && (char.IsLetterOrDigit(value[end]) || value[end] == '_'))
        {
            end++;
        }

        return value[..end];
    }

    private bool TryReadRequirementKind(out RequirementKind kind)
    {
        foreach (var (keyword, value) in RequirementKinds)
        {
            if (StartsWithKeyword(keyword))
            {
                _scanner.Position += keyword.Length;
                kind = value;
                return true;
            }
        }

        kind = default;
        return false;
    }

    internal static readonly (string Keyword, RequirementKind Kind)[] RequirementKinds =
    [
        ("requirement", RequirementKind.Requirement),
        ("functionalRequirement", RequirementKind.FunctionalRequirement),
        ("interfaceRequirement", RequirementKind.InterfaceRequirement),
        ("performanceRequirement", RequirementKind.PerformanceRequirement),
        ("physicalRequirement", RequirementKind.PhysicalRequirement),
        ("designConstraint", RequirementKind.DesignConstraint),
    ];

    private RequirementRelationshipKind ReadRelationshipKind()
    {
        var start = _scanner.Position;
        return ReadWord(stopAtDash: true).ToUpperInvariant() switch
        {
            "CONTAINS" => RequirementRelationshipKind.Contains,
            "COPIES" => RequirementRelationshipKind.Copies,
            "DERIVES" => RequirementRelationshipKind.Derives,
            "SATISFIES" => RequirementRelationshipKind.Satisfies,
            "VERIFIES" => RequirementRelationshipKind.Verifies,
            "REFINES" => RequirementRelationshipKind.Refines,
            "TRACES" => RequirementRelationshipKind.Traces,
            _ => throw _scanner.Error(start, "A relationship is expected: contains, copies, derives, satisfies, verifies, refines or traces."),
        };
    }

    /// <summary>Reads a name: a quoted text, or characters up to <c>:</c>, <c>,</c>, <c>{</c>, <c>&lt;</c>, <c>&gt;</c>, <c>-</c>, <c>=</c> or <c>#</c>, trimmed.</summary>
    private string ReadName()
    {
        var start = _scanner.Position;
        if (_scanner.Current == '"')
        {
            var name = _scanner.ReadQuoted();
            if (!IsValidQuotedName(name))
                throw _scanner.Error(start, "A name cannot be empty nor span several lines.");

            return name;
        }

        if (!char.IsLetterOrDigit(_scanner.Current) && _scanner.Current != '_')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a name is expected.");

        while (!_scanner.IsEnd && _scanner.Current is not (':' or ',' or '\n' or '{' or '<' or '>' or '-' or '=' or '#' or '"') && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position].Trim();
    }

    internal static bool IsValidQuotedName(string name) => name.Length > 0 && name.Trim().Length == name.Length && name.AsSpan().IndexOfAny('\n', '"') < 0;

    /// <summary>Reads names separated by commas: quoted, or letters, digits and <c>_</c>.</summary>
    private List<string> ReadNameList()
    {
        var result = new List<string>();
        while (true)
        {
            _scanner.SkipSpaces();
            var start = _scanner.Position;
            if (_scanner.Current == '"')
            {
                var name = _scanner.ReadQuoted();
                if (!IsValidQuotedName(name))
                    throw _scanner.Error(start, "A name cannot be empty nor span several lines.");

                result.Add(name);
            }
            else
            {
                while (!_scanner.IsEnd && (char.IsLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
                {
                    _scanner.Position++;
                }

                if (_scanner.Position == start)
                    return result;

                result.Add(_scanner.Text[start.._scanner.Position]);
            }

            _scanner.SkipSpaces();
            if (!_scanner.TryConsume(","))
                return result;
        }
    }

    private List<string> ReadClassesShorthand()
    {
        _scanner.SkipSpaces();
        if (!_scanner.TryConsume(":::"))
            return [];

        var classes = ReadNameList();
        if (classes.Count is 0)
            throw _scanner.Error("A class name is expected after ':::'.");

        return classes;
    }

    private void ValidateClassName(string name, int offset)
    {
        if (!IsValidClassName(name))
            throw _scanner.Error(offset, $"'{name}' is not a valid class name.");
    }

    internal static bool IsValidClassName(string name) => name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>Indicates whether a keyword starts here, ignoring the case, followed by a character that is not a letter, a digit or <c>_</c>.</summary>
    private bool StartsWithKeyword(string keyword)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        if (position + keyword.Length > text.Length || string.Compare(text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var end = position + keyword.Length;
        return end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_');
    }

    private string ReadWord(bool stopAtColon = false, bool stopAtDash = false)
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && !char.IsWhiteSpace(_scanner.Current) && _scanner.Current != ';' && !(stopAtColon && _scanner.Current == ':') && !(stopAtDash && _scanner.Current == '-'))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    /// <summary>Expects the end of the line, after spaces and a <c>#</c> or <c>%%</c> comment.</summary>
    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (_scanner.Current == '#')
        {
            while (!_scanner.IsEnd && _scanner.Current != '\n')
            {
                _scanner.Position++;
            }
        }

        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }
}
