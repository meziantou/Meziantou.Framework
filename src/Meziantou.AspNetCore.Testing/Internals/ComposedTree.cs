using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Walks the DOM through the open shadow roots, as the locators of Playwright do.</summary>
internal static class ComposedTree
{
    /// <summary>Adds the elements of the subtree of a node, in document order. The elements of an open shadow root come after its host, before the children of the host.</summary>
    public static void AddDescendants(Node node, List<Element> result)
    {
        if (node is Element { ShadowRoot: { } shadowRoot })
        {
            AddDescendants(shadowRoot, result);
        }

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element element)
            {
                result.Add(element);
                AddDescendants(element, result);
            }
        }
    }

    /// <summary>Gets the parent of a node, or the host of a shadow root.</summary>
    public static Node? GetParent(Node node) => node.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode;

    /// <summary>Gets whether a node is <paramref name="ancestor"/> or one of its descendants, through the shadow roots.</summary>
    public static bool IsInclusiveDescendant(Node node, Node ancestor)
    {
        for (Node? current = node; current is not null; current = GetParent(current))
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>Gets the children of an element whose text is part of the text of the element: the child elements, then the elements of its open shadow root.</summary>
    public static IEnumerable<Element> GetChildElements(Element element)
    {
        for (var child = element.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element childElement)
                yield return childElement;
        }

        if (element.ShadowRoot is { } shadowRoot)
        {
            for (var child = shadowRoot.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is Element childElement)
                    yield return childElement;
            }
        }
    }
}
