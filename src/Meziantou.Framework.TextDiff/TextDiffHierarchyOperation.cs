namespace Meziantou.Framework;

/// <summary>Specifies what happened to a chunk between the old and the new text at one level of a hierarchy diff.</summary>
public enum TextDiffHierarchyOperation
{
    /// <summary>The chunk is present in both texts.</summary>
    Equal,

    /// <summary>The chunk is only present in the new text.</summary>
    Insert,

    /// <summary>The chunk is only present in the old text.</summary>
    Delete,

    /// <summary>
    /// A chunk of the old text was replaced by a chunk of the new text. When a finer chunker is available,
    /// <see cref="TextDiffHierarchyEntry.Children"/> holds the diff of the two chunks at that level.
    /// </summary>
    Replace,
}
