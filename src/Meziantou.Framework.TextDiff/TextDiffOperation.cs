namespace Meziantou.Framework;

/// <summary>Specifies what happened to a chunk between the old and the new text.</summary>
public enum TextDiffOperation
{
    /// <summary>The chunk is present in both texts.</summary>
    Equal,

    /// <summary>The chunk is only present in the new text.</summary>
    Insert,

    /// <summary>The chunk is only present in the old text.</summary>
    Delete,
}
