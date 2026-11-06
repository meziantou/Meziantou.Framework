namespace Meziantou.Framework.Charts;

/// <summary>Where the stacks of a <see cref="StackTransform"/> start.</summary>
public enum StackOffset
{
    /// <summary>Each value starts where the previous one ends, from zero.</summary>
    None,

    /// <summary>The positive values are stacked up from zero and the negative values down from zero.</summary>
    Diverging,

    /// <summary>The stacks are normalized to go from 0 to 1: a 100% stacked chart.</summary>
    Expand,

    /// <summary>The stacks are centered on zero: a streamgraph.</summary>
    Silhouette,
}
