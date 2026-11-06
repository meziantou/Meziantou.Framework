namespace Meziantou.Framework.Charts;

/// <summary>The layout of a <see cref="TreeMark{T}"/>.</summary>
public enum TreeLayoutKind
{
    /// <summary>A tidy tree (<see cref="TreeLayout"/>): the nodes of a depth are on a line.</summary>
    Tidy,

    /// <summary>A dendrogram (<see cref="ClusterLayout"/>): the leaves are all at the deepest level.</summary>
    Cluster,
}
