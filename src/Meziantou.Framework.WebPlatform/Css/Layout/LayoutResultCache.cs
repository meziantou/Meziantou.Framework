namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout result cache (as in Blink's LayoutNG): the fragment of the last layout of a box is kept with the box (see
/// <see cref="ContainerBox.LayoutResult"/>, and <see cref="ContainerBox.MeasureLayoutResult"/> for the layouts that measure its
/// intrinsic block size) with the constraint space it was laid out in, and returned again by the next layout
/// pass that lays the box out in the same constraint space, without laying out the box and its contents again. A change of the
/// box tree drops the results of the boxes that contain the changed boxes (see <see cref="Invalidate"/>): the boxes that did not
/// change are not laid out again, unless their constraint space changed (e.g. a percentage of the size of the containing block).
/// </summary>
/// <remarks>
/// <para>
/// The key of a result is its constraint space, and the size of the initial containing block (the orthogonal flows and the
/// fixed positioned boxes depend on it). The results are valid while the generation of the layout engine does not change
/// (<see cref="LayoutEngine.LayoutCacheGeneration"/>): it changes when something that the cache does not track changes (an
/// image is loaded, the box tree is built again...). A box that participates in the block formatting context of its parent
/// (<see cref="ConstraintSpace.BlockFormattingContext"/>, a new state for each layout of the root of the context) is laid out
/// relative to the current position of the context: its result is valid when the context has no float (its layout does not
/// depend on its position then), the same adjoining margins, no line clamping, text group alignment, column spanners nor
/// margins separated by clearance; the changes of the state of the context made by its layout (see
/// <see cref="BlockFormattingContextState.Operation"/>) are made again when the result is used.
/// </para>
/// <para>
/// A result is not kept when the layout of the box had effects outside of its fragments that the next passes need (see
/// <see cref="LayoutContext.PreventLayoutCaching"/>: the sizes of the size containers and of the grid tracks, the boxes of the
/// cues of a video, the boxes laid out by a layout API class, the multi-column and paged layouts), when its fragment has
/// absolutely positioned descendants whose containing block is an ancestor (they are laid out by it), and when the box is laid
/// out in a fragmentation context. A fragment is returned at most once per layout pass (a parent keeps the offsets of its
/// child fragments in them): a second layout of the box in the same pass lays it out again. The parents set the offsets of
/// the fragments they place, and some change the offsets of the children of the fragments (e.g. the alignment of the contents
/// of a table cell): the offsets of the children of a fragment are restored when it is used again.
/// </para>
/// </remarks>
internal static class LayoutResultCache
{
    /// <summary>
    /// Returns the fragment of the last layout of a box when it was laid out in the same constraint space and nothing it depends
    /// on changed since then (the changes of the state of the block formatting context made by its layout are made again).
    /// </summary>
    public static bool TryGet(LayoutContext context, ContainerBox box, in ConstraintSpace space, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BoxFragment? fragment)
    {
        fragment = null;
        if ((space.MeasuresIntrinsicBlockSize ? box.MeasureLayoutResult : box.LayoutResult) is not { } entry || !CanCache(context, box, space))
            return false;

        // (a fragment is returned once per pass: its parent sets its offset)
        if (entry.Generation != context.Engine.LayoutCacheGeneration || entry.Pass == context.Pass || entry.InitialContainingBlockSize != context.InitialContainingBlockSize || entry.ViewportSize != context.ViewportSize)
            return false;

        var blockFormattingContext = space.BlockFormattingContext;
        if (entry.ParticipatesInBlockFormattingContext != blockFormattingContext is not null || entry.Space != space with { BlockFormattingContext = null })
            return false;

        if (blockFormattingContext is not null && entry.StartMargins != blockFormattingContext.CurrentMargin)
            return false;

        entry.Pass = context.Pass;
        entry.Fragment.PrepareForReuse(entry.FragmentState);
        if (blockFormattingContext is not null)
        {
            blockFormattingContext.Replay(entry.Operations!);
        }

        context.LayoutCacheHitCount++;
        fragment = entry.Fragment;
        return true;
    }

    /// <summary>Returns the state of the block formatting context before the layout of a box, to keep its result (see <see cref="Store"/>).</summary>
    public static StoreState BeginLayout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        if (!CanCache(context, box, space))
            return default;

        var blockFormattingContext = space.BlockFormattingContext;
        return new StoreState(
            IsCacheable: true,
            PreventionCount: context.LayoutCachingPreventionCount,
            OperationStart: blockFormattingContext?.StartRecording() ?? 0,
            InvalidRecordings: blockFormattingContext?.InvalidRecordingCount ?? 0,
            StartMargins: blockFormattingContext?.CurrentMargin ?? default);
    }

    /// <summary>Keeps the result of the layout of a box (see <see cref="BeginLayout"/>) when it can be used again.</summary>
    public static void Store(LayoutContext context, ContainerBox box, in ConstraintSpace space, BoxFragment fragment, in StoreState state)
    {
        // (a layout without the cache, e.g. the verification of the incremental layout, keeps the results)
        if (!context.UsesLayoutCache)
            return;

        var blockFormattingContext = space.BlockFormattingContext;
        if (!state.IsCacheable || state.PreventionCount != context.LayoutCachingPreventionCount || fragment.PendingOutOfFlow is not null || fragment.HasPendingOutOfFlowDescendants
            || (blockFormattingContext is not null && (blockFormattingContext.InvalidRecordingCount != state.InvalidRecordings || !blockFormattingContext.Exclusions.IsEmpty || blockFormattingContext.SeparationCount != 0)))
        {
            if (space.MeasuresIntrinsicBlockSize)
            {
                box.MeasureLayoutResult = null;
            }
            else
            {
                box.LayoutResult = null;
            }

            return;
        }

        var entry = space.MeasuresIntrinsicBlockSize ? box.MeasureLayoutResult ??= new LayoutResultCacheEntry() : box.LayoutResult ??= new LayoutResultCacheEntry();
        entry.Space = space with { BlockFormattingContext = null };
        entry.ParticipatesInBlockFormattingContext = blockFormattingContext is not null;
        entry.StartMargins = state.StartMargins;
        entry.Operations = blockFormattingContext?.GetRecordedOperations(state.OperationStart);
        entry.Fragment = fragment;
        entry.FragmentState = fragment.SaveStateForReuse();
        entry.Generation = context.Engine.LayoutCacheGeneration;
        entry.Pass = context.Pass;
        entry.InitialContainingBlockSize = context.InitialContainingBlockSize;
        entry.ViewportSize = context.ViewportSize;
    }

    /// <summary>
    /// Drops the results of a box and of its ancestors, and their intrinsic sizes: the box changed (e.g. its children were
    /// built again), the boxes that contain it must be laid out again.
    /// </summary>
    public static void Invalidate(LayoutBox? box)
    {
        for (var current = box; current is not null; current = current.Parent)
        {
            if (current is ContainerBox container)
            {
                container.LayoutResult = null;
                container.MeasureLayoutResult = null;
                container.IntrinsicSizesCache = null;
                container.IntrinsicBlockSizeCache = null;
            }
        }
    }

    /// <summary>
    /// Returns a description of the first difference between the fragment tree of an incremental layout and the one of a layout
    /// without the results of the previous passes, or null when they are the same: the kinds, the boxes, the offsets and the
    /// sizes of the fragments, the edges, the baselines and the scrollbars of the box fragments, the texts of the text fragments
    /// (see <see cref="LayoutEngine.VerifyIncrementalLayout"/>).
    /// </summary>
    internal static string? DescribeDifference(Fragment incremental, Fragment full)
    {
        const double Tolerance = 1e-6;
        if (incremental.GetType() != full.GetType() || !ReferenceEquals(incremental.Box, full.Box))
            return $"{Describe(full)}: the fragment is {Describe(incremental)}";

        // (the sticky offsets of the previous pass stay in the offsets of the fragments that were not placed again by the pass:
        // they are applied after the pass, see StickyPosition)
        var offset = incremental is BoxFragment { StickyOffset: var stickyOffset } ? incremental.Offset - stickyOffset : incremental.Offset;
        if (!Equal(offset.X, full.Offset.X) || !Equal(offset.Y, full.Offset.Y) || !Equal(incremental.Size.Width, full.Size.Width) || !Equal(incremental.Size.Height, full.Size.Height))
            return $"{Describe(full)}: the rectangle is {incremental.Rect} instead of {full.Rect}";

        if (incremental.IsHiddenForPaint != full.IsHiddenForPaint)
            return $"{Describe(full)}: the fragment is hidden: {incremental.IsHiddenForPaint}";

        switch (incremental)
        {
            case BoxFragment box when full is BoxFragment fullBox:
                if (box.Margin != fullBox.Margin || box.Border != fullBox.Border || box.Padding != fullBox.Padding)
                    return $"{Describe(full)}: the margins, borders or paddings are {box.Margin}, {box.Border}, {box.Padding} instead of {fullBox.Margin}, {fullBox.Border}, {fullBox.Padding}";

                if (box.FirstBaseline != fullBox.FirstBaseline || box.LastBaseline != fullBox.LastBaseline)
                    return $"{Describe(full)}: the baselines are {box.FirstBaseline}, {box.LastBaseline} instead of {fullBox.FirstBaseline}, {fullBox.LastBaseline}";

                if (box.ScrollbarsDisplayed != fullBox.ScrollbarsDisplayed || box.RelativeOffset != fullBox.RelativeOffset)
                    return $"{Describe(full)}: the scrollbars or the relative offset";

                break;
            case TextFragment text when full is TextFragment fullText:
                if (text.Start != fullText.Start || text.End != fullText.End || text.GlyphCount != fullText.GlyphCount || !Equal(text.Baseline, fullText.Baseline))
                    return $"{Describe(full)}: the text is [{text.Start}, {text.End}) instead of [{fullText.Start}, {fullText.End})";

                break;
        }

        var children = incremental.ChildrenSpan;
        var fullChildren = full.ChildrenSpan;
        if (children.Length != fullChildren.Length)
            return $"{Describe(full)}: {children.Length} children instead of {fullChildren.Length}";

        for (var i = 0; i < children.Length; i++)
        {
            if (!ReferenceEquals(children[i].Parent, incremental))
                return $"{Describe(fullChildren[i])}: the parent of the fragment is not its parent in the tree";

            if (DescribeDifference(children[i], fullChildren[i]) is { } difference)
                return difference;
        }

        return null;

        static bool Equal(double x, double y) => Math.Abs(x - y) <= Tolerance || (double.IsNaN(x) && double.IsNaN(y)) || x == y;

        static string Describe(Fragment fragment)
        {
            var box = fragment.Box is { } layoutBox ? BoxTree.Dump(layoutBox).Split('\n')[0].Trim() : "line box";
            var path = new System.Text.StringBuilder(box);
            for (var parent = fragment.Parent; parent is not null && path.Length < 400; parent = parent.Parent)
            {
                if (parent.Box is ContainerBox { Element: { } element })
                {
                    path.Append(" in <").Append(element.LocalName).Append(element.Id is { Length: > 0 } id ? "#" + id : "").Append('>');
                }
            }

            return path.ToString();
        }
    }

    /// <summary>Whether the result of the layout of a box in a constraint space can be kept or used.</summary>
    private static bool CanCache(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        if (!context.UsesLayoutCache || context.MultiColumnDepth > 0 || context.ColumnSpannerSizes is not null || context.FloatPushes is not null || context.GridLanesBreaks is not null
            || box.LayoutApi is not null || space.OutsideMarker is not null || space.TrimsBlockStartMargin || space.TrimsBlockEndMargin)
        {
            return false;
        }

        return space.BlockFormattingContext is not { } blockFormattingContext
            || (blockFormattingContext.Exclusions.IsEmpty && blockFormattingContext.SeparationCount == 0 && blockFormattingContext.LineClamp is null
                && blockFormattingContext.MultiColumnFlow is null && blockFormattingContext.TextGroupAlignment is null);
    }

    /// <summary>The state of the layout cache when the layout of a box starts (see <see cref="BeginLayout"/>).</summary>
    internal readonly record struct StoreState(bool IsCacheable, int PreventionCount, int OperationStart, int InvalidRecordings, MarginStrut StartMargins);
}

/// <summary>The result of the last layout of a box (see <see cref="LayoutResultCache"/>).</summary>
internal sealed class LayoutResultCacheEntry
{
    /// <summary>The constraint space of the layout, without its block formatting context.</summary>
    public ConstraintSpace Space;

    /// <summary>Whether the box was laid out in the block formatting context of its parent.</summary>
    public bool ParticipatesInBlockFormattingContext;

    /// <summary>The adjoining margins of the block formatting context before the layout of the box.</summary>
    public MarginStrut StartMargins;

    /// <summary>The changes of the state of the block formatting context made by the layout of the box.</summary>
    public BlockFormattingContextState.Operation[]? Operations;

    public BoxFragment Fragment = null!;

    /// <summary>The state of the fragment when it was laid out, restored when it is used again (its parents change it when they place it).</summary>
    public BoxFragment.ReuseState FragmentState;

    /// <summary>The generation of the layout engine when the box was laid out (see <see cref="LayoutEngine.LayoutCacheGeneration"/>).</summary>
    public int Generation;

    /// <summary>The last layout pass that returned the fragment.</summary>
    public int Pass;

    public PhysicalSize InitialContainingBlockSize;

    public PhysicalSize ViewportSize;
}
