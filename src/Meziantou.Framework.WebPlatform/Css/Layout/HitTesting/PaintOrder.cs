namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The painting order of the fragments (https://drafts.csswg.org/css2/#elaborate-stacking-contexts, CSS 2 Appendix E): the
/// box and text fragments from the bottom to the top, by stacking context (the negative <c>z-index</c> stacking contexts,
/// the in-flow block-level boxes, the floats, the inline content, the positioned boxes and the stacking contexts with
/// <c>z-index: auto</c> or 0 in tree order, the positive <c>z-index</c> stacking contexts), then the top layer. Used by the
/// hit testing (which walks it from the top), and by the painting (<see cref="Painter"/>).
/// </summary>
/// <remarks>
/// The floats, the atomic inlines and the positioned boxes with <c>z-index: auto</c> are painted as if they established a
/// stacking context, but their positioned descendants and the stacking contexts they contain belong to the parent
/// stacking context. The flex items are painted the same way as the atomic inlines, in order-modified document order (the
/// order of their fragments, https://drafts.csswg.org/css-flexbox-1/#painting). The line boxes are not part of the list
/// (their contents are), nor the fragments without box.
/// </remarks>
/// <summary>
/// An item of the painting order: a box or text fragment, or the band of a block split out of inline boxes
/// (<see cref="IsSplitInlineBand"/>: the area of the anonymous block box that the inline boxes contain, across their block
/// container, https://drafts.csswg.org/css2/#anonymous-block-level), which belongs to the inline boxes.
/// </summary>
internal readonly record struct PaintItem(Fragment Fragment, bool IsSplitInlineBand = false)
{
    /// <summary>
    /// Whether the item marks the start or the end of a stacking context (only when the painting order is collected with the
    /// markers, for the painting): <see cref="Fragment"/> is the root of the stacking context, its own item follows the start marker.
    /// </summary>
    public PaintItemMarker Marker { get; init; }

    /// <summary>
    /// The truncation of the line of a text or of an inline box (<c>text-overflow</c>, <see cref="LineTruncator"/>) that clips it,
    /// or null (only when the painting order is collected with the markers, for the painting).
    /// </summary>
    public LineTruncation? Truncation { get; init; }

    /// <summary>The part of the box that the item paints: a block-level replaced box has two items (see <see cref="PaintItemPart"/>).</summary>
    public PaintItemPart Part { get; init; }
}

/// <summary>
/// The parts of a box painted by an item of the painting order (https://drafts.csswg.org/css2/#elaborate-stacking-contexts): the
/// background and the borders of a block-level replaced box are painted with the ones of the in-flow blocks (step 4), "the
/// replaced content, atomically" with the inline content (step 7).
/// </summary>
internal enum PaintItemPart : byte
{
    /// <summary>The whole box.</summary>
    All,

    /// <summary>The background and the borders of a block-level replaced box.</summary>
    Decorations,

    /// <summary>The replaced content of a block-level replaced box.</summary>
    ReplacedContent,

    /// <summary>
    /// The backplates of the text of the lines of a block container in forced colors mode
    /// (https://drafts.csswg.org/css-color-adjust-1/#forced), painted between the backgrounds of the blocks and the floats (only
    /// when the painting order is collected for the painting).
    /// </summary>
    Backplate,
}

/// <summary>The markers of the bounds of the stacking contexts in a painting order (see <see cref="PaintItem.Marker"/>).</summary>
internal enum PaintItemMarker : byte
{
    /// <summary>The item is a fragment to paint.</summary>
    None,

    /// <summary>The stacking context of the fragment starts: its effects (transform, opacity...) apply to the following items.</summary>
    BeginStackingContext,

    /// <summary>The stacking context of the fragment ends.</summary>
    EndStackingContext,

    /// <summary>
    /// The outlines of the boxes painted since the previous outlines of the stacking context are painted: the in-flow content of
    /// a stacking context or of a positioned box ends (as in Blink and WebKit, the outlines of a layer are painted above its
    /// in-flow content and below the positioned boxes that follow it, rather than at the end of the stacking context, the last
    /// step of https://drafts.csswg.org/css2/#elaborate-stacking-contexts).
    /// </summary>
    Outlines,
}

internal static class PaintOrder
{
    /// <summary>Appends the fragments of the fragment tree whose root is <paramref name="root"/> to <paramref name="output"/>, in painting order (the bottommost first).</summary>
    /// <param name="markers">Whether the bounds of the stacking contexts are marked (see <see cref="PaintItem.Marker"/>).</param>
    public static void Collect(BoxFragment root, List<PaintItem> output, bool markers = false)
    {
        // The boxes of the top layer are painted above everything else, in the order of the top layer
        // (https://drafts.csswg.org/css-position-4/#top-layer)
        List<BoxFragment>? topLayer = null;
        foreach (var child in root.ChildrenSpan)
        {
            if (child is BoxFragment { Box.IsInTopLayer: true } box)
            {
                (topLayer ??= []).Add(box);
            }
        }

        if (topLayer is null)
        {
            var context = new TreeOrder();
            ComputeAdoptions(root, excluded: null, context);
            PaintStackingContext(root, output, excluded: null, markers, context);
            return;
        }

        // The fixed positioned descendants of a box of the top layer whose containing block is the viewport are children of
        // the root fragment, but they belong to the stacking context of the box of the top layer
        var excluded = new List<BoxFragment>(topLayer);
        Dictionary<BoxFragment, List<BoxFragment>>? adopted = null;
        foreach (var child in root.ChildrenSpan)
        {
            if (child is not BoxFragment { Box.IsInTopLayer: false } box)
                continue;

            for (var ancestor = box.Box.Parent; ancestor is not null && ancestor.Kind != ContainerBoxKind.Viewport; ancestor = ancestor.Parent)
            {
                if (!ancestor.IsInTopLayer)
                    continue;

                if (topLayer.Find(fragment => ReferenceEquals(fragment.Box, ancestor)) is { } owner)
                {
                    excluded.Add(box);
                    adopted ??= new(ReferenceEqualityComparer.Instance);
                    if (!adopted.TryGetValue(owner, out var list))
                    {
                        adopted[owner] = list = [];
                    }

                    list.Add(box);
                }

                break;
            }
        }

        var treeOrder = new TreeOrder();
        ComputeAdoptions(root, excluded, treeOrder);
        PaintStackingContext(root, output, excluded, markers, treeOrder);
        foreach (var box in topLayer)
        {
            if (!box.IsForceHidden)
            {
                PaintStackingContext(box, output, excluded: null, markers, treeOrder, adopted?.GetValueOrDefault(box));
            }
        }
    }

    /// <summary>
    /// Computes the layers that belong to another stacking context than the one of their containing block chain
    /// (https://drafts.csswg.org/css2/#stacking-context: a stacking context contains its descendants in the DOM): an absolutely
    /// positioned box whose containing block is outside of its nearest ancestor stacking context (e.g. a box positioned relative
    /// to the initial containing block in the stacking context of the root element, or in an element with opacity) is a child of
    /// the fragment of its containing block, but it is painted in its ancestor stacking context
    /// (<see cref="TreeOrder.Adopted"/>), and not in the stacking context of its containing block (<see cref="TreeOrder.Moved"/>).
    /// </summary>
    private static void ComputeAdoptions(BoxFragment root, List<BoxFragment>? excluded, TreeOrder context)
    {
        // A depth-first traversal of the fragment tree with the stacking context that collects the layers of each fragment
        var stack = new Stack<(Fragment Fragment, BoxFragment StackingContext)>();
        stack.Push((root, root));
        while (stack.TryPop(out var entry))
        {
            var (fragment, stackingContext) = entry;

            // The children of a replaced box (the foreignObject boxes of an svg element) are painted by its contents; the
            // ones of a form control whose parts are boxes are painted as the contents of the other boxes
            if (fragment is BoxFragment { Box.Kind: ContainerBoxKind.Replaced } replaced && !ReferenceEquals(fragment, root) && !FormControlContents.HasContentsFragment(replaced))
                continue;

            var children = fragment.ChildrenSpan;
            for (var i = children.Length - 1; i >= 0; i--)
            {
                var child = children[i];

                // The content hidden after the clamp point of a line-clamp container is not painted (see LineClamp)
                if (child.IsHiddenForPaint)
                    continue;

                var childContext = stackingContext;
                if (child is BoxFragment excludedBox && (excluded?.Contains(excludedBox) == true || excludedBox.Box.IsInTopLayer))
                {
                    // The boxes of the top layer and their adopted descendants are painted in their own stacking contexts
                    childContext = excludedBox.EstablishesStackingContext ? excludedBox : GetAncestorStackingContext(excludedBox) ?? stackingContext;
                }
                else if (child is BoxFragment box && IsLayer(box))
                {
                    // (a stacking context split by the fragmentation paints the adopted layers with its last fragment: above
                    // all its fragments; a page whose tree has no fragment of it paints them in the stacking context of the page)
                    var owner = GetAncestorStackingContext(box);
                    if (owner is not null && !ReferenceEquals(owner.Box, stackingContext.Box) && GetLastFragmentInTree(owner, root) is { } treeOwner)
                    {
                        owner = treeOwner;
                        context.Moved.Add(box);
                        context.Adopted ??= new(ReferenceEqualityComparer.Instance);
                        if (!context.Adopted.TryGetValue(owner, out var list))
                        {
                            context.Adopted[owner] = list = [];
                        }

                        list.Add(box);
                        childContext = owner;
                        MarkHiddenContexts(owner, context);
                    }

                    if (box.EstablishesStackingContext)
                    {
                        childContext = box;
                    }
                }

                if (child is BoxFragment { Box.SkipsContents: true })
                    continue;

                stack.Push((child, childContext));
            }
        }
    }

    /// <summary>
    /// Records the stacking context that adopts a layer when it is hidden after the clamp point of a line-clamp container
    /// (<see cref="Fragment.IsHiddenForPaint"/>), with its ancestor stacking contexts in the hidden content
    /// (<see cref="TreeOrder.HiddenContexts"/>): https://drafts.csswg.org/css-overflow-4/#line-clamp, an absolutely positioned box
    /// whose containing block is not hidden is visible, and the effects of its hidden ancestors (e.g. the opacity of an inline box)
    /// still apply to it (WPT css/css-overflow/line-clamp/line-clamp-with-abspos-024.html).
    /// </summary>
    private static void MarkHiddenContexts(BoxFragment owner, TreeOrder context)
    {
        var isHidden = false;
        for (Fragment? current = owner; current is not null; current = current.Parent)
        {
            if (current.IsHiddenForPaint)
            {
                isHidden = true;
                break;
            }
        }

        if (!isHidden)
            return;

        context.HiddenContexts ??= new(ReferenceEqualityComparer.Instance);
        for (Fragment? current = owner; current is not null; current = current.Parent)
        {
            if (current is BoxFragment { EstablishesStackingContext: true } box)
            {
                context.HiddenContexts.Add(box);
            }

            if (current.IsHiddenForPaint)
                break;
        }
    }

    /// <summary>
    /// Collects the outermost stacking contexts of the content of a fragment hidden after the clamp point of a line-clamp container
    /// that adopt a visible layer (<see cref="TreeOrder.HiddenContexts"/>): only their adopted layers are painted.
    /// </summary>
    private static void CollectHiddenLayers(Fragment fragment, List<(BoxFragment Fragment, int ZIndex)> layers, TreeOrder treeOrder)
    {
        if (treeOrder.HiddenContexts is not { } hiddenContexts)
            return;

        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is BoxFragment box && hiddenContexts.Contains(box))
            {
                layers.Add((box, box.ZIndex ?? 0));
                continue;
            }

            CollectHiddenLayers(child, layers, treeOrder);
        }
    }

    /// <summary>Returns the fragment of the nearest ancestor of a box in the box tree that establishes a stacking context, or null.</summary>
    /// <summary>
    /// Returns the last fragment of the box of a fragment that is in the fragment tree of <paramref name="root"/>, or null when
    /// there is none: the pages of a paginated document are separate trees, whose fragments of a box split by the pages are
    /// each painted with their page.
    /// </summary>
    private static BoxFragment? GetLastFragmentInTree(BoxFragment fragment, BoxFragment root)
    {
        if (fragment.Box.LastFragment is BoxFragment last && IsInTree(last, root))
            return last;

        BoxFragment? result = null;
        for (var other = fragment.Box.FirstFragment; other is not null; other = other.NextFragmentOfBox)
        {
            if (other is BoxFragment candidate && IsInTree(candidate, root))
            {
                result = candidate;
            }
        }

        return result;

        static bool IsInTree(Fragment fragment, BoxFragment root)
        {
            var current = fragment;
            while (current.Parent is { } parent)
            {
                current = parent;
            }

            return ReferenceEquals(current, root);
        }
    }

    private static BoxFragment? GetAncestorStackingContext(BoxFragment fragment)
    {
        for (var box = fragment.Box.Parent; box is not null; box = box.Parent)
        {
            if (box.IsAnonymous || box.Kind == ContainerBoxKind.TableGrid)
                continue;

            if (box.FirstFragment is BoxFragment ancestor && ancestor.EstablishesStackingContext)
                return ancestor;
        }

        return null;
    }

    /// <summary>
    /// Appends the fragments of a stacking context and of its descendants in painting order (the fragment of a foreignObject
    /// element, painted by the SVG painting of its svg element, see <see cref="SvgForeignObjectLayout"/>).
    /// </summary>
    public static void CollectStackingContext(BoxFragment context, List<PaintItem> output, bool markers = false) => PaintStackingContext(context, output, excluded: null, markers, new TreeOrder());

    /// <summary>Whether a fragment is a stacking context root, or a positioned box that is painted in the lists of its stacking context.</summary>
    private static bool IsLayer(BoxFragment fragment) => fragment.EstablishesStackingContext || IsPositioned(fragment);

    private static bool IsPositioned(BoxFragment fragment)
        => fragment.Box is { Kind: not ContainerBoxKind.TableGrid and not ContainerBoxKind.Viewport, IsAnonymous: false } box && box.Style.Layout.IsPositioned;

    /// <summary>Whether a fragment is the fragment of an in-flow block-level box split out of an inline box (see <see cref="ContainerBox.SplitInlineParent"/>).</summary>
    private static bool IsSplitOutOfInline(BoxFragment fragment) => fragment.Box is { SplitInlineParent: not null, IsBlockLevel: true, IsInFlow: true };

    /// <summary>
    /// Returns the first part of the nearest inline box a block was split out of (see <see cref="ContainerBox.SplitInlineParent"/>)
    /// that is a layer (a stacking context or a positioned box), or null: the block is painted in the layer of the inline box,
    /// as its descendant (https://drafts.csswg.org/css2/#anonymous-block-level: "when such an inline box is affected by relative
    /// positioning, any resulting translation also affects the block-level box contained in the inline box"; its opacity, its
    /// filters and its z-index apply to the block too, as in the browsers).
    /// </summary>
    private static ContainerBox? GetSplitInlineLayerBox(ContainerBox block)
    {
        for (var inline = block.SplitInlineParent; inline is { IsInlineBox: true }; inline = inline.Parent)
        {
            var first = inline;
            while (first.PreviousContinuation is { } previous)
            {
                first = previous;
            }

            if (first.FirstFragment is BoxFragment fragment && IsLayer(fragment))
                return first;
        }

        return null;
    }

    /// <summary>Whether a fragment is painted in the layer of an inline box it was split out of (see <see cref="GetSplitInlineLayerBox"/>).</summary>
    private static bool IsPaintedInInlineLayer(BoxFragment fragment)
        => fragment.Box.SplitInlineParent is not null && IsSplitOutOfInline(fragment) && !IsLayer(fragment) && ReferenceEquals(fragment.Box.FirstFragment, fragment) && GetSplitInlineLayerBox(fragment.Box) is not null;

    /// <summary>Returns the fragments of the blocks split out of an inline box that are painted in its layer, when the fragment is the first fragment of the inline box.</summary>
    private static List<BoxFragment>? GetBlocksPaintedInInlineLayer(BoxFragment layer)
    {
        var box = layer.Box;
        if (!box.IsInlineBox || box.Continuation is null || box.PreviousContinuation is not null || !ReferenceEquals(box.FirstFragment, layer))
            return null;

        // The blocks are the siblings of the anonymous block box of the first part of the inline box
        var outermost = box;
        while (outermost.Parent is { IsInlineBox: true } parent)
        {
            outermost = parent;
        }

        List<BoxFragment>? blocks = null;
        for (var sibling = outermost.Parent?.NextSibling; sibling is not null; sibling = sibling.NextSibling)
        {
            if (sibling is ContainerBox { SplitInlineParent: not null, FirstFragment: BoxFragment fragment } block && IsPaintedInInlineLayer(fragment) && ReferenceEquals(GetSplitInlineLayerBox(block), box))
            {
                (blocks ??= []).Add(fragment);
            }
        }

        return blocks;
    }

    /// <summary>Paints the blocks split out of an inline box in its layer, below its inline content: their backgrounds, their floats and their inline content.</summary>
    private static void PaintBlocksOfInlineLayer(List<BoxFragment>? blocks, List<PaintItem> output, bool markers)
    {
        if (blocks is null)
            return;

        foreach (var block in blocks)
        {
            output.Add(new PaintItem(block, IsSplitInlineBand: true));
            output.Add(new PaintItem(block));
            if (!block.Box.SkipsContents)
            {
                PaintBlocks(block, output);
                PaintFloats(block, output, markers);
                PaintInlines(block, output, markers);
            }
        }
    }

    private static bool IsFloat(BoxFragment fragment)
        => fragment.Box is { IsAnonymous: false, Kind: not ContainerBoxKind.TableGrid } box && box.Style.Layout.IsFloating && box.IsOutOfFlow;

    /// <summary>
    /// Whether the children of a fragment are flex or grid items, which are painted as atomic inlines in the inline content phase
    /// (https://drafts.csswg.org/css-flexbox-1/#painting: "Flex items paint exactly the same as inline blocks, except that
    /// order-modified document order is used in place of raw document order"; https://drafts.csswg.org/css-grid-2/#z-order: "The
    /// painting order of grid items is exactly the same as inline blocks"). The fragments of the items are in order-modified
    /// document order. As in Blink, the rendered legend and the anonymous fieldset content box of a fieldset are painted the same
    /// way (the legend, with its floats, is under the content box where they overlap, and over the floats that precede the
    /// fieldset: WPT html/rendering/non-replaced-elements/the-fieldset-and-legend-elements/fieldset-painting-order.html and
    /// legend-painting-order.html).
    /// </summary>
    private static bool PaintsChildrenAtomically(Fragment fragment)
        => fragment is BoxFragment { Box: { } box } && (FormattingContext.For(box).Kind is FormattingContextKind.Flex or FormattingContextKind.Grid || BlockFormattingContext.IsFieldset(box));

    /// <summary>https://drafts.csswg.org/css2/#elaborate-stacking-contexts: paints a stacking context.</summary>
    /// <param name="continuations">The other fragments of the inline box of <paramref name="context"/>, whose stacking contexts are painted in its group (see <see cref="PaintLayerStackingContext"/>).</param>
    private static void PaintStackingContext(BoxFragment context, List<PaintItem> output, List<BoxFragment>? excluded, bool markers, TreeOrder treeOrder, List<BoxFragment>? adopted = null, List<(BoxFragment Fragment, int ZIndex)>? continuations = null)
    {
        if (treeOrder.Adopted?.GetValueOrDefault(context) is { } adoptedLayers)
        {
            adopted = adopted is null ? adoptedLayers : [.. adopted, .. adoptedLayers];
        }

        if (markers)
        {
            output.Add(new PaintItem(context) { Marker = PaintItemMarker.BeginStackingContext });
        }

        // A stacking context hidden after the clamp point of a line-clamp container only paints the visible layers it adopts
        if (treeOrder.HiddenContexts?.Contains(context) == true)
        {
            PaintHiddenStackingContextContents(context, output, markers, treeOrder, adopted);
            if (markers)
            {
                output.Add(new PaintItem(context) { Marker = PaintItemMarker.EndStackingContext });
            }

            return;
        }

        output.Add(new PaintItem(context));

        // https://drafts.csswg.org/css-contain-2/#skips-its-contents: the skipped contents (laid out for a geometry API) are not
        // painted nor hit tested; a box that skips its contents has paint containment, so it is a stacking context
        if (!context.Box.SkipsContents)
        {
            PaintStackingContextContents(context, output, excluded, markers, treeOrder, adopted);
        }

        if (continuations is not null)
        {
            foreach (var continuation in continuations)
            {
                PaintStackingContext(continuation.Fragment, output, excluded: null, markers, treeOrder);
            }
        }

        if (markers)
        {
            output.Add(new PaintItem(context) { Marker = PaintItemMarker.EndStackingContext });
        }

        // https://drafts.csswg.org/css-view-transitions-2/#scoped-vt-layout: "the ::view-transition pseudo-element generates a
        // stacking context which paints separately from, and on top of, the painting of the root element and its other
        // children": it follows the stacking context of the root element of the scoped view transition
        if (context.Box is { IsAnonymous: false, PseudoElement: null, Element: { } element } && element.NodeDocument.HasScopedViewTransitions
            && element.NodeDocument.GetScopedViewTransition(element)?.PseudoTree is not null)
        {
            foreach (var child in context.ChildrenSpan)
            {
                if (child is BoxFragment { Box.IsScopedViewTransitionRoot: true, IsHiddenForPaint: false } viewTransition)
                {
                    PaintStackingContext(viewTransition, output, excluded: null, markers, treeOrder);
                }
            }
        }
    }

    /// <summary>
    /// Paints the stacking context of a layer of a sorted list, and returns the index of the last layer it painted: the next
    /// layers that are the other fragments of the same inline box (an inline box split over several lines) are painted in the
    /// group of the first one when the effects of the box are composited once for the box (<see cref="IsCompositedAsOneGroup"/>;
    /// https://drafts.csswg.org/css-color-4/#transparency: the opacity applies to the element as a group, not to each of its
    /// fragments: the parts of two fragments that overlap are not composited twice).
    /// </summary>
    private static int PaintLayerStackingContext(List<(BoxFragment Fragment, int ZIndex)> sorted, int index, List<PaintItem> output, bool markers, TreeOrder treeOrder)
    {
        var context = sorted[index].Fragment;
        var last = index;
        if (context.Box.IsInlineBox && IsCompositedAsOneGroup(context.Box))
        {
            while (last + 1 < sorted.Count && sorted[last + 1].Fragment is { EstablishesStackingContext: true } next && ReferenceEquals(next.Box, context.Box))
            {
                last++;
            }
        }

        PaintStackingContext(context, output, excluded: null, markers, treeOrder, adopted: null, last > index ? sorted.GetRange(index + 1, last - index) : null);
        return last;
    }

    /// <summary>
    /// Whether the fragments of an inline box that establishes a stacking context are painted in one group: its effects are only
    /// the ones that composite the group (the opacity, the blend mode, the isolation). The filters, the clip paths and the masks,
    /// whose geometry is the one of a fragment, are applied to each fragment.
    /// </summary>
    internal static bool IsCompositedAsOneGroup(ContainerBox box)
    {
        if (box.IsAnonymous || !box.IsInlineBox)
            return false;

        var style = box.Style;
        return !FilterEffects.HasFilter(style) && !FilterEffects.HasFilter(style, "backdrop-filter") && style.GetComputedValue("clip-path") is "none" or ""
            && style.GetComputedValue("mask-image") is "none" or "" && style.GetComputedValue("mask-border-source") is "none" or "";
    }

    /// <summary>
    /// Returns the first fragment of the nearest inline box that contains a float in the box tree and establishes a stacking
    /// context, or null: the float is painted in that stacking context (https://drafts.csswg.org/css2/#elaborate-stacking-contexts:
    /// the descendants of an element are in its stacking context; e.g. the opacity of the inline box applies to the float),
    /// although its fragment is a child of the fragment of the block container.
    /// </summary>
    private static BoxFragment? GetInlineStackingContextOfFloat(ContainerBox floatBox)
    {
        for (var inline = floatBox.Parent; inline is { IsInlineBox: true }; inline = inline.Parent)
        {
            if (GetInlineStackingContext(inline) is { } context)
                return context;
        }

        return null;
    }

    /// <summary>Returns the first fragment of an inline box (of its first part, when it is split around a block) when it establishes a stacking context, or null.</summary>
    private static BoxFragment? GetInlineStackingContext(ContainerBox inline)
    {
        var first = inline;
        while (first.PreviousContinuation is { } previous)
        {
            first = previous;
        }

        return first.FirstFragment is BoxFragment { EstablishesStackingContext: true } fragment ? fragment : null;
    }

    /// <summary>Paints the floats of an inline box that establishes a stacking context (see <see cref="GetInlineStackingContextOfFloat"/>), when the fragment is its first fragment.</summary>
    private static void PaintFloatsOfInlineStackingContext(BoxFragment context, List<PaintItem> output, bool markers)
    {
        var box = context.Box;
        if (!box.IsInlineBox || box.IsAnonymous || box.PreviousContinuation is not null || !ReferenceEquals(box.FirstFragment, context))
            return;

        for (var part = box; part is not null; part = part.Continuation)
        {
            Visit(part);
        }

        void Visit(ContainerBox inline)
        {
            foreach (var child in inline.Children)
            {
                if (child is not ContainerBox container)
                    continue;

                if (container.IsInlineBox)
                {
                    // (the floats of an inline box that establishes a stacking context are painted by it)
                    if (GetInlineStackingContext(container) is null)
                    {
                        Visit(container);
                    }

                    continue;
                }

                for (var fragment = container.FirstFragment as BoxFragment; fragment is not null; fragment = fragment.NextFragmentOfBox as BoxFragment)
                {
                    if (IsFloat(fragment) && !IsLayer(fragment) && !fragment.IsHiddenForPaint)
                    {
                        PaintPseudoStackingContext(fragment, output, markers);
                    }
                }
            }
        }
    }

    private static void PaintStackingContextContents(BoxFragment context, List<PaintItem> output, List<BoxFragment>? excluded, bool markers, TreeOrder treeOrder, List<BoxFragment>? adopted)
    {
        // The positioned descendants and the descendant stacking contexts, by z-index
        var layers = new List<(BoxFragment Fragment, int ZIndex)>();
        CollectLayers(context, layers, excluded, treeOrder);

        // The blocks split out of an inline box that establishes the stacking context are in it, with their layers
        var splitBlocks = GetBlocksPaintedInInlineLayer(context);
        if (splitBlocks is not null)
        {
            foreach (var block in splitBlocks)
            {
                CollectLayers(block, layers, excluded, treeOrder);
            }
        }

        if (adopted is not null)
        {
            foreach (var fragment in adopted)
            {
                // https://drafts.csswg.org/css-anchor-position-1/#position-visibility: visibility: force-hidden hides the box and its contents
                if (fragment.IsForceHidden || fragment.IsHiddenForPaint)
                    continue;

                layers.Add((fragment, fragment.EstablishesStackingContext ? fragment.ZIndex ?? 0 : 0));
                if (!fragment.EstablishesStackingContext)
                {
                    CollectLayers(fragment, layers, excluded: null, treeOrder);
                }
            }
        }

        // A stable sort: the layers with the same z-index stay in tree order
        var sorted = layers.Count > 1 ? SortByZIndex(layers, treeOrder) : layers;
        var index = 0;
        for (; index < sorted.Count && sorted[index].ZIndex < 0; index++)
        {
            index = PaintLayerStackingContext(sorted, index, output, markers, treeOrder);
        }

        PaintBlocksOfInlineLayer(splitBlocks, output, markers);
        PaintFloatsOfInlineStackingContext(context, output, markers);
        PaintNormalFlow(context, output, markers);
        AddOutlinesMarker(context, output, markers);

        for (; index < sorted.Count; index++)
        {
            var layer = sorted[index].Fragment;
            if (layer.EstablishesStackingContext)
            {
                index = PaintLayerStackingContext(sorted, index, output, markers, treeOrder);
            }
            else
            {
                // The blocks split out of a positioned inline box are painted with it (their layers are in the stacking context)
                PaintBlocksOfInlineLayer(GetBlocksPaintedInInlineLayer(layer), output, markers);
                PaintPseudoStackingContext(layer, output, markers);
                AddOutlinesMarker(layer, output, markers);
            }
        }
    }

    /// <summary>Paints the visible layers adopted by a stacking context hidden after the clamp point of a line-clamp container (see <see cref="MarkHiddenContexts"/>).</summary>
    private static void PaintHiddenStackingContextContents(BoxFragment context, List<PaintItem> output, bool markers, TreeOrder treeOrder, List<BoxFragment>? adopted)
    {
        var layers = new List<(BoxFragment Fragment, int ZIndex)>();
        CollectHiddenLayers(context, layers, treeOrder);
        if (adopted is not null)
        {
            foreach (var fragment in adopted)
            {
                if (fragment.IsForceHidden || fragment.IsHiddenForPaint)
                    continue;

                layers.Add((fragment, fragment.EstablishesStackingContext ? fragment.ZIndex ?? 0 : 0));
                if (!fragment.EstablishesStackingContext)
                {
                    CollectLayers(fragment, layers, excluded: null, treeOrder);
                }
            }
        }

        var sorted = layers.Count > 1 ? SortByZIndex(layers, treeOrder) : layers;
        for (var index = 0; index < sorted.Count; index++)
        {
            var layer = sorted[index].Fragment;
            if (layer.EstablishesStackingContext)
            {
                index = PaintLayerStackingContext(sorted, index, output, markers, treeOrder);
            }
            else
            {
                PaintPseudoStackingContext(layer, output, markers);
                AddOutlinesMarker(layer, output, markers);
            }
        }
    }

    private static void AddOutlinesMarker(BoxFragment layer, List<PaintItem> output, bool markers)
    {
        if (markers)
        {
            output.Add(new PaintItem(layer) { Marker = PaintItemMarker.Outlines });
        }
    }

    private static List<(BoxFragment Fragment, int ZIndex)> SortByZIndex(List<(BoxFragment Fragment, int ZIndex)> layers, TreeOrder treeOrder)
    {
        var sorted = new List<(BoxFragment Fragment, int ZIndex)>(layers.Count);
        var order = new int[layers.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // The layers with the same z-index are painted in tree order (https://drafts.csswg.org/css2/#elaborate-stacking-contexts,
        // step 8: "in tree order"): the order of the boxes, not of the fragments (an absolutely positioned box is a child of the
        // fragment of its containing block); the flex and grid items are in order-modified document order (the order of their
        // fragments): the tree indexes of the items of a container are assigned in the order of their fragments
        var keys = new int[layers.Count];
        Dictionary<Fragment, List<int>>? itemGroups = null;
        for (var i = 0; i < keys.Length; i++)
        {
            var fragment = layers[i].Fragment;
            keys[i] = treeOrder.Get(fragment.Box);
            if (fragment.Parent is { } parent && PaintsChildrenAtomically(parent))
            {
                itemGroups ??= new(ReferenceEqualityComparer.Instance);
                if (!itemGroups.TryGetValue(parent, out var group))
                {
                    itemGroups[parent] = group = [];
                }

                group.Add(i);
            }
        }

        if (itemGroups is not null)
        {
            foreach (var group in itemGroups.Values)
            {
                var groupKeys = group.ConvertAll(i => keys[i]);
                groupKeys.Sort();

                // https://drafts.csswg.org/css-flexbox-1/#order-property: "absolutely-positioned children of a flex container are
                // treated as having order: 0 for the purpose of determining their painting order relative to flex items" (their
                // fragments are after the ones of the items)
                var treeKeys = keys;
                group.Sort((a, b) =>
                {
                    var result = GetItemOrder(layers[a].Fragment).CompareTo(GetItemOrder(layers[b].Fragment));
                    if (result != 0)
                        return result;

                    result = treeKeys[a].CompareTo(treeKeys[b]);
                    return result != 0 ? result : a.CompareTo(b);
                });

                for (var i = 0; i < group.Count; i++)
                {
                    keys[group[i]] = groupKeys[i];
                }
            }
        }

        Array.Sort(order, (a, b) =>
        {
            var result = layers[a].ZIndex.CompareTo(layers[b].ZIndex);
            if (result != 0)
                return result;

            result = keys[a].CompareTo(keys[b]);
            return result != 0 ? result : a.CompareTo(b);
        });

        foreach (var i in order)
        {
            sorted.Add(layers[i]);
        }

        return sorted;
    }

    /// <summary>
    /// Returns the <c>order</c> of a layer whose fragment is a child of the fragment of a flex or grid container, for the
    /// painting order: the one of the item that contains its box (the item itself, or an absolutely positioned descendant of
    /// the item whose containing block is the container), 0 for an absolutely positioned child of the container.
    /// </summary>
    private static int GetItemOrder(BoxFragment fragment)
    {
        if (fragment.Parent is not BoxFragment { Box: var container })
            return 0;

        var item = fragment.Box;
        while (item.Parent is { } parent && !ReferenceEquals(parent, container))
        {
            item = parent;
        }

        if (item.Parent is null || PositionedLayout.IsAbsolutelyPositioned(item))
            return 0;

        var flex = item.Style.Layout.Flex;
        return container.Display.Inside == DisplayInside.WebkitBox ? flex.WebkitBoxOrdinalGroup : flex.Order;
    }

    /// <summary>
    /// The state of a collection of a painting order: the indexes of the boxes in tree order (a preorder traversal of the box tree,
    /// computed on first use), and the layers adopted by the stacking context of the root element.
    /// </summary>
    private sealed class TreeOrder
    {
        private Dictionary<LayoutBox, int>? _indexes;

        /// <summary>The layers painted in another stacking context than the one of their containing block chain (see <see cref="ComputeAdoptions"/>), by stacking context.</summary>
        public Dictionary<BoxFragment, List<BoxFragment>>? Adopted { get; set; }

        /// <summary>The layers that are not painted in the stacking context of their containing block chain.</summary>
        public HashSet<BoxFragment> Moved { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The stacking contexts hidden after the clamp point of a line-clamp container that contain an adopted visible layer (see <see cref="MarkHiddenContexts"/>).</summary>
        public HashSet<BoxFragment>? HiddenContexts { get; set; }

        public int Get(ContainerBox box)
        {
            if (_indexes is null)
            {
                _indexes = new(ReferenceEqualityComparer.Instance);
                LayoutBox root = box;
                while (root.Parent is { } parent)
                {
                    root = parent;
                }

                var stack = new Stack<LayoutBox>();
                stack.Push(root);
                while (stack.TryPop(out var current))
                {
                    _indexes[current] = _indexes.Count;
                    if (current is ContainerBox container)
                    {
                        // The children are pushed in reverse order
                        var children = new List<LayoutBox>();
                        foreach (var child in container.Children)
                        {
                            children.Add(child);
                        }

                        for (var i = children.Count - 1; i >= 0; i--)
                        {
                            stack.Push(children[i]);
                        }
                    }
                }
            }

            return _indexes.GetValueOrDefault(box, int.MaxValue);
        }
    }

    /// <summary>
    /// Collects the descendants of a stacking context that are painted by its layers: the descendant stacking contexts (their
    /// descendants belong to them) and the positioned boxes with <c>z-index: auto</c> (their descendants belong to the
    /// stacking context too), in tree order.
    /// </summary>
    private static void CollectLayers(Fragment fragment, List<(BoxFragment Fragment, int ZIndex)> layers, List<BoxFragment>? excluded, TreeOrder treeOrder)
    {
        // The children of a replaced box (the foreignObject boxes of an svg element) are painted by its contents (the SVG
        // painting), but the boxes of the parts of a form control (see FormControlContents)
        if (fragment is BoxFragment { Box.Kind: ContainerBoxKind.Replaced } replaced && !FormControlContents.HasContentsFragment(replaced))
            return;

        foreach (var child in fragment.ChildrenSpan)
        {
            if (child.IsHiddenForPaint)
            {
                if (child is BoxFragment hiddenBox && treeOrder.HiddenContexts?.Contains(hiddenBox) == true)
                {
                    layers.Add((hiddenBox, hiddenBox.ZIndex ?? 0));
                }
                else
                {
                    CollectHiddenLayers(child, layers, treeOrder);
                }

                continue;
            }

            if (child is BoxFragment box)
            {
                if ((excluded is not null && excluded.Contains(box)) || treeOrder.Moved.Contains(box))
                    continue;

                // https://drafts.csswg.org/css-anchor-position-1/#position-visibility: visibility: force-hidden hides the box and its contents
                if (box.IsForceHidden)
                    continue;

                // The layers of a block split out of an inline box that establishes a stacking context are in that stacking context
                if (IsPaintedInInlineLayer(box) && !ReferenceEquals(box, fragment) && GetSplitInlineLayerBox(box.Box)!.FirstFragment is BoxFragment { EstablishesStackingContext: true })
                    continue;

                // The ::view-transition pseudo-element of a scoped view transition is painted after the stacking context of its
                // root element (see PaintStackingContext)
                if (box.Box.IsScopedViewTransitionRoot)
                    continue;

                if (box.EstablishesStackingContext)
                {
                    layers.Add((box, box.ZIndex ?? 0));
                    continue;
                }

                if (IsPositioned(box))
                {
                    layers.Add((box, 0));
                }

                if (box.Box.SkipsContents)
                    continue;
            }

            CollectLayers(child, layers, excluded, treeOrder);
        }
    }

    /// <summary>
    /// Paints a box as if it established a stacking context (a positioned box with <c>z-index: auto</c>, a float, an atomic
    /// inline), without its positioned descendants and the stacking contexts it contains.
    /// </summary>
    private static void PaintPseudoStackingContext(BoxFragment fragment, List<PaintItem> output, bool markers)
    {
        output.Add(new PaintItem(fragment));
        if (!fragment.Box.SkipsContents)
        {
            PaintNormalFlow(fragment, output, markers);
        }
    }

    /// <summary>
    /// Paints the in-flow contents of a box: the block-level descendants, the floats, then the inline content. When the painting
    /// order is collected for the painting (<paramref name="markers"/>), the lines are truncated by <c>text-overflow</c>.
    /// </summary>
    private static void PaintNormalFlow(BoxFragment fragment, List<PaintItem> output, bool markers)
    {
        if (fragment.Box.IsInlineBox)
        {
            PaintInlineContent(fragment, output, markers, truncation: null);
            return;
        }

        PaintBlocks(fragment, output);
        if (markers && fragment.Box.Style.Environment.ForcedColors)
        {
            PaintBackplates(fragment, output);
        }

        PaintFloats(fragment, output, markers);
        PaintInlines(fragment, output, markers);
    }

    /// <summary>
    /// The backplates of the text of a box and of its in-flow, non-positioned, block-level descendants in forced colors mode (see
    /// <see cref="PaintItemPart.Backplate"/>): as in Chromium, they are above the backgrounds of the blocks and below the floats.
    /// </summary>
    private static void PaintBackplates(BoxFragment fragment, List<PaintItem> output)
    {
        var hasLines = false;
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is LineBoxFragment)
            {
                hasLines = true;
            }
            else if (child is BoxFragment { IsHiddenForPaint: false } box && !PaintsChildrenAtomically(fragment) && !IsPaintedInInlineLayer(box) && !IsLayer(box) && !IsFloat(box) && !box.Box.SkipsContents)
            {
                PaintBackplates(box, output);
            }
        }

        if (hasLines)
        {
            output.Add(new PaintItem(fragment) { Part = PaintItemPart.Backplate });
        }
    }

    /// <summary>The backgrounds of the in-flow, non-positioned, block-level descendants, in tree order.</summary>
    private static void PaintBlocks(Fragment fragment, List<PaintItem> output)
    {
        if (PaintsChildrenAtomically(fragment))
            return;

        // The background and the borders of a table are the ones of its grid box: as in the browsers, they are painted before
        // its captions, which are before the grid box in the wrapper box (a caption moved over the table by a negative margin
        // covers its border, WPT css/CSS2/box-display/root-box-002.xht)
        BoxFragment? tableGrid = null;
        if (fragment is BoxFragment { Box.Kind: ContainerBoxKind.TableWrapper })
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is BoxFragment { Box.Kind: ContainerBoxKind.TableGrid, IsHiddenForPaint: false } grid && !IsLayer(grid))
                {
                    tableGrid = grid;
                    output.Add(new PaintItem(grid));
                    break;
                }
            }
        }

        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is not BoxFragment box || box.IsHiddenForPaint)
                continue;

            // A block split out of an inline box that is a layer is painted in the layer
            if (IsPaintedInInlineLayer(box))
                continue;

            // A block split out of inline boxes (https://drafts.csswg.org/css2/#anonymous-block-level) is in them, as in the
            // browsers: the inline boxes cover the band of the block across the block container, below the block
            if (IsSplitOutOfInline(box))
            {
                output.Add(new PaintItem(box, IsSplitInlineBand: true));
            }

            if (IsLayer(box) || IsFloat(box))
                continue;

            if (!ReferenceEquals(box, tableGrid))
            {
                // The replaced content of a block-level replaced box is painted with the inline content (see PaintInlines)
                output.Add(new PaintItem(box) { Part = box.Box.Kind == ContainerBoxKind.Replaced ? PaintItemPart.Decorations : PaintItemPart.All });
            }

            if (!box.Box.SkipsContents)
            {
                PaintBlocks(box, output);
            }
        }
    }

    /// <summary>
    /// Returns the last fragment of the block-level descendants of a fragment that are painted with it (the last item that
    /// <see cref="PaintBlocks"/> appends for the fragment), or null when it has none.
    /// </summary>
    internal static BoxFragment? GetLastPaintedBlock(Fragment fragment)
    {
        if (PaintsChildrenAtomically(fragment))
            return null;

        var children = fragment.ChildrenSpan;
        for (var i = children.Length - 1; i >= 0; i--)
        {
            if (children[i] is not BoxFragment box || box.IsHiddenForPaint || IsPaintedInInlineLayer(box) || IsLayer(box) || IsFloat(box))
                continue;

            // (the items of the descendants of a box follow its own item)
            return (box.Box.SkipsContents ? null : GetLastPaintedBlock(box)) ?? box;
        }

        return null;
    }

    /// <summary>The non-positioned floats, in tree order, painted as pseudo stacking contexts.</summary>
    private static void PaintFloats(Fragment fragment, List<PaintItem> output, bool markers)
    {
        // The floats of a flex item are painted with it
        if (PaintsChildrenAtomically(fragment))
            return;

        foreach (var child in fragment.ChildrenSpan)
        {
            if (child.IsHiddenForPaint)
                continue;

            if (child is BoxFragment box)
            {
                if (IsLayer(box) || IsPaintedInInlineLayer(box))
                    continue;

                if (IsFloat(box))
                {
                    // (a float in an inline box that establishes a stacking context is painted in that stacking context)
                    if (GetInlineStackingContextOfFloat(box.Box) is null)
                    {
                        PaintPseudoStackingContext(box, output, markers);
                    }

                    continue;
                }

                // The floats of an atomic inline are painted with it
                if (box.Box.IsAtomicInline || box.Box.SkipsContents)
                    continue;
            }

            PaintFloats(child, output, markers);
        }
    }

    /// <summary>The inline content of the line boxes of the block containers, in tree order, and the flex items.</summary>
    private static void PaintInlines(Fragment fragment, List<PaintItem> output, bool markers)
    {
        if (PaintsChildrenAtomically(fragment))
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is BoxFragment { IsHiddenForPaint: false } item && !IsLayer(item) && !IsFloat(item))
                {
                    PaintPseudoStackingContext(item, output, markers);
                }
            }

            return;
        }

        // The outside marker of a list item precedes its content in the tree (the layout adds its fragment after the line box it
        // is placed next to): it is painted first, under the content that overlaps it. A marker placed next to a line of a
        // positioned descendant block belongs to the inline content of the list item: it is painted under the positioned block
        // (as in Blink and Gecko, WPT css/css-lists/outside-marker-covered-by-relpos-block-link.html)
        var hasOutsideMarker = false;
        if (fragment is BoxFragment { Box: var listItem } && GetOutsideMarkerBox(listItem) is { } markerBox)
        {
            for (var markerFragment = markerBox.FirstFragment; markerFragment is not null; markerFragment = markerFragment.NextFragmentOfBox)
            {
                if (markerFragment is BoxFragment marker && ReferenceEquals(GetListItemPaintingMarker(marker), fragment))
                {
                    PaintChild(marker);
                }
            }
        }

        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is BoxFragment { Box.IsOutsideMarker: true } marker)
            {
                hasOutsideMarker = true;
                if (GetListItemPaintingMarker(marker) is null)
                {
                    PaintChild(child);
                }
            }
        }

        foreach (var child in fragment.ChildrenSpan)
        {
            if (!hasOutsideMarker || child is not BoxFragment { Box.IsOutsideMarker: true })
            {
                PaintChild(child);
            }
        }

        void PaintChild(Fragment child)
        {
            if (child.IsHiddenForPaint)
                return;

            switch (child)
            {
                case LineBoxFragment line:
                    PaintLine(line, output, markers);
                    break;

                case BoxFragment box when !IsLayer(box) && !IsFloat(box) && !IsPaintedInInlineLayer(box):
                    if (box.Box.Kind == ContainerBoxKind.Replaced)
                    {
                        output.Add(new PaintItem(box) { Part = PaintItemPart.ReplacedContent });
                    }

                    if (!box.Box.SkipsContents)
                    {
                        PaintInlines(box, output, markers);
                    }

                    break;
            }
        }
    }

    private static ContainerBox? GetOutsideMarkerBox(ContainerBox box)
    {
        foreach (var child in box.Children)
        {
            if (child is ContainerBox { IsOutsideMarker: true } marker)
                return marker;
        }

        return null;
    }

    /// <summary>
    /// Returns the fragment of the list item that paints an outside marker placed next to a line of a positioned descendant block
    /// that does not establish a stacking context (the marker belongs to the inline content of the list item, under the
    /// positioned block), or null when the marker is painted with the block container of the line.
    /// </summary>
    private static BoxFragment? GetListItemPaintingMarker(BoxFragment marker)
    {
        var listItem = marker.Box.Parent;
        var inLayer = false;
        for (var ancestor = marker.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is not BoxFragment box)
                continue;

            if (ReferenceEquals(box.Box, listItem))
                return inLayer ? box : null;

            if (box.EstablishesStackingContext)
                return null;

            inLayer |= IsPositioned(box);
        }

        return null;
    }

    /// <summary>
    /// The contents of a line box, then its ellipses (https://drafts.csswg.org/css-overflow-4/#ellipsing-details: the ellipsis is
    /// in the root inline box, after the content). For the painting, the line is truncated by the <c>text-overflow</c> of its block
    /// container (<see cref="LineTruncator.GetTextOverflow"/>); the hit testing sees the content of the line as laid out
    /// ("Ellipsing only affects rendering and must not affect [...] dispatching of pointer events").
    /// </summary>
    private static void PaintLine(LineBoxFragment line, List<PaintItem> output, bool markers)
    {
        var truncation = markers ? LineTruncator.GetTruncation(line) : null;

        // The background of the ::first-line pseudo-element is painted behind the content of the line (the root inline box)
        if (markers && line.Style is { PseudoElement: "first-line" })
        {
            output.Add(new PaintItem(line));
        }

        PaintInlineContent(line, output, markers, truncation);
        if (!markers)
            return;

        if (truncation is { BlockEllipsis: { } truncatedBlockEllipsis, BlockEllipsisTruncation: { } blockEllipsisTruncation })
        {
            // The block ellipsis truncated by text-overflow as the content of the line
            foreach (var fragment in truncatedBlockEllipsis.Fragments)
            {
                if (!blockEllipsisTruncation.IsHidden(fragment))
                {
                    output.Add(new PaintItem(fragment) { Truncation = blockEllipsisTruncation });
                }
            }
        }
        else if (!line.IsTruncatedByBlockEllipsis)
        {
            AddEllipsis(line.BlockEllipsis, output);
        }

        AddEllipsis(truncation?.LeftEllipsis, output);
        AddEllipsis(truncation?.RightEllipsis, output);

        static void AddEllipsis(LineEllipsis? ellipsis, List<PaintItem> output)
        {
            if (ellipsis is null)
                return;

            foreach (var fragment in ellipsis.Fragments)
            {
                output.Add(new PaintItem(fragment));
            }
        }
    }

    /// <summary>
    /// The contents of a line box or of an inline box: the inline boxes and the texts in tree order, the atomic inlines as pseudo
    /// stacking contexts, without the fragments hidden by the truncation of the line.
    /// </summary>
    private static void PaintInlineContent(Fragment fragment, List<PaintItem> output, bool markers, LineTruncation? truncation)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            switch (child)
            {
                case TextFragment text:
                    if (truncation?.IsHidden(text) != true)
                    {
                        output.Add(new PaintItem(text) { Truncation = truncation });
                    }

                    break;

                case BoxFragment box when IsLayer(box) || IsFloat(box):
                    break;

                case BoxFragment { Box.IsInlineBox: true } inlineBox:
                    output.Add(new PaintItem(inlineBox) { Truncation = truncation });
                    PaintInlineContent(inlineBox, output, markers, truncation);
                    break;

                case BoxFragment atomic:
                    if (truncation?.IsHidden(atomic) != true)
                    {
                        PaintPseudoStackingContext(atomic, output, markers);
                    }

                    break;

                default:
                    PaintInlineContent(child, output, markers, truncation);
                    break;
            }
        }
    }
}
