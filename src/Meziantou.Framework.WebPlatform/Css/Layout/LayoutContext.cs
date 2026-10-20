using Meziantou.Framework.WebPlatform.Infra;
using System.Diagnostics;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The state of a layout pass of a document (see <see cref="LayoutEngine"/>): the initial containing block, the environment,
/// the buffers reused by the formatting contexts, and the hooks of the layout steps that interleave with the style (the
/// size container queries).
/// </summary>
internal sealed class LayoutContext
{
    internal LayoutContext(LayoutEngine engine, Document document, MediaEnvironment environment)
    {
        Engine = engine;
        Document = document;
        Environment = environment;
        Pass = engine.LayoutCount + 1;

        // https://drafts.csswg.org/css-display/#initial-containing-block: the size of the viewport (the iframe for a nested
        // browsing context, see StyleEngine.CreateEnvironment)
        ViewportSize = new PhysicalSize(environment.Width, environment.Height);
        InitialContainingBlockSize = ViewportSize;
        ScrollbarThickness = document.BrowsingContext is { } browsingContext ? Math.Max(0, (browsingContext.Top.Options.Viewport ?? ViewportOptions.Default).ScrollbarThickness) : 0;
    }

    public LayoutEngine Engine { get; }

    /// <summary>
    /// The intrinsic sizes of the column spanners of the multi-column container whose content is being measured
    /// (<see cref="MultiColumnFormattingContext.ComputeIntrinsicInlineSizes"/>), or null: the spanners are not content of the columns.
    /// </summary>
    internal ColumnSpannerIntrinsicSizes? ColumnSpannerSizes { get; set; }

    /// <summary>The number of multi-column containers whose strip is being laid out: a multi-column container laid out in a strip is nested (see <see cref="NestedColumns"/>).</summary>
    internal int MultiColumnDepth { get; set; }

    /// <summary>The states of the nested multi-column containers laid out in the strip of the outermost one: they keep the layout pass until it is fragmented.</summary>
    internal List<MultiColumnState> NestedColumnStates { get; } = [];

    /// <summary>
    /// The floats of the strip that is being laid out that the multi-column layout pushed to the start of a fragmentainer (see
    /// <see cref="FloatPush"/>): they are placed at or below a position of their block formatting context. Null otherwise.
    /// </summary>
    internal Dictionary<ContainerBox, FloatPush>? FloatPushes { get; set; }

    /// <summary>
    /// The fragmentainers of the grid lanes containers of the strip that is being laid out that are fragmented in their stacking
    /// axis (see <see cref="GridLanesBreaks"/>): their items are placed in each fragmentainer. Null otherwise.
    /// </summary>
    internal Dictionary<ContainerBox, GridLanesBreaks>? GridLanesBreaks { get; set; }

    /// <summary>
    /// The inputs of the layout of the strip that is being laid out that depend on its fragmentation, and the records of the
    /// boxes whose layout may depend on it (see <see cref="Layout.StripRelayout"/>). Null otherwise (also in the strips of the
    /// nested multi-column containers).
    /// </summary>
    internal StripRelayout? StripRelayout { get; set; }

    /// <summary>Whether a box is being laid out in the area of its next fragments (see <see cref="InlineVariant"/>).</summary>
    internal bool LaysOutNextFragments { get; set; }

    /// <summary>
    /// The position of the block-start margin edge of the floats laid out in a strip (<see cref="MultiColumnDepth"/>), in their
    /// block formatting context: the multi-column layout converts the positions of the strip to them (see <see cref="FloatPush"/>).
    /// </summary>
    internal Dictionary<ContainerBox, double> FloatPlacements { get; } = [];

    /// <summary>
    /// The number of the layout pass: the caches attached to the boxes for a pass (e.g. <see cref="ContainerBox.IntrinsicSizesCache"/>)
    /// are valid while it matches, and in the next passes while the layout result cache keeps them (see <see cref="IsCacheOfPreviousPassValid"/>).
    /// </summary>
    public int Pass { get; }

    public Document Document { get; }

    /// <summary>The environment the styles were computed with (the viewport size, the device pixel ratio...).</summary>
    public MediaEnvironment Environment { get; }

    /// <summary>The size of the viewport, including its scrollbars (<c>innerWidth</c>, <c>innerHeight</c>).</summary>
    public PhysicalSize ViewportSize { get; internal set; }

