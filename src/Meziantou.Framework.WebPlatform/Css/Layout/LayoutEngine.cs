using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a document: builds its box tree (<see cref="Layout.BoxTree"/>) and lays it out into a fragment tree whose
/// root is the fragment of the initial containing block. The layout is lazy: it is computed when something reads the
/// geometry (<see cref="EnsureLayout"/>, a forced layout), and kept up to date by the rendering steps of the event loop once
/// it was requested. A document that never reads the geometry is never laid out.
/// </summary>
/// <remarks>
/// The invalidation is version-based: the layout is computed again when the version
/// of the styles changes (<see cref="Cascade.StyleEngine.Version"/>: the DOM, the state of the elements, the style sheets, the
/// environment including the viewport size, the animations and the fonts), or when <see cref="SetNeedsLayout"/> was called
/// (a change that does not affect the styles, e.g. the natural size of an image). When the styles changed, the box tree is
/// patched with the boxes of the elements that changed (see <see cref="BoxTree.TryPatch"/>), or built again. The boxes that did
/// not change are not laid out again when their constraint space is the same (see <see cref="LayoutResultCache"/>).
/// </remarks>
internal sealed partial class LayoutEngine
{
    private readonly Document _document;
    private BoxTree? _boxTree;
    private BoxFragment? _root;

    /// <summary>The version of the styles the box tree was built with.</summary>
    private int _boxTreeStyleVersion = -1;

    /// <summary>Whether the layout must be computed again, even if the styles did not change.</summary>
    private bool _needsLayout = true;
    private bool _inLayout;
    private bool _updatedByRendering;
    private int _fragmentIndexVersion;

    /// <summary>
    /// The sizes of the content boxes of the size containers (see <see cref="LayoutContext.OnSizeContainerLaidOut"/>), kept from
    /// one pass to the next: the container queries use the size of the last layout of a container (see
    /// <see cref="TryGetLaidOutContainerSize"/>). The entries of the containers that were not laid out by the last pass are removed.
    /// </summary>
    private Dictionary<Element, ContainerSize>? _containerSizes;
    private Dictionary<ContainerBox, (GridTrackSizes Columns, GridTrackSizes Rows)>? _gridTrackSizes;
    private List<BoxFragment>? _stickyFragments;
    private PhysicalSize _initialContainingBlockSize;

    /// <summary>The instances of the inline layout algorithm (one per nesting level of the inline formatting contexts), kept between the passes to reuse their buffers.</summary>
    private readonly Stack<InlineLayoutAlgorithm> _inlineLayouts = new();

    public LayoutEngine(Document document)
    {
        _document = document;
    }

    /// <summary>The document laid out by this engine.</summary>
    public Document Document => _document;

    /// <summary>The number of layout passes computed so far (for the tests and the diagnostics).</summary>
    public int LayoutCount { get; private set; }

    /// <summary>The number of updates of the box tree that patched it instead of building it again (see <see cref="BoxTree.TryPatch"/>), for the tests and the diagnostics.</summary>
    public int BoxTreePatchCount { get; private set; }

    /// <summary>
    /// Whether the incremental updates of the box tree and of the layout are verified (the <c>WEBPLATFORM_VERIFY_INCREMENTAL_LAYOUT</c>
    /// environment variable is <c>1</c>): after each patch of the box tree, the tree is built again and compared with the patched
    /// tree, and an <see cref="InvalidOperationException"/> is thrown when they differ. For the tests.
    /// </summary>
    internal static bool VerifyIncrementalLayout { get; set; } = System.Environment.GetEnvironmentVariable("WEBPLATFORM_VERIFY_INCREMENTAL_LAYOUT") == "1";

    /// <summary>
    /// The generation of the results kept by the layout result cache (see <see cref="LayoutResultCache"/>) and of the intrinsic
    /// sizes kept from one pass to the next: incremented when something that the cache does not track changes (see
    /// <see cref="SetNeedsLayout"/>, the box tree is built again).
    /// </summary>
    internal int LayoutCacheGeneration { get; private set; }

    /// <summary>The number of results of the previous passes used by the layout (see <see cref="LayoutResultCache"/>), for the tests and the diagnostics.</summary>
    public int LayoutCacheHitCount { get; private set; }

    /// <summary>Whether an anchor-positioned box was laid out: the layout result cache is not used anymore (see <see cref="CanUseLayoutCache"/>).</summary>
    internal bool UsesAnchorPositioning { get; private set; }

