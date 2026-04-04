namespace Meziantou.Framework;

/// <summary>Specifies the algorithm used to compute text differences.</summary>
public enum TextDiffAlgorithm
{
    /// <summary>
    /// Uses Myers shortest-edit-script algorithm.
    /// Use this as the default when you want high-quality, minimal edit scripts.
    /// </summary>
    Myers,

    /// <summary>
    /// Uses Patience diff.
    /// Use this when human readability is more important than strictly minimal edits.
    /// </summary>
    Patience,

    /// <summary>
    /// Uses Histogram diff, favoring low-frequency anchors.
    /// Use this when you want good practical performance on large, repetitive texts.
    /// </summary>
    Histogram,

    /// <summary>
    /// Uses Hunt-Szymanski LCS-based diff.
    /// Use this when inputs are large and matches are relatively sparse.
    /// </summary>
    HuntSzymanski,
}
