using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.C4Diagrams;

/// <summary>
/// Parses the C4 diagrams of Mermaid: statements in the syntax of C4-PlantUML, <c>Name(attribute, ...)</c>, whose
/// attributes are positional or named (<c>$descr="..."</c>), and the boundaries that open a block <c>{ ... }</c>.
/// </summary>
internal sealed class C4DiagramParser
{
    // The positional attributes of each statement, after the alias
    private static readonly string[] ElementAttributes = ["label", "descr", "sprite", "tags", "link"];
    private static readonly string[] TechnologyElementAttributes = ["label", "techn", "descr", "sprite", "tags", "link"];
    private static readonly string[] BoundaryAttributes = ["label", "type", "tags", "link"];
    private static readonly string[] KindBoundaryAttributes = ["label", "tags", "link"];
    private static readonly string[] NodeAttributes = ["label", "type", "descr", "sprite", "tags", "link"];
    private static readonly string[] RelationshipAttributes = ["to", "label", "techn", "descr", "sprite", "tags", "link"];
    private static readonly string[] ElementStyleAttributes = ["bgColor", "fontColor", "borderColor", "shadowing", "shape", "sprite", "techn", "legendText", "legendSprite"];
    private static readonly string[] RelationshipStyleAttributes = ["to", "textColor", "lineColor", "offsetX", "offsetY"];

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly C4Diagram _diagram = new();
    private readonly Dictionary<string, C4Item> _items = new(StringComparer.Ordinal);
    private readonly Stack<C4Boundary> _boundaries = new();
    private readonly List<(string From, string To, C4Relationship Draft, int Offset)> _relationships = [];

    private C4DiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static C4Diagram Parse(ParseContext context, int position) => new C4DiagramParser(context, position).Parse();

