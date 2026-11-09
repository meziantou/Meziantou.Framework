using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.Flowcharts;

/// <summary>Writes a <see cref="FlowchartDiagram"/> as Mermaid.</summary>
internal static class FlowchartWriter
{
    public static void Write(StringBuilder builder, FlowchartDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Curve is not EdgeCurve.Basis)
        {
            configuration.Add("curve: " + MermaidCommon.FormatCurve(diagram.Curve));
        }

        if (diagram.NodeSpacing != 50)
        {
            configuration.Add("nodeSpacing: " + MermaidStyle.Number(diagram.NodeSpacing));
        }

        if (diagram.RankSpacing != 50)
        {
            configuration.Add("rankSpacing: " + MermaidStyle.Number(diagram.RankSpacing));
        }

        if (diagram.WrappingWidth != 200)
        {
            configuration.Add("wrappingWidth: " + (diagram.WrappingWidth is { } width ? MermaidStyle.Number(width) : "100000"));
        }

        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "flowchart", configuration);
        builder.Append(diagram.Swimlanes ? "swimlane-beta " : "flowchart ").Append(MermaidCommon.FormatDirection(diagram.Direction)).Append('\n');
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);

        var written = new HashSet<FlowchartElement>(ReferenceEqualityComparer.Instance);
        foreach (var node in diagram.Nodes)
        {
            WriteNode(builder, node, 1, written);
        }

        foreach (var subgraph in diagram.Subgraphs)
        {
            WriteSubgraph(builder, subgraph, 1, written, depth: 0);
        }

        foreach (var edge in diagram.Edges)
        {
            // An edge to a node of no list declares it
            foreach (var end in new[] { edge.Source, edge.Target })
            {
                if (end is FlowchartNode node && !written.Contains(node))
                {
                    WriteNode(builder, node, 1, written);
                }
            }
        }

        for (var i = 0; i < diagram.Edges.Count; i++)
        {
            WriteEdge(builder, diagram.Edges[i]);
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            ValidateClassName(name);
            builder.Append("    classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var element in written)
        {
            foreach (var className in element.CssClasses)
            {
                ValidateClassName(className);
                builder.Append("    class ").Append(element.Id).Append(' ').Append(className).Append('\n');
            }

            if (!MermaidStyle.IsEmpty(element.Style))
            {
                builder.Append("    style ").Append(element.Id).Append(' ').Append(MermaidStyle.Format(element.Style!)).Append('\n');
            }

            if (element.Link is not null || element.Tooltip is not null)
            {
                if (element is FlowchartSubgraph)
                    throw new ArgumentException($"The link or the tooltip of the subgraph '{element.Id}' cannot be written as Mermaid.", nameof(diagram));

                // A tooltip without a link is the tooltip of a callback, which the parser ignores
                builder.Append("    click ").Append(element.Id).Append(element.Link is null ? " callback" : " href " + QuoteAttribute(element.Link));
                if (element.Tooltip is not null)
                {
                    builder.Append(' ').Append(QuoteAttribute(element.Tooltip));
                }

                builder.Append('\n');
            }
        }

        if (!MermaidStyle.IsEmpty(diagram.DefaultEdgeStyle))
        {
            builder.Append("    linkStyle default ").Append(MermaidStyle.Format(diagram.DefaultEdgeStyle!)).Append('\n');
        }

        for (var i = 0; i < diagram.Edges.Count; i++)
        {
            var edge = diagram.Edges[i];
            foreach (var className in edge.CssClasses)
            {
                if (edge.Id is null)
                    throw new ArgumentException($"The edge {i} has classes but no identifier: Mermaid cannot write the classes of an edge without an identifier.", nameof(diagram));

                ValidateClassName(className);
                builder.Append("    class ").Append(edge.Id).Append(' ').Append(className).Append('\n');
            }

            var hasStyle = !MermaidStyle.IsEmpty(edge.Style);
            if (hasStyle || edge.Curve is not null)
            {
                builder.Append("    linkStyle ").Append(i.ToString(CultureInfo.InvariantCulture));
                if (edge.Curve is { } curve)
                {
                    builder.Append(" interpolate ").Append(MermaidCommon.FormatCurve(curve));
                }

                if (hasStyle)
                {
                    builder.Append(' ').Append(MermaidStyle.Format(edge.Style!));
                }

                builder.Append('\n');
            }
        }
    }

    private static void WriteSubgraph(StringBuilder builder, FlowchartSubgraph subgraph, int indent, HashSet<FlowchartElement> written, int depth)
    {
        if (!written.Add(subgraph))
            throw new ArgumentException($"The subgraph '{subgraph.Id}' is in the flowchart twice.", nameof(subgraph));

        if (depth > 64)
            throw new ArgumentException("The subgraphs are nested too deeply.", nameof(subgraph));

        builder.AppendIndent(indent).Append("subgraph ");
        if (IsValidIdentifier(subgraph.Id))
        {
            builder.Append(subgraph.Id);
            if (subgraph.Label is not null)
            {
                builder.Append('[').Append(MermaidText.EncodeQuoted(subgraph.Label)).Append(']');
            }
        }
        else
        {
            throw new ArgumentException($"The identifier '{subgraph.Id}' cannot be written as Mermaid.", nameof(subgraph));
        }

        builder.Append('\n');
        if (subgraph.Direction is { } direction)
        {
            builder.AppendIndent(indent + 1).Append("direction ").Append(MermaidCommon.FormatDirection(direction)).Append('\n');
        }

        foreach (var node in subgraph.Nodes)
        {
            WriteNode(builder, node, indent + 1, written);
        }

        foreach (var child in subgraph.Subgraphs)
        {
            WriteSubgraph(builder, child, indent + 1, written, depth + 1);
        }

        builder.AppendIndent(indent).Append("end\n");
    }

    private static void WriteNode(StringBuilder builder, FlowchartNode node, int indent, HashSet<FlowchartElement> written)
    {
        if (!written.Add(node))
            return;

        if (!IsValidIdentifier(node.Id))
            throw new ArgumentException($"The identifier '{node.Id}' cannot be written as Mermaid.", nameof(node));

        builder.AppendIndent(indent).Append(node.Id);
        if (node.Label is null)
        {
            if (node.Shape is not FlowchartNodeShape.Rectangle)
            {
                builder.Append("@{ shape: ").Append(FlowchartShapes.GetShortName(node.Shape)).Append(" }");
            }
        }
        else
        {
            var text = MermaidText.EncodeQuoted(node.Label);
            var classic = node.Shape switch
            {
                FlowchartNodeShape.Rectangle => ("[", "]"),
                FlowchartNodeShape.RoundedRectangle => ("(", ")"),
                FlowchartNodeShape.Stadium => ("([", "])"),
                FlowchartNodeShape.FramedRectangle => ("[[", "]]"),
                FlowchartNodeShape.Cylinder => ("[(", ")]"),
                FlowchartNodeShape.Circle => ("((", "))"),
                FlowchartNodeShape.DoubleCircle => ("(((", ")))"),
                FlowchartNodeShape.Diamond => ("{", "}"),
                FlowchartNodeShape.Hexagon => ("{{", "}}"),
                FlowchartNodeShape.LeanRight => ("[/", "/]"),
                FlowchartNodeShape.LeanLeft => ("[\\", "\\]"),
                FlowchartNodeShape.TrapezoidBaseBottom => ("[/", "\\]"),
                FlowchartNodeShape.TrapezoidBaseTop => ("[\\", "/]"),
                FlowchartNodeShape.Odd => (">", "]"),
                _ => ((string Open, string Close)?)null,
            };

            if (classic is { } delimiters)
            {
                builder.Append(delimiters.Open).Append(text).Append(delimiters.Close);
            }
            else
            {
                builder.Append("@{ shape: ").Append(FlowchartShapes.GetShortName(node.Shape)).Append(", label: ").Append(MermaidWriterHelpers.YamlSingleQuoted(text[1..^1])).Append(" }");
            }
        }

        builder.Append('\n');
    }

    private static void WriteEdge(StringBuilder builder, FlowchartEdge edge)
    {
        // An invisible edge draws no head
        if (edge.LineStyle is not FlowchartLineStyle.Invisible && edge.StartArrowHead is not FlowchartArrowHead.None && edge.StartArrowHead != edge.EndArrowHead)
            throw new ArgumentException($"The edge from '{edge.Source.Id}' to '{edge.Target.Id}' cannot be written as Mermaid: its two heads must be the same when it has a start head.", nameof(edge));

        builder.Append("    ").Append(edge.Source.Id).Append(' ');
        if (edge.Id is not null)
        {
            if (!IsValidIdentifier(edge.Id))
                throw new ArgumentException($"The identifier '{edge.Id}' cannot be written as Mermaid.", nameof(edge));

            builder.Append(edge.Id).Append('@');
        }

        builder.Append(FormatLink(edge));
        if (edge.Label is not null)
        {
            builder.Append('|').Append(MermaidText.EncodeQuoted(edge.Label)).Append('|');
        }

        builder.Append(' ').Append(edge.Target.Id).Append('\n');
    }

    private static string FormatLink(FlowchartEdge edge)
    {
        static char Head(FlowchartArrowHead head, bool start) => head switch
        {
            FlowchartArrowHead.Arrow => start ? '<' : '>',
            FlowchartArrowHead.Cross => 'x',
            FlowchartArrowHead.Circle => 'o',
            _ => '\0',
        };

        var invisible = edge.LineStyle is FlowchartLineStyle.Invisible;
        var start = invisible ? '\0' : Head(edge.StartArrowHead, start: true);
        var end = invisible ? '\0' : Head(edge.EndArrowHead, start: false);
        var builder = new StringBuilder();
        if (start != '\0')
        {
            builder.Append(start);
        }

        switch (edge.LineStyle)
        {
            case FlowchartLineStyle.Invisible:
                builder.Append('~', 2 + edge.Length);
                return builder.ToString();
            case FlowchartLineStyle.Dotted:
                builder.Append('-').Append('.', edge.Length).Append('-');
                if (end != '\0')
                {
                    builder.Append(end);
                }

                return builder.ToString();
            case FlowchartLineStyle.Thick:
                builder.Append('=', edge.Length + 1).Append(end != '\0' ? end : '=');
                return builder.ToString();
            default:
                builder.Append('-', edge.Length + 1).Append(end != '\0' ? end : '-');
                return builder.ToString();
        }
    }

    private static string QuoteAttribute(string value) => MermaidText.QuoteAttribute(value);

    private static void ValidateClassName(string name)
    {
        if (name.Length is 0 || name.Any(c => char.IsWhiteSpace(c) || c is ',' or ';'))
            throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(name));
    }

    /// <summary>Indicates whether an identifier reads back as itself: no character that ends an identifier, and no keyword.</summary>
    private static bool IsValidIdentifier(string id)
    {
        if (id.Length is 0 || FlowchartParser.IsKeyword(id) || FlowchartParser.IsAccessibilityStatement(id) || id.Contains("%%", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (char.IsWhiteSpace(c) || c is '[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or '|' or ';' or ',' or '&' or '"' or '\'' or '@' or '`')
                return false;

            if (c == ':' && i + 2 < id.Length && id[i + 1] == ':' && id[i + 2] == ':')
                return false;

            if (c is '-' or '=' && i + 1 < id.Length && (id[i + 1] == c || (c == '-' && id[i + 1] == '.')))
                return false;

            if (c == '~' && i + 1 < id.Length && id[i + 1] == '~')
                return false;
        }

        return true;
    }
}
