using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// A node of the logical tree of the render tree. The sibling indexes of the edits of a render batch are indexes in the
/// logical tree: a component and a markup block are one logical child, whatever the number of DOM nodes they render.
/// </summary>
internal abstract class LogicalNode
{
    public LogicalContainer? Parent { get; set; }

    public abstract void CollectDomNodes(List<Node> nodes);

    public abstract Node? GetFirstDomNode();
}