    /// <summary>Whether the layout result cache can be used, for the tests (it is used when <see cref="CanUseLayoutCache"/> allows it).</summary>
    internal static bool IsLayoutCacheEnabled { get; set; } = System.Environment.GetEnvironmentVariable("WEBPLATFORM_DISABLE_LAYOUT_CACHE") != "1";

    /// <summary>
    /// Whether the next pass uses the results of the previous passes (see <see cref="LayoutResultCache"/>): not for the printing,
    /// nor for the documents with anchor-positioned boxes (the anchor queries find the fragments of the anchors laid out by the
    /// pass), with scroll navigation controls (their boxes are collected again by the layout), with boxes whose
    /// <c>contain-intrinsic-size</c> remembers their size (it changes after the layout), or with an active view transition.
    /// </summary>
    private bool CanUseLayoutCache()
        => IsLayoutCacheEnabled && _printSession is null && !UsesAnchorPositioning && _boxTree is { ScrollControls: null, ContainIntrinsicSizeAutoBoxes.Count: 0 }
            && _document.ViewTransitionPseudoTree is null && !_document.HasScopedViewTransitions;

    /// <summary>
    /// Lays out the document again without the results of the previous passes and throws when the fragment tree differs from the
    /// one of the incremental layout (see <see cref="VerifyIncrementalLayout"/>).
    /// </summary>
    private void VerifyIncrementalLayoutResult(Cascade.StyleEngine styleEngine, ContainerBox viewport, LayoutContext context)
    {
        var verificationContext = new LayoutContext(this, _document, styleEngine.Environment) { UsesLayoutCache = false };
        var full = LayoutViewport(verificationContext, viewport);
        if (LayoutResultCache.DescribeDifference(_root!, full) is { } difference)
            throw new InvalidOperationException($"Incremental layout: the fragment tree differs from the layout without the results of the previous passes ({_document.Url}, {context.LayoutCacheHitCount} results used): {difference}");
    }

    /// <summary>Builds the box tree of the document again and throws when it differs from the patched tree (see <see cref="VerifyIncrementalLayout"/>).</summary>
    private void VerifyPatchedBoxTree(BoxTree patched)
    {
        var built = BoxTree.Build(_document);
        if (BoxTreeBuilder.DescribeDifference(patched, built) is { } difference)
            throw new InvalidOperationException($"Incremental layout: the patched box tree differs from the box tree built for the document ({_document.Url}): {difference}");
    }

    /// <summary>Whether the layout was requested at least once: the rendering steps of the event loop then keep it up to date.</summary>
    public bool IsLayoutRequested => _updatedByRendering;

    /// <summary>Whether the layout is out of date: the next geometry read computes it again.</summary>
    public bool NeedsLayout => _needsLayout || _document.GetStyleEngine().Version != _boxTreeStyleVersion;

    /// <summary>Whether the layout is being computed (the fragments of the elements are not indexed yet).</summary>
    public bool IsInLayout => _inLayout;

    /// <summary>The box tree of the last layout (see <see cref="EnsureLayout"/>), or null when the document was never laid out.</summary>
    public BoxTree? BoxTree => _boxTree;

    /// <summary>
    /// Computes the layout when it is out of date (a forced layout, https://drafts.csswg.org/cssom-view/#layout-box) and returns
    /// the root of the fragment tree: the fragment of the initial containing block, or null when the document is not rendered
    /// (no browsing context, or the root element does not generate a box).
    /// </summary>
    public BoxFragment? EnsureLayout()
    {
        // A geometry read during the layout (e.g. from a style computed by the layout) sees the layout being computed
        if (_inLayout)
            return _root;

        RegisterForRenderingUpdates();
        UpdateLayout();
        return _root;
    }

    /// <summary>
    /// Registers the document for the rendering steps without computing the layout (e.g. it has resize observers), and
    /// schedules a rendering update: the layout is computed by the next rendering update, and kept up to date by the next ones.
    /// </summary>
    public void RequestRenderingUpdates()
    {
        RegisterForRenderingUpdates();
        if (_updatedByRendering)
        {
            _document.EventLoop?.RequestRenderingUpdate();
        }
    }

    private void RegisterForRenderingUpdates()
    {
        // (the layout of the printing is not updated by the rendering steps)
        if (!_updatedByRendering && _printSession is null && _document.BrowsingContext is not null && _document.EventLoop is { } eventLoop)
        {
            _updatedByRendering = true;
            eventLoop.AddDocumentWithLayout(_document);
        }
    }

