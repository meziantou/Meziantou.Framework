using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>
/// Finds the elements that match a CSS selector, as <c>querySelectorAll</c> of each node of the scope: <c>:scope</c> is
/// the node that is searched (the document for the roots of a page). The open shadow roots are searched too.
/// </summary>
internal sealed class CssStep(string selector) : LocatorStep
{
    private readonly SelectorList _selector = SelectorList.Parse(selector);

    public override List<Element> Apply(LocatorEvaluator evaluator, SearchScope initialScope, SearchScope scope, List<Element> elements)
    {
        // The candidates are in document order, through the shadow roots
        var candidates = LocatorEvaluator.GetCandidates(scope);
        var matches = new HashSet<Element>();
        foreach (var node in scope.Nodes)
        {
            // The root of a page is searched from its document, so the locator finds what document.querySelectorAll finds
            if (scope.IncludeNodes && node is Element { ParentNode: Document document })
            {
                AddMatches(document, matches);
            }
            else
            {
                if (scope.IncludeNodes && node is Element element && element.Matches(_selector))
                {
                    matches.Add(element);
                }

                AddMatches(node, matches);
            }

            // The selector is matched in each shadow root, whose :scope is the shadow root
            if (node is Element { ShadowRoot: { } shadowRoot })
            {
                AddMatches(shadowRoot, matches);
            }
        }

        // The candidates are the descendants of the nodes of the scope, so they hold the other shadow roots to search
        foreach (var candidate in candidates)
        {
            if (candidate.ShadowRoot is { } shadowRoot)
            {
                AddMatches(shadowRoot, matches);
            }
        }

        if (matches.Count == 0)
            return [];

        return [.. candidates.Where(matches.Contains)];
    }

    private void AddMatches(Node node, HashSet<Element> matches)
    {
        var result = node switch
        {
            Element element => element.QuerySelectorAll<Element>(_selector),
            Document document => document.QuerySelectorAll<Element>(_selector),
            DocumentFragment fragment => fragment.QuerySelectorAll<Element>(_selector),
            _ => [],
        };

        matches.UnionWith(result);
    }

    public override string ToString() => "Locator(" + TextMatcher.Quote(selector) + ")";
}
