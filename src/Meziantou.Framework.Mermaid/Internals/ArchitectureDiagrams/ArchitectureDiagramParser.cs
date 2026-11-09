using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.ArchitectureDiagrams;

/// <summary>Parses the <c>architecture-beta</c> diagrams of Mermaid. As in Mermaid, an item is declared before it is used: a parent before its children, the nodes before their edges.</summary>
internal sealed class ArchitectureDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly ArchitectureDiagram _diagram = new();
    private readonly Dictionary<string, ArchitectureGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchitectureNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<ArchitectureNode, ArchitectureGroup> _parents = new(ReferenceEqualityComparer.Instance);

    private ArchitectureDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static ArchitectureDiagram Parse(ParseContext context, int position) => new ArchitectureDiagramParser(context, position).Parse();

    private ArchitectureDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("architecture-beta"))
            throw _scanner.Error(keywordStart, "'architecture-beta' is expected.");

        ExpectLineEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "architecture");
        if (_context.Configuration.GetNumber("architecture", "iconSize") is >= 1 and <= 1000 and var iconSize)
        {
            _diagram.IconSize = iconSize;
        }

        if (_context.Configuration.GetNumber("architecture", "padding") is >= 0 and <= 1000 and var padding)
        {
            _diagram.GroupPadding = padding;
        }

        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (_scanner.StartsWithKeyword("title"))
        {
            _scanner.Position += "title".Length;
            var title = _scanner.ReadRestOfLine().Trim();
            _diagram.Title = title.Length is 0 ? null : title;
            return;
        }

        if (_scanner.StartsWithKeyword("group"))
        {
            _scanner.Position += "group".Length;
            _scanner.SkipSpaces();
            var id = ReadId("group");
            CheckUnique(id, start);
            var group = new ArchitectureGroup(id) { Icon = ReadIcon(), Title = ReadTitle() };
            var parent = ReadParent(id, "group");
            _groups[id] = group;
            (parent?.Groups ?? _diagram.Groups).Add(group);
            ExpectLineEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("service"))
        {
            _scanner.Position += "service".Length;
            _scanner.SkipSpaces();
            var id = ReadId("service");
            CheckUnique(id, start);
            var service = new ArchitectureService(id);
            _scanner.SkipSpaces();
            if (_scanner.Current is '"' or '\'')
            {
                service.IconText = ReadString();
            }
            else
            {
                service.Icon = ReadIcon();
            }

            service.Title = ReadTitle();
            AddNode(service, ReadParent(id, "service"));
            ExpectLineEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("junction"))
        {
            _scanner.Position += "junction".Length;
            _scanner.SkipSpaces();
            var id = ReadId("junction");
            CheckUnique(id, start);
            AddNode(new ArchitectureJunction(id), ReadParent(id, "junction"));
            ExpectLineEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("align"))
        {
            ParseAlignment(start);
            return;
        }

        ParseEdge(start);
    }

    private void AddNode(ArchitectureNode node, ArchitectureGroup? parent)
    {
        if (_nodes.Count + _groups.Count >= _context.Options.MaxNodes)
            throw _scanner.Error($"The diagram has more than {_context.Options.MaxNodes} items.");

        _nodes[node.Id] = node;
        if (parent is null)
        {
            _diagram.Nodes.Add(node);
        }
        else
        {
            parent.Nodes.Add(node);
            _parents[node] = parent;
        }
    }

    /// <summary>Parses an edge: <c>a:R --&gt; L:b</c>, <c>a{group}:B -[label]- T:b{group}</c>.</summary>
    private void ParseEdge(int start)
    {
        var fromId = ReadId("edge");
        var fromGroup = _scanner.TryConsume("{group}");
        _scanner.SkipSpaces();
        _scanner.Expect(":");
        _scanner.SkipSpaces();
        var fromSide = ReadSide();
        _scanner.SkipSpaces();
        var fromArrow = _scanner.TryConsume("<");
        _scanner.SkipSpaces();
        DiagramText? label = null;
        if (_scanner.TryConsume("--"))
        {
        }
        else if (_scanner.TryConsume("-"))
        {
            _scanner.SkipSpaces();
            label = ReadTitle() ?? throw _scanner.Error("The label of the edge is expected: -[label]-.");
            _scanner.SkipSpaces();
            _scanner.Expect("-");
        }
        else
        {
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': '--' is expected.");
        }

        _scanner.SkipSpaces();
        var toArrow = _scanner.TryConsume(">");
        _scanner.SkipSpaces();
        var toSide = ReadSide();
        _scanner.SkipSpaces();
        _scanner.Expect(":");
        _scanner.SkipSpaces();
        var toId = ReadId("edge");
        var toGroup = _scanner.TryConsume("{group}");
        ExpectLineEnd();

        var from = _nodes.GetValueOrDefault(fromId) ?? throw _scanner.Error(start, $"'{fromId}' is not declared: a service or a junction is declared before its edges.");
        var to = _nodes.GetValueOrDefault(toId) ?? throw _scanner.Error(start, $"'{toId}' is not declared: a service or a junction is declared before its edges.");
        var fromParent = _parents.GetValueOrDefault(from);
        var toParent = _parents.GetValueOrDefault(to);
        if ((fromGroup || toGroup) && (fromParent is null || toParent is null || fromParent == toParent))
            throw _scanner.Error(start, "The {group} modifier needs an edge between two different groups.");

        if (_diagram.Edges.Count >= _context.Options.MaxEdges)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxEdges} edges.");

        _diagram.Edges.Add(new ArchitectureEdge(from, fromSide, to, toSide) { FromArrow = fromArrow, ToArrow = toArrow, FromGroup = fromGroup, ToGroup = toGroup, Label = label });
    }

    private void ParseAlignment(int start)
    {
        _scanner.Position += "align".Length;
        _scanner.SkipSpaces();
        bool isRow;
        if (_scanner.StartsWithKeyword("row"))
        {
            isRow = true;
            _scanner.Position += 3;
        }
        else if (_scanner.StartsWithKeyword("column"))
        {
            isRow = false;
            _scanner.Position += 6;
        }
        else
        {
            throw _scanner.Error("'row' or 'column' is expected after 'align'.");
        }

        var alignment = new ArchitectureAlignment(isRow);
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsAtStatementEnd())
                break;

            var memberStart = _scanner.Position;
            var id = ReadId("member");
            var node = _nodes.GetValueOrDefault(id) ?? throw _scanner.Error(memberStart, $"'{id}' is not a service nor a junction declared before the alignment.");
            if (alignment.Members.Contains(node))
                throw _scanner.Error(memberStart, $"'{id}' is listed twice.");

            alignment.Members.Add(node);
        }

        if (alignment.Members.Count < 2)
            throw _scanner.Error(start, "An alignment has two members at least.");

        ExpectLineEnd();
        _diagram.Alignments.Add(alignment);
    }

    private ArchitectureSide ReadSide()
    {
        var side = _scanner.Current switch
        {
            'L' => ArchitectureSide.Left,
            'R' => ArchitectureSide.Right,
            'T' => ArchitectureSide.Top,
            'B' => ArchitectureSide.Bottom,
            _ => throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a side is expected: L, R, T or B."),
        };
        _scanner.Position++;
        return side;
    }

    private ArchitectureGroup? ReadParent(string id, string kind)
    {
        _scanner.SkipSpaces();
        if (!_scanner.StartsWithKeyword("in"))
            return null;

        var start = _scanner.Position;
        _scanner.Position += 2;
        _scanner.SkipSpaces();
        var parentId = ReadId("parent");
        if (string.Equals(parentId, id, StringComparison.Ordinal))
            throw _scanner.Error(start, $"The {kind} '{id}' cannot be placed within itself.");

        if (_groups.TryGetValue(parentId, out var parent))
            return parent;

        if (_nodes.ContainsKey(parentId))
            throw _scanner.Error(start, $"The parent of the {kind} '{id}' is not a group.");

        throw _scanner.Error(start, $"The parent of the {kind} '{id}', '{parentId}', does not exist: a group is declared before its content.");
    }

    private string? ReadIcon()
    {
        _scanner.SkipSpaces();
        if (_scanner.Current != '(')
            return null;

        var start = _scanner.Position;
        var end = _scanner.Text.IndexOfOrdinal(')', start);
        if (end < 0)
            throw _scanner.Error(start, "The icon is not closed by ')'.");

        var icon = _scanner.Text[(start + 1)..end].Trim();
        if (!IsValidIcon(icon))
            throw _scanner.Error(start, "The name of an icon has letters, digits, '_', '-' and ':'.");

        _scanner.Position = end + 1;
        if (!IsBuiltIn(icon))
        {
            _context.Warn(start, $"The icon '{icon}' is not built in: a question mark is drawn.");
        }

        return icon;
    }

    internal static bool IsValidIcon(string icon) => icon.Length > 0 && icon.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ':');

    private static bool IsBuiltIn(string icon) => icon is "cloud" or "database" or "disk" or "internet" or "server";

    /// <summary>Reads a title between brackets: a text, or a quoted text with backslash escapes.</summary>
    private DiagramText? ReadTitle()
    {
        _scanner.SkipSpaces();
        if (_scanner.Current != '[')
            return null;

        var start = _scanner.Position;
        _scanner.Position++;
        _scanner.SkipSpaces();
        string title;
        if (_scanner.Current is '"' or '\'')
        {
            title = ReadString();
            _scanner.SkipSpaces();
        }
        else
        {
            var textStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current is not (']' or '[' or '\n'))
            {
                _scanner.Position++;
            }

            title = _scanner.Text[textStart.._scanner.Position].Trim();
        }

        if (_scanner.Current != ']')
            throw _scanner.Error(start, "The title is not closed by ']'.");

        _scanner.Position++;
        title = title.Trim();
        return title.Length is 0 ? null : new DiagramText(title);
    }

    /// <summary>Reads a quoted string, with backslash escapes.</summary>
    private string ReadString()
    {
        var start = _scanner.Position;
        var quote = _scanner.Current;
        _scanner.Position++;
        var builder = new StringBuilder();
        while (true)
        {
            if (_scanner.IsEnd || _scanner.Current == '\n')
                throw _scanner.Error(start, "The string is not closed.");

            var c = _scanner.Current;
            _scanner.Position++;
            if (c == quote)
                return builder.ToString();

            if (c == '\\' && !_scanner.IsEnd && _scanner.Current != '\n')
            {
                c = _scanner.Current;
                _scanner.Position++;
            }

            builder.Append(c);
        }
    }

    /// <summary>Reads an identifier: letters, digits, <c>_</c> and <c>-</c> inside, which is not a keyword.</summary>
    private string ReadId(string what)
    {
        var start = _scanner.Position;
        var end = start;
        while (end < _scanner.Text.Length && (char.IsAsciiLetterOrDigit(_scanner.Text[end]) || _scanner.Text[end] is '_' or '-'))
        {
            end++;
        }

        // An identifier does not end with a dash: the dash starts the edge
        while (end > start && _scanner.Text[end - 1] == '-')
        {
            end--;
        }

        if (end == start)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the identifier of the {what} is expected.");

        var id = _scanner.Text[start..end];
        if (IsKeyword(id))
            throw _scanner.Error(start, $"'{id}' is a keyword and cannot be an identifier.");

        _scanner.Position = end;
        return id;
    }

    internal static bool IsValidId(string id) => id.Length > 0 && (char.IsAsciiLetterOrDigit(id[0]) || id[0] == '_') && id[^1] != '-' && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && !IsKeyword(id);

    internal static bool IsKeyword(string id) => id is "group" or "service" or "junction" or "align" or "in" or "row" or "column" or "title" or "L" or "R" or "T" or "B" or "accTitle" or "accDescr";

    private void CheckUnique(string id, int offset)
    {
        if (_groups.ContainsKey(id) || _nodes.ContainsKey(id))
            throw _scanner.Error(offset, $"The identifier '{id}' is already in use.");
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a statement is alone on its line.");
    }
}