    /// <summary>Marks the layout as out of date, for a change that does not change the computed styles.</summary>
    /// <param name="rebuildBoxTree">Whether the box tree must be built again (the boxes generated by an element changed).</param>
    public void SetNeedsLayout(bool rebuildBoxTree = false)
    {
        _needsLayout = true;

        // (the results of the previous passes may depend on what changed)
        LayoutCacheGeneration++;
        if (rebuildBoxTree)
        {
            // The scroll anchoring selects its anchor nodes in the boxes of the previous pass
            _discardedBoxTree = _boxTree ?? _discardedBoxTree;
            _boxTree = null;
        }

        // The changes of the styles schedule a rendering update, not the other changes of the layout (e.g. an image is loaded):
        // the sizes observed by the resize observers and the intersections observed by the intersection observers may change,
        // and the boxes may shift, and so may the states of the scroll-state query containers
        if (_updatedByRendering && _document.EventLoop is { } eventLoop && (eventLoop.ResizeObserverCount > 0 || eventLoop.IntersectionObserverCount > 0 || _document.IsTrackingLayoutShifts || _document.IsTrackingPaintTiming || _scrollStateQueried || HasScrollMarkerState))
        {
            eventLoop.RequestRenderingUpdate();
        }

        // The scroll-driven timelines depend on the layout
        _document.AnimationsOrNull?.OnScrollOrLayoutChanged();
    }

