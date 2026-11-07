using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The getters of the IDL attributes annotated with [ReflectSetter]: the setter reflects the content attribute, the getter
// is defined by the specification.

public partial class HtmlBaseElement
{
    partial void OnCreated() => NodeDocument.HasBaseElements = true;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/semantics.html#set-the-frozen-base-url: the frozen base URL is set when the element
    /// becomes the first base element with an href attribute. It is computed lazily, except when a content security policy
    /// applies to the document, so that the violations of the <c>base-uri</c> directive are reported when the element is inserted.
    /// </summary>
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        SetFrozenBaseUrlIfContentSecurityPolicy();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName == "href")
        {
            SetFrozenBaseUrlIfContentSecurityPolicy();
        }
    }

    private void SetFrozenBaseUrlIfContentSecurityPolicy()
    {
        if (IsConnected && ContentSecurityPolicy.GetInlineCheckDocument(this) is not null)
        {
            _ = NodeDocument.DocumentBaseUrl;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#dom-base-href</summary>
    private partial string ComputeHref()
    {
        var document = NodeDocument;
        var url = GetAttributeValue("href") ?? "";

        // Parsed with the fallback base URL, so the base element isn't affected by other base elements or itself
        var record = UrlParser.Parse(url, document.FallbackBaseUrl, WebEncodingRegistryHelper.GetUrlEncoding(document));
        return record is null ? url : record.Serialize();
    }
}

public partial class HtmlButtonElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-button-type</summary>
    private partial string ComputeType()
    {
        if (IsButtonSubmitButton)
            return "submit";

        var table = EnumeratedAttributes.ButtonType;
        var state = table.GetState(GetAttributeValue("type"));
        return table.CanonicalKeywords[state] ?? "button";
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-submit-button</summary>
    internal bool IsButtonSubmitButton
    {
        get
        {
            var table = EnumeratedAttributes.ButtonType;
            var state = table.GetState(GetAttributeValue("type"));
            if (state == table.MissingValueDefault)
                return !HasAttributeWithLocalName("command") && !HasAttributeWithLocalName("commandfor") && ParentNode is not HtmlSelectElement;

            return state == table.Keywords["submit"];
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-button-command</summary>
    private partial string ComputeCommand()
    {
        var value = GetAttributeValue("command");
        if (value is null)
            return "";

        // The Custom state: the value starts with "--"
        if (value.StartsWith("--", StringComparison.Ordinal))
            return value;

        var table = EnumeratedAttributes.Command;
        var state = table.GetState(value);
        return state < 0 ? "" : table.CanonicalKeywords[state] ?? "";
    }

    private partial string ComputeFormAction() => Reflection.GetFormAction(this, "formaction");
}

public partial class HtmlDialogElement
{
    /// <summary>Whether the dialog is modal (https://html.spec.whatwg.org/multipage/interactive-elements.html#is-modal).</summary>
    internal bool IsModal
    {
        get;
        set
        {
            field = value;
            NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dom-dialog-closedby</summary>
    private partial string ComputeClosedBy()
    {
        // The computed closed-by state
        var table = EnumeratedAttributes.Closedby;
        var state = table.GetState(GetAttributeValue("closedby"));
        if (state == table.MissingValueDefault)
            return IsModal ? "closerequest" : "none";

        return table.CanonicalKeywords[state] ?? "";
    }
}

public partial class HtmlFormElement
{
    private partial string ComputeAction() => Reflection.GetFormAction(this, "action");
}

public partial class HtmlInputElement
{
    private partial string ComputeFormAction() => Reflection.GetFormAction(this, "formaction");

    private partial string ComputeAutocomplete() => Reflection.GetIdlExposedAutofillValue(this);

    private partial uint ComputeWidth() => GetImageButtonDimensions().Width;

    private partial uint ComputeHeight() => GetImageButtonDimensions().Height;
}

public partial class HtmlSelectElement
{
    private partial string ComputeAutocomplete() => Reflection.GetIdlExposedAutofillValue(this);
}

public partial class HtmlTextAreaElement
{
    private partial string ComputeAutocomplete() => Reflection.GetIdlExposedAutofillValue(this);
}

public partial class HtmlOptionElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-label</summary>
    private partial string ComputeLabel() => GetAttributeValue("label") ?? GetLabel(includeAltText: false);

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-label</summary>
    internal string GetLabel(bool includeAltText)
    {
        var label = GetAttributeValue("label");
        if (!string.IsNullOrEmpty(label))
            return label;

        return Reflection.GetHtmlAwareTextContent(this, includeAltText);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-value</summary>
    private partial string ComputeValue() => GetAttributeValue("value") ?? Reflection.GetHtmlAwareTextContent(this, includeAltText: false);
}

public partial class HtmlMeterElement
{
    private double MinimumValue => ParseFloat("min") ?? 0;

    private double MaximumValue => Math.Max(ParseFloat("max") ?? 1.0, MinimumValue);

    private double LowBoundary => Math.Clamp(ParseFloat("low") ?? MinimumValue, MinimumValue, MaximumValue);

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-meter-actual</summary>
    private partial double ComputeValue() => Math.Clamp(ParseFloat("value") ?? 0, MinimumValue, MaximumValue);

    private partial double ComputeMin() => MinimumValue;

    private partial double ComputeMax() => MaximumValue;

    private partial double ComputeLow() => LowBoundary;

    private partial double ComputeHigh() => Math.Clamp(ParseFloat("high") ?? MaximumValue, LowBoundary, MaximumValue);

    private partial double ComputeOptimum() => Math.Clamp(ParseFloat("optimum") ?? ((MinimumValue + MaximumValue) / 2), MinimumValue, MaximumValue);

    private double? ParseFloat(string name) => GetAttributeValue(name) is { } value && Microsyntaxes.TryParseFloat(value, out var result) ? result : null;
}

public partial class HtmlProgressElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-progress-value</summary>
    private partial double ComputeValue()
    {
        // Indeterminate progress bar
        var value = GetAttributeValue("value");
        if (value is null)
            return 0;

        var maximum = GetAttributeValue("max") is { } max && Microsyntaxes.TryParseFloat(max, out var parsedMax) && parsedMax > 0 ? parsedMax : 1.0;
        var current = Microsyntaxes.TryParseFloat(value, out var parsedValue) && parsedValue > 0 ? parsedValue : 0;
        return Math.Min(current, maximum);
    }
}

public partial class HtmlAnchorElement
{
    private partial string ComputeHref() => Reflection.GetHyperlinkHref(this);
}

public partial class HtmlAreaElement
{
    private partial string ComputeHref() => Reflection.GetHyperlinkHref(this);
}

public partial class MathMLAnchorElement
{
    private partial string ComputeHref() => Reflection.GetHyperlinkHref(this);
}
