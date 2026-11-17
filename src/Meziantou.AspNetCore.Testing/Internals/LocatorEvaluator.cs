using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Evaluates the steps of a locator, and caches the text and the labels of the elements during one evaluation.</summary>
internal sealed class LocatorEvaluator
{
    private readonly Dictionary<Element, string> _texts = [];
    private Dictionary<Element, List<HtmlLabelElement>>? _labels;
    private Dictionary<Element, int>? _documentOrder;

    public LocatorEvaluator(TestPage page) => Page = page;

    public TestPage Page { get; }

    public static List<Element> Evaluate(TestPage page, IReadOnlyList<LocatorStep> steps, SearchScope scope)
    {
        using (page.EnterEventLoop())
        {
            return new LocatorEvaluator(page).Evaluate(steps, scope);
        }
    }

    public List<Element> Evaluate(IReadOnlyList<LocatorStep> steps, SearchScope scope)
    {
        var initialScope = scope;
        List<Element> elements = [];
        foreach (var step in steps)
        {
            elements = step.Apply(this, initialScope, scope, elements);
            scope = new SearchScope(elements, IncludeNodes: false);
        }

        return elements;
    }

    /// <summary>Gets the elements of a scope, in document order.</summary>
    public static List<Element> GetCandidates(SearchScope scope)
    {
        var result = new List<Element>();
        var nodes = scope.Nodes;
        HashSet<Node>? set = nodes.Count > 1 ? [.. nodes] : null;
        foreach (var node in nodes)
        {
            // The descendants of a node of the scope that is inside another node of the scope are already added
            if (set is not null && IsInsideAnother(node, set))
                continue;

            if (scope.IncludeNodes && node is Element element)
            {
                result.Add(element);
            }

            ComposedTree.AddDescendants(node, result);
        }

        return result;

        static bool IsInsideAnother(Node node, HashSet<Node> set)
        {
            for (var parent = ComposedTree.GetParent(node); parent is not null; parent = ComposedTree.GetParent(parent))
            {
                if (set.Contains(parent))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the text of an element as Playwright does: the text of its descendants and of its shadow root, without the
    /// scripts and the styles; the value of an input button.
    /// </summary>
    public string GetText(Element element)
    {
        if (_texts.TryGetValue(element, out var text))
            return text;

        if (element is HtmlInputElement { Type: "button" or "submit" or "reset" } button)
        {
            text = button.Value;
        }
        else if (IsSkippedForText(element))
        {
            text = "";
        }
        else
        {
            var builder = new StringBuilder();
            AppendChildrenText(element, builder);
            if (element.ShadowRoot is { } shadowRoot)
            {
                AppendChildrenText(shadowRoot, builder);
            }

            text = builder.ToString();
        }

        _texts.Add(element, text);
        return text;

        void AppendChildrenText(Node parent, StringBuilder builder)
        {
            for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
            {
                switch (child)
                {
                    case Text textNode:
                        builder.Append(textNode.Data);
                        break;

                    case Element childElement:
                        builder.Append(GetText(childElement));
                        break;
                }
            }
        }
    }

    public static bool IsSkippedForText(Element element) => element.LocalName is "script" or "style" or "noscript" or "template";

    /// <summary>
    /// Gets the labels of an element as Playwright does: the elements referenced by <c>aria-labelledby</c>, else the
    /// <c>aria-label</c> attribute, else the <c>label</c> elements of a form control.
    /// </summary>
    public IEnumerable<string> GetLabels(Element element)
    {
        if (element.GetAttribute("aria-labelledby") is { } labelledBy)
        {
            var root = element.GetRootNode();
            var found = false;
            foreach (var id in labelledBy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var label = root switch
                {
                    Document document => document.GetElementById(id),
                    DocumentFragment fragment => fragment.GetElementById(id),
                    _ => null,
                };

                if (label is not null)
                {
                    found = true;
                    yield return GetText(label);
                }
            }

            if (found)
                yield break;
        }

        if (element.GetAttribute("aria-label") is { } ariaLabel && !string.IsNullOrWhiteSpace(ariaLabel))
        {
            yield return ariaLabel;
            yield break;
        }

        if (element is HtmlButtonElement or HtmlMeterElement or HtmlOutputElement or HtmlProgressElement or HtmlSelectElement or HtmlTextAreaElement or HtmlInputElement { Type: not "hidden" })
        {
            _labels ??= GetLabelsByControl(Page.Document);
            if (_labels.TryGetValue(element, out var labels))
            {
                foreach (var label in labels)
                    yield return GetText(label);
            }
        }

        static Dictionary<Element, List<HtmlLabelElement>> GetLabelsByControl(Document document)
        {
            var elements = new List<Element>();
            ComposedTree.AddDescendants(document, elements);
            var result = new Dictionary<Element, List<HtmlLabelElement>>();
            foreach (var element in elements)
            {
                if (element is HtmlLabelElement { Control: { } control } label)
                {
                    if (!result.TryGetValue(control, out var list))
                    {
                        list = [];
                        result.Add(control, list);
                    }

                    list.Add(label);
                }
            }

            return result;
        }
    }

    /// <summary>Sorts elements in document order, and removes the duplicates.</summary>
    public List<Element> SortInDocumentOrder(IEnumerable<Element> elements)
    {
        if (_documentOrder is null)
        {
            var all = new List<Element>();
            ComposedTree.AddDescendants(Page.Document, all);
            _documentOrder = new Dictionary<Element, int>(all.Count);
            for (var i = 0; i < all.Count; i++)
            {
                _documentOrder[all[i]] = i;
            }
        }

        var order = _documentOrder;
        return [.. elements.Distinct().OrderBy(element => order.TryGetValue(element, out var index) ? index : int.MaxValue)];
    }
}
