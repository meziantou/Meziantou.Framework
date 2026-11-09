using System.Text;
using System.Text.Json;

namespace Meziantou.Framework.Mermaid.Internals.UseCaseDiagrams;

/// <summary>
/// Parses the <c>usecase-beta</c> diagrams of Mermaid. The statements can use an element before its declaration, so the
/// parser collects them and builds the diagram at the end: an element that is only used is an ellipse use case.
/// </summary>
internal sealed class UseCaseDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly UseCaseDiagram _diagram = new();
    private readonly Dictionary<string, ElementDraft> _elements = new(StringComparer.Ordinal);
    private readonly List<ElementDraft> _elementOrder = [];
    private readonly Dictionary<string, BoundaryDraft> _boundaries = new(StringComparer.Ordinal);
    private readonly List<RelationshipDraft> _relationships = [];
    private readonly Dictionary<string, RelationshipDraft> _edgeIds = new(StringComparer.Ordinal);
    private readonly List<(string Target, DiagramText Text, int Offset)> _notes = [];
    private readonly List<(string Target, Dictionary<string, object?> Values, int Offset, int Mention)> _metadata = [];
    private readonly List<(List<string> Ids, List<string> Classes, int Offset)> _classAssignments = [];
    private readonly List<(List<string> Ids, DiagramStyle Style, int Offset)> _styleAssignments = [];

    private int _mentions;

    private UseCaseDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static UseCaseDiagram Parse(ParseContext context, int position) => new UseCaseDiagramParser(context, position).Parse();

    private UseCaseDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("usecase-beta"))
            throw _scanner.Error(keywordStart, "'usecase-beta' is expected.");

        ExpectLineEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "usecase");
        if (_context.Configuration.GetNumber("usecase", "nodeSpacing") is >= 0 and <= 10_000 and var nodeSpacing)
        {
            _diagram.NodeSpacing = nodeSpacing;
        }

        if (_context.Configuration.GetNumber("usecase", "rankSpacing") is >= 0 and <= 10_000 and var rankSpacing)
        {
            _diagram.RankSpacing = rankSpacing;
        }

        while (true)
        {
            SkipBlankLines();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        Build();
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
            var word = ReadIdentifier(allowKeyword: true) ?? string.Empty;
            _diagram.Direction = word is "TB" or "TD" or "BT" or "LR" or "RL" ? MermaidCommon.ParseDirection(word)!.Value : throw _scanner.Error(start, "A direction is expected: TB, TD, BT, LR or RL.");
            ExpectLineEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("actor"))
        {
            _scanner.Position += "actor".Length;
            ParseActorStatement(boundary: null);
            return;
        }

        if (_scanner.StartsWithKeyword("systemBoundary"))
        {
            ParseBoundary();
            return;
        }

        if (_scanner.StartsWithKeyword("note"))
        {
            ParseNote();
            return;
        }

        if (_scanner.StartsWithKeyword("json"))
        {
            ParseJson();
            return;
        }

        if (_scanner.StartsWithKeyword("classDef"))
        {
            _scanner.Position += "classDef".Length;
            _scanner.SkipSpaces();
            var names = ReadIdentifierList();
            if (names.Count is 0)
                throw _scanner.Error(start, "A class name is expected.");

            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(ReadRestOfLine(), _context, start);
            foreach (var name in names)
            {
                _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
            }

            return;
        }

        if (_scanner.StartsWithKeyword("class"))
        {
            _scanner.Position += "class".Length;
            _scanner.SkipSpaces();
            var ids = ReadIdentifierList();
            _scanner.SkipSpaces();
            var classes = ReadIdentifierList();
            if (ids.Count is 0 || classes.Count is 0)
                throw _scanner.Error(start, "The elements and the class names are expected.");

            ExpectLineEnd();
            _classAssignments.Add((ids, classes, start));
            return;
        }

        if (_scanner.StartsWithKeyword("style"))
        {
            _scanner.Position += "style".Length;
            _scanner.SkipSpaces();
            var ids = ReadIdentifierList();
            if (ids.Count is 0)
                throw _scanner.Error(start, "The elements are expected.");

            _scanner.SkipSpaces();
            var style = MermaidStyle.Parse(ReadRestOfLine(), _context, start);
            _styleAssignments.Add((ids, style, start));
            return;
        }

        // Metadata of an element, a boundary or a relationship: id@{ ... }
        if (TryPeekIdentifier(out var identifierEnd) && identifierEnd + 1 < _scanner.Text.Length && _scanner.Text[identifierEnd] == '@' && _scanner.Text[identifierEnd + 1] == '{' && IsMetadataStatement(identifierEnd))
        {
            var id = ReadIdentifier()!;
            _scanner.Position++;
            _metadata.Add((id, MermaidCommon.ReadInlineMapping(_scanner), start, _mentions++));
            ExpectLineEnd();
            return;
        }

        var source = ParseEntity(boundary: null, isActor: false);
        _scanner.SkipSpaces();
        if (!IsAtLineEnd())
        {
            ParseRelationTail(source, start);
        }

        ExpectLineEnd();
    }

    /// <summary>Indicates whether <c>id@{...}</c> is alone on its line: a metadata statement, not the metadata of a use case followed by more.</summary>
    private bool IsMetadataStatement(int identifierEnd)
    {
        var saved = _scanner.Position;
        try
        {
            _scanner.Position = identifierEnd + 1;
            MermaidCommon.ReadInlineMapping(_scanner);
            return IsAtLineEnd();
        }
        catch (MermaidParseException)
        {
            return true;
        }
        finally
        {
            _scanner.Position = saved;
        }
    }

    private void ParseActorStatement(BoundaryDraft? boundary)
    {
        _scanner.SkipSpaces();
        var start = _scanner.Position;
        var first = ParseEntity(boundary, isActor: true);
        _scanner.SkipSpaces();
        if (_scanner.Current == ',')
        {
            while (_scanner.TryConsume(","))
            {
                _scanner.SkipSpaces();
                ParseEntity(boundary, isActor: true);
                _scanner.SkipSpaces();
            }
        }
        else if (boundary is null && !IsAtLineEnd())
        {
            ParseRelationTail(first, start);
        }

        ExpectLineEnd();
    }

    /// <summary>
    /// Parses an actor or a use case: <c>id</c>, <c>id(label)</c>, <c>id[label]</c> (use cases only) or a quoted label,
    /// then metadata, a stereotype and classes. A use case without label nor metadata is only a reference.
    /// </summary>
    private string ParseEntity(BoundaryDraft? boundary, bool isActor)
    {
        var start = _scanner.Position;
        string id;
        DiagramText? label = null;
        UseCaseShape? shape = null;
        if (_scanner.Current == '"' || _scanner.Current == '\'')
        {
            var (raw, text) = ReadString();
            id = DeriveIdentifier(raw);
            label = text;
            if (!IsIdentifier(id))
                throw _scanner.Error(start, $"The label is empty or its identifier, '{id}', is a keyword.");
        }
        else
        {
            id = ReadIdentifier() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an identifier is expected.");
            var afterId = _scanner.Position;
            _scanner.SkipSpaces();
            if (_scanner.Current == '(' || (!isActor && _scanner.Current == '['))
            {
                shape = _scanner.Current == '(' ? UseCaseShape.Ellipse : UseCaseShape.Rectangle;
                label = ReadDelimitedLabel();
            }
            else
            {
                _scanner.Position = afterId;
            }
        }

        _scanner.SkipSpaces();
        Dictionary<string, object?>? metadata = null;
        var metadataStart = _scanner.Position;
        if (_scanner.StartsWith("@{"))
        {
            _scanner.Position++;
            metadata = MermaidCommon.ReadInlineMapping(_scanner);
            _scanner.SkipSpaces();
        }

        string? stereotype = null;
        if (_scanner.StartsWith("<<") && !_scanner.StartsWith("<--"))
        {
            var stereotypeStart = _scanner.Position;
            var end = _scanner.Text.IndexOf(">>", _scanner.Position + 2, StringComparison.Ordinal);
            var lineEnd = _scanner.Text.IndexOfOrdinal('\n', _scanner.Position);
            if (end < 0 || (lineEnd >= 0 && lineEnd < end))
                throw _scanner.Error(stereotypeStart, "The stereotype is not closed by '>>'.");

            stereotype = _scanner.Text[(_scanner.Position + 2)..end].Trim();
            if (stereotype.Length is 0)
                throw _scanner.Error(stereotypeStart, "The stereotype is empty.");

            _scanner.Position = end + 2;
            _scanner.SkipSpaces();
        }

        var classes = ReadClasses();
        var isDeclaration = isActor || boundary is not null || label is not null || metadata is not null || stereotype is not null || classes.Count > 0;
        var draft = GetElement(id, start);
        if (isDeclaration)
        {
            var kind = isActor ? ElementKind.Actor : ElementKind.UseCase;
            if (draft.Kind is { } existing && existing != kind)
                throw _scanner.Error(start, $"'{id}' is already declared as {Describe(existing)}.");

            draft.Kind = kind;
            if (label is not null)
            {
                draft.Label = label;
            }

            if (shape is not null && !isActor)
            {
                draft.Shape = shape.Value;
            }

            if (stereotype is not null)
            {
                draft.Stereotype = stereotype;
            }

            draft.Classes.AddRange(classes);
            if (metadata is not null)
            {
                _metadata.Add((id, metadata, metadataStart, _mentions++));
            }
        }

        if (boundary is not null)
        {
            if (draft.Boundary is not null && draft.Boundary != boundary)
                throw _scanner.Error(start, $"'{id}' already belongs to the boundary '{draft.Boundary.Id}'.");

            if (draft.Boundary is null)
            {
                draft.Boundary = boundary;
                boundary.Members.Add(draft);
            }
        }

        return id;
    }

    /// <summary>Parses the end of a relationship: an optional <c>id@</c>, an operator, and the target.</summary>
    private void ParseRelationTail(string source, int start)
    {
        string? edgeId = null;
        if (TryPeekIdentifier(out var identifierEnd) && identifierEnd < _scanner.Text.Length && _scanner.Text[identifierEnd] == '@' && (identifierEnd + 1 >= _scanner.Text.Length || _scanner.Text[identifierEnd + 1] != '{'))
        {
            edgeId = ReadIdentifier();
            _scanner.Position++;
        }

        var draft = new RelationshipDraft { Source = source, Id = edgeId, Offset = start };
        ReadOperator(draft);
        _scanner.SkipSpaces();
        draft.Target = ParseEntity(boundary: null, isActor: false);
        if (edgeId is not null)
        {
            if (_edgeIds.ContainsKey(edgeId))
                throw _scanner.Error(start, $"The relationship '{edgeId}' is already defined.");

            _edgeIds[edgeId] = draft;
        }

        if (_relationships.Count >= _context.Options.MaxEdges)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxEdges} relationships.");

        _relationships.Add(draft);
    }

    private void ReadOperator(RelationshipDraft draft)
    {
        var start = _scanner.Position;
        if (_scanner.TryConsume("..>"))
        {
            _scanner.SkipSpaces();
            _scanner.Expect(":");
            _scanner.SkipSpaces();
            var word = ReadIdentifier(allowKeyword: true) ?? string.Empty;
            draft.Kind = word.ToUpperInvariant() switch
            {
                "INCLUDE" => UseCaseRelationshipKind.Include,
                "EXTEND" => UseCaseRelationshipKind.Extend,
                _ => throw _scanner.Error(start, "'include' or 'extend' is expected after '..> :'."),
            };
            return;
        }

        if (_scanner.TryConsume("--|>"))
        {
            draft.Kind = UseCaseRelationshipKind.Generalization;
            return;
        }

        if (_scanner.StartsWith("<--"))
        {
            _scanner.Position++;
            var dashes = ReadDashes();
            draft.FromHead = UseCaseArrowHead.Arrow;
            draft.MinLength = dashes - 1;
            ReadLabelAndClosingLine(draft, allowArrowHead: false);
            return;
        }

        if (_scanner.StartsWith("o--") || _scanner.StartsWith("x--"))
        {
            draft.FromHead = _scanner.Current == 'o' ? UseCaseArrowHead.Circle : UseCaseArrowHead.Cross;
            _scanner.Position += 3;
            ReadLabelAndClosingLine(draft, allowArrowHead: false);
            draft.MinLength = 1;
            return;
        }

        if (!_scanner.StartsWith("--"))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a relationship ('-->', '..> : include'...) is expected.");

        var count = ReadDashes();
        if (_scanner.Current == '>')
        {
            _scanner.Position++;
            draft.ToHead = UseCaseArrowHead.Arrow;
            draft.MinLength = count - 1;
            return;
        }

        if (count == 2 && _scanner.Current is 'o' or 'x')
        {
            draft.ToHead = _scanner.Current == 'o' ? UseCaseArrowHead.Circle : UseCaseArrowHead.Cross;
            _scanner.Position++;
            return;
        }

        draft.MinLength = count - 1;
        ReadLabelAndClosingLine(draft, allowArrowHead: true);
    }

    /// <summary>Reads the label of an association and the line after it, when the rest of the line has one: <c>-- label --&gt;</c>.</summary>
    private void ReadLabelAndClosingLine(RelationshipDraft draft, bool allowArrowHead)
    {
        var operatorIndex = FindNextLine();
        if (operatorIndex < 0)
            return;

        while (_scanner.Current is ' ' or '\t')
        {
            _scanner.Position++;
        }

        var labelStart = _scanner.Position;
        if (_scanner.Current is '"' or '\'' && SkipString() <= operatorIndex && IsBlank(SkipString(), operatorIndex))
        {
            draft.Label = ReadString().Text;
        }
        else
        {
            var raw = _scanner.Text[labelStart..operatorIndex].Trim();
            if (raw.Length is 0)
                throw _scanner.Error(labelStart, "The label of the relationship is empty.");

            ValidateUnquotedLabel(raw, labelStart);
            draft.Label = new DiagramText(MermaidText.DecodeEntities(raw));
        }

        _scanner.Position = operatorIndex;
        var dashes = ReadDashes();
        draft.MinLength = Math.Max(draft.MinLength, dashes - 1);
        if (_scanner.Current == '>')
        {
            if (!allowArrowHead)
                throw _scanner.Error("The relationship has an arrowhead at both ends.");

            _scanner.Position++;
            draft.ToHead = UseCaseArrowHead.Arrow;
        }
        else if (dashes == 2 && _scanner.Current is 'o' or 'x' && allowArrowHead)
        {
            draft.ToHead = _scanner.Current == 'o' ? UseCaseArrowHead.Circle : UseCaseArrowHead.Cross;
            draft.MinLength = 1;
            _scanner.Position++;
        }
    }

    /// <summary>Finds the next <c>--</c> on the line, outside of the strings, or -1.</summary>
    private int FindNextLine()
    {
        var text = _scanner.Text;
        var i = _scanner.Position;
        while (i < text.Length && text[i] != '\n')
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                var end = StringEnd(i);
                if (end < 0)
                    return -1;

                i = end;
                continue;
            }

            if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                return i;

            i++;
        }

        return -1;
    }

    private int SkipString() => StringEnd(_scanner.Position);

    /// <summary>Returns the position after the string that starts at a position, or -1 when it is not closed.</summary>
    private int StringEnd(int position)
    {
        var text = _scanner.Text;
        if (text[position] == '"' && position + 1 < text.Length && text[position + 1] == '`')
        {
            var close = text.IndexOf("`\"", position + 2, StringComparison.Ordinal);
            return close < 0 ? -1 : close + 2;
        }

        var quote = text[position];
        for (var i = position + 1; i < text.Length; i++)
        {
            if (text[i] == quote)
                return i + 1;

            if (text[i] == '\n')
                return -1;
        }

        return -1;
    }

    private bool IsBlank(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (_scanner.Text[i] is not (' ' or '\t'))
                return false;
        }

        return true;
    }

    private int ReadDashes()
    {
        var count = 0;
        while (_scanner.Current == '-')
        {
            _scanner.Position++;
            count++;
        }

        if (count > 101)
            throw _scanner.Error("The relationship is too long.");

        return count;
    }

    private void ParseBoundary()
    {
        var start = _scanner.Position;
        _scanner.Position += "systemBoundary".Length;
        _scanner.SkipSpaces();
        string id;
        DiagramText? title = null;
        if (_scanner.Current is '"' or '\'')
        {
            var (raw, text) = ReadString();
            id = DeriveIdentifier(raw);
            title = text;
        }
        else
        {
            id = ReadIdentifier() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the identifier of the boundary is expected.");
            _scanner.SkipSpaces();
            if (_scanner.Current is '(' or '[')
            {
                title = ReadDelimitedLabel();
            }
        }

        if (!IsIdentifier(id))
            throw _scanner.Error(start, $"The title of the boundary is empty or its identifier, '{id}', is a keyword.");

        if (_boundaries.ContainsKey(id))
            throw _scanner.Error(start, $"The boundary '{id}' is already defined.");

        if (_boundaries.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} boundaries.");

        var boundary = new BoundaryDraft(id) { Title = title, Offset = start };
        _boundaries[id] = boundary;
        _scanner.SkipSpaces();
        if (_scanner.StartsWith("@{"))
        {
            var metadataStart = _scanner.Position;
            _scanner.Position++;
            _metadata.Add((id, MermaidCommon.ReadInlineMapping(_scanner), metadataStart, _mentions++));
            _scanner.SkipSpaces();
        }

        boundary.Classes.AddRange(ReadClasses());
        ExpectLineEnd();
        while (true)
        {
            SkipBlankLines();
            if (_scanner.IsEnd)
                throw _scanner.Error(start, $"The boundary '{id}' is not closed by 'end'.");

            if (_scanner.StartsWithKeyword("end"))
            {
                _scanner.Position += "end".Length;
                ExpectLineEnd();
                return;
            }

            if (_scanner.StartsWithKeyword("actor"))
            {
                _scanner.Position += "actor".Length;
                ParseActorStatement(boundary);
                continue;
            }

            if (_scanner.StartsWithKeyword("systemBoundary"))
                throw _scanner.Error("The boundaries cannot be nested.");

            ParseEntity(boundary, isActor: false);
            ExpectLineEnd();
        }
    }

    private void ParseNote()
    {
        var start = _scanner.Position;
        _scanner.Position += "note".Length;
        _scanner.SkipSpaces();
        if (!_scanner.StartsWithKeyword("for"))
            throw _scanner.Error("'for' is expected after 'note'.");

        _scanner.Position += "for".Length;
        _scanner.SkipSpaces();
        var target = ReadIdentifier() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the target of the note is expected.");
        _scanner.SkipSpaces();
        DiagramText text;
        if (_scanner.Current is '"' or '\'')
        {
            text = ReadString().Text;
        }
        else
        {
            var labelStart = _scanner.Position;
            var raw = ReadRestOfLine().Trim();
            if (raw.Length is 0)
                throw _scanner.Error(labelStart, "The text of the note is expected.");

            text = new DiagramText(MermaidText.DecodeEntities(raw));
        }

        ExpectLineEnd();
        _notes.Add((target, text, start));
    }

    private void ParseJson()
    {
        var start = _scanner.Position;
        _scanner.Position += "json".Length;
        _scanner.SkipSpaces();
        var id = ReadIdentifier() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the identifier of the table is expected.");
        _scanner.SkipSpaces();
        _scanner.Expect("@");
        _scanner.SkipSpaces();
        if (_scanner.Current != '{')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a JSON object is expected.");

        var jsonStart = _scanner.Position;
        var end = FindJsonEnd(jsonStart);
        if (end < 0)
            throw _scanner.Error(jsonStart, "The JSON object is not closed by '}'.");

        var json = _scanner.Text[jsonStart..end];
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = Math.Min(64, _context.Options.MaxDepth) });
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw _scanner.Error(jsonStart, "The JSON object is not valid: " + exception.Message);
        }

        _scanner.Position = end;
        _scanner.SkipSpaces();
        var classes = ReadClasses();
        ExpectLineEnd();
        var draft = GetElement(id, start);
        if (draft.Kind is { } existing && existing is not ElementKind.Json)
            throw _scanner.Error(start, $"'{id}' is already declared as {Describe(existing)}.");

        if (draft.Kind is ElementKind.Json)
            throw _scanner.Error(start, $"The table '{id}' is already defined.");

        draft.Kind = ElementKind.Json;
        draft.Json = json;
        draft.Classes.AddRange(classes);
    }

    private int FindJsonEnd(int start)
    {
        var text = _scanner.Text;
        var depth = 0;
        var quoted = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    quoted = false;
                }

                continue;
            }

            if (c == '"')
            {
                quoted = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// <summary>Builds the diagram from the statements: the elements, the boundaries, the metadata, the relationships, the notes and the styles.</summary>
    private void Build()
    {
        foreach (var boundary in _boundaries.Values)
        {
            if (_elements.ContainsKey(boundary.Id))
                throw _context.Error(boundary.Offset, $"'{boundary.Id}' is the identifier of a boundary and of an element.");
        }

        foreach (var edgeId in _edgeIds.Keys)
        {
            if (_elements.ContainsKey(edgeId) || _boundaries.ContainsKey(edgeId))
                throw _context.Error(_edgeIds[edgeId].Offset, $"'{edgeId}' is the identifier of a relationship and of another element.");
        }

        // A metadata statement names an element too: it can come before the other statements that name it
        foreach (var (target, _, _, mention) in _metadata)
        {
            if (_elements.TryGetValue(target, out var named) && mention < named.FirstMention)
            {
                named.FirstMention = mention;
            }
        }

        _elementOrder.Sort((a, b) => a.FirstMention.CompareTo(b.FirstMention));
        foreach (var draft in _elementOrder)
        {
            if (draft.Boundary is not null && draft.Kind is ElementKind.Json)
                throw _context.Error(draft.Offset, $"The table '{draft.Id}' cannot be in a boundary.");

            draft.Element = draft.Kind switch
            {
                ElementKind.Actor => new UseCaseActor(draft.Id, draft.Label) { Stereotype = draft.Stereotype },
                ElementKind.Json => new UseCaseJsonTable(draft.Id, draft.Json!),
                _ => new UseCase(draft.Id, draft.Label) { Shape = draft.Shape, Stereotype = draft.Stereotype },
            };
            foreach (var className in draft.Classes)
            {
                draft.Element.CssClasses.Add(className);
            }

            switch (draft.Element)
            {
                case UseCaseActor actor:
                    _diagram.Actors.Add(actor);
                    break;
                case UseCase useCase:
                    _diagram.UseCases.Add(useCase);
                    break;
                case UseCaseJsonTable table:
                    _diagram.Tables.Add(table);
                    break;
            }
        }

        foreach (var boundary in _boundaries.Values)
        {
            var result = new UseCaseBoundary(boundary.Id, boundary.Title);
            foreach (var className in boundary.Classes)
            {
                result.CssClasses.Add(className);
            }

            foreach (var member in boundary.Members)
            {
                result.Elements.Add(member.Element!);
            }

            boundary.Result = result;
            _diagram.Boundaries.Add(result);
        }

        foreach (var (target, values, offset, _) in _metadata)
        {
            ApplyMetadata(target, values, offset);
        }

        foreach (var draft in _elementOrder)
        {
            switch (draft.Element)
            {
                case UseCaseActor { IsBusiness: true, Kind: UseCaseActorKind.Awesome }:
                    throw _context.Error(draft.Offset, $"The business actor '{draft.Id}' must be a normal or a hollow actor.");
                case UseCase { IsBusiness: true, Shape: UseCaseShape.Rectangle }:
                    throw _context.Error(draft.Offset, $"The rectangular use case '{draft.Id}' cannot be a business use case.");
            }
        }

        foreach (var draft in _relationships)
        {
            var from = Resolve(draft.Source, draft.Offset);
            var to = Resolve(draft.Target!, draft.Offset);
            switch (draft.Kind)
            {
                case UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend when from is not UseCase || to is not UseCase:
                    throw _context.Error(draft.Offset, $"An {(draft.Kind is UseCaseRelationshipKind.Include ? "include" : "extend")} relationship links two use cases.");
                case UseCaseRelationshipKind.Generalization when !((from is UseCaseActor && to is UseCaseActor) || (from is UseCase && to is UseCase)):
                    throw _context.Error(draft.Offset, "A generalization links two actors or two use cases.");
                case UseCaseRelationshipKind.Association when (from is UseCaseJsonTable || to is UseCaseJsonTable) && (draft.FromHead is UseCaseArrowHead.Circle or UseCaseArrowHead.Cross || draft.ToHead is UseCaseArrowHead.Circle or UseCaseArrowHead.Cross):
                    throw _context.Error(draft.Offset, "A table can be linked only by an association with an arrowhead or none.");
                case not UseCaseRelationshipKind.Association when from is UseCaseJsonTable || to is UseCaseJsonTable:
                    throw _context.Error(draft.Offset, "A table can be linked only by an association.");
            }

            var relationship = new UseCaseRelationship(from, to, draft.Kind)
            {
                FromArrowHead = draft.FromHead,
                ToArrowHead = draft.ToHead,
                Label = draft.Kind is UseCaseRelationshipKind.Association ? draft.Label : null,
                MinLength = Math.Max(1, draft.MinLength),
                Id = draft.Id,
            };
            draft.Result = relationship;
            _diagram.Relationships.Add(relationship);
        }

        foreach (var (target, text, offset) in _notes)
        {
            if (!_elements.TryGetValue(target, out var draft) || draft.Element is not (UseCaseActor or UseCase))
                throw _context.Error(offset, $"The target of the note, '{target}', is not an actor nor a use case.");

            _diagram.Notes.Add(new UseCaseNote(draft.Element, text));
        }

        foreach (var (ids, classes, offset) in _classAssignments)
        {
            foreach (var id in ids)
            {
                var list = ResolveStyled(id, offset).Classes;
                foreach (var className in classes)
                {
                    list.Add(className);
                }
            }
        }

        foreach (var (ids, style, offset) in _styleAssignments)
        {
            foreach (var id in ids)
            {
                var (_, current, set) = ResolveStyled(id, offset);
                set(current is null ? style : current.Merge(style));
            }
        }
    }

    private (IList<string> Classes, DiagramStyle? Style, Action<DiagramStyle> SetStyle) ResolveStyled(string id, int offset)
    {
        if (_elements.TryGetValue(id, out var element))
        {
            var target = element.Element!;
            return (target.CssClasses, target.Style, style => target.Style = style);
        }

        if (_boundaries.TryGetValue(id, out var boundary))
        {
            var target = boundary.Result!;
            return (target.CssClasses, target.Style, style => target.Style = style);
        }

        if (_edgeIds.TryGetValue(id, out var edge))
        {
            var target = edge.Result!;
            return (target.CssClasses, target.Style, style => target.Style = style);
        }

        throw _context.Error(offset, $"The class or style target '{id}' is not defined.");
    }

    private UseCaseDiagramElement Resolve(string id, int offset)
    {
        if (_boundaries.ContainsKey(id))
            throw _context.Error(offset, $"'{id}' is a boundary and cannot be linked.");

        return _elements[id].Element!;
    }

    private void ApplyMetadata(string target, Dictionary<string, object?> values, int offset)
    {
        if (_boundaries.TryGetValue(target, out var boundary))
        {
            foreach (var (key, value) in values)
            {
                if (key != "type")
                    throw _context.Error(offset, $"The metadata property '{key}' is not valid for a boundary: use type.");

                boundary.Result!.Kind = ToString(value) switch
                {
                    "rect" => UseCaseBoundaryKind.Rectangle,
                    "package" => UseCaseBoundaryKind.Package,
                    _ => throw _context.Error(offset, "The type of a boundary must be rect or package."),
                };
            }

            return;
        }

        if (_edgeIds.ContainsKey(target))
        {
            foreach (var key in values.Keys)
            {
                if (key is not ("animate" or "animation"))
                    throw _context.Error(offset, $"The metadata property '{key}' is not valid for a relationship: use animate or animation.");
            }

            _context.Warn(offset, "The animation of the relationships is not supported: it is ignored.");
            return;
        }

        if (!_elements.TryGetValue(target, out var draft))
            throw _context.Error(offset, $"The metadata target '{target}' is not defined.");

        switch (draft.Element)
        {
            case UseCaseActor actor:
                var hasIcon = false;
                foreach (var (key, value) in values)
                {
                    switch (key)
                    {
                        case "type":
                            actor.Kind = ToString(value) switch
                            {
                                "normal" => UseCaseActorKind.Normal,
                                "hollow" => UseCaseActorKind.Hollow,
                                "awesome" => UseCaseActorKind.Awesome,
                                _ => throw _context.Error(offset, "The type of an actor must be normal, hollow or awesome."),
                            };
                            break;
                        case "business":
                            actor.IsBusiness = ToBoolean(value, offset);
                            break;
                        case "icon":
                            hasIcon = true;
                            _context.Warn(offset, "The icons of the actors are not supported: the actor is drawn as a stick figure.");
                            break;
                        default:
                            throw _context.Error(offset, $"The metadata property '{key}' is not valid for an actor: use type, icon or business.");
                    }
                }

                if (hasIcon && (actor.Kind is not UseCaseActorKind.Normal || actor.IsBusiness))
                    throw _context.Error(offset, $"The actor '{target}' cannot combine an icon with a type or business.");

                break;
            case UseCase useCase:
                foreach (var (key, value) in values)
                {
                    if (key != "business")
                        throw _context.Error(offset, $"The metadata property '{key}' is not valid for a use case: use business.");

                    useCase.IsBusiness = ToBoolean(value, offset);
                }

                break;
            default:
                throw _context.Error(offset, $"The table '{target}' has no metadata.");
        }
    }

    private static string? ToString(object? value) => value switch
    {
        string text => text,
        bool boolean => boolean ? "true" : "false",
        _ => null,
    };

    private bool ToBoolean(object? value, int offset) => value switch
    {
        bool boolean => boolean,
        "true" => true,
        "false" => false,
        _ => throw _context.Error(offset, "A boolean is expected: true or false."),
    };

    private ElementDraft GetElement(string id, int offset)
    {
        if (_elements.TryGetValue(id, out var draft))
            return draft;

        if (_elements.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} elements.");

        draft = new ElementDraft(id) { Offset = offset, FirstMention = _mentions++ };
        _elements[id] = draft;
        _elementOrder.Add(draft);
        return draft;
    }

    /// <summary>Reads a plain string (<c>"text"</c> or <c>'text'</c>) or a markdown string (<c>"`text`"</c>).</summary>
    private (string Raw, DiagramText Text) ReadString()
    {
        var start = _scanner.Position;
        var end = StringEnd(start);
        if (end < 0)
            throw _scanner.Error(start, "The string is not closed.");

        _scanner.Position = end;
        if (_scanner.Text[start] == '"' && _scanner.Text[start + 1] == '`')
        {
            var raw = _scanner.Text[(start + 2)..(end - 2)];
            var lines = raw.Split('\n').Select(line => line.Trim());
            return (raw, DiagramText.Markdown(MermaidText.DecodeEntities(string.Join('\n', lines))));
        }

        var content = _scanner.Text[(start + 1)..(end - 1)];
        return (content, new DiagramText(MermaidText.DecodePlain(content)));
    }

    /// <summary>Reads a label between parentheses or brackets: a string, or text up to the closing delimiter.</summary>
    private DiagramText ReadDelimitedLabel()
    {
        var open = _scanner.Current;
        var close = open == '(' ? ')' : ']';
        var start = _scanner.Position;
        _scanner.Position++;
        _scanner.SkipSpaces();
        DiagramText label;
        if (_scanner.Current is '"' or '\'')
        {
            label = ReadString().Text;
            _scanner.SkipSpaces();
        }
        else
        {
            var labelStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current != close && _scanner.Current != '\n')
            {
                _scanner.Position++;
            }

            var raw = _scanner.Text[labelStart.._scanner.Position].Trim();
            if (raw.Length is 0)
                throw _scanner.Error(start, "The label is empty.");

            ValidateUnquotedLabel(raw, labelStart);
            label = new DiagramText(MermaidText.DecodeEntities(raw));
        }

        if (_scanner.Current != close)
            throw _scanner.Error(start, $"The label is not closed by '{close}'.");

        _scanner.Position++;
        return label;
    }

    private void ValidateUnquotedLabel(string raw, int offset)
    {
        if (!IsValidUnquotedLabel(raw))
            throw _scanner.Error(offset, $"The label '{raw}' must be quoted: it contains a reserved character or sequence.");
    }

    /// <summary>Indicates whether a label can be written without quotes: it has none of the delimiters and the operators of the syntax.</summary>
    internal static bool IsValidUnquotedLabel(string raw)
    {
        if (raw.Length is 0 || raw.Trim().Length != raw.Length || raw.AsSpan().IndexOfAny("()[]{}\"'\n\r") >= 0)
            return false;

        return !(raw.Contains("--", StringComparison.Ordinal) || raw.Contains("..>", StringComparison.Ordinal) || raw.Contains(":::", StringComparison.Ordinal)
            || raw.Contains("@{", StringComparison.Ordinal) || raw.Contains("<<", StringComparison.Ordinal) || raw.Contains("%%", StringComparison.Ordinal));
    }

    /// <summary>Returns the identifier of an element declared by its label: each character that is not a letter, a digit or <c>_</c> becomes <c>_</c>.</summary>
    internal static string DeriveIdentifier(string label)
    {
        var builder = new StringBuilder(label.Length);
        foreach (var c in label)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    private List<string> ReadClasses()
    {
        if (!_scanner.TryConsume(":::"))
            return [];

        var start = _scanner.Position;
        var classes = ReadIdentifierList(allowDash: true);
        if (classes.Count is 0)
            throw _scanner.Error(start, "A class name is expected after ':::'.");

        _scanner.SkipSpaces();
        return classes;
    }

    private List<string> ReadIdentifierList(bool allowDash = false)
    {
        var result = new List<string>();
        while (true)
        {
            _scanner.SkipSpaces();
            var start = _scanner.Position;
            while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_' || (_scanner.Current == '-' && (allowDash || _scanner.Position > start))))
            {
                _scanner.Position++;
            }

            if (_scanner.Position == start)
                return result;

            result.Add(_scanner.Text[start.._scanner.Position]);
            if (!_scanner.TryConsume(","))
                return result;
        }
    }

    /// <summary>Indicates whether a word can be an identifier: letters, digits and <c>_</c>, and not a keyword of the syntax.</summary>
    internal static bool IsIdentifier(string value) => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !IsKeyword(value);

    private static bool IsKeyword(string value) => value is "actor" or "systemBoundary" or "end" or "direction" or "note" or "for" or "json" or "classDef" or "class" or "style" or "true" or "false" or "TD" or "TB" or "BT" or "LR" or "RL"
        || string.Equals(value, "include", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "extend", StringComparison.OrdinalIgnoreCase);

    internal static bool IsClassName(string value) => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private bool TryPeekIdentifier(out int end)
    {
        end = _scanner.Position;
        while (end < _scanner.Text.Length && (char.IsAsciiLetterOrDigit(_scanner.Text[end]) || _scanner.Text[end] == '_'))
        {
            end++;
        }

        return end > _scanner.Position;
    }

    private string? ReadIdentifier(bool allowKeyword = false)
    {
        if (!TryPeekIdentifier(out var end))
            return null;

        var result = _scanner.Text[_scanner.Position..end];
        if (IsKeyword(result) && !allowKeyword)
            throw _scanner.Error($"'{result}' is a keyword and cannot be an identifier.");

        _scanner.Position = end;
        return result;
    }

    private string ReadRestOfLine()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && _scanner.Current != '\n')
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private bool IsAtLineEnd()
    {
        var p = _scanner.Position;
        while (p < _scanner.Text.Length && _scanner.Text[p] is ' ' or '\t')
        {
            p++;
        }

        return p >= _scanner.Text.Length || _scanner.Text[p] == '\n';
    }

    /// <summary>Expects the end of the line: a statement is alone on its line, and a semicolon is not a separator.</summary>
    private void ExpectLineEnd()
    {
        while (!_scanner.IsEnd && _scanner.Current is ' ' or '\t')
        {
            _scanner.Position++;
        }

        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a statement is alone on its line.");
    }

    private void SkipBlankLines()
    {
        while (!_scanner.IsEnd && char.IsWhiteSpace(_scanner.Current))
        {
            _scanner.Position++;
        }
    }

    private static string Describe(ElementKind kind) => kind switch
    {
        ElementKind.Actor => "an actor",
        ElementKind.Json => "a table",
        _ => "a use case",
    };

    private enum ElementKind
    {
        UseCase,
        Actor,
        Json,
    }

    private sealed class ElementDraft(string id)
    {
        public string Id { get; } = id;

        public ElementKind? Kind { get; set; }

        public DiagramText? Label { get; set; }

        public UseCaseShape Shape { get; set; }

        public string? Stereotype { get; set; }

        public string? Json { get; set; }

        public List<string> Classes { get; } = [];

        public BoundaryDraft? Boundary { get; set; }

        public int Offset { get; set; }

        /// <summary>Gets or sets the rank of the first statement that names the element, which orders the elements.</summary>
        public int FirstMention { get; set; }

        public UseCaseDiagramElement? Element { get; set; }
    }

    private sealed class BoundaryDraft(string id)
    {
        public string Id { get; } = id;

        public DiagramText? Title { get; set; }

        public List<ElementDraft> Members { get; } = [];

        public List<string> Classes { get; } = [];

        public int Offset { get; set; }

        public UseCaseBoundary? Result { get; set; }
    }

    private sealed class RelationshipDraft
    {
        public required string Source { get; init; }

        public string? Target { get; set; }

        public string? Id { get; init; }

        public UseCaseRelationshipKind Kind { get; set; }

        public UseCaseArrowHead FromHead { get; set; }

        public UseCaseArrowHead ToHead { get; set; }

        public DiagramText? Label { get; set; }

        public int MinLength { get; set; } = 1;

        public int Offset { get; init; }

        public UseCaseRelationship? Result { get; set; }
    }
}
