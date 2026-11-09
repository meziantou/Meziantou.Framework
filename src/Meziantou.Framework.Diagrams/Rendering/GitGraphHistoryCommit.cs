namespace Meziantou.Framework.Diagrams.Rendering;

internal sealed class GitGraphHistoryCommit(string id, int sequence, string branch, bool isCustomId, GitGraphHistoryCommitKind kind)
{
    public string Id { get; } = id;

    public int Sequence { get; } = sequence;

    public string Branch { get; } = branch;

    public bool IsCustomId { get; } = isCustomId;

    public GitGraphHistoryCommitKind Kind { get; } = kind;

    /// <summary>Gets or sets the type of a commit, or the type that replaces the symbol of a merge commit.</summary>
    public GitGraphCommitType? Type { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<GitGraphHistoryCommit> Parents { get; set; } = [];
}
