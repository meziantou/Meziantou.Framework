namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The side of a path a text is drawn on.</summary>
public enum TextPathSide
{
    /// <summary>
    /// The text follows the path in its direction and stands on its left: above a path that goes from left to right.
    /// </summary>
    Left = 0,

    /// <summary>
    /// The text follows the path from its end to its start and stands on the other side: under a path that goes from left
    /// to right, upside down.
    /// </summary>
    Right = 1,
}
