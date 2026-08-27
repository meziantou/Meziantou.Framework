namespace Meziantou.Framework;

/// <summary>The result of <see cref="TextDiff.ComputeDiff"/>: the chunks of both texts in order, each tagged with
/// what happened to it.</summary>
public sealed class TextDiffResult
{
    internal TextDiffResult(IReadOnlyList<TextDiffEntry> entries, bool hasDifferences)
    {
        Entries = entries;
        HasDifferences = hasDifferences;
    }


    /// <summary>Gets the chunks of both texts, in order.</summary>
    public IReadOnlyList<TextDiffEntry> Entries { get; }

    /// <summary>Gets a value indicating whether the two texts differ under the comparison options that were used.</summary>
    public bool HasDifferences { get; }

    public override string ToString()
    {
        var insertCount = 0;
        var deleteCount = 0;
        var equalCount = 0;
        foreach (var entry in Entries)
        {
            switch (entry.Operation)
            {
                case TextDiffOperation.Equal:
                    equalCount++;
                    break;
                case TextDiffOperation.Insert:
                    insertCount++;
                    break;
                case TextDiffOperation.Delete:
                    deleteCount++;
                    break;
            }
        }

        return $"Insertions: {insertCount}, Deletions: {deleteCount}, Equals: {equalCount}";
    }
}