using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The focus navigation scopes of a document (https://html.spec.whatwg.org/multipage/interaction.html#focus-navigation-scope):
/// the elements of the flat tree by associated focus navigation owner, and the tabindex-ordered focus navigation scope of each
/// owner. The scope owners are the document, the shadow hosts, the slots, the triggers of the showing popovers and, for the
/// <c>reading-flow</c> property (https://drafts.csswg.org/css-display-4/#reading-flow, whose focus navigation is not specified
/// yet: the scopes are the ones of Chromium), the reading flow containers, the elements with <c>display: contents</c> whose
/// children are laid out by a reading flow container, and the reading flow items.
/// </summary>
/// <remarks>
/// As in the browsers, the fallback content of a slot is in the scope of the slot (the specification puts it in the scope of
/// the shadow host).
/// </remarks>
internal sealed class FocusNavigationScopes
{
    private readonly Document _document;
    private readonly Dictionary<Node, (int Start, int End)> _positions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, Node?> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, List<Element>> _scopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, List<Element>> _orderedScopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Element, bool> _sequentiallyFocusable = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _expanding = new(ReferenceEqualityComparer.Instance);

    // The reading flow container of the elements whose flat tree children are reading flow items: the reading flow containers
    // themselves, and the elements with display: contents whose children are laid out by a reading flow container
    private Dictionary<Element, Element>? _readingFlowParents;

    // The elements laid out by a reading flow container (their nearest flat tree ancestor that generates a box)
    private HashSet<Element>? _readingFlowItems;

    // The position of the box-generating items of each reading flow container in its reading flow
    private Dictionary<Element, Dictionary<Element, int>>? _readingFlowRanks;

    // The elements of the scope of each reading flow parent in the reading flow order (with the ones that are not in the tabindex-ordered scope)
    private Dictionary<Element, List<Element>>? _readingFlowOrders;

    public FocusNavigationScopes(Document document)
    {
        _document = document;
        Build();
    }

    /// <summary>The position of each node of the flat tree of the document (in pre-order), and the position of its last descendant.</summary>
    public Dictionary<Node, (int Start, int End)> Positions => _positions;

    /// <summary>The associated focus navigation owner of a node of the flat tree (https://html.spec.whatwg.org/multipage/interaction.html#associated-focus-navigation-owner), or null.</summary>
    public Node? GetOwner(Node node) => _owners.GetValueOrDefault(node);

    /// <summary>The focus navigation scope owner of the flat tree children of a node.</summary>
    public Node? GetOwnerOfChildren(Node node)
    {
        if (node is not Element element)
            return node is Document ? node : GetOwner(node);

        if (element is HtmlSlotElement || element.AttachedShadowRoot is not null || IsReadingFlowScopeOwner(element))
            return element;

        return ReferenceEquals(element, _document.DocumentElement) ? _document : GetOwner(element);
    }

    /// <summary>
    /// Whether the element is a focus navigation scope owner (https://html.spec.whatwg.org/multipage/interaction.html#focus-navigation-scope-owner):
    /// a shadow host, a slot, the trigger of a showing popover, or the owner of a scope of a reading flow.
    /// </summary>
    public bool IsScopeOwner(Element element)
        => element.AttachedShadowRoot is not null || element is HtmlSlotElement || IsReadingFlowScopeOwner(element) || element.NodeDocument.IsTriggerOfShowingPopover(element);

    /// <summary>Whether the focusable area is sequentially focusable (see <see cref="FocusModel.IsSequentiallyFocusable"/>).</summary>
    public bool IsSequentiallyFocusable(Element element)
    {
        if (!_sequentiallyFocusable.TryGetValue(element, out var result))
        {
            result = FocusModel.IsSequentiallyFocusable(element);
            _sequentiallyFocusable.Add(element, result);
        }

        return result;
    }

    /// <summary>
    /// The sequential focus navigation order of the document (https://html.spec.whatwg.org/multipage/interaction.html#sequential-focus-navigation-order):
    /// its flattened tabindex-ordered focus navigation scope.
    /// </summary>
    public List<Element> GetSequentialNavigationOrder()
    {
        var result = new List<Element>();
        Flatten(_document, result);
        return result;
    }

    /// <summary>The flattened tabindex-ordered focus navigation scope (https://html.spec.whatwg.org/multipage/interaction.html#flattened-tabindex-ordered-focus-navigation-scope).</summary>
    private void Flatten(Node owner, List<Element> result)
    {
        // The scopes can form a cycle (the trigger of a popover in a popover whose trigger is in the first popover)
        if (!_expanding.Add(owner))
            return;

        foreach (var item in GetTabIndexOrderedScope(owner))
        {
            if (IsSequentiallyFocusable(item))
            {
                result.Add(item);
            }

            if (IsScopeOwner(item))
            {
                Flatten(item, result);
            }
        }

        _expanding.Remove(owner);
    }

    /// <summary>
    /// The first focusable area of the flattened items of a tabindex-ordered focus navigation scope, from the item at an index:
    /// a focusable item precedes the contents of the scope it owns.
    /// </summary>
    public Element? GetFirstArea(List<Element> orderedScope, int index)
    {
        for (var i = Math.Max(index, 0); i < orderedScope.Count; i++)
        {
            var item = orderedScope[i];
            if (IsSequentiallyFocusable(item))
                return item;

            if (IsScopeOwner(item) && _expanding.Add(item))
            {
                var inner = GetFirstArea(GetTabIndexOrderedScope(item), 0);
                _expanding.Remove(item);
                if (inner is not null)
                    return inner;
            }
        }

        return null;
    }

    /// <summary>The last focusable area of the flattened items of a tabindex-ordered focus navigation scope, up to the item at an index.</summary>
    public Element? GetLastArea(List<Element> orderedScope, int index)
    {
        for (var i = Math.Min(index, orderedScope.Count - 1); i >= 0; i--)
        {
            var item = orderedScope[i];
            if (IsScopeOwner(item) && _expanding.Add(item))
            {
                var scope = GetTabIndexOrderedScope(item);
                var inner = GetLastArea(scope, scope.Count - 1);
                _expanding.Remove(item);
                if (inner is not null)
                    return inner;
            }

            if (IsSequentiallyFocusable(item))
                return item;
        }

        return null;
    }

    /// <summary>
    /// The tabindex-ordered focus navigation scope of an owner (https://html.spec.whatwg.org/multipage/interaction.html#tabindex-ordered-focus-navigation-scope):
    /// the scope owners and the focusable areas of the scope whose tabindex value is not negative, the ones with a positive
    /// tabindex first (in increasing order), then the others, in flat tree order. The items of a reading flow are in the
    /// reading flow order instead: their positive tabindex values are ignored.
    /// </summary>
    public List<Element> GetTabIndexOrderedScope(Node owner)
    {
        if (_orderedScopes.TryGetValue(owner, out var result))
            return result;

        result = [];
        if (_scopes.TryGetValue(owner, out var scope))
        {
            if (owner is Element ownerElement && GetReadingFlowOrder(ownerElement) is { } readingFlowOrder)
            {
                foreach (var element in readingFlowOrder)
                {
                    if (FocusModel.GetTabIndexValue(element) is not < 0 && (IsScopeOwner(element) || IsSequentiallyFocusable(element)))
                    {
                        result.Add(element);
                    }
                }
            }
            else
            {
                List<(Element Element, long TabIndex)>? positive = null;
                foreach (var element in scope)
                {
                    var tabIndex = FocusModel.GetTabIndexValue(element);
                    if (tabIndex < 0 || (!IsScopeOwner(element) && !IsSequentiallyFocusable(element)))
                        continue;

                    if (tabIndex > 0)
                    {
                        (positive ??= []).Add((element, tabIndex.Value));
                    }
                    else
                    {
                        result.Add(element);
                    }
                }

                if (positive is not null)
                {
                    // A stable sort: the positive tabindex values first, in increasing order
                    result.InsertRange(0, positive.Select((item, index) => (item.Element, item.TabIndex, index)).OrderBy(item => item.TabIndex).ThenBy(item => item.index).Select(item => item.Element));
                }
            }
        }

        _orderedScopes.Add(owner, result);
        return result;
    }

    /// <summary>
    /// The position of an element in the reading flow order of the scope of an owner, for the "DOM" selection mechanism of
    /// the sequential navigation search algorithm; null when the owner has no reading flow, or for a node that is not in it.
    /// </summary>
    public int? GetReadingFlowPosition(Node owner, Node node)
    {
        if (owner is Element ownerElement && node is Element element && GetReadingFlowOrder(ownerElement) is { } readingFlowOrder && readingFlowOrder.IndexOf(element) is var index and >= 0)
            return index;

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scopes
    // ----------------------------------------------------------------------------------------------------------------

    private void Build()
    {
        // The reading flows need the computed styles: the elements of a document that is not rendered have none
        var hasStyles = _document.DocumentElement is { } root && ComputedStyleDeclarations.IsRendered(root);
        var styleEngine = hasStyles ? _document.GetStyleEngine() : null;

        // A traversal of the flat tree in pre-order: the layout parent of a node is its nearest ancestor that generates a box
        var stack = new Stack<(Node Node, Node? Parent, Element? LayoutParent, bool Exit)>();
        stack.Push((_document, null, null, false));
        var index = 0;
        while (stack.Count > 0)
        {
            var (node, parent, layoutParent, exit) = stack.Pop();
            if (exit)
            {
                _positions[node] = (_positions[node].Start, index - 1);
                continue;
            }

            _positions[node] = (index, index);
            index++;

            var childLayoutParent = layoutParent;
            if (node is Element element)
            {
                if (styleEngine is not null)
                {
                    var style = styleEngine.GetStyle(element);
                    var display = style.Layout.Display;
                    var isItem = layoutParent is not null && _readingFlowParents is not null && _readingFlowParents.ContainsKey(layoutParent);
                    if (isItem)
                    {
                        (_readingFlowItems ??= new(ReferenceEqualityComparer.Instance)).Add(element);
                    }

                    if (display.GeneratesBox)
                    {
                        childLayoutParent = element;
                        if (GetReadingFlow(style) != ReadingFlow.Normal)
                        {
                            (_readingFlowParents ??= new(ReferenceEqualityComparer.Instance))[element] = element;
                        }
                    }
                    else if (display.IsContents && isItem)
                    {
                        _readingFlowParents![element] = layoutParent!;
                    }
                }

                var owner = parent is null ? null : GetOwner(element, parent);
                _owners[element] = owner;
                if (owner is not null)
                {
                    if (!_scopes.TryGetValue(owner, out var scope))
                    {
                        scope = [];
                        _scopes.Add(owner, scope);
                    }

                    scope.Add(element);
                }
            }
            else if (parent is not null)
            {
                _owners[node] = GetOwnerOfChildren(parent);
            }

            stack.Push((node, parent, layoutParent, true));

            // The children are pushed in reverse order, so they are visited in order
            if (node is Element { AttachedShadowRoot: { } shadowRoot })
            {
                for (var child = shadowRoot.LastChild; child is not null; child = child.PreviousSibling)
                {
                    stack.Push((child, node, childLayoutParent, false));
                }
            }
            else if (node is HtmlSlotElement { AssignedNodesList.Count: > 0 } slot)
            {
                for (var i = slot.AssignedNodesList.Count - 1; i >= 0; i--)
                {
                    stack.Push((slot.AssignedNodesList[i], node, childLayoutParent, false));
                }
            }
            else
            {
                for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
                {
                    stack.Push((child, node, childLayoutParent, false));
                }
            }
        }
    }

    /// <summary>
    /// The associated focus navigation owner of an element (https://html.spec.whatwg.org/multipage/interaction.html#associated-focus-navigation-owner),
    /// from its parent in the flat tree, whose owner is known.
    /// </summary>
    private Node? GetOwner(Element element, Node parent)
    {
        if (parent is HtmlSlotElement slot)
        {
            // As in Chromium, a slot without box is transparent in a reading flow: its assigned nodes are in the scope of its parent
            if (!ReferenceEquals(element.ParentNode, slot)
                && _readingFlowParents is not null
                && GetFlatTreeParent(slot) is Element slotParent
                && IsReadingFlowScopeOwner(slotParent)
                && !_document.GetStyleEngine().GetStyle(slot).Layout.Display.GeneratesBox)
            {
                return slotParent;
            }

            // "If element's parent is a shadow host, then return element's assigned slot"; as in the browsers, the fallback
            // content of a slot is in the scope of the slot
            return slot;
        }

        // "If element's parent is a shadow root, then return the parent's host"
        if (element.ParentNode is ShadowRoot shadowRoot)
            return shadowRoot.Host;

        // The children of the owner of a scope of a reading flow, except the showing popovers, which are in the top layer. As
        // in Chromium, a popover that contains its trigger is not in the scope of the trigger (the trigger would own itself).
        var isShowingPopover = element is HtmlElement { IsPopoverShowing: true, PopoverTrigger: { } trigger } && !element.IsShadowIncludingInclusiveAncestorOf(trigger);
        if (!isShowingPopover && parent is Element parentElement && IsReadingFlowScopeOwner(parentElement))
            return parentElement;

        // "If element's parent is the document element, then return the parent's node document"
        if (parent is Document || ReferenceEquals(parent, _document.DocumentElement))
            return _document;

        // "If element is in the popover showing state and has a popover trigger set, then return element's popover trigger"
        if (isShowingPopover)
            return ((HtmlElement)element).PopoverTrigger;

        // "Return element's parent's associated focus navigation owner"
        return _owners.GetValueOrDefault(parent);
    }

    private static Node? GetFlatTreeParent(Element element)
    {
        if (element.Slottable?.AssignedSlot is { } slot)
            return slot;

        return element.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : element.ParentNode;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Reading flow (https://drafts.csswg.org/css-display-4/#reading-flow)
    // ----------------------------------------------------------------------------------------------------------------

    private enum ReadingFlow
    {
        Normal,
        SourceOrder,
        FlexVisual,
        FlexFlow,
        GridRows,
        GridColumns,
        GridOrder,
    }

    /// <summary>
    /// The reading flow of a box: not <see cref="ReadingFlow.Normal"/> when the box is a reading flow container
    /// (https://drafts.csswg.org/css-display-4/#reading-flow-container: "a block, flex or grid container with a valid
    /// reading-flow value other than normal"; the flex-* values only take effect on flex containers, and the grid-* values on
    /// grid containers).
    /// </summary>
    private static ReadingFlow GetReadingFlow(ComputedStyle style)
    {
        var value = style.GetComputedValue("reading-flow");
        if (value == "normal")
            return ReadingFlow.Normal;

        var display = style.Layout.Display;
        if (!display.GeneratesBox || display.Internal is not (DisplayInternal.None or DisplayInternal.TableCell or DisplayInternal.TableCaption))
            return ReadingFlow.Normal;

        return (display.Inside, value) switch
        {
            (DisplayInside.Flex or DisplayInside.Grid or DisplayInside.FlowRoot, "source-order") => ReadingFlow.SourceOrder,
            (DisplayInside.Flow, "source-order") when !display.IsInlineBox => ReadingFlow.SourceOrder,
            (DisplayInside.Flex, "flex-visual") => ReadingFlow.FlexVisual,
            (DisplayInside.Flex, "flex-flow") => ReadingFlow.FlexFlow,
            (DisplayInside.Grid, "grid-rows") => ReadingFlow.GridRows,
            (DisplayInside.Grid, "grid-columns") => ReadingFlow.GridColumns,
            (DisplayInside.Grid, "grid-order") => ReadingFlow.GridOrder,
            _ => ReadingFlow.Normal,
        };
    }

    /// <summary>
    /// Whether the element owns a scope of a reading flow: it is a reading flow container, an element with
    /// <c>display: contents</c> whose children are laid out by a reading flow container, or a reading flow item (the scope of
    /// its descendants).
    /// </summary>
    private bool IsReadingFlowScopeOwner(Element element)
        => (_readingFlowParents is not null && _readingFlowParents.ContainsKey(element)) || (_readingFlowItems is not null && _readingFlowItems.Contains(element));

    /// <summary>
    /// The elements of the scope of a reading flow container, or of an element with <c>display: contents</c> whose children
    /// are laid out by a reading flow container, in the reading flow order; null for the other owners. As in Chromium, the
    /// elements are sorted by <c>reading-order</c>; the ones with the same value are in the order of the reading flow of the
    /// container (an element with <c>display: contents</c> is at the position of its first item), followed by the elements
    /// that are not in the reading flow (e.g. the absolutely positioned boxes), in tree order.
    /// </summary>
    private List<Element>? GetReadingFlowOrder(Element owner)
    {
        if (_readingFlowParents is null || !_readingFlowParents.TryGetValue(owner, out var container))
            return null;

        if ((_readingFlowOrders ??= new(ReferenceEqualityComparer.Instance)).TryGetValue(owner, out var result))
            return result;

        var ranks = GetReadingFlowRanks(container);
        var styleEngine = _document.GetStyleEngine();
        var scope = _scopes.GetValueOrDefault(owner) ?? [];
        var items = new List<(Element Element, int ReadingOrder, int Rank, int Index)>(scope.Count);
        for (var i = 0; i < scope.Count; i++)
        {
            var element = scope[i];
            var readingOrder = int.TryParse(styleEngine.GetStyle(element).GetComputedValue("reading-order"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0;
            items.Add((element, readingOrder, GetRank(element), i));
        }

        result = items.OrderBy(item => item.ReadingOrder).ThenBy(item => item.Rank).ThenBy(item => item.Index).Select(item => item.Element).ToList();
        _readingFlowOrders.Add(owner, result);
        return result;

        // The position of the first item of the reading flow in the element (the element itself when it generates a box)
        int GetRank(Element element)
        {
            if (ranks.TryGetValue(element, out var rank))
                return rank;

            var first = int.MaxValue;
            if (_readingFlowParents.ContainsKey(element) && !ReferenceEquals(element, container))
            {
                foreach (var (item, itemRank) in ranks)
                {
                    if (itemRank < first && IsInContents(item, element))
                    {
                        first = itemRank;
                    }
                }
            }

            return first;
        }

        // Whether the element with display: contents is between the item and its reading flow container, in the flat tree
        bool IsInContents(Element item, Element contents)
        {
            for (var parent = GetFlatTreeParent(item); parent is Element parentElement && !ReferenceEquals(parentElement, container); parent = GetFlatTreeParent(parentElement))
            {
                if (ReferenceEquals(parentElement, contents))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The position of the items of a reading flow container in its reading flow (https://drafts.csswg.org/css-display-4/#reading-flow):
    /// the in-flow boxes it lays out. <c>source-order</c> follows the flat tree; <c>flex-flow</c> and <c>grid-order</c> follow the
    /// order-modified document order; <c>flex-visual</c> follows the flex lines and the items of each line in the writing mode
    /// of the container (the order of the items of a line is reversed by <c>row-reverse</c> and <c>column-reverse</c>, the one of
    /// the lines by <c>wrap-reverse</c>); <c>grid-rows</c> and <c>grid-columns</c> follow the rows (then the columns) or the
    /// columns (then the rows) of the grid areas of the items.
    /// </summary>
    private Dictionary<Element, int> GetReadingFlowRanks(Element container)
    {
        if ((_readingFlowRanks ??= new(ReferenceEqualityComparer.Instance)).TryGetValue(container, out var ranks))
            return ranks;

        var styleEngine = _document.GetStyleEngine();
        var containerStyle = styleEngine.GetStyle(container);
        var flow = GetReadingFlow(containerStyle);

        // The items, in flat tree order
        var items = new List<Element>();
        if (_readingFlowItems is not null)
        {
            Collect(container);
        }

        if (flow != ReadingFlow.SourceOrder)
        {
            // https://drafts.csswg.org/css-display-4/#order-modified-document-order
            items = items.Select((item, index) => (item, index)).OrderBy(entry => styleEngine.GetStyle(entry.item).Layout.Flex.Order).ThenBy(entry => entry.index).Select(entry => entry.item).ToList();
        }

        switch (flow)
        {
            case ReadingFlow.FlexVisual:
                items = SortFlexItemsVisually(container, containerStyle, items);
                break;

            case ReadingFlow.GridRows or ReadingFlow.GridColumns:
                items = SortGridItems(container, items, byRows: flow == ReadingFlow.GridRows);
                break;
        }

        ranks = new Dictionary<Element, int>(items.Count, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < items.Count; i++)
        {
            ranks.Add(items[i], i);
        }

        _readingFlowRanks.Add(container, ranks);
        return ranks;

        void Collect(Element parent)
        {
            foreach (var element in _scopes.GetValueOrDefault(parent) ?? [])
            {
                if (!_readingFlowItems.Contains(element))
                    continue;

                var layout = styleEngine.GetStyle(element).Layout;
                if (layout.Display.GeneratesBox)
                {
                    // The absolutely positioned boxes are not laid out by the container
                    if (!layout.IsAbsolutelyPositioned)
                    {
                        items.Add(element);
                    }
                }
                else if (_readingFlowParents!.ContainsKey(element))
                {
                    Collect(element);
                }
            }
        }
    }

    /// <summary>
    /// Sorts the items of a flex container (in order-modified document order) in the visual order of <c>reading-flow: flex-visual</c>,
    /// from the fragments of the layout: a flex line ends when the next item does not follow the previous one in the main axis.
    /// </summary>
    private List<Element> SortFlexItemsVisually(Element container, ComputedStyle containerStyle, List<Element> items)
    {
        var flex = containerStyle.Layout.Flex;
        if (flex.Wrap == FlexWrap.NoWrap)
        {
            if (flex.IsReverse)
            {
                items.Reverse();
            }

            return items;
        }

        var layoutEngine = _document.GetLayoutEngine();
        if (layoutEngine.GetFragments(container) is not [var containerFragment, ..])
            return items;

        var writingDirection = WritingDirection.FromStyle(containerStyle.Layout);
        var origin = containerFragment.GetOffsetInRoot();
        var lines = new List<List<Element>>();
        (double Main, double Cross)? previous = null;
        foreach (var item in items)
        {
            if (layoutEngine.GetFragments(item) is not [var fragment, ..])
                continue;

            var rect = fragment.GetRectInRoot();
            var offset = writingDirection.ToLogical(new PhysicalRect(new PhysicalOffset(rect.X - origin.X, rect.Y - origin.Y), rect.Size), containerFragment.Size).Offset;
            var (main, cross) = flex.IsRow ? (offset.Inline, offset.Block) : (offset.Block, offset.Inline);
            var follows = previous is { } last && (cross == last.Cross || (flex.IsReverse ? main < last.Main : main > last.Main));
            if (!follows)
            {
                lines.Add([]);
            }

            lines[^1].Add(item);
            previous = (main, cross);
        }

        if (flex.Wrap == FlexWrap.WrapReverse)
        {
            lines.Reverse();
        }

        var result = new List<Element>(items.Count);
        foreach (var line in lines)
        {
            if (flex.IsReverse)
            {
                line.Reverse();
            }

            result.AddRange(line);
        }

        return result;
    }

    /// <summary>
    /// Sorts the items of a grid container (in order-modified document order) by the first row then the first column of their
    /// grid areas (<c>reading-flow: grid-rows</c>), or by the first column then the first row (<c>grid-columns</c>).
    /// </summary>
    private List<Element> SortGridItems(Element container, List<Element> items, bool byRows)
    {
        var layoutEngine = _document.GetLayoutEngine();
        if (layoutEngine.GetFragments(container) is not [var containerFragment, ..] || containerFragment.Box.LayoutState is not GridContainerState { ItemRows: { } areas })
            return items;

        var boxTree = layoutEngine.BoxTree;
        return items
            .Select((item, index) => (item, index, area: boxTree?.GetPrincipalBox(item) is { } box && areas.TryGetValue(box, out var area) ? area : (GridItemRows?)null))
            .OrderBy(entry => entry.area is { } area ? (byRows ? area.Start : area.ColumnStart) : int.MaxValue)
            .ThenBy(entry => entry.area is { } area ? (byRows ? area.ColumnStart : area.Start) : int.MaxValue)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item)
            .ToList();
    }
}
