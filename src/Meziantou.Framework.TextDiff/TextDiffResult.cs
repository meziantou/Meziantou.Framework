namespace Meziantou.Framework;

public sealed class TextDiffResult
{
    internal TextDiffResult(IReadOnlyList<TextDiffEntry> entries, bool hasDifferences)
    {
        Entries = entries;
        HasDifferences = hasDifferences;
    }

    public IReadOnlyList<TextDiffEntry> Entries { get; }

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