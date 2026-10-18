namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/selection-api/#dom-getcomposedrangesoptions</summary>
[IdlInterface("GetComposedRangesOptions", "https://w3c.github.io/selection-api/#dom-getcomposedrangesoptions")]
public sealed class GetComposedRangesOptions
{
    /// <summary>Gets the shadow roots whose content may be exposed by the composed ranges.</summary>
    [IdlMember("shadowRoots", "https://w3c.github.io/selection-api/#dom-getcomposedrangesoptions-shadowroots")]
    public IReadOnlyList<ShadowRoot> ShadowRoots { get; init; } = [];
}

/// <summary>
/// The selection of a document (https://w3c.github.io/selection-api/#selection-interface): at most one live <see cref="Range"/>
/// and a direction. The selection changes through this API (<c>modify()</c> moves it over the layout as the keyboard does),
/// the <see cref="Range"/> methods, the DOM mutations, and the caret movement keys in editable content.
/// </summary>
[IdlInterface("Selection", "https://w3c.github.io/selection-api/#selection-interface")]
public sealed partial class Selection
{
    private readonly Document _document;
    private Range? _range;
    private SelectionDirection _direction;

    /// <summary>The range of the selection, or null (without the checks of <see cref="GetRangeAt"/>).</summary>
    internal Range? RangeOrNull => _range;

    // The anchor and the focus given to setBaseAndExtent() when they are in different trees (see GetRenderedBoundaryPoints)
    private (Node Node, uint Offset)? _crossTreeAnchor;
    private (Node Node, uint Offset)? _crossTreeFocus;

    // The state of the selection when the last selectionchange event was scheduled, so that an operation that does not
    // change the selection (e.g. calling setBaseAndExtent twice with the same arguments) does not fire an event, as in
    // the browsers.
    private SelectionState _lastState;

    internal Selection(Document document)
    {
        _document = document;
    }

    private enum SelectionDirection
    {
        Directionless,
        Forwards,
        Backwards,
    }

    private readonly record struct SelectionState(Node? StartNode, uint StartOffset, Node? EndNode, uint EndOffset, SelectionDirection Direction);

    /// <summary>https://w3c.github.io/selection-api/#dfn-anchor</summary>
    private (Node Node, uint Offset) Anchor => _direction != SelectionDirection.Backwards
        ? (_range!.StartContainer, _range.StartOffset)
        : (_range!.EndContainer, _range.EndOffset);

    /// <summary>https://w3c.github.io/selection-api/#dfn-focus</summary>
    private (Node Node, uint Offset) Focus => _direction != SelectionDirection.Backwards
        ? (_range!.EndContainer, _range.EndOffset)
        : (_range!.StartContainer, _range.StartOffset);

    /// <summary>Whether the node is in the document tree of the document associated with this selection.</summary>
    private bool IsInDocumentTree(Node node) => ReferenceEquals(node.GetRoot(), _document);

    /// <summary>Whether the selection is not empty and both its anchor and its focus are in the document tree.</summary>
    private bool HasRangeInDocumentTree => _range is not null && IsInDocumentTree(_range.StartContainer) && IsInDocumentTree(_range.EndContainer);

    /// <summary>
    /// Gets the node in which the selection starts, or null when there is no range in the document tree.
    /// </summary>
    [IdlMember("anchorNode", "https://w3c.github.io/selection-api/#dom-selection-anchornode")]
    public Node? AnchorNode => _range is not null && IsInDocumentTree(Anchor.Node) ? Anchor.Node : null;

    /// <summary>Gets the offset of the anchor of the selection within the anchor node.</summary>
    [IdlMember("anchorOffset", "https://w3c.github.io/selection-api/#dom-selection-anchoroffset")]
    public uint AnchorOffset => _range is not null && IsInDocumentTree(Anchor.Node) ? Anchor.Offset : 0;

    /// <summary>
    /// Gets the node in which the selection ends, or null when there is no range in the document tree.
    /// </summary>
    [IdlMember("focusNode", "https://w3c.github.io/selection-api/#dom-selection-focusnode")]
    public Node? FocusNode => _range is not null && IsInDocumentTree(Focus.Node) ? Focus.Node : null;

    /// <summary>Gets the offset of the focus of the selection within the focus node.</summary>
    [IdlMember("focusOffset", "https://w3c.github.io/selection-api/#dom-selection-focusoffset")]
    public uint FocusOffset => _range is not null && IsInDocumentTree(Focus.Node) ? Focus.Offset : 0;

