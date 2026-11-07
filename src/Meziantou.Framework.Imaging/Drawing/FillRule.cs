namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The rule deciding which points are inside a path whose contours overlap or intersect themselves.</summary>
public enum FillRule
{
    /// <summary>A point is inside when its winding number is not zero. This is the default.</summary>
    NonZero = 0,

    /// <summary>A point is inside when its winding number is odd: overlapping contours make holes.</summary>
    EvenOdd = 1,
}