    /// <summary>
    /// Updates the layout when it is out of date, without registering the document for the rendering steps: the "update
    /// layout" of the rendering steps (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering).
    /// </summary>
    internal void UpdateLayout()
    {
        if (_inLayout)
            return;

        var styleEngine = _document.GetStyleEngine();
        var styleVersion = styleEngine.Version;
        if (!_needsLayout && styleVersion == _boxTreeStyleVersion && _boxTree is not null)
            return;

        _inLayout = true;
        var suspension = styleEngine.SuspendUpdates();
        var anchoring = false;
        try
        {
            _needsLayout = false;

            // https://drafts.csswg.org/css-scroll-anchoring/#scroll-adjustment: the anchor nodes are selected and their positions
            // saved with the previous layout, before the boxes are built again
            var previousBoxTree = _boxTree ?? _discardedBoxTree;
            _discardedBoxTree = null;
            anchoring = _printSession is null && QueueScrollAnchoringAdjustments(previousBoxTree, stylesChanged: _boxTree is null || styleVersion != _boxTreeStyleVersion);
            if (_boxTree is null || styleVersion != _boxTreeStyleVersion)
            {
                // The box tree is patched with the boxes of the elements that changed since it was built, when the changes are
                // known (see BoxTreeChanges), else it is built again. The changes are recorded from now on: the styles discarded
                // while the tree is built are built again by the next update
                var changes = styleEngine.TakeBoxTreeChanges();
                if (_printSession is null)
                {
                    styleEngine.TrackBoxTreeChanges();
                }

                if (_boxTree is not null && _printSession is null && changes is not null && _boxTree.TryPatch(_document, changes))
                {
                    BoxTreePatchCount++;
                    UpdateSkippedContents(_boxTree);
                    ResetLaidOutSkippedContents();
                    if (VerifyIncrementalLayout)
                    {
                        VerifyPatchedBoxTree(_boxTree);
                    }
                }
                else
                {
                    _boxTree = BoxTree.Build(_document);
                    _boxesLayingOutSkippedContents = null;
                    LayoutCacheGeneration++;
                }

                _boxTreeStyleVersion = styleVersion;
                if (_elementsWithLastRememberedSize is { Count: > 0 } remembered)
                {
                    LastRememberedSizes.ForgetWithoutAutoKeyword(_boxTree, remembered);
                }
            }
            else
            {
                // The relevance to the user of the elements with content-visibility: auto may have changed (focus, selection, proximity)
                UpdateSkippedContents(_boxTree);
            }

            ApplyLaidOutSkippedContents(_boxTree);

            // https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing: "To the extent that the presence of scrollbars can affect
            // sizing, UAs must start with the assumption that no scrollbars are needed, and recalculate sizes if it turns out they
            // are": a box lays out its contents again when its scrollbars appear, but the boxes that were sized from its intrinsic
            // sizes before its layout (a shrink-to-fit ancestor, the flex layout of its parent) assumed the scrollbars of its
            // last layout. When the pass displays scrollbars that were not assumed, the document is laid out once more: the
            // scrollbars of a layout are the first guess of the next one, and they are never removed by it.
            LayoutContext context;
            for (var attempt = 0; ; attempt++)
            {
                _gridTrackSizes?.Clear();
                ClearAnchorPositionedBoxes();
                var usedAnchorPositioning = UsesAnchorPositioning;
                context = new LayoutContext(this, _document, styleEngine.Environment) { UsesLayoutCache = CanUseLayoutCache() };
                if (_printSession is not null)
                {
                    // The initial containing block of the paged media is the page area of the first page
                    context.ViewportSize = styleEngine.PrintViewportSize ?? context.ViewportSize;
                    _pages = null;
                    _root = _boxTree.Viewport is { } pagedViewport ? LayoutPages(context, pagedViewport) : null;
                }
                else
                {
                    _root = _boxTree.Viewport is { } viewport ? LayoutViewport(context, viewport) : null;
                    LayoutCacheHitCount += context.LayoutCacheHitCount;

                    // The anchor queries of the first anchor-positioned box find the anchors laid out by the pass: the pass is
                    // computed again without the results of the previous passes
                    if (!usedAnchorPositioning && UsesAnchorPositioning && context.LayoutCacheHitCount > 0)
                    {
                        LayoutCount++;
                        continue;
                    }

                    if (VerifyIncrementalLayout && context.LayoutCacheHitCount > 0 && _boxTree.Viewport is { } verifiedViewport)
                    {
                        VerifyIncrementalLayoutResult(styleEngine, verifiedViewport, context);
                    }
                }

                LayoutCount++;
                RemoveStaleContainerSizes(context.Pass);
                RemoveStaleAnchoredFallbacks(context.Pass);
                _stickyFragments = IndexFragments(context.Pass, out var scrollbarsChangedIntrinsicSizes);

                // The scroll markers were collected again while their groups were laid out (the styles of the contents of a size
                // container were computed again with its size, see BoxTreeBuilder.RebuildContents): the document is laid out
                // once more, with the same sizes of the containers
                if (_boxTree.ScrollControls is { PopulatedDuringLayout: true } scrollControls)
                {
                    scrollControls.PopulatedDuringLayout = false;
                    scrollbarsChangedIntrinsicSizes = true;
                }

                // https://drafts.csswg.org/css-multicol-2/#column-pseudo: the scroll markers of the columns are generated for the
                // number of columns of the previous layout: when it changed, they are collected again and the document is laid
                // out again (the groups have size containment: their markers do not change the columns, unless a group is
                // absolutely positioned, hence the limit)
                var columnsChanged = attempt < 3 && UpdateColumnScrollMarkers();
                if (!columnsChanged && (!scrollbarsChangedIntrinsicSizes || attempt > 0))
                    break;
            }

            _initialContainingBlockSize = context.InitialContainingBlockSize;
            if (_printSession is not null)
                return;

            // https://drafts.csswg.org/cssom-view/#run-the-resize-steps: the size of the visual viewport may have changed (e.g.
            // the classic scrollbars of the viewport appeared)
            _document.VisualViewportOrNull?.OnLayout(_initialContainingBlockSize);

            // https://drafts.csswg.org/css-position-3/#sticky-pos: the sticky offsets depend on the positions of the scroll containers
            if (_stickyFragments is not null)
            {
                StickyPosition.Apply(_stickyFragments, _initialContainingBlockSize, ViewportScrollState.Offset);
            }

            // https://drafts.csswg.org/css-anchor-position-1/#scroll: the default scroll shifts of the anchor-positioned boxes
            UpdateAnchorPositionedBoxes();
        }
        finally
        {
            suspension.Dispose();
            _inLayout = false;
        }

        // The scroll anchoring adjusts the scroll positions before the snap containers re-snap: "if the scroll container is
        // currently snapped to an element, scroll anchoring is limited to adjustments that would be allowed by re-snapping"
        // (https://drafts.csswg.org/css-scroll-anchoring/#description): the re-snap follows the snap targets
        if (anchoring)
        {
            PerformScrollAnchoringAdjustments();
        }

        // The scroll containers that were not scrolled yet are scrolled to their initial scroll target, then snapped
        ScrollToInitialScrollTargets();
        ResnapAfterLayout();
        RunPendingScrollsIntoView();

        // The styles are computed again with the first snapshots of the new scroll-state query containers, of the new scroll
        // marker groups and of the new scroll buttons
        var scrollStateChanged = TakeInitialScrollStateSnapshots();
        if (UpdateScrollMarkerSnapshots(onlyNew: true) || scrollStateChanged)
        {
            UpdateLayout();
        }
    }

