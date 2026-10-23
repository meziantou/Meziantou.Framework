using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The user interactions with the select elements (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element),
/// run as default actions of the trusted mouse and keyboard events (see <see cref="UserAgentDefaultActions"/>). The
/// specification lets the user agent choose the interactions ("the user agent should allow the user to..."); they are the
/// ones of Chrome on Windows and Linux:
/// <list type="bullet">
/// <item>a drop-down box: a press of the primary button on the select element opens its picker, or closes it; the space key,
/// the arrow keys and Enter open it;</item>
/// <item>the picker with base appearance (the options are boxes): the release of the primary button on an option, and
/// Enter or the space key on the focused option, pick the option and close the picker; the arrow keys, Home, End, PageUp
/// and PageDown focus another option, a typed character focuses the next option whose label starts with it; Tab closes
/// the picker (Escape and a press outside of it too: the picker is a popover);</item>
/// <item>the native picker (the options are not rendered): the arrow keys, Home and End pick another option.</item>
/// </list>
/// </summary>
internal static class SelectActions
{
    /// <summary>The time after which the typed characters start a new search of the type-ahead, in milliseconds.</summary>
    private const double TypeAheadTimeout = 1000;

    /// <summary>
    /// Runs the default action of a press of the primary button that was not canceled ("If event's canceled flag is set, then
    /// return. If event is a MouseEvent and event's button attribute is not 0, then return."); returns whether the action
    /// moved the focus (the click focus is then skipped: as in Chrome, the select element is not focused before its option).
    /// </summary>
    public static bool OnMouseDown(MouseEvent @event, Node target)
    {
        if (GetSelect(target, out var option) is not { } select || HtmlElementStates.IsActuallyDisabled(select))
            return false;

        if (!StyleEngine.IsDropDownBox(select))
        {
            // A press on an option of a list box with base appearance (the click focus then focuses the option)
            if (select.AreContentsRenderedWithBaseAppearance())
            {
                if (option is not null && !option.IsDisabled)
                {
                    ToggleListBoxOption(select, option);
                }
            }
            else if (option is { IsDisabled: false } row)
            {
                // A press on a row of a list box with native appearance (the box of an option): the option is picked (the
                // other options of a multiple selection are unselected, unless the Control or Meta key toggles the option;
                // as in the browsers, a tap on a touch screen toggles it too)
                if (select.IsMultiple && (@event.CtrlKey || @event.MetaKey || @event.IsFromTouch))
                {
                    select.ToggleOption(row);
                }
                else
                {
                    SelectOnly(select, row);
                }
            }

            return false;
        }

        var document = select.NodeDocument;
        if (!select.IsPickerOpen)
        {
            select.OpenPicker(focusVisible: false);
            select.PickerPressPosition = select.IsPickerOpen ? (@event.ClientX, @event.ClientY) : null;
            return select.IsPickerOpen && FocusModel.GetFocusedElement(document) is HtmlOptionElement focused && ReferenceEquals(focused.CachedNearestAncestorSelect, select);
        }

        // A press in the picker (an option, or the box of the picker) does not close it
        if (option is null && !IsInPicker(select, @event))
        {
            select.ClosePicker(focusSelect: true, focusVisible: false);
            return ReferenceEquals(FocusModel.GetFocusedElement(document), select);
        }

        return false;
    }

    public static void OnMouseUp(MouseEvent @event, Node target, bool canceled)
    {
        if (GetSelect(target, out var option) is not { } select)
            return;

        // As in Chrome, the release of the press that opened the picker does not pick the option that the picker placed under
        // the pointer, unless the pointer moved
        var pressPosition = select.PickerPressPosition;
        select.PickerPressPosition = null;
        if (pressPosition is { } position && position.X == @event.ClientX && position.Y == @event.ClientY)
            return;

        if (option is null || !select.IsPickerOpen || !StyleEngine.IsDropDownBox(select) || !select.AreContentsRenderedWithBaseAppearance())
            return;

        // The user picks an option "through a click": the release of the button on an option of the picker
        if (!option.IsDisabled)
        {
            select.PickOption(option, @event, canceled);
        }
    }

