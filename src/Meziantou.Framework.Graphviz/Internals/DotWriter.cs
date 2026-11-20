using System.Text;

namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>
/// Writes a flowchart as DOT. The attributes of each cluster and of the graph are written after their content, so that
/// the subgraphs, which start with the attributes of their parent graph, do not inherit them when the text is read back.
/// </summary>
internal sealed class DotWriter
{
    private const string Indentation = "    ";

    private readonly FlowchartDiagram _diagram;
    private readonly StringBuilder _builder = new();
    private readonly Dictionary<FlowchartNode, FlowchartSubgraph?> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FlowchartNode> _written = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FlowchartSubgraph> _writtenSubgraphs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FlowchartSubgraph, FlowchartSubgraph?> _parents = new(ReferenceEqualityComparer.Instance);

    private DotWriter(FlowchartDiagram diagram)
    {
        _diagram = diagram;
    }

    public static string Write(FlowchartDiagram diagram) => new DotWriter(diagram).Write();

    private string Write()
    {
        ComputeOwners();
        _builder.Append("digraph {\n");
        foreach (var node in _diagram.Nodes)
        {
            if (_owners.TryGetValue(node, out var owner) && owner is null)
            {
                WriteNode(node, 1);
            }
        }

        foreach (var subgraph in _diagram.Subgraphs)
        {
            WriteSubgraph(subgraph, 1);
        }

        // The nodes that only the edges mention
        foreach (var edge in _diagram.Edges)
        {
            foreach (var end in new[] { edge.Source, edge.Target })
            {
                if (end is FlowchartNode node && !_written.Contains(node))
                {
                    WriteNode(node, 1);
                }
            }
        }

        var compound = false;
        foreach (var edge in _diagram.Edges)
        {
            compound |= WriteEdge(edge);
        }

        if (_diagram.Title is { Length: > 0 } title)
        {
            WriteGraphAttribute("label", EscapeText(title));
        }

        var direction = _diagram.Direction switch
        {
            DiagramDirection.BottomToTop => "BT",
            DiagramDirection.LeftToRight => "LR",
            DiagramDirection.RightToLeft => "RL",
            _ => null,
        };
        if (direction is not null)
        {
            WriteGraphAttribute("rankdir", direction);
        }

        if (_diagram.NodeSpacing != 50)
        {
            WriteGraphAttribute("nodesep", FormatId(DotValues.FormatNumber(ToInches(_diagram.NodeSpacing))));
        }

        if (_diagram.RankSpacing != 50)
        {
            WriteGraphAttribute("ranksep", FormatId(DotValues.FormatNumber(ToInches(_diagram.RankSpacing))));
        }

        var splines = _diagram.Curve switch
        {
            EdgeCurve.Linear => "line",
            EdgeCurve.Step => "ortho",
            _ => null,
        };
        if (splines is not null)
        {
            WriteGraphAttribute("splines", splines);
        }

        // The background of the default theme is the default of Graphviz
        if (_diagram.Theme.Background is { } background && background != DiagramTheme.Default.Background)
        {
            WriteGraphAttribute("bgcolor", FormatId(background.ToString()));
        }

        if (compound)
        {
            WriteGraphAttribute("compound", "true");
        }

        _builder.Append("}\n");
        return _builder.ToString();
    }

    /// <summary>Finds the innermost subgraph of each node, as the flowchart draws it.</summary>
    // Pixels to inches, rounded: reading the value back rounds it to the same number of pixels
    private static double ToInches(double pixels) => Math.Round(pixels / 72, 9, MidpointRounding.AwayFromZero);

    private void ComputeOwners()
    {
        foreach (var node in _diagram.Nodes)
        {
            _owners.TryAdd(node, null);
        }

        var depths = new Dictionary<FlowchartSubgraph, int>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(FlowchartSubgraph Subgraph, FlowchartSubgraph? Parent, int Depth)>();
        for (var i = _diagram.Subgraphs.Count - 1; i >= 0; i--)
        {
            stack.Push((_diagram.Subgraphs[i], null, 1));
        }

        while (stack.Count > 0)
        {
            var (subgraph, parent, depth) = stack.Pop();
            if (!depths.TryAdd(subgraph, depth))
                continue;

            _parents[subgraph] = parent;
            foreach (var node in subgraph.Nodes)
            {
                if (!_owners.TryGetValue(node, out var owner) || owner is null || depths[owner] < depth)
                {
                    _owners[node] = subgraph;
                }
            }

            for (var i = subgraph.Subgraphs.Count - 1; i >= 0; i--)
            {
                stack.Push((subgraph.Subgraphs[i], subgraph, depth + 1));
            }
        }
    }

