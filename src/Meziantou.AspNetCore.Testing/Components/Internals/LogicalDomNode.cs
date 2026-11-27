using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>A DOM node that is not a container: a text node, or a node of a markup block.</summary>
internal sealed class LogicalDomNode(Node node) : LogicalNode
{
    public Node Node { get; } = node;

    public override int DomNodeCount => 1;

    public override void CollectDomNodes(List<Node> nodes) => nodes.Add(Node);

    public override Node? GetFirstDomNode() => Node;
}
