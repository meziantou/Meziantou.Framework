namespace Meziantou.AspNetCore.Testing.Components.Internals;

internal abstract class LogicalContainer : LogicalNode
{
    public List<LogicalNode> Children { get; } = [];

    /// <summary>
    /// Gets or sets an index from which no child has a DOM node (the children not rendered yet, such as the components
    /// that a parent inserted before rendering them). The search of the next DOM sibling stops there.
    /// </summary>
    public int EmptyChildrenStart { get; set; }
}
