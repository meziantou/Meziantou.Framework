using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// Light dismiss (https://html.spec.whatwg.org/multipage/popover.html#popover-light-dismiss and
// https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-light-dismiss): a click outside of the open auto
// popovers hides them, and a click outside of the topmost dialog whose closedby attribute is "any" closes it.
public partial class Document
{
    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-pointerdown-target</summary>
    private HtmlElement? _popoverPointerDownTarget;

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-pointerdown-target</summary>
    private HtmlDialogElement? _dialogPointerDownTarget;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interactive-elements.html#run-light-dismiss-activities: run for the trusted
    /// pointerdown and pointerup events (as in browsers, before they are dispatched, so that the handlers of the events
    /// cannot prevent it).
    /// </summary>
    internal void RunLightDismissActivities(PointerEvent @event, Node target)
    {
        LightDismissOpenPopovers(@event, target);
        LightDismissOpenDialogs(@event, target);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#light-dismiss-open-popovers</summary>
    private void LightDismissOpenPopovers(PointerEvent @event, Node target)
    {
        if (GetShowingPopoverList(PopoverState.Auto).Count == 0 && GetShowingPopoverList(PopoverState.Hint).Count == 0)
            return;

        if (@event.Type == "pointerdown")
        {
            _popoverPointerDownTarget = GetTopmostClickedPopover(target);
        }
        else if (@event.Type == "pointerup")
        {
            var ancestor = GetTopmostClickedPopover(target);
            var sameTarget = ReferenceEquals(ancestor, _popoverPointerDownTarget);
            _popoverPointerDownTarget = null;
            if (!sameTarget)
                return;

            HidePopoversUntil(ancestor, focusPreviousElement: false, fireEvents: true);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#topmost-clicked-popover</summary>
    private HtmlElement? GetTopmostClickedPopover(Node node)
    {
        var clickedPopover = GetNearestInclusiveOpenPopover(node);
        var targetPopover = GetNearestInclusiveTargetPopover(node);
        return GetPopoverStackPosition(clickedPopover) > GetPopoverStackPosition(targetPopover) ? clickedPopover : targetPopover;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#get-the-popover-stack-position</summary>
    private int GetPopoverStackPosition(HtmlElement? popover)
    {
        if (popover is null)
            return 0;

        var autoList = GetShowingPopoverList(PopoverState.Auto);
        var hintIndex = GetShowingPopoverList(PopoverState.Hint).IndexOf(popover);
        if (hintIndex >= 0)
            return hintIndex + autoList.Count + 1;

        return autoList.IndexOf(popover) + 1;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#nearest-inclusive-open-popover</summary>
    private static HtmlElement? GetNearestInclusiveOpenPopover(Node node)
    {
        for (Node? current = node; current is not null; current = Dom.HtmlElement.GetFlatTreeParent(current))
        {
            if (current is HtmlElement { IsPopoverShowing: true, OpenedInPopoverMode: PopoverState.Auto or PopoverState.Hint } popover)
                return popover;
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#nearest-inclusive-target-popover</summary>
    private static HtmlElement? GetNearestInclusiveTargetPopover(Node node)
    {
        for (Node? current = node; current is not null; current = Dom.HtmlElement.GetFlatTreeParent(current))
        {
            if (current is HtmlElement element && GetTargetPopover(element) is { IsPopoverShowing: true, PopoverAttributeState: PopoverState.Auto or PopoverState.Hint } targetPopover)
                return targetPopover;
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#get-the-target-popover</summary>
    private static HtmlElement? GetTargetPopover(HtmlElement node)
    {
        if (node is not HtmlButtonElement button)
            return Dom.HtmlElement.GetPopoverTargetElement(node);

        if (Css.Selectors.HtmlElementStates.IsActuallyDisabled(button))
            return null;

        if (button.CommandForElement is not { } target)
            return Dom.HtmlElement.GetPopoverTargetElement(node);

        if (button.FormOwner is not null)
        {
            var type = EnumeratedAttributes.ButtonType;
            var state = type.GetState(button.GetAttributeValue("type"));
            if (button.IsSubmitButton() || state == type.Keywords["reset"] || state == type.MissingValueDefault)
                return null;
        }

        return button.CommandState is ButtonCommand.TogglePopover or ButtonCommand.HidePopover or ButtonCommand.ShowPopover ? target as HtmlElement : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#light-dismiss-open-dialogs</summary>
    private void LightDismissOpenDialogs(PointerEvent @event, Node target)
    {
        if (OpenDialogs.Count == 0)
            return;

        var ancestor = GetNearestClickedDialog(@event, target);
        if (@event.Type == "pointerdown")
        {
            _dialogPointerDownTarget = ancestor;
        }
        else if (@event.Type == "pointerup")
        {
            var sameTarget = ReferenceEquals(ancestor, _dialogPointerDownTarget);
            _dialogPointerDownTarget = null;
            if (!sameTarget)
                return;

            var topmostDialog = OpenDialogs[^1];
            if (ReferenceEquals(ancestor, topmostDialog))
                return;

            topmostDialog.LightDismiss();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interactive-elements.html#nearest-clicked-dialog: the nearest open dialog in the
    /// flat tree. A pointer event targeted at a modal dialog outside of its bounds (the border box of the dialog, see
    /// <see cref="Element.GetBoundingClientRect"/>) hits its <c>::backdrop</c>.
    /// </summary>
    private static HtmlDialogElement? GetNearestClickedDialog(PointerEvent @event, Node target)
    {
        // "If target is a dialog element, target has an open attribute, target's is modal is true, and event's clientX and
        // clientY are outside the bounds of target, then return null"
        if (target is HtmlDialogElement { IsModal: true } modalDialog && modalDialog.HasAttributeWithLocalName("open"))
        {
            var bounds = modalDialog.GetBoundingClientRect();
            if (@event.ClientX < bounds.Left || @event.ClientX > bounds.Right || @event.ClientY < bounds.Top || @event.ClientY > bounds.Bottom)
                return null;
        }

        for (Node? current = target; current is not null; current = Dom.HtmlElement.GetFlatTreeParent(current))
        {
            if (current is HtmlDialogElement dialog && dialog.HasAttributeWithLocalName("open"))
                return dialog;
        }

        return null;
    }
}