    /// <summary>Runs the default action of a key on a select element or on an option of its picker; returns whether the key was handled.</summary>
    public static bool OnKeyDown(KeyboardEvent @event, Node target, bool canceled)
    {
        if (target is not Element element || GetSelect(element, out var option) is not { } select || HtmlElementStates.IsActuallyDisabled(select))
            return false;

        if (@event.CtrlKey || @event.MetaKey || @event.AltKey)
            return false;

        var key = GetLogicalKey(select, @event.Key);
        if (!StyleEngine.IsDropDownBox(select))
        {
            if (select.AreContentsRenderedWithBaseAppearance())
                return option is not null && ReferenceEquals(option, target) && OnListBoxKeyDown(select, option, key, canceled);

            // A list box with native appearance: the arrow keys of the block axis, Home and End select another option
            if (!ReferenceEquals(select, target) || key is not ("ArrowUp" or "ArrowDown" or "Home" or "End"))
                return false;

            if (!canceled && GetNativeListBoxOptionForKey(select, key) is { } next)
            {
                SelectOnly(select, next);
                RevealOption(next);
            }

            return true;
        }

        if (!select.IsPickerOpen)
        {
            if (!ReferenceEquals(select, target))
                return false;

            // As in Chrome, Enter does not submit the form of a select element
            if (key is " " or "Enter")
            {
                if (!canceled)
                {
                    select.OpenPicker(focusVisible: true);
                }

                return true;
            }

            if (key is "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight")
            {
                if (canceled)
                    return true;

                // The arrow keys open the picker with base appearance, and pick the previous or the next option otherwise
                var engine = select.NodeDocument.GetStyleEngine();
                if (ComputedStyleDeclarations.IsRendered(select) && StyleEngine.HasBaseAppearance(engine.GetStyle(select)))
                {
                    select.OpenPicker(focusVisible: true);
                }
                else
                {
                    PickAdjacentOption(select, forward: key is "ArrowDown" or "ArrowRight");
                }

                return true;
            }

            if (key is "Home" or "End" && !canceled)
            {
                PickEdgeOption(select, first: key == "Home");
                return true;
            }

            // As in the browsers, the type-ahead of a closed drop-down box picks the option whose label starts with the typed characters
            if (!canceled && IsTypeAheadKey(key))
            {
                var selected = select.GetListOfOptions().Find(static option => option.Selectedness);
                if (FindTypeAheadOption(select, selected, key, static option => !option.IsDisabled) is { } match)
                {
                    select.PickOption(match, @event: null, canceled: false);
                }

                return true;
            }

            return false;
        }

        if (key == "Tab")
        {
            if (!canceled)
            {
                select.ClosePicker(focusSelect: true, focusVisible: true);
            }

            return true;
        }

        if (!select.AreContentsRenderedWithBaseAppearance())
        {
            // The native picker
            if (canceled)
                return key is " " or "Enter" or "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight" or "Home" or "End";

            switch (key)
            {
                case " " or "Enter":
                    select.ClosePicker(focusSelect: true, focusVisible: true);
                    return true;

                case "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight":
                    PickAdjacentOption(select, forward: key is "ArrowDown" or "ArrowRight");
                    return true;

                case "Home" or "End":
                    PickEdgeOption(select, first: key == "Home");
                    return true;
            }

            return false;
        }

        switch (key)
        {
            case "Enter" or " ":
                if (option is not null && !option.IsDisabled && ReferenceEquals(option, target))
                {
                    select.PickOption(option, @event, canceled);
                }

                return true;

        }

        return FocusOptionByKey(select, option, key, canceled);
    }

