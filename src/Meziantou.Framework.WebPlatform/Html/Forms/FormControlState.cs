using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The state of the form controls of a document, saved in the persisted user state of its session history entry when the
/// document is destroyed, and restored when a traversal loads the document again
/// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#persisted-user-state-restoration, which leaves the form
/// control state implementation-defined). As in the browsers, a control is identified by its element type, its type and
/// its name, and by its position among the controls of the document with the same identity. The state of a control whose
/// autofill field name is "off" is not saved (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#autofilling-form-controls:-the-autocomplete-attribute).
/// The controls are restored once the document is parsed, without firing input or change events.
/// </summary>
internal sealed class FormControlState
{
    private readonly Dictionary<ControlKey, List<object?>> _states;

    private FormControlState(Dictionary<ControlKey, List<object?>> states) => _states = states;

    /// <summary>Saves the state of the form controls of the document, or returns null when no control has a state to restore.</summary>
    public static FormControlState? Save(Document document)
    {
        Dictionary<ControlKey, List<object?>>? states = null;
        var hasState = false;
        foreach (var node in document.Descendants())
        {
            if (node is not HtmlElement element || GetKey(element) is not { } key)
                continue;

            var state = IsAutocompleteOff(element) ? null : SaveState(element);
            states ??= [];
            if (!states.TryGetValue(key, out var list))
            {
                list = [];
                states.Add(key, list);
            }

            list.Add(state);
            hasState |= state is not null;
        }

        return hasState ? new FormControlState(states!) : null;
    }

    /// <summary>Restores the state of the form controls of the document (the controls removed by the restoration of a previous control are skipped).</summary>
    public void Restore(Document document)
    {
        var controls = new List<(HtmlElement Element, object State)>();
        var counts = new Dictionary<ControlKey, int>();
        foreach (var node in document.Descendants())
        {
            if (node is not HtmlElement element || GetKey(element) is not { } key)
                continue;

            counts.TryGetValue(key, out var index);
            counts[key] = index + 1;
            if (_states.TryGetValue(key, out var list) && index < list.Count && list[index] is { } state && !IsAutocompleteOff(element))
            {
                controls.Add((element, state));
            }
        }

        foreach (var (element, state) in controls)
        {
            if (!element.IsConnected || !ReferenceEquals(element.NodeDocument, document))
                continue;

            RestoreState(element, state);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document, step 1: the form controls of a
    /// document restored from the back/forward cache whose autofill field name is "off" are reset.
    /// </summary>
    public static void ResetAutocompleteOffControls(Document document)
    {
        List<HtmlElement>? controls = null;
        foreach (var node in document.Descendants())
        {
            if (node is HtmlElement element && IsAutocompleteOff(element))
            {
                (controls ??= []).Add(element);
            }
        }

        if (controls is null)
            return;

        foreach (var control in controls)
        {
            control.ResetAlgorithm();
        }
    }

    private static ControlKey? GetKey(HtmlElement element) => element switch
    {
        HtmlInputElement input => new ControlKey(input.LocalName, input.InputType, input.GetAttributeValue("name")),
        HtmlTextAreaElement or HtmlSelectElement => new ControlKey(element.LocalName, Type: null, element.GetAttributeValue("name")),
        _ when element.IsCustomizedFormAssociatedCustomElement => new ControlKey(element.LocalName, Type: null, element.GetAttributeValue("name")),
        _ => null,
    };

    /// <summary>The identity of a control: its local name, its type (for an input element) and its name.</summary>
    private readonly record struct ControlKey(string LocalName, InputType? Type, string? Name);

    private static object? SaveState(HtmlElement element) => element switch
    {
        HtmlInputElement input => input.SaveFormControlState(),
        HtmlTextAreaElement textArea => textArea.SaveFormControlState(),
        HtmlSelectElement select => select.SaveFormControlState(),
        _ => element.FormAssociatedCustomElementData.State,
    };

    private static void RestoreState(HtmlElement element, object state)
    {
        switch (element)
        {
            case HtmlInputElement input:
                input.RestoreFormControlState(state);
                break;
            case HtmlTextAreaElement textArea when state is string rawValue:
                textArea.RestoreFormControlState(rawValue);
                break;
            case HtmlSelectElement select when state is List<(int Index, string Value)> selectState:
                select.RestoreFormControlState(selectState);
                break;
            case not (HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement) when element.IsCustomizedFormAssociatedCustomElement:
                // https://html.spec.whatwg.org/multipage/custom-elements.html#face-state: "When the user agent updates a
                // form-associated custom element's value on behalf of a user or as part of navigation, its MUST enqueue a custom
                // element callback reaction with the element, callback name "formStateRestoreCallback", and « the new state and
                // "restore" »."
                var callbackState = state is List<KeyValuePair<string, FormDataEntryValue>> entries ? new FormData([.. entries]) : state;
                CustomElementReactions.EnqueueCallbackReaction(element, CustomElementCallbackKind.FormStateRestore, [callbackState, "restore"]);
                break;
        }
    }

    /// <summary>
    /// Whether the autofill field name of the control is "off": its <c>autocomplete</c> attribute is "off", or it has no
    /// valid value and the <c>autocomplete</c> attribute of its form owner is "off"
    /// (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#autofill-processing-model).
    /// </summary>
    private static bool IsAutocompleteOff(HtmlElement element)
    {
        if (element is not (HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement))
            return false;

        var value = Reflection.GetIdlExposedAutofillValue(element);
        if (value.Length > 0)
            return value == "off";

        return element.FormOwner is { } form && form.Autocomplete == "off";
    }
}
