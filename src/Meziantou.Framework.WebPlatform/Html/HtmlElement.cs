using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://html.spec.whatwg.org/#htmlelement
public partial class HtmlElement : Element
{
    internal HtmlElement(Document document, string localName, string? prefix)
        : base(document, localName, Namespaces.Html, prefix)
    {
    }

    /// <summary>Runs the reset algorithm of resettable elements when created by the parser.</summary>
    internal virtual void ResetFromParser()
    {
    }

    private partial int ComputeTabIndex() => Reflection.GetTabIndex(this);

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-hidden</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-hidden"/>
    [IdlMember("hidden", "https://html.spec.whatwg.org/multipage/interaction.html#dom-hidden")]
    public BooleanOrDoubleOrString? Hidden
    {
        get
        {
            var table = EnumeratedAttributes.Hidden;
            var value = GetAttributeValue("hidden");
            if (value is not null && table.GetState(value) == table.Keywords["until-found"])
                return "until-found";

            return value is not null;
        }
        set
        {
            var remove = value?.Value switch
            {
                null => true,
                bool b => !b,
                double d => d == 0 || double.IsNaN(d),
                string s => s.Length == 0,
                _ => false,
            };

            if (value?.Value is string text && InfraAscii.EqualsIgnoreAsciiCase(text, "until-found"))
            {
                SetAttributeValue("hidden", "until-found");
            }
            else if (remove)
            {
                RemoveAttributeByLocalName("hidden");
            }
            else
            {
                SetAttributeValue("hidden", "");
            }
        }
    }

    /// <summary>Whether the hidden attribute is in the Hidden Until Found state.</summary>
    private bool IsHiddenUntilFound => GetAttributeValue("hidden") is { } value && EnumeratedAttributes.Hidden.GetState(value) == EnumeratedAttributes.Hidden.Keywords["until-found"];

    /// <summary>
    /// The ancestor revealing algorithm (https://html.spec.whatwg.org/multipage/interaction.html#ancestor-revealing-algorithm):
    /// fires beforematch at the hidden=until-found inclusive ancestors of <paramref name="target"/> in the flat tree and
    /// removes their hidden attribute, and opens the closed details elements that contain it. The details elements have
    /// no shadow tree: a child of a details element other than its summary is in its second slot.
    /// </summary>
    internal static void RunAncestorRevealingAlgorithm(Node target)
    {
        var ancestorsToReveal = new List<(Element Element, bool IsDetails)>();
        for (var ancestor = target; GetFlatTreeParent(ancestor) is { } parent; ancestor = parent)
        {
            if (ancestor is HtmlElement { IsHiddenUntilFound: true } hiddenElement)
            {
                ancestorsToReveal.Add((hiddenElement, false));
            }

            if (parent is HtmlDetailsElement details && !details.HasAttributeWithLocalName("open") && !(ancestor is Element summary && summary.IsHtml(TagId.Summary) && Reflection.IsSummaryForItsParentDetails(summary)))
            {
                ancestorsToReveal.Add((details, true));
            }
        }

        foreach (var (element, isDetails) in ancestorsToReveal)
        {
            if (!element.IsConnected)
                return;

            if (isDetails)
            {
                if (element.HasAttributeWithLocalName("open"))
                    return;

                element.SetAttributeValue("open", "");
            }
            else
            {
                var hiddenElement = (HtmlElement)element;
                if (!hiddenElement.IsHiddenUntilFound)
                    return;

                hiddenElement.FireEvent(new Event("beforematch", new EventInit { Bubbles = true }));
                if (!hiddenElement.IsConnected || !hiddenElement.IsHiddenUntilFound)
                    return;

                hiddenElement.RemoveAttributeByLocalName("hidden");
            }
        }
    }

