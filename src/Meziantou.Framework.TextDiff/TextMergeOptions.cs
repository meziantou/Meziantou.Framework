namespace Meziantou.Framework;

/// <summary>Options of <see cref="TextMerge.Merge"/>.</summary>
public sealed class TextMergeOptions
{
    /// <summary>
    /// Gets or sets the options of the diffs between the base and each side: the chunker, the algorithm, the comparison
    /// options and the time limit. Defaults to <see langword="null"/>: a line diff with the default options.
    /// </summary>
    /// <remarks><see cref="TextDiffOptions.DetectMoves"/> is ignored.</remarks>
    public TextDiffOptions? DiffOptions { get; set; }

    /// <summary>Gets or sets how <see cref="TextMergeResult.Text"/> writes the conflicts. Defaults to <see cref="TextMergeConflictStyle.Merge"/>.</summary>
    public TextMergeConflictStyle ConflictStyle { get; set; }

    /// <summary>Gets or sets the label written after the <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> marker. Defaults to <c>"ours"</c>.</summary>
    public string OursLabel { get; set; } = "ours";

    /// <summary>Gets or sets the label written after the <c>|||||||</c> marker. Defaults to <c>"base"</c>.</summary>
    public string BaseLabel { get; set; } = "base";

    /// <summary>Gets or sets the label written after the <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> marker. Defaults to <c>"theirs"</c>.</summary>
    public string TheirsLabel { get; set; } = "theirs";
}
