using System.Globalization;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Runs the operations of a Git graph as Mermaid does, and gives the commits and the branches. An operation that
/// Mermaid rejects, such as a checkout of a branch that does not exist, is skipped.
/// </summary>
internal sealed class GitGraphHistory
{
    private GitGraphHistory()
    {
    }

    public List<GitGraphHistoryCommit> Commits { get; } = [];

    /// <summary>Gets the branches, sorted by their order, the order of creation breaking the ties.</summary>
    public List<string> Branches { get; } = [];

    public static GitGraphHistory Create(GitGraphDiagram diagram)
    {
        var history = new GitGraphHistory();
        var heads = new Dictionary<string, GitGraphHistoryCommit?>(StringComparer.Ordinal) { [diagram.MainBranchName] = null };
        var orders = new List<(string Name, double Order)> { (diagram.MainBranchName, double.IsFinite(diagram.MainBranchOrder) ? diagram.MainBranchOrder : 0) };
        var ids = new Dictionary<string, GitGraphHistoryCommit>(StringComparer.Ordinal);
        var current = diagram.MainBranchName;
        GitGraphHistoryCommit? head = null;
        foreach (var operation in diagram.Operations)
        {
            switch (operation)
            {
                case GitGraphCommit commit:
                    Add(new GitGraphHistoryCommit(commit.Id ?? GenerateId(history.Commits.Count), history.Commits.Count, current, commit.Id is not null, GitGraphHistoryCommitKind.Normal)
                    {
                        Type = commit.Type,
                        Tags = [.. commit.Tags],
                        Parents = head is null ? [] : [head],
                    });
                    break;
                case GitGraphBranch branch:
                    if (heads.ContainsKey(branch.Name))
                        break;

                    heads[branch.Name] = head;
                    orders.Add((branch.Name, branch.Order ?? 0));
                    current = branch.Name;
                    break;
                case GitGraphCheckout checkout:
                    if (heads.TryGetValue(checkout.Branch, out var checkoutHead))
                    {
                        current = checkout.Branch;
                        head = checkoutHead;
                    }

                    break;
                case GitGraphMerge merge:
                    {
                        if (!heads.TryGetValue(merge.Branch, out var other) || other is null || head is null || merge.Branch == current || head == other || head.Branch == merge.Branch)
                            break;

                        if (merge.Id is not null && ids.ContainsKey(merge.Id))
                            break;

                        Add(new GitGraphHistoryCommit(merge.Id ?? GenerateId(history.Commits.Count), history.Commits.Count, current, merge.Id is not null, GitGraphHistoryCommitKind.Merge)
                        {
                            Type = merge.Type,
                            Tags = [.. merge.Tags],
                            Parents = [head, other],
                        });
                        break;
                    }

                case GitGraphCherryPick cherryPick:
                    {
                        if (!ids.TryGetValue(cherryPick.CommitId, out var source) || head is null || source.Branch == current)
                            break;

                        if (cherryPick.Parent is not null && !source.Parents.Any(parent => parent.Id == cherryPick.Parent))
                            break;

                        if (source.Kind is GitGraphHistoryCommitKind.Merge && cherryPick.Parent is null)
                            break;

                        List<string> tags = cherryPick.Tags.Count > 0
                            ? [.. cherryPick.Tags.Where(tag => tag.Length > 0)]
                            : cherryPick.ShowDefaultTag ? ["cherry-pick:" + source.Id + (source.Kind is GitGraphHistoryCommitKind.Merge ? "|parent:" + cherryPick.Parent : string.Empty)] : [];
                        Add(new GitGraphHistoryCommit(GenerateId(history.Commits.Count), history.Commits.Count, current, isCustomId: false, GitGraphHistoryCommitKind.CherryPick)
                        {
                            Tags = tags,
                            Parents = [head, source],
                        });
                        break;
                    }
            }
        }

        history.Branches.AddRange(orders.Select((item, index) => (item.Name, item.Order, Index: index)).OrderBy(item => item.Order).ThenBy(item => item.Index).Select(item => item.Name));
        return history;

        void Add(GitGraphHistoryCommit commit)
        {
            history.Commits.Add(commit);
            ids[commit.Id] = commit;
            head = commit;
            heads[current] = commit;
        }
    }

    /// <summary>Returns a deterministic identifier for a commit without one; Mermaid uses a random one, with the same shape.</summary>
    internal static string GenerateId(int sequence)
    {
        var hash = unchecked((uint)(sequence + 1) * 2654435761u);
        return sequence.ToString(CultureInfo.InvariantCulture) + "-" + (hash & 0xFFFFFFF).ToString("x7", CultureInfo.InvariantCulture);
    }
}
