using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.SankeyDiagrams;

/// <summary>Writes a <see cref="SankeyDiagram"/> as Mermaid: a row <c>source,target,value</c> per link, the names quoted when needed.</summary>
internal static class SankeyDiagramWriter
{
    public static void Write(StringBuilder builder, SankeyDiagram diagram)
    {
        // The grammar has no accessibility statements: Mermaid would read them as rows
        if (diagram.AccessibleTitle is not null || diagram.Description is not null)
            throw new ArgumentException("A sankey diagram cannot have an accessible title nor a description in Mermaid.", nameof(diagram));

        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.Width != 600)
        {
            configuration.Add("width: " + MermaidStyle.Number(diagram.Width));
        }

        if (diagram.Height != 400)
        {
            configuration.Add("height: " + MermaidStyle.Number(diagram.Height));
        }

        if (diagram.NodeWidth != 10)
        {
            configuration.Add("nodeWidth: " + MermaidStyle.Number(diagram.NodeWidth));
        }

        if (diagram.NodePadding != 12)
        {
            configuration.Add("nodePadding: " + MermaidStyle.Number(diagram.NodePadding));
        }

        if (diagram.LinkColor is { } linkColor)
        {
            configuration.Add("linkColor: " + MermaidWriterHelpers.YamlDoubleQuoted(linkColor.ToString()));
        }
        else if (diagram.LinkColoring is not SankeyLinkColoring.Gradient)
        {
            configuration.Add("linkColor: " + (diagram.LinkColoring is SankeyLinkColoring.Source ? "source" : "target"));
        }

        if (diagram.NodeAlignment is not SankeyNodeAlignment.Justify)
        {
            configuration.Add("nodeAlignment: " + diagram.NodeAlignment switch
            {
                SankeyNodeAlignment.Left => "left",
                SankeyNodeAlignment.Right => "right",
                _ => "center",
            });
        }

        if (!diagram.ShowValues)
        {
            configuration.Add("showValues: false");
        }

        if (diagram.ValuePrefix.Length > 0)
        {
            configuration.Add("prefix: " + MermaidWriterHelpers.YamlDoubleQuoted(diagram.ValuePrefix));
        }

        if (diagram.ValueSuffix.Length > 0)
        {
            configuration.Add("suffix: " + MermaidWriterHelpers.YamlDoubleQuoted(diagram.ValueSuffix));
        }

        if (diagram.LabelStyle is SankeyLabelStyle.Outlined)
        {
            configuration.Add("labelStyle: outlined");
        }

        if (diagram.NodeColors.Count > 0)
        {
            configuration.Add("nodeColors:");
            foreach (var (name, color) in diagram.NodeColors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                configuration.Add("  " + MermaidWriterHelpers.YamlDoubleQuoted(name) + ": " + MermaidWriterHelpers.YamlDoubleQuoted(color.ToString()));
            }
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "sankey", configuration);
        builder.Append("sankey\n");
        foreach (var link in diagram.Links)
        {
            builder.Append(Encode(link.Source)).Append(',').Append(Encode(link.Target)).Append(',').Append(MermaidStyle.Number(link.Value)).Append('\n');
        }
    }

    /// <summary>Writes a name as a field of the CSV: quoted when it has a comma, a quote, a line feed or starts with <c>%</c>.</summary>
    private static string Encode(string name)
    {
        // A quoted space is the only way to write an empty name: the names are trimmed
        if (name.Length is 0)
            return "\" \"";

        if (SankeyDiagramParser.IsJavaScriptWhiteSpace(name[0]) || SankeyDiagramParser.IsJavaScriptWhiteSpace(name[^1]))
            throw new ArgumentException($"The name '{name}' cannot start or end with white space in Mermaid: the names are trimmed.", nameof(name));

        if (name.Contains('\r', StringComparison.Ordinal) || name.Contains("\n\n", StringComparison.Ordinal))
            throw new ArgumentException($"The name '{name}' cannot contain a carriage return nor an empty line in Mermaid.", nameof(name));

        if (name.Contains("%%{", StringComparison.Ordinal) || name.Split('\n').Skip(1).Any(line => line.TrimStart(' ', '\t').StartsWith("%%", StringComparison.Ordinal)))
            throw new ArgumentException($"The name '{name}' cannot contain '%%{{' nor a line that starts with '%%' in Mermaid: it would be a directive or a comment.", nameof(name));

        if (name.AsSpan().IndexOfAny(",\"\n") < 0 && name[0] != '%')
            return name;

        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
