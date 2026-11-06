namespace Meziantou.Framework.Charts;

/// <summary>A node of a tree placed by a <see cref="PackLayout"/>.</summary>
/// <typeparam name="T">The type of the data of the nodes.</typeparam>
/// <param name="Node">The node.</param>
/// <param name="X">The x coordinate of the center.</param>
/// <param name="Y">The y coordinate of the center.</param>
/// <param name="Radius">The radius.</param>
public sealed record HierarchyCircle<T>(HierarchyNode<T> Node, double X, double Y, double Radius);
