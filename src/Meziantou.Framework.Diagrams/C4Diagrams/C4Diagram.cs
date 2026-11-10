using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A C4 diagram of a software architecture: people, systems, containers and components, grouped in boundaries or
/// deployment nodes, and the relationships between them (Mermaid <c>C4Context</c>, <c>C4Container</c>,
/// <c>C4Component</c>, <c>C4Dynamic</c> and <c>C4Deployment</c>). As in Mermaid, the elements are placed in rows in the
/// order they are declared, not by an automatic layout.
/// </summary>
public sealed class C4Diagram : Diagram
{
    private int _shapesPerRow = 4;
    private int _boundariesPerRow = 2;

    /// <summary>Gets or sets the kind of the diagram. A <see cref="C4DiagramKind.Dynamic"/> diagram numbers its relationships.</summary>
    public C4DiagramKind Kind { get; set; }

    /// <summary>Gets the elements that are in no boundary.</summary>
    public IList<C4Element> Elements { get; } = new List<C4Element>();

    /// <summary>Gets the top-level boundaries.</summary>
    public IList<C4Boundary> Boundaries { get; } = new List<C4Boundary>();

    /// <summary>Gets the relationships, drawn in order.</summary>
    public IList<C4Relationship> Relationships { get; } = new List<C4Relationship>();

    /// <summary>Gets or sets how many elements a row has, at most. The default is 4 (Mermaid <c>c4ShapeInRow</c>).</summary>
    public int ShapesPerRow
    {
        get => _shapesPerRow;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _shapesPerRow = value;
        }
    }

    /// <summary>Gets or sets how many boundaries a row has, at most. The default is 2 (Mermaid <c>c4BoundaryInRow</c>).</summary>
    public int BoundariesPerRow
    {
        get => _boundariesPerRow;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _boundariesPerRow = value;
        }
    }

    /// <summary>Gets or sets a value that indicates whether the texts wrap to the width of the elements. The default is <see langword="true"/>.</summary>
    public bool Wrap { get; set; } = true;

    /// <summary>Adds an element that is in no boundary.</summary>
    /// <param name="id">The identifier (Mermaid alias).</param>
    /// <param name="kind">The kind.</param>
    /// <param name="label">The name.</param>
    /// <param name="description">The description, or <see langword="null"/> for none.</param>
    /// <returns>The element.</returns>
    public C4Element AddElement(string id, C4ElementKind kind, DiagramText label, DiagramText? description = null)
    {
        var element = new C4Element(id, kind, label) { Description = description };
        Elements.Add(element);
        return element;
    }

    /// <summary>Adds a top-level boundary.</summary>
    /// <param name="id">The identifier (Mermaid alias).</param>
    /// <param name="label">The name.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The boundary.</returns>
    public C4Boundary AddBoundary(string id, DiagramText label, C4BoundaryKind kind = C4BoundaryKind.Boundary)
    {
        var boundary = new C4Boundary(id, label, kind);
        Boundaries.Add(boundary);
        return boundary;
    }

    /// <summary>Adds a relationship.</summary>
    /// <param name="from">The source.</param>
    /// <param name="to">The target.</param>
    /// <param name="label">The text.</param>
    /// <param name="technology">The technology, shown between brackets below the text, or <see langword="null"/> for none.</param>
    /// <returns>The relationship.</returns>
    public C4Relationship AddRelationship(C4Item from, C4Item to, DiagramText label, string? technology = null)
    {
        var relationship = new C4Relationship(from, to, label) { Technology = technology };
        Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>Finds an element or a boundary by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The item, or <see langword="null"/> when there is none.</returns>
    public C4Item? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var element in Elements)
        {
            if (string.Equals(element.Id, id, StringComparison.Ordinal))
                return element;
        }

        foreach (var boundary in EnumerateBoundaries())
        {
            if (string.Equals(boundary.Id, id, StringComparison.Ordinal))
                return boundary;

            foreach (var element in boundary.Elements)
            {
                if (string.Equals(element.Id, id, StringComparison.Ordinal))
                    return element;
            }
        }

        return null;
    }

    /// <summary>Enumerates the boundaries and their nested boundaries, depth first.</summary>
    /// <returns>The boundaries.</returns>
    public IEnumerable<C4Boundary> EnumerateBoundaries()
    {
        var visited = new HashSet<C4Boundary>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<C4Boundary>(Boundaries.Reverse());
        while (stack.Count > 0)
        {
            var boundary = stack.Pop();
            if (!visited.Add(boundary))
                continue;

            yield return boundary;
            for (var i = boundary.Boundaries.Count - 1; i >= 0; i--)
            {
                stack.Push(boundary.Boundaries[i]);
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => C4DiagramRenderer.Render(this, context, group);
}
