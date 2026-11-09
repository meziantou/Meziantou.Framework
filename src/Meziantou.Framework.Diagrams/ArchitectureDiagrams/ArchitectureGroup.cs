namespace Meziantou.Framework.Diagrams;

/// <summary>A group of an architecture diagram: a frame with an icon and a title around services, junctions and groups.</summary>
public sealed class ArchitectureGroup
{
    /// <summary>Initializes a new instance of the <see cref="ArchitectureGroup"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    public ArchitectureGroup(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>Gets the identifier, unique in the diagram.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the title, or <see langword="null"/> for none.</summary>
    public DiagramText? Title { get; set; }

    /// <summary>Gets or sets the name of the icon shown before the title, or <see langword="null"/> for none.</summary>
    public string? Icon { get; set; }

    /// <summary>Gets the services and the junctions of the group.</summary>
    public IList<ArchitectureNode> Nodes { get; } = new List<ArchitectureNode>();

    /// <summary>Gets the groups nested in this one.</summary>
    public IList<ArchitectureGroup> Groups { get; } = new List<ArchitectureGroup>();

    /// <summary>Returns the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
