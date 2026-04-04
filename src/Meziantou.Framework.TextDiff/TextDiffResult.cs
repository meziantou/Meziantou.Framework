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
}
