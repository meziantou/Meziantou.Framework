namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>Converts a DOT graph to a flowchart: the clusters become subgraphs, the attributes become shapes, styles and labels.</summary>
internal sealed class DotFlowchartBuilder
{
    internal static readonly double[] DashedPattern = [5, 2];
    internal static readonly double[] DottedPattern = [1, 3];

    private const double PointsPerInch = 72;
    private const double MaxSpacing = 1000;
    private const double MaxStrokeWidth = 100;
    private const double MinFontSize = 1;
    private const double MaxFontSize = 500;
    private static readonly SceneColor DefaultFillColor = new(211, 211, 211);

    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.Ordinal) { "comment", "charset" };
    private static readonly HashSet<string> NodeAttributes = new(StringComparer.Ordinal) { "label", "shape", "style", "color", "fillcolor", "fontcolor", "penwidth", "fontsize", "URL", "href", "tooltip", "class", "sides" };
    private static readonly HashSet<string> EdgeAttributes = new(StringComparer.Ordinal) { "label", "style", "color", "fontcolor", "penwidth", "fontsize", "arrowhead", "arrowtail", "dir", "minlen", "class", "lhead", "ltail" };
    private static readonly HashSet<string> ClusterAttributes = new(StringComparer.Ordinal) { "label", "style", "color", "pencolor", "fillcolor", "bgcolor", "fontcolor", "penwidth", "fontsize", "URL", "href", "tooltip", "class", "cluster", "rank" };
    private static readonly HashSet<string> GraphAttributes = new(StringComparer.Ordinal) { "label", "rankdir", "nodesep", "ranksep", "splines", "bgcolor", "compound", "layout" };

    private readonly DotContext _context;
    private readonly DotGraph _graph;
    private readonly FlowchartDiagram _diagram = new();
    private readonly Dictionary<DotSubgraph, FlowchartSubgraph> _clusters = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DotNode, FlowchartNode> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, DotSubgraph> _clustersByName = new(StringComparer.Ordinal);

    private DotFlowchartBuilder(DotContext context, DotGraph graph)
    {
        _context = context;
        _graph = graph;
    }

    public static FlowchartDiagram Build(DotContext context, DotGraph graph) => new DotFlowchartBuilder(context, graph).Build();

    private FlowchartDiagram Build()
    {
        if (_context.Options.DefaultTheme is { } theme)
        {
            _diagram.Theme = theme;
        }

        ApplyGraphAttributes();
        CreateClusters();
        CreateNodes();
        CreateEdges();
        return _diagram;
    }

    private void ApplyGraphAttributes()
    {
        var attributes = _graph.Root.Attributes;
        foreach (var (name, attribute) in attributes.Values)
        {
            if (!GraphAttributes.Contains(name) && !IgnoredAttributes.Contains(name))
            {
                _context.WarnOnce("graph:" + name, attribute.Position, $"The attribute '{name}' of the graph is not supported and is ignored.");
            }
        }

        if (attributes.Get("label") is { } label && ConvertLabel(label, graphName: _graph.Name) is { IsEmpty: false } title)
        {
            _diagram.Title = title.Text;
        }

        if (attributes.Get("rankdir") is { } rankdir && rankdir.Value.Length > 0)
        {
            DiagramDirection? direction = rankdir.Value.Trim().ToUpperInvariant() switch
            {
                "TB" => DiagramDirection.TopToBottom,
                "BT" => DiagramDirection.BottomToTop,
                "LR" => DiagramDirection.LeftToRight,
                "RL" => DiagramDirection.RightToLeft,
                _ => null,
            };

            if (direction is { } value)
            {
                _diagram.Direction = value;
            }
            else
            {
                WarnInvalid(rankdir, "rankdir");
            }
        }

        if (GetNumber(attributes, "nodesep") is { } nodeSpacing)
        {
            _diagram.NodeSpacing = ToPixels(nodeSpacing);
        }

        if (attributes.Get("ranksep") is { } rankSeparation && rankSeparation.Value.Length > 0)
        {
            // "1.2 equally": the number comes first
            var value = rankSeparation.Value.Trim().Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (DotValues.TryParseNumber(value, out var number))
            {
                _diagram.RankSpacing = ToPixels(number);
            }
            else
            {
                WarnInvalid(rankSeparation, "ranksep");
            }
        }

        if (attributes.Get("splines") is { } splines)
        {
            switch (splines.Value.Trim().ToLowerInvariant())
            {
                case "line" or "false" or "polyline":
                    _diagram.Curve = EdgeCurve.Linear;
                    break;
                case "ortho":
                    _diagram.Curve = EdgeCurve.Step;
                    break;
                case "spline" or "true" or "curved" or "compound":
                    _diagram.Curve = EdgeCurve.Basis;
                    break;
                default:
                    WarnInvalid(splines, "splines");
                    break;
            }
        }

        if (attributes.Get("bgcolor") is { } background && GetColor(background) is { } backgroundColor)
        {
            _diagram.Theme = _diagram.Theme with { Background = backgroundColor };
        }

        if (attributes.Get("layout") is { } layout && !layout.Value.Trim().Equals("dot", StringComparison.OrdinalIgnoreCase) && layout.Value.Length > 0)
        {
            _context.WarnOnce("layout", layout.Position, $"The layout engine '{layout.Value}' is not supported: the graph is drawn with the layered layout of dot.");
        }
    }

    private void CreateClusters()
    {
        var usedIds = new HashSet<string>(_graph.NodesById.Keys, StringComparer.Ordinal);
        foreach (var subgraph in _graph.Subgraphs)
        {
            if (subgraph.Attributes.Get("rank") is { IsInherited: false } rank && rank.Value.Length > 0)
            {
                _context.WarnOnce("rank", rank.Position, "The ranks of the subgraphs (rank=same, min, max, source, sink) are not supported: the layout places the nodes.");
            }

            if (!IsCluster(subgraph))
                continue;

            var id = subgraph.Name ?? subgraph.GeneratedName!;
            if (!usedIds.Add(id))
            {
                // A node and a cluster can have the same name in DOT, not in a flowchart
                var index = 1;
                while (!usedIds.Add(id + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    index++;
                }

                id = id + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var cluster = new FlowchartSubgraph(id);
            ApplyClusterAttributes(subgraph, cluster);
            _clusters.Add(subgraph, cluster);
            if (subgraph.Name is not null)
            {
                _clustersByName.TryAdd(subgraph.Name, subgraph);
            }

            if (GetParentCluster(subgraph) is { } parent)
            {
                _clusters[parent].Subgraphs.Add(cluster);
            }
            else
            {
                _diagram.Subgraphs.Add(cluster);
            }
        }
    }

    private static bool IsCluster(DotSubgraph subgraph)
    {
        if (subgraph.Name is { } name && name.StartsWith("cluster", StringComparison.OrdinalIgnoreCase))
            return true;

        return DotValues.ParseBoolean(subgraph.Attributes.GetValue("cluster"));
    }

    private DotSubgraph? GetParentCluster(DotSubgraph subgraph)
    {
        for (var current = subgraph.Parent; current is not null; current = current.Parent)
        {
            if (_clusters.ContainsKey(current))
                return current;
        }

        return null;
    }

    private DotSubgraph? GetCluster(DotSubgraph subgraph)
    {
        for (var current = subgraph; current is not null; current = current.Parent)
        {
            if (_clusters.ContainsKey(current))
                return current;
        }

        return null;
    }

    private void ApplyClusterAttributes(DotSubgraph subgraph, FlowchartSubgraph cluster)
    {
        var attributes = subgraph.Attributes;
        foreach (var (name, attribute) in attributes.Values)
        {
            if (!attribute.IsInherited && !ClusterAttributes.Contains(name) && !IgnoredAttributes.Contains(name))
            {
                _context.WarnOnce("cluster:" + name, attribute.Position, $"The attribute '{name}' of the clusters is not supported and is ignored.");
            }
        }

        cluster.Label = attributes.Get("label") is { } label ? ConvertLabel(label, graphName: subgraph.Name) : new DiagramText(string.Empty);
        var style = ParseStyle(attributes, "clusters");
        if (style.Rounded && attributes.Get("style") is { IsInherited: false } styleAttribute)
        {
            _context.WarnOnce("cluster-rounded", styleAttribute.Position, "The rounded corners of the clusters are not supported.");
        }

        var fill = style.Filled
            ? GetColor(attributes, "fillcolor") ?? GetColor(attributes, "color") ?? GetColor(attributes, "bgcolor") ?? DefaultFillColor
            : GetColor(attributes, "bgcolor");
        cluster.Style = CreateStyle(
            fill,
            GetColor(attributes, "pencolor") ?? GetColor(attributes, "color"),
            GetStrokeWidth(attributes, style),
            style.DashArray,
            GetColor(attributes, "fontcolor"),
            GetFontSize(attributes),
            style.Invisible);
        cluster.Link = GetLink(attributes);
        cluster.Tooltip = GetEscapedText(attributes, "tooltip");
        AddClasses(attributes, cluster.CssClasses);
    }

    private void CreateNodes()
    {
        foreach (var node in _graph.Nodes)
        {
            // A node mentioned in several clusters belongs to the innermost one; between two unrelated clusters, to the first
            DotSubgraph? owner = null;
            foreach (var mention in node.Mentions)
            {
                var cluster = GetCluster(mention);
                if (cluster is null)
                    continue;

                if (owner is null || cluster.IsDescendantOf(owner))
                {
                    owner = cluster;
                }
            }

            var flowchartNode = new FlowchartNode(node.Id);
            ApplyNodeAttributes(node, flowchartNode);
            _nodes.Add(node, flowchartNode);
            if (owner is null)
            {
                _diagram.Nodes.Add(flowchartNode);
            }
            else
            {
                _clusters[owner].Nodes.Add(flowchartNode);
            }
        }
    }

    private void ApplyNodeAttributes(DotNode node, FlowchartNode flowchartNode)
    {
        var attributes = node.Attributes;
        foreach (var (name, attribute) in attributes.Values)
        {
            if (!NodeAttributes.Contains(name) && !IgnoredAttributes.Contains(name))
            {
                _context.WarnOnce("node:" + name, attribute.Position, $"The attribute '{name}' of the nodes is not supported and is ignored.");
            }
        }

        var style = ParseStyle(attributes, "nodes");
        var shapeName = attributes.GetValue("shape") ?? "ellipse";
        var shape = DotShapes.FromDot(shapeName, style.Rounded, attributes.GetValue("sides"));
        if (shape is null)
        {
            _context.WarnOnce("shape:" + shapeName, attributes.Get("shape")!.Value.Position, $"The shape '{shapeName}' is not a shape of Graphviz: the node is drawn as a box.");
            shape = FlowchartNodeShape.Rectangle;
        }

        flowchartNode.Shape = shape.Value;
        var isRecord = shapeName.Equals("record", StringComparison.OrdinalIgnoreCase) || shapeName.Equals("Mrecord", StringComparison.OrdinalIgnoreCase);
        if (attributes.Get("label") is { } label && !(label.Value is "\\N" && !label.IsHtml))
        {
            if (isRecord && !label.IsHtml)
            {
                _context.WarnOnce("record", label.Position, "The fields of the record shapes are drawn as the lines of a label, without their separators and their ports.");
                flowchartNode.Label = DotValues.RecordToText(label.Value, value => DotValues.Unescape(value, nodeName: node.Id, graphName: _graph.Name));
            }
            else
            {
                flowchartNode.Label = ConvertLabel(label, nodeName: node.Id, graphName: _graph.Name);
            }
        }

        var fill = style.Filled ? GetColor(attributes, "fillcolor") ?? GetColor(attributes, "color") ?? DefaultFillColor : (SceneColor?)null;
        flowchartNode.Style = CreateStyle(
            fill,
            GetColor(attributes, "color"),
            GetStrokeWidth(attributes, style),
            style.DashArray,
            GetColor(attributes, "fontcolor"),
            GetFontSize(attributes),
            style.Invisible);
        flowchartNode.Link = GetLink(attributes, node.Id);
        flowchartNode.Tooltip = GetEscapedText(attributes, "tooltip", node.Id);
        AddClasses(attributes, flowchartNode.CssClasses);
    }

    private void CreateEdges()
    {
        var compound = DotValues.ParseBoolean(_graph.Root.Attributes.GetValue("compound"));
        foreach (var edge in _graph.Edges)
        {
            var attributes = edge.Attributes;
            foreach (var (name, attribute) in attributes.Values)
            {
                if (!EdgeAttributes.Contains(name) && !IgnoredAttributes.Contains(name))
                {
                    _context.WarnOnce("edge:" + name, attribute.Position, $"The attribute '{name}' of the edges is not supported and is ignored.");
                }
            }

            FlowchartElement source = _nodes[edge.Tail];
            FlowchartElement target = _nodes[edge.Head];
            if (compound)
            {
                // The edge is clipped at the frame of a cluster that contains its end, but not its other end
                if (GetEdgeCluster(attributes.Get("ltail"), edge.Tail, edge.Head) is { } tailCluster)
                {
                    source = tailCluster;
                }

                if (GetEdgeCluster(attributes.Get("lhead"), edge.Head, edge.Tail) is { } headCluster)
                {
                    target = headCluster;
                }
            }

            var edgeName = edge.Tail.Id + (_graph.IsDirected ? "->" : "--") + edge.Head.Id;
            DiagramText? label = null;
            if (attributes.Get("label") is { } labelAttribute
                && ConvertLabel(labelAttribute, graphName: _graph.Name, tailName: edge.Tail.Id, headName: edge.Head.Id, edgeName: edgeName) is { IsEmpty: false } text)
            {
                label = text;
            }

            var flowchartEdge = _diagram.AddEdge(source, target, label);
            var direction = attributes.GetValue("dir")?.Trim().ToLowerInvariant() ?? (_graph.IsDirected ? "forward" : "none");
            if (direction is not ("forward" or "back" or "both" or "none"))
            {
                WarnInvalid(attributes.Get("dir")!.Value, "dir");
                direction = _graph.IsDirected ? "forward" : "none";
            }

            flowchartEdge.EndArrowHead = direction is "forward" or "both" ? GetArrowHead(attributes, "arrowhead") : FlowchartArrowHead.None;
            flowchartEdge.StartArrowHead = direction is "back" or "both" ? GetArrowHead(attributes, "arrowtail") : FlowchartArrowHead.None;

            var style = ParseStyle(attributes, "edges");
            var strokeWidth = GetNumber(attributes, "penwidth") is { } penWidth ? Math.Clamp(penWidth, 0, MaxStrokeWidth) : style.LineWidth;
            if (style.Invisible)
            {
                flowchartEdge.LineStyle = FlowchartLineStyle.Invisible;
            }
            else if (style.DashArray is not null)
            {
                flowchartEdge.LineStyle = FlowchartLineStyle.Dotted;
                if (style.Bold)
                {
                    strokeWidth ??= 2;
                }
            }
            else if (style.Bold)
            {
                flowchartEdge.LineStyle = FlowchartLineStyle.Thick;
            }

            flowchartEdge.Style = CreateStyle(
                fill: null,
                GetColor(attributes, "color"),
                strokeWidth,
                ReferenceEquals(style.DashArray, DashedPattern) && !style.Invisible ? DashedPattern : null,
                GetColor(attributes, "fontcolor"),
                GetFontSize(attributes),
                invisible: false);

            if (GetNumber(attributes, "minlen") is { } minimumLength)
            {
                flowchartEdge.Length = (int)Math.Clamp(Math.Round(minimumLength, MidpointRounding.AwayFromZero), 1, 100);
            }

            AddClasses(attributes, flowchartEdge.CssClasses);
        }
    }

    private FlowchartSubgraph? GetEdgeCluster(DotAttribute? attribute, DotNode end, DotNode otherEnd)
    {
        if (attribute is not { } value || value.Value.Length is 0)
            return null;

        if (!_clustersByName.TryGetValue(value.Value, out var subgraph) || !subgraph.Contains(end) || subgraph.Contains(otherEnd))
        {
            _context.WarnOnce("compound:" + value.Value, value.Position, $"The cluster '{value.Value}' does not exist, does not contain the end of the edge, or contains both ends: the edge is not clipped.");
            return null;
        }

        return _clusters[subgraph];
    }

    private FlowchartArrowHead GetArrowHead(DotAttributes attributes, string name)
    {
        if (attributes.Get(name) is not { } attribute || attribute.Value.Length is 0)
            return FlowchartArrowHead.Arrow;

        var (arrowHead, isExact) = DotShapes.ArrowFromDot(attribute.Value.Trim());
        if (!isExact)
        {
            _context.WarnOnce("arrow:" + attribute.Value, attribute.Position, $"The arrowhead '{attribute.Value}' is not supported: it is drawn as {(arrowHead is FlowchartArrowHead.Cross ? "a cross" : "an arrow")}.");
        }

        return arrowHead;
    }

    private DiagramText ConvertLabel(DotAttribute label, string? nodeName = null, string? graphName = null, string? tailName = null, string? headName = null, string? edgeName = null)
    {
        if (label.IsHtml)
        {
            _context.WarnOnce("html", label.Position, "The HTML-like labels are drawn as plain text: their tables, fonts and images are not supported.");
            return new DiagramText(DotValues.HtmlToText(label.Value));
        }

        return new DiagramText(DotValues.Unescape(label.Value, nodeName, graphName, tailName, headName, edgeName));
    }

    private StyleFlags ParseStyle(DotAttributes attributes, string kind)
    {
        var result = new StyleFlags();
        if (attributes.Get("style") is not { } style)
            return result;

        foreach (var item in style.Value.Split([',', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = item.ToLowerInvariant();
            switch (value)
            {
                case "filled":
                    result.Filled = true;
                    break;
                case "rounded":
                    result.Rounded = true;
                    break;
                case "dashed":
                    result.DashArray = DashedPattern;
                    break;
                case "dotted":
                    result.DashArray = DottedPattern;
                    break;
                case "bold":
                    result.Bold = true;
                    break;
                case "invis" or "invisible":
                    result.Invisible = true;
                    break;
                case "solid":
                    result.DashArray = null;
                    break;
                default:
                    if (value.StartsWith("setlinewidth(", StringComparison.Ordinal) && value.EndsWith(")", StringComparison.Ordinal) && DotValues.TryParseNumber(value["setlinewidth(".Length..^1], out var width))
                    {
                        result.LineWidth = Math.Clamp(width, 0, MaxStrokeWidth);
                    }
                    else if (!style.IsInherited)
                    {
                        _context.WarnOnce("style:" + kind + ":" + value, style.Position, $"The style '{item}' of the {kind} is not supported and is ignored.");
                    }

                    break;
            }
        }

        return result;
    }

    private double? GetStrokeWidth(DotAttributes attributes, StyleFlags style)
    {
        if (GetNumber(attributes, "penwidth") is { } penWidth)
            return Math.Clamp(penWidth, 0, MaxStrokeWidth);

        return style.LineWidth ?? (style.Bold ? 2 : null);
    }

    private double? GetFontSize(DotAttributes attributes) => GetNumber(attributes, "fontsize") is { } size ? Math.Clamp(size, MinFontSize, MaxFontSize) : null;

    private static DiagramStyle? CreateStyle(SceneColor? fill, SceneColor? stroke, double? strokeWidth, double[]? dashArray, SceneColor? textColor, double? fontSize, bool invisible)
    {
        if (fill is null && stroke is null && strokeWidth is null && dashArray is null && textColor is null && fontSize is null && !invisible)
            return null;

        return new DiagramStyle
        {
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = strokeWidth,
            StrokeDashArray = dashArray,
            TextColor = textColor,
            FontSize = fontSize,
            Opacity = invisible ? 0 : null,
        };
    }

    private string? GetEscapedText(DotAttributes attributes, string name, string? nodeName = null)
    {
        if (attributes.Get(name) is not { } attribute || attribute.Value.Length is 0)
            return null;

        var text = DotValues.Unescape(attribute.Value, nodeName, _graph.Name);
        return text.Length is 0 ? null : text;
    }

    /// <summary>Gets the link of a node or a cluster, when it cannot run a script: http, https, mailto, relative addresses and fragments.</summary>
    private string? GetLink(DotAttributes attributes, string? nodeName = null)
    {
        var name = attributes.Get("URL") is { Value.Length: > 0 } ? "URL" : "href";
        if (GetEscapedText(attributes, name, nodeName) is not { } link)
            return null;

        var trimmed = link.Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var separator = trimmed.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (separator >= 0 && separator < colon))
            return link;

        var scheme = trimmed[..colon];
        if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase))
            return link;

        _context.WarnOnce("link:" + scheme, attributes.Get(name)!.Value.Position, $"The link '{trimmed}' is ignored: only http, https, mailto and relative addresses are allowed.");
        return null;
    }

    private static void AddClasses(DotAttributes attributes, IList<string> classes)
    {
        if (attributes.GetValue("class") is not { } value)
            return;

        foreach (var name in value.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!classes.Contains(name))
            {
                classes.Add(name);
            }
        }
    }

    private SceneColor? GetColor(DotAttributes attributes, string name) => attributes.Get(name) is { } attribute ? GetColor(attribute) : null;

    private SceneColor? GetColor(DotAttribute attribute)
    {
        if (attribute.Value.Trim().Length is 0)
            return null;

        if (DotValues.TryParseColor(attribute.Value, out var color))
            return color;

        _context.WarnOnce("color:" + attribute.Value, attribute.Position, $"The color '{attribute.Value}' is not supported and is ignored.");
        return null;
    }

    private double? GetNumber(DotAttributes attributes, string name)
    {
        if (attributes.Get(name) is not { } attribute || attribute.Value.Trim().Length is 0)
            return null;

        if (DotValues.TryParseNumber(attribute.Value, out var number))
            return number;

        WarnInvalid(attribute, name);
        return null;
    }

    private void WarnInvalid(DotAttribute attribute, string name) => _context.WarnOnce("invalid:" + name + ":" + attribute.Value, attribute.Position, $"The value '{attribute.Value}' of the attribute '{name}' is not valid and is ignored.");

    // Inches to pixels, rounded so that writing the value back and reading it again gives the same number
    private static double ToPixels(double inches) => Math.Clamp(Math.Round(inches * PointsPerInch, 6, MidpointRounding.AwayFromZero), 0, MaxSpacing);

    private sealed class StyleFlags
    {
        public bool Filled { get; set; }

        public bool Rounded { get; set; }

        public bool Bold { get; set; }

        public bool Invisible { get; set; }

        public double[]? DashArray { get; set; }

        public double? LineWidth { get; set; }
    }
}