    /// <summary>Whether the anchor and the focus are the same, including when the selection is empty.</summary>
    [IdlMember("isCollapsed", "https://w3c.github.io/selection-api/#dom-selection-iscollapsed")]
    public bool IsCollapsed => _range is null || _range.Collapsed;

    /// <summary>Gets the number of ranges in the selection (0 or 1).</summary>
    [IdlMember("rangeCount", "https://w3c.github.io/selection-api/#dom-selection-rangecount")]
    public uint RangeCount => HasRangeInDocumentTree ? 1u : 0u;

    /// <summary>"None", "Caret" or "Range".</summary>
    [IdlMember("type", "https://w3c.github.io/selection-api/#dom-selection-type")]
    public string Type => !HasRangeInDocumentTree ? "None" : _range!.Collapsed ? "Caret" : "Range";

    /// <summary>"none", "forward" or "backward".</summary>
    [IdlMember("direction", "https://w3c.github.io/selection-api/#dom-selection-direction")]
    public string Direction => _range is null ? "none" : _direction switch
    {
        SelectionDirection.Forwards => "forward",
        SelectionDirection.Backwards => "backward",
        _ => "none",
    };

    /// <summary>Returns the range of the selection (not a copy).</summary>
    [IdlMember("getRangeAt", "https://w3c.github.io/selection-api/#dom-selection-getrangeat")]
    public Range GetRangeAt(uint index)
    {
        if (index != 0 || !HasRangeInDocumentTree)
            throw DomException.IndexSize("The index is not in the allowed range");

        return _range!;
    }

    /// <summary>Associates the range (not a copy) with the selection, when the selection is empty.</summary>
    [IdlMember("addRange", "https://w3c.github.io/selection-api/#dom-selection-addrange")]
    public void AddRange(Range range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!IsInDocumentTree(range.StartContainer) || RangeCount != 0)
            return;

