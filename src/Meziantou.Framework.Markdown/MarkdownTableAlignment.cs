namespace Meziantou.Framework.Markdown;

/// <summary>The alignment of a column of a table, which the colons of its cell of the delimiter row give.</summary>
public enum MarkdownTableAlignment
{
    /// <summary>No colon: <c>---</c>.</summary>
    None,

    /// <summary>A colon on the left: <c>:--</c>.</summary>
    Left,

    /// <summary>A colon on each side: <c>:-:</c>.</summary>
    Center,

    /// <summary>A colon on the right: <c>--:</c>.</summary>
    Right,
}
