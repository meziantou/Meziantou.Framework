using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Evaluates the steps of a locator, and caches the text and the labels of the elements during one evaluation.</summary>
internal sealed class LocatorEvaluator
{
    private readonly Dictionary<Element, string> _texts = [];
    private readonly Dictionary<Element, (int Start, int Length)> _textRanges = [];
    private char[] _textBuffer = [];
    private int _textLength;
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

        // One text child (<span>Name</span>): its text is the text of the element, without a copy
        text = element is not HtmlInputElement && !IsSkippedForText(element) && element.ShadowRoot is null && element.FirstChild is Text { NextSibling: null } textNode
            ? textNode.Data
            : GetTextSpan(element).ToString();

        _texts.Add(element, text);
        return text;
    }

    /// <summary>
    /// Gets the text of an element (see <see cref="GetText"/>) without creating a string. The texts of an element and of its
    /// descendants are appended once to a buffer: the text of an element is a range of the buffer, as the text of a parent is
    /// the concatenation of the texts of its children. The span is valid until the next call.
    /// </summary>
    public ReadOnlySpan<char> GetTextSpan(Element element)
    {
        if (!_textRanges.TryGetValue(element, out var range))
        {
            AppendText(element);
            range = _textRanges[element];
        }

        return _textBuffer.AsSpan(range.Start, range.Length);
    }

    private void AppendText(Element element)
    {
        var start = _textLength;
        if (element is HtmlInputElement { Type: "button" or "submit" or "reset" } button)
        {
            AppendToTextBuffer(button.Value);
        }
        else if (!IsSkippedForText(element))
        {
            AppendChildrenText(element);
            if (element.ShadowRoot is { } shadowRoot)
            {
                AppendChildrenText(shadowRoot);
            }
        }

        _textRanges[element] = (start, _textLength - start);
    }

    private void AppendChildrenText(Node parent)
    {
        for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case Text textNode:
                    AppendToTextBuffer(textNode.Data);
                    break;

                case Element childElement:
                    AppendText(childElement);
                    break;
            }
        }
    }

    private void AppendToTextBuffer(string value)
    {
        if (_textBuffer.Length - _textLength < value.Length)
        {
            Array.Resize(ref _textBuffer, Math.Max(_textBuffer.Length * 2, Math.Max(_textLength + value.Length, 256)));
        }

        value.CopyTo(_textBuffer.AsSpan(_textLength));
        _textLength += value.Length;
    }

    public static bool IsSkippedForText(Element element) => element.LocalName is "script" or "style" or "noscript" or "template";

    /// <summary>
    /// Gets the labels of an element as Playwright does: the elements referenced by <c>aria-labelledby</c>, else the
    /// <c>aria-label</c> attribute, else the <c>label</c> elements of a form control.
    /// </summary>
    public IEnumerable<string> GetLabels(Element element)
    {
        // Most elements have no label: they are rejected without creating an enumerator
        if (!element.HasAttribute("aria-labelledby") && !element.HasAttribute("aria-label") && !IsLabelableControl(element))
            return [];

        return GetLabelsCore(element);
    }

    private static bool IsLabelableControl(Element element) => element is HtmlButtonElement or HtmlMeterElement or HtmlOutputElement or HtmlProgressElement or HtmlSelectElement or HtmlTextAreaElement or HtmlInputElement { Type: not "hidden" };

    private IEnumerable<string> GetLabelsCore(Element element)
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

        if (IsLabelableControl(element))
        {
            _labels ??= GetLabelsByControl(Page.Document);
            if (_labels.TryGetValue(element, out var labels))
            {
                foreach (var label in labels)
                    yield return GetLabelText(label, element);
            }
        }

        static Dictionary<Element, List<HtmlLabelElement>> GetLabelsByControl(Document document)
        {
            var elements = new List<Element>();
            ComposedTree.AddDescendants(document, elements);
            var result = new Dictionary<Element, List<HtmlLabelElement>>();
            foreach (var element in elements)
            {
                if (element is HtmlLabelElement label && GetControl(label) is { } control)
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

        // HtmlLabelElement.Control searches the tree of the label for the element of its "for" attribute: the identifiers of
        // the tree are indexed instead (GetElementById), so finding the controls of all the labels is not quadratic
        static HtmlElement? GetControl(HtmlLabelElement label)
        {
            if (label.HtmlFor is not { Length: > 0 } id)
                return label.Control;

            Element? element;
            switch (label.GetRootNode())
            {
                case Document document:
                    element = document.GetElementById(id);
                    break;

                case DocumentFragment fragment:
                    element = fragment.GetElementById(id);
                    break;

                default:
                    return label.Control;
            }

            return element switch
            {
                null => null,
                HtmlInputElement input => input.Type == "hidden" ? null : input,
                HtmlButtonElement or HtmlMeterElement or HtmlOutputElement or HtmlProgressElement or HtmlSelectElement or HtmlTextAreaElement => (HtmlElement)element,

                // Whether the other elements are labelable is known to WebPlatform only (form-associated custom elements)
                _ => label.Control,
            };
        }
    }

    /// <summary>
    /// Gets the text of the <c>label</c> element of a control as the accessible name computation does
    /// (https://w3c.github.io/accname/#comp_embedded_control): the labeled control is not part of its label, and the other
    /// controls of the label contribute their value instead of their content (e.g. the options of a <c>select</c>).
    /// </summary>
    private string GetLabelText(HtmlLabelElement label, Element control)
    {
        var builder = new StringBuilder();
        AppendChildrenText(label, builder);
        return builder.ToString();

        void AppendChildrenText(Node parent, StringBuilder builder)
        {
            for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
            {
                switch (child)
                {
                    case Text textNode:
                        builder.Append(textNode.Data);
                        break;

                    case Element childElement when ReferenceEquals(childElement, control) || IsSkippedForText(childElement):
                        break;

                    case Element childElement when GetEmbeddedControlValue(childElement) is { } value:
                        builder.Append(value);
                        break;

                    case Element childElement:
                        AppendChildrenText(childElement, builder);
                        if (childElement.ShadowRoot is { } shadowRoot)
                        {
                            AppendChildrenText(shadowRoot, builder);
                        }

                        break;
                }
            }
        }

        string? GetEmbeddedControlValue(Element element) => element switch
        {
            HtmlInputElement { Type: "button" or "submit" or "reset" } => GetText(element),
            HtmlInputElement { Type: "number" or "range" } input => input.GetAttribute("aria-valuetext") ?? input.GetAttribute("aria-valuenow") ?? input.Value,
            HtmlInputElement { Type: "text" or "search" or "email" or "tel" or "url" } input => input.Value,
            HtmlTextAreaElement textArea => textArea.Value,
            HtmlSelectElement select => string.Join(' ', select.SelectedOptions.OfType<HtmlOptionElement>().Select(option => option.Text)),
            _ => null,
        };
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
