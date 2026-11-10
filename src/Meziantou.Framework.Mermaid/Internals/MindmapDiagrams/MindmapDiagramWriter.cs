using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.MindmapDiagrams;

/// <summary>Writes a <see cref="MindmapDiagram"/> as Mermaid: a node per line, indented by two spaces per level.</summary>
internal static class MindmapDiagramWriter
{
    public static void Write(StringBuilder builder, MindmapDiagram diagram)
    {
        if (diagram.AccessibleTitle is not null || diagram.Description is not null)
            throw new ArgumentException("The mindmaps of Mermaid have no accessible title nor description.", nameof(diagram));

        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.ColorBy is MindmapColorMode.Depth)
        {
            configuration.Add("colorBy: depth");
        }

        if (diagram.NodePadding != 10)
        {
            configuration.Add("padding: " + MermaidStyle.Number(diagram.NodePadding));
        }

        if (diagram.MaxNodeWidth != 200)
        {
            configuration.Add("maxNodeWidth: " + MermaidStyle.Number(diagram.MaxNodeWidth));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "mindmap", configuration);
        builder.Append("mindmap\n");
        if (diagram.Root is { } root)
        {
            WriteNode(builder, root, 1, new HashSet<MindmapNode>(ReferenceEqualityComparer.Instance));
        }
    }

    private static void WriteNode(StringBuilder builder, MindmapNode node, int level, HashSet<MindmapNode> written)
    {
        if (!written.Add(node))
            throw new ArgumentException("A node is in the mindmap twice.", nameof(node));

        if (level > 256)
            throw new ArgumentException("The mindmap is too deep.", nameof(node));

        builder.Append(' ', 2 * level).Append(FormatNode(node)).Append('\n');
        if (node.Icon is { Length: > 0 } icon)
        {
            if (icon.AsSpan().IndexOfAny(")\n\r") >= 0)
                throw new ArgumentException($"The icon '{icon}' cannot be written as Mermaid.", nameof(node));

            builder.Append(' ', 2 * (level + 1)).Append("::icon(").Append(icon).Append(")\n");
        }

        if (node.CssClasses.Count > 0)
        {
            foreach (var className in node.CssClasses)
            {
                if (className.Length is 0 || className.Any(char.IsWhiteSpace))
                    throw new ArgumentException($"The class name '{className}' cannot be written as Mermaid.", nameof(node));
            }

            builder.Append(' ', 2 * (level + 1)).Append(":::").Append(string.Join(' ', node.CssClasses)).Append('\n');
        }

        foreach (var child in node.Children)
        {
            WriteNode(builder, child, level + 1, written);
        }
    }

    /// <summary>Writes a node: its text alone when Mermaid reads it back the same, else the shape around a quoted text.</summary>
    private static string FormatNode(MindmapNode node)
    {
        var label = node.Label;
        if (label.IsEmpty)
            throw new ArgumentException("The text of a node cannot be empty in Mermaid.", nameof(node));

        // Mermaid does not use the identifiers: one that cannot be written as is, is dropped
        var id = node.Id is { } value && IsPlainText(value) && MermaidText.DecodePlain(value) == value ? value : null;
        if (node.Shape is MindmapNodeShape.Default && id is null && label.Format is DiagramTextFormat.Plain && IsPlainText(label.Text))
            return label.Text;

        var (open, close) = node.Shape switch
        {
            MindmapNodeShape.Square => ("[", "]"),
            MindmapNodeShape.RoundedSquare => ("(", ")"),
            MindmapNodeShape.Circle => ("((", "))"),
            MindmapNodeShape.Bang => ("))", "(("),
            MindmapNodeShape.Cloud => (")", "("),
            MindmapNodeShape.Hexagon => ("{{", "}}"),

            // A node without shape can still have delimiters: (- and -)
            _ => ("(-", "-)"),
        };

        // A space keeps the identifier and the delimiter from forming "-)", which opens a node, or "::icon(", which sets an icon
        var separator = id is not null && ((id[^1] == '-' && open[0] == ')') || (id + open).StartsWith("::icon(", StringComparison.Ordinal)) ? " " : string.Empty;
        return id + separator + open + MermaidText.EncodeQuoted(label) + close;
    }

    /// <summary>Indicates whether a text can be written as is: Mermaid reads it as one node, and the decoding does not change it.</summary>
    private static bool IsPlainText(string text) => text.Length > 0
        && !MindmapDiagramParser.IsSpace(text[0]) && !MindmapDiagramParser.IsSpace(text[^1])
        && text.AsSpan().IndexOfAny("()[]{}\n\r") < 0
        && MermaidText.EncodePlain(text) == text
        && !text.StartsWith("-)", StringComparison.Ordinal)
        && !text.StartsWith("::icon(", StringComparison.Ordinal)
        && !text.StartsWith(":::", StringComparison.Ordinal)
        && !MindmapDiagramParser.StartsWithKeyword(text);
}
