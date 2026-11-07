using Meziantou.Framework.WebPlatform.Css.Selectors;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Selector queries on a sequence of nodes, such as a <see cref="NodeList"/> or an <see cref="HtmlCollection"/>.</summary>
/// <remarks>
/// Each method searches the descendants of each node of the sequence, as <see cref="Element.QuerySelectorAll(string)"/>
/// does: the nodes of the sequence are not part of the result, and <c>:scope</c> is the node being searched. Nodes that
/// cannot have element children (text, comments…) are skipped. The results follow the order of the sequence, then the
/// tree order, and an element found from several nodes of the sequence is returned once.
/// </remarks>
public static class NodeEnumerableExtensions
{
    /// <summary>Returns the first descendant of the nodes that matches the selector.</summary>
    public static Element? QuerySelector(this IEnumerable<Node> nodes, string selectors) => QuerySelector<Element>(nodes, ParseSelectors(selectors));

    /// <summary>Returns the first descendant of the nodes that matches the parsed selector.</summary>
    public static Element? QuerySelector(this IEnumerable<Node> nodes, SelectorList selectors) => QuerySelector<Element>(nodes, selectors);

    /// <summary>Returns the first descendant of the nodes that is a <typeparamref name="T"/> and matches the selector.</summary>
    public static T? QuerySelector<T>(this IEnumerable<Node> nodes, string selectors) where T : Element => QuerySelector<T>(nodes, ParseSelectors(selectors));

    /// <summary>Returns the first descendant of the nodes that is a <typeparamref name="T"/> and matches the parsed selector.</summary>
    public static T? QuerySelector<T>(this IEnumerable<Node> nodes, SelectorList selectors) where T : Element
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(selectors);

        foreach (var node in nodes)
        {
            var result = node switch
            {
                Element element => element.QuerySelector<T>(selectors),
                Document document => document.QuerySelector<T>(selectors),
                DocumentFragment fragment => fragment.QuerySelector<T>(selectors),
                _ => null,
            };

            if (result is not null)
                return result;
        }

        return null;
    }

    /// <summary>Returns the descendants of the nodes that match the selector.</summary>
    public static IReadOnlyList<Element> QuerySelectorAll(this IEnumerable<Node> nodes, string selectors) => QuerySelectorAll<Element>(nodes, ParseSelectors(selectors));

    /// <summary>Returns the descendants of the nodes that match the parsed selector.</summary>
    public static IReadOnlyList<Element> QuerySelectorAll(this IEnumerable<Node> nodes, SelectorList selectors) => QuerySelectorAll<Element>(nodes, selectors);

    /// <summary>Returns the descendants of the nodes that are a <typeparamref name="T"/> and match the selector.</summary>
    public static IReadOnlyList<T> QuerySelectorAll<T>(this IEnumerable<Node> nodes, string selectors) where T : Element => QuerySelectorAll<T>(nodes, ParseSelectors(selectors));

    /// <summary>Returns the descendants of the nodes that are a <typeparamref name="T"/> and match the parsed selector.</summary>
    public static IReadOnlyList<T> QuerySelectorAll<T>(this IEnumerable<Node> nodes, SelectorList selectors) where T : Element
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(selectors);

        var result = new List<T>();
        var seen = new HashSet<Element>();
        foreach (var node in nodes)
        {
            if (node is Element or Document or DocumentFragment)
            {
                node.AddQuerySelectorAllResults(selectors, result, seen);
            }
        }

        return result;
    }

    private static SelectorList ParseSelectors(string selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        return SelectorCache.Parse(selectors);
    }
}
