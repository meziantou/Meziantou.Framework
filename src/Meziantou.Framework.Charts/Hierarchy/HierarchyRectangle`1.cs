namespace Meziantou.Framework.Charts;

/// <summary>A node of a tree placed by a <see cref="TreemapLayout"/> or a <see cref="PartitionLayout"/>.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
/// <param name="Node">The node.</param>
/// <param name="X0">The left edge.</param>
/// <param name="Y0">The top edge.</param>
/// <param name="X1">The right edge.</param>
/// <param name="Y1">The bottom edge.</param>
public sealed record HierarchyRectangle<T>(HierarchyNode<T> Node, double X0, double Y0, double X1, double Y1)
{
    /// <summary>Gets the width.</summary>
    public double Width => X1 - X0;

    /// <summary>Gets the height.</summary>
    public double Height => Y1 - Y0;
}
