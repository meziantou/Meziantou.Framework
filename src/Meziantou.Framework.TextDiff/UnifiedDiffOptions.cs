namespace Meziantou.Framework;

/// <summary>Options of <see cref="UnifiedDiff.Create"/>.</summary>
public sealed class UnifiedDiffOptions
{
    /// <summary>Gets or sets the name written after <c>---</c>, the name of the old text. Defaults to <c>"a"</c>.</summary>
    public string OldFileName { get; set; } = "a";

    /// <summary>Gets or sets the name written after <c>+++</c>, the name of the new text. Defaults to <c>"b"</c>.</summary>
    public string NewFileName { get; set; } = "b";

    /// <summary>
    /// Gets or sets the number of unchanged lines written before and after each change. Defaults to 3, as
    /// <c>diff -u</c> and <c>git diff</c>.
    /// </summary>
    /// <remarks>Changes separated by at most twice this number of unchanged lines share a hunk.</remarks>
    public int ContextLines { get; set; } = 3;

    /// <summary>Gets or sets the algorithm used to compare the lines. Defaults to <see cref="TextDiffAlgorithm.Myers"/>.</summary>
    public TextDiffAlgorithm Algorithm { get; set; } = TextDiffAlgorithm.Myers;

    /// <summary>Gets or sets the time limit of the diff. See <see cref="TextDiffOptions.Timeout"/>.</summary>
    public TimeSpan? Timeout { get; set; }
}
