namespace Meziantou.Framework.Charts;

/// <summary>The order of the series of a <see cref="StackTransform"/>, from the bottom of the stacks to their top.</summary>
public enum StackOrder
{
    /// <summary>The order the series first appear in the data.</summary>
    Given,

    /// <summary>The series with the smallest sum at the bottom.</summary>
    Ascending,

    /// <summary>The series with the largest sum at the bottom.</summary>
    Descending,

    /// <summary>The series that peak earliest in the middle, the others alternately above and below them, as d3's <c>stackOrderInsideOut</c>: the order of streamgraphs.</summary>
    InsideOut,
}
