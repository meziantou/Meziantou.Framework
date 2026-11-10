namespace Meziantou.Framework.Diagrams;

/// <summary>Merges a branch into the current branch of a Git graph with a merge commit (Mermaid <c>merge</c>).</summary>
public sealed class GitGraphMerge : GitGraphOperation
{
    /// <summary>Initializes a new instance of the <see cref="GitGraphMerge"/> class.</summary>
    /// <param name="branch">The name of the branch to merge.</param>
    public GitGraphMerge(string branch)
    {
        ArgumentException.ThrowIfNullOrEmpty(branch);
        Branch = branch;
    }

    /// <summary>Gets the name of the branch to merge.</summary>
    public string Branch { get; }

    /// <summary>Gets or sets the identifier of the merge commit, or <see langword="null"/> to generate one. Only an explicit identifier is drawn.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the type that replaces the symbol of a merge commit, or <see langword="null"/> for the merge symbol.</summary>
    public GitGraphCommitType? Type { get; set; }

    /// <summary>Gets the tags, drawn above the merge commit.</summary>
    public IList<string> Tags { get; } = new List<string>();
}
