namespace Meziantou.Framework;

/// <summary>One chunk of a hierarchy diff, with the finer diff of its two sides when there is a deeper chunking level.</summary>
public sealed class TextDiffHierarchyEntry
{
    public TextDiffHierarchyEntry(TextDiffHierarchyOperation operation, string? oldText, string? newText, IReadOnlyList<TextDiffHierarchyEntry>? children = null)
    {
        Operation = operation;
        OldText = oldText;
        NewText = newText;
        Children = children ?? [];
    }


    /// <summary>Gets what happened to this chunk between the two texts.</summary>
    public TextDiffHierarchyOperation Operation { get; }

    /// <summary>
    /// Gets the chunk as it appears in the old text, or <see langword="null"/> when <see cref="Operation"/> is
    /// <see cref="TextDiffHierarchyOperation.Insert"/>.
    /// </summary>
    public string? OldText { get; }

    /// <summary>
    /// Gets the chunk as it appears in the new text, or <see langword="null"/> when <see cref="Operation"/> is
    /// <see cref="TextDiffHierarchyOperation.Delete"/>.
    /// </summary>
    public string? NewText { get; }

    /// <summary>
    /// Gets the diff of <see cref="OldText"/> against <see cref="NewText"/> at the next chunking level. Empty
    /// unless <see cref="Operation"/> is <see cref="TextDiffHierarchyOperation.Replace"/> and a finer chunker
    /// was supplied.
    /// </summary>
    public IReadOnlyList<TextDiffHierarchyEntry> Children { get; }
}