    /// <summary>
    /// The size of the initial containing block (https://drafts.csswg.org/css-display/#initial-containing-block): the viewport
    /// without its classic scrollbars (set by the layout of the viewport).
    /// </summary>
    public PhysicalSize InitialContainingBlockSize { get; internal set; }

    /// <summary>The thickness of the classic scrollbars (<see cref="ViewportOptions.ScrollbarThickness"/>), 0 for the overlay scrollbars or without browsing context.</summary>
    public double ScrollbarThickness { get; }

    /// <summary>Whether the document is in quirks mode (https://quirks.spec.whatwg.org/).</summary>
    public bool IsQuirksMode => Document.Mode == DocumentMode.Quirks;

    /// <summary>
    /// Whether the document is in quirks mode or in limited-quirks mode: the line height calculation quirk and the blocks ignore
    /// line-height quirk apply (https://quirks.spec.whatwg.org/#the-line-height-calculation-quirk).
    /// </summary>
    public bool UsesLineHeightQuirks => Document.Mode != DocumentMode.NoQuirks;

    /// <summary>
    /// Returns an instance of the inline layout algorithm, with its buffers (the line breaker, the bidi resolver, the text):
    /// the instances are reused by the layout engine. An atomic inline laid out by an inline formatting context rents another one.
    /// </summary>
    internal InlineLayoutAlgorithm RentInlineLayout() => Engine.RentInlineLayout();

    /// <summary>Returns an instance of the inline layout algorithm to the pool of the layout engine.</summary>
    internal void ReturnInlineLayout(InlineLayoutAlgorithm algorithm) => Engine.ReturnInlineLayout(algorithm);

    /// <summary>
    /// The data of the items of the inline formatting contexts measured during the pass, reused by their layout (see
    /// <see cref="InlineLayoutAlgorithm.InlineItemsData"/>).
    /// </summary>
    internal Dictionary<ContainerBox, InlineLayoutAlgorithm.InlineItemsData>? InlineItemsData { get; set; }

    /// <summary>
    /// The anchor elements by anchor name (https://drafts.csswg.org/css-anchor-position-1/#name), built by the first anchor query of
    /// the pass (see <see cref="AnchorQuery"/>).
    /// </summary>
    internal Dictionary<string, List<ContainerBox>>? AnchorNameIndex { get; set; }

    private static bool IsBlockSizeQueryContainer(ContainerBox box)
        => box.Kind != ContainerBoxKind.Replaced && (box.Style.Layout.ContainerType & ContainerType.Size) != ContainerType.Normal && UsedContainment.CanApplySizeContainment(box);

    /// <summary>
    /// Whether the pass uses the results of the previous passes (see <see cref="LayoutResultCache"/>), and the intrinsic sizes they
    /// computed: false when the layout engine does not (e.g. a document with anchor-positioned boxes, the printing), and for the
    /// layout that verifies the incremental layout (see <see cref="LayoutEngine.VerifyIncrementalLayout"/>).
    /// </summary>
    public bool UsesLayoutCache { get; init; }

    /// <summary>
    /// The number of layouts whose effects outside of their fragments are needed by the next passes (see <see cref="PreventLayoutCaching"/>):
    /// the results of the boxes being laid out are not kept when it changes during their layout.
    /// </summary>
    internal int LayoutCachingPreventionCount { get; private set; }

    /// <summary>The number of results of the previous passes used by the pass (see <see cref="LayoutResultCache"/>).</summary>
    internal int LayoutCacheHitCount { get; set; }

    /// <summary>
    /// Prevents the layout result cache from keeping the results of the boxes being laid out (the box and its ancestors): the
    /// layout has effects outside of the fragments that the next passes need (e.g. the size of a size container is recorded for
    /// the container queries, the boxes of the cues of a video are built again).
    /// </summary>
    internal void PreventLayoutCaching() => LayoutCachingPreventionCount++;