    private bool _clickInProgress;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-click</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-click"/>
    [IdlMember("click", "https://html.spec.whatwg.org/multipage/interaction.html#dom-click")]
    public void Click()
    {
        // A form control that is disabled: a fieldset is not a form control (a disabled fieldset does not block the clicks, as
        // in the browsers)
        if (Css.Selectors.HtmlElementStates.IsEnabledCandidate(this) && LocalName is not ("optgroup" or "option" or "fieldset") && Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
            return;

        if (_clickInProgress)
            return;

        _clickInProgress = true;
        try
        {
            FireSyntheticPointerEvent("click", notTrusted: true);
        }
        finally
        {
            _clickInProgress = false;
        }
    }

    /// <summary>
    /// Runs the synthetic click activation steps for a click of the user forwarded to the element (e.g. by its label): as in
    /// browsers, the click event is trusted and has the pointer properties and the modifiers of the click of the user.
    /// </summary>
    internal void ForwardTrustedClick(MouseEvent source)
    {
        if (Css.Selectors.HtmlElementStates.IsEnabledCandidate(this) && LocalName is not ("optgroup" or "option") && Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
            return;

        if (_clickInProgress)
            return;

        _clickInProgress = true;
        try
        {
            var pointer = source as PointerEvent;
            var click = new PointerEvent("click", new PointerEventInit
            {
                Bubbles = true,
                Cancelable = true,
                Composed = true,
                View = NodeDocument.DefaultView,
                Detail = source.Detail,
                ScreenX = source.ScreenX,
                ScreenY = source.ScreenY,
                ClientX = source.ClientX,
                ClientY = source.ClientY,
                Button = source.Button,
                Buttons = source.Buttons,
                CtrlKey = source.CtrlKey,
                ShiftKey = source.ShiftKey,
                AltKey = source.AltKey,
                MetaKey = source.MetaKey,
                PointerId = pointer?.PointerId ?? -1,
                PointerType = pointer?.PointerType ?? "",
                IsPrimary = pointer?.IsPrimary ?? false,
            })
            {
                IsTrusted = true,
            };
            EventDispatcher.Dispatch(click, this);
        }
        finally
        {
            _clickInProgress = false;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#fire-a-synthetic-pointer-event</summary>
    internal bool FireSyntheticPointerEvent(string type, bool notTrusted)
    {
        _ = notTrusted; // Events created by the library are never trusted

        // A PointerEvent whose pointerId is -1 (https://w3c.github.io/pointerevents/#the-click-auxclick-and-contextmenu-events)
        var @event = new PointerEvent(type, new PointerEventInit
        {
            Bubbles = true,
            Cancelable = true,
            Composed = true,
            View = NodeDocument.DefaultView,
            PointerId = -1,
        })
        {
            IsPositionless = true,
        };
        return DispatchEvent(@event);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-autocapitalize</summary>
    private partial string ComputeAutocapitalize()
    {
        var table = EnumeratedAttributes.Autocapitalize;
        var state = GetOwnAutocapitalizationHint();
        if (state < 0 || table.CanonicalKeywords[state] is null)
            return "";

        if (state == table.Keywords["none"])
            return "none";

        if (state == table.Keywords["sentences"])
            return "sentences";

        return table.CanonicalKeywords[state]!;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#own-autocapitalization-hint</summary>
    internal int GetOwnAutocapitalizationHint()
    {
        var table = EnumeratedAttributes.Autocapitalize;
        var value = GetAttributeValue("autocapitalize");
        if (!string.IsNullOrEmpty(value))
            return table.GetState(value);

        // Autocapitalize-and-autocorrect inheriting elements use the hint of their form owner
        if (IsHtmlNamespace && TagId is TagId.Button or TagId.Fieldset or TagId.Input or TagId.Output or TagId.Select or TagId.Textarea && FormOwner is { } form)
            return form.GetOwnAutocapitalizationHint();

        return table.MissingValueDefault;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-writingsuggestions</summary>
    private partial string ComputeWritingSuggestions()
    {
        // The computed writing suggestions value
        var table = EnumeratedAttributes.Writingsuggestions;
        for (Element? element = this; element is not null; element = element.ParentElement)
        {
            var state = table.GetState(element.GetAttributeValue("writingsuggestions"));
            if (state == table.Keywords["false"])
                return "false";

            if (state != table.MissingValueDefault)
                return "true";
        }

        return "true";
    }
}
