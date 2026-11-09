using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.UserJourneyDiagrams;

/// <summary>Writes a <see cref="UserJourneyDiagram"/> as Mermaid.</summary>
internal static class UserJourneyDiagramWriter
{
    public static void Write(StringBuilder builder, UserJourneyDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        // The title statement cannot hold every character: the front matter can
        var titleStatement = diagram.Title is { } title && IsStatementText(title);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "journey", configuration, includeTitle: !titleStatement);
        builder.Append("journey\n");
        if (titleStatement)
        {
            builder.Append("    title ").Append(diagram.Title).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        for (var i = 0; i < diagram.Sections.Count; i++)
        {
            var section = diagram.Sections[i];
            var indent = "    ";
            if (section.Name.Length is 0 && i is 0)
            {
                // The tasks before the first section statement
            }
            else
            {
                if (!IsStatementText(section.Name) || section.Name.Contains(':', StringComparison.Ordinal))
                    throw new ArgumentException($"The section '{section.Name}' cannot be written as Mermaid: its name is empty or has a character that ends it.", nameof(diagram));

                builder.Append("    section ").Append(section.Name).Append('\n');
                indent = "      ";
            }

            foreach (var task in section.Tasks)
            {
                ValidateTaskName(task.Name);
                if (!double.IsFinite(task.Score))
                    throw new ArgumentException($"The score of the task '{task.Name}' is not a finite number.", nameof(diagram));

                builder.Append(indent).Append(task.Name).Append(": ").Append(MermaidStyle.Number(task.Score));
                if (task.Actors.Count > 0)
                {
                    builder.Append(": ");
                    for (var j = 0; j < task.Actors.Count; j++)
                    {
                        var actor = task.Actors[j];
                        if (!IsStatementText(actor) || actor.AsSpan().IndexOfAny(",:") >= 0)
                            throw new ArgumentException($"The actor '{actor}' cannot be written as Mermaid.", nameof(diagram));

                        if (j > 0)
                        {
                            builder.Append(", ");
                        }

                        builder.Append(actor);
                    }
                }

                builder.Append('\n');
            }
        }
    }

    private static void ValidateTaskName(string name)
    {
        // A task cannot start like a keyword of the diagram, which Mermaid reads ignoring the case
        if (!IsStatementText(name) || name.Contains(':', StringComparison.Ordinal)
            || StartsWithKeyword(name, "section") || StartsWithKeyword(name, "title")
            || name.StartsWith("journey", StringComparison.OrdinalIgnoreCase) || IsAccessibilityStatement(name))
        {
            throw new ArgumentException($"The task '{name}' cannot be written as Mermaid.", nameof(name));
        }
    }

    private static bool StartsWithKeyword(string text, string keyword) => text.Length > keyword.Length && text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) && text[keyword.Length] is ' ' or '\t';

    /// <summary>Indicates whether a task line would read as an accessibility statement: <c>accTitle:</c>, <c>accDescr:</c> or <c>accDescr {</c>.</summary>
    private static bool IsAccessibilityStatement(string name) => name is "accTitle" or "accDescr" || (name.StartsWith("accDescr", StringComparison.Ordinal) && name[8..].TrimStart(' ', '\t').StartsWith('{', StringComparison.Ordinal));

    /// <summary>Indicates whether a text can be written in a statement: not empty, trimmed, without line feed, semicolon, <c>#</c> nor <c>%%</c>.</summary>
    private static bool IsStatementText(string text) => text.Length > 0 && text.Trim().Length == text.Length && text.AsSpan().IndexOfAny("\n\r;#") < 0 && !text.Contains("%%", StringComparison.Ordinal);
}
