using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The access keys (https://html.spec.whatwg.org/multipage/interaction.html#the-accesskey-attribute). As in browsers on
/// Windows and Linux, the key combination of an assigned access key is Alt+Shift and the key given by the
/// <c>accesskey</c> attribute.
/// </summary>
internal static class AccessKeys
{
    /// <summary>The modifier keys of the assigned access keys, as represented by <see cref="GetLabel"/>.</summary>
    private const string ModifiersLabel = "Alt+Shift+";

    /// <summary>
    /// The assigned access key of the element (https://html.spec.whatwg.org/multipage/interaction.html#assigned-access-key):
    /// the first token of its <c>accesskey</c> attribute that is a single code point corresponding to a key, or null. The
    /// value is derived from the attribute when needed, which is the same as updating it whenever the attribute changes.
    /// </summary>
    public static string? GetAssignedAccessKey(Element element)
    {
        if (!element.IsHtmlNamespace || element.GetAttributeValue("accesskey") is not { } value)
            return null;

        foreach (var token in InfraString.SplitOnAsciiWhitespace(value))
        {
            if (Rune.DecodeFromUtf16(token, out var rune, out var length) != System.Buffers.OperationStatus.Done || length != token.Length)
                continue;

            // The control, format, private use and unassigned code points do not correspond to a key of a keyboard
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                continue;

            return token;
        }

        return null;
    }

    /// <summary>The string that represents the assigned access key of the element (https://html.spec.whatwg.org/multipage/interaction.html#dom-accesskeylabel), or the empty string.</summary>
    public static string GetLabel(Element element) => GetAssignedAccessKey(element) is { } key ? ModifiersLabel + key.ToUpperInvariant() : "";

    /// <summary>
    /// The user pressed a key combination (a trusted keydown event): when it corresponds to the assigned access key of an
    /// element of the document (in shadow-including tree order, the first one wins), the action of the command of the element
    /// is triggered (https://html.spec.whatwg.org/multipage/interaction.html#the-accesskey-attribute:assigned-access-key-3).
    /// </summary>
    /// <returns>Whether an access key was triggered.</returns>
    public static bool Process(Document document, KeyboardEvent keyboardEvent)
    {
        if (!keyboardEvent.AltKey || !keyboardEvent.ShiftKey || keyboardEvent.CtrlKey || keyboardEvent.MetaKey || document.BrowsingContext is null)
            return false;

        foreach (var node in document.ShadowIncludingInclusiveDescendants())
        {
            if (node is not Element element || GetAssignedAccessKey(element) is not { } key || !Matches(key, keyboardEvent))
                continue;

            if (GetCommandElement(element) is not { } commandElement || IsHiddenOrDisabled(element, commandElement))
                continue;

            if (commandElement.NodeDocument.BrowsingContext?.Top.Document is { } topDocument)
            {
                // Pressing an access key is a keyboard interaction: the focused element indicates its focus
                topDocument.LastFocusWasPointer = false;
            }

            TriggerAction(commandElement);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the key of the event is the access key. The key value of a letter depends on the Shift key, and the key value
    /// of a digit on the keyboard layout, so the key code (the physical key) is also compared.
    /// </summary>
    private static bool Matches(string accessKey, KeyboardEvent keyboardEvent)
    {
        if (string.Equals(accessKey, keyboardEvent.Key, StringComparison.OrdinalIgnoreCase))
            return true;

        var code = keyboardEvent.Code;
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) || code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
            return string.Equals(accessKey, code[^1..], StringComparison.OrdinalIgnoreCase);

        return false;
    }

    /// <summary>
    /// The element whose command facets apply when the access key of <paramref name="element"/> is pressed
    /// (https://html.spec.whatwg.org/multipage/interactive-elements.html#commands): the element itself, or the accesskey
    /// delegatee of a legend element. Every element that has an assigned access key defines a command.
    /// </summary>
    private static Element? GetCommandElement(Element element)
    {
        if (!element.IsHtml(TagId.Legend))
            return element;

        // https://html.spec.whatwg.org/multipage/interactive-elements.html#using-the-accesskey-attribute-on-a-legend-element-to-define-a-command
        if (element.ParentNode is not Element { } fieldset || !fieldset.IsHtml(TagId.Fieldset))
            return null;

        foreach (var descendant in fieldset.Descendants<Element>())
        {
            if (descendant.IsHtml(TagId.Label) || descendant.IsHtml(TagId.Legend))
                continue;

            if (GetKind(descendant) != CommandKind.None || GetAssignedAccessKey(descendant) is not null)
                return descendant;
        }

        return null;
    }

    /// <summary>
    /// Whether the access key does nothing: the element or one of its ancestors has a hidden attribute, or the command is
    /// hidden (its element has a hidden attribute) or disabled.
    /// </summary>
    private static bool IsHiddenOrDisabled(Element element, Element commandElement)
    {
        for (Node? node = element; node is not null; node = node is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode)
        {
            if (node is Element { IsHtmlNamespace: true } ancestor && ancestor.HasAttributeWithLocalName("hidden"))
                return true;
        }

        if (commandElement.IsHtmlNamespace && commandElement.HasAttributeWithLocalName("hidden"))
            return true;

        if (FocusModel.IsInert(commandElement))
            return true;

        return GetKind(commandElement) switch
        {
            CommandKind.Button => HtmlElementStates.IsActuallyDisabled(commandElement),
            CommandKind.Option => HtmlElementStates.IsActuallyDisabled(commandElement) || ((HtmlOptionElement)commandElement).CachedNearestAncestorSelect is not { } select || HtmlElementStates.IsActuallyDisabled(select),
            _ => false,
        };
    }

    /// <summary>Triggers the action of the command of the element (https://html.spec.whatwg.org/multipage/interactive-elements.html#command-facet-action).</summary>
    private static void TriggerAction(Element element)
    {
        switch (GetKind(element))
        {
            case CommandKind.Link:
                UserAgentDefaultActions.FireSyntheticClick(element);
                break;

            case CommandKind.Button:
                // The action of the command of a button is to fire a click event; as in browsers (and as expected by the
                // tests of :focus-visible), the button is also focused, as the elements of the other commands
                FocusModel.Focus(element, focusVisible: true);
                UserAgentDefaultActions.FireSyntheticClick(element);
                break;

            case CommandKind.Option:
                var option = (HtmlOptionElement)element;
                if (option.CachedNearestAncestorSelect is { } select)
                {
                    if (select.IsMultiple)
                    {
                        select.ToggleOption(option);
                    }
                    else
                    {
                        select.PickOption(option);
                    }
                }

                break;

            default:
                // https://html.spec.whatwg.org/multipage/interactive-elements.html#using-the-accesskey-attribute-to-define-a-command-on-other-elements
                FocusModel.Focus(element, focusVisible: true);
                UserAgentDefaultActions.FireSyntheticClick(element);
                break;
        }
    }

    /// <summary>The kind of command an element defines without an access key (https://html.spec.whatwg.org/multipage/interactive-elements.html#commands).</summary>
    private static CommandKind GetKind(Element element)
    {
        if (!element.IsHtmlNamespace)
            return CommandKind.None;

        return element.TagId switch
        {
            TagId.A when element.HasAttributeWithLocalName("href") => CommandKind.Link,
            TagId.Button => CommandKind.Button,
            TagId.Input when HtmlElementStates.GetInputType(element) is "submit" or "reset" or "image" or "button" or "radio" or "checkbox" => CommandKind.Button,
            TagId.Option when element is HtmlOptionElement { CachedNearestAncestorSelect: not null } && element.GetAttributeValue("value") is null or { Length: > 0 } => CommandKind.Option,
            _ => CommandKind.None,
        };
    }

    private enum CommandKind
    {
        None,
        Link,
        Button,
        Option,
    }
}