    /// <summary>
    /// Whether a cache of a box computed by a previous pass (its intrinsic sizes, see <see cref="IntrinsicSizesCache"/>) can be used:
    /// the pass uses the layout result cache, the cache was computed for the same generation of the layout engine and the same
    /// initial containing block, and the box is not laid out in a fragmentation context. The caches are dropped with the results
    /// of the boxes when the box tree changes (see <see cref="LayoutResultCache.Invalidate"/>).
    /// </summary>
    internal bool IsCacheOfPreviousPassValid(int generation, PhysicalSize initialContainingBlockSize)
        => UsesLayoutCache && generation >= 0 && generation == Engine.LayoutCacheGeneration && initialContainingBlockSize == InitialContainingBlockSize
            && MultiColumnDepth == 0 && ColumnSpannerSizes is null && FloatPushes is null && GridLanesBreaks is null;

    /// <summary>The generation a cache computed now is valid for (see <see cref="IsCacheOfPreviousPassValid"/>): -1 when the computation prevented the caching (it is valid for the pass only).</summary>
    internal int GetCacheGeneration(int preventionCountBefore) => UsesLayoutCache && preventionCountBefore == LayoutCachingPreventionCount ? Engine.LayoutCacheGeneration : -1;

    /// <summary>Lays out a box with the layout algorithm of its formatting context (see <see cref="FormattingContext.For"/>).</summary>
    public BoxFragment LayoutBox(ContainerBox box, in ConstraintSpace space)
    {
        // The result of a previous pass, when the box was laid out in the same constraint space (see LayoutResultCache)
        if (LayoutResultCache.TryGet(this, box, space, out var cached))
        {
            box.LastLayoutFragment = cached;
            cached.LayoutPass = Pass;
            return cached;
        }

        // A deeply nested box is laid out on a new stack (each level of the recursion of the formatting contexts uses ~20 KB)
        if (!StackGuard.HasSufficientStack)
        {
            var spaceCopy = space;
            return StackGuard.RunOnNewStack(() => LayoutBox(box, spaceCopy));
        }

        var cacheState = LayoutResultCache.BeginLayout(this, box, space);
        var fragment = FormattingContext.For(box).Layout(this, box, space);

        // The anchor queries find the fragment of an anchor box from its box (the last layout of the box in the pass)
        box.LastLayoutFragment = fragment;
        fragment.LayoutPass = Pass;

        // https://drafts.csswg.org/css-contain-2/#containment-layout: "For the purpose of the vertical-align property, or any other
        // property whose effects need to relate the position of the layout containment box's baseline to something other than
        // its descendants, the containment box is treated as having no baseline"
        // As in the browsers, `contain: size` alone keeps the baselines of the contents (WPT
        // css/css-contain/contain-size-baseline-001.html), but a size query container (`container-type: size`, which has no
        // layout containment: csswg-drafts#10544) gives no baseline to its parent (WPT
        // css/css-conditional/container-queries/nested-query-containers.html)
        if ((fragment.FirstBaseline is not null || fragment.LastBaseline is not null)
            && (UsedContainment.HasLayoutContainment(box) || IsBlockSizeQueryContainer(box)))
        {
            fragment.FirstBaseline = null;
            fragment.LastBaseline = null;
        }

        // https://github.com/w3c/csswg-drafts/issues/7660: the baselines of a scroll container are clamped to its border box (its
        // contents may be scrolled out of it)
        if (fragment.IsScrollContainer && (fragment.FirstBaseline is not null || fragment.LastBaseline is not null))
        {
            var blockSize = fragment.WritingDirection.ToLogical(fragment.Size).Block;
            fragment.FirstBaseline = fragment.FirstBaseline is { } first ? Math.Clamp(first, 0, blockSize) : null;
            fragment.LastBaseline = fragment.LastBaseline is { } last ? Math.Clamp(last, 0, blockSize) : null;
        }

        LayoutResultCache.Store(this, box, space, fragment, cacheState);
        return fragment;
    }

