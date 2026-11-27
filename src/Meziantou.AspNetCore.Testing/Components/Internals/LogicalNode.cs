using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// A node of the logical tree of the render tree. The sibling indexes of the edits of a render batch are indexes in the
/// logical tree: a component and a markup block are one logical child, whatever the number of DOM nodes they render.
/// </summary>
internal abstract class LogicalNode
{
    public LogicalContainer? Parent { get; set; }

    /// <summary>
    /// Gets or sets the index of the node in the children of its parent, when it was inserted or last found. Inserting or
    /// removing a previous sibling makes it stale: check it before using it.
    /// </summary>
    public int IndexHint { get; set; }

    /// <summary>Gets the number of DOM nodes of the node: 1 for an element or a DOM node, the DOM nodes of its children for a fragment.</summary>
    public abstract int DomNodeCount { get; }

    public abstract void CollectDomNodes(List<Node> nodes);

    public abstract Node? GetFirstDomNode();
}