    /// <summary>
    /// Returns the box fragments of an element (or of one of its pseudo-elements), in the order of the fragment tree: the
    /// fragments of its principal box and of its continuations (an inline box split around block-level boxes). The list is
    /// empty when the element does not generate a box. The layout is computed when it is out of date.
    /// </summary>
    public IReadOnlyList<BoxFragment> GetFragments(Element element, string? pseudoElement = null)
    {
        EnsureLayout();
        if (_boxTree is null)
            return [];

        var box = pseudoElement is null ? _boxTree.GetPrincipalBox(element) : _boxTree.GetPseudoElementBox(element, pseudoElement);
        if (box is not null && GetFirstFragment(box) is null && EnsureSkippedContentsLaidOut(box, includeSelf: false))
        {
            // The box tree may have been built again
            box = pseudoElement is null ? _boxTree.GetPrincipalBox(element) : _boxTree.GetPseudoElementBox(element, pseudoElement);
        }

        List<BoxFragment>? result = null;
        for (var part = box; part is not null; part = part.Continuation)
        {
            for (var fragment = GetFirstFragment(part); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                (result ??= []).Add((BoxFragment)fragment);
            }
        }

        return result is null ? [] : result;
    }

    /// <summary>Returns the text fragments of a text node (of all its text runs), in the order of the fragment tree. The layout is computed when it is out of date.</summary>
    public IReadOnlyList<TextFragment> GetTextFragments(Text text)
    {
        EnsureLayout();
        if (_boxTree is null)
            return [];

        if (_boxTree.GetFirstTextRun(text) is { } firstRun && GetFirstFragment(firstRun) is null)
        {
            EnsureSkippedContentsLaidOut(firstRun, includeSelf: false);
            if (_boxTree is null)
                return [];
        }

        List<TextFragment>? result = null;
        for (var run = _boxTree.GetFirstTextRun(text); run is not null; run = run.NextRunOfNode)
        {
            for (var fragment = GetFirstFragment(run); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                (result ??= []).Add((TextFragment)fragment);
            }
        }

        return result is null ? [] : result;
    }

    /// <summary>
    /// Returns the border box of an element in the coordinates of the viewport: the rectangle of its first fragment united with
    /// the non-empty rectangles of the other ones, or null when the element does not generate a box.
    /// </summary>
    public PhysicalRect? GetBorderBoxRect(Element element)
    {
        var fragments = GetFragments(element);
        if (fragments.Count == 0)
            return null;

        var rect = fragments[0].GetRectInRoot();
        for (var i = 1; i < fragments.Count; i++)
        {
            rect = rect.Union(fragments[i].GetRectInRoot());
        }

        return ToViewport(rect);
    }

    /// <summary>
    /// Converts a rectangle relative to the root of the fragment tree (see <see cref="Fragment.GetRectInRoot"/>) to the
    /// coordinates of the viewport (https://drafts.csswg.org/cssom-view/#css-layout-viewport).
    /// </summary>
    /// <remarks>The scroll position of the viewport is subtracted (the scroll positions of the scroll containers are already applied by <see cref="Fragment.GetOffsetInRoot"/>).</remarks>
    public PhysicalRect ToViewport(PhysicalRect rect)
    {
        var offset = _viewportScrollState?.Offset ?? PhysicalOffset.Zero;
        return offset == PhysicalOffset.Zero ? rect : rect.Translate(-offset);
    }

    /// <summary>
    /// Returns the size of the content box of a size container computed by the last layout (see
    /// <see cref="LayoutContext.OnSizeContainerLaidOut"/>), and whether its block size was known before its contents were laid
    /// out (<c>container-type: size</c>, or a definite block size).
    /// </summary>
    public bool TryGetContainerSize(Element element, out PhysicalSize size, out bool hasBlockSize)
    {
        EnsureLayout();
        return TryGetLaidOutContainerSize(element, out size, out hasBlockSize);
    }

    /// <summary>
    /// Returns the size of the content box of a size container computed by its last layout, without computing the layout (the
    /// container queries are evaluated while the styles are computed, possibly by the layout itself): during a layout pass, the
    /// size computed by the current pass once the container is laid out, else the size of the previous pass.
    /// </summary>
    public bool TryGetLaidOutContainerSize(Element element, out PhysicalSize size, out bool hasBlockSize)
    {
        if (_containerSizes is not null && _containerSizes.TryGetValue(element, out var value))
        {
            size = value.Size;
            hasBlockSize = value.HasBlockSize;
            return true;
        }

        size = default;
        hasBlockSize = false;
        return false;
    }