    /// <summary>
    /// Returns the intrinsic block size of a box (<see cref="BoxFragment.IntrinsicBlockSize"/>) laid out in a constraint space
    /// that measures it (<see cref="ConstraintSpace.MeasuresIntrinsicBlockSize"/>), cached for the layout pass (and the next ones,
    /// see <see cref="IsCacheOfPreviousPassValid"/>): a box measured
    /// again with the same space (e.g. by the layouts of its parent with and without a definite size) is not laid out again,
    /// which keeps the cost of nested flex containers linear.
    /// </summary>
    public double MeasureIntrinsicBlockSize(ContainerBox box, in ConstraintSpace space)
    {
        Debug.Assert(space.MeasuresIntrinsicBlockSize, "The space must measure the intrinsic block size");
        var cache = box.IntrinsicBlockSizeCache;
        if (cache is not null && cache.Space == space && (cache.Pass == Pass || IsCacheOfPreviousPassValid(cache.Generation, cache.InitialContainingBlockSize)))
            return cache.Size;

        var preventionCount = LayoutCachingPreventionCount;
        var size = LayoutBox(box, space).IntrinsicBlockSize;
        cache ??= box.IntrinsicBlockSizeCache = new IntrinsicBlockSizeCache();
        cache.Pass = Pass;
        cache.Space = space;
        cache.Size = size;
        cache.Generation = GetCacheGeneration(preventionCount);
        cache.InitialContainingBlockSize = InitialContainingBlockSize;
        return size;
    }

    /// <summary>
    /// Called by the formatting contexts when the content box of a size container
    /// (https://drafts.csswg.org/css-contain-3/#size-container, <c>container-type: size</c> or <c>inline-size</c>) is sized,
    /// before its contents are laid out: size containment makes its size independent of its contents. The block size of the
    /// content area is null when it depends on the contents (<c>inline-size</c> containment and an <c>auto</c> block size).
    /// </summary>
    /// <remarks>
    /// This is the style and layout interleaving of the container queries (https://drafts.csswg.org/css-conditional-5/#container-queries):
    /// the size is recorded (see <see cref="LayoutEngine.TryGetLaidOutContainerSize"/>), the size queries and the container
    /// units use it from now on. When the styles of the descendants of the container (and of its pseudo-elements) were computed
    /// with another size of the container (before its layout: the size of the previous layout, or the size known without
    /// layout), they are computed again, and the boxes of the contents of the container are built again, before they are laid
    /// out. The size of the container does not depend on the styles of its descendants, so this does not loop: the contents
    /// are styled again at most once per layout of the container (a box may be laid out more than once by a pass, e.g. with and
    /// without scrollbars, or to measure it).
    /// </remarks>
    public void OnSizeContainerLaidOut(ContainerBox box, in ContentArea content)
    {
        // (the sizes of the containers are recorded by each pass; the layouts of a box in the areas of its next fragments do not
        // change the size of the query containers: as in Blink, the size of a fragmented container is the size of its first
        // fragment, see InlineVariant)
        PreventLayoutCaching();
        if (box.Element is not { } element || box.PseudoElement is not null || LaysOutNextFragments)
            return;

        var size = content.WritingDirection.ToPhysical(new LogicalSize(content.InlineSize, content.BlockSize ?? 0));
        var hasBlockSize = content.BlockSize is not null;
        Engine.RecordContainerSize(element, size, hasBlockSize, Pass);

        // The sizes queried by the styles are in the coordinates of the computed values of the container (see ContainerQueries)
        var styleEngine = Document.GetStyleEngine();
        var zoom = box.Style.Layout.EffectiveZoom;
        var vertical = !content.WritingDirection.IsHorizontal;
        double? width = vertical && !hasBlockSize ? null : size.Width / zoom;
        double? height = !vertical && !hasBlockSize ? null : size.Height / zoom;
        if (!styleEngine.IsQueriedWithAnotherSize(element, width, height))
            return;

        styleEngine.InvalidateQueryContainerDescendants(element);
        Engine.BoxTree?.RebuildContents(box);
    }

    /// <summary>
    /// Called by the grid layout once the tracks of a grid container are sized: the used track sizes are recorded for the
    /// resolved values of <c>grid-template-columns</c> and <c>grid-template-rows</c> (see <see cref="LayoutEngine.TryGetGridTrackSizes"/>).
    /// </summary>
    /// <param name="box">The grid container.</param>
    /// <param name="columns">The columns (the tracks of the inline axis of the container).</param>
    /// <param name="rows">The rows (the tracks of its block axis).</param>
    public void OnGridLaidOut(ContainerBox box, GridTrackSizes columns, GridTrackSizes rows)
    {
        // (the track sizes are recorded by each pass)
        PreventLayoutCaching();
        Engine.RecordGridTrackSizes(box, columns, rows);
    }
}
