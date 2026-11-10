namespace Meziantou.Framework;

/// <summary>A region of a three-way merge, with its text in the base and in both sides.</summary>
public sealed class TextMergeChunk
{
    internal TextMergeChunk(TextMergeChunkKind kind, string baseText, string oursText, string theirsText)
    {
        Kind = kind;
        BaseText = baseText;
        OursText = oursText;
        TheirsText = theirsText;
    }

    /// <summary>Gets how the region was resolved.</summary>
    public TextMergeChunkKind Kind { get; }

    /// <summary>Gets the text of the region in the base.</summary>
    public string BaseText { get; }

    /// <summary>Gets the text of the region in "ours".</summary>
    public string OursText { get; }

    /// <summary>Gets the text of the region in "theirs".</summary>
    public string TheirsText { get; }

    /// <summary>
    /// Gets the merged text of the region: <see cref="TheirsText"/> for <see cref="TextMergeChunkKind.ChangedInTheirs"/>,
    /// <see cref="OursText"/> otherwise, or <see langword="null"/> for a <see cref="TextMergeChunkKind.Conflict"/>.
    /// </summary>
    public string? Text => Kind switch
    {
        TextMergeChunkKind.Conflict => null,
        TextMergeChunkKind.ChangedInTheirs => TheirsText,
        _ => OursText,
    };

    public override string ToString() => $"{Kind}: {Text ?? OursText}";
}