    /// <summary>The keys that focus another option: the arrow keys, Home, End, PageUp, PageDown and the characters of the type-ahead.</summary>
    private static bool FocusOptionByKey(HtmlSelectElement select, HtmlOptionElement? option, string key, bool canceled)
    {
        if (key is "ArrowDown" or "ArrowUp" or "ArrowLeft" or "ArrowRight" or "Home" or "End" or "PageDown" or "PageUp")
        {
            // "If event's canceled flag is set, then return. Run the focusing steps on option."
            if (!canceled && GetOptionToFocus(select, option, key) is { } next)
            {
                FocusOption(next);
            }

            return true;
        }

        if (!canceled && IsTypeAheadKey(key))
        {
            if (FindTypeAheadOption(select, option, key, static option => FocusModel.IsFocusableArea(option)) is { } match)
            {
                FocusOption(match);
            }

            return true;
        }

        return false;
    }

    /// <summary>Whether the key value is a character (one code point).</summary>
    private static bool IsTypeAheadKey(string key) => key.Length == 1 || (key.Length == 2 && char.IsSurrogatePair(key, 0));

    /// <summary>
    /// The keys of a list box with base appearance, on its focused option: Enter and the space key toggle the option of a
    /// multiple selection, and select or unselect the option of a single selection; the other keys focus another option.
    /// </summary>
    private static bool OnListBoxKeyDown(HtmlSelectElement select, HtmlOptionElement option, string key, bool canceled)
    {
        if (key is "Enter" or " ")
        {
            if (!canceled && !option.IsDisabled)
            {
                ToggleListBoxOption(select, option);
            }

            return true;
        }

        return FocusOptionByKey(select, option, key, canceled);
    }

    /// <summary>
    /// The user toggles an option of a list box: the option of a multiple selection
    /// (https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-toggle); the option of a single selection is
    /// picked, or unselected when it is selected ("the user agent should also allow the user to request that the option whose
    /// selectedness is true, if any, be unselected").
    /// </summary>
    private static void ToggleListBoxOption(HtmlSelectElement select, HtmlOptionElement option)
    {
        if (select.IsMultiple)
        {
            select.ToggleOption(option);
        }
        else if (option.Selectedness)
        {
            select.UnselectOption(option);
        }
        else
        {
            select.PickOption(option, @event: null, canceled: false);
        }
    }

    /// <summary>Selects an option of a list box and unselects the other ones; the update notifications are sent when the selection changed.</summary>
    private static void SelectOnly(HtmlSelectElement select, HtmlOptionElement option)
    {
        if (!select.IsMultiple)
        {
            // Picking the selected option sets its dirtiness
            select.PickOption(option, @event: null, canceled: false);
            return;
        }

        var changed = false;
        foreach (var other in select.GetListOfOptions())
        {
            var selected = ReferenceEquals(other, option);
            if (other.Selectedness != selected)
            {
                other.Selectedness = selected;
                other.Dirtiness = true;
                changed = true;
            }
        }

        if (changed)
        {
            select.NotifySelectionChangedByUser();
        }
    }

    /// <summary>The option that a key selects in a list box with native appearance, from its last selected option.</summary>
    private static HtmlOptionElement? GetNativeListBoxOptionForKey(HtmlSelectElement select, string key)
    {
        var options = select.GetListOfOptions().FindAll(static option => !option.IsDisabled && FormControlSizing.IsDisplayedInListBox(option));
        if (options.Count == 0)
            return null;

        var index = options.FindLastIndex(static option => option.Selectedness);
        return key switch
        {
            "Home" => options[0],
            "End" => options[^1],
            "ArrowDown" => index + 1 < options.Count ? options[index + 1] : null,
            _ => index > 0 ? options[index - 1] : index < 0 ? options[0] : null,
        };
    }

    /// <summary>
    /// The key of the horizontal writing mode that corresponds to an arrow key in the writing mode of the select element: the
    /// arrow keys of the block axis focus the previous or the next option (as in Chrome, the up and down arrow keys keep
    /// doing so in the vertical writing modes).
    /// </summary>
    private static string GetLogicalKey(HtmlSelectElement select, string key)
    {
        if (key is not ("ArrowLeft" or "ArrowRight") || !ComputedStyleDeclarations.IsRendered(select))
            return key;

        return select.NodeDocument.GetStyleEngine().GetStyle(select).GetComputedValue("writing-mode") switch
        {
            "vertical-lr" or "sideways-lr" => key == "ArrowRight" ? "ArrowDown" : "ArrowUp",
            "vertical-rl" or "sideways-rl" => key == "ArrowLeft" ? "ArrowDown" : "ArrowUp",
            _ => key,
        };
    }

