namespace Meziantou.Framework.Diagrams;

/// <summary>Copies a commit of another branch onto the current branch of a Git graph (Mermaid <c>cherry-pick</c>).</summary>
public sealed class GitGraphCherryPick : GitGraphOperation
{
    /// <summary>Initializes a new instance of the <see cref="GitGraphCherryPick"/> class.</summary>
    /// <param name="commitId">The identifier of the commit to copy.</param>
    public GitGraphCherryPick(string commitId)
    {
        ArgumentException.ThrowIfNullOrEmpty(commitId);
        CommitId = commitId;
    }

    /// <summary>Gets the identifier of the commit to copy.</summary>
    public string CommitId { get; }

    /// <summary>Gets or sets the parent of a merge commit to copy the changes from, or <see langword="null"/>. A merge commit needs one.</summary>
    public string? Parent { get; set; }

    /// <summary>Gets the tags, drawn above the new commit. Without tags, the commit has the tag <c>cherry-pick:</c> and the identifier it copies, unless <see cref="ShowDefaultTag"/> is <see langword="false"/>.</summary>
    public IList<string> Tags { get; } = new List<string>();

    /// <summary>Gets or sets a value that indicates whether a cherry-pick without tags has the tag <c>cherry-pick:</c> and the identifier it copies. The default is <see langword="true"/>.</summary>
    public bool ShowDefaultTag { get; set; } = true;
}
