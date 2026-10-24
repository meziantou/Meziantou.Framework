using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The behavior of the details element (https://html.spec.whatwg.org/multipage/interactive-elements.html#the-details-element).</summary>
public sealed partial class HtmlDetailsElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#details-toggle-task-tracker</summary>
    private ToggleTaskTracker? _toggleTaskTracker;

    /// <summary>The user agent shadow tree that holds <see cref="ImplicitSummary"/>, created on first use while the element is connected.</summary>
    private ShadowRoot? _userAgentShadowRoot;

    /// <summary>
    /// The summary that the user agent provides when the element has no summary child, or null when it has one or is not
    /// connected (https://html.spec.whatwg.org/multipage/rendering.html#the-details-and-summary-elements: "If the element has no
    /// child summary element, then the user agent should provide its own legend (e.g. "Details")"). As in the browsers, it is a
    /// summary element with the text "Details", the child of a closed user agent shadow root whose host is the details element:
    /// it is rendered before the other children (the <c>:host(details) &gt; summary</c> rules of the user agent give it the style
    /// of the summary of a details element), it is focusable, and its activation toggles the details element; the events and the
    /// focus are retargeted to the details element. The children of the details element are not slotted in this shadow tree:
    /// the box tree builder renders them in the box of the <c>::details-content</c> pseudo-element.
    /// </summary>
    internal HtmlElement? ImplicitSummary
    {
        get
        {
            if (!IsConnected || HasSummaryChild())
                return null;

            if (_userAgentShadowRoot is null)
            {
                var document = NodeDocument;
                var root = new ShadowRoot(document, this, ShadowRootMode.Closed);
                var summary = ElementFactory.CreateElement(document, "summary", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false, registry: null);
                summary.AppendChild(document.CreateTextNode("Details"));
                root.AppendChild(summary);

                // The events dispatched in the shadow tree are retargeted (see EventDispatcher)
                document.HasUserAgentShadowRoots = true;
                root.SetConnected(true);
                _userAgentShadowRoot = root;
            }

            return (HtmlElement)_userAgentShadowRoot.FirstChild!;
        }
    }

    /// <summary>Whether the element is the summary that the user agent provides for this element (see <see cref="ImplicitSummary"/>), the element having no summary child.</summary>
    internal bool IsImplicitSummary(Element element) => _userAgentShadowRoot is { } root && ReferenceEquals(root.FirstChild, element) && IsConnected && !HasSummaryChild();

    /// <summary>The summary of the user agent shadow tree when it was created (see <see cref="ImplicitSummary"/>), without creating it: its style is invalidated with the style of the element.</summary>
    internal Element? CreatedImplicitSummary => _userAgentShadowRoot?.FirstChild as Element;

    private bool HasSummaryChild()
    {
        for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(TagId.Summary))
                return true;
        }

        return false;
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);

        // A new shadow tree is created when the element is connected again (possibly in another document)
        _userAgentShadowRoot?.SetConnected(false);
        _userAgentShadowRoot = null;
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null)
            return;

        if (localName == "name")
        {
            EnsureExclusivityByClosingThisElementIfNeeded();
        }
        else if (localName == "open")
        {
            if (oldValue is null != value is null)
            {
                _toggleTaskTracker = ToggleTaskTracker.Queue(this, _toggleTaskTracker, tracker => _toggleTaskTracker = tracker, oldValue is null ? "closed" : "open", oldValue is null ? "open" : "closed", source: null);
            }

            if (oldValue is null && value is not null)
            {
                EnsureExclusivityByClosingOtherElementsIfNeeded();
            }
        }
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        EnsureExclusivityByClosingThisElementIfNeeded();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#ensure-details-exclusivity-by-closing-other-elements-if-needed</summary>
    private void EnsureExclusivityByClosingOtherElementsIfNeeded()
    {
        if (!HasAttributeWithLocalName("open"))
            return;

        var other = FindOpenElementInNameGroup();
        other?.RemoveAttribute("open");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#ensure-details-exclusivity-by-closing-the-given-element-if-needed</summary>
    private void EnsureExclusivityByClosingThisElementIfNeeded()
    {
        if (!HasAttributeWithLocalName("open"))
            return;

        if (FindOpenElementInNameGroup() is not null)
        {
            RemoveAttribute("open");
        }
    }

    /// <summary>The first open element of the details name group of this element, excluding it (https://html.spec.whatwg.org/multipage/interactive-elements.html#details-name-group).</summary>
    private HtmlDetailsElement? FindOpenElementInNameGroup()
    {
        var name = GetAttributeValue("name");
        if (string.IsNullOrEmpty(name))
            return null;

        var root = GetRoot();
        for (var node = root is Element rootElement ? rootElement : root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlDetailsElement details && !ReferenceEquals(details, this) && details.GetAttributeValue("name") == name && details.HasAttributeWithLocalName("open"))
                return details;
        }

        return null;
    }
}

public partial class HtmlElement
{
    internal override bool HasActivationBehavior => IsHtml(TagId.Summary) || base.HasActivationBehavior;

    internal override void ActivationBehavior(Event @event)
    {
        if (IsHtml(TagId.Summary))
        {
            SummaryActivationBehavior(@event);
            return;
        }

        base.ActivationBehavior(@event);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interactive-elements.html#the-summary-element:activation-behaviour. Like the
    /// browsers (and the web-platform-tests), a click on interactive content in the summary (e.g. a textarea, a video with
    /// controls, an SVG link) does not toggle the details element.
    /// </summary>
    private void SummaryActivationBehavior(Event @event)
    {
        if (!Reflection.IsSummaryForItsParentDetails(this))
            return;

        for (var node = @event.Target as Node; node is not null && !ReferenceEquals(node, this); node = node.ParentNode)
        {
            if (node is HtmlElement element ? HtmlLabelElement.IsInteractiveContent(element) : node is SvgAElement svgLink && (svgLink.HasAttribute("href") || svgLink.HasAttribute("xlink:href")))
                return;
        }

        var parent = Reflection.GetDetailsOfSummary(this)!;
        if (parent.HasAttributeWithLocalName("open"))
        {
            parent.RemoveAttribute("open");
        }
        else
        {
            parent.SetAttribute("open", "");
        }
    }
}
