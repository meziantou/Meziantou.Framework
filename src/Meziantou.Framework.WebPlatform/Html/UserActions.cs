using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The state of the pointing device of a document, for the :hover and :active pseudo-classes
// (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-hover). There is no pointing device: the state is
// updated by the trusted pointer and mouse events that simulate the user input (see UserActionStates).
public partial class Document
{
    /// <summary>
    /// Whether a node of the document was the target of a pointer of the user (see <see cref="UserInput"/>): the removal of a
    /// node then updates the targets of the pointers.
    /// </summary>
    internal bool HasPointerTargets { get; set; }

    /// <summary>The element the user designates with the pointing device (the target of the last pointer event), or null.</summary>
    internal Element? DesignatedElement
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            var previous = field;
            field = value;
            OnElementStateChangedForStyles(previous, value, StyleInvalidationStates.Hover);
        }
    }

    /// <summary>
    /// The element that is being actively pointed at (https://html.spec.whatwg.org/multipage/semantics-other.html#being-actively-pointed-at):
    /// the target of the press of a button of the pointing device, until all the buttons are released.
    /// </summary>
    internal Element? ActivelyPointedElement
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            var previous = field;
            field = value;
            OnElementStateChangedForStyles(previous, value, StyleInvalidationStates.Active);
        }
    }
}

/// <summary>The user action pseudo-classes that depend on the pointing device: :hover and :active.</summary>
internal static class UserActionStates
{
    /// <summary>
    /// Updates the state of the pointing device before a trusted pointer or mouse event is dispatched (as in browsers, the
    /// listeners of the event observe the new state): the target of the event is designated by the pointer (for the boundary
    /// events that leave an element, the related target is), a press makes it actively pointed at, and the release of all
    /// the buttons ends it.
    /// </summary>
    public static void BeforeDispatch(MouseEvent @event, Node target)
    {
        var document = target.NodeDocument;

        // The scroll navigation control (a pseudo-element of the target) at the point of the event
        ScrollControlActions.BeforeDispatch(@event, target);
        switch (@event.Type)
        {
            case "pointerout" or "pointerleave" or "mouseout" or "mouseleave":
                document.DesignatedElement = GetElement(@event.RelatedTarget as Node);
                break;

            case "pointerover" or "pointerenter" or "mouseover" or "mouseenter" or "pointermove" or "mousemove":
                document.DesignatedElement = GetElement(target);
                break;

            case "pointerdown" or "mousedown":
                document.DesignatedElement = GetElement(target);
                document.ActivelyPointedElement = GetElement(target);
                break;

            case "pointerup" or "mouseup" or "pointercancel":
                document.DesignatedElement = GetElement(target);
                if (@event.Buttons == 0 || @event.Type == "pointercancel")
                {
                    document.ActivelyPointedElement = null;
                }

                break;
        }
    }

    /// <summary>The element of an event target: a text node designates its parent element in the flat tree.</summary>
    private static Element? GetElement(Node? node)
    {
        for (; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (node is Element element)
                return element;
        }

        return null;
    }

    /// <summary>
    /// Whether the element matches <c>:hover</c> (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-hover):
    /// it or one of its descendants in the flat tree is designated by the pointer, or it is the labeled control of a label
    /// that matches <c>:hover</c>. The ancestors of an element of the top layer do not match (see <see cref="IsFlatTreeInclusiveAncestor"/>).
    /// </summary>
    public static bool IsHovered(Element element)
    {
        var designated = element.NodeDocument.DesignatedElement;
        return designated is not null && !IsInSelectPopover(element, designated) && (IsFlatTreeInclusiveAncestor(element, designated) || IsLabeledControlOf(element, designated));
    }

    /// <summary>
    /// Whether the element matches <c>:active</c> (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-active):
    /// it or one of its descendants in the flat tree is being actively pointed at, or it is the labeled control of a label that
    /// matches <c>:active</c>, or it is in a formal activation state (a button activated by the space key, between the keydown
    /// and keyup events). As in browsers, the ancestors of an element in a formal activation state do not match.
    /// </summary>
    public static bool IsActive(Element element)
    {
        if (element.NodeDocument.ActivelyPointedElement is { } pointed && !IsInSelectPopover(element, pointed) && (IsFlatTreeInclusiveAncestor(element, pointed) || IsLabeledControlOf(element, pointed)))
            return true;

        return ReferenceEquals(element.NodeDocument.BrowsingContext?.Top.Document?.SpaceKeyActivationTarget, element);
    }

    /// <summary>
    /// Whether <paramref name="descendant"/> is in the showing select popover of a select element (an option, or another
    /// child that is not its button) that is <paramref name="element"/> or one of its descendants: as in Chrome, the select
    /// element and its ancestors do not match <c>:hover</c> and <c>:active</c> while the user interacts with the contents of
    /// its picker, which is rendered in the top layer.
    /// </summary>
    internal static bool IsInSelectPopover(Element element, Element descendant)
    {
        if (element.NodeDocument.OpenSelectPickerCount == 0)
            return false;

        for (Node? node = descendant; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (node.ParentNode is HtmlSelectElement { IsPickerInPopoverStack: true } select && node is Element child && Css.Cascade.StyleEngine.IsInSelectPicker(child))
                return IsFlatTreeInclusiveAncestor(element, select);
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="element"/> is <paramref name="descendant"/> or one of the ancestors that match a user action
    /// pseudo-class with it (https://drafts.csswg.org/selectors-4/#useraction-pseudos: ":hover, :active, :focus-within [...] also
    /// match on the element's flat tree ancestors, up to and including the first top layer element or the root element,
    /// whichever is encountered first").
    /// </summary>
    internal static bool IsFlatTreeInclusiveAncestor(Element element, Element descendant)
    {
        if (!descendant.IsConnected)
            return false;

        var document = descendant.NodeDocument;
        var hasTopLayer = document.TopLayerElements is { Count: > 0 };
        for (Node? node = descendant; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (ReferenceEquals(node, element))
                return true;

            if (hasTopLayer && node is Element current && document.TopLayerContains(current))
                return false;
        }

        return false;
    }

    /// <summary>Whether the element is the labeled control of a label that is a flat tree inclusive ancestor of <paramref name="descendant"/>.</summary>
    private static bool IsLabeledControlOf(Element element, Element descendant)
    {
        if (element is not HtmlElement { IsLabelable: true } || !descendant.IsConnected)
            return false;

        for (Node? node = descendant; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (node is HtmlLabelElement label && ReferenceEquals(label.Control, element))
                return true;
        }

        return false;
    }
}
