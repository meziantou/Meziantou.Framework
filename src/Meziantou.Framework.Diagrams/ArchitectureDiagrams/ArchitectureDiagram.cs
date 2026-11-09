using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// An architecture diagram: services drawn as icons, junctions, groups, and edges that leave and reach the services by
/// a given side (Mermaid <c>architecture-beta</c>). The sides of the edges place the services on a grid: an edge from
/// the right of A to the left of B puts B on the right of A.
/// </summary>
public sealed class ArchitectureDiagram : Diagram
{
    private double _iconSize = 80;
    private double _groupPadding = 40;

    /// <summary>Gets the top-level groups.</summary>
    public IList<ArchitectureGroup> Groups { get; } = new List<ArchitectureGroup>();

    /// <summary>Gets the services and the junctions that are in no group, in order.</summary>
    public IList<ArchitectureNode> Nodes { get; } = new List<ArchitectureNode>();

    /// <summary>Gets the edges.</summary>
    public IList<ArchitectureEdge> Edges { get; } = new List<ArchitectureEdge>();

    /// <summary>Gets the alignments: services and junctions placed on a row or a column, in order.</summary>
    public IList<ArchitectureAlignment> Alignments { get; } = new List<ArchitectureAlignment>();

    /// <summary>Gets or sets the size of the icons of the services, in pixels. The default is 80.</summary>
    public double IconSize
    {
        get => _iconSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _iconSize = value;
        }
    }

    /// <summary>Gets or sets the space between the frame of a group and its content, in pixels. The default is 40.</summary>
    public double GroupPadding
    {
        get => _groupPadding;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _groupPadding = value;
        }
    }

    /// <summary>Adds a service that is in no group.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="title">The title shown below the icon, or <see langword="null"/> for none.</param>
    /// <param name="icon">The icon: <c>cloud</c>, <c>database</c>, <c>disk</c>, <c>internet</c> or <c>server</c>, or <see langword="null"/> for none.</param>
    /// <returns>The service.</returns>
    public ArchitectureService AddService(string id, DiagramText? title = null, string? icon = null)
    {
        var service = new ArchitectureService(id) { Title = title, Icon = icon };
        Nodes.Add(service);
        return service;
    }

    /// <summary>Adds a group that is in no group.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="title">The title, or <see langword="null"/> for none.</param>
    /// <param name="icon">The icon shown before the title, or <see langword="null"/> for none.</param>
    /// <returns>The group.</returns>
    public ArchitectureGroup AddGroup(string id, DiagramText? title = null, string? icon = null)
    {
        var group = new ArchitectureGroup(id) { Title = title, Icon = icon };
        Groups.Add(group);
        return group;
    }

    /// <summary>Adds an edge.</summary>
    /// <param name="from">The first node.</param>
    /// <param name="fromSide">The side of <paramref name="from"/> the edge leaves.</param>
    /// <param name="to">The second node.</param>
    /// <param name="toSide">The side of <paramref name="to"/> the edge reaches.</param>
    /// <returns>The edge.</returns>
    public ArchitectureEdge AddEdge(ArchitectureNode from, ArchitectureSide fromSide, ArchitectureNode to, ArchitectureSide toSide)
    {
        var edge = new ArchitectureEdge(from, fromSide, to, toSide);
        Edges.Add(edge);
        return edge;
    }

    /// <summary>Enumerates the groups and their nested groups, depth first.</summary>
    /// <returns>The groups.</returns>
    public IEnumerable<ArchitectureGroup> EnumerateGroups()
    {
        var visited = new HashSet<ArchitectureGroup>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<ArchitectureGroup>(Groups.Reverse());
        while (stack.Count > 0)
        {
            var group = stack.Pop();
            if (!visited.Add(group))
                continue;

            yield return group;
            for (var i = group.Groups.Count - 1; i >= 0; i--)
            {
                stack.Push(group.Groups[i]);
            }
        }
    }

    /// <summary>Enumerates the services and the junctions: those in no group, then those of the groups.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<ArchitectureNode> EnumerateNodes()
    {
        var visited = new HashSet<ArchitectureNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in Nodes.Concat(EnumerateGroups().SelectMany(group => group.Nodes)))
        {
            if (visited.Add(node))
                yield return node;
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => ArchitectureDiagramRenderer.Render(this, context, group);
}
