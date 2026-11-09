namespace Meziantou.Framework.Diagrams;

/// <summary>A boundary of a C4 diagram, or a deployment node: a frame with a name around elements and other boundaries.</summary>
public sealed class C4Boundary : C4Item
{
    /// <summary>Initializes a new instance of the <see cref="C4Boundary"/> class.</summary>
    /// <param name="id">The identifier (Mermaid alias).</param>
    /// <param name="label">The name.</param>
    /// <param name="kind">The kind.</param>
    public C4Boundary(string id, DiagramText label, C4BoundaryKind kind = C4BoundaryKind.Boundary)
        : base(id, label)
    {
        Kind = kind;
    }

    /// <summary>Gets or sets the kind of the boundary. A deployment node has a solid frame; the other boundaries are dashed.</summary>
    public C4BoundaryKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the type shown between brackets below the name, such as <c>Ubuntu 16.04 LTS</c> for a deployment node,
    /// or <see langword="null"/> for the default: none for a <see cref="C4BoundaryKind.Boundary"/>, the kind for the others.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Gets the elements of the boundary, placed in rows in order.</summary>
    public IList<C4Element> Elements { get; } = new List<C4Element>();

    /// <summary>Gets the boundaries nested in this one, placed after the elements.</summary>
    public IList<C4Boundary> Boundaries { get; } = new List<C4Boundary>();
}
