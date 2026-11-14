using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>The states of the elements that the locators and the assertions check, with the semantics of Playwright.</summary>
internal static class ElementStates
{
    private static readonly CheckVisibilityOptions VisibilityOptions = new() { CheckVisibilityCss = true };

    // The roles that support aria-readonly (https://w3c.github.io/aria/#aria-readonly)
    private static readonly HashSet<string> ReadOnlyRoles = new(StringComparer.Ordinal)
    {
        "checkbox", "combobox", "grid", "gridcell", "listbox", "radiogroup", "slider", "spinbutton", "textbox", "columnheader", "rowheader", "searchbox", "switch", "treegrid",
    };

    /// <summary>
    /// Gets whether the element is visible: it has a non-empty box and is not <c>visibility: hidden</c>. An element with
    /// <c>display: contents</c> is visible when one of its children is visible.
    /// </summary>
    public static bool IsVisible(Element element)
    {
        if (!element.IsConnected)
            return false;

        if (!element.CheckVisibility(VisibilityOptions))
        {
            if (element.OwnerDocument?.DefaultView is not { } window || window.GetComputedStyle(element).Display != "contents")
                return false;

            for (var child = element.FirstChild; child is not null; child = child.NextSibling)
            {
                switch (child)
                {
                    case Element childElement when IsVisible(childElement):
                        return true;

                    case Text text when text.Data.Length > 0 && element.OwnerDocument is { } document:
                        var range = document.CreateRange();
                        range.SelectNodeContents(text);
                        var textRect = range.GetBoundingClientRect();
                        if (textRect.Width > 0 && textRect.Height > 0)
                            return true;

                        break;
                }
            }

            return false;
        }

        var rect = element.GetBoundingClientRect();
        return rect.Width > 0 && rect.Height > 0;
    }

    /// <summary>Gets whether the element is disabled: a disabled form control, or <c>aria-disabled="true"</c> on the element or an ancestor.</summary>
    public static bool IsDisabled(Element element) => RetargetToControl(element).GetAccessibilityProperties().Disabled;

    /// <summary>Gets the checked state of a check box, a radio button or an element whose role has a checked state, or null.</summary>
    public static AccessibilityCheckedState? GetChecked(Element element) => RetargetToControl(element).GetAccessibilityProperties().Checked;

    /// <summary>Gets whether the element is editable: enabled and not read-only. Null when the element cannot be edited (it is not a field).</summary>
    public static bool? IsEditable(Element element)
    {
        element = RetargetToControl(element);
        bool readOnly;
        switch (element)
        {
            case HtmlInputElement input:
                readOnly = input.ReadOnly;
                break;

            case HtmlTextAreaElement textArea:
                readOnly = textArea.ReadOnly;
                break;

            case HtmlSelectElement:
                readOnly = false;
                break;

            case HtmlElement { IsContentEditable: true }:
                readOnly = false;
                break;

            default:
                if (!ReadOnlyRoles.Contains(element.ComputedRole))
                    return null;

                readOnly = string.Equals(element.GetAttribute("aria-readonly"), "true", StringComparison.OrdinalIgnoreCase);
                break;
        }

        return !readOnly && !IsDisabled(element);
    }

    /// <summary>Gets whether the element can be filled: a text field, a field whose value is set (a date...), a text area or an editable element.</summary>
    public static bool CanBeFilled(Element element) => element switch
    {
        HtmlInputElement input => IsTextInput(input) || IsValueInput(input),
        HtmlTextAreaElement => true,
        HtmlElement { IsContentEditable: true } => true,
        _ => false,
    };

    /// <summary>Gets whether the text of the input is typed with the keyboard.</summary>
    public static bool IsTextInput(HtmlInputElement input) => input.Type is "text" or "email" or "number" or "password" or "search" or "tel" or "url";

    /// <summary>Gets whether the value of the input is set, as Playwright does: the keyboard cannot type in a date, a time, a color or a range.</summary>
    public static bool IsValueInput(HtmlInputElement input) => input.Type is "date" or "datetime-local" or "month" or "time" or "week" or "color" or "range";

    /// <summary>Gets whether the element has the focus, including in a shadow root.</summary>
    public static bool IsFocused(Element element)
    {
        var active = element.OwnerDocument?.ActiveElement;
        while (active is { ShadowRoot: { ActiveElement: { } inner } })
        {
            active = inner;
        }

        return active == element;
    }

    /// <summary>Gets whether the element has no text, or, for a field, no value.</summary>
    public static bool IsEmpty(Element element) => RetargetToControl(element) switch
    {
        HtmlInputElement input => input.Value.Length == 0,
        HtmlTextAreaElement textArea => textArea.Value.Length == 0,
        var other => string.IsNullOrWhiteSpace(other.TextContent),
    };

    /// <summary>Gets the value of a field (<c>input</c>, <c>textarea</c> or <c>select</c>).</summary>
    public static string? GetInputValue(Element element) => RetargetToControl(element) switch
    {
        HtmlInputElement input => input.Value,
        HtmlTextAreaElement textArea => textArea.Value,
        HtmlSelectElement select => select.Value,
        _ => null,
    };

    /// <summary>
    /// Gets the element that receives the input of a field: an element inside a <c>label</c> that is not a field itself
    /// targets the control of the label, as Playwright does (<c>GetByText("Accept").CheckAsync()</c>).
    /// </summary>
    public static Element RetargetToControl(Element element)
    {
        if (element is HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement or HtmlElement { IsContentEditable: true })
            return element;

        if (element.GetAttribute("role") is { Length: > 0 })
            return element;

        if (element.Closest("label") is HtmlLabelElement { Control: { } control })
            return control;

        return element;
    }
}
