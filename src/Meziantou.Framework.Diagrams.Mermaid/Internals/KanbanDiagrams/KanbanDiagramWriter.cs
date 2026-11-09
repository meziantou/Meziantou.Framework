using System.Globalization;
using System.Text;
using Meziantou.Framework.Diagrams.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.KanbanDiagrams;

/// <summary>Writes a <see cref="KanbanDiagram"/> as Mermaid: the columns indented by two spaces, their cards by four.</summary>
internal static class KanbanDiagramWriter
{
    public static void Write(StringBuilder builder, KanbanDiagram diagram)
    {
        if (diagram.AccessibleTitle is not null || diagram.Description is not null)
            throw new ArgumentException("The kanban diagrams of Mermaid have no accessible title nor description.", nameof(diagram));

        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.ColumnWidth != 200)
        {
            configuration.Add("sectionWidth: " + MermaidStyle.Number(diagram.ColumnWidth));
        }

        if (diagram.TicketLinkFormat is { } format && !string.IsNullOrWhiteSpace(format))
        {
            configuration.Add("ticketBaseUrl: " + MermaidWriterHelpers.YamlDoubleQuoted(format.Trim()));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "kanban", configuration);
        builder.Append("kanban\n");
        foreach (var column in diagram.Columns)
        {
            WriteItem(builder, 2, column.Id, column.Label, metadata: null, column.CssClasses);
            foreach (var card in column.Cards)
            {
                var metadata = new List<string>();
                if (card.Ticket is { Length: > 0 } ticket)
                {
                    metadata.Add("ticket: " + QuoteYaml(GitGraphLangium.ReplaceLoneSurrogates(ticket)));
                }

                if (card.Assigned is { Length: > 0 } assigned)
                {
                    metadata.Add("assigned: " + QuoteYaml(GitGraphLangium.ReplaceLoneSurrogates(assigned)));
                }

                if (card.Priority is { } priority)
                {
                    metadata.Add("priority: '" + GetPriorityName(priority) + "'");
                }

                WriteItem(builder, 4, card.Id, card.Label, metadata, card.CssClasses);
            }
        }
    }

    internal static string GetPriorityName(KanbanPriority priority) => priority switch
    {
        KanbanPriority.VeryLow => "Very Low",
        KanbanPriority.Low => "Low",
        KanbanPriority.Medium => "Medium",
        KanbanPriority.High => "High",
        KanbanPriority.VeryHigh => "Very High",
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, message: null),
    };

    private static void WriteItem(StringBuilder builder, int indent, string id, DiagramText? label, List<string>? metadata, IList<string> classes)
    {
        builder.Append(' ', indent);
        var text = label?.Text;
        if (text is { Length: 0 } || string.Equals(text, id, StringComparison.Ordinal))
        {
            text = null;
        }

        if (IsBareIdentifier(id))
        {
            builder.Append(id);
            if (text is not null)
            {
                builder.Append('[').Append(FormatText(text)).Append(']');
            }
        }
        else
        {
            // An item without identifier takes its text as identifier: the label goes to the metadata
            builder.Append('[').Append(FormatText(id)).Append(']');
            if (text is not null)
            {
                metadata ??= [];
                metadata.Insert(0, "label: " + QuoteYaml(GitGraphLangium.ReplaceLoneSurrogates(MermaidText.EncodePlain(text))));
            }
        }

        if (metadata is { Count: > 0 })
        {
            builder.Append("@{ ").Append(string.Join(", ", metadata)).Append(" }");
        }

        builder.Append('\n');
        if (classes.Count > 0)
        {
            foreach (var name in classes)
            {
                if (string.IsNullOrEmpty(name) || name.Any(char.IsWhiteSpace) || name.Contains("%%", StringComparison.Ordinal))
                    throw new ArgumentException($"The class name '{name}' of the item '{id}' cannot be written in Mermaid: it must not be empty nor contain spaces or '%%'.", nameof(classes));
            }

            builder.Append(' ', indent).Append(":::").Append(string.Join(' ', classes)).Append('\n');
        }
    }

    /// <summary>Indicates whether an identifier can be written as is: Mermaid reads the identifiers up to a delimiter of the shapes.</summary>
    internal static bool IsBareIdentifier(string id)
    {
        if (id.Length is 0 || char.IsWhiteSpace(id[0]) || char.IsWhiteSpace(id[^1]) || id[0] is ':' or '"')
            return false;

        if (id.StartsWith("kanban", StringComparison.OrdinalIgnoreCase) || id.Contains("%%", StringComparison.Ordinal))
            return false;

        foreach (var c in id)
        {
            if (!KanbanDiagramParser.IsIdCharacter(c) || char.IsControl(c) || c is '\u2028' or '\u2029' or '\u0085')
                return false;
        }

        return true;
    }

    /// <summary>Writes the text of an item, between brackets: as is when it can, quoted otherwise.</summary>
    private static string FormatText(string text)
    {
        var encoded = MermaidText.EncodePlain(text);
        if (string.Equals(encoded, text, StringComparison.Ordinal) && text.Trim().Length == text.Length && text.AsSpan().IndexOfAny("()[]{}\"") < 0)
            return text;

        return MermaidText.EncodeQuoted(new DiagramText(text));
    }

    /// <summary>Quotes a YAML string so that the reading of the inline mapping, which ignores the escapes, finds its end.</summary>
    private static string QuoteYaml(string value)
    {
        if (!value.Any(c => char.IsControl(c) || char.IsSurrogate(c) || c is '\u2028' or '\u2029' or '\u0085' or '\ufeff' or '\ufffe' or '\uffff'))
            return MermaidWriterHelpers.YamlSingleQuoted(value);

        var builder = new StringBuilder("\"");
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (c is '"' or '\\' || char.IsControl(c) || char.IsSurrogate(c) || c is '\u2028' or '\u2029' or '\u0085' or '\ufeff' or '\ufffe' or '\uffff')
            {
                builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.Append('"').ToString();
    }
}
