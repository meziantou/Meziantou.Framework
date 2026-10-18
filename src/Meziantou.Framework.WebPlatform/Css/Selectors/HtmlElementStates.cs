using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>
/// The element states used by the pseudo-classes defined by HTML (https://html.spec.whatwg.org/multipage/semantics-other.html#pseudo-classes).
/// States that depend on the form controls' values, dialogs, popovers and media are refined by the element interfaces.
/// </summary>
internal static class HtmlElementStates
{
    private static bool IsHtml(Element element, string localName) => element.IsHtmlNamespace && element.LocalName == localName;

    // ----------------------------------------------------------------------------------------------------------------
    // Disabled state (https://html.spec.whatwg.org/multipage/semantics-other.html#concept-element-disabled)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/semantics-other.html#concept-element-disabled</summary>
    public static bool IsActuallyDisabled(Element element)
    {
        if (!element.IsHtmlNamespace)
            return false;

        return element.LocalName switch
        {
            "button" or "input" or "select" or "textarea" => IsFormControlDisabled(element),
            "optgroup" => element.HasAttributeWithLocalName("disabled") || IsNearestAncestorSelectDisabled(element),
            "option" => (element is HtmlOptionElement option && option.IsDisabled) || IsNearestAncestorSelectDisabled(element),
            "fieldset" => IsDisabledFieldset(element),
            _ => element is HtmlElement { IsCustomizedFormAssociatedCustomElement: true } && IsFormControlDisabled(element),
        };
    }

