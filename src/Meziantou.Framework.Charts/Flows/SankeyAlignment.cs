namespace Meziantou.Framework.Charts;

/// <summary>How the nodes of a <see cref="SankeyMark{T}"/> are placed in columns.</summary>
public enum SankeyAlignment
{
    /// <summary>Like <see cref="Left"/>, except that the nodes without outgoing links are in the last column.</summary>
    Justify,

    /// <summary>Each node is in the column of its depth: the length of the longest path from a source to it.</summary>
    Left,

    /// <summary>Each node is in the column of its height, counted from the last column: the length of the longest path from it to a sink.</summary>
    Right,

    /// <summary>Like <see cref="Left"/>, except that the sources are moved right, next to their first target.</summary>
    Center,
}
