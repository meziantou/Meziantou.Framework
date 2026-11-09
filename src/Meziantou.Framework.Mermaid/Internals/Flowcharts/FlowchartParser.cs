using System.Globalization;

namespace Meziantou.Framework.Mermaid.Internals.Flowcharts;

/// <summary>Parses the <c>flowchart</c> and <c>graph</c> diagrams of Mermaid.</summary>
internal sealed class FlowchartParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly FlowchartDiagram _diagram = new();
    private readonly Dictionary<string, Vertex> _vertices = new(StringComparer.Ordinal);
    private readonly List<Vertex> _vertexOrder = [];
    private readonly List<SubgraphInfo> _subgraphs = [];
    private readonly Dictionary<string, SubgraphInfo> _subgraphsById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);
    private readonly List<EdgeInfo> _edges = [];
    private readonly Dictionary<string, EdgeInfo> _edgesById = new(StringComparer.Ordinal);
    private readonly List<(string Ids, DiagramStyle Style, EdgeCurve? Curve, int Offset)> _linkStyles = [];
    private readonly Stack<SubgraphInfo> _open = new();
    private int _subgraphCount;

    private FlowchartParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static FlowchartDiagram Parse(ParseContext context, int position) => new FlowchartParser(context, position).Parse();

    private FlowchartDiagram Parse()
    {
        // The header: flowchart or graph, and an optional direction
        var keywordStart = _scanner.Position;
        while (!_scanner.IsEnd && !char.IsWhiteSpace(_scanner.Current) && _scanner.Current != ';')
        {
            _scanner.Position++;
        }

        var keyword = _scanner.Text[keywordStart.._scanner.Position];
        if (keyword is not ("flowchart" or "graph" or "flowchart-elk" or "flowchart-v2" or "swimlane-beta"))
            throw _scanner.Error(keywordStart, $"'{keyword}' is not a flowchart.");

        _diagram.Swimlanes = keyword is "swimlane-beta";

        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';'))
        {
            var directionStart = _scanner.Position;
            var direction = ReadWord();
            _diagram.Direction = MermaidCommon.ParseDirection(direction) ?? throw _scanner.Error(directionStart, $"'{direction}' is not a direction: use TB, TD, BT, LR or RL.");
        }

        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "flowchart");
        ApplyConfiguration();

        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_open.Count > 0)
            throw _scanner.Error(_open.Peek().Offset, $"The subgraph '{_open.Peek().Id}' is not closed by 'end'.");

        Build();
        return _diagram;
    }

    private void ApplyConfiguration()
    {
        var configuration = _context.Configuration;
        if (MermaidCommon.ParseCurve(configuration.GetString("flowchart", "curve")) is { } curve)
        {
            _diagram.Curve = curve;
        }

        if (configuration.GetNumber("flowchart", "nodeSpacing") is >= 0 and <= 10_000 and var nodeSpacing)
        {
            _diagram.NodeSpacing = nodeSpacing;
        }

        if (configuration.GetNumber("flowchart", "rankSpacing") is >= 0 and <= 10_000 and var rankSpacing)
        {
            _diagram.RankSpacing = rankSpacing;
        }

        if (configuration.GetNumber("flowchart", "wrappingWidth") is > 0 and <= 100_000 and var wrappingWidth)
        {
            _diagram.WrappingWidth = wrappingWidth;
        }
    }

    private void ParseStatement()
    {
        var start = _scanner.Position;
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        if (_scanner.StartsWithKeyword("subgraph"))
        {
            ParseSubgraph();
            return;
        }

        if (_scanner.StartsWithKeyword("end"))
        {
            if (_open.Count is 0)
                throw _scanner.Error("'end' closes no subgraph.");

            _scanner.Position += 3;
            CloseSubgraph(_open.Pop());
            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("direction"))
        {
            _scanner.Position += "direction".Length;
            _scanner.SkipSpaces();
            var directionStart = _scanner.Position;
            var direction = MermaidCommon.ParseDirection(ReadWord()) ?? throw _scanner.Error(directionStart, "A direction is expected: TB, TD, BT, LR or RL.");
            if (_open.TryPeek(out var subgraph))
            {
                subgraph.Direction = direction;
            }
            else
            {
                _diagram.Direction = direction;
            }

            _scanner.ExpectStatementEnd();
            return;
        }

        if (_scanner.StartsWithKeyword("classDef"))
        {
            ParseClassDef();
            return;
        }

        if (_scanner.StartsWithKeyword("class"))
        {
            ParseClass();
            return;
        }

        if (_scanner.StartsWithKeyword("style"))
        {
            ParseStyle();
            return;
        }

        if (_scanner.StartsWithKeyword("linkStyle"))
        {
            ParseLinkStyle();
            return;
        }

        if (_scanner.StartsWithKeyword("click"))
        {
            ParseClick();
            return;
        }

        // The data of an edge: e1@{ curve: linear }
        var wordEnd = SkipIdentifier(_scanner.Position);
        if (wordEnd > start && wordEnd + 1 < _scanner.Text.Length && _scanner.Text[wordEnd] == '@' && _scanner.Text[wordEnd + 1] == '{' && _edgesById.TryGetValue(_scanner.Text[start..wordEnd], out var edge))
        {
            _scanner.Position = wordEnd + 1;
            ApplyEdgeData(edge, MermaidCommon.ReadInlineMapping(_scanner), start);
            _scanner.ExpectStatementEnd();
            return;
        }

        ParseChain();
    }

    private void ParseSubgraph()
    {
        var start = _scanner.Position;
        if (_open.Count >= _context.Options.MaxDepth)
            throw _scanner.Error($"The subgraphs are nested deeper than {_context.Options.MaxDepth} levels.");

        _scanner.Position += "subgraph".Length;
        _scanner.SkipSpaces();
        var autoId = "subGraph" + _subgraphCount.ToString(CultureInfo.InvariantCulture);
        _subgraphCount++;
        string id;
        DiagramText? title = null;
        if (_scanner.Current == '"')
        {
            // subgraph "title": the identifier is generated, as in Mermaid
            title = ReadText(_scanner.ReadQuoted());
            id = autoId;
        }
        else
        {
            var idStart = _scanner.Position;
            var idEnd = SkipIdentifier(idStart);
            if (idEnd == idStart)
                throw _scanner.Error("A subgraph identifier or title is expected.");

            id = _scanner.Text[idStart..idEnd];
            if (IsKeyword(id) || IsAccessibilityStatement(id))
                throw _scanner.Error(idStart, $"'{id}' is a keyword and cannot be a subgraph identifier.");

            _scanner.Position = idEnd;
            _scanner.SkipSpaces();
            if (_scanner.Current == '[')
            {
                _scanner.Position++;
                title = ReadShapeText("]");
            }
            else if (!_scanner.IsAtStatementEnd())
            {
                // subgraph some title: the whole line is the title, and the identifier is generated
                _scanner.Position = idStart;
                title = ReadText(_scanner.ReadStatement().Trim());
                id = autoId;
            }
        }

        _scanner.ExpectStatementEnd();
        var subgraph = new SubgraphInfo(id, title, start, _open.TryPeek(out var parent) ? parent : null);
        _open.Push(subgraph);
    }

    private void CloseSubgraph(SubgraphInfo subgraph)
    {
        // As in Mermaid, a node belongs to the first subgraph that is closed after it is mentioned
        subgraph.Members.RemoveAll(member => _claimed.Contains(member));
        foreach (var member in subgraph.Members)
        {
            _claimed.Add(member);
        }

        if (_subgraphsById.ContainsKey(subgraph.Id))
        {
            _context.Warn(subgraph.Offset, $"The subgraph '{subgraph.Id}' is defined twice; the second definition is ignored.");
            return;
        }

        _subgraphs.Add(subgraph);
        _subgraphsById[subgraph.Id] = subgraph;
        if (subgraph.Parent is { } parent)
        {
            parent.Children.Add(subgraph);
        }
    }

    private void ParseClassDef()
    {
        var start = _scanner.Position;
        _scanner.Position += "classDef".Length;
        _scanner.SkipSpaces();
        var names = ReadWord();
        if (names.Length is 0)
            throw _scanner.Error("A class name is expected.");

        _scanner.SkipSpaces();
        var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
        foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
        }
    }

    private void ParseClass()
    {
        _scanner.Position += "class".Length;
        _scanner.SkipSpaces();
        var ids = ReadIdList();
        _scanner.SkipSpaces();
        var classNames = ReadWord().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (classNames.Length is 0)
            throw _scanner.Error("A class name is expected.");

        foreach (var id in ids)
        {
            var classes = _edgesById.TryGetValue(id, out var edge) ? edge.Classes
                : _subgraphsById.TryGetValue(id, out var subgraph) || TryGetOpenSubgraph(id, out subgraph) ? subgraph.Classes
                : GetVertex(id, _scanner.Position).Classes;
            classes.AddRange(classNames);
        }

        _scanner.ExpectStatementEnd();
    }

    private void ParseStyle()
    {
        var start = _scanner.Position;
        _scanner.Position += "style".Length;
        _scanner.SkipSpaces();
        var idStart = _scanner.Position;
        var idEnd = SkipIdentifier(idStart);
        if (idEnd == idStart)
            throw _scanner.Error("An identifier is expected.");

        var id = _scanner.Text[idStart..idEnd];
        _scanner.Position = idEnd;
        _scanner.SkipSpaces();
        var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
        if (_subgraphsById.TryGetValue(id, out var subgraph) || TryGetOpenSubgraph(id, out subgraph))
        {
            subgraph.Style = subgraph.Style is null ? style : subgraph.Style.Merge(style);
        }
        else
        {
            var vertex = GetVertex(id, idStart);
            vertex.Style = vertex.Style is null ? style : vertex.Style.Merge(style);
        }
    }

    private void ParseLinkStyle()
    {
        var start = _scanner.Position;
        _scanner.Position += "linkStyle".Length;
        _scanner.SkipSpaces();
        var ids = ReadWord();
        if (ids.Length is 0)
            throw _scanner.Error("The indexes of the edges, or 'default', are expected.");

        _scanner.SkipSpaces();
        EdgeCurve? curve = null;
        if (_scanner.StartsWithKeyword("interpolate"))
        {
            _scanner.Position += "interpolate".Length;
            _scanner.SkipSpaces();
            var curveStart = _scanner.Position;
            var curveName = ReadWord();
            curve = MermaidCommon.ParseCurve(curveName);
            if (curve is null)
            {
                _context.Warn(curveStart, $"The curve '{curveName}' is not supported.");
            }

            _scanner.SkipSpaces();
        }

        var style = MermaidStyle.Parse(_scanner.ReadStatement(), _context, start);
        _linkStyles.Add((ids, style, curve, start));
    }

    private void ParseClick()
    {
        var start = _scanner.Position;
        _scanner.Position += "click".Length;
        _scanner.SkipSpaces();
        var idStart = _scanner.Position;
        var idEnd = SkipIdentifier(idStart);
        if (idEnd == idStart)
            throw _scanner.Error("An identifier is expected.");

        var id = _scanner.Text[idStart..idEnd];
        _scanner.Position = idEnd;
        _scanner.SkipSpaces();
        string? link = null;
        string? tooltip = null;
        if (_scanner.StartsWithKeyword("href") || _scanner.Current == '"')
        {
            _scanner.TryConsume("href");
            _scanner.SkipSpaces();
            if (_scanner.Current != '"')
                throw _scanner.Error("A quoted address is expected.");

            link = MermaidCommon.SanitizeUrl(MermaidText.DecodeEntities(_scanner.ReadQuoted()), _context, start);
            _scanner.SkipSpaces();
            if (_scanner.Current == '"')
            {
                tooltip = _scanner.ReadQuoted();
                _scanner.SkipSpaces();
            }

            // The target of the link: _self, _blank, _parent, _top
            if (_scanner.Current == '_')
            {
                ReadWord();
            }
        }
        else
        {
            _context.Warn(start, "The JavaScript callbacks of 'click' are not supported.");
            if (_scanner.StartsWithKeyword("call"))
            {
                _scanner.Position += 4;
                _scanner.SkipSpaces();
            }

            // The callback, and its arguments
            while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';' or '"' or ' ' or '\t'))
            {
                if (_scanner.Current == '(')
                {
                    var close = _scanner.Text.IndexOfOrdinal(')', _scanner.Position);
                    _scanner.Position = close < 0 ? _scanner.Text.Length : close + 1;
                    continue;
                }

                _scanner.Position++;
            }

            _scanner.SkipSpaces();
            if (_scanner.Current == '"')
            {
                tooltip = _scanner.ReadQuoted();
            }
        }

        _scanner.ExpectStatementEnd();
        var vertex = GetVertex(id, idStart);
        if (link is not null)
        {
            vertex.Link = link;
        }

        if (tooltip is not null)
        {
            vertex.Tooltip = MermaidText.DecodeEntities(tooltip);
        }
    }

    /// <summary>Parses a chain of vertices and edges: <c>A &amp; B --&gt; C -- text --&gt; D</c>.</summary>
    private void ParseChain()
    {
        var group = ParseVertexGroup();
        while (true)
        {
            _scanner.SkipSpaces();
            var linkStart = _scanner.Position;
            if (!TryParseLink(out var link))
                break;

            _scanner.SkipSpaces();
            if (_scanner.IsAtStatementEnd())
                throw _scanner.Error("A node is expected after the edge.");

            var next = ParseVertexGroup();
            var first = true;
            foreach (var source in group)
            {
                foreach (var target in next)
                {
                    if (_edges.Count >= _context.Options.MaxEdges)
                        throw _scanner.Error(linkStart, $"The flowchart has more than {_context.Options.MaxEdges} edges.");

                    var edge = new EdgeInfo(source, target, link, linkStart);
                    _edges.Add(edge);

                    // An identifier names the first edge of the statement only
                    if (first && link.Id is not null)
                    {
                        if (!_edgesById.TryAdd(link.Id, edge))
                        {
                            _context.Warn(linkStart, $"The edge identifier '{link.Id}' is used twice.");
                        }

                        first = false;
                    }
                }
            }

            group = next;
        }

        _scanner.SkipSpaces();
        if (!_scanner.IsAtStatementEnd())
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an edge, '&', or the end of the statement is expected.");
    }

    private List<string> ParseVertexGroup()
    {
        var result = new List<string> { ParseVertex() };
        while (true)
        {
            var position = _scanner.Position;
            _scanner.SkipSpaces();
            if (_scanner.Current != '&')
            {
                _scanner.Position = position;
                return result;
            }

            _scanner.Position++;
            _scanner.SkipSpaces();
            result.Add(ParseVertex());
        }
    }

    private string ParseVertex()
    {
        var start = _scanner.Position;
        var end = SkipIdentifier(start);
        if (end == start)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a node identifier is expected.");

        var id = _scanner.Text[start..end];
        _scanner.Position = end;
        var vertex = GetVertex(id, start);
        ParseShape(vertex);

        // The classes: A:::class1:::class2
        while (_scanner.TryConsume(":::"))
        {
            var className = ReadClassName();
            if (className.Length is 0)
                throw _scanner.Error("A class name is expected after ':::'.");

            vertex.Classes.Add(className);
        }

        if (_open.TryPeek(out var subgraph) && !subgraph.Members.Contains(id))
        {
            subgraph.Members.Add(id);
        }

        return id;
    }

    private void ParseShape(Vertex vertex)
    {
        var start = _scanner.Position;
        if (_scanner.StartsWith("@{"))
        {
            _scanner.Position++;
            ApplyShapeData(vertex, MermaidCommon.ReadInlineMapping(_scanner), start);
            return;
        }

        (string Open, (string Close, FlowchartNodeShape Shape)[] Closes)[] shapes =
        [
            ("(((", [(")))", FlowchartNodeShape.DoubleCircle)]),
            ("((", [("))", FlowchartNodeShape.Circle)]),
            ("([", [("])", FlowchartNodeShape.Stadium)]),
            ("(", [(")", FlowchartNodeShape.RoundedRectangle)]),
            ("[[", [("]]", FlowchartNodeShape.FramedRectangle)]),
            ("[(", [(")]", FlowchartNodeShape.Cylinder)]),
            ("[/", [("/]", FlowchartNodeShape.LeanRight), ("\\]", FlowchartNodeShape.TrapezoidBaseBottom)]),
            ("[\\", [("\\]", FlowchartNodeShape.LeanLeft), ("/]", FlowchartNodeShape.TrapezoidBaseTop)]),
            ("[", [("]", FlowchartNodeShape.Rectangle)]),
            ("{{", [("}}", FlowchartNodeShape.Hexagon)]),
            ("{", [("}", FlowchartNodeShape.Diamond)]),
            (">", [("]", FlowchartNodeShape.Odd)]),
        ];

        foreach (var (open, closes) in shapes)
        {
            if (!_scanner.TryConsume(open))
                continue;

            var (text, shape) = ReadShapeText(closes);
            vertex.Label = text;
            vertex.Shape = shape;
            return;
        }
    }

    private DiagramText ReadShapeText(string close) => ReadShapeText([(close, FlowchartNodeShape.Rectangle)]).Text;

    /// <summary>Reads the text of a shape up to one of its closing delimiters, which tells the shape.</summary>
    private (DiagramText Text, FlowchartNodeShape Shape) ReadShapeText((string Close, FlowchartNodeShape Shape)[] closes)
    {
        var start = _scanner.Position;
        _scanner.SkipSpaces();
        if (_scanner.Current == '"')
        {
            var quoted = _scanner.ReadQuoted();
            _scanner.SkipSpaces();
            foreach (var (close, shape) in closes)
            {
                if (_scanner.TryConsume(close))
                    return (ReadText(quoted), shape);
            }

            throw _scanner.Error($"'{closes[0].Close}' is expected.");
        }

        _scanner.Position = start;
        var best = -1;
        var bestShape = closes[0].Shape;
        var bestLength = 0;
        foreach (var (close, shape) in closes)
        {
            var index = _scanner.Text.IndexOf(close, start, StringComparison.Ordinal);
            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
                bestShape = shape;
                bestLength = close.Length;
            }
        }

        var lineEnd = _scanner.Text.IndexOfOrdinal('\n', start);
        if (best < 0 || (lineEnd >= 0 && lineEnd < best))
            throw _scanner.Error(start, $"The text of the node is not closed by '{closes[0].Close}'.");

        var raw = _scanner.Text[start..best].Trim();
        _scanner.Position = best + bestLength;
        return (ReadText(raw), bestShape);
    }

    /// <summary>Decodes a label: a markdown string between backticks, or a text with entity codes and HTML.</summary>
    private static DiagramText ReadText(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '`' && raw[^1] == '`')
            return MermaidText.Decode(TrimLines(raw[1..^1]), isMarkdown: true);

        return MermaidText.Decode(raw);

        static string TrimLines(string text) => string.Join('\n', text.Split('\n').Select(line => line.Trim()));
    }

    private void ApplyShapeData(Vertex vertex, Dictionary<string, object?> data, int offset)
    {
        foreach (var (key, value) in data)
        {
            var text = value as string;
            switch (key)
            {
                case "shape":
                    if (text is not null && FlowchartShapes.TryGetShape(text.Trim(), out var shape))
                    {
                        vertex.Shape = shape;
                    }
                    else
                    {
                        _context.Warn(offset, $"The shape '{text}' is not supported; a rectangle is drawn.");
                        vertex.Shape = FlowchartNodeShape.Rectangle;
                    }

                    break;
                case "label":
                    if (text is not null)
                    {
                        vertex.Label = ReadText(text.Trim());
                    }

                    break;
                default:
                    _context.Warn(offset, $"The node data '{key}' is not supported.");
                    break;
            }
        }
    }

    private void ApplyEdgeData(EdgeInfo edge, Dictionary<string, object?> data, int offset)
    {
        foreach (var (key, value) in data)
        {
            switch (key)
            {
                case "curve":
                    if (MermaidCommon.ParseCurve(value as string) is { } curve)
                    {
                        edge.Curve = curve;
                    }
                    else
                    {
                        _context.Warn(offset, $"The curve '{value}' is not supported.");
                    }

                    break;
                default:
                    _context.Warn(offset, $"The edge data '{key}' is not supported.");
                    break;
            }
        }
    }

    /// <summary>
    /// Parses an edge: <c>--&gt;</c>, <c>---</c>, <c>-.-&gt;</c>, <c>==&gt;</c>, <c>~~~</c>, with the heads <c>&lt;</c>,
    /// <c>x</c> and <c>o</c>, more dashes for a longer edge, a text in the middle (<c>-- text --&gt;</c>) or between pipes
    /// (<c>--&gt;|text|</c>), and an identifier before it (<c>e1@--&gt;</c>).
    /// </summary>
    private bool TryParseLink(out LinkInfo link)
    {
        link = default!;
        var start = _scanner.Position;
        string? id = null;
        var idEnd = SkipIdentifier(start);
        if (idEnd > start && idEnd < _scanner.Text.Length && _scanner.Text[idEnd] == '@' && IsLinkStart(idEnd + 1))
        {
            id = _scanner.Text[start..idEnd];
            if (IsKeyword(id) || IsAccessibilityStatement(id))
                throw _scanner.Error(start, $"'{id}' is a keyword and cannot be an edge identifier.");

            _scanner.Position = idEnd + 1;
        }

        var linkStart = _scanner.Position;
        if (!IsLinkStart(linkStart))
        {
            _scanner.Position = start;
            return false;
        }

        if (TryReadEndToken(linkStart, out var token, allowNoDash: false))
        {
            _scanner.Position = linkStart + token.Length;
            var info = DestructEndLink(token, linkStart);
            DiagramText? label = null;
            var position = _scanner.Position;
            _scanner.SkipSpaces();
            if (_scanner.Current == '|')
            {
                label = ReadPipeText();
            }
            else
            {
                _scanner.Position = position;
            }

            link = info with { Id = id, Label = label };
            return true;
        }

        // The text form: -- text -->, == text ==>, -. text .->
        var startTokenLength = _scanner.Text[linkStart] is 'x' or 'o' or '<' ? 3 : 2;
        var startToken = _scanner.Text.Substring(linkStart, Math.Min(startTokenLength, _scanner.Text.Length - linkStart));
        _scanner.Position = linkStart + startToken.Length;
        _scanner.SkipSpaces();
        DiagramText text;
        int endStart;
        if (_scanner.Current == '"')
        {
            text = ReadText(_scanner.ReadQuoted());
            _scanner.SkipSpaces();
            endStart = _scanner.Position;
            if (!TryReadEndToken(endStart, out token, allowNoDash: true))
                throw _scanner.Error("The end of the edge is expected after its text.");
        }
        else
        {
            var textStart = _scanner.Position;
            endStart = textStart;
            token = string.Empty;
            while (endStart < _scanner.Text.Length && _scanner.Text[endStart] != '\n')
            {
                if (_scanner.Text[endStart] is '-' or '=' or '.' && TryReadEndToken(endStart, out token, allowNoDash: true) && IsEndTokenOf(startToken, token))
                    break;

                endStart++;
            }

            if (token.Length is 0 || endStart >= _scanner.Text.Length || _scanner.Text[endStart] == '\n')
                throw _scanner.Error(linkStart, "The edge is not closed: '-->', '---', '==>' or '.->' is expected after its text.");

            text = ReadText(_scanner.Text[textStart..endStart].Trim());
        }

        _scanner.Position = endStart + token.Length;
        var end = DestructEndLink(token, endStart);
        var startInfo = DestructStartLink(startToken);
        if (startInfo.LineStyle != end.LineStyle)
            throw _scanner.Error(linkStart, "The start and the end of the edge have different lines.");

        FlowchartArrowHead startHead;
        if (startInfo.Head is FlowchartArrowHead.None)
        {
            startHead = FlowchartArrowHead.None;
        }
        else if (startInfo.Head == end.EndHead)
        {
            startHead = startInfo.Head;
        }
        else
        {
            throw _scanner.Error(linkStart, "The two ends of the edge have different heads.");
        }

        link = end with { Id = id, Label = text, StartHead = startHead };
        return true;
    }

    private DiagramText ReadPipeText()
    {
        var start = _scanner.Position;
        _scanner.Position++;
        _scanner.SkipSpaces();
        if (_scanner.Current == '"')
        {
            var quoted = _scanner.ReadQuoted();
            _scanner.SkipSpaces();
            if (!_scanner.TryConsume("|"))
                throw _scanner.Error("'|' is expected after the text of the edge.");

            return ReadText(quoted);
        }

        var end = _scanner.Text.IndexOfOrdinal('|', _scanner.Position);
        var lineEnd = _scanner.Text.IndexOfOrdinal('\n', _scanner.Position);
        if (end < 0 || (lineEnd >= 0 && lineEnd < end))
            throw _scanner.Error(start, "The text of the edge is not closed by '|'.");

        var raw = _scanner.Text[_scanner.Position..end].Trim();
        _scanner.Position = end + 1;
        return ReadText(raw);
    }

    private bool IsLinkStart(int position)
    {
        var text = _scanner.Text;
        if (position >= text.Length)
            return false;

        var p = position;
        if (text[p] is 'x' or 'o' or '<')
        {
            p++;
        }

        if (p + 1 >= text.Length)
            return false;

        return (text[p], text[p + 1]) switch
        {
            ('-', '-') or ('=', '=') or ('-', '.') => true,
            ('~', '~') => p + 2 < text.Length && text[p + 2] == '~' && p == position,
            _ => false,
        };
    }

    /// <summary>Reads a complete edge token: <c>[xo&lt;]?--+[-xo&gt;]</c>, <c>[xo&lt;]?==+[=xo&gt;]</c>, <c>[xo&lt;]?-?\.+-[xo&gt;]?</c> or <c>~~~+</c>.</summary>
    private bool TryReadEndToken(int position, out string token, bool allowNoDash)
    {
        token = string.Empty;
        var text = _scanner.Text;
        var p = position;
        if (p < text.Length && text[p] is 'x' or 'o' or '<' && !allowNoDash)
        {
            p++;
        }

        if (p >= text.Length)
            return false;

        if (text[p] == '~')
        {
            var end = p;
            while (end < text.Length && text[end] == '~')
            {
                end++;
            }

            if (end - p < 3 || p != position)
                return false;

            token = text[position..end];
            return true;
        }

        if (text[p] is '-' or '=' && p + 1 < text.Length && text[p + 1] == text[p])
        {
            var c = text[p];
            var end = p;
            while (end < text.Length && text[end] == c)
            {
                end++;
            }

            if (end < text.Length && text[end] is 'x' or 'o' or '>')
            {
                token = text[position..(end + 1)];
                return true;
            }

            // At least three of the character: the last one closes the edge
            if (end - p >= 3)
            {
                token = text[position..end];
                return true;
            }

            return false;
        }

        // Dotted: -.->, -..-, .-> (the end of the text form)
        var q = p;
        if (text[q] == '-')
        {
            q++;
        }
        else if (!allowNoDash)
        {
            return false;
        }

        var dots = q;
        while (dots < text.Length && text[dots] == '.')
        {
            dots++;
        }

        if (dots == q || dots >= text.Length || text[dots] != '-')
            return false;

        var tokenEnd = dots + 1;
        if (tokenEnd < text.Length && text[tokenEnd] is 'x' or 'o' or '>')
        {
            tokenEnd++;
        }

        token = text[position..tokenEnd];
        return true;
    }

    private static bool IsEndTokenOf(string startToken, string endToken)
    {
        var start = startToken.TrimStart('x', 'o', '<');
        var end = endToken.TrimStart('x', 'o', '<');
        return start[0] switch
        {
            '=' => end.Contains('=', StringComparison.Ordinal),
            '-' when start.Length > 1 && start[1] == '.' => end.Contains('.', StringComparison.Ordinal),
            _ => !end.Contains('.', StringComparison.Ordinal) && !end.Contains('=', StringComparison.Ordinal),
        };
    }

    /// <summary>The type, the line and the length of an edge token, as <c>destructEndLink</c> of Mermaid.</summary>
    private LinkInfo DestructEndLink(string token, int offset)
    {
        var line = token[..^1];
        var endHead = FlowchartArrowHead.None;
        var startHead = FlowchartArrowHead.None;
        switch (token[^1])
        {
            case 'x':
                endHead = FlowchartArrowHead.Cross;
                if (token[0] == 'x' && token.Length > 1)
                {
                    startHead = FlowchartArrowHead.Cross;
                    line = line[1..];
                }

                break;
            case '>':
                endHead = FlowchartArrowHead.Arrow;
                if (token[0] == '<')
                {
                    startHead = FlowchartArrowHead.Arrow;
                    line = line[1..];
                }

                break;
            case 'o':
                endHead = FlowchartArrowHead.Circle;
                if (token[0] == 'o' && token.Length > 1)
                {
                    startHead = FlowchartArrowHead.Circle;
                    line = line[1..];
                }

                break;
        }

        var lineStyle = FlowchartLineStyle.Solid;
        var length = line.Length - 1;
        if (line.Length > 0 && line[0] == '=')
        {
            lineStyle = FlowchartLineStyle.Thick;
        }

        if (line.Length > 0 && line[0] == '~')
        {
            lineStyle = FlowchartLineStyle.Invisible;
        }

        var dots = line.Count(c => c == '.');
        if (dots > 0)
        {
            lineStyle = FlowchartLineStyle.Dotted;
            length = dots;
        }

        if (length > 100)
            throw _scanner.Error(offset, "The edge is longer than 100 ranks.");

        return new LinkInfo(lineStyle, startHead, endHead, Math.Max(1, length), Id: null, Label: null);
    }

    private static (FlowchartLineStyle LineStyle, FlowchartArrowHead Head) DestructStartLink(string token)
    {
        var head = FlowchartArrowHead.None;
        var line = token;
        switch (token[0])
        {
            case '<':
                head = FlowchartArrowHead.Arrow;
                line = token[1..];
                break;
            case 'x':
                head = FlowchartArrowHead.Cross;
                line = token[1..];
                break;
            case 'o':
                head = FlowchartArrowHead.Circle;
                line = token[1..];
                break;
        }

        var lineStyle = line.Contains('=', StringComparison.Ordinal) ? FlowchartLineStyle.Thick : FlowchartLineStyle.Solid;
        if (line.Contains('.', StringComparison.Ordinal))
        {
            lineStyle = FlowchartLineStyle.Dotted;
        }

        return (lineStyle, head);
    }

    /// <summary>Returns the end of the identifier that starts at a position: it stops at the shapes, the edges, the classes and the separators.</summary>
    private int SkipIdentifier(int position)
    {
        var text = _scanner.Text;
        var i = position;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c is '[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or '|' or ';' or ',' or '&' or '"' or '\'' or '@' or '`')
                break;

            if (c == ':' && i + 2 < text.Length && text[i + 1] == ':' && text[i + 2] == ':')
                break;

            if (c is '-' or '=' && i + 1 < text.Length && (text[i + 1] == c || (c == '-' && text[i + 1] == '.')))
                break;

            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~')
                break;

            // %% starts a comment
            if (c == '%' && i + 1 < text.Length && text[i + 1] == '%')
                break;

            i++;
        }

        return i;
    }

    /// <summary>Indicates whether an identifier is a keyword that starts a statement, and cannot name a node.</summary>
    internal static bool IsKeyword(string id) => id is "end" or "subgraph" or "style" or "class" or "classDef" or "linkStyle" or "click" or "direction" or "graph" or "flowchart" or "flowchart-elk" or "flowchart-v2";

    /// <summary>Indicates whether an identifier reads as an accessibility statement at the start of a line.</summary>
    internal static bool IsAccessibilityStatement(string id) => id.StartsWith("accTitle:", StringComparison.Ordinal) || id.StartsWith("accDescr:", StringComparison.Ordinal);

    private string ReadWord()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && !char.IsWhiteSpace(_scanner.Current) && _scanner.Current != ';')
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private string ReadClassName()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && (char.IsLetterOrDigit(_scanner.Current) || _scanner.Current is '_' or '-'))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private List<string> ReadIdList()
    {
        var result = new List<string>();
        while (true)
        {
            var start = _scanner.Position;
            var end = SkipIdentifier(start);
            if (end == start)
                throw _scanner.Error("An identifier is expected.");

            result.Add(_scanner.Text[start..end]);
            _scanner.Position = end;
            _scanner.SkipSpaces();
            if (!_scanner.TryConsume(","))
                return result;

            _scanner.SkipSpaces();
        }
    }

    private bool TryGetOpenSubgraph(string id, out SubgraphInfo subgraph)
    {
        foreach (var open in _open)
        {
            if (string.Equals(open.Id, id, StringComparison.Ordinal))
            {
                subgraph = open;
                return true;
            }
        }

        subgraph = null!;
        return false;
    }

    private Vertex GetVertex(string id, int offset)
    {
        if (!_vertices.TryGetValue(id, out var vertex))
        {
            if (IsKeyword(id) || IsAccessibilityStatement(id))
                throw _scanner.Error(offset, $"'{id}' is a keyword and cannot be a node identifier.");

            if (_vertices.Count >= _context.Options.MaxNodes)
                throw _scanner.Error(offset, $"The flowchart has more than {_context.Options.MaxNodes} nodes.");

            vertex = new Vertex(id);
            _vertices[id] = vertex;
            _vertexOrder.Add(vertex);
        }

        return vertex;
    }

    private void Build()
    {
        var subgraphs = new Dictionary<SubgraphInfo, FlowchartSubgraph>();
        foreach (var info in _subgraphs)
        {
            var subgraph = new FlowchartSubgraph(info.Id, info.Title) { Direction = info.Direction, Style = info.Style };
            foreach (var className in info.Classes)
            {
                subgraph.CssClasses.Add(className);
            }

            subgraphs[info] = subgraph;
        }

        // The subgraphs nest in the order they are opened
        foreach (var info in _subgraphs.OrderBy(info => info.Offset))
        {
            if (info.Parent is { } parent && subgraphs.TryGetValue(parent, out var parentSubgraph))
            {
                parentSubgraph.Subgraphs.Add(subgraphs[info]);
            }
            else
            {
                _diagram.Subgraphs.Add(subgraphs[info]);
            }
        }

        var memberOf = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in _subgraphs)
        {
            memberOf.UnionWith(info.Members);
        }

        var nodes = new Dictionary<string, FlowchartNode>(StringComparer.Ordinal);
        foreach (var vertex in _vertexOrder)
        {
            if (_subgraphsById.ContainsKey(vertex.Id))
                continue;

            var node = new FlowchartNode(vertex.Id, vertex.Label, vertex.Shape ?? FlowchartNodeShape.Rectangle)
            {
                Style = vertex.Style,
                Link = vertex.Link,
                Tooltip = vertex.Tooltip,
            };
            foreach (var className in vertex.Classes)
            {
                node.CssClasses.Add(className);
            }

            nodes[vertex.Id] = node;
            if (!memberOf.Contains(vertex.Id))
            {
                _diagram.Nodes.Add(node);
            }
        }

        // The nodes of a subgraph are in the order they are mentioned in it
        foreach (var info in _subgraphs)
        {
            foreach (var member in info.Members)
            {
                if (nodes.TryGetValue(member, out var node))
                {
                    subgraphs[info].Nodes.Add(node);
                }
            }
        }

        var edges = new List<FlowchartEdge>(_edges.Count);
        foreach (var info in _edges)
        {
            var source = Resolve(info.Source);
            var target = Resolve(info.Target);
            var edge = new FlowchartEdge(source, target, info.Link.Label)
            {
                Id = info.Link.Id,
                LineStyle = info.Link.LineStyle,
                StartArrowHead = info.Link.StartHead,
                EndArrowHead = info.Link.EndHead,
                Length = info.Link.Length,
                Curve = info.Curve,
            };
            foreach (var className in info.Classes)
            {
                edge.CssClasses.Add(className);
            }

            edges.Add(edge);
            _diagram.Edges.Add(edge);
        }

        foreach (var (ids, style, curve, offset) in _linkStyles)
        {
            if (string.Equals(ids, "default", StringComparison.Ordinal))
            {
                _diagram.DefaultEdgeStyle = MermaidStyle.IsEmpty(style) ? _diagram.DefaultEdgeStyle : (_diagram.DefaultEdgeStyle ?? new DiagramStyle()).Merge(style);
                if (curve is { } defaultCurve)
                {
                    _diagram.Curve = defaultCurve;
                }

                continue;
            }

            foreach (var part in ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= edges.Count)
                    throw _context.Error(offset, $"The edge {part} of linkStyle does not exist: the flowchart has {edges.Count} edges.");

                var edge = edges[index];
                if (!MermaidStyle.IsEmpty(style))
                {
                    edge.Style = edge.Style is null ? style : edge.Style.Merge(style);
                }

                if (curve is { } edgeCurve)
                {
                    edge.Curve = edgeCurve;
                }
            }
        }

        FlowchartElement Resolve(string id)
        {
            if (_subgraphsById.TryGetValue(id, out var info))
                return subgraphs[info];

            return nodes[id];
        }
    }

    private sealed class Vertex(string id)
    {
        public string Id { get; } = id;

        public DiagramText? Label { get; set; }

        public FlowchartNodeShape? Shape { get; set; }

        public List<string> Classes { get; } = [];

        public DiagramStyle? Style { get; set; }

        public string? Link { get; set; }

        public string? Tooltip { get; set; }
    }

    private sealed class SubgraphInfo(string id, DiagramText? title, int offset, SubgraphInfo? parent)
    {
        public string Id { get; } = id;

        public DiagramText? Title { get; } = title;

        public int Offset { get; } = offset;

        public SubgraphInfo? Parent { get; } = parent;

        public DiagramDirection? Direction { get; set; }

        public DiagramStyle? Style { get; set; }

        public List<string> Classes { get; } = [];

        public List<string> Members { get; } = [];

        public List<SubgraphInfo> Children { get; } = [];
    }

    private sealed record LinkInfo(FlowchartLineStyle LineStyle, FlowchartArrowHead StartHead, FlowchartArrowHead EndHead, int Length, string? Id, DiagramText? Label);

    private sealed class EdgeInfo(string source, string target, LinkInfo link, int offset)
    {
        public string Source { get; } = source;

        public string Target { get; } = target;

        public LinkInfo Link { get; } = link;

        public int Offset { get; } = offset;

        public EdgeCurve? Curve { get; set; }

        public List<string> Classes { get; } = [];
    }
}
