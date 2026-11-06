namespace Meziantou.Framework.Charts;

/// <summary>A node of a tree placed by a <see cref="TreeLayout"/> or a <see cref="ClusterLayout"/>.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
/// <param name="Node">The node.</param>
/// <param name="X">The position across the depths, from 0 to the width of the layout.</param>
/// <param name="Y">The position along the depth, from 0 (the root) to the height of the layout (the deepest nodes).</param>
public sealed record HierarchyPoint<T>(HierarchyNode<T> Node, double X, double Y);
