namespace Meziantou.Framework.Diagrams;

/// <summary>A node of a tree view: a file or a directory, with its children.</summary>
public sealed class TreeViewNode
{
    private string _name;

    /// <summary>Initializes a new instance of the <see cref="TreeViewNode"/> class.</summary>
    /// <param name="name">The name. It can contain spaces, and be empty.</param>
    public TreeViewNode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
    }

    /// <summary>Gets or sets the name, shown as is.</summary>
    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _name = value;
        }
    }

    /// <summary>Gets or sets a value that indicates whether the node is a directory, drawn in bold (Mermaid: a name that ends with <c>/</c>).</summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Gets or sets the icon: <c>file</c> or <c>folder</c> for the built-in icons, an icon of an icon pack of Mermaid such
    /// as <c>logos:react</c> (drawn as a question mark), <c>none</c> to hide the icon, or <see langword="null"/> for the
    /// icon <see cref="TreeViewDiagram.ShowIcons"/> gives (Mermaid <c>icon(name)</c>).
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>Gets or sets the CSS class of the row, or <see langword="null"/> for none. The class <c>highlight</c> draws the row on an amber background (Mermaid <c>:::class</c>).</summary>
    public string? CssClass { get; set; }

    /// <summary>Gets or sets the description, shown in italic in a column after the names, or <see langword="null"/> for none (Mermaid <c>## text</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Gets the children.</summary>
    public IList<TreeViewNode> Children { get; } = new List<TreeViewNode>();

    /// <summary>Adds a child.</summary>
    /// <param name="name">The name.</param>
    /// <param name="isDirectory">Whether the child is a directory.</param>
    /// <returns>The child.</returns>
    public TreeViewNode AddChild(string name, bool isDirectory = false)
    {
        var node = new TreeViewNode(name) { IsDirectory = isDirectory };
        Children.Add(node);
        return node;
    }

    /// <summary>Returns the name.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
