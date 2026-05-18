namespace Meziantou.Framework;

public sealed class TextDiffHierarchyResult
{
    internal TextDiffHierarchyResult(IReadOnlyList<TextDiffHierarchyEntry> entries, bool hasDifferences)
    {
        Entries = entries;
        HasDifferences = hasDifferences;
    }

    public IReadOnlyList<TextDiffHierarchyEntry> Entries { get; }

    public bool HasDifferences { get; }
}
