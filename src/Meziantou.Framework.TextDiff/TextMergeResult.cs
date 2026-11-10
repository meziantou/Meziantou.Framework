namespace Meziantou.Framework;

/// <summary>The result of <see cref="TextMerge.Merge"/>.</summary>
public sealed class TextMergeResult
{
    internal TextMergeResult(IReadOnlyList<TextMergeChunk> chunks, bool hasConflicts, string text)
    {
        Chunks = chunks;
        HasConflicts = hasConflicts;
        Text = text;
    }

    /// <summary>Gets the regions of the merge, in order.</summary>
    public IReadOnlyList<TextMergeChunk> Chunks { get; }

    /// <summary>Gets a value indicating whether a region is a <see cref="TextMergeChunkKind.Conflict"/>.</summary>
    public bool HasConflicts { get; }

    /// <summary>
    /// Gets the merged text. Each conflict is written between conflict markers, as set by
    /// <see cref="TextMergeOptions.ConflictStyle"/>.
    /// </summary>
    /// <remarks>
    /// A marker always starts a line: a line feed is added before a marker that would follow text that does not end with
    /// <c>"\n"</c>.
    /// </remarks>
    public string Text { get; }

    public override string ToString() => Text;
}
