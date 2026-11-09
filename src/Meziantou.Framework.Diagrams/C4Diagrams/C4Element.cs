namespace Meziantou.Framework.Diagrams;

/// <summary>An element of a C4 diagram: a person, a software system, a container or a component.</summary>
public sealed class C4Element : C4Item
{
    /// <summary>Initializes a new instance of the <see cref="C4Element"/> class.</summary>
    /// <param name="id">The identifier (Mermaid alias).</param>
    /// <param name="kind">The kind.</param>
    /// <param name="label">The name.</param>
    public C4Element(string id, C4ElementKind kind, DiagramText label)
        : base(id, label)
    {
        Kind = kind;
    }

    /// <summary>Gets or sets the kind of the element, shown between brackets and with the colors of C4.</summary>
    public C4ElementKind Kind { get; set; }

    /// <summary>Gets or sets a value that indicates whether the element is outside of the system being described, drawn in gray (Mermaid <c>_Ext</c>).</summary>
    public bool IsExternal { get; set; }

    /// <summary>Gets or sets the shape of the element. A person is always drawn as a person.</summary>
    public C4ElementShape Shape { get; set; }

    /// <summary>Gets or sets the technology of a container or a component, shown with its kind, or <see langword="null"/> for none.</summary>
    public string? Technology { get; set; }
}
