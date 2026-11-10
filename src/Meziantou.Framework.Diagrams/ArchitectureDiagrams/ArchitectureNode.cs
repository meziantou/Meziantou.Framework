namespace Meziantou.Framework.Diagrams;

/// <summary>A node of an architecture diagram that edges link: a service or a junction.</summary>
public abstract class ArchitectureNode
{
    private protected ArchitectureNode(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>Gets the identifier, unique in the diagram.</summary>
    public string Id { get; }

    /// <summary>Returns the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Id;
}
