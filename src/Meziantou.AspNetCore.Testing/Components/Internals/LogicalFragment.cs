using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>A component or a markup block: its children are rendered in the DOM parent of the fragment.</summary>
internal sealed class LogicalFragment : LogicalContainer
{
    public override void CollectDomNodes(List<Node> nodes)
    {
        foreach (var child in Children)
        {
            child.CollectDomNodes(nodes);
        }
    }

    public override Node? GetFirstDomNode()
    {
        foreach (var child in Children)
        {
            if (child.GetFirstDomNode() is { } node)
                return node;
        }

        return null;
    }
}
