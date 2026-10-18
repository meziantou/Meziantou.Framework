using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The picker of a select element rendered as a drop-down box (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element).
// With base appearance (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2), the picker is the select
// popover, a popover of the shadow tree of the select element: the library has no shadow tree of the user agent, so the
// select element stands for its select popover in the top layer, in the showing auto popover list and in the close
// watchers of its window while its picker is open (the box of the popover is the box of its ::picker(select)
// pseudo-element). The toggle events of the select popover, which no script can observe, are not fired. The native picker
// of a select element with native appearance is not rendered, but it has the same state.
public partial class HtmlSelectElement
{
    /// <summary>
    /// Whether the picker of the select element is open (it matches <c>:open</c>, https://html.spec.whatwg.org/multipage/semantics-other.html#selector-open):
    /// the select popover is showing when the picker has base appearance, the native picker (which is not rendered) otherwise.
    /// </summary>
    internal bool IsPickerOpen { get; private set; }

    /// <summary>Whether the select element stands for its select popover in the top layer and in the showing auto popover list of its document.</summary>
    internal bool IsPickerInPopoverStack { get; private set; }

    /// <summary>
    /// Opens the picker (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element: "Run the show popover
    /// algorithm given select's select popover, false, and select"): the popovers that are not ancestors of the select element
    /// are hidden, and, as in Chrome, the selected option is focused when the options are rendered.
    /// </summary>
    /// <param name="focusVisible">Whether the focus of the option is indicated: false for the pointing device, true for the keyboard, null for the heuristic of the scripts.</param>
    internal void OpenPicker(bool? focusVisible)
    {
        var document = NodeDocument;
        if (IsPickerOpen || !IsConnected || !document.IsFullyActive || !StyleEngine.IsDropDownBox(this) || HtmlElementStates.IsActuallyDisabled(this))
            return;

        _isPickerRenderedWhenOpened = IsSelectPopoverRendered();

        // A select element that is a showing popover cannot stand for its select popover too
        if (!IsPopoverShowing && !document.ShowingPopover && document.HidingPopoverNestingCount == 0)
        {
            document.ShowingPopover = true;
            try
            {
                var ancestor = GetTopmostPopoverAncestor(this, this, isPopover: true);
                document.HidePopoverStackUntil(ancestor, PopoverState.Hint, focusPreviousElement: false, fireEvents: true);
                document.HidePopoverStackUntil(ancestor, PopoverState.Auto, focusPreviousElement: false, fireEvents: true);

                // The event handlers may have opened the picker, or disconnected the select element
                if (IsPickerOpen || IsPopoverShowing || !IsConnected || !ReferenceEquals(document, NodeDocument))
                    return;

                var data = PopoverData;
                data.OpenedInPopoverMode = PopoverState.Auto;
                data.PreviouslyFocusedElement = null;
                data.Trigger = null;
                IsPickerOpen = true;
                IsPickerInPopoverStack = true;
                document.AddToTopLayer(this);
                data.IsShowing = true;

                // The close watcher of the select popover (https://html.spec.whatwg.org/multipage/popover.html#popover-close-watcher):
                // a close request closes the picker and focuses the select element
                if (document.RelevantWindow is { } window)
                {
                    data.CloseWatcher = InternalCloseWatcher.Establish(
                        window,
                        cancelAction: static _ => true,
                        closeAction: () => HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: false, source: null),
                        getEnabledState: static () => true);
                }
            }
            finally
            {
                document.ShowingPopover = false;
            }
        }

        IsPickerOpen = true;
        document.OpenSelectPickers.Add(this);
        document.IncrementElementStateVersion();
        document.InvalidateLayout(rebuildBoxTree: true);

        // The style of the open picker (:open) may give it another appearance: the picker of that appearance was not opened
        if (IsSelectPopoverRendered() != _isPickerRenderedWhenOpened)
        {
            ClosePicker(focusSelect: false);
            return;
        }

