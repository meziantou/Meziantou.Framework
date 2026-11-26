using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>A component or a markup block: its children are rendered in the DOM parent of the fragment.</summary>
internal sealed class LogicalFragment : LogicalContainer
{
    private int _domNodeCount;

    public override int DomNodeCount => _domNodeCount;

    public void AddDomNodeCount(int count) => _domNodeCount += count;

    public override void CollectDomNodes(List<Node> nodes)
    {
        foreach (var child in Children)
        {
            child.CollectDomNodes(nodes);
        }
    }

    public override Node? GetFirstDomNode()
    {
        if (_domNodeCount == 0)
            return null;

        foreach (var child in Children)
        {
            if (child.GetFirstDomNode() is { } node)
                return node;
        }

        return null;
    }
}