    /// <summary>
    /// Returns whether the nearest ancestor select of an optgroup or option element (https://html.spec.whatwg.org/multipage/form-elements.html#option-element-nearest-ancestor-select)
    /// is disabled. As in the browsers, an optgroup counts as its own optgroup ancestor, so a nested optgroup has no nearest ancestor select.
    /// </summary>
    private static bool IsNearestAncestorSelectDisabled(Element element)
    {
        var hasAncestorOptgroup = element.LocalName == "optgroup";
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (!ancestor.IsHtmlNamespace)
                continue;

            switch (ancestor.LocalName)
            {
                case "datalist" or "hr" or "option":
                    return false;

                case "optgroup":
                    if (hasAncestorOptgroup)
                        return false;

                    hasAncestorOptgroup = true;
                    break;

                case "select":
                    return IsFormControlDisabled(ancestor);
            }
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-fe-disabled</summary>
    public static bool IsFormControlDisabled(Element element) => element.HasAttributeWithLocalName("disabled") || IsDescendantOfDisabledFieldset(element);

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-fieldset-disabled</summary>
    public static bool IsDisabledFieldset(Element fieldset) => fieldset.HasAttributeWithLocalName("disabled") || IsDescendantOfDisabledFieldset(fieldset);

    /// <summary>Returns whether the element is a descendant of a disabled fieldset, and not a descendant of its first legend child.</summary>
    private static bool IsDescendantOfDisabledFieldset(Element element)
    {
        Node child = element;
        for (var ancestor = element.ParentElement; ancestor is not null; child = ancestor, ancestor = ancestor.ParentElement)
        {
            if (!IsHtml(ancestor, "fieldset") || !ancestor.HasAttributeWithLocalName("disabled"))
                continue;

            // Is the element in the first legend child of the fieldset?
            Element? firstLegend = null;
            for (var c = ancestor.FirstChild; c is not null; c = c.NextSibling)
            {
                if (c is Element legend && IsHtml(legend, "legend"))
                {
                    firstLegend = legend;
                    break;
                }
            }

            if (firstLegend is not null && ReferenceEquals(child, firstLegend))
                continue;

            return true;
        }

        return false;
    }

    public static bool IsEnabledCandidate(Element element) => element.IsHtmlNamespace && (element.LocalName is "button" or "input" or "select" or "textarea" or "optgroup" or "option" or "fieldset" || element is HtmlElement { IsCustomizedFormAssociatedCustomElement: true });

    // ----------------------------------------------------------------------------------------------------------------
    // Input types
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The state of the type attribute of an input element (https://html.spec.whatwg.org/multipage/input.html#attr-input-type).</summary>
    public static string GetInputType(Element input)
    {
        var type = input.GetAttributeValue("type");
        if (type is null)
            return "text";

        type = InfraAscii.ToAsciiLowercase(type);
        return type switch
        {
            "hidden" or "text" or "search" or "tel" or "url" or "email" or "password" or "date" or "month" or "week" or "time" or "datetime-local"
                or "number" or "range" or "color" or "checkbox" or "radio" or "file" or "submit" or "image" or "reset" or "button" => type,
            _ => "text",
        };
    }

    private static bool IsInput(Element element, out string type)
    {
        if (IsHtml(element, "input"))
        {
            type = GetInputType(element);
            return true;
        }

        type = "";
        return false;
    }

    /// <summary>The types to which the readonly attribute applies.</summary>
    private static bool ReadonlyApplies(string type) => type is "text" or "search" or "url" or "tel" or "email" or "password" or "date" or "month" or "week" or "time" or "datetime-local" or "number";

    /// <summary>The types to which the required attribute applies.</summary>
    private static bool RequiredApplies(string type) => type is not ("hidden" or "range" or "color" or "submit" or "image" or "reset" or "button");

    /// <summary>The types to which the placeholder attribute applies.</summary>
    private static bool PlaceholderApplies(string type) => type is "text" or "search" or "url" or "tel" or "email" or "password" or "number";

    // ----------------------------------------------------------------------------------------------------------------
    // Pseudo-classes
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>:read-write (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-read-write).</summary>
    public static bool IsReadWrite(Element element)
    {
        if (IsInput(element, out var type))
            return ReadonlyApplies(type) && !element.HasAttributeWithLocalName("readonly") && !IsActuallyDisabled(element);

        if (IsHtml(element, "textarea"))
            return !element.HasAttributeWithLocalName("readonly") && !IsActuallyDisabled(element);

        return IsEditingHostOrEditable(element);
    }

    /// <summary>Returns whether the element is an editing host or is editable (contenteditable, designMode).</summary>
    internal static bool IsEditingHostOrEditable(Element element) => Html.Editing.IsEditingHost(element) || Html.Editing.IsEditable(element);

    /// <summary>
    /// :placeholder-shown (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-placeholder-shown): as in
    /// browsers, an empty placeholder attribute is presented too.
    /// </summary>
    public static bool IsPlaceholderShown(Element element)
    {
        if (element is HtmlInputElement input && IsInput(element, out var type))
        {
            return PlaceholderApplies(type)
                && element.HasAttributeWithLocalName("placeholder")
                && input.Value.Length == 0;
        }

        if (element is HtmlTextAreaElement textArea)
            return element.HasAttributeWithLocalName("placeholder") && textArea.Value.Length == 0;

        return false;
    }

    /// <summary>:checked (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-checked).</summary>
    public static bool IsChecked(Element element)
    {
        if (element is HtmlInputElement input)
            return input.InputType is InputType.Checkbox or InputType.Radio && input.Checkedness;

        return element is HtmlOptionElement option && option.Selectedness;
    }

    /// <summary>:indeterminate (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-indeterminate).</summary>
    public static bool IsIndeterminate(Element element)
    {
        if (element is HtmlInputElement input)
        {
            if (input.InputType == InputType.Checkbox)
                return input.Indeterminate;

            if (input.InputType == InputType.Radio)
                return !input.Checkedness && !input.GetRadioButtonGroup().Exists(r => r.Checkedness);

            return false;
        }

        return IsHtml(element, "progress") && !element.HasAttributeWithLocalName("value");
    }

    /// <summary>The form owner of a form-associated element (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#form-owner).</summary>
    public static Element? GetFormOwner(Element element) => (element as HtmlElement)?.FormOwner;

    /// <summary>:default (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-default).</summary>
    public static bool IsDefault(Element element)
    {
        if (IsInput(element, out var type))
        {
            if (type is "checkbox" or "radio")
                return element.HasAttributeWithLocalName("checked");

            if (type is "submit" or "image")
                return IsDefaultButton(element);

            return false;
        }

        if (IsHtml(element, "button"))
            return IsSubmitButton(element) && IsDefaultButton(element);

        return IsHtml(element, "option") && element.HasAttributeWithLocalName("selected");
    }

    private static bool IsSubmitButton(Element element)
    {
        if (IsInput(element, out var type))
            return type is "submit" or "image";

        // The submit button state of a button depends on its command attributes and its select parent
        return element is HtmlButtonElement button && button.IsButtonSubmitButton;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#default-button</summary>
    private static bool IsDefaultButton(Element button)
    {
        var form = GetFormOwner(button);
        if (form is null)
            return false;

        var root = form.GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element candidate && IsSubmitButton(candidate) && ReferenceEquals(GetFormOwner(candidate), form))
                return ReferenceEquals(candidate, button);
        }

        return false;
    }

    /// <summary>:required (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-required).</summary>
    public static bool IsRequired(Element element)
    {
        if (IsInput(element, out var type))
            return RequiredApplies(type) && element.HasAttributeWithLocalName("required");

        return (IsHtml(element, "select") || IsHtml(element, "textarea")) && element.HasAttributeWithLocalName("required");
    }

    /// <summary>
    /// :optional (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-optional): as in Blink and WebKit, an
    /// input element to which the required attribute does not apply (e.g. a hidden input) is optional.
    /// </summary>
    public static bool IsOptional(Element element)
    {
        if (IsInput(element, out var type))
            return !RequiredApplies(type) || !element.HasAttributeWithLocalName("required");

        return (IsHtml(element, "select") || IsHtml(element, "textarea")) && !element.HasAttributeWithLocalName("required");
    }

    /// <summary>
    /// :valid and :invalid (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-valid): null when the
    /// element is neither. A form is invalid when one of its controls is invalid, and a fieldset when one of its descendants is.
    /// </summary>
    public static bool? IsValid(Element element)
    {
        switch (element)
        {
            case HtmlFormElement form:
                return !form.Elements.GetElements().Exists(e => e is HtmlElement { IsCandidateForConstraintValidation: true, SatisfiesConstraints: false });

            case HtmlFieldSetElement fieldset:
                for (var node = fieldset.FirstChild; node is not null; node = node.GetNextInTreeOrder(fieldset))
                {
                    if (node is HtmlElement { IsSubmittable: true, IsCandidateForConstraintValidation: true, SatisfiesConstraints: false })
                        return false;
                }

                return true;

            case HtmlElement { IsSubmittable: true } control when control.IsCandidateForConstraintValidation:
                return control.SatisfiesConstraints;

            default:
                return null;
        }
    }

    /// <summary>
    /// :user-valid and :user-invalid (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-user-valid): null
    /// when the element is neither (it is not an input, textarea or select element whose user validity is true and that is a
    /// candidate for constraint validation).
    /// </summary>
    public static bool? IsUserValid(Element element)
    {
        if (element is not (HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement) || element is not HtmlElement { UserValidity: true, IsCandidateForConstraintValidation: true } control)
            return null;

        return control.SatisfiesConstraints;
    }

    /// <summary>:in-range and :out-of-range (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-in-range): null when the element is neither.</summary>
    public static bool? IsInRange(Element element)
    {
        if (element is not HtmlInputElement input || !input.IsCandidateForConstraintValidation || !input.HasRangeLimitations)
            return null;

        return (input.GetValidityFlags() & (ValidityFlags.RangeUnderflow | ValidityFlags.RangeOverflow)) == ValidityFlags.None;
    }

    /// <summary>:open (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-open).</summary>
    public static bool IsOpen(Element element) => element is HtmlSelectElement select
        ? select.IsPickerOpen
        : (IsHtml(element, "details") || IsHtml(element, "dialog")) && element.HasAttributeWithLocalName("open");

    public static bool IsMediaElement(Element element) => IsHtml(element, "audio") || IsHtml(element, "video");

    /// <summary>
    /// :link and :any-link (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-link), including the MathML
    /// <c>a</c> element, whose href attribute has the same semantics (https://w3c.github.io/mathml-core/#the-a-element), and
    /// the SVG <c>a</c> element, whose link is its href attribute or the deprecated xlink:href attribute
    /// (https://svgwg.org/svg2-draft/linking.html#Links).
    /// </summary>
    public static bool IsLink(Element element)
    {
        if (element is SvgAElement)
            return element.HasAttributeWithLocalName("href") || element.HasAttributeNS(Namespaces.XLink, "href");

        return (IsHtml(element, "a") || IsHtml(element, "area") || element is MathMLAnchorElement) && element.HasAttributeWithLocalName("href");
    }

    /// <summary>
    /// :heading and :heading() (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-heading): the computed
    /// heading level of the h1-h6 elements (https://html.spec.whatwg.org/multipage/sections.html#get-an-element's-computed-heading-level),
    /// or 0 for the other elements.
    /// </summary>
    public static int GetHeadingLevel(Element element)
    {
        if (!element.IsHtmlNamespace || element.LocalName.Length != 2 || element.LocalName[0] != 'h' || element.LocalName[1] is not (>= '1' and <= '6'))
            return 0;

        return Math.Min(9, element.LocalName[1] - '0' + GetHeadingOffset(element));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/sections.html#get-an-element's-computed-heading-offset: the sum of the
    /// headingoffset attributes of the shadow-including inclusive ancestors, up to the first one with a headingreset attribute.
    /// </summary>
    private static int GetHeadingOffset(Element element)
    {
        // The level is at most 9, so the offset is clamped to avoid overflows
        var offset = 0;
        for (var ancestor = element; ancestor is not null; ancestor = ancestor.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : ancestor.ParentElement)
        {
            if (!ancestor.IsHtmlNamespace)
                continue;

            if (ancestor.GetAttributeValue("headingoffset") is { } value && Html.Microsyntaxes.TryParseNonNegativeInteger(value, out var nextOffset))
            {
                offset = (int)Math.Min(9, offset + Math.Min(9, nextOffset));
            }

            if (ancestor.HasAttributeWithLocalName("headingreset"))
                break;
        }

        return offset;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // :target (https://html.spec.whatwg.org/multipage/browsing-the-web.html#target-element)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the target element of the document: the indicated part given by the fragment of the document URL.</summary>
    public static Element? GetTargetElement(Document document)
    {
        // https://wicg.github.io/scroll-to-text-fragment/#invoking-text-directives: the indicated part of a document with text
        // directives is the first range found, when the document is allowed to scroll to it. Its target element is the first
        // common ancestor of the start and the end of the range that is an element (the host of a shadow root for a text that
        // is a child of the shadow root).
        if (document.IsTargetTextIndicated && document.TargetTextRanges is [var targetText, ..])
        {
            var container = targetText.CommonAncestorContainer;
            return container as Element ?? container.ParentElement ?? (container.ParentNode as ShadowRoot)?.Host;
        }

        var fragment = document.TargetFragment;
        if (string.IsNullOrEmpty(fragment))
            return null;

        return GetIndicatedElement(document, fragment);
    }

    /// <summary>Returns the element of a document indicated by a URL fragment (https://html.spec.whatwg.org/multipage/browsing-the-web.html#select-the-indicated-part), or null.</summary>
    public static Element? GetIndicatedElement(Document document, string fragment)
    {
        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#find-a-potential-indicated-element
        var decoded = System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(fragment));
        return FindPotentialIndicatedElement(document, fragment) ?? FindPotentialIndicatedElement(document, decoded);
    }

    private static Element? FindPotentialIndicatedElement(Document document, string fragment)
    {
        if (document.GetElementById(fragment) is { } element)
            return element;

        for (var node = document.FirstChild; node is not null; node = node.GetNextInTreeOrder(document))
        {
            if (node is Element a && IsHtml(a, "a") && a.GetAttributeValue("name") == fragment)
                return a;
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // :lang() (https://drafts.csswg.org/selectors-4/#the-lang-pseudo)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the language of the element (https://html.spec.whatwg.org/multipage/dom.html#language), or null when unknown.</summary>
    public static string? GetLanguage(Element element)
    {
        for (Node? node = element; node is not null; node = node.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode)
        {
            if (node is not Element current)
                continue;

            // https://w3c.github.io/webvtt/#the-cue-pseudo-element: the language of a WebVTT internal node object is its
            // applicable language
            if (current is WebVtt.WebVttElement vttElement)
                return vttElement.Language;

            // xml:lang has priority over lang
            if (current.GetAttributeValue(Namespaces.Xml, "lang") is { } xmlLang)
                return xmlLang;

            if ((current.IsHtmlNamespace || ReferenceEquals(current.NamespaceUri, Namespaces.Svg)) && current.GetAttributeValue("lang") is { } lang)
                return lang;
        }

        // The pragma-set default language, then the language of the higher-level protocol (the Content-Language header)
        var document = element.NodeDocument;
        return document.PragmaSetDefaultLanguage ?? document.ProtocolLanguage;
    }

    /// <summary>Extended filtering (https://www.rfc-editor.org/rfc/rfc4647#section-3.3.2), with the implicit wildcard of Selectors 4.</summary>
    public static bool MatchesLanguageRange(string language, string range)
    {
        if (range.Length == 0)
            return language.Length == 0;

        if (language.Length == 0)
            return false;

        // A subtag of a language tag has at most 8 characters (https://www.rfc-editor.org/rfc/rfc5646#section-2.1): as in
        // browsers, a value with a longer subtag is not a language tag, and it matches no range
        if (range is not "*" && !range.Contains('-', StringComparison.Ordinal))
        {
            // Most ranges are a single subtag (e.g. :lang(en)): they match the languages whose first subtag is the range,
            // without splitting the values
            if (HasSubtagLongerThan8(language))
                return false;

            var dash = language.IndexOf('-', StringComparison.Ordinal);
            return InfraAscii.EqualsIgnoreAsciiCase(dash < 0 ? language : language.AsSpan(0, dash), range);
        }

        var rangeSubtags = range.Split('-');
        var languageSubtags = language.Split('-');
        if (languageSubtags.Any(subtag => subtag.Length > 8))
            return false;

        if (rangeSubtags[0] != "*" && !InfraAscii.EqualsIgnoreAsciiCase(rangeSubtags[0], languageSubtags[0]))
            return false;

        var r = 1;
        var l = 1;
        while (r < rangeSubtags.Length)
        {
            if (rangeSubtags[r] == "*")
            {
                r++;
                continue;
            }

            if (l >= languageSubtags.Length)
                return false;

            if (InfraAscii.EqualsIgnoreAsciiCase(rangeSubtags[r], languageSubtags[l]))
            {
                r++;
                l++;
                continue;
            }

            if (languageSubtags[l].Length == 1)
                return false;

            l++;
        }

        return true;
    }

    private static bool HasSubtagLongerThan8(string language)
    {
        var length = 0;
        foreach (var c in language)
        {
            length = c == '-' ? 0 : length + 1;
            if (length > 8)
                return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // :dir() (https://html.spec.whatwg.org/multipage/dom.html#the-directionality)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns true for 'ltr' and false for 'rtl' (https://html.spec.whatwg.org/multipage/dom.html#the-directionality).</summary>
    public static bool IsLeftToRight(Element element)
    {
        while (true)
        {
            var state = GetDirState(element);
            if (state == "ltr")
                return true;

            if (state == "rtl")
                return false;

            if (state == "auto" || IsHtml(element, "bdi"))
                return GetAutoDirectionality(element) ?? true;

            if (IsInput(element, out var type) && type == "tel")
                return true;

            // The parent directionality
            switch (element.ParentNode)
            {
                case ShadowRoot shadowRoot:
                    element = shadowRoot.Host;
                    break;
                case Element parent:
                    element = parent;
                    break;
                default:
                    return true;
            }
        }
    }

    /// <summary>The state of the dir attribute: "ltr", "rtl", "auto", or null (undefined) (https://html.spec.whatwg.org/multipage/dom.html#attr-dir).</summary>
    private static string? GetDirState(Element element)
    {
        if (!element.IsHtmlNamespace || element.GetAttributeValue("dir") is not { } dir)
            return null;

        dir = InfraAscii.ToAsciiLowercase(dir);
        return dir is "ltr" or "rtl" or "auto" ? dir : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#auto-directionality</summary>
    private static bool? GetAutoDirectionality(Element element)
    {
        // Auto-directionality form-associated elements
        var isAutoDirectionalityInput = IsInput(element, out var type) && type is "hidden" or "text" or "search" or "tel" or "url" or "email" or "password" or "submit" or "reset" or "button";
        if (isAutoDirectionalityInput || IsHtml(element, "textarea"))
        {
            // The value of the control (not its default value): https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-fe-value
            var value = element switch
            {
                HtmlInputElement input => input.Value,
                HtmlTextAreaElement textArea => textArea.Value,
                _ => element.GetAttributeValue("value") ?? "",
            };
            if (GetStrongDirection(value) == false)
                return false;

            return value.Length > 0 ? true : null;
        }

        if (element is HtmlSlotElement slot && slot.GetRoot() is ShadowRoot)
        {
            var assignedNodes = slot.AssignedNodes();
            if (assignedNodes.Count > 0)
            {
                foreach (var child in assignedNodes)
                {
                    var childDirection = child is Text text ? GetStrongDirection(text.Data) : GetContainedTextAutoDirectionality((Element)child, canExcludeRoot: true);
                    if (childDirection is not null)
                        return childDirection;
                }

                return null;
            }
        }

        return GetContainedTextAutoDirectionality(element, canExcludeRoot: false);
    }

    private static bool IsExcludedFromAutoDirectionality(Element element) =>
        element.IsHtmlNamespace && (element.LocalName is "bdi" or "script" or "style" or "textarea" || GetDirState(element) is not null);

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#contained-text-auto-directionality</summary>
    private static bool? GetContainedTextAutoDirectionality(Element element, bool canExcludeRoot)
    {
        if (canExcludeRoot && IsExcludedFromAutoDirectionality(element))
            return null;

        var node = element.FirstChild;
        while (node is not null)
        {
            if (node is Element descendant)
            {
                if (IsExcludedFromAutoDirectionality(descendant))
                {
                    node = node.GetNextInTreeOrderSkippingChildren(element);
                    continue;
                }

                if (descendant is HtmlSlotElement && descendant.GetRoot() is ShadowRoot shadowRoot)
                    return IsLeftToRight(shadowRoot.Host);
            }
            else if (node is Text text && GetStrongDirection(text.Data) is { } direction)
            {
                return direction;
            }

            node = node.GetNextInTreeOrder(element);
        }

        return null;
    }

    private static bool? GetStrongDirection(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var bidiClass = UnicodeProperties.GetBidiClass(rune.Value);
            if (bidiClass == BidiClass.L)
                return true;

            if (bidiClass is BidiClass.R or BidiClass.AL)
                return false;
        }

        return null;
    }
}
