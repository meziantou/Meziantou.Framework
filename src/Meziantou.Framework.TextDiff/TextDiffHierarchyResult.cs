namespace Meziantou.Framework;

/// <summary>The result of <see cref="TextDiff.ComputeHierarchyDiff"/>: the chunks produced by the first chunker,
/// each of which may carry a finer diff of its own.</summary>
public sealed class TextDiffHierarchyResult
{
    internal TextDiffHierarchyResult(IReadOnlyList<TextDiffHierarchyEntry> entries, bool hasDifferences)
    {
        Entries = entries;
        HasDifferences = hasDifferences;
    }


    /// <summary>Gets the chunks of both texts at the outermost chunking level, in order.</summary>
    public IReadOnlyList<TextDiffHierarchyEntry> Entries { get; }

    /// <summary>Gets a value indicating whether the two texts differ under the comparison options that were used.</summary>
    public bool HasDifferences { get; }
}
