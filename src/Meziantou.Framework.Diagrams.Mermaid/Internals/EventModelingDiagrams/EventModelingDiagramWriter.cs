using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.EventModelingDiagrams;

/// <summary>
/// Writes an <see cref="EventModelingDiagram"/> as Mermaid, in the compact notation: the frames, then the data blocks,
/// the entities, the notes and the scenarios. Mermaid reads the title from the front matter only, and no accessibility
/// statement, so the accessible title and description are not written.
/// </summary>
internal static class EventModelingDiagramWriter
{
    public static void Write(StringBuilder builder, EventModelingDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "eventmodeling", configuration);
        builder.Append("eventmodeling\n");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in diagram.Frames)
        {
            if (frame.Id.Length is 0 or > 3 || !frame.Id.All(char.IsAsciiDigit))
                throw new ArgumentException($"The frame '{frame.Id}' cannot be written as Mermaid: its identifier has one to three digits.", nameof(diagram));

            if (!ids.Add(frame.Id))
                throw new ArgumentException($"The frame identifier '{frame.Id}' is used several times.", nameof(diagram));
        }

        foreach (var frame in diagram.Frames)
        {
            builder.Append(frame.IsReset ? "rf " : "tf ").Append(frame.Id).Append(' ').Append(FormatKind(frame.Kind)).Append(' ').Append(FormatQualifiedName(frame.Entity));
            foreach (var source in frame.Sources)
            {
                if (!diagram.Frames.Contains(source))
                    throw new ArgumentException($"The frame '{frame.Id}' receives input from a frame that is not in the diagram.", nameof(diagram));

                builder.Append(" ->> ").Append(source.Id);
            }

            if (frame.DataBlock is { } block)
            {
                if (!diagram.DataBlocks.Contains(block))
                    throw new ArgumentException($"The frame '{frame.Id}' references a data block that is not in the diagram.", nameof(diagram));

                builder.Append(" [[").Append(FormatIdentifier(block.Name)).Append("]]");
            }

            if (frame.Data is { } data)
            {
                var content = data.Content.Trim();
                if (content.AsSpan().IndexOfAny('\n', '\r') >= 0)
                    throw new ArgumentException($"The inline data of the frame '{frame.Id}' cannot span several lines in Mermaid: use a data block.", nameof(diagram));

                builder.Append(' ').Append(FormatFormat(data.Format)).Append(content.Length is 0 ? "{}" : "{ " + content + " }");
            }

            builder.Append('\n');
        }

        foreach (var block in diagram.DataBlocks)
        {
            builder.Append("data ").Append(FormatIdentifier(block.Name)).Append(' ');
            AppendBlock(builder, block.Data);
        }

        foreach (var entity in diagram.Entities)
        {
            builder.Append("entity ").Append(FormatQualifiedName(entity)).Append('\n');
        }

        foreach (var note in diagram.Notes)
        {
            builder.Append("note ").Append(GetFrameId(diagram, note.Frame)).Append(' ');
            AppendBlock(builder, note.Data);
        }

        foreach (var scenario in diagram.Scenarios)
        {
            if (scenario.Given.Count is 0 || scenario.Then.Count is 0)
                throw new ArgumentException("A scenario has a given step and a then step at least in Mermaid.", nameof(diagram));

            builder.Append("gwt ").Append(GetFrameId(diagram, scenario.Frame)).Append(" given");
            AppendSteps(builder, scenario.Given);
            if (scenario.When.Count > 0)
            {
                builder.Append(" when");
                AppendSteps(builder, scenario.When);
            }

            builder.Append(" then");
            AppendSteps(builder, scenario.Then);
            builder.Append('\n');
        }
    }

    private static void AppendSteps(StringBuilder builder, IList<EventModelingStep> steps)
    {
        foreach (var step in steps)
        {
            builder.Append(' ').Append(FormatKind(step.Kind)).Append(' ').Append(FormatIdentifier(step.Entity));
        }
    }

    /// <summary>Writes a data block: <c>{</c> at the end of a line, the lines, and <c>}</c> at the start of a line.</summary>
    private static void AppendBlock(StringBuilder builder, EventModelingData data)
    {
        builder.Append(FormatFormat(data.Format)).Append("{\n");
        if (data.Content.Length > 0)
        {
            foreach (var line in data.Content.Split('\n'))
            {
                // A line that starts with } would close the block, and the comments are removed before the reading
                if ((line.StartsWith('}', StringComparison.Ordinal) && (line.Length is 1 || char.IsWhiteSpace(line[1]))) || line.TrimStart().StartsWith("%%", StringComparison.Ordinal) || line.Contains("%%{", StringComparison.Ordinal) || line.Contains('\r', StringComparison.Ordinal))
                    throw new ArgumentException($"The line '{line}' of a data block cannot be written as Mermaid.", nameof(data));
            }

            builder.Append(data.Content).Append('\n');
        }

        builder.Append("}\n");
    }

    private static string GetFrameId(EventModelingDiagram diagram, EventModelingFrame frame)
    {
        if (!diagram.Frames.Contains(frame))
            throw new ArgumentException($"The frame '{frame.Id}' is not in the diagram.", nameof(frame));

        return frame.Id;
    }

    private static string FormatFormat(EventModelingDataFormat? format) => format switch
    {
        null => string.Empty,
        EventModelingDataFormat.Json => "`json`",
        EventModelingDataFormat.JavaScriptObject => "`jsobj`",
        EventModelingDataFormat.Figma => "`figma`",
        EventModelingDataFormat.Salt => "`salt`",
        EventModelingDataFormat.Uri => "`uri`",
        EventModelingDataFormat.Markdown => "`md`",
        EventModelingDataFormat.Html => "`html`",
        _ => "`text`",
    };

    private static string FormatKind(EventModelingEntityKind kind) => kind switch
    {
        EventModelingEntityKind.Ui => "ui",
        EventModelingEntityKind.Processor => "pcr",
        EventModelingEntityKind.Command => "cmd",
        EventModelingEntityKind.ReadModel => "rmo",
        _ => "evt",
    };

    private static string FormatQualifiedName(string name)
    {
        foreach (var part in name.Split('.'))
        {
            FormatIdentifier(part);
        }

        return name;
    }

    private static string FormatIdentifier(string name)
    {
        if (!EventModelingDiagramParser.IsIdentifier(name))
            throw new ArgumentException($"The name '{name}' cannot be written as Mermaid: it is a letter or '_', then letters, digits and '_', and not a keyword.", nameof(name));

        return name;
    }
}
