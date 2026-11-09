using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.ArchitectureDiagrams;

/// <summary>Writes an <see cref="ArchitectureDiagram"/> as Mermaid: the groups, the services and the junctions, then the edges and the alignments.</summary>
internal static class ArchitectureDiagramWriter
{
    public static void Write(StringBuilder builder, ArchitectureDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.IconSize != 80)
        {
            configuration.Add("iconSize: " + MermaidStyle.Number(diagram.IconSize));
        }

        if (diagram.GroupPadding != 40)
        {
            configuration.Add("padding: " + MermaidStyle.Number(diagram.GroupPadding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "architecture", configuration);
        builder.Append("architecture-beta\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var parents = new Dictionary<ArchitectureNode, ArchitectureGroup>(ReferenceEqualityComparer.Instance);
        var groups = new List<ArchitectureGroup>();
        foreach (var group in diagram.Groups)
        {
            WriteGroup(builder, group, parent: null, ids, groups, parents, depth: 0);
        }

        var written = new HashSet<ArchitectureNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in diagram.Nodes)
        {
            WriteNode(builder, node, parent: null, ids, written);
        }

        foreach (var group in groups)
        {
            foreach (var node in group.Nodes)
            {
                if (parents.TryGetValue(node, out var owner) && owner == group)
                {
                    WriteNode(builder, node, group, ids, written);
                }
            }
        }

        foreach (var edge in diagram.Edges)
        {
            if (!written.Contains(edge.From) || !written.Contains(edge.To))
                throw new ArgumentException("An edge links a node that is not in the diagram.", nameof(diagram));

            if ((edge.FromGroup || edge.ToGroup) && (!parents.TryGetValue(edge.From, out var fromParent) || !parents.TryGetValue(edge.To, out var toParent) || fromParent == toParent))
                throw new ArgumentException("The {group} modifier needs an edge between two different groups.", nameof(diagram));

            builder.Append("    ").Append(edge.From.Id).Append(edge.FromGroup ? "{group}" : string.Empty).Append(':').Append(FormatSide(edge.FromSide)).Append(' ')
                .Append(edge.FromArrow ? "<" : string.Empty)
                .Append(edge.Label is { IsEmpty: false } label ? "-" + FormatTitle(label) + "-" : "--")
                .Append(edge.ToArrow ? ">" : string.Empty)
                .Append(' ').Append(FormatSide(edge.ToSide)).Append(':').Append(edge.To.Id).Append(edge.ToGroup ? "{group}" : string.Empty).Append('\n');
        }

        foreach (var alignment in diagram.Alignments)
        {
            if (alignment.Members.Count < 2 || alignment.Members.Distinct().Count() != alignment.Members.Count || alignment.Members.Any(member => !written.Contains(member)))
                throw new ArgumentException("An alignment has two distinct members of the diagram at least.", nameof(diagram));

            builder.Append("    align ").Append(alignment.IsRow ? "row" : "column");
            foreach (var member in alignment.Members)
            {
                builder.Append(' ').Append(member.Id);
            }

            builder.Append('\n');
        }
    }

    private static void WriteGroup(StringBuilder builder, ArchitectureGroup group, ArchitectureGroup? parent, HashSet<string> ids, List<ArchitectureGroup> groups, Dictionary<ArchitectureNode, ArchitectureGroup> parents, int depth)
    {
        if (groups.Contains(group))
            return;

        if (depth > 64)
            throw new ArgumentException("The groups are nested too deeply.", nameof(group));

        AddId(group.Id, ids);
        groups.Add(group);
        builder.Append("    group ").Append(group.Id);
        AppendIcon(builder, group.Icon);
        if (group.Title is { IsEmpty: false } title)
        {
            builder.Append(FormatTitle(title));
        }

        if (parent is not null)
        {
            builder.Append(" in ").Append(parent.Id);
        }

        builder.Append('\n');
        foreach (var node in group.Nodes)
        {
            parents.TryAdd(node, group);
        }

        foreach (var child in group.Groups)
        {
            WriteGroup(builder, child, group, ids, groups, parents, depth + 1);
        }
    }

    private static void WriteNode(StringBuilder builder, ArchitectureNode node, ArchitectureGroup? parent, HashSet<string> ids, HashSet<ArchitectureNode> written)
    {
        if (!written.Add(node))
            return;

        AddId(node.Id, ids);
        if (node is ArchitectureService service)
        {
            builder.Append("    service ").Append(service.Id);
            if (service.IconText is { } iconText)
            {
                builder.Append(' ').Append(Quote(iconText));
            }
            else
            {
                AppendIcon(builder, service.Icon);
            }

            if (service.Title is { IsEmpty: false } title)
            {
                builder.Append(FormatTitle(title));
            }
        }
        else
        {
            builder.Append("    junction ").Append(node.Id);
        }

        if (parent is not null)
        {
            builder.Append(" in ").Append(parent.Id);
        }

        builder.Append('\n');
    }

    private static void AddId(string id, HashSet<string> ids)
    {
        if (!ArchitectureDiagramParser.IsValidId(id))
            throw new ArgumentException($"The identifier '{id}' cannot be written as Mermaid.", nameof(id));

        if (!ids.Add(id))
            throw new ArgumentException($"The identifier '{id}' is used several times.", nameof(id));
    }

    private static void AppendIcon(StringBuilder builder, string? icon)
    {
        if (icon is null)
            return;

        if (!ArchitectureDiagramParser.IsValidIcon(icon))
            throw new ArgumentException($"The icon '{icon}' cannot be written as Mermaid.", nameof(icon));

        builder.Append('(').Append(icon).Append(')');
    }

    /// <summary>Writes a title between brackets, quoted when it has a character that ends a plain title.</summary>
    private static string FormatTitle(DiagramText title)
    {
        var text = title.Text.Trim();
        if (text.AsSpan().IndexOfAny("\r\n") >= 0)
            throw new ArgumentException("A title cannot contain a line break in Mermaid.", nameof(title));

        return text.AsSpan().IndexOfAny("[]\"'\\") >= 0 ? "[" + Quote(text) + "]" : "[" + text + "]";
    }

    private static string Quote(string text)
    {
        if (text.AsSpan().IndexOfAny("\r\n") >= 0)
            throw new ArgumentException("A text cannot contain a line break in Mermaid.", nameof(text));

        return "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static char FormatSide(ArchitectureSide side) => side switch
    {
        ArchitectureSide.Left => 'L',
        ArchitectureSide.Right => 'R',
        ArchitectureSide.Top => 'T',
        _ => 'B',
    };
}