    /// <summary>
    /// Returns the used track sizes of a grid container computed by the last layout (see <see cref="LayoutContext.OnGridLaidOut"/>):
    /// the columns (the tracks of its inline axis) and the rows. It does not compute the layout: the box must come from the
    /// fragments of the current layout (a forced layout may build the box tree again, e.g. when a style sheet of the document
    /// changed, and the sizes are recorded for the new boxes).
    /// </summary>
    public bool TryGetGridTrackSizes(ContainerBox box, [NotNullWhen(true)] out GridTrackSizes? columns, [NotNullWhen(true)] out GridTrackSizes? rows)
    {
        if (_gridTrackSizes is not null && _gridTrackSizes.TryGetValue(box, out var value))
        {
            (columns, rows) = value;
            return true;
        }

        columns = null;
        rows = null;
        return false;
    }

    internal InlineLayoutAlgorithm RentInlineLayout() => _inlineLayouts.TryPop(out var algorithm) ? algorithm : new InlineLayoutAlgorithm();

    internal void ReturnInlineLayout(InlineLayoutAlgorithm algorithm) => _inlineLayouts.Push(algorithm);

    internal void RecordGridTrackSizes(ContainerBox box, GridTrackSizes columns, GridTrackSizes rows) => (_gridTrackSizes ??= new(ReferenceEqualityComparer.Instance))[box] = (columns, rows);

    internal void RecordContainerSize(Element element, PhysicalSize size, bool hasBlockSize, int pass) => (_containerSizes ??= new(ReferenceEqualityComparer.Instance))[element] = new ContainerSize(size, hasBlockSize, pass);

    /// <summary>Removes the sizes of the containers that the pass did not lay out (removed from the document, no longer size containers or not rendered).</summary>
    private void RemoveStaleContainerSizes(int pass)
    {
        if (_containerSizes is null || _containerSizes.Count == 0)
            return;

        List<Element>? stale = null;
        foreach (var (element, size) in _containerSizes)
        {
            if (size.Pass != pass)
            {
                (stale ??= []).Add(element);
            }
        }

        if (stale is not null)
        {
            foreach (var element in stale)
            {
                _containerSizes.Remove(element);
            }
        }
    }

    /// <summary>Returns a text representation of the fragment tree (see <see cref="FragmentTree.Dump(Fragment)"/>), after computing the layout.</summary>
    public string Dump() => EnsureLayout() is { } root ? FragmentTree.Dump(root) : "";

    /// <summary>
    /// Lays out the viewport box: the initial containing block, whose child is the principal box of the root element. The
    /// viewport is a scroll container whose <c>overflow</c> is propagated from the root or the body element
    /// (https://drafts.csswg.org/css-overflow-3/#overflow-propagation): its classic scrollbars take space from the initial
    /// containing block (https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing).
    /// </summary>
    private BoxFragment LayoutViewport(LayoutContext context, ContainerBox viewport)
    {
        var (overflowStyle, rootStyle) = ScrollContainers.GetViewportStyles(viewport);
        var overflow = ScrollContainers.GetViewportOverflow(overflowStyle);
        var thickness = ScrollContainers.GetScrollbarThickness(context, rootStyle);
        if (thickness <= 0 || ScrollContainers.HidesViewportScrollbars(context.Document))
            return LayoutViewport(context, viewport, default, Scrollbars.None);

        // The scrollbars of the viewport are on the right and at the bottom; scrollbar-gutter applies to the inline edges of
        // the root element
        var writingDirection = rootStyle is null ? WritingDirection.HorizontalLtr : new WritingDirection(rootStyle.WritingMode, Direction.Ltr);
        var gutter = rootStyle?.Overflow.ScrollbarGutter ?? ScrollbarGutter.Auto;
        var scrollbars = ScrollContainers.GetForcedScrollbars(overflow);
        if (_viewportScrollState is { } previous)
        {
            scrollbars |= previous.Scrollbars & ((overflow.X == OverflowKind.Auto ? Scrollbars.Horizontal : Scrollbars.None) | (overflow.Y == OverflowKind.Auto ? Scrollbars.Vertical : Scrollbars.None));
        }

        var root = LayoutViewport(context, viewport, ScrollContainers.GetScrollbarSides(scrollbars, overflow, writingDirection, gutter, thickness), scrollbars);
        for (var pass = 0; pass < 2; pass++)
        {
            var needed = ScrollContainers.GetNeededScrollbars(overflow, root.GetScrollingArea(), root.PaddingBoxRect);
            if (pass > 0)
            {
                needed |= scrollbars;
            }

            if (needed == scrollbars)
                break;

            scrollbars = needed;
            root = LayoutViewport(context, viewport, ScrollContainers.GetScrollbarSides(scrollbars, overflow, writingDirection, gutter, thickness), scrollbars);
        }

        return root;
    }

