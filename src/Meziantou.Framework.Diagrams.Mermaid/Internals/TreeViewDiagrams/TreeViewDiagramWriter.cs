using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.TreeViewDiagrams;

/// <summary>
/// Writes a <see cref="TreeViewDiagram"/> as Mermaid: the nodes indented by four spaces per level, or, when a name or a
/// description contains a box-drawing character, which would switch Mermaid to that syntax, drawn with <c>├──</c> and <c>└──</c>.
/// </summary>
internal static class TreeViewDiagramWriter
{
    private const string BoxCharacters = "─━│┃└┗├┣";

    public static void Write(StringBuilder builder, TreeViewDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        AddNumber("rowIndent", diagram.RowIndent, 10);
        AddNumber("paddingX", diagram.PaddingX, 5);
        AddNumber("paddingY", diagram.PaddingY, 5);
        AddNumber("lineThickness", diagram.LineThickness, 1);
        if (diagram.ShowIcons)
        {
            configuration.Add("showIcons: true");
        }

        if (diagram.DefaultIconPack is { Length: > 0 } defaultIconPack)
        {
            configuration.Add("defaultIconPack: " + MermaidWriterHelpers.YamlDoubleQuoted(defaultIconPack));
        }

        AddIcons("filenameIcons", diagram.FileNameIcons);
        AddIcons("extensionIcons", diagram.ExtensionIcons);

        var variables = new List<string>();
        if (diagram.FontSize is { } fontSize)
        {
            if (!(fontSize is > 0 and < 1000))
                throw new ArgumentException("The font size of a tree view must be from 0 to 1000 to be written as Mermaid.", nameof(diagram));

            variables.Add("\"labelFontSize\": \"" + MermaidStyle.Number(fontSize) + "px\"");
        }

        AddColor("labelColor", diagram.LabelColor);
        AddColor("lineColor", diagram.LineColor);
        AddColor("iconColor", diagram.IconColor);
        AddColor("descriptionColor", diagram.DescriptionColor);
        AddColor("highlightBg", diagram.HighlightBackground);
        AddColor("highlightStroke", diagram.HighlightBorderColor);

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "treeView", configuration);
        if (variables.Count > 0)
        {
            // The colors of the tree view are theme variables, which a directive sets
            builder.Append("%%{init: {\"themeVariables\": {\"treeView\": {").Append(string.Join(", ", variables)).Append("}}}}%%\n");
        }

        builder.Append("treeView-beta\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 0);
        var nodes = Flatten(diagram);
        // A box-drawing character anywhere switches the parser to that syntax, which also writes the names that would read as a title and cannot be quoted
        var boxDrawing = nodes.Any(entry => ContainsBoxCharacter(entry.Node.Name) || (entry.Node.Description is { } description && ContainsBoxCharacter(description)) || (TreeViewDiagramParser.IsMetadataLine(entry.Node.Name) && HasBothQuotes(entry.Node.Name)));
        foreach (var (node, depth, prefix, isLast) in nodes)
        {
            if (boxDrawing)
            {
                builder.Append(prefix).Append(isLast ? "└── " : "├── ");
            }
            else
            {
                builder.Append(' ', 4 * depth);
            }

            WriteNode(builder, node, boxDrawing);
        }

        void AddNumber(string name, double value, double defaultValue)
        {
            if (value != defaultValue)
            {
                configuration.Add(name + ": " + MermaidStyle.Number(value));
            }
        }

        void AddIcons(string name, IDictionary<string, string> icons)
        {
            if (icons.Count is 0)
                return;

            configuration.Add(name + ":");
            foreach (var (key, icon) in icons.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (key.Length is 0)
                    throw new ArgumentException($"The {name} of a tree view cannot have an empty key in Mermaid.", nameof(diagram));

                configuration.Add("  " + MermaidWriterHelpers.YamlDoubleQuoted(key) + ": " + MermaidWriterHelpers.YamlDoubleQuoted(icon));
            }
        }

        void AddColor(string name, SceneColor? color)
        {
            if (color is { } value)
            {
                variables.Add("\"" + name + "\": \"" + value.ToString() + "\"");
            }
        }
    }

    /// <summary>
    /// Lists the nodes in the order they are written, with their depth, the prefix of their box-drawing branch, and whether
    /// they are the last child of their parent. As the renderer, a node that appears twice is written once, which also stops the cycles.
    /// </summary>
    private static List<(TreeViewNode Node, int Depth, string Prefix, bool IsLast)> Flatten(TreeViewDiagram diagram)
    {
        var entries = new List<(TreeViewNode Node, int Depth, int Parent)>();
        var visited = new HashSet<TreeViewNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(TreeViewNode Node, int Depth, int Parent)>();
        PushChildren(diagram.Nodes, depth: 0, parent: -1);
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (!visited.Add(entry.Node))
                continue;

            entries.Add(entry);
            PushChildren(entry.Node.Children, entry.Depth + 1, entries.Count - 1);
        }

