using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A tree view: a hierarchy of files and directories drawn as indented rows linked by connector lines, like the output
/// of the <c>tree</c> command, with optional icons and descriptions (Mermaid <c>treeView-beta</c>). As in Mermaid, the
/// tree starts with a root directory named <c>/</c>.
/// </summary>
public sealed class TreeViewDiagram : Diagram
{
    private double _rowIndent = 10;
    private double _paddingX = 5;
    private double _paddingY = 5;
    private double _lineThickness = 1;

    /// <summary>Gets the top-level nodes: the children of the root directory.</summary>
    public IList<TreeViewNode> Nodes { get; } = new List<TreeViewNode>();

    /// <summary>Gets or sets the indentation of a level, in pixels, added to <see cref="PaddingX"/>. The default is 10.</summary>
    public double RowIndent
    {
        get => _rowIndent;
        set => _rowIndent = Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets the horizontal padding of a row, in pixels. The default is 5.</summary>
    public double PaddingX
    {
        get => _paddingX;
        set => _paddingX = Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets the vertical padding of a row, in pixels. The default is 5.</summary>
    public double PaddingY
    {
        get => _paddingY;
        set => _paddingY = Validate(value, 0, 1000);
    }

    /// <summary>Gets or sets the thickness of the connector lines, in pixels. The default is 1.</summary>
    public double LineThickness
    {
        get => _lineThickness;
        set => _lineThickness = Validate(value, 0, 100);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the nodes without an <see cref="TreeViewNode.Icon"/> show the built-in
    /// file or folder icon, or the icon <see cref="FileNameIcons"/> or <see cref="ExtensionIcons"/> give them. The default is <see langword="false"/>.
    /// </summary>
    public bool ShowIcons { get; set; }

    /// <summary>Gets or sets the icon pack of the icon names without a pack, such as <c>material-icon-theme</c>, or <see langword="null"/> for none.</summary>
    public string? DefaultIconPack { get; set; }

    /// <summary>Gets the icons of the files by exact name, used when <see cref="ShowIcons"/> is set. The icon <c>none</c> hides the icon.</summary>
    public IDictionary<string, string> FileNameIcons { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the icons of the files by extension, such as <c>.ts</c> (the dot is optional, the case is ignored), used when <see cref="ShowIcons"/> is set.</summary>
    public IDictionary<string, string> ExtensionIcons { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the font size of the labels and the descriptions, or <see langword="null"/> for the font size of the theme.</summary>
    public double? FontSize { get; set; }

    /// <summary>Gets or sets the color of the labels, or <see langword="null"/> for the text color of the theme.</summary>
    public SceneColor? LabelColor { get; set; }

    /// <summary>Gets or sets the color of the connector lines, or <see langword="null"/> for the line color of the theme.</summary>
    public SceneColor? LineColor { get; set; }

    /// <summary>Gets or sets the color of the icons, or <see langword="null"/> for Mermaid's gray.</summary>
    public SceneColor? IconColor { get; set; }

    /// <summary>Gets or sets the color of the descriptions, or <see langword="null"/> for Mermaid's green.</summary>
    public SceneColor? DescriptionColor { get; set; }

    /// <summary>Gets or sets the fill of the rows of the nodes of class <c>highlight</c>, or <see langword="null"/> for Mermaid's light amber.</summary>
    public SceneColor? HighlightBackground { get; set; }

    /// <summary>Gets or sets the border of the rows of the nodes of class <c>highlight</c>, or <see langword="null"/> for Mermaid's amber.</summary>
    public SceneColor? HighlightBorderColor { get; set; }

    /// <summary>Adds a top-level node.</summary>
    /// <param name="name">The name.</param>
    /// <param name="isDirectory">Whether the node is a directory, drawn in bold.</param>
    /// <returns>The node.</returns>
    public TreeViewNode AddNode(string name, bool isDirectory = false)
    {
        var node = new TreeViewNode(name) { IsDirectory = isDirectory };
        Nodes.Add(node);
        return node;
    }

    /// <summary>Enumerates the nodes and their descendants, depth first, in the order they are drawn.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<TreeViewNode> EnumerateNodes()
    {
        var visited = new HashSet<TreeViewNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<TreeViewNode>(Nodes.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!visited.Add(node))
                continue;

            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private static double Validate(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"The value must be from {minimum} to {maximum}.");

        return value;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => TreeViewDiagramRenderer.Render(this, context, group);
}
