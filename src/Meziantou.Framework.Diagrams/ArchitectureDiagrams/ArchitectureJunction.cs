namespace Meziantou.Framework.Diagrams;

/// <summary>A junction of an architecture diagram: a point where edges meet, which is not drawn.</summary>
public sealed class ArchitectureJunction : ArchitectureNode
{
    /// <summary>Initializes a new instance of the <see cref="ArchitectureJunction"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    public ArchitectureJunction(string id)
        : base(id)
    {
    }
}
