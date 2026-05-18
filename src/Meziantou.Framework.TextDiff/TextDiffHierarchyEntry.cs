namespace Meziantou.Framework;

public sealed class TextDiffHierarchyEntry
{
    public TextDiffHierarchyEntry(TextDiffHierarchyOperation operation, string? oldText, string? newText, IReadOnlyList<TextDiffHierarchyEntry>? children = null)
    {
        Operation = operation;
        OldText = oldText;
        NewText = newText;
        Children = children ?? [];
    }

    public TextDiffHierarchyOperation Operation { get; }

    public string? OldText { get; }

    public string? NewText { get; }

    public IReadOnlyList<TextDiffHierarchyEntry> Children { get; }
}
