namespace Meziantou.Framework.Charts;

/// <summary>How a <see cref="TreemapLayout"/> divides the rectangle of a node among its children.</summary>
public enum TreemapTiling
{
    /// <summary>Rows of rectangles as close to the golden ratio as possible (Bruls et al., 2000).</summary>
    Squarified,

    /// <summary>A balanced binary split, alternately horizontal and vertical by the shape of the rectangle.</summary>
    Binary,

    /// <summary>Horizontal strips, from top to bottom.</summary>
    Slice,

    /// <summary>Vertical strips, from left to right.</summary>
    Dice,

    /// <summary>Strips that alternate between <see cref="Slice"/> and <see cref="Dice"/> with the depth.</summary>
    SliceDice,
}