    private static BoxFragment LayoutViewport(LayoutContext context, ContainerBox viewport, Sides<double> scrollbarSides, Scrollbars scrollbars)
    {
        // https://drafts.csswg.org/css-display/#initial-containing-block: the root element is laid out in the initial
        // containing block, which has the size of the viewport (without its scrollbars) and the writing mode of the root element
        var size = new PhysicalSize(Math.Max(0, context.ViewportSize.Width - scrollbarSides.HorizontalSum()), Math.Max(0, context.ViewportSize.Height - scrollbarSides.VerticalSum()));
        context.InitialContainingBlockSize = size;
        var writingDirection = WritingDirection.FromStyle(viewport.Style.Layout);
        var logicalSize = writingDirection.ToLogical(size);
        var space = ConstraintSpace.ForContentBox(writingDirection, logicalSize.Inline, logicalSize.Block);
        var builder = new FragmentBuilder(writingDirection);

        // The next sibling is read after the layout of a child: the layout of a size container may build again the boxes of
        // the top layer that follow the root box (see BoxTree.RebuildContents)
        for (var child = viewport.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is not ContainerBox box)
                continue;

            // An absolutely positioned root element and the boxes of the top layer, whose "static position for left, right, and
            // top is zero" (https://drafts.csswg.org/css-position-4/#top-layer), are laid out in the initial containing block
            // (or the viewport for the fixed positioned boxes)
            if (PositionedLayout.IsAbsolutelyPositioned(box))
            {
                builder.AddOutOfFlowCandidate(box, LogicalOffset.Zero);
                continue;
            }

            // https://www.w3.org/TR/SVG11/coords.html#ViewportSpace: the viewport of an SVG document embedded by reference (an
            // object or embed element) is the box of the element, which is sized with the natural dimensions of the document:
            // the width and height of the outermost svg element only give these natural dimensions (as in the browsers)
            var childSpace = space;
            if (box.Kind == ContainerBoxKind.Replaced && box.Element is { } element && ReferenceEquals(element, context.Document.DocumentElement) && BoxGeneration.IsSvgRoot(element)
                && context.Document.BrowsingContext?.Container is HtmlObjectElement or HtmlEmbedElement)
            {
                childSpace = space with { FixedInlineSize = logicalSize.Inline, FixedBlockSize = logicalSize.Block };
            }

            var fragment = context.LayoutBox(box, childSpace);
            var margin = writingDirection.ToLogical(fragment.Margin);
            var inlineOffset = margin.InlineStart;

            // https://drafts.csswg.org/css2/#float-position: a floating root element is shifted to the line-left or line-right
            // edge of the initial containing block
            var layout = box.Style.Layout;
            if (layout.IsFloating && (ExclusionSpace.GetFloatSide(layout.Float, writingDirection) == FloatSide.LineLeft) == writingDirection.IsRightToLeft)
            {
                inlineOffset = logicalSize.Inline - writingDirection.ToLogical(fragment.Size).Inline - margin.InlineEnd;
            }

            builder.AddChild(fragment, new LogicalOffset(inlineOffset, margin.BlockStart));
        }

        var root = new BoxFragment(viewport, size, default, default, default, builder.Build(size))
        {
            ScrollbarsDisplayed = scrollbars,
            Offset = new PhysicalOffset(scrollbarSides.Left, scrollbarSides.Top),
        };

        // The margin box of the root element is the in-flow contents of the viewport (its scrollable overflow includes the
        // block-end margin of the root element, as in the browsers)
        var inFlow = root.LocalRect;
        foreach (var child in root.ChildrenSpan)
        {
            if (child is BoxFragment rootFragment)
            {
                inFlow = BoxFragment.UnionWithEmpty(inFlow, rootFragment.MarginBoxRect.Translate(rootFragment.Offset - rootFragment.RelativeOffset));
            }
        }

