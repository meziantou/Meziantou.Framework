using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/dom.html#showpopoveroptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#showpopoveroptions"/>
[IdlInterface("ShowPopoverOptions", "https://html.spec.whatwg.org/multipage/dom.html#showpopoveroptions")]
public class ShowPopoverOptions
{
    /// <summary>
    /// Gets the element that invokes the popover, used for the invoker relationship and the anchor positioning.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-showpopoveroptions-source"/>
    [IdlMember("source", "https://html.spec.whatwg.org/multipage/dom.html#dom-showpopoveroptions-source")]
    public HtmlElement? Source { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/dom.html#togglepopoveroptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#togglepopoveroptions"/>
[IdlInterface("TogglePopoverOptions", "https://html.spec.whatwg.org/multipage/dom.html#togglepopoveroptions")]
public sealed class TogglePopoverOptions : ShowPopoverOptions
{
    /// <summary>
    /// Gets whether to force the popover to be shown (true) or hidden (false); when null the popover is toggled.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-togglepopoveroptions-force"/>
    [IdlMember("force", "https://html.spec.whatwg.org/multipage/dom.html#dom-togglepopoveroptions-force")]
    public bool? Force { get; init; }
}

/// <summary>The states of the popover attribute (https://html.spec.whatwg.org/multipage/popover.html#attr-popover).</summary>
internal enum PopoverState
{
    None,
    Auto,
    Manual,
    Hint,
}

/// <summary>A toggle task tracker (https://html.spec.whatwg.org/multipage/interaction.html#toggle-task-tracker).</summary>
internal sealed class ToggleTaskTracker(string oldState)
{
    public string OldState { get; } = oldState;

    /// <summary>Whether the task was removed from its task queue.</summary>
    public bool Removed { get; set; }

    /// <summary>Queues a task firing a toggle event, coalesced with the pending task of <paramref name="current"/> (the toggle event task steps of popovers and dialogs).</summary>
    public static ToggleTaskTracker Queue(Element element, ToggleTaskTracker? current, Action<ToggleTaskTracker?> setTracker, string oldState, string newState, Element? source)
    {
        if (current is not null)
        {
            oldState = current.OldState;
            current.Removed = true;
        }

        var tracker = new ToggleTaskTracker(oldState);
        element.NodeDocument.QueueElementTask(() =>
        {
            if (tracker.Removed)
                return;

            // The task is no longer in the task queue: a toggle queued by a listener (e.g. after a microtask checkpoint) is
            // not coalesced with this event
            tracker.Removed = true;
            setTracker(null);
            element.FireEvent(new ToggleEvent("toggle", new ToggleEventInit { OldState = tracker.OldState, NewState = newState, Source = source }));
        });

        return tracker;
    }
}

/// <summary>The popover state of an HTML element (https://html.spec.whatwg.org/multipage/popover.html).</summary>
internal sealed class PopoverData
{
    /// <summary>Whether the popover visibility state is showing.</summary>
    public bool IsShowing;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-trigger</summary>
    public HtmlElement? Trigger;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-previously-focused-element</summary>
    public Element? PreviouslyFocusedElement;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-showing-or-hiding</summary>
    public bool Hiding;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-toggle-task-tracker</summary>
    public ToggleTaskTracker? ToggleTaskTracker;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#opened-in-popover-mode (<see cref="PopoverState.None"/> for null).</summary>
    public PopoverState OpenedInPopoverMode;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-close-watcher</summary>
    public InternalCloseWatcher? CloseWatcher;
}

// The popover API (https://html.spec.whatwg.org/multipage/popover.html). The popovers are never rendered: there is no
// anchor positioning. The close requests (Escape) and the light dismiss (LightDismiss.cs) run for the trusted events that
// simulate the user input.
public partial class HtmlElement
{
    private protected PopoverData? PopoverDataOrNull => (RareDataOrNull as ElementRareData)?.Popover;

    private protected PopoverData PopoverData => ElementRareData.Popover ??= new PopoverData();

    /// <summary>Whether the popover visibility state is showing (https://html.spec.whatwg.org/multipage/popover.html#popover-visibility-state).</summary>
    internal bool IsPopoverShowing => PopoverDataOrNull?.IsShowing == true;

    /// <summary>The popover trigger (https://html.spec.whatwg.org/multipage/popover.html#popover-trigger) of a showing popover.</summary>
    internal HtmlElement? PopoverTrigger => PopoverDataOrNull?.Trigger;

    /// <summary>
    /// The implicit anchor element of the element (https://drafts.csswg.org/css-anchor-position-1/#implicit): the source of a showing
    /// popover ("Set element's implicit anchor element to source", https://html.spec.whatwg.org/multipage/popover.html#show-popover;
    /// set to null when it is hidden), or null.
    /// </summary>
    internal HtmlElement? ImplicitAnchorElement => PopoverDataOrNull is { IsShowing: true } data ? data.Trigger : null;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#opened-in-popover-mode</summary>
    internal PopoverState OpenedInPopoverMode => PopoverDataOrNull?.OpenedInPopoverMode ?? PopoverState.None;

    /// <summary>The state of the popover attribute.</summary>
    internal PopoverState PopoverAttributeState => GetPopoverState(GetAttributeValue("popover"));

    private static PopoverState GetPopoverState(string? value)
    {
        var table = EnumeratedAttributes.Popover;
        var state = table.GetState(value);
        if (state == table.Keywords["auto"])
            return PopoverState.Auto;

        if (state == table.Keywords["manual"])
            return PopoverState.Manual;

        if (state == table.Keywords["hint"])
            return PopoverState.Hint;

        return PopoverState.None;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#dom-showpopover</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/popover.html#dom-showpopover"/>
    [IdlMember("showPopover", "https://html.spec.whatwg.org/multipage/popover.html#dom-showpopover")]
    public void ShowPopover(ShowPopoverOptions? options = null) => ShowPopover(throwExceptions: true, options?.Source);

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#dom-hidepopover</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/popover.html#dom-hidepopover"/>
    [IdlMember("hidePopover", "https://html.spec.whatwg.org/multipage/popover.html#dom-hidepopover")]
    public void HidePopover() => HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: true, source: null);

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#dom-togglepopover</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/popover.html#dom-togglepopover"/>
    [IdlMember("togglePopover", "https://html.spec.whatwg.org/multipage/popover.html#dom-togglepopover")]
    public bool TogglePopover(TogglePopoverOptions? options = null) => TogglePopover(options?.Force, options?.Source);

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#dom-togglepopover</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/popover.html#dom-togglepopover"/>
    [IdlMember("togglePopover", "https://html.spec.whatwg.org/multipage/popover.html#dom-togglepopover")]
    public bool TogglePopover(bool force) => TogglePopover((bool?)force, source: null);

    private bool TogglePopover(bool? force, HtmlElement? source)
    {
        if (IsPopoverShowing && force is null or false)
        {
            HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: true, source: null);
        }
        else if (force is null or true)
        {
            ShowPopover(throwExceptions: true, source);
        }
        else
        {
            var expectedToBeShowing = IsPopoverShowing;
            if (!CheckPopoverValidity(expectedToBeShowing, expectedDocument: null, forceHide: false, out var exception) && exception is not null)
                throw exception;
        }

        return IsPopoverShowing;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/popover.html#check-popover-validity: returns false when the result is false or
    /// an exception, in which case <paramref name="exception"/> is the exception.
    /// </summary>
    /// <param name="forceHide">
    /// Whether the popover attribute check is skipped. The attribute change steps run once the popover attribute is removed:
    /// as in browsers, they hide the popover, which the check would otherwise prevent.
    /// </param>
    internal bool CheckPopoverValidity(bool expectedToBeShowing, Document? expectedDocument, bool forceHide, out DomException? exception)
    {
        exception = null;
        if (!forceHide && PopoverAttributeState == PopoverState.None)
        {
            exception = DomException.NotSupported("The element is not a popover");
            return false;
        }

        if (IsPopoverShowing != expectedToBeShowing)
            return false;

        // A showing popover can be hidden once it is disconnected or its document is no longer fully active
        if (!expectedToBeShowing && (!IsConnected || !NodeDocument.IsFullyActive))
        {
            exception = DomException.InvalidState("The popover is not connected to a fully active document");
            return false;
        }

        if (expectedDocument is not null && !ReferenceEquals(NodeDocument, expectedDocument))
        {
            exception = DomException.InvalidState("The popover was moved to another document");
            return false;
        }

        if (this is HtmlDialogElement { IsModal: true })
        {
            exception = DomException.InvalidState("The element is a modal dialog");
            return false;
        }

        if (HasFullscreenFlag)
        {
            exception = DomException.InvalidState("The element is displayed fullscreen");
            return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#show-popover</summary>
    internal void ShowPopover(bool throwExceptions, HtmlElement? source)
    {
        var document = NodeDocument;
        if (document.ShowingPopover || document.HidingPopoverNestingCount != 0)
        {
            if (throwExceptions)
                throw DomException.InvalidState("A popover is already being shown or hidden");

            return;
        }

        if (!CheckPopoverValidity(expectedToBeShowing: false, expectedDocument: null, forceHide: false, out var exception))
        {
            if (throwExceptions && exception is not null)
                throw exception;

            return;
        }

        document.ShowingPopover = true;
        try
        {
            if (!FireEvent(new ToggleEvent("beforetoggle", new ToggleEventInit { Cancelable = true, OldState = "closed", NewState = "open", Source = source })))
                return;

            if (!CheckPopoverValidity(expectedToBeShowing: false, document, forceHide: false, out exception))
            {
                if (throwExceptions && exception is not null)
                    throw exception;

                return;
            }

            var shouldRestoreFocus = false;
            var originalType = PopoverAttributeState;
            var effectiveType = originalType;
            HtmlElement? ancestor = null;
            if (originalType is PopoverState.Auto or PopoverState.Hint)
            {
                ancestor = GetTopmostPopoverAncestor(this, source, isPopover: true);
                if (ancestor is not null && ancestor.OpenedInPopoverMode == PopoverState.Hint && effectiveType == PopoverState.Auto)
                {
                    effectiveType = PopoverState.Hint;
                }

                document.HidePopoverStackUntil(ancestor, PopoverState.Hint, focusPreviousElement: false, fireEvents: true);
                if (effectiveType == PopoverState.Auto)
                {
                    document.HidePopoverStackUntil(ancestor, PopoverState.Auto, focusPreviousElement: false, fireEvents: true);
                }

                // The event handlers may have changed the type of the popover or disconnected it
                if (originalType != PopoverAttributeState)
                {
                    if (throwExceptions)
                        throw DomException.InvalidState("The type of the popover changed while other popovers were hidden");

                    return;
                }

                if (!CheckPopoverValidity(expectedToBeShowing: false, document, forceHide: false, out exception))
                {
                    if (throwExceptions && exception is not null)
                        throw exception;

                    return;
                }

                // The focus is returned to the previously focused element only for the first popover of a stack
                if (document.GetShowingPopoverList(PopoverState.Auto).Count == 0 && document.GetShowingPopoverList(PopoverState.Hint).Count == 0)
                {
                    shouldRestoreFocus = true;
                }

                PopoverData.OpenedInPopoverMode = effectiveType;
            }

            PopoverData.PreviouslyFocusedElement = null;
            var originallyFocusedElement = document.FocusedArea;
            document.AddToTopLayer(this);
            if (effectiveType == PopoverState.Hint && ancestor?.OpenedInPopoverMode == PopoverState.Auto)
            {
                document.HintStackParent = ancestor;
            }

            var data = PopoverData;
            data.IsShowing = true;
            if (effectiveType is PopoverState.Auto or PopoverState.Hint)
            {
                // The popover close watcher (https://html.spec.whatwg.org/multipage/popover.html#popover-close-watcher):
                // a close request hides the popover (the documents without window get no close request)
                if (document.RelevantWindow is { } window && document.IsFullyActive)
                {
                    data.CloseWatcher = InternalCloseWatcher.Establish(
                        window,
                        cancelAction: static _ => true,
                        closeAction: () => HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: false, source: null),
                        getEnabledState: static () => true);
                }
            }

            NodeDocument.IncrementElementStateVersion();
            data.Trigger = source;
            FocusModel.RunPopoverFocusingSteps(this);
            if (shouldRestoreFocus && PopoverAttributeState != PopoverState.None)
            {
                data.PreviouslyFocusedElement = originallyFocusedElement;
            }
        }
        finally
        {
            document.ShowingPopover = false;
        }

        QueuePopoverToggleEventTask("closed", "open", source);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#hide-popover-algorithm</summary>
    internal void HidePopover(bool focusPreviousElement, bool fireEvents, bool throwExceptions, HtmlElement? source, bool forceHide = false)
    {
        // The select popover of a select element is hidden with the popovers of the stack
        if (this is HtmlSelectElement { IsPickerInPopoverStack: true } select)
        {
            select.ClosePicker(focusPreviousElement, focusVisible: focusPreviousElement ? true : null);
            return;
        }

        if (!CheckPopoverValidity(expectedToBeShowing: true, expectedDocument: null, forceHide, out var exception))
        {
            if (throwExceptions && exception is not null)
                throw exception;

            return;
        }

        var document = NodeDocument;
        var data = PopoverData;
        var nestedHide = data.Hiding;
        data.Hiding = true;
        if (nestedHide)
        {
            fireEvents = false;
        }

        document.HidingPopoverNestingCount++;
        try
        {
            var autoPopoverListContainsElement = document.GetShowingPopoverList(PopoverState.Auto).Contains(this);
            var hintPopoverListContainsElement = document.GetShowingPopoverList(PopoverState.Hint).Contains(this);
            if (data.OpenedInPopoverMode is PopoverState.Auto or PopoverState.Hint)
            {
                if (hintPopoverListContainsElement)
                {
                    document.HidePopoverStackUntil(this, PopoverState.Hint, focusPreviousElement, fireEvents);
                }

                if (ReferenceEquals(this, document.HintStackParent))
                {
                    document.HidePopoverStackUntil(endpoint: null, PopoverState.Hint, focusPreviousElement, fireEvents);
                }

                if (autoPopoverListContainsElement)
                {
                    document.HidePopoverStackUntil(this, PopoverState.Auto, focusPreviousElement, fireEvents);
                }

                if (!CheckPopoverValidity(expectedToBeShowing: true, document, forceHide, out exception))
                {
                    if (throwExceptions && exception is not null)
                        throw exception;

                    return;
                }
            }

            if (fireEvents)
            {
                FireEvent(new ToggleEvent("beforetoggle", new ToggleEventInit { OldState = "open", NewState = "closed", Source = source }));
                if (!CheckPopoverValidity(expectedToBeShowing: true, document, forceHide, out exception))
                {
                    if (throwExceptions && exception is not null)
                        throw exception;

                    return;
                }
            }

            document.RequestTopLayerRemoval(this);
            data.Trigger = null;
            data.OpenedInPopoverMode = PopoverState.None;
            data.IsShowing = false;

            NodeDocument.IncrementElementStateVersion();
            if (ReferenceEquals(this, document.HintStackParent) || document.GetShowingPopoverList(PopoverState.Hint).Count == 0)
            {
                document.HintStackParent = null;
            }

            if (fireEvents)
            {
                QueuePopoverToggleEventTask("open", "closed", source);
            }

            if (data.PreviouslyFocusedElement is { } previouslyFocusedElement)
            {
                data.PreviouslyFocusedElement = null;
                if (focusPreviousElement && document.FocusedArea is { } focusedArea && IsShadowIncludingInclusiveAncestorOf(focusedArea))
                {
                    FocusModel.Focus(previouslyFocusedElement);
                }
            }
        }
        finally
        {
            if (!nestedHide)
            {
                data.Hiding = false;
            }

            // A popover that an event handler showed again (e.g. in another document) keeps the close watcher of that show
            if (!data.IsShowing && data.CloseWatcher is { } closeWatcher)
            {
                closeWatcher.Destroy();
                data.CloseWatcher = null;
            }

            document.HidingPopoverNestingCount--;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#queue-a-popover-toggle-event-task</summary>
    private void QueuePopoverToggleEventTask(string oldState, string newState, Element? source)
    {
        var data = PopoverData;
        ToggleTaskTracker? queued = null;
        queued = ToggleTaskTracker.Queue(this, data.ToggleTaskTracker, tracker =>
        {
            // The tracker may have been forgotten (the popover attribute was removed) and replaced by the one of a newer task
            if (ReferenceEquals(data.ToggleTaskTracker, queued))
            {
                data.ToggleTaskTracker = tracker;
            }
        }, oldState, newState, source);
        data.ToggleTaskTracker = queued;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#topmost-popover-ancestor</summary>
    internal static HtmlElement? GetTopmostPopoverAncestor(Node newPopoverOrTopLayerElement, HtmlElement? source, bool isPopover)
    {
        _ = isPopover;
        var document = newPopoverOrTopLayerElement.NodeDocument;
        var combinedPopovers = document.GetShowingPopoverList(PopoverState.Auto);
        combinedPopovers.AddRange(document.GetShowingPopoverList(PopoverState.Hint));

        var popoverAncestorIndex = GetLastFlatTreeAncestorIndex(combinedPopovers, newPopoverOrTopLayerElement, inclusive: false);
        // The spec checks whether source is a flat tree descendant of the popovers, but the browsers (and the WPT tests) consider
        // the inclusive ancestors of source, so that a showing popover passed as source is the ancestor of the new popover
        var sourceAncestorIndex = source is null ? -1 : GetLastFlatTreeAncestorIndex(combinedPopovers, source, inclusive: true);
        var ancestorIndex = Math.Max(popoverAncestorIndex, sourceAncestorIndex);
        return ancestorIndex < 0 ? null : combinedPopovers[ancestorIndex];

        static int GetLastFlatTreeAncestorIndex(List<HtmlElement> popovers, Node node, bool inclusive)
        {
            for (var i = popovers.Count - 1; i >= 0; i--)
            {
                if ((inclusive && ReferenceEquals(node, popovers[i])) || IsFlatTreeDescendant(node, popovers[i]))
                    return i;
            }

            return -1;
        }
    }

    /// <summary>Whether <paramref name="node"/> is a flat tree descendant of <paramref name="ancestor"/> (https://drafts.csswg.org/css-scoping/#flat-tree).</summary>
    private protected static bool IsFlatTreeDescendant(Node node, Node ancestor)
    {
        for (var current = GetFlatTreeParent(node); current is not null; current = GetFlatTreeParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>The children of the element in the flat tree (https://drafts.csswg.org/css-scoping/#flat-tree): the shadow tree of a shadow host, the assigned nodes of a slot (or its fallback content).</summary>
    internal static NodeEnumerable GetFlatTreeChildren(Element element)
    {
        if (element.AttachedShadowRoot is { } shadowRoot)
            return shadowRoot.EnumerateChildNodes();

        if (element is HtmlSlotElement slot && slot.AssignedNodesList.Count > 0)
            return new NodeEnumerable(slot.AssignedNodesList);

        return element.EnumerateChildNodes();
    }

    /// <summary>The parent of a node in the flat tree (https://drafts.csswg.org/css-scoping/#flat-tree).</summary>
    internal static Node? GetFlatTreeParent(Node node)
    {
        if (node.Slottable?.AssignedSlot is { } slot)
            return slot;

        var parent = node.ParentNode;
        if (parent is ShadowRoot shadowRoot)
            return shadowRoot.Host;

        // The children of a shadow host that are not assigned to a slot are not in the flat tree
        if (parent is Element { AttachedShadowRoot: not null })
            return null;

        return parent;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-target-element</summary>
    internal static HtmlElement? GetPopoverTargetElement(HtmlElement node)
    {
        // A button: a button element, or an input element whose type is Submit Button, Reset Button, Image Button or Button
        var isButton = node is HtmlButtonElement || (node is HtmlInputElement input && input.InputType is InputType.Submit or InputType.Reset or InputType.Image or InputType.Button);
        if (!isButton || Css.Selectors.HtmlElementStates.IsActuallyDisabled(node) || (node.FormOwner is not null && node.IsSubmitButton()))
            return null;

        return Reflection.GetElement<Element>(node, "popovertarget") is HtmlElement popover && popover.PopoverAttributeState != PopoverState.None ? popover : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-target-attribute-activation-behavior</summary>
    internal static void RunPopoverTargetAttributeActivationBehavior(HtmlElement node, EventTarget? eventTarget)
    {
        var popover = GetPopoverTargetElement(node);
        if (popover is null)
            return;

        if (eventTarget is Node target && popover.IsShadowIncludingInclusiveAncestorOf(target) && !ReferenceEquals(node, popover) && node.IsShadowIncludingInclusiveAncestorOf(popover))
            return;

        var table = EnumeratedAttributes.Popovertargetaction;
        var action = table.GetState(node.GetAttributeValue("popovertargetaction"));
        if (action == table.Keywords["show"] && popover.IsPopoverShowing)
            return;

        if (action == table.Keywords["hide"] && !popover.IsPopoverShowing)
            return;

        if (popover.IsPopoverShowing)
        {
            popover.HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: false, node);
        }
        else if (popover.CheckPopoverValidity(expectedToBeShowing: false, expectedDocument: null, forceHide: false, out _))
        {
            popover.ShowPopover(throwExceptions: false, node);
        }
    }

    /// <summary>The attribute change steps of the popover attribute (https://html.spec.whatwg.org/multipage/popover.html#the-popover-attribute).</summary>
    private void PopoverAttributeChangeSteps(string? oldValue, string? value)
    {
        if (IsPopoverShowing && GetPopoverState(oldValue) != GetPopoverState(value))
        {
            HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: false, source: null, forceHide: GetPopoverState(value) == PopoverState.None);
        }

        // Not in the spec, but as in Blink (which drops the popover data of the element): removing the popover attribute forgets
        // the pending toggle event task (it still fires), so that the toggle event of a next show is not coalesced with it
        // (e.g. the attribute removed by a focus listener during the popover focusing steps of showPopover())
        if (GetPopoverState(value) == PopoverState.None && PopoverDataOrNull is { } data)
        {
            data.ToggleTaskTracker = null;
        }
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is null && localName == "popover")
        {
            PopoverAttributeChangeSteps(oldValue, value);
        }
        else if (@namespace is null && localName is "form" or "disabled")
        {
            // https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#association-of-controls-and-forms: when the form
            // attribute of a listed element is set, changed or removed, its form owner is reset
            if (localName == "form" && IsListed)
            {
                ResetFormOwner();
            }

            CustomElementAttributeChangeSteps(localName);
        }
    }

    /// <summary>The removing steps of the HTML standard (https://html.spec.whatwg.org/multipage/infrastructure.html#html-element-removing-steps).</summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        OnFormAssociatedElementRemoved();
        OnFormAssociatedCustomElementTreeChanged();
        OnLegendRemoved(isSubtreeRoot, oldAncestor);

        // The hide popover algorithm does nothing when the popover is not showing
        if (IsPopoverShowing && PopoverAttributeState != PopoverState.None)
        {
            HidePopover(focusPreviousElement: false, fireEvents: false, throwExceptions: false, source: null, forceHide: true);
        }
    }
}

public partial class Document
{
    private List<Element>? _topLayer;
    private HashSet<Element>? _pendingTopLayerRemovals;

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#hiding-popover-nesting-count</summary>
    internal int HidingPopoverNestingCount { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#document-showing-popover</summary>
    internal bool ShowingPopover { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#hint-stack-parent</summary>
    internal HtmlElement? HintStackParent { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#open-dialogs-list</summary>
    internal List<HtmlDialogElement> OpenDialogs => _openDialogs ??= [];

    private List<HtmlDialogElement>? _openDialogs;

    /// <summary>The select elements of the document whose picker is open.</summary>
    internal List<HtmlSelectElement> OpenSelectPickers => _openSelectPickers ??= [];

    private List<HtmlSelectElement>? _openSelectPickers;

    internal int OpenSelectPickerCount => _openSelectPickers?.Count ?? 0;

    /// <summary>Closes the pickers of the select elements whose appearance changed since they were opened (see <see cref="HtmlSelectElement.ClosePickerIfAppearanceChanged"/>).</summary>
    internal void CheckOpenSelectPickers()
    {
        if (_openSelectPickers is not { Count: > 0 } pickers)
            return;

        foreach (var select in pickers.ToArray())
        {
            select.ClosePickerIfAppearanceChanged();
        }
    }

    /// <summary>
    /// The elements in the top layer (https://drafts.csswg.org/css-position-4/#in-the-top-layer), in order, or null when it was
    /// never used: the elements of the top layer that are not pending removal.
    /// </summary>
    internal IReadOnlyList<Element>? TopLayerElements => _pendingTopLayerRemovals is { Count: > 0 } pending ? _topLayer!.FindAll(element => !pending.Contains(element)) : _topLayer;

    /// <summary>
    /// The elements of the top layer (https://drafts.csswg.org/css-position-4/#document-top-layer), in order, including the ones
    /// pending removal (their <c>overlay</c> may still be <c>auto</c> while it transitions), or null when it was never used: the
    /// ones whose computed <c>overlay</c> is <c>auto</c> are rendered in the top layer (see <see cref="IsRenderedInTopLayer(Element)"/>).
    /// </summary>
    internal IReadOnlyList<Element>? TopLayerElementsForRendering => _topLayer;

    /// <summary>https://drafts.csswg.org/css-position-4/#in-the-top-layer</summary>
    /// <remarks>False for a select element whose entry in the top layer is its picker (see <see cref="IsTopLayerPicker"/>).</remarks>
    internal bool TopLayerContains(Element element) => HasOverlayAutoRule(element) && !IsTopLayerPicker(element);

    /// <summary>
    /// Whether the user agent <c>!important</c> rule <c>overlay: auto</c> targets the element: it is in the top layer and not
    /// pending removal (https://drafts.csswg.org/css-position-4/#add-an-element-to-the-top-layer).
    /// </summary>
    internal bool HasOverlayAutoRule(Element element) => _topLayer?.Contains(element) == true && _pendingTopLayerRemovals?.Contains(element) != true;

    /// <summary>
    /// https://drafts.csswg.org/css-position-4/#rendered-in-the-top-layer: the element is in the top layer (possibly pending
    /// removal) and its computed <c>overlay</c> is <c>auto</c>.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="overlay">The computed value of <c>overlay</c> of the element.</param>
    internal bool IsRenderedInTopLayer(Element element, string overlay) => overlay == "auto" && _topLayer?.Contains(element) == true;

    /// <summary>
    /// Whether the boxes of an element are rendered in the top layer (https://drafts.csswg.org/css-position-4/#rendered-in-the-top-layer),
    /// as children of the viewport: a select element whose entry is its picker is not.
    /// </summary>
    internal bool IsRenderedInTopLayer(Element element)
        => _topLayer?.Contains(element) == true && !IsTopLayerPicker(element) && GetStyleEngine().GetStyle(element).GetComputedValue("overlay") == "auto";

    /// <summary>
    /// Whether the entry of the element in the top layer is the select popover of a select element whose picker is open
    /// (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2): the box of its <c>::picker(select)</c>
    /// pseudo-element is rendered in the top layer, not the select element.
    /// </summary>
    internal static bool IsTopLayerPicker(Element element) => element is HtmlSelectElement { IsPickerInPopoverStack: true };

    /// <summary>https://drafts.csswg.org/css-position-4/#add-an-element-to-the-top-layer</summary>
    internal void AddToTopLayer(Element element)
    {
        _topLayer ??= [];
        _topLayer.Remove(element);
        _pendingTopLayerRemovals?.Remove(element);
        _topLayer.Add(element);

        // The computed position, display and overlay of an element of the top layer change, and its boxes are children of the
        // viewport box
        IncrementElementStateVersion();
        InvalidateLayout(rebuildBoxTree: true);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-position-4/#request-an-element-to-be-removed-from-the-top-layer: the element stays in the top
    /// layer, pending removal, without the <c>overlay: auto</c> rule: it is rendered in the top layer while its <c>overlay</c>
    /// transitions to <c>none</c>, and removed by the next rendering update after (<see cref="ProcessTopLayerRemovals"/>).
    /// </summary>
    internal void RequestTopLayerRemoval(Element element)
    {
        if (_topLayer?.Contains(element) != true || _pendingTopLayerRemovals?.Contains(element) == true)
            return;

        (_pendingTopLayerRemovals ??= new(ReferenceEqualityComparer.Instance)).Add(element);
        IncrementElementStateVersion();
        InvalidateLayout(rebuildBoxTree: true);
        EventLoop?.RequestRenderingOpportunity(this);
    }

    /// <summary>https://drafts.csswg.org/css-position-4/#remove-an-element-from-the-top-layer-immediately</summary>
    internal void RemoveFromTopLayer(Element element)
    {
        _pendingTopLayerRemovals?.Remove(element);
        if (_topLayer?.Remove(element) == true)
        {
            IncrementElementStateVersion();
            InvalidateLayout(rebuildBoxTree: true);
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-position-4/#process-top-layer-removals (in the rendering update, after the styles are
    /// recalculated): the elements pending removal whose computed <c>overlay</c> is <c>none</c> (e.g. its transition ended), or
    /// that are not rendered, are removed from the top layer.
    /// </summary>
    internal void ProcessTopLayerRemovals()
    {
        if (_pendingTopLayerRemovals is not { Count: > 0 } pending)
            return;

        foreach (var element in _topLayer!.ToArray())
        {
            if (pending.Contains(element) && (!Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(element) || GetStyleEngine().GetStyle(element).GetComputedValue("overlay") == "none"))
            {
                RemoveFromTopLayer(element);
            }
        }

        // The elements whose overlay still transitions are checked again by the next rendering updates
        if (pending.Count > 0)
        {
            EventLoop?.RequestRenderingOpportunity(this);
        }
    }

    /// <summary>
    /// The showing auto popover list (https://html.spec.whatwg.org/multipage/popover.html#auto-popover-list) or the showing
    /// hint popover list (https://html.spec.whatwg.org/multipage/popover.html#showing-hint-popover-list).
    /// </summary>
    internal List<HtmlElement> GetShowingPopoverList(PopoverState mode)
    {
        var popovers = new List<HtmlElement>();
        if (_topLayer is null)
            return popovers;

        foreach (var element in _topLayer)
        {
            if (element is HtmlElement htmlElement && htmlElement.OpenedInPopoverMode == mode && htmlElement.IsPopoverShowing)
            {
                popovers.Add(htmlElement);
            }
        }

        return popovers;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#hide-popovers-until</summary>
    internal void HidePopoversUntil(HtmlElement? endpoint, bool focusPreviousElement, bool fireEvents)
    {
        var endpointIsHint = endpoint is not null && GetShowingPopoverList(PopoverState.Hint).Contains(endpoint);
        HidePopoverStackUntil(endpoint, PopoverState.Hint, focusPreviousElement, fireEvents);
        var autoEndpoint = endpointIsHint ? HintStackParent : endpoint;
        HidePopoverStackUntil(autoEndpoint, PopoverState.Auto, focusPreviousElement, fireEvents);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#hide-popover-stack-until</summary>
    internal void HidePopoverStackUntil(HtmlElement? endpoint, PopoverState stackType, bool focusPreviousElement, bool fireEvents)
    {
        var popoverList = GetShowingPopoverList(stackType);
        var lastHideIndex = endpoint is null ? 0 : popoverList.IndexOf(endpoint) + 1;
        for (var i = popoverList.Count - 1; i >= lastHideIndex; i--)
        {
            popoverList[i].HidePopover(focusPreviousElement, fireEvents, throwExceptions: false, source: null);
        }

        // The event handlers may have shown other popovers: hide them without firing events
        var toRemain = popoverList.GetRange(0, lastHideIndex);
        var newPopoverList = GetShowingPopoverList(stackType);
        for (var i = newPopoverList.Count - 1; i >= 0; i--)
        {
            var popover = newPopoverList[i];
            if (toRemain.Contains(popover))
                continue;

            popover.HidePopover(focusPreviousElement, fireEvents: false, throwExceptions: false, source: null);
        }
    }
}
