namespace Meziantou.Framework.WebPlatform.Dom;

// https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element
public partial class HtmlSelectElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-size</summary>
    internal long DisplaySize
    {
        get
        {
            var size = GetAttributeValue("size");
            if (size is not null && Microsyntaxes.TryParseNonNegativeInteger(size, out var value))
                return value;

            return HasAttributeWithLocalName("multiple") ? 4 : 1;
        }
    }

    internal bool IsMultiple => HasAttributeWithLocalName("multiple");

    /// <summary>The user picks an option (https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-pick), without a triggering event.</summary>
    internal void PickOption(HtmlOptionElement option)
    {
        if (IsMultiple || Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
            return;

        option.Selected = true;
        SendUpdateNotifications();
    }

    /// <summary>
    /// The user picks an option (https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-pick) with the
    /// keydown or mouseup event of the interaction (null for the native picker). As in the browsers, the update
    /// notifications are only sent when the selected option changes.
    /// </summary>
    internal void PickOption(HtmlOptionElement option, Event? @event, bool canceled)
    {
        if (IsMultiple || Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
            return;

        if (@event is not null && canceled)
            return;

        if (@event is MouseEvent { Button: not 0 })
            return;

        var changed = !option.Selectedness;
        if (changed)
        {
            foreach (var other in GetListOfOptions())
            {
                other.Selectedness = ReferenceEquals(other, option);
            }
        }

        option.Dirtiness = true;
        if (changed)
        {
            NodeDocument.InvalidateLayout(rebuildBoxTree: true);
            SendUpdateNotifications();
        }

        // "If select is being rendered as a drop-down box with base appearance, then run the hide popover algorithm given
        // select's select popover."
        if (@event is not null)
        {
            ClosePicker(focusSelect: true, focusVisible: @event is not MouseEvent);
        }
    }

    /// <summary>The position of the press of the button that opened the picker, until the button is released.</summary>
    internal (double X, double Y)? PickerPressPosition { get; set; }

    /// <summary>The characters typed in the picker for the type-ahead, and the time of the last one.</summary>
    internal string? TypeAheadText { get; set; }

    internal double TypeAheadTime { get; set; }

    /// <summary>The user toggles an option of a multiple selection (https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-toggle).</summary>
    internal void ToggleOption(HtmlOptionElement option)
    {
        if (!IsMultiple || Css.Selectors.HtmlElementStates.IsActuallyDisabled(this) || Css.Selectors.HtmlElementStates.IsActuallyDisabled(option))
            return;

        option.Selected = !option.Selectedness;
        SendUpdateNotifications();
    }

    /// <summary>
    /// The user unselects the selected option of a single selection rendered as a list box
    /// (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element: "set the selectedness of that option
    /// element to false, set its dirtiness to true, and then send select update notifications").
    /// </summary>
    internal void UnselectOption(HtmlOptionElement option)
    {
        if (IsMultiple || !option.Selectedness || Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
            return;

        option.Selectedness = false;
        option.Dirtiness = true;
        SendUpdateNotifications();
    }

    /// <summary>The user changed the selectedness of the options of a list box: the update notifications are sent.</summary>
    internal void NotifySelectionChangedByUser()
    {
        NodeDocument.InvalidateLayout(rebuildBoxTree: false);
        SendUpdateNotifications();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#send-select-update-notifications</summary>
    private void SendUpdateNotifications()
    {
        NodeDocument.QueueElementTask(() =>
        {
            UserValidity = true;
            UpdateDescendantSelectedContentElements();
            FireEvent(new Event("input", new EventInit { Bubbles = true, Composed = true }));
            FireEvent(new Event("change", new EventInit { Bubbles = true }));
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-select-option-list</summary>
    internal List<HtmlOptionElement> GetListOfOptions()
    {
        var options = new List<HtmlOptionElement>();
        var node = FirstChild;
        while (node is not null)
        {
            if (node is HtmlOptionElement option)
            {
                options.Add(option);
            }

            // The options in a selectedcontent are not options of the select (see GetNearestAncestorSelect)
            var skipDescendants = node is Element element && element.IsHtmlNamespace &&
                (element.TagId is TagId.Select or TagId.Hr or TagId.Option or TagId.Datalist or TagId.Selectedcontent ||
                 (element.TagId == TagId.Optgroup && HasOptgroupAncestorBetween(element)));

            node = skipDescendants ? node.GetNextInTreeOrderSkippingChildren(this) : node.GetNextInTreeOrder(this);
        }

        return options;
    }

    private bool HasOptgroupAncestorBetween(Element optgroup)
    {
        for (var ancestor = optgroup.ParentNode; ancestor is not null && !ReferenceEquals(ancestor, this); ancestor = ancestor.ParentNode)
        {
            if (ancestor is Element { IsHtmlNamespace: true, TagId: TagId.Optgroup })
                return true;
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#selectedness-setting-algorithm</summary>
    internal bool RunSelectednessSettingAlgorithm()
    {
        if (IsMultiple)
            return false;

        var hasChanged = false;
        HtmlOptionElement? firstEnabledOption = null;
        HtmlOptionElement? lastSelectedOption = null;
        foreach (var option in GetListOfOptions())
        {
            if (option.Selectedness)
            {
                if (lastSelectedOption is not null)
                {
                    lastSelectedOption.Selectedness = false;
                    hasChanged = true;
                }

                lastSelectedOption = option;
            }

            if (firstEnabledOption is null && !option.IsDisabled)
            {
                firstEnabledOption = option;
            }
        }

        // As in the browsers, a display size of 0 (size=0) is a drop-down box too: the specification only selects the first
        // option when the display size is 1 (WPT html/rendering/widgets/the-select-element/select-size-002.html)
        if (lastSelectedOption is null && firstEnabledOption is not null && DisplaySize <= 1)
        {
            firstEnabledOption.Selectedness = true;
            lastSelectedOption = firstEnabledOption;
            hasChanged = true;
        }

        _lastSelectednessResult = lastSelectedOption;
        return hasChanged;
    }

    /// <summary>The selected option after the last run of the selectedness setting algorithm.</summary>
    private HtmlOptionElement? _lastSelectednessResult;

    /// <summary>
    /// Whether inserting an option whose selectedness is false leaves the selectedness unchanged: the algorithm would keep
    /// the selected option of its last run, which is still the selected option of this select. This makes appending n
    /// options O(n) instead of O(n²) (e.g. a parsed select with many options).
    /// </summary>
    internal bool IsSelectednessUnchangedByUnselectedOption() =>
        !IsMultiple && _lastSelectednessResult is { Selectedness: true } selected && ReferenceEquals(selected.CachedNearestAncestorSelect, this);

    private bool _isUpdatingOptionsInBatch;
    private bool _hasPendingSelectednessUpdate;

    /// <summary>
    /// Inserts or removes options without running the selectedness setting algorithm after each one: it runs once at the end,
    /// which gives the same result since no script runs in between (otherwise adding n options is O(n²)).
    /// </summary>
    internal void UpdateOptionsInBatch<TState>(TState state, Action<HtmlSelectElement, TState> action)
    {
        if (_isUpdatingOptionsInBatch)
        {
            action(this, state);
            return;
        }

        _isUpdatingOptionsInBatch = true;
        try
        {
            action(this, state);
        }
        finally
        {
            _isUpdatingOptionsInBatch = false;
        }

        if (_hasPendingSelectednessUpdate)
        {
            _hasPendingSelectednessUpdate = false;
            RunSelectednessSettingAlgorithm();
            UpdateDescendantSelectedContentElements();
        }
    }

    /// <summary>Returns <see langword="false"/> when the update is deferred to the end of <see cref="UpdateOptionsInBatch"/>.</summary>
    internal bool TryDeferSelectednessUpdate()
    {
        if (!_isUpdatingOptionsInBatch)
            return false;

        _hasPendingSelectednessUpdate = true;
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#reset-a-select's-selectedness</summary>
    internal void ResetSelectedness(bool hasChanged)
    {
        if (RunSelectednessSettingAlgorithm())
        {
            hasChanged = true;
        }

        if (!hasChanged)
            return;

        UpdateDescendantSelectedContentElements();
    }

    // A pending update of the descendant selectedcontent elements: queued in a microtask (by removing or moving an option), or
    // left to the post-connection steps of the inserted options (when inserting an option changes the selectedness). Updating
    // the descendant selectedcontent elements performs it, so that each mutation updates them once
    // (WPT html/semantics/forms/the-select-element/customizable-select/selectedcontent-queued-update.html).
    private bool _selectedContentUpdateQueued;
    private bool _selectedContentUpdatePending;

    internal bool HasPendingSelectedContentUpdate => _selectedContentUpdateQueued || _selectedContentUpdatePending;

    /// <summary>Leaves the update of the descendant selectedcontent elements to the post-connection steps of an inserted option.</summary>
    internal void SetSelectedContentUpdatePending() => _selectedContentUpdatePending = true;

    /// <summary>Queues a microtask to update the descendant selectedcontent elements, unless one is already queued (synchronously in a document without an event loop).</summary>
    internal void QueueSelectedContentUpdate()
    {
        if (NodeDocument.EventLoop is not { } eventLoop)
        {
            UpdateDescendantSelectedContentElements();
            return;
        }

        if (_selectedContentUpdateQueued)
            return;

        _selectedContentUpdateQueued = true;
        eventLoop.QueueMicrotask(() =>
        {
            if (_selectedContentUpdateQueued)
            {
                UpdateDescendantSelectedContentElements();
            }
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#update-a-select's-descendant-selectedcontent-elements</summary>
    internal void UpdateDescendantSelectedContentElements()
    {
        _selectedContentUpdateQueued = false;
        _selectedContentUpdatePending = false;
        if (IsMultiple)
            return;

        var selectedContents = new List<HtmlSelectedContentElement>();
        for (var node = FirstChild; node is not null; node = node.GetNextInTreeOrder(this))
        {
            if (node is HtmlSelectedContentElement { Disabled: false } selectedContent)
            {
                selectedContents.Add(selectedContent);
            }
        }

        foreach (var selectedContent in selectedContents)
        {
            selectedContent.Update(this);
        }
    }

    /// <summary>
    /// The state of the control that a traversal restores (see <see cref="FormControlState"/>): the indexes and the values of
    /// the selected options, when the selectedness of an option was changed since the last reset (its dirtiness), or null.
    /// </summary>
    internal List<(int Index, string Value)>? SaveFormControlState()
    {
        var options = GetListOfOptions();
        if (!options.Exists(option => option.Dirtiness))
            return null;

        var state = new List<(int Index, string Value)>();
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Selectedness)
            {
                state.Add((i, options[i].Value));
            }
        }

        return state;
    }

    /// <summary>
    /// Restores the state saved by <see cref="SaveFormControlState"/> (no input or change event is fired): an option is found
    /// by its index and its value, else by its value. As in the browsers, the select keeps its state when none of the saved
    /// options is found.
    /// </summary>
    internal void RestoreFormControlState(List<(int Index, string Value)> state)
    {
        var options = GetListOfOptions();
        var selectedOptions = new List<HtmlOptionElement>();
        foreach (var (index, value) in state)
        {
            var option = index < options.Count && options[index].Value == value ? options[index] : options.Find(option => option.Value == value && !selectedOptions.Contains(option));
            if (option is not null && !selectedOptions.Contains(option))
            {
                selectedOptions.Add(option);
                if (!IsMultiple)
                    break;
            }
        }

        if (state.Count > 0 && selectedOptions.Count == 0)
            return;

        foreach (var option in options)
        {
            option.Selectedness = selectedOptions.Contains(option);
            option.Dirtiness = true;
        }

        RunSelectednessSettingAlgorithm();
        NodeDocument.InvalidateLayout(rebuildBoxTree: true);
        UpdateDescendantSelectedContentElements();
    }

    /// <summary>The reset algorithm (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element:concept-form-reset-control).</summary>
    internal override void ResetFromParser()
    {
        foreach (var option in GetListOfOptions())
        {
            option.Selectedness = option.HasAttributeWithLocalName("selected");
            option.Dirtiness = false;
        }

        RunSelectednessSettingAlgorithm();
        UpdateDescendantSelectedContentElements();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null)
            return;

        if (IsPickerOpen && localName is "size" or "multiple" or "disabled")
        {
            ClosePickerIfInvalid();
        }

        if (localName == "size")
        {
            ResetSelectedness(hasChanged: false);
        }
        else if (localName == "multiple" && (oldValue is null) != (value is null))
        {
            if (value is null)
            {
                // The multiple attribute was removed: keep only the first selected option
                var first = true;
                foreach (var option in GetListOfOptions())
                {
                    if (option.Selectedness)
                    {
                        if (!first)
                        {
                            option.Selectedness = false;
                        }

                        first = false;
                    }
                }
            }

            ResetSelectedness(hasChanged: true);
        }
    }
}

// https://html.spec.whatwg.org/multipage/form-elements.html#the-optgroup-element
public partial class HtmlOptGroupElement
{
    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);

        // Whenever the disabled attribute is added or removed, the selectedness of the nearest ancestor select is reset
        if (@namespace is null && localName == "disabled" && (oldValue is null) != (value is null) && HtmlOptionElement.GetNearestAncestorSelect(this) is { } select)
        {
            select.ResetSelectedness(hasChanged: false);
        }
    }
}

// https://html.spec.whatwg.org/multipage/form-elements.html#the-option-element
public partial class HtmlOptionElement
{
    private bool _selectedness;

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-selectedness</summary>
    internal bool Selectedness
    {
        get => _selectedness;
        set
        {
            if (_selectedness == value)
                return;

            _selectedness = value;

            // Live collections (selectedOptions) depend on the selectedness
            NodeDocument.IncrementDomVersion();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-dirtiness</summary>
    internal bool Dirtiness { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#cached-nearest-ancestor-select-element</summary>
    internal HtmlSelectElement? CachedNearestAncestorSelect { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-disabled</summary>
    internal bool IsDisabled
    {
        get
        {
            if (HasAttributeWithLocalName("disabled"))
                return true;

            for (var ancestor = ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
            {
                if (ancestor.IsHtmlNamespace && ancestor.TagId is TagId.Select or TagId.Hr or TagId.Datalist or TagId.Option)
                    return false;

                if (ancestor.IsHtml(TagId.Optgroup))
                    return ancestor.HasAttributeWithLocalName("disabled");
            }

            return false;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#option-element-nearest-ancestor-select</summary>
    private HtmlSelectElement? GetNearestAncestorSelect() => GetNearestAncestorSelect(this);

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#option-element-nearest-ancestor-select</summary>
    internal static HtmlSelectElement? GetNearestAncestorSelect(Element element)
    {
        Element? ancestorOptgroup = null;
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            // The options in a selectedcontent are clones (deviation from the specification, which loops forever when the clone
            // contains a selected option, e.g. <option>a<span><option selected>, as it is then the selected option of the select):
            // https://github.com/whatwg/html/issues/13038
            if (ancestor.IsHtmlNamespace && ancestor.TagId is TagId.Datalist or TagId.Hr or TagId.Option or TagId.Selectedcontent)
                return null;

            if (ancestor.IsHtml(TagId.Optgroup))
            {
                if (ancestorOptgroup is not null)
                    return null;

                ancestorOptgroup = ancestor;
            }

            if (ancestor is HtmlSelectElement select)
                return select;
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#update-an-option's-nearest-ancestor-select</summary>
    private List<HtmlSelectElement> UpdateNearestAncestorSelect()
    {
        var oldSelect = CachedNearestAncestorSelect;
        var newSelect = GetNearestAncestorSelect();
        CachedNearestAncestorSelect = newSelect;
        if (ReferenceEquals(oldSelect, newSelect))
            return [];

        var result = new List<HtmlSelectElement>();
        foreach (var select in new[] { oldSelect, newSelect })
        {
            if (select is null || select.TryDeferSelectednessUpdate())
                continue;

            // While the children of a node are replaced, the selects that lose options are reset once all the children are removed
            if (ReferenceEquals(select, oldSelect) && NodeDocument.DeferSelectRemovalResets(select))
            {
                if (Selectedness)
                {
                    result.Add(select);
                }

                continue;
            }

            if (ReferenceEquals(select, newSelect) && !Selectedness && select.IsSelectednessUnchangedByUnselectedOption())
                continue;

            var hasChanged = select.RunSelectednessSettingAlgorithm();
            if (Selectedness || hasChanged)
            {
                result.Add(select);
            }
        }

        return result;
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();

        // As in browsers, an inserted option whose selectedness is true is the selected option of its select (the selectedness
        // setting algorithm would keep the last selected option in tree order)
        if (Selectedness && GetNearestAncestorSelect() is { IsMultiple: false } select && !ReferenceEquals(select, CachedNearestAncestorSelect))
        {
            // The options inserted with this one (e.g. in the same optgroup) have not run their insertion steps yet: the
            // last one of them that is selected wins
            foreach (var option in select.GetListOfOptions())
            {
                if (!ReferenceEquals(option, this) && ReferenceEquals(option.CachedNearestAncestorSelect, select))
                {
                    option.Selectedness = false;
                }
            }
        }

        // The update of the selectedcontent elements is left to the post-connection steps, which run once the tree is in a
        // consistent state (they do not run in a disconnected tree)
        if (IsConnected)
        {
            foreach (var changedSelect in UpdateNearestAncestorSelect())
            {
                changedSelect.SetSelectedContentUpdatePending();
            }
        }
        else
        {
            UpdateNearestAncestorSelect();
        }
    }

    /// <summary>The post-connection steps perform the pending update of the selectedcontent elements of the select.</summary>
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        if (CachedNearestAncestorSelect is { HasPendingSelectedContentUpdate: true } select)
        {
            select.UpdateDescendantSelectedContentElements();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#the-option-element (moving steps)</summary>
    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        QueueSelectedContentUpdates(UpdateNearestAncestorSelect());
    }

    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        QueueSelectedContentUpdates(UpdateNearestAncestorSelect());
    }

    private static void QueueSelectedContentUpdates(List<HtmlSelectElement> selects)
    {
        foreach (var select in selects)
        {
            select.QueueSelectedContentUpdate();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#update-descendant-selectedcontent-elements-for-an-option</summary>
    internal void UpdateDescendantSelectedContentElements()
    {
        var select = CachedNearestAncestorSelect;
        if (select is null || !Selectedness)
            return;

        select.UpdateDescendantSelectedContentElements();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null || (oldValue is null) == (value is null))
            return;

        if (localName == "selected")
        {
            var hasChanged = false;
            if (!Dirtiness)
            {
                if (value is not null && !Selectedness)
                {
                    Selectedness = true;
                    hasChanged = true;
                }
                else if (value is null && Selectedness)
                {
                    Selectedness = false;
                    hasChanged = true;
                }
            }

            CachedNearestAncestorSelect?.ResetSelectedness(hasChanged);
        }
        else if (localName == "disabled")
        {
            CachedNearestAncestorSelect?.ResetSelectedness(hasChanged: false);
        }
    }
}

// https://html.spec.whatwg.org/multipage/form-elements.html#the-selectedcontent-element
public partial class HtmlSelectedContentElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#selectedcontent-disabled</summary>
    internal bool Disabled { get; private set; }

    // The select returned by the last disabledness recalculation
    private HtmlSelectElement? _nearestSelectAncestor;

    // Whether the element was inserted in a document and its post-connection steps have not updated it yet: an update of its
    // select made in between (e.g. by the post-connection steps of an option inserted before it) leaves nothing to do
    private bool _postConnectionUpdatePending;

    // Whether the moving steps queued an update that has not run yet
    private bool _moveUpdateQueued;

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#recalculate-a-selectedcontent-element's-disabledness</summary>
    private HtmlSelectElement? RecalculateDisabledness()
    {
        HtmlSelectElement? nearestSelectAncestor = null;
        Disabled = false;
        for (var ancestor = ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor is HtmlSelectElement select)
            {
                if (nearestSelectAncestor is null)
                {
                    nearestSelectAncestor = select;
                    continue;
                }

                Disabled = true;
                break;
            }

            if (ancestor is HtmlOptionElement or HtmlSelectedContentElement)
            {
                Disabled = true;
                break;
            }
        }

        _nearestSelectAncestor = nearestSelectAncestor;
        return nearestSelectAncestor;
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        RecalculateDisabledness();
        _postConnectionUpdatePending = IsConnected;
    }

    /// <summary>
    /// The post-connection steps; the update is left to a pending update of the select's descendant selectedcontent elements,
    /// which updates this element too.
    /// </summary>
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        var pending = _postConnectionUpdatePending;
        _postConnectionUpdatePending = false;
        var select = RecalculateDisabledness();
        if (!pending || Disabled || select is null || select.IsMultiple || select.HasPendingSelectedContentUpdate)
            return;

        Update(select);
    }

    /// <summary>The removing steps: removing an ancestor select may enable the element (WPT selectedcontent-nested-select.html).</summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        RecalculateDisabledness();
    }

    /// <summary>
    /// The moving steps (https://github.com/whatwg/html/pull/12263): a selectedcontent element moved to another select is updated
    /// in a microtask, which finds the select of the element when it runs. Moving it again before the microtask runs queues no
    /// other update.
    /// </summary>
    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        var oldSelect = _nearestSelectAncestor;
        var wasDisabled = Disabled;
        var select = RecalculateDisabledness();
        if (Disabled || select is null || select.IsMultiple)
            return;

        // The content is up to date when the element stays in the same select (e.g. an ancestor of the select is moved)
        if (!wasDisabled && ReferenceEquals(select, oldSelect))
            return;

        if (NodeDocument.EventLoop is not { } eventLoop)
        {
            Update(select);
            return;
        }

        if (_moveUpdateQueued)
            return;

        _moveUpdateQueued = true;
        eventLoop.QueueMicrotask(() =>
        {
            if (!_moveUpdateQueued)
                return;

            _moveUpdateQueued = false;
            if (RecalculateDisabledness() is { IsMultiple: false } currentSelect && !Disabled)
            {
                Update(currentSelect);
            }
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#update-a-selectedcontent</summary>
    internal void Update(HtmlSelectElement select)
    {
        _postConnectionUpdatePending = false;
        _moveUpdateQueued = false;
        if (Disabled)
            return;

        HtmlOptionElement? option = null;
        foreach (var candidate in select.GetListOfOptions())
        {
            if (candidate.Selectedness)
            {
                option = candidate;
                break;
            }
        }

        if (option is null)
        {
            ReplaceAll(null);
            return;
        }

        // Clone an option into a selectedcontent
        var fragment = new DocumentFragment(option.NodeDocument);
        for (var child = option.FirstChild; child is not null; child = child.NextSibling)
        {
            fragment.AppendChildCore(child.Clone(document: null, subtree: true, parent: null, fallbackRegistry: null));
        }

        ReplaceAll(fragment);
    }
}

public partial class Document
{
    private int _replaceAllRemovalDepth;
    private List<HtmlSelectElement>? _pendingSelectRemovalResets;

    /// <summary>
    /// Starts the removal of the children of a node by "replace all" (e.g. <c>select.innerHTML = ''</c>): as in browsers
    /// (https://github.com/whatwg/html/issues/11825), the selectedness setting algorithm of the selects that lose options runs
    /// once all the children are removed, so that it does not select an option that is removed next.
    /// </summary>
    internal void BeginReplaceAllRemoval() => _replaceAllRemovalDepth++;

    /// <summary>Ends the removal of the children of a node by "replace all", and resets the selectedness of the selects that lost options.</summary>
    internal void EndReplaceAllRemoval()
    {
        if (--_replaceAllRemovalDepth > 0 || _pendingSelectRemovalResets is not { } selects)
            return;

        _pendingSelectRemovalResets = null;
        foreach (var select in selects)
        {
            if (select.RunSelectednessSettingAlgorithm())
            {
                select.QueueSelectedContentUpdate();
            }
        }
    }

    /// <summary>Records the select whose option is removed while the children of a node are replaced; returns false when no children are being replaced.</summary>
    internal bool DeferSelectRemovalResets(HtmlSelectElement select)
    {
        if (_replaceAllRemovalDepth == 0)
            return false;

        _pendingSelectRemovalResets ??= [];
        if (!_pendingSelectRemovalResets.Contains(select))
        {
            _pendingSelectRemovalResets.Add(select);
        }

        return true;
    }
}