        root.InFlowBounds = inFlow;
        PositionedLayout.LayoutOutOfFlowDescendants(context, viewport, root, builder);
        return root;
    }

    internal Fragment? GetFirstFragment(LayoutBox box) => box.FragmentIndexVersion == _fragmentIndexVersion ? box.FirstFragment : null;

    /// <summary>
    /// Links the fragments of each box (<see cref="LayoutBox.FirstFragment"/>, <see cref="Fragment.NextFragmentOfBox"/>), in the
    /// order of the fragment tree, associates the scroll containers with their scrolling boxes (see <see cref="ScrollState"/>),
    /// and returns the fragments of the sticky positioned boxes in that order (or null).
    /// </summary>
    /// <param name="pass">The layout pass that built the fragment tree.</param>
    /// <param name="scrollbarsChangedIntrinsicSizes">
    /// Whether the pass displayed scrollbars of a scroll container that it did not assume when it sized a box from the intrinsic
    /// sizes of that container (see <see cref="ContainerBox.ScrollbarGuttersPass"/>).
    /// </param>
    private List<BoxFragment>? IndexFragments(int pass, out bool scrollbarsChangedIntrinsicSizes)
    {
        scrollbarsChangedIntrinsicSizes = false;
        _fragmentIndexVersion++;
        _scrollStateContainers?.Clear();
        _initialScrollTargetBoxes?.Clear();
        if (_root is null)
        {
            DiscardScrollStates();
            return null;
        }

        List<BoxFragment>? sticky = null;
        var stack = new Stack<Fragment>();
        if (_pages is { } pages)
        {
            // (the fragments of the pages of a document being printed, in the order of the pages)
            for (var i = pages.Count - 1; i >= 0; i--)
            {
                stack.Push(pages[i].Content);
            }
        }
        else
        {
            UpdateScrollState(ViewportScrollState, _root);
            stack.Push(_root);
        }

        while (stack.TryPop(out var fragment))
        {
            if (fragment.Box is { } box)
            {
                fragment.NextFragmentOfBox = null;
                if (box.FragmentIndexVersion != _fragmentIndexVersion)
                {
                    box.FragmentIndexVersion = _fragmentIndexVersion;
                    box.FirstFragment = fragment;

                    // The scroll-state query containers (https://drafts.csswg.org/css-conditional-5/#scroll-state-container)
                    if (fragment is BoxFragment { Box: { PseudoElement: null, Element: { } containerElement } } containerFragment
                        && (box.Style.Layout.ContainerType & ContainerType.ScrollState) != ContainerType.Normal
                        && ReferenceEquals(_boxTree!.GetPrincipalBox(containerElement), box))
                    {
                        (_scrollStateContainers ??= []).Add(containerFragment);
                    }

                    // The candidate initial scroll targets (https://drafts.csswg.org/css-scroll-snap-2/#initial-scroll-target), in tree order
                    if (box is ContainerBox candidate && candidate.Style.Layout.IsScrollInitialTarget)
                    {
                        (_initialScrollTargetBoxes ??= []).Add(candidate);
                    }
                }
                else
                {
                    box.LastFragment!.NextFragmentOfBox = fragment;
                }

                box.LastFragment = fragment;
                if (fragment is BoxFragment { Box: { Kind: not ContainerBoxKind.TableGrid, Style.Layout.Position: PositionKind.Sticky } } boxFragment)
                {
                    (sticky ??= []).Add(boxFragment);
                }

                if (fragment is BoxFragment { IsScrollContainer: true, Box: { ScrollingElement: { } element, Kind: not ContainerBoxKind.Viewport } } scrollContainer)
                {
                    var state = GetOrCreateScrollState(element);
                    if (state.LayoutVersion != _fragmentIndexVersion)
                    {
                        UpdateScrollState(state, scrollContainer);
                        scrollbarsChangedIntrinsicSizes |= scrollContainer.Box is { } container && container.ScrollbarGuttersPass == pass
                            && (scrollContainer.ScrollbarsDisplayed & ~container.AssumedScrollbars) != Scrollbars.None;
                    }
                }
            }

            var children = fragment.ChildrenSpan;
            for (var i = children.Length - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        DiscardScrollStates();
        return sticky;
    }

    /// <summary>The size of the content box of a size container (physical, zoomed px), whether its block size is known, and the pass that computed it.</summary>
    private readonly record struct ContainerSize(PhysicalSize Size, bool HasBlockSize, int Pass);
}