    private void WriteSubgraph(FlowchartSubgraph subgraph, int depth)
    {
        if (!_writtenSubgraphs.Add(subgraph))
            return;

        Indent(depth);
        _builder.Append("subgraph ").Append(FormatId(subgraph.Id)).Append(" {\n");
        foreach (var node in subgraph.Nodes)
        {
            if (_owners.TryGetValue(node, out var owner) && ReferenceEquals(owner, subgraph))
            {
                WriteNode(node, depth + 1);
            }
        }

        foreach (var child in subgraph.Subgraphs)
        {
            WriteSubgraph(child, depth + 1);
        }

        var attributes = new List<(string Name, string Value)>
        {
            ("label", EscapeText(subgraph.Label?.Text ?? subgraph.Id)),
        };

        if (!subgraph.Id.StartsWith("cluster", StringComparison.OrdinalIgnoreCase))
        {
            attributes.Add(("cluster", "true"));
        }

        var style = ResolveStyle(subgraph.CssClasses, subgraph.Style);
        var styles = new List<string>();
        AddDashStyle(styles, style?.StrokeDashArray);
        if (style?.Opacity is 0)
        {
            styles.Add("invis");
        }

        AddStyle(attributes, styles);
        AddColor(attributes, "bgcolor", style?.Fill);
        AddColor(attributes, "pencolor", style?.Stroke);
        AddCommonAttributes(attributes, style, subgraph);
        foreach (var (name, value) in attributes)
        {
            Indent(depth + 1);
            _builder.Append(name).Append('=').Append(value).Append(";\n");
        }

        Indent(depth);
        _builder.Append("}\n");
    }

    private void WriteNode(FlowchartNode node, int depth)
    {
        if (!_written.Add(node))
            return;

        var attributes = new List<(string Name, string Value)>();
        if (node.Label is { } label)
        {
            attributes.Add(("label", EscapeText(label.Text)));
        }

        attributes.Add(("shape", DotShapes.ToDot(node.Shape)));
        var style = ResolveStyle(node.CssClasses, node.Style, includeDefaultClass: true);
        var styles = new List<string>();
        if (node.Shape is FlowchartNodeShape.RoundedRectangle)
        {
            styles.Add("rounded");
        }

        if (style?.Fill is not null)
        {
            styles.Add("filled");
        }

        AddDashStyle(styles, style?.StrokeDashArray);
        if (style?.Opacity is 0)
        {
            styles.Add("invis");
        }

        AddStyle(attributes, styles);
        AddColor(attributes, "fillcolor", style?.Fill);
        AddColor(attributes, "color", style?.Stroke);
        AddCommonAttributes(attributes, style, node);
        Indent(depth);
        _builder.Append(FormatId(node.Id));
        WriteAttributeList(attributes);
        _builder.Append(";\n");
    }

    /// <summary>Writes an edge. An end that is a subgraph is written as a node of the subgraph, clipped at its frame (<c>lhead</c>, <c>ltail</c>).</summary>
    /// <returns><see langword="true"/> when the edge is clipped at a cluster, which needs <c>compound=true</c>.</returns>
    private bool WriteEdge(FlowchartEdge edge)
    {
        var tail = GetRepresentative(edge.Source);
        var head = GetRepresentative(edge.Target);
        if (tail is null || head is null)
            return false;

        var attributes = new List<(string Name, string Value)>();
        var compound = false;
        if (edge.Source is FlowchartSubgraph sourceSubgraph && !Contains(sourceSubgraph, head))
        {
            attributes.Add(("ltail", FormatId(sourceSubgraph.Id)));
            compound = true;
        }

        if (edge.Target is FlowchartSubgraph targetSubgraph && !Contains(targetSubgraph, tail))
        {
            attributes.Add(("lhead", FormatId(targetSubgraph.Id)));
            compound = true;
        }

        if (edge.Label is { } label)
        {
            attributes.Add(("label", EscapeText(label.Text)));
        }

        switch (edge.StartArrowHead, edge.EndArrowHead)
        {
            case (FlowchartArrowHead.None, FlowchartArrowHead.None):
                attributes.Add(("dir", "none"));
                break;
            case (FlowchartArrowHead.None, var end):
                AddArrowHead(attributes, "arrowhead", end);
                break;
            case (var start, FlowchartArrowHead.None):
                attributes.Add(("dir", "back"));
                AddArrowHead(attributes, "arrowtail", start);
                break;
            case var (start, end):
                attributes.Add(("dir", "both"));
                AddArrowHead(attributes, "arrowhead", end);
                AddArrowHead(attributes, "arrowtail", start);
                break;
        }

        var style = _diagram.DefaultEdgeStyle;
        foreach (var name in edge.CssClasses)
        {
            if (_diagram.ClassDefinitions.TryGetValue(name, out var definition))
            {
                style = style?.Merge(definition) ?? definition;
            }
        }

        style = style?.Merge(edge.Style) ?? edge.Style;
        var styles = new List<string>();
        switch (edge.LineStyle)
        {
            case FlowchartLineStyle.Dotted:
                styles.Add(IsPattern(style?.StrokeDashArray, DotFlowchartBuilder.DashedPattern) ? "dashed" : "dotted");
                break;
            case FlowchartLineStyle.Thick:
                styles.Add("bold");
                AddDashStyle(styles, style?.StrokeDashArray);
                break;
            case FlowchartLineStyle.Invisible:
                styles.Add("invis");
                break;
            default:
                // The dashes of a solid edge can come from its style, its classes or the default style of the edges
                AddDashStyle(styles, style?.StrokeDashArray);
                break;
        }

        AddStyle(attributes, styles);
        AddColor(attributes, "color", style?.Stroke);
        AddColor(attributes, "fontcolor", style?.TextColor);
        AddNumber(attributes, "penwidth", style?.StrokeWidth);
        AddNumber(attributes, "fontsize", style?.FontSize);
        if (edge.Length != 1)
        {
            attributes.Add(("minlen", FormatId(DotValues.FormatNumber(edge.Length))));
        }

        AddClasses(attributes, edge.CssClasses);
        Indent(1);
        _builder.Append(FormatId(tail.Id)).Append(" -> ").Append(FormatId(head.Id));
        WriteAttributeList(attributes);
        _builder.Append(";\n");
        return compound;
    }