        FocusSelectedOption(focusVisible);
    }

    private bool _isPickerRenderedWhenOpened;

    /// <summary>Whether the picker of the select element is its select popover: the select element and its picker have base appearance.</summary>
    private bool IsSelectPopoverRendered()
    {
        if (!Css.Cascade.ComputedStyleDeclarations.IsRendered(this))
            return false;

        var engine = NodeDocument.GetStyleEngine();
        return engine.IsSelectPickerRendered(engine.GetStyle(this));
    }

    /// <summary>
    /// Closes the picker when its appearance changed since it was opened (the select popover replaces the native picker, or
    /// the reverse), as in Chrome: the picker of the other appearance is not opened. Runs when the rendering is updated.
    /// </summary>
    internal void ClosePickerIfAppearanceChanged()
    {
        if (IsPickerOpen && IsSelectPopoverRendered() != _isPickerRenderedWhenOpened)
        {
            ClosePicker(focusSelect: false);
        }
    }

    /// <summary>
    /// Closes the picker (the hide popover algorithm of the select popover): the popovers shown from the picker are hidden
    /// first, and the select element is focused again when the focus was in the picker.
    /// </summary>
    /// <param name="focusSelect">Whether the select element is focused again when the focus is in the picker.</param>
    /// <param name="focusVisible">Whether the focus of the select element is then indicated (see <see cref="OpenPicker"/>).</param>
    internal void ClosePicker(bool focusSelect, bool? focusVisible = null)
    {
        if (!IsPickerOpen)
            return;

        var document = NodeDocument;
        IsPickerOpen = false;
        document.OpenSelectPickers.Remove(this);
        if (IsPickerInPopoverStack)
        {
            // The popovers that are after the select popover in the stacks
            document.HidingPopoverNestingCount++;
            try
            {
                if (document.GetShowingPopoverList(PopoverState.Auto).Contains(this))
                {
                    document.HidePopoverStackUntil(this, PopoverState.Hint, focusPreviousElement: false, fireEvents: true);
                    document.HidePopoverStackUntil(this, PopoverState.Auto, focusPreviousElement: false, fireEvents: true);
                }
            }
            finally
            {
                document.HidingPopoverNestingCount--;
            }

            IsPickerInPopoverStack = false;
            document.RemoveFromTopLayer(this);
            if (PopoverDataOrNull is { } data)
            {
                data.CloseWatcher?.Destroy();
                data.CloseWatcher = null;
                data.OpenedInPopoverMode = PopoverState.None;
                data.IsShowing = false;
            }
        }

        document.IncrementElementStateVersion();
        document.InvalidateLayout(rebuildBoxTree: true);
        if (FocusModel.GetFocusedElement(document) is { } focused && !ReferenceEquals(focused, this) && Contains(focused))
        {
            if (focusSelect && IsConnected)
            {
                FocusModel.Focus(this, focusVisible);
            }
            else
            {
                // The focused option is not rendered any more
                FocusModel.RunFocusFixupRule(document);
            }
        }
    }

    /// <summary>The first selected option of the list of options, or null.</summary>
    internal HtmlOptionElement? GetSelectedOption()
    {
        foreach (var option in GetListOfOptions())
        {
            if (option.Selectedness)
                return option;
        }

        return null;
    }

    /// <summary>
    /// Whether the contents of the select element are rendered with base appearance
    /// (https://html.spec.whatwg.org/multipage/rendering.html#select's-options-are-being-rendered-with-base-appearance): its
    /// options are boxes, which are focusable.
    /// </summary>
    internal bool AreContentsRenderedWithBaseAppearance()
    {
        if (!Css.Cascade.ComputedStyleDeclarations.IsRendered(this))
            return false;

        var engine = NodeDocument.GetStyleEngine();
        return engine.AreContentsRenderedWithBaseAppearance(engine.GetStyle(this));
    }

    /// <summary>As in Chrome, the selected option (else the first option that can be focused) is focused when the picker opens.</summary>
    private void FocusSelectedOption(bool? focusVisible)
    {
        if (!AreContentsRenderedWithBaseAppearance())
            return;

        var selected = GetSelectedOption();
        if (selected is not null && FocusModel.IsFocusableArea(selected))
        {
            FocusOption(selected, focusVisible);
            return;
        }

        foreach (var option in GetListOfOptions())
        {
            if (FocusModel.IsFocusableArea(option))
            {
                FocusOption(option, focusVisible);
                return;
            }
        }
    }

    /// <summary>Focuses an option of the picker and centers it in the select popover when it is not in view (only the contents of the popover scroll).</summary>
    private static void FocusOption(HtmlOptionElement option, bool? focusVisible)
    {
        FocusModel.Focus(option, focusVisible);
        if (option.IsConnected && option.NodeDocument.BrowsingContext is not null)
        {
            _ = Css.Layout.ScrollIntoView.Run(option.NodeDocument.GetLayoutEngine(), option, ScrollBehavior.Auto, ScrollLogicalPosition.Center, ScrollLogicalPosition.Nearest, container: option);
        }
    }

    /// <summary>Closes the picker when the select element cannot have one any more: it is removed, disabled, or rendered as a list box.</summary>
    internal void ClosePickerIfInvalid()
    {
        if (IsPickerOpen && (!IsConnected || !StyleEngine.IsDropDownBox(this) || HtmlElementStates.IsActuallyDisabled(this)))
        {
            ClosePicker(focusSelect: false);
        }
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        if (IsPickerOpen)
        {
            ClosePicker(focusSelect: false);
        }
    }
}
