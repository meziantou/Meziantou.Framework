using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>An element, whose logical children are rendered as the DOM children of the element.</summary>
internal sealed class LogicalElement(Element element) : LogicalContainer
{
    public Element Element { get; } = element;

    public override void CollectDomNodes(List<Node> nodes) => nodes.Add(Element);

    public override Node? GetFirstDomNode() => Element;
}
