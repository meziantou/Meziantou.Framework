using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The content of a fragmentation context (https://drafts.csswg.org/css-break-3/#fragmentation-context) laid out in a single strip
/// of the inline size of its fragmentainers, and what its fragmentation needs to know about it: the possible breaks
/// (https://drafts.csswg.org/css-break-3/#possible-breaks), the unbreakable intervals (the line boxes and the monolithic boxes,
/// https://drafts.csswg.org/css-break-3/#monolithic), the column spanners and the extent of the content.
/// </summary>
/// <remarks>
/// <para>
/// The fragmentation of the layout is a pass over the fragment tree of the strip: the breaker (<see cref="ColumnBreaker"/>)
/// chooses the positions of the breaks in the strip, then <see cref="FragmentSlicer"/> distributes the fragments to the
/// fragmentainers, splitting the boxes that span several of them into one fragment per fragmentainer
/// (https://drafts.csswg.org/css-break-3/#box-splitting). The positions are logical, along the block axis of the fragmentation
/// context, from the block-start edge of the strip; the boxes whose writing mode differs from the one of the context are monolithic.
/// </para>
/// <para>
/// The possible breaks are the ones of https://drafts.csswg.org/css-break-3/#possible-breaks: between the in-flow siblings (class A)
/// and between the line boxes (class B) of the boxes that are not monolithic, and the empty block size of a box after its
/// contents (where the browsers break anywhere, e.g. a box with a fixed block size and no contents). A break between two
/// boxes is forced by <c>break-before</c>/<c>break-after</c> (propagated from the first and last children,
/// https://drafts.csswg.org/css-break-3/#break-propagation); it violates <c>avoid</c> values, <c>break-inside: avoid</c> on an
/// ancestor, and <c>orphans</c>/<c>widows</c> between the line boxes. A possible break inside an unbreakable interval (a line
/// box or a monolithic box of a parallel flow) is not valid.
/// </para>
/// <para>
/// The items of the flex containers and of the grids and the cells of the table rows are parallel flows
/// (https://drafts.csswg.org/css-break-3/#parallel-flows) that break independently: each one has its own flow
/// (<see cref="Groups"/>), with its own possible breaks. A nested multi-column container has its own flow too
/// (<see cref="NestedColumns"/>): the breaker places its rows of columns in the fragmentainers. The absolutely positioned
/// boxes whose containing block is in the strip are flows of their own (<see cref="OutOfFlowItems"/>). The floats are in the
/// flow of their container; the ones that do not fit in what remains of a fragmentainer are pushed to the next one by a new
/// layout of the strip (<see cref="FloatPusher"/>).
/// </para>
/// <para>
/// The layout of the strip does not know the fragmentainers: what depends on them is done by second passes over the strip (the
/// breaker, the slicer, a new layout of the strip with the floats to push), not by a layout in each fragmentainer.
/// </para>
/// </remarks>
internal sealed class FragmentationFlow
{
    private const double Epsilon = 0.01;

    private readonly WritingDirection _writingDirection;
    private readonly HashSet<ContainerBox>? _spannerBoxes;
    private readonly List<(double Start, double End)> _unbreakables = [];
    private readonly FragmentationFlow _root;
    private Dictionary<Fragment, double>? _subtreeEnds;

    /// <summary>The floats of the flow (see <see cref="AddFloat"/>).</summary>
    private List<FloatExtent>? _floats;

    /// <summary>The floats of the strip and of its parallel flows, in the order of the content.</summary>
    private List<FloatExtent>? _allFloats;

    /// <summary>The floats that the multi-column layout pushed to the start of a fragmentainer (see <see cref="FloatPush"/>).</summary>
    private Dictionary<ContainerBox, FloatPush>? _pushedFloats;

    /// <summary>The in-flow boxes of the flow that avoid the floats (they establish a formatting context), when floats are pushed: see <see cref="GetFloatAvoider"/>.</summary>
    private List<FloatAvoider>? _avoiders;

    /// <summary>The breaks of the pushed floats that are fragmented apart (see <see cref="GetFloatAvoider"/>), and their first fragmentainer.</summary>
    private Dictionary<BoxFragment, FlowMap>? _floatMaps;

    /// <summary>
    /// The spaces between two in-flow siblings (or before a first child) that are not made of their margins: the clearance of a
    /// box, the offset of a box that avoids the floats (see <see cref="ClearanceGaps"/>).
    /// </summary>
    private List<ClearanceGap>? _clearanceGaps;

    /// <summary>The block-start edges of the line boxes of the flow that are trimmed when they are the first formatted line of a fragmentainer, and how much (see <see cref="ApplyFragmentainerTrims"/>).</summary>
    private List<(double Position, double Trim, bool IsOfColumns)>? _lineStartTrims;

    /// <summary>The block-end edges of the line boxes of the flow that are trimmed when they are the last formatted line of a fragmentainer, and how much.</summary>
    private List<(double Position, double Trim, bool IsOfColumns)>? _lineEndTrims;

    /// <summary>The table headers and footers that are repeated in each fragment of their table grid box (see <see cref="GetRepeatedSections"/>).</summary>
    private Dictionary<BoxFragment, RepeatedSections>? _repeatedSections;

    /// <summary>The table grid boxes of this flow whose header or footer may be repeated (see <see cref="GetRepeatedSections"/>), and their block-start edges.</summary>
    private List<(double Start, double End, RepeatedSections Sections)>? _repeatedTables;

    /// <summary>The boxes of the flow with <c>break-inside: avoid</c>.</summary>
    private List<(double Start, double End, BoxFragment? Float, bool IsFloat)>? _avoidedBoxes;
    private Dictionary<BoxFragment, ParallelGroup>? _groupsByContainer;

    /// <summary>The parallel flows of the nested multi-column containers (see <see cref="NestedColumns"/>).</summary>
    private Dictionary<BoxFragment, ParallelItem>? _nestedItems;

    /// <summary>The absolutely positioned boxes whose static position is in the strip of a nested multi-column container and whose containing block is outside of it (see <see cref="GetNestedStaticPosition"/>).</summary>
    private Dictionary<ContainerBox, (ParallelItem Item, int Index)>? _nestedStaticPositions;
    /// <summary>The parallel flows of the contents of the boxes whose block size does not follow their contents (see <see cref="TryAddDefiniteBox"/>).</summary>
    private Dictionary<BoxFragment, ParallelItem>? _definiteItems;
    private HashSet<BoxFragment>? _groupAncestors;

    /// <summary>The absolutely positioned boxes of the strip that are fragmented apart (see <see cref="OutOfFlowItems"/>), by fragment.</summary>
    private Dictionary<BoxFragment, ParallelItem>? _outOfFlowByFragment;
    private List<ParallelItem>? _outOfFlowItems;

    /// <summary>Whether the absolutely positioned boxes of the strip are fragmented apart (see <see cref="OutOfFlowItems"/>).</summary>
    private bool _separatesOutOfFlow;

    /// <summary>The items of the column flex containers that start a fragmentainer after a forced break although they overlap the item before them, and the position of the break (see <see cref="GetForcedStart"/>).</summary>
    private Dictionary<Fragment, double>? _forcedStarts;

    /// <summary>The order in the content of the strip of the in-flow boxes without block size (see <see cref="GetEmptyOrdinal"/>).</summary>
    private Dictionary<Fragment, int>? _emptyOrdinals;
    private int _sequence;
    private bool _unbreakablesSorted;

    /// <summary>The largest end of the unbreakable intervals up to each index (sorted by start).</summary>
    private double[]? _maxEnds;

    private FragmentationFlow(WritingDirection writingDirection, HashSet<ContainerBox>? spannerBoxes, FragmentationFlow? root = null)
    {
        _writingDirection = writingDirection;
        _spannerBoxes = spannerBoxes;
        _root = root ?? this;
    }

    /// <summary>
    /// The boxes of the flow whose contents are parallel flows (https://drafts.csswg.org/css-break-3/#parallel-flows): the items of
    /// a line of a row flex container, the items of a grid, the cells of a table row, the lines of a multi-line column flex
    /// container. Each parallel flow has its own possible breaks and breaks independently. Sorted by position.
    /// </summary>
    public List<ParallelGroup>? Groups { get; private set; }

    /// <summary>The number of times the breaker ran on the flow (the state of the parallel flows of the previous runs is stale).</summary>
    public int Generation { get; private set; }

    /// <summary>The floats of the strip and of its parallel flows, in the order of the content (see <see cref="AddFloat"/>).</summary>
    public List<FloatExtent>? Floats => _root._allFloats;

    /// <summary>
    /// Whether the flow may break before the floats that are not pushed yet (<see cref="BreakCandidate.IsBeforeFloat"/>): the
    /// columns are balanced as if the floats that do not fit were pushed to the next column.
    /// </summary>
    public bool BreaksBeforeFloats
    {
        get => _root._breaksBeforeFloats;
        set => _root._breaksBeforeFloats = value;
    }

    private bool _breaksBeforeFloats;

    /// <summary>The spaces of the strip and of its parallel flows where a box was moved below floats (its clearance, the offset of a box that avoids the floats).</summary>
    public List<ClearanceGap>? ClearanceGaps => _root._clearanceGaps;

    /// <summary>Whether floats of this flow were pushed to the start of a fragmentainer (see <see cref="FloatPush"/>).</summary>
    public bool HasPushedFloats { get; private set; }

    /// <summary>Whether the strip contains a grid lanes container (see <see cref="GridLanesFragmentation"/>).</summary>
    public bool HasGridLanes => _root._hasGridLanes;

    private bool _hasGridLanes;

    /// <summary>Returns the breaks of a pushed float that is fragmented apart from the flow of its containing block (see <see cref="GetFloatAvoider"/>), or null.</summary>
    public FlowMap? GetFloatMap(BoxFragment box) => _root._floatMaps is { } maps && maps.TryGetValue(box, out var map) ? map : null;

    /// <summary>Records the breaks of a pushed float that is fragmented apart.</summary>
    public void SetFloatMap(BoxFragment box, FlowMap map) => (_root._floatMaps ??= [])[box] = map;

    private List<KeptFloat>? _keptFloats;

    /// <summary>The floats kept in the page before a forced break that their margin box crosses (see <see cref="KeepFloat"/>).</summary>
    public List<KeptFloat>? KeptFloats => _root._keptFloats;

    /// <summary>
    /// Keeps in a page a float that starts before a forced page break and whose margin box ends after it, in the rest of the page:
    /// as in Blink, the float is in the page of its position in the content (the breaks of the content after the break do not
    /// move it), and its exclusion ends at the break (see <see cref="StripRelayout.UpdateFloatExclusionEnds"/>): the content
    /// after the break, in the next pages, is not laid out beside it.
    /// </summary>
    /// <param name="extent">The float.</param>
    /// <param name="map">The page of the float.</param>
    /// <param name="breakPosition">The position of the break in the strip.</param>
    public void KeepFloat(in FloatExtent extent, FlowMap map, double breakPosition)
    {
        SetFloatMap(extent.Fragment, map);
        (_root._keptFloats ??= []).Add(new KeptFloat(extent.Fragment, extent.Start, breakPosition));
    }

    /// <summary>Forgets the floats kept before the forced breaks (the breaks are found again, see <see cref="KeepFloat"/>).</summary>
    public void ClearKeptFloats()
    {
        if (_root._keptFloats is not { } kept)
            return;

        foreach (var keptFloat in kept)
        {
            _root._floatMaps?.Remove(keptFloat.Fragment);
        }

        kept.Clear();
    }

    /// <summary>
    /// Returns the box of the flow that avoids the floats, was laid out before a pushed float (which it does not see) and
    /// continues beside it after a position: as in Blink, its part in the fragmentainer of the float is placed below the float.
    /// The layout in the strip does not know the fragmentainers, so the breaker shifts the content of the flow from the start
    /// of the fragmentainer by the block size of the float there, and the float is fragmented apart (it is not shifted).
    /// Null when no box avoids the float there (the box and the float do not overlap in the inline axis, or they are not in
    /// the same block formatting context).
    /// </summary>
    public BoxFragment? GetFloatAvoider(in FloatExtent extent, double position)
    {
        if (_avoiders is null)
            return null;

        (double Start, double End)? floatRange = null;
        foreach (var avoider in _avoiders)
        {
            if (avoider.Start >= extent.Start - Epsilon || avoider.End <= position + Epsilon || !ReferenceEquals(avoider.Context, extent.Context))
                continue;

            floatRange ??= GetInlineRange(extent.Fragment, withMargins: true);
            var range = GetInlineRange(avoider.Fragment, withMargins: false);

            // (a box laid out in the area beside the floats of its fragment there, see InlineVariant)
            if (Relayout?.GetInlineVariants(avoider.Fragment) is { } variants && StripRelayout.FindVariant(variants, position - avoider.Start) is { } variant)
            {
                var start = range.Start + (_writingDirection.IsHorizontal ? variant.PhysicalDelta.X : variant.PhysicalDelta.Y);
                range = (start, start + variant.InlineSize);
            }

            if (range.Start < floatRange.Value.End - Epsilon && range.End > floatRange.Value.Start + Epsilon)
                return avoider.Fragment;
        }

        return null;
    }

    /// <summary>Returns the range of a fragment of the strip in the inline axis of the fragmentation context (physical coordinates of the strip).</summary>
    private (double Start, double End) GetInlineRange(BoxFragment fragment, bool withMargins)
    {
        var offset = fragment.Offset;
        for (var parent = fragment.Parent; parent is not null; parent = parent.Parent)
        {
            offset += parent.Offset;
        }

        var margin = withMargins ? fragment.Margin : default;
        return _writingDirection.IsHorizontal
            ? (offset.X - margin.Left, offset.X + fragment.Size.Width + margin.Right)
            : (offset.Y - margin.Top, offset.Y + fragment.Size.Height + margin.Bottom);
    }

    /// <summary>
    /// Returns the header and the footer of a table grid box that are repeated in each of its fragments
    /// (https://drafts.csswg.org/css-tables-3/#repeated-headers), or null. As in Blink (the only browser that repeats them): the
    /// first <c>table-header-group</c> and the first <c>table-footer-group</c> of the table when they have
    /// <c>break-inside: avoid</c> and are not taller than a quarter of the fragmentainer where the table starts (the breaker
    /// decides it, see <see cref="RepeatedSections.IsHeaderDisabled"/>). The breaker reserves their block
    /// size at the start and at the end of the fragmentainers where the table continues (as the edges of the boxes with
    /// <c>box-decoration-break: clone</c>), and the slicer adds a copy of their fragments to the fragments of the table grid box.
    /// </summary>
    public RepeatedSections? GetRepeatedSections(BoxFragment tableGrid) => _root._repeatedSections is { } sections && sections.TryGetValue(tableGrid, out var result) ? result : null;

    /// <summary>The table grid boxes of this flow whose header or footer may be repeated, and their block-start edges in the strip.</summary>
    public List<(double Start, double End, RepeatedSections Sections)>? RepeatedTables => _repeatedTables;

    /// <summary>
    /// Returns the block size that a break in a portion of the strip adds to its content: the header and the footer that the
    /// table that is broken repeats (the columns are balanced with them).
    /// </summary>
    public double GetRepeatedSize(double start, double end)
    {
        var result = 0d;
        if (_repeatedTables is not null)
        {
            foreach (var (tableStart, tableEnd, sections) in _repeatedTables)
            {
                if (tableStart < end - Epsilon && tableEnd > start + Epsilon)
                {
                    result = Math.Max(result, sections.HeaderSize + sections.FooterSize);
                }
            }
        }

        return result;
    }

    /// <summary>Finds the header and the footer of a table grid box that are repeated (see <see cref="GetRepeatedSections"/>).</summary>
    private RepeatedSections? FindRepeatedSections(BoxFragment tableGrid, double top)
    {
        var children = tableGrid.ChildrenSpan;
        var count = Math.Min(tableGrid.InFlowChildCount, children.Length);
        BoxFragment? header = null;
        BoxFragment? footer = null;
        var headerIndex = -1;
        var footerIndex = -1;
        for (var i = 0; i < count; i++)
        {
            if (children[i] is not BoxFragment { Box.IsOutOfFlow: false } section)
                continue;

            if (section.Box.Display.Internal == DisplayInternal.TableHeaderGroup && header is null)
            {
                (header, headerIndex) = (section, i);
            }
            else if (section.Box.Display.Internal == DisplayInternal.TableFooterGroup && footer is null)
            {
                (footer, footerIndex) = (section, i);
            }
        }

        // (the header is the first section and the footer the last one: the table layout orders them)
        if (header is not null && (headerIndex != 0 || !IsRepeatable(header)))
        {
            header = null;
        }

        if (footer is not null && (footerIndex != count - 1 || !IsRepeatable(footer)))
        {
            footer = null;
        }

        if ((header is null && footer is null) || count < 2)
            return null;

        // The block size they take in a fragment: with the border spacing between them and the other sections
        var headerSize = 0d;
        var footerSize = 0d;
        if (header is not null)
        {
            var headerTop = GetBlockRange(header, tableGrid.Size, tableGrid.WritingDirection, top).Top;
            headerSize = GetBlockRange(children[1], tableGrid.Size, tableGrid.WritingDirection, top).Top - headerTop;
        }

        if (footer is not null)
        {
            var footerBottom = GetBlockRange(footer, tableGrid.Size, tableGrid.WritingDirection, top).Bottom;
            footerSize = footerBottom - GetBlockRange(children[count - 2], tableGrid.Size, tableGrid.WritingDirection, top).Bottom;
        }

        if (count == 2 && header is not null && footer is not null)
            return null;

        if (headerSize <= Epsilon)
        {
            header = null;
            headerSize = 0;
        }

        if (footerSize <= Epsilon)
        {
            footer = null;
            footerSize = 0;
        }

        return header is null && footer is null ? null : new RepeatedSections(header, headerSize, footer, footerSize)
        {
            HeaderBlockSize = header is null ? 0 : _writingDirection.ToLogical(header.Size).Block,
            FooterBlockSize = footer is null ? 0 : _writingDirection.ToLogical(footer.Size).Block,
        };

        // (in the pages, the header and the footer groups are repeated whatever their break-inside, as in Gecko, when they are not
        // taller than a quarter of the page area: https://drafts.csswg.org/css-tables-3/#repeated-headers "When rendering the
        // document into a paged media, user agents must repeat header rows and footer rows on each page spanned by a table")
        bool IsRepeatable(BoxFragment section) => section.Box.Style.Layout.Fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn
            || (_root._isPaged && PageBreakRules.Current?.InitialContainingBlock is { } initialContainingBlock
                && _writingDirection.ToLogical(section.Size).Block * 4 <= _writingDirection.ToLogical(initialContainingBlock.Size).Block + Epsilon);
    }

    /// <summary>
    /// Whether a box of the strip or of its parallel flows may be taller once fragmented than in the strip without being broken
    /// (see <see cref="ParallelItem.EncompassesContents"/>): the slicer creates the fragment of such a box again.
    /// </summary>
    public bool GrowsUnbroken { get; private set; }

    /// <summary>Whether a box of the strip of this flow grows without being broken (see <see cref="GrowsUnbroken"/>).</summary>
    public bool IsRootGrowingUnbroken => _root.GrowsUnbroken;

    /// <summary>
    /// Whether the monolithic content that does not fit in an empty fragmentainer is sliced at its end (the pages, see
    /// <see cref="PageBreakRules"/>), instead of overflowing it: the strip of this flow and of its parallel flows.
    /// </summary>
    public bool SlicesMonolithicContent
    {
        get => _root._slicesMonolithicContent;
        set => _root._slicesMonolithicContent = value;
    }

    private bool _slicesMonolithicContent;

    private HashSet<int>? _monolithicSlices;

    /// <summary>
    /// Records that the fragmentainer of an index starts in monolithic content sliced by the pages: as in Blink (whose page
    /// grows over the monolithic content that does not fit), the boxes with <c>box-decoration-break: clone</c> do not repeat
    /// their edges there.
    /// </summary>
    public void AddMonolithicSlice(int index) => (_root._monolithicSlices ??= []).Add(index);

    /// <summary>Returns whether the fragmentainer of an index starts in monolithic content sliced by the pages (see <see cref="AddMonolithicSlice"/>).</summary>
    public bool StartsInMonolithicSlice(int index) => _root._monolithicSlices?.Contains(index) == true;

    /// <summary>Forgets the slices of the monolithic content (the breaks are found again).</summary>
    public void ClearMonolithicSlices() => _root._monolithicSlices?.Clear();

    /// <summary>Whether the flow is collected with the rules of the pages (see <see cref="PageBreakRules"/>).</summary>
    private bool _isPaged;

    /// <summary>Whether the fragmentation context of the flow is the pages (see <see cref="PageBreakRules"/>), else columns.</summary>
    public bool IsPaged => _root._isPaged;

    /// <summary>Starts a new run of the breaker.</summary>
    public int NextGeneration() => ++_root.Generation;

    /// <summary>Returns the parallel flows of the contents of a box (see <see cref="Groups"/>), or null.</summary>
    public ParallelGroup? GetGroup(BoxFragment container) => _root._groupsByContainer is { } groups && groups.TryGetValue(container, out var group) ? group : null;

    /// <summary>Returns the parallel flow of a nested multi-column container that the breaker fragments itself (see <see cref="NestedColumns"/>), or null.</summary>
    public ParallelItem? GetNestedColumns(BoxFragment box) => _root._nestedItems is { } items && items.TryGetValue(box, out var item) ? item : null;

    /// <summary>Whether the strip has nested multi-column containers that the breaker fragments itself (see <see cref="NestedColumns"/>).</summary>
    public bool HasNestedColumns => _root._nestedItems is not null;

    /// <summary>The parallel flows of the nested multi-column containers of the strip that the breaker fragments itself (see <see cref="NestedColumns"/>), or null.</summary>
    public Dictionary<BoxFragment, ParallelItem>.ValueCollection? NestedItems => _root._nestedItems?.Values;

    /// <summary>
    /// Returns the nested multi-column container whose strip contains the static position of an absolutely positioned box
    /// whose containing block is outside of that strip, and the index of the box in <see cref="NestedColumns.OutOfFlow"/>, or null.
    /// </summary>
    public (ParallelItem Item, int Index)? GetNestedStaticPosition(ContainerBox box) => _root._nestedStaticPositions is { } positions && positions.TryGetValue(box, out var position) ? position : null;

    /// <summary>Returns the parallel flow of the contents of a box whose block size does not follow them (see <see cref="TryAddDefiniteBox"/>), or null.</summary>
    public ParallelItem? GetDefiniteBox(BoxFragment box) => _root._definiteItems is { } items && items.TryGetValue(box, out var item) ? item : null;

    /// <summary>
    /// The absolutely positioned boxes of the strip whose containing block is in the strip, in the order of the content. As in
    /// Blink, they do not take part in the fragmentation of the in-flow content: each one is a flow of its own
    /// (<see cref="ParallelItem.OutOfFlow"/>) that the breaker places once the in-flow content is (see
    /// <c>ColumnBreaker.PlaceOutOfFlow</c>), from the offset of the box in its containing block, in as many fragmentainers as it
    /// needs (new fragmentainers are created for the boxes that end after the in-flow content).
    /// </summary>
    public List<ParallelItem>? OutOfFlowItems => _root._outOfFlowItems;

    /// <summary>Returns the flow of an absolutely positioned box that is fragmented apart (see <see cref="OutOfFlowItems"/>), or null.</summary>
    public ParallelItem? GetOutOfFlowItem(BoxFragment box) => _root._outOfFlowByFragment is { } items && items.TryGetValue(box, out var item) ? item : null;

    /// <summary>
    /// Returns the parallel flow that contains the contents of a fragment of the strip (the nearest ancestor that is an absolutely
    /// positioned box fragmented apart, a box whose block size does not follow its contents, or an item of parallel flows), or
    /// null when they are in the flow of the strip.
    /// </summary>
    public ParallelItem? GetContainingItem(BoxFragment fragment)
    {
        for (Fragment? current = fragment; current is BoxFragment box; current = current.Parent)
        {
            if (GetOutOfFlowItem(box) is { } outOfFlow)
                return outOfFlow;

            if (GetDefiniteBox(box) is { } definite)
                return definite;

            if (box.Parent is BoxFragment parent && GetGroup(parent) is { } group && group.ItemsByFragment.TryGetValue(box, out var item))
                return item;
        }

        return null;
    }

    /// <summary>The parallel flow whose contents are this flow, or null for the flow of the strip.</summary>
    public ParallelItem? OwnerItem { get; set; }

    /// <summary>The inputs of the layout of the strip that depend on its fragmentation (see <see cref="StripRelayout"/>), or null.</summary>
    public StripRelayout? Relayout
    {
        get => _root._relayout;
        set => _root._relayout = value;
    }

    private StripRelayout? _relayout;

    /// <summary>
    /// Returns the block-end edge of the absolutely positioned boxes that are fragmented apart and start in a portion of the strip
    /// (<paramref name="includesEnd"/>: also the ones that start after it), at least <paramref name="extentEnd"/>: the columns
    /// are balanced with them.
    /// </summary>
    public double GetOutOfFlowExtent(double start, double end, bool includesEnd, double extentEnd)
    {
        if (_root._outOfFlowItems is { } items)
        {
            foreach (var item in items)
            {
                if (IsOutOfFlowIn(item, start, end, includesEnd))
                {
                    // (rounded down to the layout units of the browsers: a box that ends a fraction of a unit after the in-flow
                    // content does not make the columns taller)
                    var itemEnd = Math.Max(Math.Min(item.End, item.OutOfFlow!.ClipEnd), item.Flow.ExtentEnd);
                    extentEnd = Math.Max(extentEnd, start + (Math.Floor(((itemEnd - start) * 64) + 1e-6) / 64));
                }
            }
        }

        return extentEnd;
    }

    /// <summary>
    /// Returns the block size of the tallest content that is not broken (the monolithic content, and as in Blink the boxes with
    /// <c>break-inside: avoid</c>) of the absolutely positioned boxes that are fragmented apart and start in a portion of the
    /// strip: the balanced columns are at least as tall.
    /// </summary>
    public double GetOutOfFlowTallestUnbreakable(double start, double end, bool includesEnd)
    {
        var tallest = 0d;
        if (_root._outOfFlowItems is { } items)
        {
            foreach (var item in items)
            {
                if (!IsOutOfFlowIn(item, start, end, includesEnd))
                    continue;

                tallest = Math.Max(tallest, item.Flow.GetTallestUnbreakable(double.NegativeInfinity, double.PositiveInfinity));
                if (item.Flow._avoidedBoxes is { } avoided)
                {
                    foreach (var (avoidedStart, avoidedEnd, _, _) in avoided)
                    {
                        tallest = Math.Max(tallest, avoidedEnd - avoidedStart);
                    }
                }
            }
        }

        return tallest;
    }

    /// <summary>
    /// Returns whether an absolutely positioned box that is fragmented apart is content of the columns of a portion of the strip
    /// for their balancing: it starts in the portion, and its containing block is not interrupted by the column spanner after
    /// the portion (as in Blink, the box is then placed once the content after the spanner is).
    /// </summary>
    private bool IsOutOfFlowIn(ParallelItem item, double start, double end, bool includesEnd)
    {
        if (item.Start < start - Epsilon)
            return false;

        var spanners = _root.Spanners;
        var portion = GetOutOfFlowPortion(item);
        if (includesEnd)
            return portion == spanners.Count;

        return portion < spanners.Count && item.Start <= end + Epsilon && item.OutOfFlow!.ContainerEnd <= end + Epsilon
            && Math.Abs(spanners[portion].MarginBoxStart - end) < Epsilon;
    }

    /// <summary>
    /// Returns the portion of the strip (the content between two column spanners) of an absolutely positioned box that is
    /// fragmented apart: the one where its containing block starts, or the one of its position for a box placed in the content
    /// of its containing block (its static position): the first portion of the containing block that ends after it.
    /// </summary>
    public int GetOutOfFlowPortion(ParallelItem item)
    {
        var placement = item.OutOfFlow!;
        if (!placement.IsPlacedInContent)
            return placement.ContainerPortion;

        var spanners = _root.Spanners;
        for (var portion = placement.ContainerPortion; portion < placement.ContainerEndPortion && portion < spanners.Count; portion++)
        {
            if (item.Start < spanners[portion].MarginBoxStart - Epsilon)
                return portion;
        }

        return placement.ContainerEndPortion;
    }

    /// <summary>
    /// Returns the order in the content of the strip of an in-flow box without block size, to compare with
    /// <see cref="BreakCandidate.Sequence"/>: the box is before a break at its position when its order is lower (the break is
    /// after the box: as in the browsers, a box without block size at the end of a fragmentainer stays in it). Null for the other boxes.
    /// </summary>
    public int? GetEmptyOrdinal(Fragment fragment) => _root._emptyOrdinals is { } ordinals && ordinals.TryGetValue(fragment, out var ordinal) ? ordinal : null;

    /// <summary>
    /// Returns the position of the forced break before an item of a column flex container that overlaps the item before it (a
    /// negative margin), or null: the item is in the fragmentainer that starts at the break, before its start (its position in
    /// the strip is before the break).
    /// </summary>
    public double? GetForcedStart(Fragment fragment) => _root._forcedStarts is { } starts && starts.TryGetValue(fragment, out var position) ? position : null;

    /// <summary>Returns whether a box or one of its descendants has parallel flows.</summary>
    public bool ContainsGroup(BoxFragment box) => _root._groupAncestors is { } ancestors && ancestors.Contains(box);

    /// <summary>The possible breaks at a single position, sorted by position.</summary>
    public List<BreakCandidate> Candidates { get; } = [];

    /// <summary>The ranges of breakable positions (<see cref="BreakCandidate.RangeEnd"/>), sorted by start.</summary>
    public List<BreakCandidate> Ranges { get; } = [];

    /// <summary>
    /// The gutters between the rows of the grid containers of the strip that no item spans across, and the gaps between the lines
    /// of the row flex containers: "the gap disappears when it coincides with a fragmentation break"
    /// (https://drafts.csswg.org/css-gaps-1/#gaps).
    /// </summary>
    private List<(double Start, double End)>? _gaps;

    /// <summary>
    /// Returns the position where the content continues after a break at a position: the end of the gap the break coincides with
    /// (the break is in the gap, or the gap is the last content before the break), else the position.
    /// </summary>
    public double SkipGap(double position)
    {
        if (_gaps is not null)
        {
            foreach (var (start, end) in _gaps)
            {
                if (position >= start - Epsilon && position < end - Epsilon)
                    return end;
            }
        }

        return position;
    }

    /// <summary>Returns whether a block size of a box does not follow its contents: it is specified, or limited by its maximum block size.</summary>
    private bool HasDefiniteBlockSize(BoxFragment box)
    {
        // (the block size of the anonymous fieldset content box is the one that remains in its fieldset)
        if (box.Box.IsFieldsetContent)
            return box.Parent is BoxFragment fieldset && HasDefiniteBlockSize(fieldset);

        var style = box.Box.Style.Layout.Box;
        var size = style.GetSize(horizontal: !_writingDirection.IsHorizontal);
        if (!size.IsAuto)
        {
            // A percentage that is not resolved behaves as auto (the box is as tall as its contents)
            return !(size.IsLengthPercentage && size.Value.HasPercentage && !double.IsNaN(box.IntrinsicBlockSize)
                && Math.Abs(box.IntrinsicBlockSize - _writingDirection.ToLogical(box.Size).Block) < Epsilon);
        }

        // An item of a grid lanes container whose lanes are rows, shorter than its contents (stretched in a row that does not
        // follow them): as an item of a grid in a fixed row, it does not grow when its contents are pushed by the breaks
        if (box.Parent is BoxFragment { Box.Display.Inside: DisplayInside.GridLanes } lanes && GridLayoutAlgorithm.GetLaneAxis(lanes.Box.Style.Layout) == GridAxis.Rows
            && box.IntrinsicBlockSize > _writingDirection.ToLogical(box.Size).Block + Epsilon)
        {
            return true;
        }

        var maxSize = style.GetMaxSize(horizontal: !_writingDirection.IsHorizontal);
        if (maxSize.IsNone)
            return false;

        var blockSize = _writingDirection.ToLogical(box.Size).Block;
        if (box.IntrinsicBlockSize > blockSize + Epsilon)
            return true;

        // A box that is as tall as its maximum block size does not grow when its contents are pushed by the breaks
        if (maxSize.TryResolve(percentageBasis: null) is not { } max)
            return false;

        var edges = _writingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
        return Math.Abs(max - blockSize) < Epsilon || Math.Abs(max - (blockSize - edges.BlockStart - edges.BlockEnd)) < Epsilon;
    }

    /// <summary>Returns whether a position is inside a box of the flow with <c>break-inside: avoid</c>.</summary>
    /// <param name="position">The position.</param>
    /// <param name="fragmentainerStart">The position of the flow at the block-start edge of the fragmentainer of the break.</param>
    public bool IsInsideAvoidedBox(double position, double fragmentainerStart)
    {
        if (_avoidedBoxes is not null)
        {
            foreach (var (start, end, floatBox, isFloat) in _avoidedBoxes)
            {
                // (not beside a float that is fragmented apart, see GetFloatAvoider: it is broken by the block size of the fragmentainers;
                // in the pages, not beside a float that starts after the start of the fragmentainer: as in Gecko, it is pushed to the
                // next page, see FloatPusher, WPT css/CSS2/pagination/float-page-break-inside-avoid-8-print.html)
                if (position > start + Epsilon && position < end - Epsilon && (floatBox is null || GetFloatMap(floatBox) is null) && !(isFloat && _root._isPaged && start > fragmentainerStart + Epsilon))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the block-end edge of the content of a monolithic box and of its descendants that overflow it (in the block axis of
    /// the strip), or the block-end edge of the box when it clips its overflow.
    /// </summary>
    private double GetOverflowEnd(BoxFragment box, double top, double bottom)
    {
        if (box.IsScrollContainer || (_writingDirection.IsHorizontal ? box.ClipsOverflowY : box.ClipsOverflowX))
            return bottom;

        var end = bottom;
        foreach (var child in box.ChildrenSpan)
        {
            var (childTop, childBottom, _) = GetBlockRange(child, box.Size, _writingDirection, top);
            end = Math.Max(end, child is BoxFragment childBox ? GetOverflowEnd(childBox, childTop, childBottom) : childBottom);
        }

        return end;
    }

    /// <summary>Returns the start of the gap that ends at a position (the end of the content before a break after the gap), else the position.</summary>
    public double GetGapStart(double position)
    {
        if (_gaps is not null)
        {
            foreach (var (start, end) in _gaps)
            {
                if (Math.Abs(end - position) < Epsilon)
                    return start;
            }
        }

        return position;
    }

    /// <summary>The column spanners (https://drafts.csswg.org/css-multicol-1/#column-span) found in the strip, in the order of the flow.</summary>
    public List<ColumnSpannerPlacement> Spanners { get; } = [];

    /// <summary>The block-end edge of the content of the strip (its block size, extended by the boxes and lines that overflow it).</summary>
    public double ContentEnd { get; private set; }

    /// <summary>The block-end edge of the content of the strip and of its parallel flows (which may overflow the boxes of the strip).</summary>
    public double ExtentEnd => Math.Max(ContentEnd, _parallelExtentEnd);

    private double _parallelExtentEnd;

    /// <summary>The block-end edge of the floats of the parallel flows of the strip.</summary>
    private double _parallelFloatEnd;

    /// <summary>
    /// Collects the possible breaks of a strip.
    /// </summary>
    /// <param name="children">The fragments of the strip (their offsets relative to the block-start/inline-start corner of the strip).</param>
    /// <param name="stripSize">The physical size of the strip.</param>
    /// <param name="writingDirection">The writing mode of the fragmentation context.</param>
    /// <param name="spannerBoxes">The column spanners, whose fragments are taken out of the strip, or null.</param>
    /// <param name="container">The box whose content is the strip (the <c>orphans</c> and <c>widows</c> of its line boxes).</param>
    /// <param name="separatesOutOfFlow">Whether the absolutely positioned boxes of the strip are fragmented apart (see <see cref="OutOfFlowItems"/>), else with the content of their containing block.</param>
    /// <param name="pushedFloats">The floats that were pushed to the start of a fragmentainer (see <see cref="FloatPush"/>), or null.</param>
    /// <param name="pageRules">The rules of the page breaks when the fragmentation context is paged, else null (columns).</param>
    public static FragmentationFlow Collect(ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, HashSet<ContainerBox>? spannerBoxes, ContainerBox container, bool separatesOutOfFlow = false, Dictionary<ContainerBox, FloatPush>? pushedFloats = null, PageBreakRules? pageRules = null)
    {
        var outerPageRules = PageBreakRules.Current;
        PageBreakRules.Current = pageRules;
        try
        {
            return CollectCore(children, stripSize, writingDirection, spannerBoxes, container, separatesOutOfFlow, pushedFloats);
        }
        finally
        {
            PageBreakRules.Current = outerPageRules;
        }
    }

    private static FragmentationFlow CollectCore(ReadOnlySpan<Fragment> children, PhysicalSize stripSize, WritingDirection writingDirection, HashSet<ContainerBox>? spannerBoxes, ContainerBox container, bool separatesOutOfFlow, Dictionary<ContainerBox, FloatPush>? pushedFloats)
    {
        var flow = new FragmentationFlow(writingDirection, spannerBoxes)
        {
            _isPaged = PageBreakRules.Current is not null,
            ContentEnd = writingDirection.ToLogical(stripSize).Block,
            _separatesOutOfFlow = separatesOutOfFlow,
            _pushedFloats = pushedFloats,
        };

        var fragmentation = container.Style.Layout.Fragmentation;
        // https://drafts.csswg.org/css-inline-3/#text-box-trim: the text-box-trim of a multi-column container applies to "each
        // column of a multi-column container"
        var textBoxTrim = container.Style.Layout.IsMultiColumnContainer ? container.Style.Layout.Text.TextBoxTrim : TextBoxTrim.None;
        var context = new VisitContext(Avoid: false, CloneStart: 0, CloneEnd: 0, ClipEnd: double.PositiveInfinity)
        {
            LeadingStart = 0,
            LineStartTrim = (textBoxTrim & TextBoxTrim.TrimStart) != TextBoxTrim.None ? FragmentainerTrim.Columns : FragmentainerTrim.None,
            LineEndTrim = (textBoxTrim & TextBoxTrim.TrimEnd) != TextBoxTrim.None ? FragmentainerTrim.Columns : FragmentainerTrim.None,
        };
        var stripBlockSize = writingDirection.ToLogical(stripSize).Block;
        var lastLineEnd = container.ChildrenAreInline && InlineLayoutAlgorithm.IsTextBoxTrimmed(container, start: false) ? stripBlockSize : double.PositiveInfinity;
        flow.ContentEnd = 0;
        flow.VisitChildren(children, children.Length, stripSize, writingDirection, parentTop: 0, context, CountLines(children), Math.Max(1, fragmentation.Orphans), Math.Max(1, fragmentation.Widows), lastLineEnd,
            firstLineIsTrimmed: context.LineStartTrim != FragmentainerTrim.None && container.ChildrenAreInline && InlineLayoutAlgorithm.IsTextBoxTrimmed(container, start: true));

        // The strip is as tall as its content (a block formatting context root contains its floats); the floats of the
        // contents that overflow a box whose block size does not follow them are in a parallel flow, not in the flow of the strip
        if (flow.ContentEnd >= stripBlockSize - Epsilon || flow._parallelFloatEnd < stripBlockSize - Epsilon)
        {
            flow.ContentEnd = Math.Max(flow.ContentEnd, stripBlockSize);
        }

        flow.Sort();
        return flow;
    }

    private void Sort()
    {
        ApplyFragmentainerTrims();
        // (the breaks at the same position in the order of the content)
        Candidates.Sort(static (a, b) => a.Position.CompareTo(b.Position) is var order and not 0 ? order : a.Sequence.CompareTo(b.Sequence));
        if (_floats is not null)
        {
            // The break before a float that starts a fragmentainer is the break of the in-flow content at that position when
            // there is one (with its own rules)
            for (var i = Candidates.Count - 1; i >= 0; i--)
            {
                if (!Candidates[i].IsBeforePushedFloat)
                    continue;

                var position = Candidates[i].Position;
                var isDuplicate = false;
                for (var j = i - 1; j >= 0 && Candidates[j].Position >= position - Epsilon && !isDuplicate; j--)
                {
                    isDuplicate = !Candidates[j].IsBeforePushedFloat && !Candidates[j].IsBeforeFloat && !Candidates[j].IsPastEndOnly;
                }

                for (var j = i + 1; j < Candidates.Count && Candidates[j].Position <= position + Epsilon && !isDuplicate; j++)
                {
                    isDuplicate = !Candidates[j].IsBeforePushedFloat && !Candidates[j].IsBeforeFloat && !Candidates[j].IsPastEndOnly;
                }

                if (isDuplicate)
                {
                    Candidates.RemoveAt(i);
                }
            }
        }

        Ranges.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        Groups?.Sort(static (a, b) => a.Start.CompareTo(b.Start));
    }

    /// <summary>
    /// Returns the forced breaks of a portion of the strip, in the flow and in its parallel flows (the contents of the boxes whose
    /// block size does not follow them, the items of the flex containers...), sorted by position.
    /// </summary>
    public List<BreakCandidate> GetForcedBreaks(double start, double end)
    {
        var result = new List<BreakCandidate>();
        AddForcedBreaks(result, start, end);
        result.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        return result;
    }

    private void AddForcedBreaks(List<BreakCandidate> result, double start, double end)
    {
        foreach (var candidate in Candidates)
        {
            if (candidate.Appeal == BreakAppeal.Forced && candidate.Position > start + Epsilon && candidate.Position < end - Epsilon)
            {
                result.Add(candidate);
            }
        }

        if (Groups is null)
            return;

        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                if (item.Nested is null && item.Start >= start - Epsilon && item.Start < end - Epsilon)
                {
                    item.Flow.AddForcedBreaks(result, start, double.PositiveInfinity);
                }
            }
        }
    }

    /// <summary>
    /// Returns the block size of the tallest unbreakable interval (a line box or a monolithic box) that starts in a portion of the
    /// strip: the smallest block size of a fragmentainer that contains it (a line box without the parts that <c>text-box-trim</c>
    /// trims when it is the first and the last formatted line of a fragmentainer).
    /// </summary>
    public double GetTallestUnbreakable(double start, double end)
    {
        var tallest = 0d;
        var hasTrims = _lineStartTrims is { Count: > 0 } || _lineEndTrims is { Count: > 0 };
        foreach (var (top, bottom) in _unbreakables)
        {
            if (top >= start - Epsilon && top < end - Epsilon)
            {
                tallest = Math.Max(tallest, bottom - top - (hasTrims ? FindTrim(_lineStartTrims, top) + FindTrim(_lineEndTrims, bottom) : 0));
            }
        }

        if (Groups is not null)
        {
            foreach (var group in Groups)
            {
                foreach (var item in group.Items)
                {
                    if (item.Start >= start - Epsilon && item.Start < end - Epsilon)
                    {
                        tallest = Math.Max(tallest, item.Flow.GetTallestUnbreakable(double.NegativeInfinity, double.PositiveInfinity));
                    }
                }
            }
        }

        return tallest;
    }

    /// <summary>Returns the block-end edge of the content of a box fragment of the strip, including its overflowing descendants (at least the end of its border box).</summary>
    public double GetSubtreeEnd(Fragment fragment, double bottom) => _root._subtreeEnds is { } ends && ends.TryGetValue(fragment, out var end) ? Math.Max(end, bottom) : bottom;

    /// <summary>
    /// Returns whether a break at a position is valid: it is not inside an unbreakable interval (a line box or a monolithic
    /// box), else the start of that interval (which may be a valid break), or NaN when the interval starts at or before <paramref name="lowerBound"/>.
    /// </summary>
    public double ClampToValidPosition(double position, double lowerBound)
    {
        EnsureUnbreakablesSorted();
        var adjusted = position;

        // The intervals may nest or overlap: move the position before every interval it is inside of
        for (var changed = true; changed;)
        {
            changed = false;
            var index = FindLastStartingBefore(adjusted);
            for (var i = index; i >= 0; i--)
            {
                var (start, end) = _unbreakables[i];
                if (start < adjusted - Epsilon && end > adjusted + Epsilon)
                {
                    adjusted = start;
                    changed = true;
                    break;
                }
            }
        }

        return adjusted <= lowerBound + Epsilon ? double.NaN : adjusted;
    }

    private int FindLastStartingBefore(double position)
    {
        var low = 0;
        var high = _unbreakables.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (_unbreakables[middle].Start < position)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    private void EnsureUnbreakablesSorted()
    {
        if (_unbreakablesSorted)
            return;

        _unbreakables.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        _maxEnds = new double[_unbreakables.Count];
        var max = double.NegativeInfinity;
        for (var i = 0; i < _unbreakables.Count; i++)
        {
            max = Math.Max(max, _unbreakables[i].End);
            _maxEnds[i] = max;
        }

        _unbreakablesSorted = true;
    }

    private static int CountLines(ReadOnlySpan<Fragment> children)
    {
        var count = 0;
        foreach (var child in children)
        {
            if (child is LineBoxFragment)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns what trims the first and the last formatted lines of a fragmentainer when they are line boxes of a box
    /// (https://drafts.csswg.org/css-inline-3/#text-box-trim): the <c>text-box-trim</c> of the multi-column container applies to
    /// the first and to the last formatted line of each column (https://github.com/w3c/csswg-drafts/issues/5335), in the
    /// columns that are adjacent to the block-start (block-end) edge of the container: not after (before) a column spanner
    /// (https://github.com/w3c/csswg-drafts/issues/11363). As in the browsers (WPT
    /// css/css-inline/text-box-trim/text-box-trim-multicol-004.html), a box that has its own <c>text-box-trim</c> on a side
    /// trims that side in its first (last) fragment only, so its line boxes are not trimmed at the breaks, unless it has
    /// <c>box-decoration-break: clone</c>: they are then trimmed in each of its fragments. As <see cref="InlineLayoutAlgorithm.IsTextBoxTrimmed"/>,
    /// the trimming only goes through the in-flow block containers.
    /// </summary>
    private static (FragmentainerTrim Start, FragmentainerTrim End) GetFragmentainerTrim(BoxFragment box, bool isInFlow, in VisitContext context)
    {
        if (!isInFlow || box.Box.IsOutOfFlow || !box.Box.IsBlockContainer)
            return default;

        var layout = box.Box.Style.Layout;
        var trim = layout.Text.TextBoxTrim;
        if (trim == TextBoxTrim.None)
            return (context.LineStartTrim, context.LineEndTrim);

        var own = layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone ? FragmentainerTrim.Clone : FragmentainerTrim.None;
        return ((trim & TextBoxTrim.TrimStart) != TextBoxTrim.None ? own : context.LineStartTrim, (trim & TextBoxTrim.TrimEnd) != TextBoxTrim.None ? own : context.LineEndTrim);
    }

    /// <summary>
    /// Applies the <c>text-box-trim</c> of the first and last formatted lines of the fragmentainers to the possible breaks of the
    /// flow: a break whose content after it starts with a line box that would be trimmed starts the next fragmentainer after the
    /// trimmed part (<see cref="BreakCandidate.StartTrim"/>), and the content before a break that ends with a line box that would
    /// be trimmed ends before the trimmed part (<see cref="BreakCandidate.EndTrim"/>).
    /// </summary>
    private void ApplyFragmentainerTrims()
    {
        if (_lineStartTrims is null && _lineEndTrims is null)
            return;

        // (the columns before a column spanner are not adjacent to the block-end edge of the multi-column container)
        if (_lineEndTrims is not null && _root.Spanners.Count > 0)
        {
            var lastSpanner = double.NegativeInfinity;
            foreach (var spanner in _root.Spanners)
            {
                lastSpanner = Math.Max(lastSpanner, spanner.MarginBoxStart);
            }

            _lineEndTrims.RemoveAll(trim => trim.IsOfColumns && trim.Position <= lastSpanner + Epsilon);
        }

        _lineStartTrims?.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        _lineEndTrims?.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        for (var i = 0; i < Candidates.Count; i++)
        {
            var candidate = Candidates[i];
            var start = FindTrim(_lineStartTrims, candidate.Position);
            var end = FindTrim(_lineEndTrims, candidate.ContentEnd);
            if (start > 0 || end > 0)
            {
                Candidates[i] = candidate with { StartTrim = start, EndTrim = end, ContentEnd = candidate.ContentEnd - end };
            }
        }
    }

    /// <summary>Returns the trim of the line box whose edge is at a position (in a list sorted by position, see <see cref="ApplyFragmentainerTrims"/>), or 0.</summary>
    private static double FindTrim(List<(double Position, double Trim, bool IsOfColumns)>? trims, double position)
    {
        if (trims is null)
            return 0;

        var (low, high) = (0, trims.Count - 1);
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var difference = trims[middle].Position - position;
            if (Math.Abs(difference) <= Epsilon)
                return trims[middle].Trim;

            if (difference < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns the block-end edge of the content before a break: the end of the unbreakable intervals that start before it (the line
    /// boxes and the monolithic boxes of the parallel flows, e.g. a float beside the content before the break), at least <paramref name="contentEnd"/>.
    /// </summary>
    public double GetContentEndBefore(double position, double contentEnd)
    {
        EnsureUnbreakablesSorted();
        var index = FindLastStartingBefore(position - Epsilon);
        var result = index < 0 ? contentEnd : Math.Max(contentEnd, Math.Min(_maxEnds![index], position));
        if (_floats is not null)
        {
            // The floats that start after the content before the break (in the clearance of the content after the break) are
            // before the break too
            foreach (var extent in _floats)
            {
                if (extent.Start >= contentEnd - Epsilon && extent.Start < position - Epsilon)
                {
                    result = Math.Max(result, Math.Min(extent.End, position));
                }
            }
        }

        return result;
    }

    /// <summary>What trims the first or the last formatted line of a fragmentainer (see <see cref="GetFragmentainerTrim"/>).</summary>
    private enum FragmentainerTrim : byte
    {
        /// <summary>The line is not trimmed.</summary>
        None,

        /// <summary>The <c>text-box-trim</c> of the multi-column container: it applies to the columns that are adjacent to the edge of the container (no column spanner between them).</summary>
        Columns,

        /// <summary>The <c>text-box-trim</c> of a box with <c>box-decoration-break: clone</c>: it applies to each fragment of the box.</summary>
        Clone,
    }

    /// <summary>
    /// Returns the block-end edge of the content before a break (see <see cref="GetContentEndBefore(double, double)"/>): the part
    /// of the last line box that <c>text-box-trim</c> trims at the break (<see cref="BreakCandidate.EndTrim"/>) is not content.
    /// </summary>
    public double GetContentEndBefore(in BreakCandidate candidate)
    {
        var end = GetContentEndBefore(candidate.Position, candidate.ContentEnd);
        return candidate.EndTrim > 0 && end <= candidate.ContentEnd + candidate.EndTrim + Epsilon ? candidate.ContentEnd : end;
    }

    /// <summary>The state of the traversal inherited from the ancestors.</summary>
    /// <param name="Avoid">Whether an ancestor has <c>break-inside: avoid</c>.</param>
    /// <param name="CloneStart">The block-start edges of the ancestors with <c>box-decoration-break: clone</c>.</param>
    /// <param name="CloneEnd">The block-end edges of the ancestors with <c>box-decoration-break: clone</c>.</param>
    /// <param name="ClipEnd">The block-end edge of the nearest ancestor that clips its overflow (https://drafts.csswg.org/css-overflow-3/#valdef-overflow-clip): its overflowing contents do not fragment.</param>
    private readonly record struct VisitContext(bool Avoid, double CloneStart, double CloneEnd, double ClipEnd)
    {
        /// <summary>
        /// Whether the first formatted line of a fragmentainer is trimmed on its block-start side when it is a line box of the
        /// contents that are visited (https://drafts.csswg.org/css-inline-3/#text-box-trim, see <see cref="GetFragmentainerTrim"/>).
        /// </summary>
        public FragmentainerTrim LineStartTrim { get; init; }

        /// <summary>Whether the last formatted line of a fragmentainer is trimmed on its block-end side (see <see cref="LineStartTrim"/>).</summary>
        public FragmentainerTrim LineEndTrim { get; init; }

        /// <summary>The position of the strip from which only the block-start borders and paddings of the ancestors precede the contents that are visited (no content).</summary>
        public double LeadingStart { get; init; }

        /// <summary>The block-start borders and paddings between <see cref="LeadingStart"/> and the contents.</summary>
        public double LeadingEdges { get; init; }

        /// <summary>
        /// The position of the strip from which only the block-start borders and paddings of the ancestors precede the contents
        /// that are visited, whether they are repeated at the breaks or not (see <see cref="BreakCandidate.EdgesOnlyFrom"/>).
        /// </summary>
        public double EdgesOnlyFrom { get; init; }

        /// <summary>The box that establishes the block formatting context of the contents that are visited, or null for the one of the strip.</summary>
        public BoxFragment? FormattingContext { get; init; }

        /// <summary>The repeated header and footer of the nearest table whose sections are visited (their block sizes are in <see cref="CloneStart"/> and <see cref="CloneEnd"/>), or null.</summary>
        public RepeatedSections? Repeated { get; init; }

        /// <summary>The nearest box around the contents that repeats its edges until it ends (see <see cref="ParallelGroup.OwnCloneStart"/>), or null.</summary>
        public ParallelGroup? CloneOwner { get; init; }

        /// <summary>The number of boxes with <c>break-inside: avoid</c> around the contents that are visited (see <see cref="BreakCandidate.AvoidDepth"/>).</summary>
        public int AvoidDepth { get; init; }

        /// <summary>The boxes around the contents that repeat their edges (in <see cref="CloneStart"/> and <see cref="CloneEnd"/>), from the nearest one.</summary>
        public CloneBox? CloneBoxes { get; init; }
    }

    /// <summary>A box that repeats its edges at the breaks inside its border box (<c>box-decoration-break: clone</c>): the block-end edge of its border box and its edges.</summary>
    private sealed record CloneBox(double End, double StartEdges, double EndEdges, CloneBox? Next);

    /// <summary>Returns the logical block range of a child fragment in the strip (without its relative offset).</summary>
    internal static (double Top, double Bottom, double Inline) GetBlockRange(Fragment child, PhysicalSize parentSize, WritingDirection parentWritingDirection, double parentTop)
    {
        var rect = child.Rect;
        if (child is BoxFragment box)
        {
            rect = rect.Translate(-(box.RelativeOffset + box.StickyOffset));
        }

        var logical = parentWritingDirection.ToLogical(rect, parentSize);
        var top = parentTop + logical.Offset.Block;
        return (top, top + logical.Size.Block, logical.Offset.Inline);
    }

    /// <param name="lastLineEnd">
    /// The end of the content box of the parent when its last line box is trimmed by <c>text-box-trim</c> (the trimmed part of the
    /// line overflows the box: it is not content to fragment), else +∞.
    /// </param>
    /// <param name="group">The parallel flows of the parent: its items are not content of the flow.</param>
    /// <param name="breaksInGaps">Whether the space between two siblings is breakable anywhere (the items of a column flex container keep their offsets after a break: their margins are not truncated).</param>
    /// <param name="parent">The box whose children are visited, or null (the strip, a line of a column flex container).</param>
    /// <param name="parentPortion">The portion of the strip (the content between two column spanners) where <paramref name="parent"/> starts.</param>
    /// <param name="firstLineIsTrimmed">Whether the first formatted line of the parent is trimmed on its block-start side by <c>text-box-trim</c> (it is not trimmed again at the start of a fragmentainer).</param>
    private void VisitChildren(ReadOnlySpan<Fragment> children, int inFlowCount, PhysicalSize parentSize, WritingDirection parentWritingDirection, double parentTop, VisitContext context, int lineCount, int orphans, int widows, double lastLineEnd, ParallelGroup? group = null, bool breaksInGaps = false, BoxFragment? parent = null, int parentPortion = 0, RepeatedSections? repeated = null, bool firstLineIsTrimmed = false)
    {
        // The in-flow siblings that follow each other in the block axis: the class A (boxes) and class B (lines) break opportunities
        Fragment? previous = null;
        var previousBottom = 0d;
        var lineIndex = 0;
        var hasContentBefore = false;
        var lines = lineCount > 1 && (orphans > 1 || widows > 1) && !context.Avoid ? new LineBreakInfo(lineCount, orphans, widows) : null;
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            var (top, bottom, _) = GetBlockRange(child, parentSize, parentWritingDirection, parentTop);

            // https://drafts.csswg.org/css-inline-3/#text-box-trim: how much the line box is trimmed when it is the first or the
            // last formatted line of a fragmentainer (the lines that the layout of the strip trimmed are not trimmed again)
            if (child is LineBoxFragment trimmedLine && (context.LineStartTrim != FragmentainerTrim.None || context.LineEndTrim != FragmentainerTrim.None) && bottom - top > Epsilon && i < inFlowCount && top < context.ClipEnd)
            {
                // (the columns after a column spanner are not adjacent to the block-start edge of the multi-column container)
                var (lineStartTrim, lineEndTrim) = InlineLayoutAlgorithm.GetFragmentainerTextBoxTrim(trimmedLine, bottom - top, parentWritingDirection);
                if (lineStartTrim > Epsilon && !(firstLineIsTrimmed && !hasContentBefore)
                    && (context.LineStartTrim == FragmentainerTrim.Clone || (context.LineStartTrim == FragmentainerTrim.Columns && _root.Spanners.Count == 0)))
                {
                    (_lineStartTrims ??= []).Add((top, lineStartTrim, context.LineStartTrim == FragmentainerTrim.Columns));
                }

                if (context.LineEndTrim != FragmentainerTrim.None && lineEndTrim > Epsilon && trimmedLine.BlockEndAnnotationOverflow <= 0 && !(lineIndex == lineCount - 1 && double.IsFinite(lastLineEnd)))
                {
                    (_lineEndTrims ??= []).Add((bottom, Math.Min(lineEndTrim, bottom - top), context.LineEndTrim == FragmentainerTrim.Columns));
                }
            }

            if (child is LineBoxFragment { BlockEndAnnotationOverflow: > 0 } annotatedLine)
            {
                // The ruby annotations under the line are content of the line for the fragmentation
                bottom += annotatedLine.BlockEndAnnotationOverflow;
            }

            if (child is LineBoxFragment && lineIndex == lineCount - 1)
            {
                bottom = Math.Max(top, Math.Min(bottom, lastLineEnd));
            }

            // The absolutely positioned boxes are fragmented apart (they are not content of the flow)
            if (i >= inFlowCount && parent is not null && child is BoxFragment outOfFlow && _root._separatesOutOfFlow && top < context.ClipEnd)
            {
                AddOutOfFlow(outOfFlow, top, bottom, context, parent, parentTop, parentPortion);
                continue;
            }

            // (also a box of the strip, whose containing block is an inline box of the strip, when its static position is in a
            // nested multi-column container: it is placed from that position once the columns of the container are)
            if (parent is null && _separatesOutOfFlow && _nestedStaticPositions is { } nestedStaticPositions && child is BoxFragment { Box.IsOutOfFlow: true } stripBox
                && nestedStaticPositions.ContainsKey(stripBox.Box) && PositionedLayout.IsAbsolutelyPositioned(stripBox.Box) && top < context.ClipEnd
                && stripBox.Box.Style.Layout.Insets.Inset is var stripInsets && stripInsets.Get(_writingDirection.BlockStart).IsAuto && stripInsets.Get(_writingDirection.BlockEnd).IsAuto)
            {
                AddOutOfFlow(stripBox, top, bottom, context, stripBox, top, _root.Spanners.Count, isStripChild: true);
                continue;
            }

            // (the absolutely positioned boxes whose containing block is the initial containing block of the pages)
            if (parent is null && PageBreakRules.Current?.InitialContainingBlock is { } initialContainingBlock && child is BoxFragment { Box.IsOutOfFlow: true } pageBox
                && PositionedLayout.IsAbsolutelyPositioned(pageBox.Box) && top < context.ClipEnd)
            {
                AddOutOfFlow(pageBox, top, bottom, context, initialContainingBlock, 0, _root.Spanners.Count, isPlacedFromStart: true);
                continue;
            }

            var clippedBottom = Math.Min(bottom, context.ClipEnd);
            ContentEnd = Math.Max(ContentEnd, clippedBottom);
            if (group is not null && group.ItemsByFragment.ContainsKey(child))
                continue;

            // The contents of the child follow content of the box, or only the block-start edges of the ancestors
            var childContext = hasContentBefore ? context with { LeadingStart = top, LeadingEdges = 0, EdgesOnlyFrom = top } : context;
            var breakContext = context;
            var isAfterRepeatedHeader = false;
            if (repeated is not null)
            {
                // https://drafts.csswg.org/css-tables-3/#repeated-headers: the breaks in the sections of a table whose header and
                // footer are repeated leave room for them; the header and the footer are not separated from the other sections
                var withRepeated = context with { CloneStart = context.CloneStart + repeated.HeaderSize, CloneEnd = context.CloneEnd + repeated.FooterSize, Repeated = repeated };
                if (!ReferenceEquals(child, repeated.Header) && !ReferenceEquals(child, repeated.Footer))
                {
                    childContext = childContext with { CloneStart = withRepeated.CloneStart, CloneEnd = withRepeated.CloneEnd, Repeated = repeated };
                }

                breakContext = ReferenceEquals(child, repeated.Footer) ? context with { Avoid = true } : withRepeated;

                // (no break between the header and the section after it: the header would be repeated before the same content)
                isAfterRepeatedHeader = previous is not null && ReferenceEquals(previous, repeated.Header);
            }

            if (i < inFlowCount && child is BoxFragment { Box.IsOutOfFlow: true } floatBox && !PositionedLayout.IsAbsolutelyPositioned(floatBox.Box) && top < context.ClipEnd)
            {
                AddFloat(floatBox, top, bottom, previous is null ? double.NaN : previousBottom, context, hasContentBefore);
            }

            if (child is BoxFragment { Box: var spannerBox } spanner && _spannerBoxes is not null && _spannerBoxes.Contains(spannerBox))
            {
                var margin = _writingDirection.ToLogical(spanner.Margin);
                Spanners.Add(new ColumnSpannerPlacement(spanner, top - margin.BlockStart, bottom + margin.BlockEnd));

                // The spanner is placed below the content that overflows the boxes before it (a parallel flow): the flow has no
                // content there, and breaks anywhere
                if (previous is not null && top - margin.BlockStart > previousBottom + Epsilon)
                {
                    AddCandidate(new BreakCandidate(previousBottom, previousBottom, context.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = top - margin.BlockStart, IncludesRangeStart = true }, context);
                }

                previous = null;
                continue;
            }

            // The floats, the absolutely positioned boxes and the outside markers are parallel flows: no break between them and their siblings
            var isInFlow = i < inFlowCount && child is not BoxFragment { Box.IsOutOfFlow: true } and not BoxFragment { Box.IsOutsideMarker: true };
            var siblingBefore = previous is not null && top >= previousBottom - Epsilon ? previous : null;

            // An anonymous box without block size (the empty line boxes around a block-level box in an inline box) is not
            // content: there is no break opportunity before and after it (as in the browsers, which create no line box)
            var isPhantom = child is BoxFragment { Box.IsAnonymous: true } && bottom - top <= Epsilon;
            if (isInFlow && previous is null && !isPhantom && !breaksInGaps && child is BoxFragment firstBox && bottom - top > Epsilon && top < context.ClipEnd
                && _writingDirection.ToLogical(firstBox.Margin).BlockStart is var leadingMargin and > Epsilon)
            {
                // The first content of a box is not at a possible break, but as in Blink, a box that starts after the end of
                // the fragmentainer (its block-start margin does not fit) is pushed to the next one: the margin is truncated
                AddCandidate(new BreakCandidate(top, top - leadingMargin, BreakAppeal.LastResort) { IsBetweenMargins = true, IsPastEndOnly = true, BoxEnd = bottom }, breakContext);
            }

            if (isInFlow)
            {
                if (child is LineBoxFragment)
                {
                    // https://drafts.csswg.org/css-break-3/#widows-orphans: the number of lines before and after the break in the
                    // block container (the breaker counts the lines before the break in the fragmentainer, see LineBreakInfo)
                    if (lines is not null && lineIndex < lines.Tops.Length)
                    {
                        lines.Tops[lineIndex] = top;
                    }

                    if (lineIndex > 0 && previous is LineBoxFragment && top >= previousBottom - Epsilon && top < context.ClipEnd)
                    {
                        var appeal = context.Avoid ? BreakAppeal.Avoid : lineIndex < orphans || lineCount - lineIndex < widows ? BreakAppeal.OrphansWidows : BreakAppeal.Perfect;
                        AddCandidate(new BreakCandidate(top, previousBottom, appeal) { Lines = lines, LineIndex = lineIndex }, context);
                    }

                    lineIndex++;
                }
                else if (!isPhantom && previous is not null && top >= previousBottom - Epsilon && top < context.ClipEnd && !isAfterRepeatedHeader)
                {
                    AddClearanceGap(previous, child, previousBottom, top);
                    if (breaksInGaps && top > previousBottom + Epsilon)
                    {
                        AddBreakInGap(previous, child, top, previousBottom, breakContext);
                    }
                    else
                    {
                        AddBreakBetween(previous, child, top, previousBottom, breakContext);
                    }
                }

                else if (breaksInGaps && !isPhantom && previous is not null && top < previousBottom - Epsilon && previousBottom < context.ClipEnd
                    && (IsForcedBreak(GetBreakAfter(previous)) || IsForcedBreak(GetBreakBefore(child))))
                {
                    // A forced break before an item of a column flex container that overlaps the previous one (a negative
                    // margin): as in Blink, the item starts the next fragmentainer at the offset its margin gives it (the
                    // margins after a forced break are preserved): it is placed from the end of the previous item
                    AddCandidate(new BreakCandidate(previousBottom, previousBottom, BreakAppeal.Forced), breakContext);
                    (_root._forcedStarts ??= [])[child] = previousBottom;

                    // (the item is content after the break, also when it ends before the position of the break)
                    ContentEnd = Math.Max(ContentEnd, previousBottom + (3 * Epsilon));
                }
                else if (breaksInGaps && !isPhantom && previous is null && parent is null && child is BoxFragment firstItem
                    && Math.Max(0, _writingDirection.ToLogical(firstItem.Margin).BlockStart) is var firstMargin and > Epsilon && top < context.ClipEnd)
                {
                    // The block-start margin of the first item of a line of a column flex container breaks anywhere, as the
                    // margins of the other items; as in Blink, the item is separated from the start of the line by its margin:
                    // a forced break before the item is taken, and the margin is preserved after it
                    if (IsForcedBreak(GetBreakBefore(child)))
                    {
                        AddCandidate(new BreakCandidate(top - firstMargin, top - firstMargin, BreakAppeal.Forced), breakContext);
                    }

                    AddCandidate(new BreakCandidate(top - firstMargin, top - firstMargin, breakContext.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = top }, breakContext);
                }

                if (!isPhantom)
                {
                    // (an item placed after a forced break before the end of the previous one: the next break is after both)
                    previousBottom = _root._forcedStarts is { } forcedStarts && forcedStarts.ContainsKey(child) ? Math.Max(bottom, previousBottom) : bottom;
                    previous = child;
                    hasContentBefore |= bottom - top > Epsilon;
                }

                if (child is BoxFragment && bottom - top <= Epsilon)
                {
                    (_root._emptyOrdinals ??= [])[child] = ++_root._sequence;
                }
            }

            if (isInFlow && _root._pushedFloats is not null && child is BoxFragment avoiderBox && bottom - top > Epsilon && !IsMonolithic(avoiderBox)
                && BlockFormattingContext.EstablishesNewFormattingContext(avoiderBox.Box, parentWritingDirection))
            {
                (_avoiders ??= []).Add(new FloatAvoider(avoiderBox, top, bottom, context.FormattingContext));
            }

            if (child is BoxFragment box && !IsMonolithic(box))
            {
                if (!isInFlow || !TryAddDefiniteBox(box, top, bottom, childContext))
                {
                    VisitBox(box, top, bottom, childContext, isInFlow);
                }
            }
            else if (!isInFlow || child is not BoxFragment nestedBox || !TryAddNestedColumns(nestedBox, top, bottom, context, siblingBefore))
            {
                AddUnbreakable(top, clippedBottom);

                // As in the browsers, the content that overflows a monolithic box adds pages (the pages slice it, see
                // SlicesMonolithicContent): the flow continues until its end, without possible break
                if (_root._isPaged && child is BoxFragment monolithic && GetOverflowEnd(monolithic, top, bottom) is var overflowEnd && overflowEnd > bottom + Epsilon)
                {
                    overflowEnd = Math.Min(overflowEnd, context.ClipEnd);
                    ContentEnd = Math.Max(ContentEnd, overflowEnd);
                    (_root._subtreeEnds ??= [])[monolithic] = overflowEnd;
                }
            }
        }
    }

    /// <summary>
    /// Adds the contents of an in-flow box whose block size does not follow them (a specified or limited block size) as a parallel
    /// flow (https://drafts.csswg.org/css-break-3/#parallel-flows: "content overflowing the content edge of a fixed-size box is
    /// considered parallel to the content after the fixed-size box"): the block-end edge of the box is placed by this flow (the box
    /// uses the block size of the fragmentainers it is broken by, whatever the breaks of its contents), and its contents have their
    /// own possible breaks, are broken at the end of each fragmentainer, and continue after the end of the box. Returns false
    /// when the box is fragmented with the flow.
    /// </summary>
    private bool TryAddDefiniteBox(BoxFragment box, double top, double bottom, VisitContext context)
    {
        var layout = box.Box.Style.Layout;
        var edges = _writingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
        var clones = layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone && (edges.BlockStart > Epsilon || edges.BlockEnd > Epsilon);
        if (box.Box.Kind != ContainerBoxKind.Normal || box.Box.Display.Internal != DisplayInternal.None || (GetFirstInFlowChild(box) is null && !clones)
            || (layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone && !clones) || !HasDefiniteBlockSize(box)
            || (_spannerBoxes is not null && ContainsSpanner(box, _spannerBoxes)))
        {
            return false;
        }

        // https://drafts.csswg.org/css-break-3/#break-decoration: the edges that the box repeats at its breaks (as in Blink, they
        // are added to a block size of the content box, and taken from a block size of the border box)
        var group = new ParallelGroup(box, [top], [bottom])
        {
            Avoid = context.Avoid,
            AvoidDepth = context.AvoidDepth,
            CloneStart = context.CloneStart,
            CloneEnd = context.CloneEnd,
            Repeated = context.Repeated,
            CloneOwner = context.CloneOwner,
            CanGrow = false,
            OwnCloneStart = clones ? edges.BlockStart : 0,
            OwnCloneEnd = clones ? edges.BlockEnd : 0,
            ShrinksAtBreaks = clones && layout.BoxSizing == BoxSizing.BorderBox,
            BlockEndEdges = clones ? 0 : edges.BlockEnd,
        };

        var item = CreateContentItem(box, top, bottom, clones ? context with { CloneOwner = group } : context);
        group.Add(item, box);
        group.Complete();
        _parallelExtentEnd = Math.Max(_parallelExtentEnd, Math.Max(bottom, item.Flow.ExtentEnd));
        (Groups ??= []).Add(group);
        (_root._definiteItems ??= [])[box] = item;
        AddGroupAncestors(box);
        return true;
    }

    private void AddGroupAncestors(BoxFragment box)
    {
        for (Fragment? ancestor = box; ancestor is BoxFragment ancestorBox; ancestor = ancestor.Parent)
        {
            if (!(_root._groupAncestors ??= []).Add(ancestorBox))
                break;
        }
    }

    /// <summary>
    /// Adds an absolutely positioned box whose containing block is in the strip as a flow of its own (see <see cref="OutOfFlowItems"/>).
    /// </summary>
    /// <param name="container">The fragment of the containing block (the parent of the fragment of the box).</param>
    /// <param name="containerTop">The block-start edge of the border box of the containing block in the strip.</param>
    /// <param name="containerPortion">The portion of the strip (the content between two column spanners) where the containing block starts.</param>
    /// <param name="isStripChild">Whether the box is a child of the strip (its containing block is an inline box of the strip): <paramref name="container"/> is the box itself.</param>
    /// <param name="isPlacedFromStart">Whether the box is placed from its position in the strip, from the block-start edge of its containing block (a box of the initial containing block of the pages, laid out in it).</param>
    private void AddOutOfFlow(BoxFragment box, double top, double bottom, VisitContext context, BoxFragment container, double containerTop, int containerPortion, bool isStripChild = false, bool isPlacedFromStart = false)
    {
        // (the box is added before its contents: the boxes are placed in the order of the content, a containing block first)
        var items = _root._outOfFlowItems ??= [];
        var index = items.Count;
        items.Add(null!);
        // (as in Blink, an absolutely positioned box is not placed between the edges that the boxes around it repeat)
        context = context with { CloneStart = 0, CloneEnd = 0, Repeated = null, CloneOwner = null };
        ParallelItem item;
        if (IsMonolithic(box))
        {
            var flow = new FragmentationFlow(_writingDirection, _spannerBoxes, _root) { ContentEnd = Math.Min(bottom, context.ClipEnd) };
            flow.AddUnbreakable(top, Math.Min(bottom, context.ClipEnd));

            // (the content that overflows a monolithic box adds pages, see VisitChildren)
            if (_root._isPaged && GetOverflowEnd(box, top, bottom) is var overflowEnd && overflowEnd > bottom + Epsilon)
            {
                flow.ContentEnd = Math.Max(flow.ContentEnd, Math.Min(overflowEnd, context.ClipEnd));
                (_root._subtreeEnds ??= [])[box] = Math.Min(overflowEnd, context.ClipEnd);
            }

            item = new ParallelItem(flow) { Box = box, Start = top, End = bottom, IntrinsicEnd = bottom, IsMonolithic = true, CountsForTrack = true, IsDefiniteBox = true };
        }
        else
        {
            item = CreateContentItem(box, top, bottom, context);
        }

        // https://drafts.csswg.org/css-position-3/#abspos-insets: a box placed from the block-end edge of its containing block,
        // by its inset or by the alignment of its static position in a flex or grid container
        var insets = box.Box.Style.Layout.Insets.Inset;
        var containerEnd = containerTop + _writingDirection.ToLogical(container.Size).Block;
        var isStatic = insets.Get(_writingDirection.BlockStart).IsAuto && insets.Get(_writingDirection.BlockEnd).IsAuto;
        var isInlineStatic = insets.Get(_writingDirection.InlineStart).IsAuto && insets.Get(_writingDirection.InlineEnd).IsAuto;
        var endRatio = !isPlacedFromStart && insets.Get(_writingDirection.BlockStart).IsAuto && !insets.Get(_writingDirection.BlockEnd).IsAuto ? 1d : 0;
        if (isPlacedFromStart)
        {
            isStatic = false;
        }

        var containerState = isStripChild ? null : container.Box.LayoutState;
        if (isStatic && containerState is FlexContainerState or GridContainerState && ReferenceEquals(box.Box.Parent, container.Box))
        {
            // The static position of a child of a flex or grid container is aligned in the container: the share of the free
            // space that is before the box
            var containerEdges = _writingDirection.ToLogical(container.Border.Add(container.Padding).Add(container.Scrollbars));
            var margin = _writingDirection.ToLogical(box.Margin);
            var before = top - margin.BlockStart - (containerTop + containerEdges.BlockStart);
            var free = before + (containerEnd - containerEdges.BlockEnd - (bottom + margin.BlockEnd));
            if (before > Epsilon && free > Epsilon && before <= free + Epsilon)
            {
                endRatio = before / free;
            }
        }

        item.Flow.OwnerItem = item;
        item.OutOfFlow = new OutOfFlowPlacement
        {
            OwnerFlow = this,
            Container = container,
            ContainerStart = containerTop,
            ContainerEnd = containerEnd,
            ContainerPortion = containerPortion,
            ContainerEndPortion = _root.Spanners.Count,
            IsContainerDefinite = !isStripChild && HasDefiniteBlockSize(container),
            IsDefinite = HasDefiniteBlockSize(box),
            EndRatio = endRatio,

            // The static position, and the grid area of a child of a grid container, are positions of the content of the
            // containing block: they follow its breaks
            IsPlacedInContent = endRatio == 0 && (isStatic || containerState is GridContainerState),
            StaticMargin = endRatio == 0 && isStatic && containerState is not (GridContainerState or FlexContainerState) ? _writingDirection.ToLogical(box.Margin).BlockStart : 0,
            IsStatic = endRatio == 0 && isStatic && containerState is not (GridContainerState or FlexContainerState),
            IsStretched = !isPlacedFromStart && !insets.Get(_writingDirection.BlockStart).IsAuto && !insets.Get(_writingDirection.BlockEnd).IsAuto && box.Box.Style.Layout.Box.GetSize(horizontal: !_writingDirection.IsHorizontal).IsAuto,
            IsInlineStatic = isInlineStatic && containerState is not (GridContainerState or FlexContainerState),
            ClipEnd = context.ClipEnd,
        };

        items[index] = item;
        (_root._outOfFlowByFragment ??= [])[box] = item;
        AddGroupAncestors(box);
    }

    /// <summary>
    /// Creates the parallel flow of the contents of a box whose block-end edge does not follow them (a box with a specified or
    /// limited block size, an absolutely positioned box): the contents, and the empty block size of the box after them.
    /// </summary>
    private ParallelItem CreateContentItem(BoxFragment box, double top, double bottom, VisitContext context)
    {
        var layout = box.Box.Style.Layout;
        var flow = new FragmentationFlow(_writingDirection, _spannerBoxes, _root);
        var avoid = context.Avoid || layout.Fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn;
        var item = new ParallelItem(flow)
        {
            Box = box,
            Start = top,
            End = bottom,
            IntrinsicEnd = bottom,
            CountsForTrack = true,
            IsDefiniteBox = true,
            Avoid = avoid,
            LeadingMonolithicEnd = GetLeadingMonolithicEnd(box, top),
        };

        // (the flow is known as the one of the item while it is visited: the absolutely positioned boxes of its contents are placed with its breaks)
        flow.OwnerItem = item;
        flow.VisitBox(box, top, bottom, context, isInFlow: true, isFlowRoot: true);
        flow.ContentEnd = Math.Min(context.ClipEnd, Math.Max(flow.ContentEnd, top));
        if (box.Box.LayoutState is GridContainerState grid && box.Box.Display.Inside == DisplayInside.Grid && grid.GetRows() is [.., var lastRow])
        {
            // (the empty rows of a grid after its items are content: their gutters disappear at the breaks)
            flow.ContentEnd = Math.Min(context.ClipEnd, Math.Max(flow.ContentEnd, top + lastRow.End));
        }

        // The empty block size of the box after its in-flow contents breaks anywhere
        var edges = _writingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
        var contentStart = top + edges.BlockStart;
        var contentEnd = Math.Min(bottom - edges.BlockEnd, context.ClipEnd);
        var lastInFlowEnd = contentStart;
        var children = box.ChildrenSpan;
        for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
        {
            if (children[i] is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true })
                continue;

            lastInFlowEnd = Math.Max(lastInFlowEnd, GetBlockRange(children[i], box.Size, box.WritingDirection, top).Bottom);
        }

        if (contentEnd - lastInFlowEnd > Epsilon)
        {
            // (with the edges that the box repeats at its breaks)
            var rangeContext = layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone
                ? context with { CloneStart = context.CloneStart + edges.BlockStart, CloneEnd = context.CloneEnd + edges.BlockEnd }
                : context;
            flow.AddCandidate(new BreakCandidate(lastInFlowEnd, lastInFlowEnd, avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = contentEnd, IncludesRangeStart = lastInFlowEnd > contentStart + Epsilon }, rangeContext);
        }

        flow.Sort();
        return item;
    }

    /// <summary>
    /// Returns the block-end edge of the first content of a box when it is monolithic (a line box, a monolithic box) and no
    /// content precedes it in the box, else NaN.
    /// </summary>
    private double GetLeadingMonolithicEnd(BoxFragment box, double top)
    {
        while (GetFirstInFlowChild(box) is { } first)
        {
            var (firstTop, firstBottom, _) = GetBlockRange(first, box.Size, box.WritingDirection, top);
            if (first is not BoxFragment firstBox || IsMonolithic(firstBox))
                return firstBottom;

            if (firstBox.Box.Kind != ContainerBoxKind.Normal || firstBox.Box.Display.Internal != DisplayInternal.None || HasDefiniteBlockSize(firstBox) || ParallelStructure.TryCreate(firstBox) is not null)
                break;

            box = firstBox;
            top = firstTop;
        }

        return double.NaN;
    }

    /// <summary>Returns whether the in-flow descendants of a fragment include a column spanner.</summary>
    private static bool ContainsSpanner(BoxFragment fragment, HashSet<ContainerBox> spannerBoxes)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is BoxFragment childBox && (spannerBoxes.Contains(childBox.Box) || ContainsSpanner(childBox, spannerBoxes)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds a multi-column container of the flow as a flow of its own (https://drafts.csswg.org/css-multicol-1/#nested-multicol):
    /// its rows of columns and its column spanners are placed in the fragmentainers by the breaker (see <see cref="NestedColumns"/>).
    /// Returns false when the container is monolithic (it is not in a strip, or it is monolithic for another reason).
    /// </summary>
    /// <param name="siblingBefore">The in-flow sibling before the container when the flow has a possible break between them, else null.</param>
    private bool TryAddNestedColumns(BoxFragment box, double top, double bottom, VisitContext context, Fragment? siblingBefore)
    {
        if (TakeNestedColumns(box, context) is not { } nested)
            return false;

        // (the breaker places the block-end edge of a container with a definite block size after its content box)
        var canGrow = !HasDefiniteBlockSize(box) || nested.BlockSize is not null;
        var group = new ParallelGroup(box, [top], [bottom])
        {
            Avoid = context.Avoid || box.Box.Style.Layout.Fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn,
            AvoidDepth = context.AvoidDepth,
            CloneStart = context.CloneStart,
            CloneEnd = context.CloneEnd,
            Repeated = context.Repeated,
            CloneOwner = context.CloneOwner,
            CanGrow = canGrow,
        };

        var item = new ParallelItem(CreateNestedColumnsFlow(nested, top, bottom))
        {
            Box = box,
            Nested = nested,
            Start = top,
            End = bottom,
            IntrinsicEnd = bottom,
            CanGrow = canGrow,
            CountsForTrack = true,
            Avoid = group.Avoid,

            // (a container that is taller than its contents has a minimum block size)
            HasMinimumEnd = canGrow && nested.BlockSize is null && !double.IsNaN(box.IntrinsicBlockSize) && box.IntrinsicBlockSize < bottom - top - Epsilon,

            // The appeal of a break before the container, instead of a break in its first row
            BreakBeforeAppeal = siblingBefore is null || context.Avoid || IsAvoidBreak(GetBreakAfter(siblingBefore)) || IsAvoidBreak(GetBreakBefore(box)) ? BreakAppeal.Avoid : BreakAppeal.Perfect,
        };

        group.Add(item, box);
        group.Complete();
        _parallelExtentEnd = Math.Max(_parallelExtentEnd, bottom);
        (Groups ??= []).Add(group);
        (_root._groupsByContainer ??= [])[box] = group;
        (_root._nestedItems ??= [])[box] = item;
        AddNestedOutOfFlow(box, item, top, context);
        for (Fragment? ancestor = box; ancestor is BoxFragment ancestorBox; ancestor = ancestor.Parent)
        {
            if (!(_root._groupAncestors ??= []).Add(ancestorBox))
                break;
        }

        return true;
    }

    /// <summary>
    /// Adds the absolutely positioned boxes of a nested multi-column container that the outer fragmentation context places:
    /// the children of its fragment (their containing block is the container) are flows of their own, as the ones of the other
    /// boxes of the strip are (see <see cref="OutOfFlowItems"/>), and the boxes whose static position is in its strip are
    /// recorded (see <see cref="GetNestedStaticPosition"/>).
    /// </summary>
    private void AddNestedOutOfFlow(BoxFragment box, ParallelItem item, double top, VisitContext context)
    {
        if (item.Nested!.OutOfFlow is { } candidates)
        {
            var positions = _root._nestedStaticPositions ??= [];
            for (var i = 0; i < candidates.Count; i++)
            {
                positions[candidates[i].Candidate.Box] = (item, i);
            }
        }

        if (!_root._separatesOutOfFlow)
            return;

        var children = box.ChildrenSpan;
        for (var i = box.InFlowChildCount; i < children.Length; i++)
        {
            if (children[i] is not BoxFragment child)
                continue;

            var (childTop, childBottom, _) = GetBlockRange(child, box.Size, box.WritingDirection, top);
            if (childTop < context.ClipEnd)
            {
                AddOutOfFlow(child, childTop, childBottom, context, box, top, _root.Spanners.Count);
            }
        }
    }

    /// <summary>
    /// Returns the content of a multi-column container of the flow that the breaker fragments (see <see cref="NestedColumns"/>), or
    /// null when the container is monolithic (it was not laid out in a strip, or it is monolithic for another reason).
    /// </summary>
    private NestedColumns? TakeNestedColumns(BoxFragment box, VisitContext context)
    {
        var container = box.Box;
        var layout = container.Style.Layout;
        if (container.Kind != ContainerBoxKind.Normal || !layout.IsMultiColumnContainer || container.LayoutState is not MultiColumnState state
            || box.IsScrollContainer || container.IsAtomicInline || layout.WritingMode != _writingDirection.WritingMode
            || !double.IsPositiveInfinity(context.ClipEnd) || (_writingDirection.IsHorizontal ? box.ClipsOverflowY : box.ClipsOverflowX)
            || (IntrinsicSizing.HasSizeContainment(container, inlineAxis: false) && UsedContainment.CanApplySizeContainment(container)))
        {
            return null;
        }

        return state.Take();
    }

    /// <summary>The flow of the parallel item of a nested multi-column container: the columns are at least as tall as the tallest unbreakable content of the container.</summary>
    private FragmentationFlow CreateNestedColumnsFlow(NestedColumns nested, double top, double bottom)
    {
        var flow = new FragmentationFlow(_writingDirection, _spannerBoxes, _root) { ContentEnd = bottom };
        flow.AddUnbreakable(top, Math.Min(bottom, top + nested.Flow.GetTallestUnbreakable(double.NegativeInfinity, double.PositiveInfinity)));
        return flow;
    }

    /// <summary>
    /// Collects the possible breaks of a box that is fragmented apart from the strip it was laid out in (a column spanner of a nested
    /// multi-column container): the positions are relative to the block-start edge of its border box.
    /// </summary>
    public static FragmentationFlow CollectBox(BoxFragment box, WritingDirection writingDirection)
    {
        // (the box is in the columns of a nested multi-column container)
        var outerPageRules = PageBreakRules.Current;
        PageBreakRules.Current = null;
        try
        {
            return CollectBoxCore(box, writingDirection);
        }
        finally
        {
            PageBreakRules.Current = outerPageRules;
        }
    }

    private static FragmentationFlow CollectBoxCore(BoxFragment box, WritingDirection writingDirection)
    {
        var blockSize = writingDirection.ToLogical(box.Size).Block;
        var flow = new FragmentationFlow(writingDirection, spannerBoxes: null) { ContentEnd = blockSize };
        if (flow.IsMonolithic(box))
        {
            flow.AddUnbreakable(0, blockSize);
        }
        else
        {
            flow.VisitBox(box, 0, blockSize, new VisitContext(Avoid: false, CloneStart: 0, CloneEnd: 0, ClipEnd: double.PositiveInfinity), isInFlow: true);
            flow.ContentEnd = Math.Max(flow.ContentEnd, blockSize);
        }

        flow.Sort();
        return flow;
    }

    /// <param name="isFlowRoot">Whether the box is the root of a parallel flow: its block-end edge is placed by the flow of its container.</param>
    private void VisitBox(BoxFragment box, double top, double bottom, VisitContext context, bool isInFlow, bool isFlowRoot = false)
    {
        var layout = box.Box.Style.Layout;
        var fragmentation = layout.Fragmentation;
        var edges = _writingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
        var childContext = context;
        if (fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn)
        {
            // (the table grid box has the style of its table wrapper box: they are one box with break-inside: avoid)
            childContext = childContext with { Avoid = true, AvoidDepth = context.AvoidDepth + (box.Box.Kind == ContainerBoxKind.TableGrid && box.Parent is BoxFragment { Box.Kind: ContainerBoxKind.TableWrapper } ? 0 : 1) };
            if (!context.Avoid && bottom - top > Epsilon)
            {
                (_avoidedBoxes ??= []).Add((top, bottom, !isInFlow && _root._pushedFloats is { } pushedFloats && pushedFloats.TryGetValue(box.Box, out var floatPush) && !floatPush.IsDeferred ? box : null, !isInFlow && layout.IsFloating));
            }
        }

        if (fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone)
        {
            childContext = childContext with
            {
                CloneStart = childContext.CloneStart + edges.BlockStart,
                CloneEnd = childContext.CloneEnd + edges.BlockEnd,
                CloneBoxes = new CloneBox(bottom, edges.BlockStart, edges.BlockEnd, childContext.CloneBoxes),
            };
        }

        if (box.Box.IsOutOfFlow || (_root._pushedFloats is not null && BlockFormattingContext.EstablishesNewFormattingContext(box.Box, box.Box.Parent is { } parentBox ? WritingDirection.FromStyle(parentBox.Style.Layout) : _writingDirection)))
        {
            childContext = childContext with { FormattingContext = box };
        }

        // (the edges repeated in each fragment are not content before the first content of the box; the other ones are)
        if (fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone)
        {
            childContext = childContext with { LeadingEdges = childContext.LeadingEdges + edges.BlockStart };
        }
        else if (edges.BlockStart > Epsilon)
        {
            childContext = childContext with { LeadingStart = top + edges.BlockStart, LeadingEdges = 0 };
        }

        // https://drafts.csswg.org/css-overflow-3/#valdef-overflow-clip: the contents that overflow a box that clips them are not
        // fragmented (they stay in its last fragment)
        if (_writingDirection.IsHorizontal ? box.ClipsOverflowY : box.ClipsOverflowX)
        {
            childContext = childContext with { ClipEnd = Math.Min(childContext.ClipEnd, bottom) };
        }

        var children = box.ChildrenSpan;
        var before = ContentEnd;
        ContentEnd = 0;
        var startSequence = ++_root._sequence;
        var portion = _root.Spanners.Count;
        var lastLineEnd = box.Box.ChildrenAreInline && InlineLayoutAlgorithm.IsTextBoxTrimmed(box.Box, start: false) ? bottom - edges.BlockEnd : double.PositiveInfinity;
        var group = CreateGroup(box, top, childContext);
        var isColumnFlex = box.Box.LayoutState is FlexContainerState { IsRow: false } && box.Box.Display.Inside is DisplayInside.Flex or DisplayInside.WebkitBox;
        if (isColumnFlex && group is null && box.Box.LayoutState is FlexContainerState { LineCount: 1 })
        {
            // The items in the order of the block axis (column-reverse, order)
            children = SortInFlowChildren(box);
        }

        RepeatedSections? repeated = null;
        if (box.Box.Kind == ContainerBoxKind.TableGrid && FindRepeatedSections(box, top) is { } sections)
        {
            repeated = sections;
            (_root._repeatedSections ??= [])[box] = sections;
            (_repeatedTables ??= []).Add((top, bottom, sections));
        }

        var (trimsLineStart, trimsLineEnd) = GetFragmentainerTrim(box, isInFlow, context);
        childContext = childContext with { LineStartTrim = trimsLineStart, LineEndTrim = trimsLineEnd };
        VisitChildren(children, box.InFlowChildCount, box.Size, box.WritingDirection, top, childContext, box.Box.ChildrenAreInline ? CountLines(children) : 0, Math.Max(1, fragmentation.Orphans), Math.Max(1, fragmentation.Widows), lastLineEnd, group, isColumnFlex, box, portion, repeated,
            firstLineIsTrimmed: trimsLineStart != FragmentainerTrim.None && box.Box.ChildrenAreInline && InlineLayoutAlgorithm.IsTextBoxTrimmed(box.Box, start: true));
        var subtreeEnd = ContentEnd;
        ContentEnd = Math.Max(before, subtreeEnd);
        if (subtreeEnd > bottom + Epsilon)
        {
            (_root._subtreeEnds ??= [])[box] = subtreeEnd;
        }

        if (isFlowRoot)
        {
            // The contents of the parallel flow (the block-end edge of the box is placed by the flow of its container)
            ContentEnd = subtreeEnd;
        }

        // The gutters between the rows of a grid container that no item spans across
        if (box.Box.LayoutState is GridContainerState grid && box.Box.Display.Inside == DisplayInside.Grid)
        {
            foreach (var (gapStart, gapEnd) in grid.GetRowGaps())
            {
                var spanned = false;
                foreach (var child in children)
                {
                    var (childTop, childBottom, _) = GetBlockRange(child, box.Size, box.WritingDirection, 0);
                    if (childTop < gapEnd - Epsilon && childBottom > gapStart + Epsilon)
                    {
                        spanned = true;
                        break;
                    }
                }

                if (!spanned)
                {
                    (_gaps ??= []).Add((top + gapStart, top + gapEnd));
                }
            }
        }

        else if (box.Box.Display.Inside == DisplayInside.GridLanes)
        {
            _root._hasGridLanes = true;
        }
        else if (box.Box.LayoutState is FlexContainerState flex)
        {
            // The gaps between the lines of a row flex container
            foreach (var (gapStart, gapEnd) in flex.LineGaps)
            {
                (_gaps ??= []).Add((top + gapStart, top + gapEnd));
            }
        }

        // The empty block size of the box after its in-flow contents (a box with a fixed block size): the browsers break anywhere in it
        var contentStart = top + edges.BlockStart;
        var contentEnd = Math.Min(bottom - edges.BlockEnd, context.ClipEnd);
        var lastInFlowEnd = contentStart;
        var inFlowCount = box.InFlowChildCount;
        for (var i = 0; i < inFlowCount && i < children.Length; i++)
        {
            var child = children[i];
            if (child is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true })
                continue;

            var (_, childBottom, _) = GetBlockRange(child, box.Size, box.WritingDirection, top);
            lastInFlowEnd = Math.Max(lastInFlowEnd, childBottom);
        }

        // The last-resort breaks after the block-start border and padding of the box and before its block-end border and padding (as
        // in the browsers: https://drafts.csswg.org/css-break-3/#possible-breaks, class C break points)
        if (GetFirstInFlowChild(box) is { } first)
        {
            var (firstTop, _, _) = GetBlockRange(first, box.Size, box.WritingDirection, top);
            // (not before the rendered legend of a fieldset, which is a part of its block-start border)
            var isLegend = first is BoxFragment { Box: { Element: { } firstElement } legendBox } && firstElement.IsHtml(TagId.Legend) && BlockFormattingContext.IsRenderedLegend(legendBox);
            if (edges.BlockStart > Epsilon && firstTop < context.ClipEnd && !isLegend)
            {
                // (after edges that the box repeats and nothing else: see BreakCandidate.OnlyEdgesFrom)
                var onlyEdgesFrom = fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone && firstTop - childContext.LeadingStart <= childContext.LeadingEdges + Epsilon ? childContext.LeadingStart : double.NaN;
                AddCandidate(new BreakCandidate(firstTop, contentStart, BreakAppeal.Avoid) { Sequence = startSequence, OnlyEdgesFrom = onlyEdgesFrom, EdgesOnlyFrom = childContext.EdgesOnlyFrom }, childContext);
            }

            // "Between the content edge of a block container box and the outer edges of its child content [...], if there is a
            // (non-zero) gap between them" (class C): the content that is not at the start of its container (aligned, or placed
            // in a track) may be pushed to the next fragmentainer
            var firstMarginTop = firstTop - (first is BoxFragment firstBox ? Math.Max(0, _writingDirection.ToLogical(firstBox.Margin).BlockStart) : 0);
            if (group is null && !isLegend && box.Box.Display.Internal == DisplayInternal.None && box.Box.Kind != ContainerBoxKind.TableGrid
                && firstMarginTop > contentStart + Epsilon && contentStart < context.ClipEnd)
            {
                AddCandidate(new BreakCandidate(contentStart, contentStart, childContext.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = Math.Min(firstMarginTop, context.ClipEnd) }, childContext);
            }
        }

        if (isFlowRoot)
            return;

        if (edges.BlockEnd > Epsilon && bottom - edges.BlockEnd > top + edges.BlockStart + Epsilon && bottom - edges.BlockEnd < context.ClipEnd)
        {
            AddCandidate(new BreakCandidate(bottom - edges.BlockEnd, Math.Max(lastInFlowEnd, contentStart), BreakAppeal.Avoid) { IsBeforeEndEdges = true }, childContext);
        }

        if (contentEnd - lastInFlowEnd > Epsilon && !HasDefiniteBlockSize(box) && GetLastInFlowChild(box) is BoxFragment lastChild
            && Math.Min(contentEnd, lastInFlowEnd + _writingDirection.ToLogical(lastChild.Margin).BlockEnd) is var marginEnd && marginEnd > lastInFlowEnd + Epsilon)
        {
            // The block-end margin of the last child, in the box (a block formatting context root, a box with a block-end padding):
            // it is truncated when it adjoins a break (https://drafts.csswg.org/css-break-3/#break-margins)
            (_gaps ??= []).Add((lastInFlowEnd, marginEnd));
        }

        if (contentEnd - lastInFlowEnd > Epsilon)
        {
            // A break at the start of the empty block size of an in-flow box without in-flow children would be a break before its
            // content: it is the class A break before the box when it has a previous sibling, and no possible break otherwise (class
            // C, no gap). The other ranges may break at their start (after the last child, before a float)
            var includesStart = !isInFlow || lastInFlowEnd > contentStart + Epsilon;
            AddCandidate(new BreakCandidate(lastInFlowEnd, lastInFlowEnd, childContext.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = contentEnd, IncludesRangeStart = includesStart, ExcludesRangeEnd = isInFlow, IsInFloat = !isInFlow && !PositionedLayout.IsAbsolutelyPositioned(box.Box) }, childContext);
        }
    }

    /// <summary>Returns the children of a box with its in-flow children sorted by their position in the block axis, when they are not.</summary>
    private static ReadOnlySpan<Fragment> SortInFlowChildren(BoxFragment box)
    {
        var children = box.ChildrenSpan;
        var count = Math.Min(box.InFlowChildCount, children.Length);
        var size = box.Size;
        var writingDirection = box.WritingDirection;
        var isSorted = true;
        for (var i = 1; i < count && isSorted; i++)
        {
            isSorted = GetBlockRange(children[i - 1], size, writingDirection, 0).Top <= GetBlockRange(children[i], size, writingDirection, 0).Top;
        }

        if (isSorted)
            return children;

        var sorted = children.ToArray();
        Array.Sort(sorted, 0, count, Comparer<Fragment>.Create((a, b) => GetBlockRange(a, size, writingDirection, 0).Top.CompareTo(GetBlockRange(b, size, writingDirection, 0).Top)));
        return sorted;
    }

    /// <summary>
    /// Creates the parallel flows of the contents of a box (see <see cref="Groups"/>): each item is visited in its own flow, and
    /// the breaks between the tracks (the lines of a flex container, the rows of a grid) are possible breaks of this flow.
    /// </summary>
    private ParallelGroup? CreateGroup(BoxFragment box, double top, VisitContext context)
    {
        if (ParallelStructure.TryCreate(box) is not { } structure)
            return null;

        var tracks = structure.Tracks;
        var starts = new double[tracks.Length];
        var ends = new double[tracks.Length];
        for (var i = 0; i < tracks.Length; i++)
        {
            starts[i] = top + tracks[i].Start;
            ends[i] = top + tracks[i].End;
        }

        var blockSizeIsAuto = !HasDefiniteBlockSize(box);
        var group = new ParallelGroup(box, starts, ends)
        {
            Avoid = context.Avoid,
            AvoidDepth = context.AvoidDepth,
            CloneStart = context.CloneStart,
            CloneEnd = context.CloneEnd,
            Repeated = context.Repeated,
            CloneOwner = context.CloneOwner,
            CanGrow = blockSizeIsAuto || box.Box.Display.Internal == DisplayInternal.TableRow,
        };

        List<List<Fragment>>? lines = null;
        foreach (var (child, startTrack, endTrack, stretches, line) in structure.Items)
        {
            if (line >= 0)
            {
                // A line of a multi-line column flex container: its items follow each other in the parallel flow
                lines ??= [];
                while (lines.Count <= line)
                {
                    lines.Add([]);
                }

                lines[line].Add(child);
                continue;
            }

            var (childTop, childBottom, _) = GetBlockRange(child, box.Size, box.WritingDirection, top);
            var nested = TakeNestedColumns(child, context);
            var flow = nested is null ? new FragmentationFlow(_writingDirection, _spannerBoxes, _root) : CreateNestedColumnsFlow(nested, childTop, childBottom);
            var item = new ParallelItem(flow)
            {
                Box = child,
                Nested = nested,
                Start = childTop,
                End = childBottom,
                IntrinsicEnd = childBottom,
                MarginEnd = Math.Max(0, _writingDirection.ToLogical(child.Margin).BlockEnd),
                StartTrack = startTrack,
                EndTrack = endTrack,
                Stretches = stretches,
                CountsForTrack = childBottom <= ends[endTrack] + Epsilon || structure.Kind != ParallelStructureKind.TableRow,
                CanBreakBefore = childTop > starts[startTrack] + Epsilon,
                BreaksBeforeAsLastResort = structure.Kind == ParallelStructureKind.Grid,
            };

            flow.OwnerItem = item;
            if (structure.Kind == ParallelStructureKind.Grid && !_root.GrowsUnbroken)
            {
                // (the row after a gutter that an item spans across grows when the gutter disappears at a break, see
                // ColumnBreaker.GetSpannedGapSpillover: the items that start in the row may grow without being broken)
                for (var track = startTrack + 1; track <= endTrack; track++)
                {
                    if (starts[track] > ends[track - 1] + Epsilon)
                    {
                        _root.GrowsUnbroken = true;
                        break;
                    }
                }
            }

            if (nested is not null)
            {
                // A multi-column container: the breaker places its rows of columns
                item.CanGrow = structure.Kind == ParallelStructureKind.TableRow || !HasDefiniteBlockSize(child) || nested.BlockSize is not null;
                item.Avoid = context.Avoid || child.Box.Style.Layout.Fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn;
                (_root._nestedItems ??= [])[child] = item;
                AddNestedOutOfFlow(child, item, childTop, context);
            }
            else if (IsMonolithic(child))
            {
                item.IsMonolithic = true;
                item.CanGrow = true;
                flow.AddUnbreakable(childTop, Math.Min(childBottom, context.ClipEnd));
                flow.ContentEnd = Math.Min(childBottom, context.ClipEnd);
            }
            else
            {
                // The block-end edge of a box that repeats its edges at the breaks (box-decoration-break: clone) follows its own breaks
                var layout = child.Box.Style.Layout;
                var childEdges = _writingDirection.ToLogical(child.Border.Add(child.Padding).Add(child.Scrollbars));
                var clones = layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone && (childEdges.BlockStart != 0 || childEdges.BlockEnd != 0);
                flow.VisitBox(child, childTop, childBottom, context with { LeadingStart = childTop, LeadingEdges = 0, EdgesOnlyFrom = childTop }, isInFlow: true, isFlowRoot: !clones);
                item.Avoid = context.Avoid || layout.Fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn;
                if (clones)
                {
                    item.CanGrow = true;
                    flow.ContentEnd = Math.Min(context.ClipEnd, Math.Max(flow.ContentEnd, childBottom));
                }
                else
                {
                    // The end of the box with its automatic block size (a stretched item is taller): the box grows with its contents
                    // when they are pushed by the breaks
                    var lastInFlowEnd = childTop + childEdges.BlockStart;
                    var grandChildren = child.ChildrenSpan;
                    for (var i = 0; i < child.InFlowChildCount && i < grandChildren.Length; i++)
                    {
                        if (grandChildren[i] is BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true })
                            continue;

                        var (_, grandChildBottom, _) = GetBlockRange(grandChildren[i], child.Size, child.WritingDirection, childTop);
                        lastInFlowEnd = Math.Max(lastInFlowEnd, grandChildBottom);
                    }

                    var intrinsicBlockSize = double.IsNaN(child.IntrinsicBlockSize) ? 0 : child.IntrinsicBlockSize;
                    item.IntrinsicEnd = Math.Min(childBottom, Math.Max(childTop + intrinsicBlockSize, lastInFlowEnd + childEdges.BlockEnd));
                    if (structure.Kind == ParallelStructureKind.FlexColumnItems && lastInFlowEnd + childEdges.BlockEnd > childBottom + Epsilon && lastInFlowEnd < context.ClipEnd)
                    {
                        // As in Blink, the block size of an item of a column flex container that is fragmented encompasses its
                        // contents (the item is not shorter than them) when the item cannot shrink and its flex basis is
                        // content-based, or its block size is automatic and it cannot shrink or its minimum block size is automatic
                        var canShrink = child.Box.Style.Layout.Flex.Shrink != 0 && !blockSizeIsAuto;
                        var hasDefiniteBasis = structure.DefiniteItems is not null && structure.DefiniteItems.Contains(child);
                        var blockSizeIsContent = layout.Box.GetSize(horizontal: !_writingDirection.IsHorizontal).IsAuto;
                        if ((!hasDefiniteBasis && !canShrink) || (blockSizeIsContent && (!canShrink || layout.Box.GetMinSize(horizontal: !_writingDirection.IsHorizontal).IsAuto)))
                        {
                            item.IntrinsicEnd = lastInFlowEnd + childEdges.BlockEnd;
                            item.EncompassesContents = true;
                            _root.GrowsUnbroken = true;
                        }
                    }

                    item.CanGrow = structure.Kind switch
                    {
                        ParallelStructureKind.TableRow => true,
                        ParallelStructureKind.FlexColumnItems => item.EncompassesContents || layout.Box.GetSize(horizontal: !_writingDirection.IsHorizontal).IsAuto || structure.DefiniteItems is null || !structure.DefiniteItems.Contains(child),
                        _ => layout.Box.GetSize(horizontal: !_writingDirection.IsHorizontal).IsAuto && !(stretches && structure.HasDefiniteTracks && !blockSizeIsAuto)
                            && !(structure.Kind == ParallelStructureKind.Grid && structure.DefiniteItems is not null && structure.DefiniteItems.Contains(child)),
                    };
                    if (structure.Kind == ParallelStructureKind.Grid && child.Box.LayoutState is GridContainerState { Subgrid: { Rows: not null } input } subgrid && input.Pass == subgrid.Pass)
                    {
                        // The rows of a subgrid are its contents (their gutters disappear at the breaks)
                        item.IsRowSubgrid = true;
                        item.IntrinsicEnd = childBottom;
                    }

                    flow.ContentEnd = Math.Min(context.ClipEnd, Math.Max(flow.ContentEnd, item.CanGrow || item.IsRowSubgrid ? item.IntrinsicEnd : childTop));
                    if (!item.CanGrow)
                    {
                        item.LeadingMonolithicEnd = GetLeadingMonolithicEnd(child, childTop);
                    }

                    // The empty block size of the box after its in-flow contents, up to its automatic block size, breaks anywhere
                    var contentEnd = Math.Min(item.IntrinsicEnd - childEdges.BlockEnd, context.ClipEnd);
                    if ((item.CanGrow || item.IsRowSubgrid) && contentEnd > lastInFlowEnd + Epsilon)
                    {
                        var includesStart = lastInFlowEnd > childTop + childEdges.BlockStart + Epsilon;
                        flow.AddCandidate(new BreakCandidate(lastInFlowEnd, lastInFlowEnd, item.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = contentEnd, IncludesRangeStart = includesStart }, context);
                    }
                }

                flow.Sort();
            }

            _parallelExtentEnd = Math.Max(_parallelExtentEnd, flow.ExtentEnd);
            group.Add(item, child);
        }

        if (lines is not null)
        {
            foreach (var line in lines)
            {
                if (line.Count == 0)
                    continue;

                // The items in the order of the block axis (column-reverse)
                var flow = new FragmentationFlow(_writingDirection, _spannerBoxes, _root);
                var fragments = line.ToArray();
                var size = box.Size;
                var writingDirection = box.WritingDirection;
                // (the items at the same position stay in the order of the content)
                var order = line;
                Array.Sort(fragments, (a, b) => GetBlockRange(a, size, writingDirection, 0).Top.CompareTo(GetBlockRange(b, size, writingDirection, 0).Top) is var result and not 0 ? result : order.IndexOf(a).CompareTo(order.IndexOf(b)));
                var (lineStart, _, _) = GetBlockRange(fragments[0], box.Size, box.WritingDirection, top);
                var lineEnd = lineStart;
                foreach (var fragment in fragments)
                {
                    lineEnd = Math.Max(lineEnd, GetBlockRange(fragment, box.Size, box.WritingDirection, top).Bottom);
                }

                // (the line starts with the block-start margin of its first item, which breaks anywhere)
                if (fragments[0] is BoxFragment firstItem)
                {
                    lineStart -= Math.Max(0, _writingDirection.ToLogical(firstItem.Margin).BlockStart);
                }
                var item = new ParallelItem(flow)
                {
                    Start = lineStart,
                    End = lineEnd,
                    IntrinsicEnd = lineEnd,
                    MarginEnd = fragments[^1] is BoxFragment last ? Math.Max(0, _writingDirection.ToLogical(last.Margin).BlockEnd) : 0,
                    CanGrow = true,
                    CountsForTrack = true,
                    Line = lines.IndexOf(line),
                };

                flow.OwnerItem = item;
                flow.VisitChildren(fragments, fragments.Length, box.Size, box.WritingDirection, top, context, lineCount: 0, orphans: 1, widows: 1, lastLineEnd: double.PositiveInfinity, breaksInGaps: true);
                flow.Sort();
                flow.ContentEnd = Math.Max(flow.ContentEnd, lineEnd);
                foreach (var fragment in fragments)
                {
                    group.ItemsByFragment[fragment] = item;
                }

                _parallelExtentEnd = Math.Max(_parallelExtentEnd, flow.ExtentEnd);
                item.Group = group;
                flow.OwnerItem = item;
                group.Items.Add(item);
            }
        }

        if (group.Items.Count == 0)
            return null;

        group.Complete();

        // The space before the first track (the lines and the items aligned in a container with a definite block size) breaks anywhere
        var groupContentStart = top + _writingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars)).BlockStart;
        if (structure.Kind is ParallelStructureKind.FlexRow or ParallelStructureKind.FlexColumnItems or ParallelStructureKind.Grid
            && starts[0] > groupContentStart + Epsilon && groupContentStart < context.ClipEnd)
        {
            // (also the block-start margin of the first item of a column flex container: the margins of the items are not truncated)
            AddCandidate(new BreakCandidate(groupContentStart, groupContentStart, context.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = Math.Min(starts[0], context.ClipEnd) }, context);
        }

        // The class A breaks between the tracks (https://drafts.csswg.org/css-flexbox-1/#pagination,
        // https://drafts.csswg.org/css-grid-2/#pagination): the break values of the items are propagated to their lines and rows
        for (var track = 1; track < tracks.Length; track++)
        {
            var position = starts[track];
            var previousEnd = ends[track - 1];
            if (position < previousEnd - Epsilon || position >= context.ClipEnd)
                continue;

            var after = BreakBetween.Auto;
            var before = BreakBetween.Auto;
            foreach (var item in group.Items)
            {
                if (item.Box is null)
                    continue;

                if (item.EndTrack == track - 1)
                {
                    after = Combine(after, GetBreakAfter(item.Box));
                }

                if (item.StartTrack == track)
                {
                    before = Combine(before, GetBreakBefore(item.Box));
                }
            }

            if (structure.Kind == ParallelStructureKind.FlexColumnItems)
            {
                AddBreakInGap(structure.Items[track - 1].Fragment, structure.Items[track].Fragment, group.Items[track].Start, group.Items[track - 1].End, context);
                continue;
            }

            var appeal = IsForcedBreak(after) || IsForcedBreak(before) ? BreakAppeal.Forced
                : context.Avoid || IsAvoidBreak(after) || IsAvoidBreak(before) ? BreakAppeal.Avoid
                : BreakAppeal.Perfect;
            AddCandidate(new BreakCandidate(position, previousEnd, appeal), context);
        }

        (Groups ??= []).Add(group);
        (_root._groupsByContainer ??= [])[box] = group;
        for (Fragment? ancestor = box; ancestor is BoxFragment ancestorBox; ancestor = ancestor.Parent)
        {
            if (!(_root._groupAncestors ??= []).Add(ancestorBox))
                break;
        }

        return group;
    }

    /// <summary>Adds the class A break opportunity between two in-flow sibling boxes (https://drafts.csswg.org/css-break-3/#possible-breaks).</summary>
    private void AddBreakBetween(Fragment previous, Fragment next, double nextTop, double previousBottom, VisitContext context)
    {
        var after = GetBreakAfter(previous);
        var before = GetBreakBefore(next);
        if (IsForcedBreak(after) || IsForcedBreak(before) || PageBreakRules.Current?.IsNameBreak(previous, next) == true)
        {
            // https://drafts.csswg.org/css-break-3/#break-margins: "When a forced break occurs there, adjoining margins before the break
            // are truncated, but margins after the break are preserved"
            var marginStart = next is BoxFragment nextBox ? GetCollapsedBlockStartMargin(nextBox) : 0;
            AddCandidate(new BreakCandidate(Math.Max(previousBottom, nextTop - marginStart), previousBottom, BreakAppeal.Forced), context);
            return;
        }

        // The space between the boxes is made of their margins (not of clearance, nor of the offset of a box that avoids floats)
        var margins = (previous is BoxFragment previousBox ? Math.Max(0, previousBox.Margin.Get(_writingDirection.BlockEnd)) : 0)
            + (next is BoxFragment followingBox ? Math.Max(0, followingBox.Margin.Get(_writingDirection.BlockStart)) : 0);
        var avoid = context.Avoid || IsAvoidBreak(after) || IsAvoidBreak(before);
        AddCandidate(new BreakCandidate(nextTop, previousBottom, avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { IsBetweenMargins = nextTop - previousBottom <= margins + Epsilon }, context);
    }

    /// <summary>
    /// Returns the positive block-start margin of a box after a forced break, collapsed with the block-start margins of its first
    /// in-flow descendants that collapse through its block-start edge (https://drafts.csswg.org/css2/#collapsing-margins): the
    /// margin of a first child that is at the block-start edge of its parent (without border nor padding) collapsed with it.
    /// </summary>
    private double GetCollapsedBlockStartMargin(BoxFragment box)
    {
        var side = _writingDirection.BlockStart;
        var margin = Math.Max(0, box.Margin.Get(side));
        while (box.Border.Get(side) + box.Padding.Get(side) <= Epsilon && GetFirstInFlowChild(box) is BoxFragment child
            && GetBlockStartOffset(box, child) <= Epsilon)
        {
            margin = Math.Max(margin, child.Margin.Get(side));
            box = child;
        }

        return margin;

        static Fragment? GetFirstInFlowChild(BoxFragment box)
        {
            var children = box.ChildrenSpan;
            for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
            {
                if (children[i] is not BoxFragment { Box.Style.Layout.IsOutOfFlow: true })
                    return children[i];
            }

            return null;
        }

        double GetBlockStartOffset(BoxFragment parent, Fragment child) => side switch
        {
            PhysicalSide.Top => child.Offset.Y,
            PhysicalSide.Left => child.Offset.X,
            PhysicalSide.Right => parent.Size.Width - child.Offset.X - child.Size.Width,
            _ => parent.Size.Height - child.Offset.Y - child.Size.Height,
        };
    }

    /// <summary>
    /// Adds the break opportunities between two items of a column flex container that are apart: as in Blink, the items keep
    /// their offsets in the container after a break, so their margins are not truncated (a break may occur anywhere in them),
    /// but "the gap disappears when it coincides with a fragmentation break" (https://drafts.csswg.org/css-gaps-1/#gaps: the
    /// gap and the space of the content distribution).
    /// </summary>
    private void AddBreakInGap(Fragment previous, Fragment next, double nextTop, double previousBottom, VisitContext context)
    {
        var after = GetBreakAfter(previous);
        var before = GetBreakBefore(next);
        var marginAfter = previous is BoxFragment previousBox ? Math.Max(0, _writingDirection.ToLogical(previousBox.Margin).BlockEnd) : 0;
        var marginBefore = next is BoxFragment nextBox ? Math.Max(0, _writingDirection.ToLogical(nextBox.Margin).BlockStart) : 0;
        var gapStart = Math.Min(nextTop, previousBottom + marginAfter);
        var gapEnd = Math.Max(gapStart, nextTop - marginBefore);
        if (IsForcedBreak(after) || IsForcedBreak(before))
        {
            AddCandidate(new BreakCandidate(gapEnd, previousBottom, BreakAppeal.Forced), context);
            if (gapEnd > gapStart + Epsilon)
            {
                (_gaps ??= []).Add((gapStart, gapEnd));
            }

            if (nextTop > gapEnd + Epsilon)
            {
                // The margin after the forced break is preserved (https://drafts.csswg.org/css-break-3/#break-margins) and
                // breaks anywhere, as the other margins of the items: an item that starts after the end of the
                // fragmentainer is pushed to the next one
                AddCandidate(new BreakCandidate(gapEnd, gapEnd, context.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { RangeEnd = nextTop }, context);
            }

            return;
        }

        var appeal = context.Avoid || IsAvoidBreak(after) || IsAvoidBreak(before) ? BreakAppeal.Avoid : BreakAppeal.Perfect;
        AddCandidate(new BreakCandidate(gapEnd, previousBottom, appeal), context);
        if (gapStart > previousBottom + Epsilon)
        {
            AddCandidate(new BreakCandidate(previousBottom, previousBottom, appeal) { RangeEnd = gapStart, IncludesRangeStart = true }, context);
        }

        if (nextTop > gapEnd + Epsilon)
        {
            AddCandidate(new BreakCandidate(gapEnd, gapEnd, appeal) { RangeEnd = nextTop, IncludesRangeStart = true }, context);
        }

        if (gapEnd > gapStart + Epsilon)
        {
            (_gaps ??= []).Add((gapStart, gapEnd));
        }
    }

    private void AddCandidate(BreakCandidate candidate, VisitContext context)
    {
        if (context.CloneStart != 0 || context.CloneEnd != 0)
        {
            // (as in Blink, a box does not repeat its edges at a break after the end of its border box, in the contents that overflow it:
            // a range of breaks across the end of the box is split there)
            if (candidate.IsRange)
            {
                for (var cloneBox = context.CloneBoxes; cloneBox is not null; cloneBox = cloneBox.Next)
                {
                    if (cloneBox.End > candidate.Position + Epsilon && cloneBox.End < candidate.RangeEnd - Epsilon)
                    {
                        AddCandidate(candidate with { RangeEnd = cloneBox.End, ExcludesRangeEnd = false }, context);
                        AddCandidate(candidate with { Position = cloneBox.End, ContentEnd = cloneBox.End, IncludesRangeStart = true }, context);
                        return;
                    }
                }
            }

            var (cloneStart, cloneEnd) = (context.CloneStart, context.CloneEnd);
            for (var cloneBox = context.CloneBoxes; cloneBox is not null; cloneBox = cloneBox.Next)
            {
                if (cloneBox.End <= candidate.Position + Epsilon)
                {
                    cloneStart -= cloneBox.StartEdges;
                    cloneEnd -= cloneBox.EndEdges;
                }
            }

            candidate = candidate with { CloneStartEdges = Math.Max(0, cloneStart), CloneEndEdges = Math.Max(0, cloneEnd), Repeated = context.Repeated, CloneOwner = context.CloneOwner };
        }

        if (context.AvoidDepth != 0)
        {
            candidate = candidate with { AvoidDepth = context.AvoidDepth };
        }

        if (candidate.IsRange)
        {
            Ranges.Add(candidate);
        }
        else
        {
            Candidates.Add(candidate.Sequence != 0 ? candidate : candidate with { Sequence = ++_root._sequence });
        }
    }

    /// <summary>
    /// Adds a float of the flow. A float is a parallel flow (https://drafts.csswg.org/css-break-3/#parallel-flows): as in Blink,
    /// it is pushed alone to the next fragmentainer when it does not fit in what remains of the current one. The floats are
    /// fragmented with the flow of their containing block: the multi-column layout pushes the ones that do not fit by laying out
    /// the strip again (see <see cref="FloatPush"/>); the flow may break before a float that was pushed (it starts the
    /// fragmentainer), and a break in the clearance after a float is after the float.
    /// </summary>
    /// <param name="previousBottom">The block-end edge of the in-flow sibling before the float, or NaN.</param>
    /// <param name="hasContentBefore">Whether in-flow content precedes the float in its containing block.</param>
    private void AddFloat(BoxFragment box, double top, double bottom, double previousBottom, VisitContext context, bool hasContentBefore)
    {
        var margin = _writingDirection.ToLogical(box.Margin);
        var marginTop = top - margin.BlockStart;
        var marginBottom = Math.Max(marginTop, bottom + margin.BlockEnd);
        if (marginBottom - marginTop <= Epsilon)
            return;

        var isMonolithic = IsMonolithic(box);
        var avoid = context.Avoid || box.Box.Style.Layout.Fragmentation.BreakInside is BreakInside.Avoid or BreakInside.AvoidColumn;
        var isPushed = _root._pushedFloats is { } pushed && pushed.TryGetValue(box.Box, out var push) && !push.IsDeferred;
        HasPushedFloats |= isPushed;
        var extent = new FloatExtent(box, marginTop, marginBottom, this)
        {
            IsPushed = isPushed,
            IsMonolithic = isMonolithic,
            Context = context.FormattingContext,
            BorderBoxEnd = bottom,
            IsUnbreakable = isMonolithic || avoid,
            HasForcedBreakBefore = IsForcedBreak(GetBreakBefore(box)),
            LeadingEnd = isMonolithic ? bottom : GetLeadingMonolithicEnd(box, top),
            LeadingStart = hasContentBefore ? marginTop : context.LeadingStart,
            LeadingEdges = hasContentBefore ? 0 : context.LeadingEdges,
        };

        (_floats ??= []).Add(extent);
        (_root._allFloats ??= []).Add(extent);
        if (!ReferenceEquals(this, _root))
        {
            _root._parallelFloatEnd = Math.Max(_root._parallelFloatEnd, marginBottom);
        }

        if (!isPushed && double.IsNaN(previousBottom))
            return;

        var contentEnd = double.IsNaN(previousBottom) ? marginTop : Math.Min(marginTop, previousBottom);
        AddCandidate(new BreakCandidate(marginTop, contentEnd, context.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect) { IsBeforeFloat = !isPushed, IsBeforePushedFloat = isPushed }, context);
    }

    /// <summary>The floats of this flow (see <see cref="AddFloat"/>).</summary>
    public List<FloatExtent>? OwnFloats => _floats;

    /// <summary>Records the space between two in-flow siblings when it is not made of their margins (see <see cref="ClearanceGaps"/>).</summary>
    private void AddClearanceGap(Fragment previous, Fragment next, double previousBottom, double nextTop)
    {
        if (_root._allFloats is null || next is not BoxFragment nextBox)
            return;

        var margins = (previous is BoxFragment previousBox ? Math.Max(0, previousBox.Margin.Get(_writingDirection.BlockEnd)) : 0) + Math.Max(0, nextBox.Margin.Get(_writingDirection.BlockStart));
        if (nextTop - previousBottom > margins + Epsilon)
        {
            (_root._clearanceGaps ??= []).Add(new ClearanceGap(previousBottom, nextTop, this));
        }
    }

    private void AddUnbreakable(double top, double bottom)
    {
        if (bottom - top <= Epsilon)
            return;

        _unbreakables.Add((top, bottom));
        _unbreakablesSorted = false;
    }

    /// <summary>
    /// Returns whether a box is monolithic in the fragmentation context (https://drafts.csswg.org/css-break-3/#monolithic): the
    /// replaced elements, the scroll containers, the boxes with size containment in the block axis
    /// (https://drafts.csswg.org/css-contain-2/#containment-size), the boxes whose writing mode differs from the one of the
    /// fragmentation context (orthogonal flows, https://drafts.csswg.org/css-writing-modes-4/#orthogonal-flows), and the nested
    /// multi-column containers: their boxes are not split as the other boxes are; the in-flow ones are fragmented by the breaker
    /// (<see cref="NestedColumns"/>), the other ones (floats, absolutely positioned boxes, inline-blocks) are not fragmented.
    /// </summary>
    internal bool IsMonolithic(BoxFragment fragment) => IsMonolithic(fragment, _writingDirection, scrollContainersAreMonolithic: !_root._isPaged);

    /// <summary>Returns whether a box is monolithic in a fragmentation context with the given writing mode (see <see cref="IsMonolithic(BoxFragment)"/>).</summary>
    /// <param name="fragment">The box.</param>
    /// <param name="fragmentationContext">The writing mode of the fragmentation context.</param>
    /// <param name="scrollContainersAreMonolithic">Whether the scroll containers are monolithic: as in the browsers, not in the pages (their contents are printed).</param>
    internal static bool IsMonolithic(BoxFragment fragment, WritingDirection fragmentationContext, bool scrollContainersAreMonolithic = true)
    {
        var box = fragment.Box;
        if (box.Kind is ContainerBoxKind.Replaced or ContainerBoxKind.Viewport || (fragment.IsScrollContainer && scrollContainersAreMonolithic) || box.IsAtomicInline)
            return true;

        var layout = box.Style.Layout;
        if (layout.WritingMode != fragmentationContext.WritingMode || layout.IsMultiColumnContainer && box.Kind == ContainerBoxKind.Normal)
            return true;

        // The rendered legend of a fieldset is a part of its block-start border: it is not broken (as in the browsers)
        if (box.Element is { } element && element.IsHtml(TagId.Legend) && BlockFormattingContext.IsRenderedLegend(box))
            return true;

        return IntrinsicSizing.HasSizeContainment(box, inlineAxis: false) && UsedContainment.CanApplySizeContainment(box);
    }

    /// <summary>
    /// Returns the <c>break-before</c> value of a box, with the values propagated from its first in-flow children
    /// (https://drafts.csswg.org/css-break-3/#break-propagation). "Forced breaks are allowed within layout-contained elements but do
    /// not propagate to the parent" (https://drafts.csswg.org/css-contain-2/#containment-layout).
    /// </summary>
    private BreakBetween GetBreakBefore(Fragment fragment)
    {
        var result = BreakBetween.Auto;
        while (fragment is BoxFragment box)
        {
            result = Combine(result, box.Box.Style.Layout.Fragmentation.BreakBefore);
            if (box.Box.LayoutState is MultiColumnState && GetFirstInFlowChild(box) is BoxFragment firstSpanner && MultiColumnFlow.IsColumnSpanner(firstSpanner.Box))
            {
                // The first content of a nested multi-column container is a column spanner, which is in this fragmentation context
                result = Combine(result, firstSpanner.Box.Style.Layout.Fragmentation.BreakBefore);
            }

            if (IsMonolithic(box) || UsedContainment.HasLayoutContainment(box.Box))
                break;

            // https://drafts.csswg.org/css-flexbox-1/#pagination: the break values of the items of the first line (row, track)
            if (ParallelStructure.TryCreate(box) is { } structure)
            {
                // (as in Blink, the first item of each line of a multi-line column flex container)
                if (structure.Kind == ParallelStructureKind.FlexColumn)
                {
                    foreach (var item in structure.GetLineEdgeItems(box, first: true))
                    {
                        result = Combine(result, GetBreakBefore(item));
                    }

                    break;
                }

                foreach (var item in structure.Items)
                {
                    if (item.StartTrack == 0)
                    {
                        result = Combine(result, GetBreakBefore(item.Fragment));
                    }
                }

                break;
            }

            // (as in Blink, the values of the items of a flex or grid container and of the first row of a table propagate through
            // its borders and paddings, not the ones of the children of a block container)
            if ((box.Box.Kind != ContainerBoxKind.TableGrid && _writingDirection.ToLogical(box.Border.Add(box.Padding)).BlockStart != 0) || GetFirstInFlowChild(box) is not { } first)
                break;

            fragment = first;
        }

        return result;
    }

    /// <summary>Returns the <c>break-after</c> value of a box, with the values propagated from its last in-flow children.</summary>
    private BreakBetween GetBreakAfter(Fragment fragment)
    {
        var result = BreakBetween.Auto;
        while (fragment is BoxFragment box)
        {
            result = Combine(result, box.Box.Style.Layout.Fragmentation.BreakAfter);
            if (box.Box.LayoutState is MultiColumnState && GetLastInFlowChild(box) is BoxFragment lastSpanner && MultiColumnFlow.IsColumnSpanner(lastSpanner.Box))
            {
                result = Combine(result, lastSpanner.Box.Style.Layout.Fragmentation.BreakAfter);
            }

            if (IsMonolithic(box) || UsedContainment.HasLayoutContainment(box.Box))
                break;

            if (ParallelStructure.TryCreate(box) is { } structure)
            {
                if (structure.Kind == ParallelStructureKind.FlexColumn)
                {
                    foreach (var item in structure.GetLineEdgeItems(box, first: false))
                    {
                        result = Combine(result, GetBreakAfter(item));
                    }

                    break;
                }

                foreach (var item in structure.Items)
                {
                    if (item.EndTrack == structure.Tracks.Length - 1)
                    {
                        result = Combine(result, GetBreakAfter(item.Fragment));
                    }
                }

                break;
            }

            if ((box.Box.Kind != ContainerBoxKind.TableGrid && _writingDirection.ToLogical(box.Border.Add(box.Padding)).BlockEnd != 0) || GetLastInFlowChild(box) is not { } last)
                break;

            fragment = last;
        }

        return result;
    }

    private static Fragment? GetFirstInFlowChild(BoxFragment box)
    {
        var children = box.ChildrenSpan;
        for (var i = 0; i < box.InFlowChildCount && i < children.Length; i++)
        {
            if (children[i] is not BoxFragment { Box.IsOutOfFlow: true } and not BoxFragment { Box.IsOutsideMarker: true })
                return children[i];
        }

        return null;
    }

    private static Fragment? GetLastInFlowChild(BoxFragment box)
    {
        var children = box.ChildrenSpan;
        for (var i = Math.Min(box.InFlowChildCount, children.Length) - 1; i >= 0; i--)
        {
            if (children[i] is not BoxFragment { Box.IsOutOfFlow: true } and not BoxFragment { Box.IsOutsideMarker: true })
                return children[i];
        }

        return null;
    }

    /// <summary>Combines the break values of a box and of its propagated children: a forced break wins over avoid, avoid over auto.</summary>
    private static BreakBetween Combine(BreakBetween a, BreakBetween b)
    {
        if (IsForcedBreak(a))
            return a;

        if (IsForcedBreak(b))
            return b;

        return IsAvoidBreak(a) ? a : b;
    }

    /// <summary>
    /// Returns whether a value of <c>break-before</c>/<c>break-after</c> forces a break between columns
    /// (https://drafts.csswg.org/css-break-4/#break-between): <c>column</c>, <c>always</c> and <c>all</c> (the innermost
    /// fragmentation context is the multi-column container); the page values do not apply in continuous media. The page values
    /// force the breaks of a paged flow (see <see cref="PageBreakRules"/>).
    /// </summary>
    internal static bool IsForcedBreak(BreakBetween value) => PageBreakRules.Current is not null ? PageBreakRules.IsForced(value) : value is BreakBetween.Column or BreakBetween.Always or BreakBetween.All;

    /// <summary>Returns whether a value of <c>break-before</c>/<c>break-after</c> avoids a break between columns (<c>avoid</c>, <c>avoid-column</c>).</summary>
    internal static bool IsAvoidBreak(BreakBetween value) => PageBreakRules.Current is not null ? PageBreakRules.IsAvoid(value) : value is BreakBetween.Avoid or BreakBetween.AvoidColumn;
}

/// <summary>A float kept in the page before a forced break (see <see cref="FragmentationFlow.KeepFloat"/>).</summary>
/// <param name="Fragment">The fragment of the float in the strip.</param>
/// <param name="Start">The block-start edge of its margin box in the strip.</param>
/// <param name="BreakPosition">The position of the forced break in the strip.</param>
internal readonly record struct KeptFloat(BoxFragment Fragment, double Start, double BreakPosition);

/// <summary>A float of the strip of a fragmentation context (see <c>FragmentationFlow.AddFloat</c>).</summary>
/// <param name="Fragment">The fragment of the float in the strip.</param>
/// <param name="Start">The block-start edge of its margin box in the strip.</param>
/// <param name="End">The block-end edge of its margin box in the strip.</param>
/// <param name="Flow">The flow that contains the float (the flow of the strip, or a parallel flow).</param>
internal readonly record struct FloatExtent(BoxFragment Fragment, double Start, double End, FragmentationFlow Flow)
{
    /// <summary>The block-end edge of the border box.</summary>
    public double BorderBoxEnd { get; init; }

    /// <summary>Whether the float is monolithic or has <c>break-inside: avoid</c>.</summary>
    public bool IsUnbreakable { get; init; }

    /// <summary>Whether the float is monolithic: it is in one fragmentainer.</summary>
    public bool IsMonolithic { get; init; }

    /// <summary>Whether the float has a forced break before it (<c>break-before</c> of the float or of its first content).</summary>
    public bool HasForcedBreakBefore { get; init; }

    /// <summary>Whether the float was pushed to the start of a fragmentainer (see <see cref="FloatPush"/>).</summary>
    public bool IsPushed { get; init; }

    /// <summary>The box that establishes the block formatting context of the float, or null for the one of the strip.</summary>
    public BoxFragment? Context { get; init; }

    /// <summary>The block-end edge of the first content of the float when it is monolithic (a line box, a monolithic box), else NaN.</summary>
    public double LeadingEnd { get; init; }

    /// <summary>The position of the strip from which only the block-start borders and paddings of the ancestors of the float precede it.</summary>
    public double LeadingStart { get; init; }

    /// <summary>The block-start borders and paddings between <see cref="LeadingStart"/> and the float.</summary>
    public double LeadingEdges { get; init; }
}

/// <summary>The header and the footer of a table grid box that are repeated in each of its fragments (see <c>FragmentationFlow.GetRepeatedSections</c>).</summary>
/// <param name="Header">The fragment of the header in the strip, or null.</param>
/// <param name="HeaderSize">The block size the header takes at the start of a fragment (with the border spacing after it).</param>
/// <param name="Footer">The fragment of the footer in the strip, or null.</param>
/// <param name="FooterSize">The block size the footer takes at the end of a fragment (with the border spacing before it).</param>
internal sealed record RepeatedSections(BoxFragment? Header, double HeaderSize, BoxFragment? Footer, double FooterSize)
{
    /// <summary>The block size of the header.</summary>
    public double HeaderBlockSize { get; init; }

    /// <summary>The block size of the footer.</summary>
    public double FooterBlockSize { get; init; }

    /// <summary>
    /// Whether the header is not repeated: it is taller than a quarter of the fragmentainer where the table starts (set by the
    /// breaker when it reaches the table).
    /// </summary>
    public bool IsHeaderDisabled { get; set; }

    /// <summary>Whether the footer is not repeated (see <see cref="IsHeaderDisabled"/>).</summary>
    public bool IsFooterDisabled { get; set; }
}

/// <summary>An in-flow box of a <see cref="FragmentationFlow"/> that avoids the floats of its block formatting context.</summary>
/// <param name="Fragment">The fragment of the box in the strip.</param>
/// <param name="Start">The block-start edge of its border box in the strip.</param>
/// <param name="End">The block-end edge of its border box in the strip.</param>
/// <param name="Context">The box that establishes the block formatting context the box participates in, or null for the one of the strip.</param>
internal readonly record struct FloatAvoider(BoxFragment Fragment, double Start, double End, BoxFragment? Context);

/// <summary>A space of a strip where a box was moved below floats: its clearance, or the offset of a box that avoids the floats.</summary>
/// <param name="Start">The position of the box without the floats (the end of the content before it).</param>
/// <param name="End">The block-start edge of the box.</param>
/// <param name="Flow">The flow of the box.</param>
internal readonly record struct ClearanceGap(double Start, double End, FragmentationFlow Flow);

/// <summary>
/// A float that the multi-column layout pushed to the start of a fragmentainer because it does not fit in the previous one
/// (https://drafts.csswg.org/css-break-3/#parallel-flows: as in Blink, a float is pushed alone, and the content beside and after
/// it is laid out without it): the strip is laid out again with the float placed at or below the position of the strip where
/// that fragmentainer starts.
/// </summary>
internal sealed class FloatPush
{
    /// <summary>The fragmentainer the float is pushed to.</summary>
    public int TargetIndex { get; set; }

    /// <summary>The highest position of the block-start margin edge of the float in its block formatting context.</summary>
    public double MinBlockOffset { get; set; }

    /// <summary>
    /// Whether the float is not pushed: it is in a parallel flow (the contents that overflow a box) that reaches it in a later
    /// fragmentainer than the content after the box, which is laid out without it (it does not clear the float).
    /// </summary>
    public bool IsDeferred { get; init; }
}

/// <summary>A column spanner of a multi-column container (https://drafts.csswg.org/css-multicol-1/#column-span) in the strip of its content.</summary>
/// <param name="Fragment">The fragment of the spanner, laid out in the inline size of the multi-column container.</param>
/// <param name="MarginBoxStart">The block-start edge of its margin box in the strip.</param>
/// <param name="MarginBoxEnd">The block-end edge of its margin box in the strip.</param>
internal readonly record struct ColumnSpannerPlacement(BoxFragment Fragment, double MarginBoxStart, double MarginBoxEnd);

/// <summary>The kinds of boxes whose contents are parallel flows.</summary>
internal enum ParallelStructureKind : byte
{
    /// <summary>A row flex container: the tracks are its lines.</summary>
    FlexRow,

    /// <summary>A multi-line column flex container: its lines are the parallel flows.</summary>
    FlexColumn,

    /// <summary>A single-line column flex container: each item is a track.</summary>
    FlexColumnItems,

    /// <summary>A grid container: the tracks are its rows.</summary>
    Grid,

    /// <summary>A table row: the parallel flows are its cells.</summary>
    TableRow,
}

/// <summary>
/// The structure of a box whose contents are parallel flows (https://drafts.csswg.org/css-break-3/#parallel-flows), from the state
/// of its layout: its tracks in the block axis (the lines of a row flex container, the rows of a grid, a table row), from the
/// block-start edge of its border box, and the tracks of each item.
/// </summary>
internal sealed class ParallelStructure
{
    public required ParallelStructureKind Kind { get; init; }

    public required (double Start, double End)[] Tracks { get; init; }

    /// <summary>The in-flow items: the tracks where they start and end, whether they are stretched in the block axis, and their line in a multi-line column flex container (else -1).</summary>
    public List<(BoxFragment Fragment, int StartTrack, int EndTrack, bool Stretches, int Line)> Items { get; } = [];

    /// <summary>
    /// The items whose block size does not follow their contents: the items of a single-line column flex container whose flex
    /// basis is definite, and the stretched items of a grid whose rows have a fixed size (as in Blink, a row whose track sizing
    /// function is fixed does not grow with its items when they are fragmented).
    /// </summary>
    public HashSet<BoxFragment>? DefiniteItems { get; set; }

    /// <summary>Whether the tracks do not grow with the stretched items when the block size of the container is definite (a single-line flex container, a grid).</summary>
    public bool HasDefiniteTracks { get; init; }

    /// <summary>
    /// Returns the first (or the last) item in the block axis of each line of a multi-line column flex container: their
    /// <c>break-before</c> (<c>break-after</c>) values are propagated to the container.
    /// </summary>
    public List<BoxFragment> GetLineEdgeItems(BoxFragment container, bool first)
    {
        var result = new List<BoxFragment>();
        var positions = new List<double>();
        var lines = new List<int>();
        foreach (var item in Items)
        {
            var (top, bottom, _) = FragmentationFlow.GetBlockRange(item.Fragment, container.Size, container.WritingDirection, 0);
            var position = first ? top : -bottom;
            var index = lines.IndexOf(item.Line);
            if (index < 0)
            {
                lines.Add(item.Line);
                result.Add(item.Fragment);
                positions.Add(position);
            }
            else if (position < positions[index])
            {
                result[index] = item.Fragment;
                positions[index] = position;
            }
        }

        return result;
    }

    public static ParallelStructure? TryCreate(BoxFragment box)
    {
        var children = box.ChildrenSpan;
        var inFlowCount = Math.Min(box.InFlowChildCount, children.Length);
        var blockSize = box.WritingDirection.ToLogical(box.Size).Block;
        ParallelStructure? result;
        switch (box.Box.LayoutState)
        {
            case FlexContainerState { IsRow: true, Lines.Length: > 0 } flex when box.Box.Display.Inside is DisplayInside.Flex or DisplayInside.WebkitBox:
                {
                    // The lines in the order of the block axis (wrap-reverse stacks them from the block-end edge)
                    var order = new int[flex.Lines.Length];
                    for (var i = 0; i < order.Length; i++)
                    {
                        order[i] = i;
                    }

                    Array.Sort(order, (a, b) => flex.Lines[a].Start.CompareTo(flex.Lines[b].Start));
                    var tracks = new (double Start, double End)[order.Length];
                    var trackOfLine = new int[order.Length];
                    for (var i = 0; i < order.Length; i++)
                    {
                        tracks[i] = flex.Lines[order[i]];
                        trackOfLine[order[i]] = i;
                    }

                    result = new ParallelStructure { Kind = ParallelStructureKind.FlexRow, Tracks = tracks, HasDefiniteTracks = flex.IsSingleLine };
                    for (var i = 0; i < inFlowCount; i++)
                    {
                        if (children[i] is not BoxFragment { Box.IsOutOfFlow: false } child)
                            continue;

                        if (!flex.Items.TryGetValue(child.Box, out var state) || state.Line < 0 || state.Line >= trackOfLine.Length)
                            return null;

                        var track = trackOfLine[state.Line];
                        result.Items.Add((child, track, track, state.Stretches, -1));
                    }

                    break;
                }

            case FlexContainerState { IsRow: false, LineCount: > 1 } flex when box.Box.Display.Inside is DisplayInside.Flex or DisplayInside.WebkitBox:
                {
                    var edges = box.WritingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
                    result = new ParallelStructure { Kind = ParallelStructureKind.FlexColumn, Tracks = [(edges.BlockStart, Math.Max(edges.BlockStart, blockSize - edges.BlockEnd))] };
                    for (var i = 0; i < inFlowCount; i++)
                    {
                        if (children[i] is not BoxFragment { Box.IsOutOfFlow: false } child)
                            continue;

                        if (!flex.Items.TryGetValue(child.Box, out var state) || state.Line < 0)
                            return null;

                        result.Items.Add((child, 0, 0, false, state.Line));
                    }

                    break;
                }

            case FlexContainerState { IsRow: false, LineCount: 1 } flex when box.Box.Display.Inside is DisplayInside.Flex or DisplayInside.WebkitBox:
                {
                    // The items follow each other in the block axis: each one is a track (they are not parallel, but each item has
                    // its own flow: the block-end edge of an item with a definite block size does not depend on its contents)
                    var items = new List<(BoxFragment Fragment, double Start, double End)>();
                    HashSet<BoxFragment>? definiteItems = null;
                    for (var i = 0; i < inFlowCount; i++)
                    {
                        if (children[i] is not BoxFragment { Box.IsOutOfFlow: false } child)
                            continue;

                        if (!flex.Items.TryGetValue(child.Box, out var itemState))
                            return null;

                        // The track of an item ends after its block-end margin
                        var rect = box.WritingDirection.ToLogical(child.Rect.Translate(-(child.RelativeOffset + child.StickyOffset)), box.Size);
                        items.Add((child, rect.Offset.Block, rect.Offset.Block + rect.Size.Block + Math.Max(0, box.WritingDirection.ToLogical(child.Margin).BlockEnd)));
                        if (itemState.HasDefiniteFlexBasis)
                        {
                            (definiteItems ??= []).Add(child);
                        }
                    }

                    items.Sort(static (a, b) => a.Start.CompareTo(b.Start));
                    var tracks = new (double Start, double End)[items.Count];
                    result = new ParallelStructure { Kind = ParallelStructureKind.FlexColumnItems, Tracks = tracks, DefiniteItems = definiteItems };
                    for (var i = 0; i < items.Count; i++)
                    {
                        // The items that overlap (negative margins) are not fragmented independently
                        if (i > 0 && items[i].Start < items[i - 1].End - 0.01)
                            return null;

                        tracks[i] = (items[i].Start, items[i].End);
                        result.Items.Add((items[i].Fragment, i, i, false, -1));
                    }

                    break;
                }

            case GridContainerState { ItemRows: { } itemRows } grid when box.Box.Display.Inside == DisplayInside.Grid:
                {
                    var rows = grid.GetRows();
                    if (rows.Length == 0)
                        return null;

                    result = new ParallelStructure { Kind = ParallelStructureKind.Grid, Tracks = rows, HasDefiniteTracks = true };
                    for (var i = 0; i < inFlowCount; i++)
                    {
                        if (children[i] is not BoxFragment { Box.IsOutOfFlow: false } child)
                            continue;

                        if (!itemRows.TryGetValue(child.Box, out var state))
                            return null;

                        var start = Math.Clamp(state.Start, 0, rows.Length - 1);
                        result.Items.Add((child, start, Math.Clamp(state.End - 1, start, rows.Length - 1), state.Stretches, -1));
                        if (state.HasFixedRows && state.Stretches)
                        {
                            (result.DefiniteItems ??= []).Add(child);
                        }
                    }

                    break;
                }

            default:
                if (box.Box.Display.Internal != DisplayInternal.TableRow)
                    return null;

                result = new ParallelStructure { Kind = ParallelStructureKind.TableRow, Tracks = [(0, blockSize)] };
                for (var i = 0; i < inFlowCount; i++)
                {
                    if (children[i] is BoxFragment { Box.IsOutOfFlow: false } child)
                    {
                        result.Items.Add((child, 0, 0, true, -1));
                    }
                }

                break;
        }

        return result.Items.Count == 0 ? null : result;
    }
}

/// <summary>
/// The parallel flows (https://drafts.csswg.org/css-break-3/#parallel-flows) of the contents of a box of a
/// <see cref="FragmentationFlow"/>: the items of a row flex container, of a grid or of a table row, or the lines of a multi-line
/// column flex container. The tracks (lines, rows) follow each other in the flow of the box; the items of a track are side by
/// side: each one is fragmented independently in its own flow, and resumes at the start of the next fragmentainer.
/// </summary>
internal sealed class ParallelGroup(BoxFragment container, double[] trackStarts, double[] trackEnds)
{
    /// <summary>The box whose contents are the parallel flows.</summary>
    public BoxFragment Container { get; } = container;

    /// <summary>The block-start edges of the tracks in the strip.</summary>
    public double[] TrackStarts { get; } = trackStarts;

    /// <summary>The block-end edges of the tracks in the strip.</summary>
    public double[] TrackEnds { get; } = trackEnds;

    public double Start => TrackStarts[0];

    public List<ParallelItem> Items { get; } = [];

    /// <summary>The parallel flow of each fragment of the items.</summary>
    public Dictionary<Fragment, ParallelItem> ItemsByFragment { get; } = [];

    /// <summary>The items that start in each track.</summary>
    public List<ParallelItem>[] StartingItems { get; private set; } = [];

    /// <summary>The items that end in each track (the track grows with them).</summary>
    public List<ParallelItem>[] EndingItems { get; private set; } = [];

    /// <summary>Whether an ancestor has <c>break-inside: avoid</c>.</summary>
    public bool Avoid { get; init; }

    /// <summary>The number of ancestors with <c>break-inside: avoid</c> (see <see cref="BreakCandidate.AvoidDepth"/>).</summary>
    public int AvoidDepth { get; init; }

    /// <summary>The block-start edges repeated at the start of the next fragmentainer by the boxes that contain the group (see <see cref="BreakCandidate.CloneStartEdges"/>).</summary>
    public double CloneStartEdges => Math.Max(0, CloneStart - (Repeated is { IsHeaderDisabled: true } repeated ? repeated.HeaderSize : 0) - (CloneOwner?.GetEndedCloneEdges().Start ?? 0));

    /// <summary>The block-end edges repeated before a break by the boxes that contain the group (see <see cref="BreakCandidate.CloneEndEdges"/>).</summary>
    public double CloneEndEdges => Math.Max(0, CloneEnd - (Repeated is { IsFooterDisabled: true } repeated ? repeated.FooterSize : 0) - (CloneOwner?.GetEndedCloneEdges().End ?? 0));

    public double CloneStart { get; init; }

    public double CloneEnd { get; init; }

    /// <summary>The repeated header and footer of the nearest table that contains the group (their block sizes are in <see cref="CloneStart"/> and <see cref="CloneEnd"/>), or null.</summary>
    public RepeatedSections? Repeated { get; init; }

    /// <summary>Whether the tracks grow with their items (the block size of the box is automatic).</summary>
    public bool CanGrow { get; init; }

    /// <summary>
    /// The block-start edges that the box of the group repeats at the breaks inside it (<c>box-decoration-break: clone</c> on a
    /// box whose block size does not follow its contents, see <see cref="FragmentationFlow.GetDefiniteBox"/>): they are not
    /// repeated before the contents that overflow the box after its last fragment.
    /// </summary>
    public double OwnCloneStart { get; init; }

    /// <summary>The block-end edges that the box of the group repeats at the breaks inside it (see <see cref="OwnCloneStart"/>).</summary>
    public double OwnCloneEnd { get; init; }

    /// <summary>Whether the edges that the box repeats are taken from its block size (<c>box-sizing: border-box</c>), else they are added to it.</summary>
    public bool ShrinksAtBreaks { get; init; }

    /// <summary>
    /// The block-end border and padding of the box of the group when it does not repeat them: as in Blink, there is no break
    /// inside them (the box breaks before them when they do not fit).
    /// </summary>
    public double BlockEndEdges { get; init; }

    /// <summary>The nearest box around the group that repeats its edges until it ends (see <see cref="OwnCloneStart"/>; its edges are in <see cref="CloneStart"/> and <see cref="CloneEnd"/>), or null.</summary>
    public ParallelGroup? CloneOwner { get; init; }

    /// <summary>
    /// The edges of the boxes around the contents of the box of the group that are not repeated any more in the current
    /// fragmentainer (the state of the breaker): the boxes that repeat their edges until they end (this one and the ones
    /// around it, see <see cref="OwnCloneStart"/>) and end in the fragmentainer or before it. As in Blink, the contents that
    /// overflow such a box are placed over its block-end edges in its last fragment (they are not repeated edges), and without
    /// any edge in the next fragmentainers.
    /// </summary>
    public (double Start, double End) GetEndedCloneEdges()
    {
        var (start, end) = CloneOwner?.GetEndedCloneEdges() ?? default;
        return HasEnded ? (start + OwnCloneStart, end + OwnCloneEnd) : (start, end);
    }

    /// <summary>Whether the box of the group, which repeats its edges until it ends, ends in the current fragmentainer or before it (the state of the breaker).</summary>
    public bool HasEnded { get; set; }

    // The state of the breaker
    public int Generation { get; set; }

    /// <summary>The number of tracks whose items are all placed.</summary>
    public int ClosedTracks { get; set; }

    public void Add(ParallelItem item, Fragment fragment)
    {
        item.Group = this;
        item.Flow.OwnerItem = item;
        Items.Add(item);
        ItemsByFragment[fragment] = item;
    }

    public void Complete()
    {
        StartingItems = new List<ParallelItem>[TrackStarts.Length];
        EndingItems = new List<ParallelItem>[TrackStarts.Length];
        for (var i = 0; i < TrackStarts.Length; i++)
        {
            StartingItems[i] = [];
            EndingItems[i] = [];
        }

        foreach (var item in Items)
        {
            StartingItems[item.StartTrack].Add(item);
            EndingItems[item.EndTrack].Add(item);
        }
    }
}

/// <summary>A parallel flow of a <see cref="ParallelGroup"/>: an item (or a line of a column flex container) and its contents.</summary>
internal sealed class ParallelItem(FragmentationFlow flow)
{
    /// <summary>The possible breaks of the contents of the item.</summary>
    public FragmentationFlow Flow { get; } = flow;

    public ParallelGroup? Group { get; set; }

    /// <summary>The box of the item, or null for a line of a column flex container.</summary>
    public BoxFragment? Box { get; init; }

    /// <summary>The index of the line of a multi-line column flex container (in the order of its layout), else -1.</summary>
    public int Line { get; init; } = -1;

    /// <summary>The content of the item when it is a nested multi-column container: the breaker places its rows of columns and its spanners itself (see <see cref="NestedColumns"/>).</summary>
    public NestedColumns? Nested { get; init; }

    /// <summary>The block-start edge of the border box in the strip.</summary>
    public double Start { get; init; }

    /// <summary>The block-end edge of the border box in the strip.</summary>
    public double End { get; init; }

    /// <summary>The block-end edge the border box would have with its automatic block size (a stretched item is taller).</summary>
    public double IntrinsicEnd { get; set; }

    public double MarginEnd { get; init; }

    public int StartTrack { get; init; }

    public int EndTrack { get; init; }

    /// <summary>The appeal of a break before a nested multi-column container: a row of the container whose columns break with a worse appeal is moved to the next fragmentainer.</summary>
    public BreakAppeal BreakBeforeAppeal { get; init; } = BreakAppeal.Perfect;

    /// <summary>Whether the block-end edge of a nested multi-column container is not before its position in the flow of its container (it has a minimum block size).</summary>
    public bool HasMinimumEnd { get; init; }

    /// <summary>Whether the item is stretched to the block size of its track: it ends with it.</summary>
    public bool Stretches { get; init; }

    /// <summary>Whether the block size of the box follows its contents when they are pushed by the breaks, else its block-end edge is placed by the flow of the container.</summary>
    public bool CanGrow { get; set; }

    /// <summary>
    /// Whether the item is a subgrid in the block axis: its contents end with its rows (<see cref="IntrinsicEnd"/>), and, as in
    /// Blink, its block-end edge is not after them (it is shorter than its area when a gutter of its rows disappears at a break).
    /// </summary>
    public bool IsRowSubgrid { get; set; }

    public bool IsMonolithic { get; set; }

    /// <summary>Whether the flow is the contents of a box of the flow of its container whose block size does not follow them (see <see cref="FragmentationFlow.GetDefiniteBox"/>): the flow of the container breaks beside the contents that overflow the box.</summary>
    public bool IsDefiniteBox { get; init; }

    /// <summary>The block-end edge of the first content of the box when it is monolithic and nothing precedes it, else NaN: it is placed with the box when the box fits in the fragmentainer.</summary>
    public double LeadingMonolithicEnd { get; set; } = double.NaN;

    /// <summary>Whether a break inside the item violates <c>break-inside: avoid</c>.</summary>
    public bool Avoid { get; set; }

    /// <summary>Whether the box is shorter than its contents in the strip, and as tall as them once fragmented (<see cref="IntrinsicEnd"/> is after <see cref="End"/>): an item of a column flex container.</summary>
    public bool EncompassesContents { get; set; }

    /// <summary>Whether the track waits for the end of the item (not for a cell that spans several rows).</summary>
    public bool CountsForTrack { get; init; }

    /// <summary>Whether the item starts after the start of its track: it can be pushed to the next fragmentainer without its track.</summary>
    public bool CanBreakBefore { get; init; }

    /// <summary>Whether the item is pushed without its track only when it cannot be broken (see <see cref="CanBreakBefore"/>): an item of a grid.</summary>
    public bool BreaksBeforeAsLastResort { get; init; }

    // The state of the breaker
    public int Generation { get; set; }

    /// <summary>The breaks of the flow, once it has started.</summary>
    public FlowMap? Map { get; set; }

    public object? Cursor { get; set; }

    /// <summary>Whether the block-end edge of the box is placed.</summary>
    public bool IsBoxDone { get; set; }

    /// <summary>Whether the contents of the box, up to its automatic block size, are placed.</summary>
    public bool IsContentPlaced { get; set; }

    /// <summary>The appeal of the break of the flow in the current fragmentainer.</summary>
    public BreakAppeal Appeal { get; set; }

    /// <summary>Whether all the contents of the flow are placed.</summary>
    public bool IsFlowDone { get; set; }

    /// <summary>The fragmentainer where the box ends and the offset of its block-end edge in it.</summary>
    public int EndIndex { get; set; }

    public double EndOffset { get; set; }

    /// <summary>The placement of an absolutely positioned box that is fragmented apart (see <see cref="FragmentationFlow.OutOfFlowItems"/>), else null.</summary>
    public OutOfFlowPlacement? OutOfFlow { get; set; }

    /// <summary>The last fragmentainer the flow was advanced in.</summary>
    public int AdvancedIndex { get; set; }

    /// <summary>Whether the flow of the item was broken in the current fragmentainer by a slice of its monolithic content (the state of the breaker).</summary>
    public bool IsMonolithicSlice { get; set; }

    /// <summary>The offset in the current fragmentainer of the end of the monolithic contents of a box that overflow it (see <see cref="IsDefiniteBox"/>), or of the end of an item that overflows it (the item ends there; the other items of its track do not), else 0.</summary>
    public double OverflowExtent { get; set; }
}

/// <summary>The containing block of an absolutely positioned box that is fragmented apart (see <see cref="FragmentationFlow.OutOfFlowItems"/>).</summary>
internal sealed class OutOfFlowPlacement
{
    /// <summary>The parallel flow whose contents include the contents of the containing block, or null for the flow of the strip: its breaks give the fragmentainers of the containing block.</summary>
    public ParallelItem? Owner => OwnerFlow?.OwnerItem;

    /// <summary>The flow that contains the contents of the containing block (its <see cref="FragmentationFlow.OwnerItem"/> is set once its contents are collected).</summary>
    public FragmentationFlow? OwnerFlow { get; init; }

    /// <summary>The fragment of the containing block in the strip.</summary>
    public required BoxFragment Container { get; init; }

    /// <summary>The block-start edge of the border box of the containing block in the strip.</summary>
    public double ContainerStart { get; init; }

    /// <summary>The block-end edge of the border box of the containing block in the strip.</summary>
    public double ContainerEnd { get; init; }

    /// <summary>The portion of the strip (the content between two column spanners) where the containing block starts.</summary>
    public int ContainerPortion { get; init; }

    /// <summary>The portion of the strip where the containing block ends (after the column spanners it contains).</summary>
    public int ContainerEndPortion { get; init; }

    /// <summary>Whether the block size of the containing block does not follow its contents.</summary>
    public bool IsContainerDefinite { get; init; }

    /// <summary>Whether the block size of the box is specified (it does not follow its contents).</summary>
    public bool IsDefinite { get; init; }

    /// <summary>
    /// The share of the block size that the fragments of the containing block gain on the containing block of the strip (its
    /// contents are pushed by the breaks) that is before the box: 1 for a box placed from the block-end edge of its containing
    /// block (<c>bottom</c> without <c>top</c>, a static position aligned to the end), 0.5 for a centered static position, 0 for
    /// a box placed from the block-start edge.
    /// </summary>
    public double EndRatio { get; init; }

    /// <summary>
    /// The block-start margin of a box placed at its static position in a block container: the static position follows the
    /// breaks of the content, and the margin is applied in its fragmentainer (as in Blink). Else 0.
    /// </summary>
    public double StaticMargin { get; init; }

    /// <summary>Whether the box is placed from a position of the content of its containing block (its static position, a grid area), else from the edges of the containing block.</summary>
    public bool IsPlacedInContent { get; init; }

    /// <summary>Whether the box is placed at its static position in the block axis, in a block container (see <see cref="StaticMargin"/>).</summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// Whether the block size of the box is the one of its containing block less its insets (both insets are set and its block
    /// size is automatic): it follows the block size of the fragments of the containing block stitched together.
    /// </summary>
    public bool IsStretched { get; init; }

    /// <summary>Whether the box is placed at its static position in the inline axis.</summary>
    public bool IsInlineStatic { get; init; }

    /// <summary>
    /// The distance in the inline axis between the static position of the box in the columns of a nested multi-column container
    /// that the breaker placed and the one the box was laid out from (see <c>ColumnBreaker.TryGetNestedStaticPosition</c>): its
    /// fragments are moved by it.
    /// </summary>
    public double InlineShift { get; set; }

    /// <summary>The block-end edge of the nearest ancestor that clips its overflow (the contents after it are not fragmented), or +∞.</summary>
    public double ClipEnd { get; init; } = double.PositiveInfinity;

    /// <summary>The fragmentainer where the breaker placed the block-start edge of the box, and its offset in it.</summary>
    public int StartIndex { get; set; }

    public double StartOffset { get; set; }
}
