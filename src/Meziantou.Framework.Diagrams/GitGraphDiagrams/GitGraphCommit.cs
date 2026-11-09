namespace Meziantou.Framework.Diagrams;

/// <summary>A commit on the current branch of a Git graph (Mermaid <c>commit</c>).</summary>
public sealed class GitGraphCommit : GitGraphOperation
{
    /// <summary>Gets or sets the identifier, shown below the commit, or <see langword="null"/> to generate one.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the message of the commit, or <see langword="null"/> for none. Mermaid does not draw it.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the type, which sets the symbol of the commit.</summary>
    public GitGraphCommitType Type { get; set; }

    /// <summary>Gets the tags, drawn above the commit.</summary>
    public IList<string> Tags { get; } = new List<string>();
}
