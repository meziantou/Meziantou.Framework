using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The behavior of the details element (https://html.spec.whatwg.org/multipage/interactive-elements.html#the-details-element).</summary>
public sealed partial class HtmlDetailsElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#details-toggle-task-tracker</summary>
    private ToggleTaskTracker? _toggleTaskTracker;

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

        var parent = (Element)ParentNode!;
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
