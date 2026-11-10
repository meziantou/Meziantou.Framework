using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A Git graph: the commits of branches, their merges and their cherry-picks, written as the Git commands that create
/// them (Mermaid <c>gitGraph</c>). The <see cref="Operations"/> run in order from an empty repository whose current
/// branch is <see cref="MainBranchName"/>.
/// </summary>
public sealed class GitGraphDiagram : Diagram
{
    private string _mainBranchName = "main";

    /// <summary>Gets or sets the direction of the history. The default is <see cref="GitGraphOrientation.LeftToRight"/>.</summary>
    public GitGraphOrientation Orientation { get; set; }

    /// <summary>Gets or sets the name of the branch the history starts on. The default is <c>main</c>.</summary>
    public string MainBranchName
    {
        get => _mainBranchName;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _mainBranchName = value;
        }
    }

    /// <summary>Gets or sets the position of the main branch among the branches, which are sorted by their order. The default is 0.</summary>
    public double MainBranchOrder { get; set; }

    /// <summary>Gets or sets a value that indicates whether the branches are drawn: their names and their lines. The default is <see langword="true"/>.</summary>
    public bool ShowBranches { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether the identifiers of the commits are drawn. The default is <see langword="true"/>.</summary>
    public bool ShowCommitLabels { get; set; } = true;

    /// <summary>Gets or sets a value that indicates whether the identifiers of the commits are rotated by 45 degrees. The default is <see langword="true"/>.</summary>
    public bool RotateCommitLabels { get; set; } = true;

    /// <summary>
    /// Gets or sets a value that indicates whether a commit is placed right after its parent, so the commits of
    /// different branches made at the same time are side by side; otherwise, each commit has its own position, in order.
    /// </summary>
    public bool ParallelCommits { get; set; }

    /// <summary>Gets the operations that build the history, in order.</summary>
    public IList<GitGraphOperation> Operations { get; } = new List<GitGraphOperation>();

    /// <summary>Gets the colors of the branches, by position of the branch, which replace the colors of the theme (Mermaid <c>git0</c>...<c>git7</c>).</summary>
    public IDictionary<int, SceneColor> BranchColors { get; } = new Dictionary<int, SceneColor>();

    /// <summary>Gets the colors of the names of the branches, by position of the branch (Mermaid <c>gitBranchLabel0</c>...).</summary>
    public IDictionary<int, SceneColor> BranchLabelColors { get; } = new Dictionary<int, SceneColor>();

    /// <summary>Gets the colors of the highlighted commits, by position of the branch (Mermaid <c>gitInv0</c>...).</summary>
    public IDictionary<int, SceneColor> HighlightColors { get; } = new Dictionary<int, SceneColor>();

    /// <summary>Gets or sets the color of the identifiers of the commits, or <see langword="null"/> for the color of the theme.</summary>
    public SceneColor? CommitLabelColor { get; set; }

    /// <summary>Gets or sets the background of the identifiers of the commits, or <see langword="null"/> for the color of the theme.</summary>
    public SceneColor? CommitLabelBackground { get; set; }

    /// <summary>Gets or sets the color of the tags, or <see langword="null"/> for the color of the theme.</summary>
    public SceneColor? TagLabelColor { get; set; }

    /// <summary>Gets or sets the background of the tags, or <see langword="null"/> for the color of the theme.</summary>
    public SceneColor? TagLabelBackground { get; set; }

    /// <summary>Gets or sets the border of the tags, or <see langword="null"/> for the color of the theme.</summary>
    public SceneColor? TagLabelBorder { get; set; }

    /// <summary>Adds a commit on the current branch.</summary>
    /// <param name="id">The identifier, or <see langword="null"/> to generate one.</param>
    /// <param name="type">The type.</param>
    /// <param name="tags">The tags.</param>
    /// <returns>The commit.</returns>
    public GitGraphCommit Commit(string? id = null, GitGraphCommitType type = GitGraphCommitType.Normal, params IEnumerable<string> tags)
    {
        var commit = new GitGraphCommit { Id = id, Type = type };
        foreach (var tag in tags)
        {
            commit.Tags.Add(tag);
        }

        Operations.Add(commit);
        return commit;
    }

    /// <summary>Creates a branch from the current commit, and checks it out.</summary>
    /// <param name="name">The name of the branch.</param>
    /// <param name="order">The position of the branch among the branches, or <see langword="null"/> for the order of creation.</param>
    /// <returns>The operation.</returns>
    public GitGraphBranch Branch(string name, int? order = null)
    {
        var branch = new GitGraphBranch(name) { Order = order };
        Operations.Add(branch);
        return branch;
    }

    /// <summary>Makes a branch the current branch.</summary>
    /// <param name="branch">The name of the branch.</param>
    /// <returns>The operation.</returns>
    public GitGraphCheckout Checkout(string branch)
    {
        var checkout = new GitGraphCheckout(branch);
        Operations.Add(checkout);
        return checkout;
    }

    /// <summary>Merges a branch into the current branch.</summary>
    /// <param name="branch">The name of the branch to merge.</param>
    /// <param name="id">The identifier of the merge commit, or <see langword="null"/> to generate one.</param>
    /// <returns>The operation.</returns>
    public GitGraphMerge Merge(string branch, string? id = null)
    {
        var merge = new GitGraphMerge(branch) { Id = id };
        Operations.Add(merge);
        return merge;
    }

    /// <summary>Copies a commit of another branch onto the current branch.</summary>
    /// <param name="commitId">The identifier of the commit to copy.</param>
    /// <param name="parent">The parent of a merge commit to copy the changes from, or <see langword="null"/>.</param>
    /// <returns>The operation.</returns>
    public GitGraphCherryPick CherryPick(string commitId, string? parent = null)
    {
        var cherryPick = new GitGraphCherryPick(commitId) { Parent = parent };
        Operations.Add(cherryPick);
        return cherryPick;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => GitGraphDiagramRenderer.Render(this, context, group);
}
