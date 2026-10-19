using System.Collections.Immutable;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>
/// Finds the descendants of a node that match a selector, in tree order (querySelector() and querySelectorAll()): the subject
/// key of each selector (see <see cref="SelectorHints"/>) rejects most elements before the selector is matched, and an
/// ancestor filter (see <see cref="SelectorAncestorFilter"/>) rejects the selectors whose ancestors cannot match.
/// </summary>
internal static class SelectorQuery
{
    [ThreadStatic]
    private static SelectorAncestorFilter? s_cachedFilter;

    /// <summary>
    /// Adds the descendants of <paramref name="root"/> that are a <typeparamref name="T"/> and match the selector to
    /// <paramref name="result"/> (skipping the elements already in <paramref name="seen"/>), or returns the first one when
    /// <paramref name="result"/> is null.
    /// </summary>
    public static T? Find<T>(Node root, SelectorList selector, in SelectorContext context, List<T>? result, HashSet<Element>? seen) where T : Element
    {
        var document = root.NodeDocument;
        var isHtmlDocument = document.IsHtmlDocument;

        // The IDs and the classes are compared ASCII case-insensitively in quirks mode, and the classes of the selectors are
        // the animated value of the class attribute: the subject keys then only check the local names
        var checkIdAndClass = document.Mode != DocumentMode.Quirks && document.SmilOrNull is null;
        var selectors = selector.Selectors;
        if (checkIdAndClass && selectors is [{ Hints: { IsSimpleCompound: true } hints } single])
            return FindSimpleCompound(root, single.Compounds[0], hints, isHtmlDocument, result, seen);

        var useFilter = false;
        foreach (var complex in selectors)
        {
            useFilter |= complex.Hints.AncestorHashes.Length > 0;
        }

        if (!useFilter)
        {
            for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is T element && Matches(selectors, element, context, isHtmlDocument, checkIdAndClass, filter: null) && Add(element, result, seen))
                    return element;
            }

            return null;
        }

        var filter = s_cachedFilter ?? new SelectorAncestorFilter();
        s_cachedFilter = null;
        filter.PushInclusiveAncestors(root as Element ?? (root as ShadowRoot)?.Host);
        var found = FindWithFilter(root, selectors, context, isHtmlDocument, checkIdAndClass, filter, result, seen);
        filter.Clear();
        s_cachedFilter = filter;
        return found;
    }

    /// <summary>Walks the tree in tree order, with the features of the ancestors of the current node in the filter.</summary>
    private static T? FindWithFilter<T>(Node root, ImmutableArray<ComplexSelector> selectors, in SelectorContext context, bool isHtmlDocument, bool checkIdAndClass, SelectorAncestorFilter filter, List<T>? result, HashSet<Element>? seen) where T : Element
    {
        var node = root.FirstChild;
        while (node is not null)
        {
            // (only the elements have children in a tree)
            if (node is Element element)
            {
                if (element is T candidate && Matches(selectors, candidate, context, isHtmlDocument, checkIdAndClass, filter) && Add(candidate, result, seen))
                    return candidate;

                if (element.FirstChild is { } firstChild)
                {
                    filter.Push(element);
                    node = firstChild;
                    continue;
                }
            }

            Node? next = null;
            for (var current = node; ; current = current.ParentNode!)
            {
                if (current.NextSibling is { } sibling)
                {
                    next = sibling;
                    break;
                }

                if (ReferenceEquals(current.ParentNode, root) || current.ParentNode is null)
                    break;

                filter.Pop();
            }

            node = next;
        }

        return null;
    }

    private static T? FindSimpleCompound<T>(Node root, CompoundSelector compound, SelectorHints hints, bool isHtmlDocument, List<T>? result, HashSet<Element>? seen) where T : Element
    {
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is T element && !hints.Rejects(element, isHtmlDocument, checkIdAndClass: true) && SelectorMatcher.MatchesSimpleCompound(compound, element, isHtmlDocument) && Add(element, result, seen))
                return element;
        }

        return null;
    }

    private static bool Matches(ImmutableArray<ComplexSelector> selectors, Element element, in SelectorContext context, bool isHtmlDocument, bool checkIdAndClass, SelectorAncestorFilter? filter)
    {
        foreach (var complex in selectors)
        {
            var hints = complex.Hints;
            if (hints.NeverMatches || hints.Rejects(element, isHtmlDocument, checkIdAndClass) || (filter is not null && !filter.MayMatch(hints)))
                continue;

            if (SelectorMatcher.MatchesComplex(complex, element, context))
                return true;
        }

        return false;
    }

    /// <summary>Adds a matching element to the result, and returns whether it is the result of the query (the result is the first element).</summary>
    private static bool Add<T>(T element, List<T>? result, HashSet<Element>? seen) where T : Element
    {
        if (result is null)
            return true;

        if (seen is null || seen.Add(element))
        {
            result.Add(element);
        }

        return false;
    }
}
