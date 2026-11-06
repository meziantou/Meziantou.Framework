namespace Meziantou.Framework.Charts;

/// <summary>How a <see cref="GroupTransform"/> combines the y values of a group.</summary>
public enum GroupReducer
{
    /// <summary>The sum of the values.</summary>
    Sum,

    /// <summary>The mean of the values.</summary>
    Mean,

    /// <summary>The number of rows, whether or not they have a y value.</summary>
    Count,

    /// <summary>The smallest value.</summary>
    Min,

    /// <summary>The largest value.</summary>
    Max,

    /// <summary>The median of the values.</summary>
    Median,
}
