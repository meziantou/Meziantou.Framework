namespace Meziantou.Framework;

/// <summary>Specifies how <see cref="TextMergeResult.Text"/> writes a conflict.</summary>
public enum TextMergeConflictStyle
{
    /// <summary>
    /// Writes both sides between <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>, <c>=======</c> and <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>
    /// markers, as <c>git merge</c> does by default.
    /// </summary>
    Merge,

    /// <summary>
    /// Also writes the base text after a <c>|||||||</c> marker, as <c>git merge</c> does with
    /// <c>merge.conflictStyle=diff3</c>.
    /// </summary>
    Diff3,
}
