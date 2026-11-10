using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.IshikawaDiagrams;

/// <summary>Writes an <see cref="IshikawaDiagram"/> as Mermaid: the effect, then the causes indented by 4 spaces per level.</summary>
internal static class IshikawaDiagramWriter
{
    public static void Write(StringBuilder builder, IshikawaDiagram diagram)
    {
        // The diagram has no accessibility statements: Mermaid would read them as causes
        if (diagram.AccessibleTitle is not null || diagram.Description is not null)
            throw new ArgumentException("An Ishikawa diagram cannot have an accessible title nor a description in Mermaid.", nameof(diagram));

        var configuration = new List<string>();
        if (diagram.Padding != 20)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "ishikawa", configuration);
        builder.Append("ishikawa-beta\n");
        if (diagram.Effect is null)
        {
            if (diagram.Causes.Count > 0)
                throw new ArgumentException("An Ishikawa diagram with causes needs an effect in Mermaid: its first line is the effect.", nameof(diagram));

            return;
        }

        builder.Append("    ").Append(Encode(diagram.Effect)).Append('\n');
        var visited = new HashSet<IshikawaCause>(ReferenceEqualityComparer.Instance);
        foreach (var cause in diagram.Causes)
        {
            WriteCause(builder, cause, 1, visited);
        }
    }

    private static void WriteCause(StringBuilder builder, IshikawaCause cause, int depth, HashSet<IshikawaCause> visited)
    {
        if (depth > 64)
            throw new ArgumentException("The causes are nested too deeply.", nameof(cause));

        if (!visited.Add(cause))
            throw new ArgumentException("A cause is in the diagram twice.", nameof(cause));

        builder.Append(' ', 4 * depth).Append(Encode(cause.Label)).Append('\n');
        foreach (var child in cause.Causes)
        {
            WriteCause(builder, child, depth + 1, visited);
        }
    }

    /// <summary>Writes a text on a line, the line breaks as <c>&lt;br&gt;</c>; Mermaid has no escapes, so the texts it cannot read back throw.</summary>
    private static string Encode(DiagramText label)
    {
        var text = label.Text.Replace("\n", "<br>", StringComparison.Ordinal);
        if (text.Length is 0)
            throw new ArgumentException("A text of an Ishikawa diagram cannot be empty in Mermaid.", nameof(label));

        if (IshikawaDiagramParser.IsWhiteSpace(text[0]) || IshikawaDiagramParser.IsWhiteSpace(text[^1]) || text.Contains('\r', StringComparison.Ordinal))
            throw new ArgumentException($"The text '{label.Text}' cannot start or end with white space nor contain a carriage return in Mermaid.", nameof(label));

        if (text.StartsWith("%%", StringComparison.Ordinal) || text.Contains("%%{", StringComparison.Ordinal))
            throw new ArgumentException($"The text '{label.Text}' cannot start with '%%' nor contain '%%{{' in Mermaid: it would be a comment or a directive.", nameof(label));

        if (IshikawaDiagramParser.GetKeywordLength(text, 0, text.Length) > 0)
            throw new ArgumentException($"The text '{label.Text}' cannot start with the keyword 'ishikawa' in Mermaid.", nameof(label));

        return text;
    }
}