        // The browsers make the direction forwards, so the anchor is the start of the range
        SetRange(range, SelectionDirection.Forwards);
    }

    /// <summary>Removes the given range from the selection.</summary>
    [IdlMember("removeRange", "https://w3c.github.io/selection-api/#dom-selection-removerange")]
    public void RemoveRange(Range range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!ReferenceEquals(_range, range))
            throw DomException.NotFound("The range is not the range of the selection");

        SetRange(null, SelectionDirection.Directionless);
    }

    /// <summary>Removes all the ranges from the selection.</summary>
    [IdlMember("removeAllRanges", "https://w3c.github.io/selection-api/#dom-selection-removeallranges")]
    public void RemoveAllRanges()
    {
        if (_range is not null)
        {
            SetRange(null, SelectionDirection.Directionless);
        }
    }

    /// <summary>Removes all the ranges from the selection. This is an alias of <c>removeAllRanges()</c>.</summary>
    [IdlMember("empty", "https://w3c.github.io/selection-api/#dom-selection-empty")]
    public void Empty() => RemoveAllRanges();

    /// <summary>
    /// Returns the range of the selection, with the boundary points in the shadow trees that are not in
    /// <paramref name="options"/> rescoped to their shadow host.
    /// </summary>
    [IdlMember("getComposedRanges", "https://w3c.github.io/selection-api/#dom-selection-getcomposedranges")]
    public IReadOnlyList<StaticRange> GetComposedRanges(GetComposedRangesOptions? options = null)
    {
        if (_range is null)
            return [];

        var shadowRoots = options?.ShadowRoots ?? [];
        var (startNode, startOffset) = (_range.StartContainer, _range.StartOffset);
        // The loops stop at a shadow host without parent (the specification sets the node to null)
        while (startNode.GetRoot() is ShadowRoot root && root.Host.ParentNode is not null && !IsShadowIncludingInclusiveAncestorOfAny(root, shadowRoots))
        {
            startOffset = (uint)root.Host.GetIndex();
            startNode = root.Host.ParentNode;
        }

        var (endNode, endOffset) = (_range.EndContainer, _range.EndOffset);
        while (endNode.GetRoot() is ShadowRoot root && root.Host.ParentNode is not null && !IsShadowIncludingInclusiveAncestorOfAny(root, shadowRoots))
        {
            endOffset = (uint)root.Host.GetIndex() + 1;
            endNode = root.Host.ParentNode;
        }

        return [new StaticRange(new StaticRangeInit { StartContainer = startNode, StartOffset = startOffset, EndContainer = endNode, EndOffset = endOffset })];

        static bool IsShadowIncludingInclusiveAncestorOfAny(ShadowRoot root, IReadOnlyList<ShadowRoot> shadowRoots)
        {
            foreach (var shadowRoot in shadowRoots)
            {
                if (root.IsShadowIncludingInclusiveAncestorOf(shadowRoot))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Collapses the selection to the boundary point, or empties it when <paramref name="node"/> is null.</summary>
    [IdlMember("collapse", "https://w3c.github.io/selection-api/#dom-selection-collapse")]
    public void Collapse(Node? node, uint offset = 0)
    {
        if (node is null)
        {
            RemoveAllRanges();
            return;
        }

        if (node is DocumentType)
            throw new DomException("The node cannot be a doctype", "InvalidNodeTypeError");

        if (offset > AbstractRange.GetNodeLength(node))
            throw DomException.IndexSize("The offset is greater than the length of the node");

        if (!_document.IsShadowIncludingInclusiveAncestorOf(node))
            return;

        SetRange(new Range(node, offset, node, offset), SelectionDirection.Directionless);
    }

    /// <summary>Collapses the selection to the given node and offset. This is an alias of <c>collapse()</c>.</summary>
    [IdlMember("setPosition", "https://w3c.github.io/selection-api/#dom-selection-setposition")]
    public void SetPosition(Node? node, uint offset = 0) => Collapse(node, offset);

    /// <summary>Collapses the selection to the start of its range.</summary>
    [IdlMember("collapseToStart", "https://w3c.github.io/selection-api/#dom-selection-collapsetostart")]
    public void CollapseToStart()
    {
        if (_range is null)
            throw DomException.InvalidState("The selection is empty");

        SetRange(new Range(_range.StartContainer, _range.StartOffset, _range.StartContainer, _range.StartOffset), SelectionDirection.Directionless);
    }

    /// <summary>Collapses the selection to the end of its range.</summary>
    [IdlMember("collapseToEnd", "https://w3c.github.io/selection-api/#dom-selection-collapsetoend")]
    public void CollapseToEnd()
    {
        if (_range is null)
            throw DomException.InvalidState("The selection is empty");

        SetRange(new Range(_range.EndContainer, _range.EndOffset, _range.EndContainer, _range.EndOffset), SelectionDirection.Directionless);
    }

    /// <summary>Moves the focus of the selection to the boundary point, keeping the anchor.</summary>
    [IdlMember("extend", "https://w3c.github.io/selection-api/#dom-selection-extend")]
    public void Extend(Node node, uint offset = 0)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_document.IsShadowIncludingInclusiveAncestorOf(node))
            return;

        if (_range is null)
            throw DomException.InvalidState("The selection is empty");

        var (anchorNode, anchorOffset) = Anchor;
        var newRange = new Range(_document);
        var sameRoot = ReferenceEquals(node.GetRoot(), _range.StartContainer.GetRoot());
        if (!sameRoot)
        {
            newRange.SetStart(node, offset);
            newRange.SetEnd(node, offset);
        }
        else if (AbstractRange.ComparePoints(anchorNode, anchorOffset, node, offset) != AbstractRange.BoundaryPointPosition.After)
        {
            newRange.SetStart(anchorNode, anchorOffset);
            newRange.SetEnd(node, offset);
        }
        else
        {
            newRange.SetStart(node, offset);
            newRange.SetEnd(anchorNode, anchorOffset);
        }

        var backwards = sameRoot && AbstractRange.ComparePoints(node, offset, anchorNode, anchorOffset) == AbstractRange.BoundaryPointPosition.Before;
        SetRange(newRange, backwards ? SelectionDirection.Backwards : SelectionDirection.Forwards);
    }

    /// <summary>Sets the anchor and the focus of the selection to the given nodes and offsets.</summary>
    [IdlMember("setBaseAndExtent", "https://w3c.github.io/selection-api/#dom-selection-setbaseandextent")]
    public void SetBaseAndExtent(Node anchorNode, uint anchorOffset, Node focusNode, uint focusOffset)
    {
        ArgumentNullException.ThrowIfNull(anchorNode);
        ArgumentNullException.ThrowIfNull(focusNode);
        if (anchorOffset > AbstractRange.GetNodeLength(anchorNode) || focusOffset > AbstractRange.GetNodeLength(focusNode))
            throw DomException.IndexSize("The offset is greater than the length of the node");

        if (!_document.IsShadowIncludingInclusiveAncestorOf(anchorNode) || !_document.IsShadowIncludingInclusiveAncestorOf(focusNode))
            return;

        // The boundary points of different trees cannot be ordered: the range is collapsed at the focus
        var sameRoot = ReferenceEquals(anchorNode.GetRoot(), focusNode.GetRoot());
        var newRange = new Range(_document);
        if (!sameRoot || AbstractRange.ComparePoints(anchorNode, anchorOffset, focusNode, focusOffset) == AbstractRange.BoundaryPointPosition.Before)
        {
            newRange.SetStart(anchorNode, anchorOffset);
            newRange.SetEnd(focusNode, focusOffset);
        }
        else
        {
            newRange.SetStart(focusNode, focusOffset);
            newRange.SetEnd(anchorNode, anchorOffset);
        }

        var backwards = sameRoot && AbstractRange.ComparePoints(focusNode, focusOffset, anchorNode, anchorOffset) == AbstractRange.BoundaryPointPosition.Before;
        SetRange(newRange, backwards ? SelectionDirection.Backwards : SelectionDirection.Forwards);
        if (!sameRoot)
        {
            _crossTreeAnchor = (anchorNode, anchorOffset);
            _crossTreeFocus = (focusNode, focusOffset);
        }
    }

    /// <summary>
    /// Returns the boundary points of the selection for its rendering, in shadow-including tree order
    /// (<see cref="AbstractRange.CompareComposedPoints"/>), or null when the selection is empty: the boundary points of its
    /// range, or the anchor and the focus given to <see cref="SetBaseAndExtent"/> when they are in different trees ("anchor and
    /// focus [...] can be in a shadow tree of the same document": https://w3c.github.io/selection-api/#dfn-range). The live
    /// range of the selection cannot have boundary points in different trees (it is collapsed at the focus): these points are
    /// kept until the selection changes, and are ignored once they are no longer in the document.
    /// </summary>
    internal (Node StartNode, uint StartOffset, Node EndNode, uint EndOffset)? GetRenderedBoundaryPoints()
    {
        if (_range is null)
            return null;

        if (_crossTreeAnchor is var (anchorNode, anchorOffset) && _crossTreeFocus is var (focusNode, focusOffset)
            && _document.IsShadowIncludingInclusiveAncestorOf(anchorNode) && anchorOffset <= AbstractRange.GetNodeLength(anchorNode)
            && _document.IsShadowIncludingInclusiveAncestorOf(focusNode) && focusOffset <= AbstractRange.GetNodeLength(focusNode))
        {
            return AbstractRange.CompareComposedPoints(anchorNode, anchorOffset, focusNode, focusOffset) == AbstractRange.BoundaryPointPosition.After
                ? (focusNode, focusOffset, anchorNode, anchorOffset)
                : (anchorNode, anchorOffset, focusNode, focusOffset);
        }

        return (_range.StartContainer, _range.StartOffset, _range.EndContainer, _range.EndOffset);
    }

    /// <summary>Sets the selection to contain all the children of the given node.</summary>
    [IdlMember("selectAllChildren", "https://w3c.github.io/selection-api/#dom-selection-selectallchildren")]
    public void SelectAllChildren(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DocumentType)
            throw new DomException("The node cannot be a doctype", "InvalidNodeTypeError");

        if (!IsInDocumentTree(node))
            return;

        SetRange(new Range(node, 0, node, (uint)node.ChildCount), SelectionDirection.Forwards);
    }

    /// <summary>Deletes the contents of the range of the selection (the range is mutated, not replaced).</summary>
    [IdlMember("deleteFromDocument", "https://w3c.github.io/selection-api/#dom-selection-deletefromdocument")]
    public void DeleteFromDocument()
    {
        if (HasRangeInDocumentTree)
        {
            _range!.DeleteContents();
        }
    }

    /// <summary>
    /// Whether the node is selected (or partially selected when <paramref name="allowPartialContainment"/> is true). There
    /// is no rendering, so boundary points are never visually equivalent: the boundary points are compared.
    /// </summary>
    [IdlMember("containsNode", "https://w3c.github.io/selection-api/#dom-selection-containsnode")]
    public bool ContainsNode(Node node, bool allowPartialContainment = false)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_range is null || !IsInDocumentTree(node) || !IsInDocumentTree(_range.StartContainer))
            return false;

        var length = AbstractRange.GetNodeLength(node);
        if (allowPartialContainment)
        {
            return AbstractRange.ComparePoints(_range.StartContainer, _range.StartOffset, node, length) != AbstractRange.BoundaryPointPosition.After &&
                AbstractRange.ComparePoints(_range.EndContainer, _range.EndOffset, node, 0) != AbstractRange.BoundaryPointPosition.Before;
        }

        return AbstractRange.ComparePoints(_range.StartContainer, _range.StartOffset, node, 0) != AbstractRange.BoundaryPointPosition.After &&
            AbstractRange.ComparePoints(_range.EndContainer, _range.EndOffset, node, length) != AbstractRange.BoundaryPointPosition.Before;
    }

    /// <summary>
    /// The stringification behavior (https://w3c.github.io/selection-api/#dom-selection-stringifier): the rendered text of the
    /// range (the innerText of its content, see <see cref="Html.RenderedText"/>), or its text when it is not rendered.
    /// </summary>
    public override string ToString()
    {
        // https://w3c.github.io/selection-api/#dom-selection-stringifier: "if the selection is within a textarea or input
        // element, it must return the selected substring in its value" (the selection of the focused text control, while the
        // selection of the document is a caret)
        if (_range is null or { Collapsed: true } && GetFocusedTextControlSelection() is { } selectedText)
            return selectedText;

        return _range is null ? "" : Html.RenderedText.GetRenderedText(_range) ?? _range.ToString();
    }

    private string? GetFocusedTextControlSelection()
    {
        switch (_document.FocusedArea)
        {
            case HtmlTextAreaElement textArea:
            {
                var value = textArea.Value;
                var start = (int)Math.Min(textArea.SelectionStart, (uint)value.Length);
                var end = (int)Math.Min(textArea.SelectionEnd, (uint)value.Length);
                return start < end ? value[start..end] : null;
            }

            case HtmlInputElement { SelectionStart: { } selectionStart, SelectionEnd: { } selectionEnd } input:
            {
                var value = input.Value;
                var start = (int)Math.Min(selectionStart, (uint)value.Length);
                var end = (int)Math.Min(selectionEnd, (uint)value.Length);
                return start < end ? value[start..end] : null;
            }

            default:
                return null;
        }
    }

    /// <summary>Sets the range of the selection (https://w3c.github.io/selection-api/#dfn-range) and its direction.</summary>
    private void SetRange(Range? range, SelectionDirection direction)
    {
        if (_range is not null && ReferenceEquals(_range.Selection, this))
        {
            _range.Selection = null;
        }

        _range = range;
        _direction = direction;
        _crossTreeAnchor = _crossTreeFocus = null;
        if (range is not null)
        {
            range.Selection = this;
        }

        // The selection that is set (by a script, the user or the user agent) has the focus (see HasEditingFocus)
        var hadEditingFocus = HasEditingFocus;
        HasEditingFocus = range is not null;
        OnChanged();
        if (hadEditingFocus != HasEditingFocus)
        {
            _document.OnCaretChanged();
        }
    }

    /// <summary>
    /// Whether the selection has the focus, for the display of its caret in editable content (see
    /// <see cref="Css.Layout.PaintedCaret"/>). Chrome and Firefox move the focus to the editing host of a selection set by a
    /// script (WPT selection/contenteditable/collapse.html), and display its caret while the focused element is in the editable
    /// content of the selection. The library does not move the focus (the Selection API does not specify it), but displays the
    /// caret as they do: the selection has the focus from the time it is set, or its editing host (or an editable ancestor of
    /// its focus in the host) is focused, until the focused area of the document changes to something else.
    /// </summary>
    internal bool HasEditingFocus { get; private set; }

    /// <summary>The focused area of the document changed (null: the viewport): updates <see cref="HasEditingFocus"/>.</summary>
    internal void OnFocusedAreaChanged(Element? focused)
    {
        var hasFocus = false;
        if (focused is not null && _range is not null)
        {
            // The focused element is the editing host of the focus of the selection, or between them
            for (var current = Focus.Node; current is not null && (Html.Editing.IsEditable(current) || Html.Editing.IsEditingHost(current)); current = current.ParentNode)
            {
                if (ReferenceEquals(current, focused))
                {
                    hasFocus = true;
                    break;
                }
            }
        }

        HasEditingFocus = hasFocus;
    }

    /// <summary>
    /// Called when the selection may have changed: its range was replaced or removed, or the boundary points of its range
    /// were changed by a <see cref="Range"/> method or a mutation of the tree.
    /// </summary>
    internal void OnChanged()
    {
        var state = _range is null
            ? new SelectionState(null, 0, null, 0, SelectionDirection.Directionless)
            : new SelectionState(_range.StartContainer, _range.StartOffset, _range.EndContainer, _range.EndOffset, _direction);
        if (state == _lastState)
            return;

        _lastState = state;
        _crossTreeAnchor = _crossTreeFocus = null;
        ScheduleSelectionChangeEvent(_document);

        // The selection is highlighted, and its caret is painted
        _document.OnCaretChanged();

        // https://drafts.csswg.org/css-contain-2/#relevant-to-the-user: the selected contents of content-visibility: auto are relevant
        _document.LayoutEngineOrNull?.OnContentRelevancyChanged();
    }

    /// <summary>https://w3c.github.io/selection-api/#scheduling-selectionhange-event</summary>
    /// <summary>
    /// Selects a range without a direction (https://w3c.github.io/selection-api/#dfn-direction: "if the user creates a
    /// selection by double clicking on a word, the selection is directionless"); its anchor is its start.
    /// </summary>
    internal void SelectDirectionless(Node startNode, uint startOffset, Node endNode, uint endOffset)
        => SetRange(new Range(startNode, startOffset, endNode, endOffset), SelectionDirection.Directionless);

    /// <summary>
    /// Fires selectstart at a node (https://w3c.github.io/selection-api/#selectstart-event): the user is about to select
    /// from a collapsed selection (or from none). Returns false when the event is canceled: the selection must not change.
    /// </summary>
    internal static bool FireSelectStart(Node target) => target.FireEvent(new Event("selectstart", new EventInit { Bubbles = true, Cancelable = true, Composed = true }));

    internal static void ScheduleSelectionChangeEvent(Node target)
    {
        // The "has scheduled selectionchange event" flags are stored by the document, so the nodes do not need a field
        var document = target as Document ?? target.NodeDocument;
        var scheduled = document.ScheduledSelectionChangeTargets ??= new HashSet<Node>(ReferenceEqualityComparer.Instance);
        if (!scheduled.Add(target))
            return;

        document.QueueElementTask(() =>
        {
            // https://w3c.github.io/selection-api/#firing-selectionhange-event
            scheduled.Remove(target);
            target.FireEvent(new Event("selectionchange", new EventInit { Bubbles = target is Element }));
        });
    }
}