    /// <summary>Scrolls the list box with native appearance of an option to reveal the row of the option that a key selected.</summary>
    private static void RevealOption(HtmlOptionElement option)
    {
        if (option.IsConnected && option.NodeDocument.LayoutEngineOrNull is { } engine)
        {
            _ = ScrollIntoView.Run(engine, option, ScrollBehavior.Auto, ScrollLogicalPosition.Nearest, ScrollLogicalPosition.Nearest, container: option);
        }
    }

    /// <summary>Focuses an option with the keyboard: the option is scrolled into view in its scroll container (the picker or the list box).</summary>
    private static void FocusOption(HtmlOptionElement option)
    {
        FocusModel.Focus(option, focusVisible: true);
        if (option.IsConnected && option.NodeDocument.LayoutEngineOrNull is { } engine)
        {
            _ = ScrollIntoView.Run(engine, option, ScrollBehavior.Auto, ScrollLogicalPosition.Nearest, ScrollLogicalPosition.Nearest, container: option);
        }
    }

    /// <summary>
    /// Returns the select element the target of an event is in (in the flat tree), and the option of its list of options
    /// that contains the target, if any.
    /// </summary>
    private static HtmlSelectElement? GetSelect(Node target, out HtmlOptionElement? option)
    {
        option = null;
        for (Node? node = target; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (node is HtmlOptionElement candidate && option is null && candidate.CachedNearestAncestorSelect is not null)
            {
                option = candidate;
            }
            else if (node is HtmlSelectElement select)
            {
                if (option is not null && !ReferenceEquals(option.CachedNearestAncestorSelect, select))
                {
                    option = null;
                }

                return select;
            }
        }

        option = null;
        return null;
    }

    /// <summary>Whether the point of a mouse event is in the box of the select popover of the select element (and not in the box of the select element).</summary>
    private static bool IsInPicker(HtmlSelectElement select, MouseEvent @event)
    {
        if (@event.IsPositionless || !select.IsPickerInPopoverStack)
            return false;

        var document = select.NodeDocument;
        if (HitTesting.HitTest(document.GetLayoutEngine(), new PhysicalOffset(@event.ClientX, @event.ClientY)) is not { } hit)
            return false;

        for (LayoutBox? box = hit.Fragment.Box; box is not null; box = box.Parent)
        {
            if (ReferenceEquals(box.Element, select))
                return box.PseudoElement == StyleEngine.SelectPicker;
        }

        return false;
    }

    /// <summary>The option that a key focuses from the focused option of an open picker with base appearance, or null.</summary>
    private static HtmlOptionElement? GetOptionToFocus(HtmlSelectElement select, HtmlOptionElement? current, string key)
    {
        var options = select.GetListOfOptions().FindAll(static option => FocusModel.IsFocusableArea(option));
        if (options.Count == 0)
            return null;

        var index = current is null ? -1 : options.IndexOf(current);
        switch (key)
        {
            case "ArrowDown":
                return index + 1 < options.Count ? options[index + 1] : null;

            case "ArrowUp":
                return index < 0 ? options[^1] : index > 0 ? options[index - 1] : null;

            case "Home":
                return options[0];

            case "End":
                return options[^1];

            case "PageUp" or "PageDown":
                return GetPageOption(select, current, options, index, down: key == "PageDown");

            default:
                return null;
        }
    }