        var lastChildren = new Dictionary<int, int>();
        for (var i = 0; i < entries.Count; i++)
        {
            lastChildren[entries[i].Parent] = i;
        }

        var result = new List<(TreeViewNode Node, int Depth, string Prefix, bool IsLast)>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var (node, depth, parent) = entries[i];
            var prefix = parent < 0 ? string.Empty : result[parent].Prefix + (result[parent].IsLast ? "    " : "│   ");
            result.Add((node, depth, prefix, lastChildren[parent] == i));
        }

        return result;

        void PushChildren(IList<TreeViewNode> children, int depth, int parent)
        {
            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], depth, parent));
            }
        }
    }

    private static void WriteNode(StringBuilder builder, TreeViewNode node, bool boxDrawing)
    {
        var name = node.Name;
        CheckText(name, "name");
        if (name.EndsWith("/", StringComparison.Ordinal) && !node.IsDirectory)
            throw new ArgumentException($"The name '{name}' of a file cannot end with '/' in Mermaid: it would be a directory.", nameof(node));

        if (node.IsDirectory)
        {
            name += "/";
        }

        // Mermaid has no escapes: a name is quoted by the quote it does not contain
        var bothQuotes = HasBothQuotes(name);
        var mustQuote = MustQuote(name, boxDrawing);
        if (mustQuote && bothQuotes)
            throw new ArgumentException($"The name '{name}' cannot contain both quotes in Mermaid.", nameof(node));

        if (mustQuote || (ShouldQuote(name) && !bothQuotes))
        {
            var quote = name.Contains('"', StringComparison.Ordinal) ? '\'' : '"';
            builder.Append(quote).Append(name).Append(quote);
        }
        else
        {
            builder.Append(name);
        }

        if (node.CssClass is { Length: > 0 } cssClass)
        {
            if (!TreeViewDiagramParser.IsValidClass(cssClass))
                throw new ArgumentException($"The class '{cssClass}' is not valid in Mermaid: a class has letters, digits, '_' and '-', and starts with a letter or '_'.", nameof(node));

            builder.Append(" :::").Append(cssClass);
        }

        if (node.Icon is { } icon)
        {
            if (icon.Length is 0)
            {
                icon = "none";
            }

            if (!TreeViewDiagramParser.IsValidIcon(icon))
                throw new ArgumentException($"The icon '{icon}' is not valid in Mermaid: an icon is 'name' or 'pack:name', of letters, digits, '_' and '-'.", nameof(node));

            builder.Append(" icon(").Append(icon).Append(')');
        }

        if (node.Description?.Trim() is { Length: > 0 } description)
        {
            CheckText(description, "description");
            builder.Append(" ## ").Append(description);
        }

        builder.Append('\n');
    }

    private static void CheckText(string value, string kind)
    {
        if (value.AsSpan().IndexOfAny('\n', '\r') >= 0)
            throw new ArgumentException($"The {kind} '{value}' cannot contain a line break in Mermaid.", nameof(value));

        if (value.Contains("%%", StringComparison.Ordinal))
            throw new ArgumentException($"The {kind} '{value}' cannot contain '%%' in Mermaid: it starts a comment.", nameof(value));    }

    /// <summary>Indicates whether a name must be quoted for the parser to read it back as is.</summary>
    private static bool MustQuote(string name, bool boxDrawing)
    {
        if (name.Length is 0 || name[0] is '"' or '\'' or ' ' or '\t' || name[^1] is ' ' or '\t' || (!boxDrawing && TreeViewDiagramParser.IsMetadataLine(name)))
            return true;

        if (name.StartsWith(":::", StringComparison.Ordinal) || name.StartsWith("icon(", StringComparison.Ordinal) || name.StartsWith("##", StringComparison.Ordinal))
            return true;

        for (var i = 0; i < name.Length; i++)
        {
            if (TreeViewDiagramParser.AnnotationLength(name, i, out var kind) > 0 || kind is TreeViewDiagramParser.AnnotationKind.Invalid)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Indicates whether a name is better quoted for Mermaid: its lexer reads a title or an accessibility statement in any
    /// line that starts with their keyword, such as <c>titles</c>, and the box-drawing characters are easier to read in quotes.
    /// </summary>
    private static bool ShouldQuote(string name) => ContainsBoxCharacter(name) || name.StartsWith("title", StringComparison.Ordinal) || name.StartsWith("accTitle", StringComparison.Ordinal) || name.StartsWith("accDescr", StringComparison.Ordinal);

    private static bool HasBothQuotes(string value) => value.Contains('"', StringComparison.Ordinal) && value.Contains('\'', StringComparison.Ordinal);

    private static bool ContainsBoxCharacter(string value) => value.AsSpan().IndexOfAny(BoxCharacters) >= 0;
}