// Extensions to other interfaces (https://w3c.github.io/selection-api/#extensions-to-other-interfaces)
public partial class Document
{
    private Selection? _selection;

    /// <summary>The targets that have a scheduled selectionchange event (https://w3c.github.io/selection-api/#dfn-has-scheduled-selectionchange-event).</summary>
    internal HashSet<Node>? ScheduledSelectionChangeTargets { get; set; }

    /// <summary>Returns the selection of the document, or null when the document has no browsing context.</summary>
    [IdlMember("getSelection", "https://w3c.github.io/selection-api/#dom-document-getselection")]
    public Selection? GetSelection() => BrowsingContext is null ? null : _selection ??= new Selection(this);

    /// <summary>The selection of the document when it was created, or null.</summary>
    internal Selection? SelectionOrNull => _selection;
}

public sealed partial class Window
{
    /// <summary>Returns the selection associated with the document of the window.</summary>
    [IdlMember("getSelection", "https://w3c.github.io/selection-api/#dom-window-getselection")]
    public Selection? GetSelection() => Document.GetSelection();
}

public sealed partial class ShadowRoot
{
    /// <summary>
    /// Returns the selection of the node document. The member was defined by the obsolete Shadow DOM specification and is
    /// implemented by Blink and WebKit, but the Selection API does not define it.
    /// </summary>
    [IdlMember("getSelection", "https://www.w3.org/TR/2014/WD-shadow-dom-20140617/#widl-ShadowRoot-getSelection-Selection")]
    [Experimental(ExperimentalApis.ShadowRootExtensions, UrlFormat = ExperimentalApis.UrlFormat)]
    public Selection? GetSelection() => NodeDocument.GetSelection();
}