    /// <summary>
    /// The option that PageDown (PageUp) focuses, as in Chrome: the last (first) option that is entirely in the scrollport of
    /// the picker or of the list box, or, when it is the focused one, the option one page after (before) it, which the
    /// focus scrolls into view.
    /// </summary>
    private static HtmlOptionElement GetPageOption(HtmlSelectElement select, HtmlOptionElement? current, List<HtmlOptionElement> options, int index, bool down)
    {
        var edge = down ? options[^1] : options[0];
        if (current is null || index < 0 || GetScrollport(select, current) is not { } scrollport)
            return edge;

        const double Tolerance = 0.5;
        var currentRect = current.GetBoundingClientRect();
        HtmlOptionElement? visible = null;
        HtmlOptionElement? page = null;
        for (var i = down ? index + 1 : index - 1; i >= 0 && i < options.Count; i += down ? 1 : -1)
        {
            var rect = options[i].GetBoundingClientRect();
            if (rect.Top >= scrollport.Y - Tolerance && rect.Bottom <= scrollport.Bottom + Tolerance)
            {
                visible = options[i];
            }

            // The options that fit in a scrollport that starts (ends) at the focused option
            if (down ? rect.Bottom <= currentRect.Top + scrollport.Height + Tolerance : rect.Top >= currentRect.Bottom - scrollport.Height - Tolerance)
            {
                page = options[i];
            }
            else
            {
                break;
            }
        }

        return visible ?? page ?? (down ? options[Math.Min(index + 1, options.Count - 1)] : options[Math.Max(index - 1, 0)]);
    }

    /// <summary>The scrollport, in the coordinates of the viewport, of the box of the select element that contains an option: its select popover, or the list box.</summary>
    private static PhysicalRect? GetScrollport(HtmlSelectElement select, HtmlOptionElement option)
    {
        var engine = select.NodeDocument.GetLayoutEngine();
        if (engine.EnsureLayout() is null || engine.GetFragmentsWithSplitBlocks(option) is not [var optionFragment, ..])
            return null;

        for (var fragment = optionFragment.Parent; fragment is not null; fragment = fragment.Parent)
        {
            if (fragment is BoxFragment box && ReferenceEquals(box.Box.Element, select) && box.Box.PseudoElement is null or StyleEngine.SelectPicker)
                return engine.ToViewport(LayoutEngine.MapToRoot(box, box.PaddingBoxRect)).Bounds;
        }

        return null;
    }

    /// <summary>
    /// The type-ahead: the typed characters, within a delay, are the prefix of the label of the option to focus (or to pick),
    /// searched from the option after the current one (from the current one while the prefix grows).
    /// </summary>
    private static HtmlOptionElement? FindTypeAheadOption(HtmlSelectElement select, HtmlOptionElement? current, string key, Predicate<HtmlOptionElement> isCandidate)
    {
        var now = select.NodeDocument.RelevantWindow?.Performance.Now() ?? 0;
        var search = select.TypeAheadText is { } previous && now - select.TypeAheadTime <= TypeAheadTimeout ? previous + key : key;
        select.TypeAheadText = search;
        select.TypeAheadTime = now;

        var options = select.GetListOfOptions().FindAll(isCandidate);
        if (options.Count == 0)
            return null;

        // A repeated character cycles through the options that start with it
        var repeated = search.Length > key.Length && search.Replace(key, "", StringComparison.OrdinalIgnoreCase).Length == 0;
        var prefix = repeated ? key : search;
        var start = current is null ? 0 : options.IndexOf(current) + (search.Length == key.Length || repeated ? 1 : 0);
        for (var i = 0; i < options.Count; i++)
        {
            var candidate = options[(Math.Max(start, 0) + i) % options.Count];
            if (candidate.GetLabel(includeAltText: false).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    /// <summary>Picks the option before or after the selected option of a drop-down box whose options are not rendered.</summary>
    private static void PickAdjacentOption(HtmlSelectElement select, bool forward)
    {
        var options = select.GetListOfOptions();
        var index = options.FindIndex(static option => option.Selectedness);
        for (var i = index + (forward ? 1 : -1); i >= 0 && i < options.Count; i += forward ? 1 : -1)
        {
            if (!options[i].IsDisabled)
            {
                select.PickOption(options[i], @event: null, canceled: false);
                return;
            }
        }
    }

    private static void PickEdgeOption(HtmlSelectElement select, bool first)
    {
        var options = select.GetListOfOptions();
        var option = first ? options.Find(static option => !option.IsDisabled) : options.FindLast(static option => !option.IsDisabled);
        if (option is not null && !option.Selectedness)
        {
            select.PickOption(option, @event: null, canceled: false);
        }
    }
}