    /// <summary>Gets the node an edge attaches to: the node, or the first node of a subgraph, depth first.</summary>
    private static FlowchartNode? GetRepresentative(FlowchartElement element)
    {
        if (element is FlowchartNode node)
            return node;

        var visited = new HashSet<FlowchartSubgraph>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<FlowchartSubgraph>();
        stack.Push((FlowchartSubgraph)element);
        while (stack.Count > 0)
        {
            var subgraph = stack.Pop();
            if (!visited.Add(subgraph))
                continue;

            if (subgraph.Nodes.Count > 0)
                return subgraph.Nodes[0];

            for (var i = subgraph.Subgraphs.Count - 1; i >= 0; i--)
            {
                stack.Push(subgraph.Subgraphs[i]);
            }
        }

        return null;
    }

    /// <summary>Gets whether a node is drawn in a subgraph or in one of its nested subgraphs.</summary>
    private bool Contains(FlowchartSubgraph subgraph, FlowchartNode node)
    {
        if (!_owners.TryGetValue(node, out var owner))
            return false;

        for (var current = owner; current is not null; current = _parents.GetValueOrDefault(current))
        {
            if (ReferenceEquals(current, subgraph))
                return true;
        }

        return false;
    }

    private DiagramStyle? ResolveStyle(IList<string> classes, DiagramStyle? style, bool includeDefaultClass = false)
    {
        // The renderer applies the "default" class to every node, before the classes of the node
        DiagramStyle? result = null;
        if (includeDefaultClass && _diagram.ClassDefinitions.TryGetValue("default", out var defaultDefinition))
        {
            result = defaultDefinition;
        }

        foreach (var name in classes)
        {
            if (_diagram.ClassDefinitions.TryGetValue(name, out var definition))
            {
                result = result?.Merge(definition) ?? definition;
            }
        }

        return result?.Merge(style) ?? style;
    }

    private static void AddCommonAttributes(List<(string Name, string Value)> attributes, DiagramStyle? style, FlowchartElement element)
    {
        AddColor(attributes, "fontcolor", style?.TextColor);
        AddNumber(attributes, "penwidth", style?.StrokeWidth);
        AddNumber(attributes, "fontsize", style?.FontSize);
        if (element.Link is { Length: > 0 } link)
        {
            attributes.Add(("URL", EscapeText(link)));
        }

        if (element.Tooltip is { Length: > 0 } tooltip)
        {
            attributes.Add(("tooltip", EscapeText(tooltip)));
        }

        AddClasses(attributes, element.CssClasses);
    }

    private static void AddClasses(List<(string Name, string Value)> attributes, IList<string> classes)
    {
        var names = classes.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        if (names.Length > 0)
        {
            attributes.Add(("class", FormatId(string.Join(' ', names))));
        }
    }

    private static void AddDashStyle(List<string> styles, IReadOnlyList<double>? dashArray)
    {
        if (IsPattern(dashArray, DotFlowchartBuilder.DashedPattern))
        {
            styles.Add("dashed");
        }
        else if (IsPattern(dashArray, DotFlowchartBuilder.DottedPattern))
        {
            styles.Add("dotted");
        }
    }

