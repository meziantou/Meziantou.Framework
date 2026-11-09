using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.TreemapDiagrams;

/// <summary>Writes a <see cref="TreemapDiagram"/> as Mermaid: a quoted name per line, indented by 4 spaces per level, then the classes.</summary>
internal static class TreemapDiagramWriter
{
    public static void Write(StringBuilder builder, TreemapDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.InnerPadding != 10)
        {
            configuration.Add("padding: " + MermaidStyle.Number(diagram.InnerPadding));
        }

        if (diagram.NodeWidth != 100)
        {
            configuration.Add("nodeWidth: " + MermaidStyle.Number(diagram.NodeWidth));
        }

        if (diagram.NodeHeight != 40)
        {
            configuration.Add("nodeHeight: " + MermaidStyle.Number(diagram.NodeHeight));
        }

        if (!diagram.ShowValues)
        {
            configuration.Add("showValues: false");
        }

        if (diagram.ValueFormat is not ("," or ""))
        {
            configuration.Add("valueFormat: " + MermaidWriterHelpers.YamlDoubleQuoted(diagram.ValueFormat));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "treemap", configuration);
        builder.Append("treemap\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 0);
        var visited = new HashSet<TreemapNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in diagram.Nodes)
        {
            WriteNode(builder, node, 0, visited);
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            if (!TreemapDiagramParser.IsClassName(name, minimumLength: 2))
                throw new ArgumentException($"The class name '{name}' is not valid in Mermaid: it has at least two letters, digits or underscores, and does not start with a digit.", nameof(diagram));

            var text = MermaidStyle.Format(style);
            builder.Append("classDef ").Append(name);
            if (text.Length > 0)
            {
                builder.Append(' ').Append(text);
            }

            builder.Append(";\n");
        }
    }

    private static void WriteNode(StringBuilder builder, TreemapNode node, int depth, HashSet<TreemapNode> visited)
    {
        if (depth > 64)
            throw new ArgumentException("The nodes are nested too deeply.", nameof(node));

        if (!visited.Add(node))
            throw new ArgumentException("A node is in the diagram twice.", nameof(node));

        if (node.Value is not null && node.Children.Count > 0)
            throw new ArgumentException($"The node '{node.Name}' has a value and children: in Mermaid, a node with a value is a leaf.", nameof(node));

        builder.Append(' ', 4 * depth).Append(Quote(node.Name));
        if (node.Value is { } value)
        {
            builder.Append(": ").Append(FormatValue(value));
        }

        if (node.CssClass is { } cssClass)
        {
            if (!TreemapDiagramParser.IsClassName(cssClass))
                throw new ArgumentException($"The class name '{cssClass}' is not valid in Mermaid: it has letters, digits or underscores, and does not start with a digit.", nameof(node));

            builder.Append(":::").Append(cssClass);
        }

        builder.Append('\n');
        foreach (var child in node.Children)
        {
            WriteNode(builder, child, depth + 1, visited);
        }
    }

    /// <summary>Quotes a name: between double quotes, or single quotes when it has a double quote; Mermaid has no escapes.</summary>
    private static string Quote(string name)
    {
        if (name.Contains('\r', StringComparison.Ordinal))
            throw new ArgumentException($"The name '{name}' cannot contain a carriage return in Mermaid.", nameof(name));

        if (name.Contains("%%{", StringComparison.Ordinal) || name.Split('\n').Skip(1).Any(line => line.TrimStart(' ', '\t').StartsWith("%%", StringComparison.Ordinal)))
            throw new ArgumentException($"The name '{name}' cannot contain '%%{{' nor a line that starts with '%%' in Mermaid: it would be a directive or a comment.", nameof(name));

        if (!name.Contains('"', StringComparison.Ordinal))
            return "\"" + name + "\"";

        if (!name.Contains('\'', StringComparison.Ordinal))
            return "'" + name + "'";

        throw new ArgumentException($"The name '{name}' cannot contain both a double and a single quote in Mermaid.", nameof(name));
    }

    /// <summary>Writes a value with digits and a point only, as the grammar of Mermaid reads them: no exponent.</summary>
    private static string FormatValue(double value)
    {
        if (value == 0)
            return "0";

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        // The digits, and n such that the value is 0.digits × 10^n
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerLength = point < 0 ? text.Length : point;
        var all = point < 0 ? text : text.Remove(point, 1);
        var leadingZeros = 0;
        while (leadingZeros < all.Length - 1 && all[leadingZeros] == '0')
        {
            leadingZeros++;
        }

        var digits = all[leadingZeros..].TrimEnd('0');
        var n = integerLength - leadingZeros + exponent;
        if (n >= digits.Length)
            return digits + new string('0', n - digits.Length);

        if (n > 0)
            return digits[..n] + "." + digits[n..];

        return "0." + new string('0', -n) + digits;
    }
}