    private C4Diagram Parse()
    {
        var keywordStart = _scanner.Position;
        var keyword = ReadName();
        _diagram.Kind = keyword switch
        {
            "C4Context" => C4DiagramKind.Context,
            "C4Container" => C4DiagramKind.Container,
            "C4Component" => C4DiagramKind.Component,
            "C4Dynamic" => C4DiagramKind.Dynamic,
            "C4Deployment" => C4DiagramKind.Deployment,
            _ => throw _scanner.Error(keywordStart, $"'{keyword}' is not a C4 diagram."),
        };
        _scanner.ExpectStatementEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "c4");
        if (_context.Configuration.GetBoolean("c4", "wrap") is false || _context.Configuration.GetBoolean("wrap") is false)
        {
            _diagram.Wrap = false;
        }

        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_boundaries.Count > 0)
            throw _scanner.Error(_scanner.Text.Length, $"The boundary '{_boundaries.Peek().Id}' is not closed by '}}'.");

        foreach (var (from, to, draft, offset) in _relationships)
        {
            if (!_items.TryGetValue(from, out var source))
                throw _context.Error(offset, $"The relationship goes from '{from}', which is not an element nor a boundary.");

            if (!_items.TryGetValue(to, out var target))
                throw _context.Error(offset, $"The relationship goes to '{to}', which is not an element nor a boundary.");

            var relationship = new C4Relationship(source, target, draft.Label)
            {
                Kind = draft.Kind,
                Technology = draft.Technology,
                Description = draft.Description,
                Style = draft.Style,
                LabelOffsetX = draft.LabelOffsetX,
                LabelOffsetY = draft.LabelOffsetY,
                Tags = draft.Tags,
                Link = draft.Link,
            };
            _diagram.Relationships.Add(relationship);
        }

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (_scanner.Current == '}')
        {
            if (_boundaries.Count is 0)
                throw _scanner.Error("'}' closes no boundary.");

            _scanner.Position++;
            _boundaries.Pop();
            return;
        }

        if (_scanner.StartsWithKeyword("title") || _scanner.StartsWithKeyword("accDescription"))
        {
            var isTitle = _scanner.StartsWith("title");
            _scanner.Position += isTitle ? "title".Length : "accDescription".Length;

            // As in Mermaid, the text stops at '#' and ';'
            var textStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current is not ('\n' or '#' or ';'))
            {
                _scanner.Position++;
            }

            if (_scanner.Current == '#')
                throw _scanner.Error($"The {(isTitle ? "title" : "description")} cannot contain '#'.");

            var text = _scanner.Text[textStart.._scanner.Position].Trim();
            if (isTitle)
            {
                _diagram.Title = text.Length is 0 ? null : text;
            }
            else
            {
                _diagram.Description = text.Length is 0 ? null : text;
            }

            return;
        }

        if (_scanner.StartsWithKeyword("direction"))
        {
            _scanner.ReadLine();
            _context.Warn(start, "The direction of a C4 diagram is ignored: the elements are placed in rows in order.");
            return;
        }

        var name = ReadName();
        if (name.Length is 0)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a statement such as 'Person(...)' is expected.");

        _scanner.SkipSpaces();
        if (_scanner.Current != '(')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': '(' is expected after '{name}'.");

        var attributesStart = _scanner.Position;
        var (positional, named) = ReadAttributes();
        switch (name)
        {
            case "Person" or "Person_Ext"
                or "System" or "SystemDb" or "SystemQueue" or "System_Ext" or "SystemDb_Ext" or "SystemQueue_Ext"
                or "Container" or "ContainerDb" or "ContainerQueue" or "Container_Ext" or "ContainerDb_Ext" or "ContainerQueue_Ext"
                or "Component" or "ComponentDb" or "ComponentQueue" or "Component_Ext" or "ComponentDb_Ext" or "ComponentQueue_Ext":
                AddElement(name, positional, named, start);
                _scanner.ExpectStatementEnd();
                break;
            case "Boundary" or "Enterprise_Boundary" or "System_Boundary" or "Container_Boundary" or "Deployment_Node" or "Node" or "Node_L" or "Node_R":
                AddBoundary(name, positional, named, start);
                break;
            case "Rel" or "BiRel" or "Rel_Up" or "Rel_U" or "Rel_Down" or "Rel_D" or "Rel_Left" or "Rel_L" or "Rel_Right" or "Rel_R" or "Rel_Back" or "RelIndex":
                AddRelationship(name, positional, named, start);
                _scanner.ExpectStatementEnd();
                break;
            case "UpdateElementStyle":
                UpdateElementStyle(positional, named, start);
                _scanner.ExpectStatementEnd();
                break;
            case "UpdateRelStyle":
                UpdateRelationshipStyle(positional, named, start);
                _scanner.ExpectStatementEnd();
                break;
            case "UpdateLayoutConfig":
                var values = Bind(["c4ShapeInRow", "c4BoundaryInRow"], positional, named, attributesStart);
                if (ParseInteger(values.GetValueOrDefault("c4ShapeInRow")) is >= 1 and <= 1000 and var shapes)
                {
                    _diagram.ShapesPerRow = shapes;
                }

                if (ParseInteger(values.GetValueOrDefault("c4BoundaryInRow")) is >= 1 and <= 1000 and var boundaries)
                {
                    _diagram.BoundariesPerRow = boundaries;
                }

                _scanner.ExpectStatementEnd();
                break;
            default:
                throw _scanner.Error(start, $"'{name}' is not a statement of a C4 diagram.");
        }
    }

    private void AddElement(string name, List<string> positional, List<(string Key, string Value)> named, int offset)
    {
        var baseName = name.Replace("_Ext", string.Empty, StringComparison.Ordinal);
        var kind = baseName switch
        {
            "Person" => C4ElementKind.Person,
            "System" or "SystemDb" or "SystemQueue" => C4ElementKind.System,
            "Container" or "ContainerDb" or "ContainerQueue" => C4ElementKind.Container,
            _ => C4ElementKind.Component,
        };
        var values = Bind(kind is C4ElementKind.Container or C4ElementKind.Component ? TechnologyElementAttributes : ElementAttributes, positional, named, offset, hasAlias: true);
        var alias = values["alias"];
        var element = _items.GetValueOrDefault(alias) as C4Element;
        if (_items.TryGetValue(alias, out var existing) && existing is not C4Element)
            throw _scanner.Error(offset, $"'{alias}' is already a boundary.");

        if (element is null)
        {
            CheckCount(offset);
            element = new C4Element(alias, kind, string.Empty);
            _items[alias] = element;
        }
        else
        {
            // As in Mermaid, a second declaration updates the element and moves it to the current boundary
            RemoveFromParent(element);
        }

        element.Kind = kind;
        element.IsExternal = name.EndsWith("_Ext", StringComparison.Ordinal);
        element.Shape = baseName.EndsWith("Db", StringComparison.Ordinal) ? C4ElementShape.Database : baseName.EndsWith("Queue", StringComparison.Ordinal) ? C4ElementShape.Queue : C4ElementShape.Box;
        element.Label = new DiagramText(MermaidText.DecodePlain(values.GetValueOrDefault("label") ?? string.Empty));
        if (values.TryGetValue("descr", out var description))
        {
            element.Description = ToText(description);
        }

        if (values.TryGetValue("techn", out var technology))
        {
            element.Technology = ToString(technology);
        }

        ApplyCommon(element, values, offset);
        (_boundaries.Count > 0 ? _boundaries.Peek().Elements : _diagram.Elements).Add(element);
    }

    private void AddBoundary(string name, List<string> positional, List<(string Key, string Value)> named, int offset)
    {
        var kind = name switch
        {
            "Enterprise_Boundary" => C4BoundaryKind.Enterprise,
            "System_Boundary" => C4BoundaryKind.System,
            "Container_Boundary" => C4BoundaryKind.Container,
            "Deployment_Node" or "Node" => C4BoundaryKind.DeploymentNode,
            "Node_L" => C4BoundaryKind.DeploymentNodeLeft,
            "Node_R" => C4BoundaryKind.DeploymentNodeRight,
            _ => C4BoundaryKind.Boundary,
        };
        var attributes = kind switch
        {
            C4BoundaryKind.Boundary => BoundaryAttributes,
            C4BoundaryKind.Enterprise or C4BoundaryKind.System or C4BoundaryKind.Container => KindBoundaryAttributes,
            _ => NodeAttributes,
        };
        var values = Bind(attributes, positional, named, offset, hasAlias: true);
        var alias = values["alias"];
        if (_items.TryGetValue(alias, out var existing) && existing is not C4Boundary)
            throw _scanner.Error(offset, $"'{alias}' is already an element.");

        if (_boundaries.Count >= _context.Options.MaxDepth)
            throw _scanner.Error(offset, $"The boundaries are nested deeper than {_context.Options.MaxDepth} levels.");

        var boundary = existing as C4Boundary;
        if (boundary is null)
        {
            CheckCount(offset);
            boundary = new C4Boundary(alias, string.Empty, kind);
            _items[alias] = boundary;
        }
        else
        {
            if (_boundaries.Contains(boundary))
                throw _scanner.Error(offset, $"The boundary '{alias}' cannot be nested in itself.");

            RemoveFromParent(boundary);
        }

        boundary.Kind = kind;
        boundary.Label = new DiagramText(MermaidText.DecodePlain(values.GetValueOrDefault("label") ?? string.Empty));
        if (values.TryGetValue("type", out var type))
        {
            boundary.Type = ToString(type);
        }

        if (values.TryGetValue("descr", out var description))
        {
            boundary.Description = ToText(description);
        }

        ApplyCommon(boundary, values, offset);
        (_boundaries.Count > 0 ? _boundaries.Peek().Boundaries : _diagram.Boundaries).Add(boundary);

        // The block opens on the line of the boundary, or on the next one
        _scanner.SkipSpaces();
        if (_scanner.Current != '{')
        {
            _scanner.SkipSeparators();
            if (_scanner.Current != '{')
                throw _scanner.Error($"Unexpected '{_scanner.Describe()}': '{{' is expected after the boundary '{alias}'.");
        }

        _scanner.Position++;
        _boundaries.Push(boundary);
    }

    private void AddRelationship(string name, List<string> positional, List<(string Key, string Value)> named, int offset)
    {
        if (name == "RelIndex")
        {
            // The index is ignored: the relationships are numbered in order
            if (positional.Count > 0)
            {
                positional.RemoveAt(0);
            }

            named.RemoveAll(item => item.Key == "index");
        }

        var values = Bind(RelationshipAttributes, positional, named, offset, hasAlias: true);
        var from = values["alias"];
        var to = values.GetValueOrDefault("to") ?? throw _scanner.Error(offset, "The target of the relationship is expected.");
        var label = values.GetValueOrDefault("label") ?? throw _scanner.Error(offset, "The label of the relationship is expected.");
        var kind = name switch
        {
            "BiRel" => C4RelationshipKind.Bidirectional,
            "Rel_Back" => C4RelationshipKind.Back,
            "Rel_Up" or "Rel_U" => C4RelationshipKind.Up,
            "Rel_Down" or "Rel_D" => C4RelationshipKind.Down,
            "Rel_Left" or "Rel_L" => C4RelationshipKind.Left,
            "Rel_Right" or "Rel_R" => C4RelationshipKind.Right,
            _ => C4RelationshipKind.Default,
        };

        // As in Mermaid, a second relationship between the same items replaces the first one, at its position
        var index = _relationships.FindIndex(item => item.From == from && item.To == to);
        C4Relationship draft;
        if (index >= 0)
        {
            draft = _relationships[index].Draft;
        }
        else
        {
            if (_relationships.Count >= _context.Options.MaxEdges)
                throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxEdges} relationships.");

            var placeholder = new C4Element("placeholder", C4ElementKind.System, string.Empty);
            draft = new C4Relationship(placeholder, placeholder, string.Empty);
            _relationships.Add((from, to, draft, offset));
        }

        draft.Kind = kind;
        draft.Label = new DiagramText(MermaidText.DecodePlain(label));
        if (values.TryGetValue("techn", out var technology))
        {
            draft.Technology = ToString(technology);
        }

        if (values.TryGetValue("descr", out var description))
        {
            draft.Description = ToText(description);
        }

        if (values.TryGetValue("tags", out var tags))
        {
            draft.Tags = ToString(tags);
        }

        if (values.TryGetValue("link", out var link))
        {
            draft.Link = ToString(link) is { } url ? MermaidCommon.SanitizeUrl(url, _context, offset) : null;
        }

        if (values.ContainsKey("sprite"))
        {
            _context.Warn(offset, "The sprites are not supported: they are ignored.");
        }
    }

    private void UpdateElementStyle(List<string> positional, List<(string Key, string Value)> named, int offset)
    {
        var values = Bind(ElementStyleAttributes, positional, named, offset, hasAlias: true);
        if (!_items.TryGetValue(values["alias"], out var item))
        {
            _context.Warn(offset, $"The element '{values["alias"]}' is not defined: its style is ignored.");
            return;
        }

        var style = item.Style ?? new DiagramStyle();
        style = style with
        {
            Fill = ParseColor(values.GetValueOrDefault("bgColor"), offset) ?? style.Fill,
            TextColor = ParseColor(values.GetValueOrDefault("fontColor"), offset) ?? style.TextColor,
            Stroke = ParseColor(values.GetValueOrDefault("borderColor"), offset) ?? style.Stroke,
        };
        item.Style = style;
        if (values.TryGetValue("techn", out var technology) && item is C4Element element)
        {
            element.Technology = ToString(technology);
        }

        foreach (var key in new[] { "shadowing", "shape", "sprite", "legendText", "legendSprite" })
        {
            if (values.ContainsKey(key))
            {
                _context.Warn(offset, $"The attribute '{key}' of UpdateElementStyle is not supported: it is ignored.");
            }
        }
    }

    private void UpdateRelationshipStyle(List<string> positional, List<(string Key, string Value)> named, int offset)
    {
        var values = Bind(RelationshipStyleAttributes, positional, named, offset, hasAlias: true);
        var from = values["alias"];
        var to = values.GetValueOrDefault("to");
        var index = _relationships.FindIndex(item => item.From == from && item.To == to);
        if (index < 0)
        {
            _context.Warn(offset, $"There is no relationship from '{from}' to '{to}': its style is ignored.");
            return;
        }

        var draft = _relationships[index].Draft;
        var style = draft.Style ?? new DiagramStyle();
        draft.Style = style with
        {
            TextColor = ParseColor(values.GetValueOrDefault("textColor"), offset) ?? style.TextColor,
            Stroke = ParseColor(values.GetValueOrDefault("lineColor"), offset) ?? style.Stroke,
        };
        if (values.TryGetValue("offsetX", out var offsetX))
        {
            draft.LabelOffsetX = ParseInteger(offsetX) ?? 0;
        }

        if (values.TryGetValue("offsetY", out var offsetY))
        {
            draft.LabelOffsetY = ParseInteger(offsetY) ?? 0;
        }
    }

    private void ApplyCommon(C4Item item, Dictionary<string, string> values, int offset)
    {
        if (values.TryGetValue("tags", out var tags))
        {
            item.Tags = ToString(tags);
        }

        if (values.TryGetValue("link", out var link))
        {
            item.Link = ToString(link) is { } url ? MermaidCommon.SanitizeUrl(url, _context, offset) : null;
        }

        if (values.ContainsKey("sprite"))
        {
            _context.Warn(offset, "The sprites are not supported: they are ignored.");
        }
    }

    /// <summary>
    /// Assigns the attributes to their names: the positional ones in order, after the alias, and the named ones
    /// (<c>$descr="..."</c>) by name, whatever their position. An empty positional attribute leaves its value unset.
    /// </summary>
    private Dictionary<string, string> Bind(string[] names, List<string> positional, List<(string Key, string Value)> named, int offset, bool hasAlias = false)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        if (hasAlias)
        {
            if (positional.Count is 0 || positional[0].Length is 0)
                throw _scanner.Error(offset, "The alias is expected as the first attribute.");

            result["alias"] = positional[0];
            index = 1;
        }

        for (var i = 0; index < positional.Count; i++, index++)
        {
            if (i >= names.Length)
            {
                _context.Warn(offset, "The extra attributes are ignored.");
                break;
            }

            if (positional[index].Length > 0)
            {
                result[names[i]] = positional[index];
            }
        }

        foreach (var (key, value) in named)
        {
            if (key == "alias" || (!names.Contains(key, StringComparer.Ordinal) && key is not ("descr" or "techn" or "type" or "label")))
            {
                _context.Warn(offset, $"The attribute '${key}' is not supported: it is ignored.");
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>Reads the attributes between parentheses: quoted, named (<c>$key="value"</c>) or plain text, separated by commas.</summary>
    private (List<string> Positional, List<(string Key, string Value)> Named) ReadAttributes()
    {
        var start = _scanner.Position;
        _scanner.Position++;
        var positional = new List<string>();
        var named = new List<(string Key, string Value)>();
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsEnd || _scanner.Current == '\n')
                throw _scanner.Error(start, "The attributes are not closed by ')'.");

            if (_scanner.Current == ')' && positional.Count is 0 && named.Count is 0)
            {
                _scanner.Position++;
                return (positional, named);
            }

            if (_scanner.Current == '$')
            {
                var keyStart = _scanner.Position + 1;
                var equals = _scanner.Text.IndexOfOrdinal('=', keyStart);
                var lineEnd = _scanner.Text.IndexOfOrdinal('\n', keyStart);
                if (equals < 0 || (lineEnd >= 0 && lineEnd < equals))
                    throw _scanner.Error("'=' is expected after the name of the attribute.");

                var key = _scanner.Text[keyStart..equals].Trim();
                _scanner.Position = equals + 1;
                _scanner.SkipSpaces();
                if (_scanner.Current != '"')
                    throw _scanner.Error("The value of a named attribute is quoted.");

                var value = ReadQuoted();
                if (key.Length is 0)
                    throw _scanner.Error("The name of the attribute is empty.");

                named.Add((key, value));
            }
            else if (_scanner.Current == '"')
            {
                positional.Add(ReadQuoted());
            }
            else
            {
                var valueStart = _scanner.Position;
                while (!_scanner.IsEnd && _scanner.Current is not (',' or ')' or '\n' or '"'))
                {
                    _scanner.Position++;
                }

                positional.Add(_scanner.Text[valueStart.._scanner.Position].Trim());
            }

            _scanner.SkipSpaces();
            if (_scanner.TryConsume(","))
            {
                // A trailing comma is an empty attribute
                _scanner.SkipSpaces();
                if (_scanner.Current == ')')
                {
                    positional.Add(string.Empty);
                }

                continue;
            }

            if (_scanner.TryConsume(")"))
                return (positional, named);

            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': ',' or ')' is expected.");
        }
    }

    private string ReadQuoted()
    {
        var start = _scanner.Position;
        var value = _scanner.ReadQuoted();
        if (value.Contains('\n', StringComparison.Ordinal))
            throw _scanner.Error(start, "A value cannot span several lines.");

        return value.Trim();
    }

    private string ReadName()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private void RemoveFromParent(C4Element element)
    {
        _diagram.Elements.Remove(element);
        foreach (var boundary in _diagram.EnumerateBoundaries())
        {
            boundary.Elements.Remove(element);
        }
    }

    private void RemoveFromParent(C4Boundary boundary)
    {
        _diagram.Boundaries.Remove(boundary);
        foreach (var parent in _diagram.EnumerateBoundaries().ToList())
        {
            parent.Boundaries.Remove(boundary);
        }
    }

    private void CheckCount(int offset)
    {
        if (_items.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} elements and boundaries.");
    }

    private SceneColor? ParseColor(string? value, int offset)
    {
        if (value is null)
            return null;

        if (SceneColor.TryParse(value, out var color))
            return color;

        _context.Warn(offset, $"'{value}' is not a color: it is ignored.");
        return null;
    }

    /// <summary>Reads an integer as <c>parseInt</c> does: the digits at the start, after a sign.</summary>
    internal static int? ParseInteger(string? value)
    {
        if (value is null)
            return null;

        var text = value.Trim();
        var end = text.Length > 0 && text[0] is '-' or '+' ? 1 : 0;
        while (end < text.Length && char.IsAsciiDigit(text[end]) && end < 9)
        {
            end++;
        }

        return int.TryParse(text.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static string? ToString(string value) => value.Length is 0 ? null : MermaidText.DecodePlain(value);

    private static DiagramText? ToText(string value) => value.Length is 0 ? null : new DiagramText(MermaidText.DecodePlain(value));
}