    private static bool IsPattern(IReadOnlyList<double>? dashArray, double[] pattern) => dashArray is not null && dashArray.SequenceEqual(pattern);

    private static void AddStyle(List<(string Name, string Value)> attributes, List<string> styles)
    {
        if (styles.Count > 0)
        {
            attributes.Add(("style", FormatId(string.Join(',', styles))));
        }
    }

    private static void AddColor(List<(string Name, string Value)> attributes, string name, SceneColor? color)
    {
        if (color is { } value)
        {
            attributes.Add((name, FormatId(value.ToString())));
        }
    }

    private static void AddNumber(List<(string Name, string Value)> attributes, string name, double? value)
    {
        if (value is { } number && double.IsFinite(number))
        {
            attributes.Add((name, FormatId(DotValues.FormatNumber(number))));
        }
    }

    private static void AddArrowHead(List<(string Name, string Value)> attributes, string name, FlowchartArrowHead arrowHead)
    {
        if (arrowHead is not FlowchartArrowHead.Arrow)
        {
            attributes.Add((name, DotShapes.ArrowToDot(arrowHead)));
        }
    }

    private void WriteAttributeList(List<(string Name, string Value)> attributes)
    {
        if (attributes.Count is 0)
            return;

        _builder.Append(" [");
        for (var i = 0; i < attributes.Count; i++)
        {
            if (i > 0)
            {
                _builder.Append(", ");
            }

            _builder.Append(attributes[i].Name).Append('=').Append(attributes[i].Value);
        }

        _builder.Append(']');
    }

    private void WriteGraphAttribute(string name, string value)
    {
        Indent(1);
        _builder.Append(name).Append('=').Append(value).Append(";\n");
    }

    private void Indent(int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            _builder.Append(Indentation);
        }
    }

    /// <summary>Writes a text that Graphviz reads with its escapes (labels, links, tooltips): the backslashes are doubled and the line feeds are <c>\n</c>.</summary>
    internal static string EscapeText(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        // Reading removes the line break that ends the last line
        if (text.EndsWith("\n", StringComparison.Ordinal))
        {
            builder.Append("\\n");
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Writes an identifier as is when possible, else between quotes, else as an HTML string.</summary>
    internal static string FormatId(string id)
    {
        if (IsBareIdentifier(id) || IsNumeral(id))
            return id;

        var quoted = id.Replace("\"", "\\\"", StringComparison.Ordinal);
        if (ReadQuoted(quoted) == id)
            return "\"" + quoted + "\"";

        if (IsBalanced(id))
            return "<" + id + ">";

        throw new ArgumentException($"The identifier '{id}' cannot be written in DOT: it ends with an odd number of backslashes and has unbalanced angle brackets.", nameof(id));
    }

    private static bool IsBareIdentifier(string id)
    {
        if (id.Length is 0 || !DotLexer.IsIdentifierStart(id[0]) || DotParser.IsKeyword(id))
            return false;

        foreach (var c in id)
        {
            if (!DotLexer.IsIdentifierPart(c))
                return false;
        }

        return true;
    }

    private static bool IsNumeral(string id)
    {
        var i = 0;
        if (i < id.Length && id[i] is '-')
        {
            i++;
        }

        var digits = 0;
        while (i < id.Length && char.IsAsciiDigit(id[i]))
        {
            i++;
            digits++;
        }

        if (i < id.Length && id[i] is '.')
        {
            i++;
            while (i < id.Length && char.IsAsciiDigit(id[i]))
            {
                i++;
                digits++;
            }
        }

        return digits > 0 && i == id.Length;
    }

    private static bool IsBalanced(string id)
    {
        var depth = 0;
        foreach (var c in id)
        {
            if (c is '<')
            {
                depth++;
            }
            else if (c is '>')
            {
                depth--;
                if (depth < 0)
                    return false;
            }
        }

        return depth is 0;
    }

    /// <summary>Reads the content of a quoted string as the lexer does, or returns <see langword="null"/> when the string would end before its last character.</summary>
    private static string? ReadQuoted(string content)
    {
        var builder = new StringBuilder(content.Length);
        var i = 0;
        while (i < content.Length)
        {
            var c = content[i];
            if (c is '"')
                return null;

            if (c is '\\')
            {
                if (i + 1 >= content.Length)
                    return null;

                switch (content[i + 1])
                {
                    case '"':
                        builder.Append('"');
                        i += 2;
                        continue;
                    case '\\':
                        builder.Append("\\\\");
                        i += 2;
                        continue;
                    case '\n':
                        i += 2;
                        continue;
                    case '\r':
                        i += i + 2 < content.Length && content[i + 2] is '\n' ? 3 : 2;
                        continue;
                }
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }
}
