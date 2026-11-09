using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.BlockDiagrams;

/// <summary>Writes a <see cref="BlockDiagram"/> as Mermaid: the items in order, the composite blocks with their content, then the edges and the styles.</summary>
internal static class BlockDiagramWriter
{
    public static void Write(StringBuilder builder, BlockDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "block", configuration);
        builder.Append("block\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        if (diagram.Columns is { } columns)
        {
            builder.Append("    columns ").Append(columns.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        var written = new List<BlockDiagramBlock>();
        var visited = new HashSet<BlockDiagramItem>(ReferenceEqualityComparer.Instance);
        foreach (var item in diagram.Items)
        {
            WriteItem(builder, item, 1, written, visited);
        }

        foreach (var edge in diagram.Edges)
        {
            builder.Append("    ").Append(FormatId(edge.From)).Append(' ').Append(FormatLink(edge)).Append(' ').Append(FormatId(edge.To)).Append('\n');
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            if (!BlockDiagramParser.IsWord(name))
                throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(diagram));

            builder.Append("    classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var block in written)
        {
            if (block.CssClasses.Count is 0 && MermaidStyle.IsEmpty(block.Style))
                continue;

            // The class and style statements name the blocks with letters, digits and _ only
            if (!BlockDiagramParser.IsWord(block.Id))
                throw new ArgumentException($"The block '{block.Id}' cannot be styled in Mermaid: its identifier has other characters than letters, digits and '_'.", nameof(diagram));

            foreach (var className in block.CssClasses)
            {
                if (!BlockDiagramParser.IsWord(className))
                    throw new ArgumentException($"The class name '{className}' cannot be written as Mermaid.", nameof(diagram));

                builder.Append("    class ").Append(block.Id).Append(' ').Append(className).Append('\n');
            }

            if (!MermaidStyle.IsEmpty(block.Style))
            {
                builder.Append("    style ").Append(block.Id).Append(' ').Append(MermaidStyle.Format(block.Style!)).Append('\n');
            }
        }
    }

    private static void WriteItem(StringBuilder builder, BlockDiagramItem item, int indent, List<BlockDiagramBlock> written, HashSet<BlockDiagramItem> visited)
    {
        if (!visited.Add(item))
            return;

        if (indent > 64)
            throw new ArgumentException("The composite blocks are nested too deeply.", nameof(item));

        builder.AppendIndent(indent);
        switch (item)
        {
            case BlockDiagramSpace space:
                builder.Append("space");
                AppendSpan(builder, space.Span);
                builder.Append('\n');
                break;
            case BlockDiagramComposite composite:
                if (BlockDiagramParser.IsValidId(composite.Id))
                {
                    if (written.Any(other => string.Equals(other.Id, composite.Id, StringComparison.Ordinal)))
                        throw new ArgumentException($"The identifier '{composite.Id}' is used several times.", nameof(item));

                    written.Add(composite);
                    builder.Append("block:").Append(composite.Id);
                    if (composite.Label is { IsEmpty: false } title)
                    {
                        builder.Append('[').Append(FormatLabel(title)).Append(']');
                    }

                    AppendSpan(builder, composite.Span);
                }
                else
                {
                    // A composite block without a valid identifier is anonymous: nothing can name it
                    if (composite.Label is { IsEmpty: false } || composite.Span != 1 || composite.CssClasses.Count > 0 || !MermaidStyle.IsEmpty(composite.Style))
                        throw new ArgumentException($"The composite block '{composite.Id}' needs a valid identifier to have a title, a span or a style in Mermaid.", nameof(item));

                    builder.Append("block");
                }

                builder.Append('\n');
                if (composite.Columns is { } columns)
                {
                    builder.AppendIndent(indent + 1).Append("columns ").Append(columns.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }

                foreach (var child in composite.Items)
                {
                    WriteItem(builder, child, indent + 1, written, visited);
                }

                builder.AppendIndent(indent).Append("end\n");
                break;
            case BlockDiagramArrow arrow:
                Add(arrow, written);
                builder.Append(FormatId(arrow)).Append("<[").Append(FormatLabel(arrow.Label ?? new DiagramText(arrow.Id))).Append("]>(").Append(FormatDirections(arrow.Directions)).Append(')');
                AppendSpan(builder, arrow.Span);
                builder.Append('\n');
                break;
            case BlockDiagramBlock block:
                Add(block, written);
                builder.Append(FormatId(block));
                if (block.Shape is not FlowchartNodeShape.Rectangle || (block.Label is { } label && !(label.Format is DiagramTextFormat.Plain && string.Equals(label.Text, block.Id, StringComparison.Ordinal))))
                {
                    var (open, close, _) = Array.Find(BlockDiagramParser.ShapeDelimiters, item => item.Shape == block.Shape);
                    if (open is null)
                        throw new ArgumentException($"The shape '{block.Shape}' of the block '{block.Id}' cannot be written as Mermaid.", nameof(item));

                    builder.Append(open).Append(FormatLabel(block.Label is { IsEmpty: false } text ? text : new DiagramText(block.Id))).Append(close);
                }

                AppendSpan(builder, block.Span);
                builder.Append('\n');
                break;
        }
    }

    private static void Add(BlockDiagramBlock block, List<BlockDiagramBlock> written)
    {
        if (written.Any(other => string.Equals(other.Id, block.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"The identifier '{block.Id}' is used several times.", nameof(block));

        written.Add(block);
    }

    private static void AppendSpan(StringBuilder builder, int span)
    {
        if (span != 1)
        {
            builder.Append(':').Append(span.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string FormatId(BlockDiagramBlock block)
    {
        if (!BlockDiagramParser.IsValidId(block.Id))
            throw new ArgumentException($"The identifier '{block.Id}' cannot be written as Mermaid.", nameof(block));

        return block.Id;
    }

    /// <summary>Writes a text as a quoted text or a markdown text, which Mermaid does not allow empty.</summary>
    private static string FormatLabel(DiagramText label)
    {
        if (label.IsEmpty)
            throw new ArgumentException("The text of a block cannot be empty in Mermaid.", nameof(label));

        return MermaidText.EncodeQuoted(label);
    }

    private static string FormatDirections(BlockArrowDirections directions)
    {
        var parts = new List<string>(2);
        var horizontal = directions & (BlockArrowDirections.Left | BlockArrowDirections.Right);
        var vertical = directions & (BlockArrowDirections.Up | BlockArrowDirections.Down);
        parts.Add(horizontal switch
        {
            BlockArrowDirections.Left | BlockArrowDirections.Right => "x",
            BlockArrowDirections.Left => "left",
            BlockArrowDirections.Right => "right",
            _ => string.Empty,
        });
        parts.Add(vertical switch
        {
            BlockArrowDirections.Up | BlockArrowDirections.Down => "y",
            BlockArrowDirections.Up => "up",
            BlockArrowDirections.Down => "down",
            _ => string.Empty,
        });
        var result = string.Join(", ", parts.Where(part => part.Length > 0));
        return result.Length is 0 ? throw new ArgumentException("A block arrow points to a direction at least in Mermaid.", nameof(directions)) : result;
    }

    private static string FormatLink(BlockDiagramEdge edge)
    {
        if (edge.StartArrowHead is not (FlowchartArrowHead.None or FlowchartArrowHead.Arrow))
            throw new ArgumentException("An edge of a block diagram can start with an arrowhead only in Mermaid.", nameof(edge));

        var start = edge.StartArrowHead is FlowchartArrowHead.Arrow ? "<" : string.Empty;
        var end = edge.EndArrowHead switch
        {
            FlowchartArrowHead.Arrow => ">",
            FlowchartArrowHead.Cross => "x",
            FlowchartArrowHead.Circle => "o",
            _ => null,
        };
        var label = edge.Label is { IsEmpty: false } text ? MermaidText.EncodeQuoted(new DiagramText(text.Text)) : null;
        switch (edge.LineStyle)
        {
            case FlowchartLineStyle.Invisible:
                if (start.Length > 0 || end is not null || label is not null)
                    throw new ArgumentException("An invisible edge has no arrowheads nor text in Mermaid.", nameof(edge));

                return "~~~";
            case FlowchartLineStyle.Thick:
                return label is null ? start + "==" + (end ?? "=") : start + "== " + label + " ==" + (end ?? "=");
            case FlowchartLineStyle.Dotted:
                return label is null ? start + "-.-" + end : start + "-. " + label + " .-" + end;
            default:
                return label is null ? start + "--" + (end ?? "-") : start + "-- " + label + " --" + (end ?? "-");
        }
    }
}
