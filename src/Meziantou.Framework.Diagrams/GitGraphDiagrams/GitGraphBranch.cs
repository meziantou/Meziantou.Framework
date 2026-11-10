namespace Meziantou.Framework.Diagrams;

/// <summary>Creates a branch from the current commit of a Git graph and checks it out (Mermaid <c>branch</c>).</summary>
public sealed class GitGraphBranch : GitGraphOperation
{
    /// <summary>Initializes a new instance of the <see cref="GitGraphBranch"/> class.</summary>
    /// <param name="name">The name of the branch.</param>
    public GitGraphBranch(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>Gets the name of the branch.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the position of the branch among the branches, or <see langword="null"/> for the order of creation (Mermaid <c>order:</c>).</summary>
    public int? Order { get; set; }
}
