namespace Meziantou.Framework.Charts;

/// <summary>Which regions of a path with crossing or nested contours are inside.</summary>
public enum SceneFillRule
{
    /// <summary>A point is inside when the contours around it do not cancel out.</summary>
    NonZero,

    /// <summary>A point is inside when it is surrounded by an odd number of contours.</summary>
    EvenOdd,
}
