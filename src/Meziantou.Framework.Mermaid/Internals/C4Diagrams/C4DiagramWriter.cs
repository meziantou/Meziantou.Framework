using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.C4Diagrams;

/// <summary>Writes a <see cref="C4Diagram"/> as Mermaid: the elements, the boundaries with their content, the relationships, then the styles.</summary>
internal static class C4DiagramWriter
{
    public static void Write(StringBuilder builder, C4Diagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (!diagram.Wrap)
        {
            configuration.Add("wrap: false");
        }

        // The title is a statement of the diagram, which cannot hold every character
        var titleStatement = diagram.Title is { } title && title.AsSpan().IndexOfAny("#;\n\r") < 0 && title.Trim().Length == title.Length && title.Length > 0;
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "c4", configuration, includeTitle: !titleStatement);
        builder.Append(diagram.Kind switch
        {
            C4DiagramKind.Container => "C4Container",
            C4DiagramKind.Component => "C4Component",
            C4DiagramKind.Dynamic => "C4Dynamic",
            C4DiagramKind.Deployment => "C4Deployment",
            _ => "C4Context",
        }).Append('\n');
        if (titleStatement)
        {
            builder.Append("    title ").Append(diagram.Title).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        var written = new List<C4Item>();
        foreach (var element in diagram.Elements)
        {
            WriteElement(builder, element, 1, written);
        }

        foreach (var boundary in diagram.Boundaries)
        {
            WriteBoundary(builder, boundary, 1, written);
        }

        var pairs = new HashSet<(C4Item, C4Item)>();
        foreach (var relationship in diagram.Relationships)
        {
            if (!written.Contains(relationship.From) || !written.Contains(relationship.To))
                throw new ArgumentException("A relationship links an item that is not in the diagram.", nameof(diagram));

            if (!pairs.Add((relationship.From, relationship.To)))
                throw new ArgumentException($"Mermaid keeps one relationship from '{relationship.From.Id}' to '{relationship.To.Id}'.", nameof(diagram));

            builder.Append("    ").Append(relationship.Kind switch
            {
                C4RelationshipKind.Bidirectional => "BiRel",
                C4RelationshipKind.Back => "Rel_Back",
                C4RelationshipKind.Up => "Rel_U",
                C4RelationshipKind.Down => "Rel_D",
                C4RelationshipKind.Left => "Rel_L",
                C4RelationshipKind.Right => "Rel_R",
                _ => "Rel",
            }).Append('(').Append(FormatAlias(relationship.From.Id)).Append(", ").Append(FormatAlias(relationship.To.Id)).Append(", ").Append(Quote(relationship.Label.Text));
            if (!string.IsNullOrEmpty(relationship.Technology))
            {
                builder.Append(", ").Append(Quote(relationship.Technology));
            }

            AppendNamed(builder, "descr", relationship.Description?.Text);
            AppendNamed(builder, "tags", relationship.Tags);
            AppendNamed(builder, "link", relationship.Link);
            builder.Append(")\n");
        }

        foreach (var item in written)
        {
            if (item.Style is { } style && (style.Fill is not null || style.TextColor is not null || style.Stroke is not null))
            {
                builder.Append("    UpdateElementStyle(").Append(FormatAlias(item.Id));
                AppendNamed(builder, "bgColor", style.Fill?.ToString(), encode: false);
                AppendNamed(builder, "fontColor", style.TextColor?.ToString(), encode: false);
                AppendNamed(builder, "borderColor", style.Stroke?.ToString(), encode: false);
                builder.Append(")\n");
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            var offsetX = ToInteger(relationship.LabelOffsetX);
            var offsetY = ToInteger(relationship.LabelOffsetY);
            var style = relationship.Style;
            if (style?.TextColor is not null || style?.Stroke is not null || offsetX != 0 || offsetY != 0)
            {
                builder.Append("    UpdateRelStyle(").Append(FormatAlias(relationship.From.Id)).Append(", ").Append(FormatAlias(relationship.To.Id));
                AppendNamed(builder, "textColor", style?.TextColor?.ToString(), encode: false);
                AppendNamed(builder, "lineColor", style?.Stroke?.ToString(), encode: false);
                AppendNamed(builder, "offsetX", offsetX == 0 ? null : offsetX.ToString(CultureInfo.InvariantCulture), encode: false);
                AppendNamed(builder, "offsetY", offsetY == 0 ? null : offsetY.ToString(CultureInfo.InvariantCulture), encode: false);
                builder.Append(")\n");
            }
        }

        if (diagram.ShapesPerRow != 4 || diagram.BoundariesPerRow != 2)
        {
            builder.Append("    UpdateLayoutConfig($c4ShapeInRow=\"").Append(diagram.ShapesPerRow.ToString(CultureInfo.InvariantCulture))
                .Append("\", $c4BoundaryInRow=\"").Append(diagram.BoundariesPerRow.ToString(CultureInfo.InvariantCulture)).Append("\")\n");
        }
    }

    private static void WriteElement(StringBuilder builder, C4Element element, int indent, List<C4Item> written)
    {
        Add(element, written);
        if (element.Kind is C4ElementKind.Person && element.Shape is not C4ElementShape.Box)
            throw new ArgumentException($"The person '{element.Id}' cannot be a database or a queue in Mermaid.", nameof(element));

        var name = element.Kind switch
        {
            C4ElementKind.Person => "Person",
            C4ElementKind.Container => "Container",
            C4ElementKind.Component => "Component",
            _ => "System",
        } + element.Shape switch
        {
            C4ElementShape.Database => "Db",
            C4ElementShape.Queue => "Queue",
            _ => string.Empty,
        } + (element.IsExternal ? "_Ext" : string.Empty);
        builder.AppendIndent(indent).Append(name).Append('(').Append(FormatAlias(element.Id)).Append(", ").Append(Quote(element.Label.Text));
        var hasTechnologySlot = element.Kind is C4ElementKind.Container or C4ElementKind.Component;
        var description = element.Description is { IsEmpty: false } text ? text.Text : null;
        if (hasTechnologySlot && (!string.IsNullOrEmpty(element.Technology) || description is not null))
        {
            builder.Append(", ").Append(Quote(element.Technology ?? string.Empty));
        }

        if (description is not null)
        {
            builder.Append(", ").Append(Quote(description));
        }

        if (!hasTechnologySlot)
        {
            AppendNamed(builder, "techn", element.Technology);
        }

        AppendNamed(builder, "tags", element.Tags);
        AppendNamed(builder, "link", element.Link);
        builder.Append(")\n");
    }

    private static void WriteBoundary(StringBuilder builder, C4Boundary boundary, int indent, List<C4Item> written)
    {
        if (indent > 64)
            throw new ArgumentException("The boundaries are nested too deeply.", nameof(boundary));

        Add(boundary, written);
        builder.AppendIndent(indent).Append(boundary.Kind switch
        {
            C4BoundaryKind.Enterprise => "Enterprise_Boundary",
            C4BoundaryKind.System => "System_Boundary",
            C4BoundaryKind.Container => "Container_Boundary",
            C4BoundaryKind.DeploymentNode => "Deployment_Node",
            C4BoundaryKind.DeploymentNodeLeft => "Node_L",
            C4BoundaryKind.DeploymentNodeRight => "Node_R",
            _ => "Boundary",
        }).Append('(').Append(FormatAlias(boundary.Id)).Append(", ").Append(Quote(boundary.Label.Text));
        var isNode = boundary.Kind is C4BoundaryKind.DeploymentNode or C4BoundaryKind.DeploymentNodeLeft or C4BoundaryKind.DeploymentNodeRight;
        var description = boundary.Description is { IsEmpty: false } text ? text.Text : null;
        if (boundary.Kind is C4BoundaryKind.Boundary || isNode)
        {
            if (!string.IsNullOrEmpty(boundary.Type) || (isNode && description is not null))
            {
                builder.Append(", ").Append(Quote(boundary.Type ?? string.Empty));
            }

            if (isNode && description is not null)
            {
                builder.Append(", ").Append(Quote(description));
            }
        }
        else
        {
            AppendNamed(builder, "type", boundary.Type);
        }

        if (!isNode)
        {
            AppendNamed(builder, "descr", description);
        }

        AppendNamed(builder, "tags", boundary.Tags);
        AppendNamed(builder, "link", boundary.Link);
        builder.Append(") {\n");
        foreach (var element in boundary.Elements)
        {
            WriteElement(builder, element, indent + 1, written);
        }

        foreach (var child in boundary.Boundaries)
        {
            WriteBoundary(builder, child, indent + 1, written);
        }

        builder.AppendIndent(indent).Append("}\n");
    }

    private static void Add(C4Item item, List<C4Item> written)
    {
        if (written.Contains(item) || written.Any(other => string.Equals(other.Id, item.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"The identifier '{item.Id}' is used several times.", nameof(item));

        written.Add(item);
    }

    /// <summary>Writes an alias, quoted when it has a character that ends a plain attribute.</summary>
    private static string FormatAlias(string alias)
    {
        if (alias.Trim().Length != alias.Length || alias.AsSpan().IndexOfAny("\"\n\r") >= 0)
            throw new ArgumentException($"The alias '{alias}' cannot be written as Mermaid.", nameof(alias));

        return alias.AsSpan().IndexOfAny(",)$(") >= 0 || alias.Contains("%%", StringComparison.Ordinal) ? "\"" + alias + "\"" : alias;
    }

    private static string Quote(string text) => MermaidText.EncodeQuoted(new DiagramText(text));

    /// <summary>Writes a named attribute; the colors and the numbers are written as is, since they are not decoded.</summary>
    private static void AppendNamed(StringBuilder builder, string name, string? value, bool encode = true)
    {
        if (string.IsNullOrEmpty(value))
            return;

        builder.Append(", $").Append(name).Append('=').Append(encode ? Quote(value) : "\"" + value + "\"");
    }

    private static int ToInteger(double value) => double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), -100_000_000, 100_000_000) : 0;
}
