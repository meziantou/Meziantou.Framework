namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The flex layout algorithm (https://drafts.csswg.org/css-flexbox-1/#layout-algorithm) of the contents of one flex container:
/// lays out its flex items in its content box, which <see cref="FormattingContext.Layout"/> sized. The lines of a balanced
/// container (<c>flex-wrap: balance</c>, https://drafts.csswg.org/css-flexbox-2/#algo-line-break) are chosen by
/// <see cref="FlexLineBalancer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm works in the logical axes of the container (its writing mode): the main axis is its inline axis for a row
/// container, its block axis for a column container. The sizes of the items are border-box sizes; their margins are kept in
/// the flex-relative directions (main-start, main-end, cross-start, cross-end), the positions are computed from the
/// main-start and cross-start edges, then converted to the logical coordinates of the container (the reverse directions and
/// <c>wrap-reverse</c> flip them).
/// </para>
/// <para>
/// The items are laid out by their own formatting context with the sizes the algorithm gives them: the main size and the
/// stretched cross size are imposed by <see cref="ConstraintSpace.FixedInlineSize"/>/<see cref="ConstraintSpace.FixedBlockSize"/>
/// (in the axes of the container, so the orthogonal items work the same way), and an <c>auto</c> cross size in the inline
/// axis of an item is its fit-content size (<see cref="ConstraintSpace.AutoInlineSizeIsFitContent"/>). The content-based main
/// size of an item in its inline axis comes from its intrinsic sizes (<see cref="AxisSizing"/>, cached for the layout pass),
/// in its block axis from a measure of the item (<see cref="LayoutContext.MeasureIntrinsicBlockSize"/>, cached for the layout
/// pass; a measured flex container does not lay out its items, it measures them). The last layout of each item is reused
/// when the next one has the same constraint space.
/// </para>
/// </remarks>
internal sealed partial class FlexLayoutAlgorithm
{
    private const double Epsilon = 1e-7;

    /// <summary>The tolerance of the collection of the items into lines: the layout unit of the browsers.</summary>
    private const double FitTolerance = 1d / 64;

    private readonly LayoutContext _context;
    private readonly ContainerBox _box;
    private readonly ContentArea _content;
    private readonly FlexContainerProperties _properties;
    private readonly WritingDirection _writingDirection;
    private readonly bool _isRow;

    /// <summary>Whether the main axis is the horizontal axis.</summary>
    private readonly bool _mainIsHorizontal;

    /// <summary>The definite inner main size of the container, or null (a column container with an indefinite block size).</summary>
    private readonly double? _definiteMainSize;

    /// <summary>The definite inner cross size of the container, or null (a row container with an indefinite block size).</summary>
    private readonly double? _definiteCrossSize;

    /// <summary>
    /// The definite cross size that the line of a single-line container and its stretched items take: null when the container is
    /// laid out to find its automatic block size (<see cref="ContentArea.MeasuresAutomaticBlockSize"/>), the lines then keep the
    /// cross size of their items.
    /// </summary>
    private readonly double? _lineDefiniteCrossSize;

    /// <summary>The constraint space of the items: the content box of the container is their containing block.</summary>
    private readonly ConstraintSpace _itemSpace;

    private readonly double _mainGap;
    private readonly double _crossGap;

    /// <summary>Whether the main sizes of the items are indefinite for their contents (a column container whose main size is indefinite).</summary>
    private readonly bool _mainSizeIsIndefinite;

    /// <summary>Whether the inline size of the content box is indefinite (see the constructor).</summary>
    private readonly bool _isInlineSizeIndefinite;

    /// <summary>Whether the container is sized under a min-content constraint (see the constructor).</summary>
    private readonly bool _isMinContentConstraint;
    private readonly List<FlexItem> _items = [];
    private readonly List<FlexLine> _lines = [];
    private List<(ContainerBox Box, int Index)>? _absolutelyPositioned;

    /// <summary>The used inner main size of the container.</summary>
    private double _mainSize;

    /// <summary>The used inner cross size of the container.</summary>
    private double _crossSize;

    /// <param name="context">The layout pass.</param>
    /// <param name="box">The flex container.</param>
    /// <param name="content">The content box of the container.</param>
    /// <param name="isInlineSizeIndefinite">
    /// Whether the inline size of the content box is indefinite (the measure of the intrinsic inline size of a multi-line column
    /// container, see <see cref="ComputeColumnLinesCrossSize"/>): the percentages of the items resolve against zero or behave
    /// as <c>auto</c>, and their <c>auto</c> inline sizes are their max-content sizes.
    /// </param>
    /// <param name="isMinContentConstraint">
    /// Whether the container is sized under a min-content constraint (its min-content inline size is computed): the content-based
    /// flex base size of the items is then their min-content size (https://drafts.csswg.org/css-flexbox-1/#algo-main-item, C).
    /// </param>
    public FlexLayoutAlgorithm(LayoutContext context, ContainerBox box, in ContentArea content, bool isInlineSizeIndefinite = false, bool isMinContentConstraint = false)
    {
        _isMinContentConstraint = isMinContentConstraint;
        _context = context;
        _box = box;
        _content = content;
        _properties = FlexContainerProperties.Get(box);
        _writingDirection = content.WritingDirection;
        _isRow = _properties.IsRow;
        _mainIsHorizontal = _isRow == _writingDirection.IsHorizontal;
        _isInlineSizeIndefinite = isInlineSizeIndefinite;
        _definiteMainSize = _isRow ? content.InlineSize : content.BlockSize;
        _definiteCrossSize = _isRow ? content.BlockSize : isInlineSizeIndefinite ? null : content.InlineSize;
        _lineDefiniteCrossSize = _isRow && content.MeasuresAutomaticBlockSize ? null : _definiteCrossSize;

        // https://drafts.csswg.org/css-align-3/#gap-percent: the percentages resolve against the content box of the container
        // (against zero when its size is indefinite)
        _mainGap = _properties.GetMainGap(_isRow ? content.InlineSize : content.PercentageBlockSize);
        _crossGap = _properties.GetCrossGap(_isRow ? content.PercentageBlockSize : content.InlineSize);

        _itemSpace = new ConstraintSpace
        {
            WritingDirection = _writingDirection,
            AvailableInlineSize = isInlineSizeIndefinite ? double.PositiveInfinity : _isRow ? content.InlineSize : GetAvailableCrossSize(content.InlineSize),
            AvailableBlockSize = _isRow && content.BlockSize is { } blockSize ? GetAvailableCrossSize(blockSize) : content.BlockSize,
            PercentageInlineSize = content.InlineSize,
            PercentageBlockSize = content.PercentageBlockSize,
            IsInlineSizeIndefinite = isInlineSizeIndefinite,
        };

        // https://drafts.csswg.org/css-flexbox-1/#definite-sizes: "If the flex container has a definite main size, a flex item's
        // post-flexing main size is treated as definite"; otherwise the main size of the items of a column container is
        // indefinite for their contents, unless their flex basis is definite (as in the browsers)
        _mainSizeIsIndefinite = !_isRow && content.PercentageBlockSize is null;
    }

    /// <summary>
    /// Returns the available cross space of the items in a definite cross size of the content box
    /// (https://drafts.csswg.org/css-flexbox-2/#flex-line-count-property): "flex-line-count changes the available space in the
    /// cross axis for flex items: subtracting the size of N-1 gaps from the normal available space, then dividing the leftover
    /// by N, where N is the flex-line-count value" (the specified value, whatever the number of lines); "Percentages continue
    /// to resolve as normal".
    /// </summary>
    private double GetAvailableCrossSize(double crossSize)
    {
        var lineCount = _properties.LineCount;
        return lineCount > 1 ? Math.Max(0, (crossSize - _crossGap * (lineCount - 1)) / lineCount) : crossSize;
    }

    /// <summary>
    /// Returns the sum of the cross sizes of the lines of a multi-line column container whose block size is definite or limited
    /// by a definite maximum, or that is balanced on several lines (<c>flex-line-count</c>), and of the gaps between them: its
    /// intrinsic inline size (as in the browsers, the items are collected into lines in its block size, and each line is as
    /// wide as the largest max-content contribution of its items).
    /// </summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="box">The flex container.</param>
    /// <param name="contentBlockSize">The definite block size of the content box, or null.</param>
    /// <param name="maxContentBlockSize">The definite maximum block size of the content box, or null: the lines of a container with an indefinite block size break at it.</param>
    public static double ComputeColumnLinesCrossSize(LayoutContext context, ContainerBox box, double? contentBlockSize, double? maxContentBlockSize)
    {
        var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
        var content = new ContentArea(writingDirection, LogicalOffset.Zero, InlineSize: 0, contentBlockSize) { MaxBlockSize = maxContentBlockSize };
        var algorithm = new FlexLayoutAlgorithm(context, box, content, isInlineSizeIndefinite: true);
        algorithm.CollectItems();
        foreach (var item in algorithm._items)
        {
            algorithm.InitializeItem(item);
            algorithm.ComputeHypotheticalMainSize(item);
        }

        algorithm.LayoutLines(collapse: false);
        var size = algorithm._crossGap * Math.Max(0, algorithm._lines.Count - 1);
        foreach (var line in algorithm._lines)
        {
            size += line.CrossSize;
        }

        return size;
    }

    /// <summary>
    /// Returns the min-content or the max-content inline size of a row container (https://drafts.csswg.org/css-flexbox-1/#intrinsic-main-sizes):
    /// the size in which each item gets its min-content or max-content contribution once the flexible lengths are resolved. The
    /// desired flex fraction of each item is the difference between its contribution and its flex base size divided by its
    /// flex factor; the largest one is applied to all the items, whose sizes (clamped by their min and max main sizes) are
    /// summed. The min-content size of a multi-line container is the largest min-content contribution of its items. The
    /// max-content size of a balanced container with a <c>flex-line-count</c> above 1 is the largest of its balanced lines.
    /// </summary>
    /// <remarks>
    /// https://drafts.csswg.org/css-flexbox-1/#intrinsic-item-contributions: the contribution of an item is its intrinsic size
    /// contribution (<see cref="IntrinsicSizing.GetContribution"/>), "clamped by its flex base size as a maximum (if it is not
    /// growable) and/or as a minimum (if it is not shrinkable), and then further clamped by its min/max main size properties".
    /// The collapsed items take no space.
    /// </remarks>
    public static double ComputeRowIntrinsicSize(LayoutContext context, ContainerBox box, double? contentBlockSize, bool minContent)
    {
        var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
        var algorithm = new FlexLayoutAlgorithm(context, box, new ContentArea(writingDirection, LogicalOffset.Zero, InlineSize: 0, contentBlockSize), isInlineSizeIndefinite: true, isMinContentConstraint: minContent);
        algorithm.CollectItems();

        // https://drafts.csswg.org/css-flexbox-1/#definite-sizes: the cross size of the stretched items of a single-line container
        // with a definite cross size is definite (the percentages of their contents and their aspect ratio use it)
        var stretches = !algorithm._properties.IsMultiLine && contentBlockSize is not null;
        var largest = 0d;
        var count = 0;
        foreach (var item in algorithm._items)
        {
            algorithm.InitializeItem(item);
            if (item.IsCollapsed)
                continue;

            algorithm.ComputeHypotheticalMainSize(item);
            var contributions = IntrinsicSizing.GetContribution(context, item.Box, writingDirection, contentBlockSize, stretches && item.Stretches, isFlexItem: true);
            var contribution = (minContent ? contributions.MinContent : contributions.MaxContent) - item.OuterMainMargins;
            if (item.Grow == 0)
            {
                contribution = Math.Min(contribution, item.FlexBaseSize);
            }

            if (item.Shrink == 0)
            {
                contribution = Math.Max(contribution, item.FlexBaseSize);
            }

            // (the automatic minimum size of a compressible replaced element is limited by its min-content contribution, which its
            // cyclic percentage sizes make zero, https://drafts.csswg.org/css-sizing-3/#min-content-zero: as in Blink, WPT
            // css/css-flexbox/intrinsic-size/auto-min-size-001.html)
            var minimum = item.MinMainSize;
            if (item.HasAutomaticMinimumSize && IntrinsicSizing.IsCompressibleReplacedElement(item.Box))
            {
                minimum = Math.Max(item.MainPaddingBorder, Math.Min(minimum, contributions.MinContent - item.OuterMainMargins));
            }

            contribution = Math.Max(minimum, Math.Min(contribution, item.MaxMainSize));
            item.TargetMainSize = contribution;
            largest = Math.Max(largest, contribution + item.OuterMainMargins);
            count++;
        }

        if (count == 0)
            return 0;

        if (minContent && algorithm._properties.IsMultiLine)
            return largest;

        // https://drafts.csswg.org/css-flexbox-2/#flex-line-count-property: a balanced container has at least flex-line-count
        // lines, also in an unlimited main size: its max-content size is its largest line (as in Blink, the lines are the ones of
        // the outer hypothetical main sizes of the items; WPT css/css-flexbox/balance/balance-line-count-intrinsic-001.html)
        if (algorithm._properties.IsBalanced && algorithm._properties.LineCount > 1 && algorithm.GetBalancedLines(double.PositiveInfinity, collapse: true) is { } ends)
        {
            var size = 0d;
            var start = 0;
            foreach (var end in ends)
            {
                size = Math.Max(size, algorithm.ComputeLineIntrinsicSize(start, end));
                start = end;
            }

            return size;
        }

        return algorithm.ComputeLineIntrinsicSize(0, algorithm._items.Count);
    }

    /// <summary>
    /// Returns the intrinsic main size of a line of a row container (see <see cref="ComputeRowIntrinsicSize"/>), from the
    /// contributions of its items (their <see cref="FlexItem.TargetMainSize"/>).
    /// </summary>
    private double ComputeLineIntrinsicSize(int start, int end)
    {
        var chosenFraction = double.NegativeInfinity;
        var sumGrow = 0d;
        var total = 0d;
        var count = 0;
        for (var i = start; i < end; i++)
        {
            var item = _items[i];
            if (item.IsCollapsed)
                continue;

            var contribution = item.TargetMainSize;
            total += contribution + item.OuterMainMargins;
            count++;

            // 1. "For each flex item, subtract its outer flex base size from its max-content contribution size. If that result is
            // positive, divide it by the item's flex grow factor if the flex grow factor is >= 1, or multiply it by the flex grow
            // factor if the flex grow factor is < 1; if the result is negative, divide it by the item's scaled flex shrink factor
            // (if dividing by zero, treat the result as negative infinity). This is the item's desired flex fraction."
            var difference = contribution - item.FlexBaseSize;
            var scaledShrink = item.Shrink * item.InnerFlexBaseSize;
            var fraction = difference > Epsilon ? (item.Grow >= 1 ? difference / item.Grow : difference * item.Grow)
                : difference < -Epsilon ? (scaledShrink > 0 ? difference / scaledShrink : double.NegativeInfinity)
                : 0;

            // 2. "Place all flex items into lines of infinite length. Within each line, find the greatest (most positive)
            // desired flex fraction among all the flex items. This is the line's chosen flex fraction."
            chosenFraction = Math.Max(chosenFraction, fraction);
            sumGrow += item.Grow;
        }

        if (count == 0)
            return 0;

        // The items shrink (or keep their flex base size) to get their contributions: as in Blink, the container is as large
        // as the sum of the contributions (an item that cannot shrink contributes its flex base size); the specification
        // applies the chosen flex fraction to every item instead, which leaves more space than the contributions need
        // (WPT css/css-flexbox/intrinsic-size/row-005.html)
        var gaps = _properties.GetMainGap(percentageBasis: 0) * (count - 1);
        if (!(chosenFraction > 0))
            return total + gaps;

        // 3. "If the chosen flex fraction is positive, and the sum of the line's flex grow factors is less than 1, divide the
        // chosen flex fraction by that sum."
        if (sumGrow < 1)
        {
            chosenFraction /= sumGrow;
        }

        // 4. "Add each item's flex base size to the product of its flex grow factor [...] and the chosen flex fraction, then
        // clamp that result by the max main size floored by the min main size."
        // 5. "The flex container's max-content size is the largest sum (among all the lines) of the afore-calculated sizes of all
        // items within a single line." (https://drafts.csswg.org/css-align-3/#gap-percent: a percentage gap resolves against zero)
        var size = gaps;
        for (var i = start; i < end; i++)
        {
            var item = _items[i];
            if (item.IsCollapsed)
                continue;

            var flexed = item.Grow == 0 ? 0 : item.Grow * chosenFraction;
            size += item.ClampMainSize(item.FlexBaseSize + flexed) + item.OuterMainMargins;
        }

        return size;
    }

    /// <summary>Lays out the items, adds their fragments to <paramref name="builder"/> and returns the block size of the contents and their baselines.</summary>
    public ContentLayoutResult Layout(FragmentBuilder builder)
    {
        CollectItems();
        var hasCollapsedItems = false;
        foreach (var item in _items)
        {
            InitializeItem(item);
            ComputeHypotheticalMainSize(item);
            hasCollapsedItems |= item.IsCollapsed;
        }

        // The measure of the intrinsic block size of a container only needs the main sizes of the lines of a column container, the
        // cross sizes of the lines of a row container: the items are measured, not laid out (the measures are cached for the
        // layout pass, so the cost of nested flex containers stays linear)
        if (_content.MeasuresIntrinsicBlockSize)
        {
            if (_isRow)
            {
                CollectLines(collapse: false);
                foreach (var line in _lines)
                {
                    ResolveFlexibleLengths(line, collapse: false);
                }

                foreach (var item in _items)
                {
                    MeasureHypotheticalCrossSize(item);
                }

                ComputeLineCrossSizes(collapse: false);
            }
            else
            {
                CollectLines(collapse: false);
            }

            return new ContentLayoutResult(ComputeContentBlockSize());
        }

        // https://drafts.csswg.org/css-flexbox-1/#visibility-collapse: the layout is done once with the collapsed items as
        // normal items, to get the cross size of their lines (their struts), then again with zero main size for them
        LayoutLines(collapse: false);

        if (hasCollapsedItems)
        {
            // (the struts are noted after the lines are stretched by align-content: "Handle 'align-content: stretch'" precedes
            // "Collapse visibility:collapse items" in the algorithm)
            AlignLines();
            foreach (var line in _lines)
            {
                for (var i = line.Start; i < line.End; i++)
                {
                    if (_items[i].IsCollapsed)
                    {
                        _items[i].Strut = line.CrossSize;
                    }
                }
            }

            LayoutLines(collapse: true);
        }

        AlignLines();
        StretchItems();
        AlignItemsInMainAxis();
        AlignItemsInCrossAxis();
        return PlaceItems(builder);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Items
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#flex-items: the in-flow children of the container (the box tree wraps its text
    /// in anonymous items), in order-modified document order (https://drafts.csswg.org/css-display/#order-modified-document-order).
    /// The absolutely positioned children are not items (https://drafts.csswg.org/css-flexbox-1/#abspos-items).
    /// </summary>
    private void CollectItems()
    {
        var index = 0;
        var needsSort = false;
        var previousOrder = int.MinValue;
        foreach (var child in _box.Children)
        {
            if (child is not ContainerBox childBox || childBox.IsOutsideMarker)
                continue;

            index++;
            if (PositionedLayout.IsAbsolutelyPositioned(childBox))
            {
                (_absolutelyPositioned ??= []).Add((childBox, index));
                continue;
            }

            var layout = childBox.Style.Layout;
            var order = _properties.GetOrder(layout);
            needsSort |= order < previousOrder;
            previousOrder = order;
            _items.Add(new FlexItem(childBox, layout, index, order));
        }

        if (needsSort)
        {
            // A stable sort: the items with the same order keep the document order
            _items.Sort(static (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));
        }
    }

    /// <summary>Resolves the margins, the paddings and borders, the flex factors and the alignment of an item.</summary>
    private void InitializeItem(FlexItem item)
    {
        var layout = item.Style;
        var boxModel = layout.Box;
        item.WritingDirection = WritingDirection.FromStyle(layout);
        item.MainIsInline = item.WritingDirection.IsHorizontal == _mainIsHorizontal;
        item.IsCollapsed = layout.Visibility == Visibility.Collapse;
        item.Grow = Math.Max(0, _properties.GetGrow(layout));
        item.Shrink = Math.Max(0, _properties.GetShrink(layout));

        // https://drafts.csswg.org/css-flexbox-1/#item-margins: the percentages of the margins and paddings of the items resolve
        // against the inline size of their containing block, the content box of the container
        var margins = _writingDirection.ToLogical(FormattingContext.ResolveMargins(boxModel, _content.InlineSize));
        var auto = _writingDirection.ToLogical(new Sides<bool>(boxModel.Margin.Top.IsAuto, boxModel.Margin.Right.IsAuto, boxModel.Margin.Bottom.IsAuto, boxModel.Margin.Left.IsAuto));
        var (mainStart, mainEnd, crossStart, crossEnd) = ToFlexRelative(margins);
        var (autoMainStart, autoMainEnd, autoCrossStart, autoCrossEnd) = ToFlexRelative(auto);
        item.MarginMainStart = autoMainStart ? 0 : mainStart;
        item.MarginMainEnd = autoMainEnd ? 0 : mainEnd;
        item.MarginCrossStart = autoCrossStart ? 0 : crossStart;
        item.MarginCrossEnd = autoCrossEnd ? 0 : crossEnd;
        item.AutoMarginMainStart = autoMainStart;
        item.AutoMarginMainEnd = autoMainEnd;
        item.AutoMarginCrossStart = autoCrossStart;
        item.AutoMarginCrossEnd = autoCrossEnd;

        var paddingAndBorder = FormattingContext.ResolvePadding(boxModel, _content.InlineSize).Add(boxModel.BorderWidth);
        var gutters = IntrinsicSizing.GetScrollbarGutters(_context, item.Box, item.WritingDirection);
        item.MainPaddingBorder = _mainIsHorizontal ? paddingAndBorder.HorizontalSum() : paddingAndBorder.VerticalSum();
        item.CrossPaddingBorder = _mainIsHorizontal ? paddingAndBorder.VerticalSum() : paddingAndBorder.HorizontalSum();
        item.MainGutters = item.MainIsInline ? gutters.InlineSum() : gutters.BlockSum();
        item.CrossGutters = item.MainIsInline ? gutters.BlockSum() : gutters.InlineSum();

        // https://drafts.csswg.org/css-flexbox-1/#cross-alignment: "If a flex item has auto margins in the cross axis, then
        // align-self has no effect"
        var alignSelf = _properties.GetAlignSelf(layout);
        item.Safe = alignSelf.Overflow == OverflowAlignment.Safe;
        item.Align = autoCrossStart || autoCrossEnd ? AxisAlignment.Start : ResolveSelfAlignment(item, alignSelf.Keyword);

        // https://drafts.csswg.org/css-flexbox-1/#valdef-align-items-stretch: "If the cross size property of the flex item
        // computes to auto, and neither of the cross-axis margins are auto, the flex item is stretched"; otherwise it is
        // aligned as flex-start
        // A cross size of stretch fills the flex line, as align-self: stretch does (https://github.com/w3c/csswg-drafts/issues/11784)
        var preferredCrossSize = boxModel.GetSize(!_mainIsHorizontal);
        if (preferredCrossSize.Kind == SizeKind.Stretch && !autoCrossStart && !autoCrossEnd)
        {
            item.Align = AxisAlignment.Stretch;
        }

        if (item.Align == AxisAlignment.Stretch)
        {
            item.Stretches = preferredCrossSize.IsAuto || preferredCrossSize.Kind == SizeKind.Stretch;
            if (!item.Stretches)
            {
                item.Align = AxisAlignment.Start;
            }
        }
    }

    /// <summary>Converts logical sides (in the writing mode of the container) to the flex-relative directions.</summary>
    private (T MainStart, T MainEnd, T CrossStart, T CrossEnd) ToFlexRelative<T>(LogicalSides<T> sides)
    {
        var (mainLogicalStart, mainLogicalEnd) = _isRow ? (sides.InlineStart, sides.InlineEnd) : (sides.BlockStart, sides.BlockEnd);
        var (crossLogicalStart, crossLogicalEnd) = _isRow ? (sides.BlockStart, sides.BlockEnd) : (sides.InlineStart, sides.InlineEnd);
        return (
            _properties.IsMainReverse ? mainLogicalEnd : mainLogicalStart,
            _properties.IsMainReverse ? mainLogicalStart : mainLogicalEnd,
            _properties.IsCrossReverse ? crossLogicalEnd : crossLogicalStart,
            _properties.IsCrossReverse ? crossLogicalStart : crossLogicalEnd);
    }

    /// <summary>Converts sides in the flex-relative directions to logical sides in the writing mode of the container.</summary>
    private LogicalSides<double> FromFlexRelative(double mainStart, double mainEnd, double crossStart, double crossEnd)
    {
        var (mainLogicalStart, mainLogicalEnd) = _properties.IsMainReverse ? (mainEnd, mainStart) : (mainStart, mainEnd);
        var (crossLogicalStart, crossLogicalEnd) = _properties.IsCrossReverse ? (crossEnd, crossStart) : (crossStart, crossEnd);
        return _isRow
            ? new LogicalSides<double>(crossLogicalStart, mainLogicalEnd, crossLogicalEnd, mainLogicalStart)
            : new LogicalSides<double>(mainLogicalStart, crossLogicalEnd, mainLogicalEnd, crossLogicalStart);
    }

    /// <summary>
    /// Returns the sizing of an item in its main or cross axis: its sizing properties resolve against the content box of the
    /// container (https://drafts.csswg.org/css-flexbox-1/#definite-sizes: a percentage of an indefinite size behaves as <c>auto</c>).
    /// </summary>
    private AxisSizing CreateSizing(FlexItem item, bool main)
    {
        var horizontal = main == _mainIsHorizontal;
        var basis = horizontal == _writingDirection.IsHorizontal ? (_isInlineSizeIndefinite ? null : _content.InlineSize) : _content.PercentageBlockSize;
        var paddingAndBorder = main ? item.MainPaddingBorder : item.CrossPaddingBorder;
        var margins = main ? item.MarginMainStart + item.MarginMainEnd : item.MarginCrossStart + item.MarginCrossEnd;
        return new AxisSizing(_context, item.Box)
        {
            Horizontal = horizontal,
            IsInlineAxis = item.WritingDirection.IsHorizontal == horizontal,
            PercentageBasis = basis,
            PaddingAndBorder = paddingAndBorder,
            Edges = paddingAndBorder + (main ? item.MainGutters : item.CrossGutters),
            StretchSize = basis is { } size ? Math.Max((main ? size : GetAvailableCrossSize(size)) - margins, paddingAndBorder) : null,
        };
    }

    /// <summary>
    /// Lays out an item with the given border-box sizes in the axes of the container (null: the size of the item is given by
    /// its own sizing properties, an <c>auto</c> inline size being its fit-content size). The last layout of the item is
    /// reused when the constraint space is the same.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="mainSize">The border-box main size, or null.</param>
    /// <param name="crossSize">The border-box cross size, or null.</param>
    /// <param name="measure">Whether the item is laid out to measure its intrinsic block size (in the main axis): its preferred block size behaves as <c>auto</c>.</param>
    private BoxFragment LayoutItem(FlexItem item, double? mainSize, double? crossSize, bool measure = false)
    {
        var space = CreateItemSpace(item, mainSize, crossSize, measure);
        if (item.Fragment is { } cached && item.Space == space)
            return cached;

        var fragment = _context.LayoutBox(item.Box, space);
        item.Fragment = fragment;
        item.Space = space;
        return fragment;
    }

    /// <summary>Returns the constraint space of a layout of an item (see <see cref="LayoutItem"/>).</summary>
    private ConstraintSpace CreateItemSpace(FlexItem item, double? mainSize, double? crossSize, bool measure, bool withoutAspectRatio = false)
    {
        var inlineSize = _isRow ? mainSize : crossSize;
        var blockSize = _isRow ? crossSize : mainSize;
        return _itemSpace with
        {
            FixedInlineSize = inlineSize,
            FixedBlockSize = blockSize,
            IsFixedBlockSizeIndefinite = _mainSizeIsIndefinite && mainSize is not null && !item.HasDefiniteFlexBasis,
            AutoInlineSizeIsFitContent = inlineSize is null,
            MeasuresIntrinsicBlockSize = measure,
            MeasuresContentsWithoutAspectRatio = withoutAspectRatio,
        };
    }

    /// <summary>Returns the border-box main size and cross size of a fragment of an item.</summary>
    private (double Main, double Cross) GetSizes(BoxFragment fragment)
    {
        var size = _writingDirection.ToLogical(fragment.Size);
        return _isRow ? (size.Inline, size.Block) : (size.Block, size.Inline);
    }

    /// <summary>
    /// Returns the content-based border-box main size of an item (its max-content size, or its min-content size), for its
    /// flex base size and its automatic minimum size: in its inline axis, its intrinsic inline sizes (or the size transferred
    /// from its definite cross size through its preferred aspect ratio); in its block axis, its automatic block size laid out
    /// with its definite cross size (else with its fit-content size, https://drafts.csswg.org/css-flexbox-1/#algo-main-item).
    /// </summary>
    private double GetContentMainSize(FlexItem item, in AxisSizing mainSizing, bool minContent)
    {
        if (item.MainIsInline)
        {
            var sizes = mainSizing.GetIntrinsicSizes();
            return minContent ? sizes.MinContent : sizes.MaxContent;
        }

        return _context.MeasureIntrinsicBlockSize(item.Box, CreateItemSpace(item, mainSize: null, item.DefiniteCrossSize, measure: true));
    }

    /// <summary>
    /// Returns the min-content border-box main size of the contents of a non-replaced item with a preferred aspect ratio, which
    /// floors its content size suggestion (https://drafts.csswg.org/css-flexbox-1/#content-size-suggestion): the size transferred
    /// from its cross size through the ratio has the automatic minimum size of the ratio-dependent axis
    /// (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum), so the item does not shrink below its contents (only the
    /// replaced elements have a transferred size suggestion, https://github.com/w3c/csswg-drafts/issues/6794).
    /// </summary>
    private double GetContentsMinContentMainSize(FlexItem item, in AxisSizing mainSizing)
    {
        if (item.MainIsInline)
            return mainSizing.GetContentIntrinsicSizes().MinContent;

        return _context.MeasureIntrinsicBlockSize(item.Box, CreateItemSpace(item, mainSize: null, item.DefiniteCrossSize, measure: true, withoutAspectRatio: true));
    }

    /// <summary>
    /// Returns the block size of the captions of a table item of a column container (with their margins), in the inline size of
    /// the table laid out with its definite cross size, else its fit-content size.
    /// </summary>
    private double GetTableCaptionsMainSize(FlexItem item)
    {
        var hasCaption = false;
        foreach (var child in item.Box.Children)
        {
            hasCaption |= child is ContainerBox { Display.Internal: DisplayInternal.TableCaption };
        }

        if (!hasCaption)
            return 0;

        var size = 0d;
        var fragment = LayoutItem(item, mainSize: null, item.DefiniteCrossSize);
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is BoxFragment { Box.Display.Internal: DisplayInternal.TableCaption } caption)
            {
                var margin = _writingDirection.ToLogical(caption.Margin);
                size += margin.BlockStart + _writingDirection.ToLogical(caption.Size).Block + margin.BlockEnd;
            }
        }

        return size;
    }

    /// <summary>
    /// Returns the definite border-box cross size of an item before the flex lines are sized, or null: its cross size property
    /// when it is definite, or the inner cross size of a single-line container with a definite cross size for a stretched item
    /// (https://drafts.csswg.org/css-flexbox-1/#definite-sizes), clamped by its min and max cross sizes.
    /// </summary>
    private double? ComputeDefiniteCrossSize(FlexItem item, in AxisSizing crossSizing)
    {
        // The min and max block sizes that depend on the contents are known once the item is laid out
        var boxModel = item.Style.Box;
        if (!crossSizing.IsInlineAxis && (boxModel.GetMinSize(!_mainIsHorizontal).IsContentBasedInBlockAxis || boxModel.GetMaxSize(!_mainIsHorizontal).IsContentBasedInBlockAxis))
            return null;

        // The intrinsic inline sizes of an item depend on its block size, its main size, which is not known yet (the percentage
        // block sizes of its contents): the layout of the item with its used main size resolves the intrinsic size keywords
        var preferred = boxModel.GetSize(!_mainIsHorizontal);
        if (crossSizing.IsInlineAxis && preferred.IsIntrinsic)
            return null;

        // The block size of a table applies to its grid box (https://drafts.csswg.org/css-tables-3/#computing-the-table-height):
        // the table layout adds its captions
        if (!crossSizing.IsInlineAxis && item.Box.Kind == ContainerBoxKind.TableWrapper && !(item.Stretches && preferred.BehavesAs == SizeKind.Auto))
            return null;

        if (preferred.Kind != SizeKind.Stretch && (crossSizing.IsInlineAxis || !preferred.IsContentBasedInBlockAxis) && crossSizing.Resolve(preferred, SizingProperty.Preferred) is { } size)
            return crossSizing.Clamp(size);

        if (item.Stretches && !_properties.IsMultiLine && _lineDefiniteCrossSize is { } innerCrossSize)
            return crossSizing.Clamp(innerCrossSize - item.MarginCrossStart - item.MarginCrossEnd);

        return null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#algo-main-item: determines the flex base size and the hypothetical main size of
    /// an item, with its min and max main sizes (and its automatic minimum size, https://drafts.csswg.org/css-flexbox-1/#min-size-auto).
    /// </summary>
    private void ComputeHypotheticalMainSize(FlexItem item)
    {
        var boxModel = item.Style.Box;
        var preferredMainSize = boxModel.GetSize(_mainIsHorizontal);
        var crossSizing = CreateSizing(item, main: false);
        var mainSizing = CreateSizing(item, main: true);

        // The preferred aspect ratio of the item (https://drafts.csswg.org/css-sizing-4/#aspect-ratio, the natural aspect ratio of
        // a replaced element), which transfers its definite cross size to its main axis, and its min and max sizes from one axis
        // to the other when the size of the other axis is auto (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers)
        var itemLogicalPaddingAndBorder = item.WritingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, _content.InlineSize).Add(boxModel.BorderWidth));
        var naturalRatio = item.Box.Kind == ContainerBoxKind.Replaced ? ReplacedContent.Get(_context, item.Box).NaturalSize.AspectRatio : null;
        var ratio = FormattingContext.GetPreferredAspectRatio(item.Box, item.WritingDirection.IsHorizontal, itemLogicalPaddingAndBorder, naturalRatio);
        item.CrossSizing = crossSizing;
        item.DefiniteCrossSize = ComputeDefiniteCrossSize(item, crossSizing);
        double? transferred = ratio is { } transfer && item.DefiniteCrossSize is { } definiteCross ? (item.MainIsInline ? transfer.ToInline(definiteCross) : transfer.ToBlock(definiteCross)) : null;

        if (item.MainIsInline)
        {
            // The definite block size is the input of the intrinsic inline sizes (percentage block sizes, orthogonal flows, aspect ratio)
            mainSizing = mainSizing with
            {
                ContentBlockSize = item.DefiniteCrossSize is { } blockSize ? Math.Max(0, blockSize - item.CrossPaddingBorder - item.CrossGutters) : null,
                TransferredSize = transferred,
            };
        }

        // The sizing properties of the block axis of a table apply to its grid box (https://drafts.csswg.org/css-tables-3/#computing-the-table-height):
        // its captions are added to its flex base size and to its minimum and maximum main sizes
        var isTable = item.Box.Kind == ContainerBoxKind.TableWrapper;
        var tableCaptionsSize = isTable && !item.MainIsInline ? GetTableCaptionsMainSize(item) : 0;

        // https://drafts.csswg.org/css-flexbox-1/#flex-basis-property: "auto: When specified on a flex item, the auto keyword
        // retrieves the value of the main size property as the used flex-basis"
        var basis = item.Style.Flex.Basis;
        var basisIsMainSize = basis.IsAuto;
        if (basisIsMainSize)
        {
            basis = preferredMainSize;
        }

        // A. "If the item has a definite used flex basis, that's the flex base size" (box-sizing applies; a percentage of an
        // indefinite main size and the content-based sizes of the block axis are content)
        double? flexBaseSize = null;
        if (basis.Kind == SizeKind.CalcSize)
        {
            // https://drafts.csswg.org/css-values-5/#calc-size: the size keyword is the used size of the basis: auto is the main
            // size property of a flex-basis (the content size for the main size property itself), content the content size
            var content = GetContentMainSize(item, mainSizing, minContent: false);
            var automatic = content;
            if (!basisIsMainSize && (mainSizing with { AutomaticSize = content, ContentSize = content }).Resolve(preferredMainSize, SizingProperty.Preferred) is { } mainSize)
            {
                automatic = mainSize;
            }

            var calcSizing = mainSizing with { AutomaticSize = automatic, ContentSize = basis.CalcSize?.BasisKind == SizeKind.Auto ? automatic : content };
            flexBaseSize = calcSizing.Resolve(basis, SizingProperty.Preferred);
        }
        else if (basis.Kind is not (SizeKind.Content or SizeKind.Auto) && (mainSizing.IsInlineAxis || !basis.IsContentBasedInBlockAxis))
        {
            flexBaseSize = mainSizing.Resolve(basis, SizingProperty.Preferred) + tableCaptionsSize;
            item.HasDefiniteFlexBasis = flexBaseSize is not null;
        }

        // B. "If the flex item has a preferred aspect ratio, the used flex basis is content, and the flex item has a definite
        // cross size, then the flex base size is calculated from its used cross size and the flex item's aspect ratio"
        // E. "Otherwise, size the item into the available space using its used flex basis in place of its main size, treating a
        // value of content as max-content" (the max-content size of a box with an aspect ratio is clamped by its min and max
        // cross sizes transferred through the ratio, https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers)
        // C. "If the used flex basis is content or depends on its available space, and the flex container is being sized under a
        // min-content or max-content constraint, size the item under that constraint" (an item that cannot shrink contributes
        // its min-content size to the min-content size of the container: WPT css/css-flexbox/intrinsic-size/row-001.html)
        if (flexBaseSize is null && transferred is not null)
        {
            // (the size transferred from a definite cross size is definite: https://drafts.csswg.org/css-sizing-4/#aspect-ratio-automatic)
            flexBaseSize = transferred;
            item.HasDefiniteFlexBasis = true;
        }

        flexBaseSize ??= ClampByTransferredCrossSizes(item, crossSizing, ratio, GetContentMainSize(item, mainSizing, minContent: _isMinContentConstraint));

        var minSize = boxModel.GetMinSize(_mainIsHorizontal);
        var maxSize = boxModel.GetMaxSize(_mainIsHorizontal);
        if (!mainSizing.IsInlineAxis && (minSize.IsContentBasedInBlockAxis || maxSize.IsContentBasedInBlockAxis || preferredMainSize.IsContentBasedInBlockAxis))
        {
            // The intrinsic size keywords of the block axis are the automatic block size of the item
            mainSizing = mainSizing with { ContentSize = GetContentMainSize(item, mainSizing, minContent: true) };
        }

        var max = mainSizing.Resolve(maxSize, SizingProperty.Maximum) + tableCaptionsSize;
        double min;
        if (minSize.IsAuto)
        {
            min = UsesAutomaticMinimumSize(item) ? ComputeAutomaticMinimumSize(item, mainSizing, crossSizing, ratio, preferredMainSize, max, tableCaptionsSize) : 0;
        }
        else if (minSize.Kind == SizeKind.CalcSize && minSize.BehavesAs == SizeKind.Auto)
        {
            // https://drafts.csswg.org/css-values-5/#calc-size: the size keyword of calc-size(auto, …) is the automatic minimum size
            var automaticMinimum = UsesAutomaticMinimumSize(item) ? ComputeAutomaticMinimumSize(item, mainSizing, crossSizing, ratio, preferredMainSize, max, tableCaptionsSize) : 0;
            min = (mainSizing with { AutomaticSize = automaticMinimum, ContentSize = automaticMinimum }).Resolve(minSize, SizingProperty.Minimum) ?? 0;
        }
        else
        {
            min = mainSizing.Resolve(minSize, SizingProperty.Minimum) + tableCaptionsSize ?? 0;
        }

        // A table is never smaller than its min-content size (https://drafts.csswg.org/css-tables-3/#computing-the-table-width:
        // "the used min-width of a table is the greater of the resolved min-width, CAPMIN, and GRIDMIN"; its rows are never
        // shorter than their contents either), whatever its minimum main size
        if (isTable)
        {
            min = Math.Max(min, GetContentMainSize(item, mainSizing, minContent: true));
        }

        item.FlexBaseSize = flexBaseSize.Value;
        item.HasAutomaticMinimumSize = minSize.IsAuto;
        item.MinMainSize = Math.Max(min, item.MainPaddingBorder);
        item.MaxMainSize = Math.Max(max ?? double.PositiveInfinity, item.MainPaddingBorder);

        // "The hypothetical main size is the item's flex base size clamped according to its used min and max main sizes (and
        // flooring the content box size at zero)"
        item.HypotheticalMainSize = item.ClampMainSize(item.FlexBaseSize);
    }

    /// <summary>Clamps a content-based main size of an item with a preferred aspect ratio by its definite min and max cross sizes converted through the ratio.</summary>
    private double ClampByTransferredCrossSizes(FlexItem item, in AxisSizing crossSizing, AspectRatioTransfer? ratio, double size)
    {
        if (ratio is not { } transfer)
            return size;

        var boxModel = item.Style.Box;
        if (crossSizing.Resolve(boxModel.GetMaxSize(!_mainIsHorizontal), SizingProperty.Maximum) is { } maxCross)
        {
            size = Math.Min(size, item.MainIsInline ? transfer.ToInline(maxCross) : transfer.ToBlock(maxCross));
        }

        if (crossSizing.Resolve(boxModel.GetMinSize(!_mainIsHorizontal), SizingProperty.Minimum) is { } minCross)
        {
            size = Math.Max(size, item.MainIsInline ? transfer.ToInline(minCross) : transfer.ToBlock(minCross));
        }

        return size;
    }

    /// <summary>
    /// Returns whether the <c>auto</c> minimum main size of an item is its automatic minimum size (https://drafts.csswg.org/css-flexbox-1/#min-size-auto):
    /// "for flex items that are not scroll containers" (as the browsers, an item that only scrolls in its cross axis has one).
    /// The items of a legacy <c>-webkit-box</c> have none, as in the browsers.
    /// </summary>
    private bool UsesAutomaticMinimumSize(FlexItem item)
        => !_properties.IsWebkitBox && BlockFormattingContext.GetContentsStyle(item.Box) is var contentsStyle && !ScrollContainers.IsScrollable(_mainIsHorizontal ? contentsStyle.OverflowX : contentsStyle.OverflowY);

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#min-size-auto: the content-based minimum size of an item: "the smaller of its
    /// specified size suggestion and its content size suggestion if its specified size suggestion exists [...] otherwise its
    /// content size suggestion. In all cases, the size is clamped by the maximum main size if it's definite."
    /// </summary>
    private double ComputeAutomaticMinimumSize(FlexItem item, in AxisSizing mainSizing, in AxisSizing crossSizing, AspectRatioTransfer? ratio, Size preferredMainSize, double? max, double tableCaptionsSize)
    {
        // "The content size suggestion is the min-content size in the main axis, clamped, if it has a preferred aspect ratio, by
        // any definite minimum and maximum cross sizes converted through the aspect ratio"
        var minContent = GetContentMainSize(item, mainSizing, minContent: true);
        if (ratio is not null && item.Box.Kind != ContainerBoxKind.Replaced)
        {
            minContent = Math.Max(minContent, GetContentsMinContentMainSize(item, mainSizing));
        }

        var content = ClampByTransferredCrossSizes(item, crossSizing, ratio, minContent);

        // "If the item's preferred main size is definite and not automatic, then the specified size suggestion is that size" (as
        // in the browsers, the percentages of a compressible form control resolve against zero, https://drafts.csswg.org/css-sizing-3/#min-content-zero,
        // and its specified size suggestion is then its automatic minimum size; the percentage size of the other replaced
        // elements, e.g. an image, is a specified size suggestion as any other size: WPT css/css-flexbox/flex-aspect-ratio-img-row-017.html)
        var result = content;
        var isCompressible = IntrinsicSizing.IsCompressibleFormControl(item.Box) && preferredMainSize.IsLengthPercentage && preferredMainSize.Value.HasPercentage;
        var specifiedSizing = isCompressible ? mainSizing with { PercentageBasis = 0 } : mainSizing;
        if ((mainSizing.IsInlineAxis || !preferredMainSize.IsContentBasedInBlockAxis) && specifiedSizing.Resolve(preferredMainSize, SizingProperty.Preferred) is { } specified)
        {
            result = isCompressible ? specified : Math.Min(specified + tableCaptionsSize, content);
        }

        return max is { } maximum ? Math.Min(result, maximum) : result;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Lines
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Collects the items into flex lines (https://drafts.csswg.org/css-flexbox-1/#algo-line-break), determines the main size
    /// of the container when it is indefinite, resolves the flexible lengths of each line, then the hypothetical cross sizes
    /// of the items and the cross sizes of the lines.
    /// </summary>
    /// <param name="collapse">Whether the collapsed items take no space (the second pass, https://drafts.csswg.org/css-flexbox-1/#visibility-collapse).</param>
    private void LayoutLines(bool collapse)
    {
        CollectLines(collapse);
        foreach (var line in _lines)
        {
            ResolveFlexibleLengths(line, collapse);
        }

        // https://drafts.csswg.org/css-flexbox-1/#algo-cross-item: "Determine the hypothetical cross size of each item by
        // performing layout as if it were an in-flow block-level box with the used main size and the given available space,
        // treating auto as fit-content"
        foreach (var item in _items)
        {
            if (collapse && item.IsCollapsed)
                continue;

            // (a cross size of stretch is the stretch-fit size in the content box of the container, as in the browsers, or behaves
            // as auto when it is indefinite; the item is then stretched in its line)
            var fragment = LayoutItem(item, item.TargetMainSize, item.DefiniteCrossSize);
            item.HypotheticalCrossSize = GetSizes(fragment).Cross;
            ComputeBaseline(item, fragment);
        }

        ComputeLineCrossSizes(collapse);
    }

    /// <summary>
    /// Determines the hypothetical cross size of an item of a row container that is measured (see <see cref="Layout"/>): its
    /// definite cross size, else its block size computed from its measured intrinsic block size with its target main size (a
    /// baseline-aligned item is laid out: its baselines are needed).
    /// </summary>
    private void MeasureHypotheticalCrossSize(FlexItem item)
    {
        if (item.DefiniteCrossSize is { } definite && item.Align is not (AxisAlignment.Baseline or AxisAlignment.LastBaseline))
        {
            item.HypotheticalCrossSize = definite;
            return;
        }

        if (item.Align is AxisAlignment.Baseline or AxisAlignment.LastBaseline || item.CrossSizing.IsInlineAxis)
        {
            var fragment = LayoutItem(item, item.TargetMainSize, item.DefiniteCrossSize);
            item.HypotheticalCrossSize = GetSizes(fragment).Cross;
            ComputeBaseline(item, fragment);
            return;
        }

        // The cross axis is the block axis of the item: its automatic block size, or its preferred block size, with its min/max sizes
        var content = _context.MeasureIntrinsicBlockSize(item.Box, CreateItemSpace(item, item.TargetMainSize, crossSize: null, measure: true));
        var crossSizing = item.CrossSizing with { ContentSize = content };
        var preferred = item.Style.Box.GetSize(!_mainIsHorizontal);
        var size = preferred.IsContentBasedInBlockAxis ? content : crossSizing.Resolve(preferred, SizingProperty.Preferred) ?? content;
        item.HypotheticalCrossSize = crossSizing.Clamp(size);
    }

    /// <summary>
    /// Collects the items into flex lines (https://drafts.csswg.org/css-flexbox-1/#algo-line-break) with their outer hypothetical
    /// main sizes, and determines the main size of the container when it is indefinite.
    /// </summary>
    private void CollectLines(bool collapse)
    {
        _lines.Clear();

        // https://drafts.csswg.org/css-flexbox-1/#algo-available: the available main space is the inner main size of the
        // container; a column container whose block size is indefinite breaks its lines at its maximum block size (as the browsers)
        var available = _definiteMainSize ?? _content.MaxBlockSize ?? double.PositiveInfinity;
        if (!_properties.IsBalanced || !CollectBalancedLines(available, collapse))
        {
            var start = 0;
            while (start < _items.Count)
            {
                var end = start;
                var used = 0d;
                var count = 0;
                while (end < _items.Count)
                {
                    // A collapsed item takes no space in the main axis: no gap is added for it either (as in Firefox, the only browser
                    // that collapses the items; WPT css/css-flexbox/gap-collapse.html)
                    var item = _items[end];
                    if (collapse && item.IsCollapsed)
                    {
                        end++;
                        continue;
                    }

                    var size = item.HypotheticalMainSize + item.MarginMainStart + item.MarginMainEnd;
                    var next = used + size + (count > 0 ? _mainGap : 0);

                    // "If the very first uncollected item wouldn't fit, collect just it into the line". The computed percentages
                    // are rounded: the items fit with the tolerance of the layout unit of the browsers, 1/64px (the columns of
                    // 16.66666667% and 8.33333333% of a row of Bootstrap fill it)
                    if (_properties.IsMultiLine && count > 0 && next > available + FitTolerance)
                        break;

                    used = next;
                    count++;
                    end++;
                }

                AddLine(start, end, used, count, collapse);
                start = end;
            }
        }

        // https://drafts.csswg.org/css-flexbox-1/#algo-main-container: an indefinite main size is the max-content main size of
        // the container (its largest line), clamped by its min and max main sizes; a calc-size() of the contents is resolved with
        // it (https://drafts.csswg.org/css-values-5/#calc-size): the items are flexed in the used main size
        if (_definiteMainSize is { } definiteMainSize)
        {
            _mainSize = definiteMainSize;
        }
        else
        {
            var largest = 0d;
            foreach (var line in _lines)
            {
                largest = Math.Max(largest, line.HypotheticalMainSize);
            }

            if (!_isRow && _content.ResolveContentBasedBlockSize?.Invoke(largest) is { } resolved)
            {
                largest = resolved;
            }

            _mainSize = Clamp(largest, _content.MinBlockSize, _content.MaxBlockSize);
        }
    }

    /// <summary>Adds the line of the items from <paramref name="start"/> to <paramref name="end"/> (exclusive).</summary>
    /// <param name="start">The first item of the line.</param>
    /// <param name="end">The first item of the next line.</param>
    /// <param name="hypotheticalMainSize">The sum of the outer hypothetical main sizes of the items that take space and of the gaps.</param>
    /// <param name="itemCount">The number of items that take space.</param>
    /// <param name="collapse">Whether the collapsed items take no space.</param>
    private void AddLine(int start, int end, double hypotheticalMainSize, int itemCount, bool collapse)
    {
        var line = new FlexLine(start, end) { HypotheticalMainSize = hypotheticalMainSize, IsCollapsedPass = collapse, ItemCount = itemCount };
        for (var i = start; i < end; i++)
        {
            _items[i].Line = line;
        }

        _lines.Add(line);
    }

    /// <summary>
    /// Collects the items of a balanced container into lines (https://drafts.csswg.org/css-flexbox-2/#algo-line-break, see
    /// <see cref="FlexLineBalancer"/>); returns false when the lines are not balanced (the items are then collected greedily).
    /// The outer hypothetical main sizes of the items are only floored at zero to choose the breaks: the lines are sized and
    /// flexed with the sizes of their items.
    /// </summary>
    private bool CollectBalancedLines(double available, bool collapse)
    {
        if (GetBalancedLines(available, collapse) is not { } ends)
            return false;

        var start = 0;
        foreach (var end in ends)
        {
            var used = 0d;
            var count = 0;
            for (var i = start; i < end; i++)
            {
                var item = _items[i];
                if (collapse && item.IsCollapsed)
                    continue;

                used += item.HypotheticalMainSize + item.MarginMainStart + item.MarginMainEnd + (count > 0 ? _mainGap : 0);
                count++;
            }

            AddLine(start, end, used, count, collapse);
            start = end;
        }

        return true;
    }

    /// <summary>
    /// Returns the end (exclusive) of each line of the items of a balanced container, or null when there is nothing to balance.
    /// The collapsed items take no space (<paramref name="collapse"/>): they stay on the line of the item that precedes them.
    /// </summary>
    private int[]? GetBalancedLines(double available, bool collapse)
    {
        var count = _items.Count;
        if (count < 2)
            return null;

        var sizes = new double[count];
        int[]? indexes = null;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            var item = _items[i];
            if (collapse && item.IsCollapsed)
            {
                if (indexes is null)
                {
                    indexes = new int[count];
                    for (var j = 0; j < i; j++)
                    {
                        indexes[j] = j;
                    }
                }

                continue;
            }

            if (indexes is not null)
            {
                indexes[length] = i;
            }

            sizes[length++] = item.HypotheticalMainSize + item.MarginMainStart + item.MarginMainEnd;
        }

        if (length == 0 || FlexLineBalancer.Balance(sizes.AsSpan(0, length), _mainGap, available, _properties.LineCount) is not { } ends)
            return null;

        // The end of a line is the first item of the next line that takes space
        for (var i = 0; i < ends.Length; i++)
        {
            ends[i] = i == ends.Length - 1 ? count : indexes is null ? ends[i] : indexes[ends[i]];
        }

        return ends;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#resolve-flexible-lengths: distributes the free space of a line to its items,
    /// according to their flex grow factors (positive free space) or their scaled flex shrink factors (negative free space),
    /// freezing the items that violate their min or max main sizes until the sizes are stable.
    /// </summary>
    private void ResolveFlexibleLengths(FlexLine line, bool collapse)
    {
        var available = _mainSize - _mainGap * Math.Max(0, line.ItemCount - 1);

        // 1. "Determine the used flex factor. Sum the outer hypothetical main sizes of all items on the line. If the sum is less
        // than the flex container's inner main size, use the flex grow factor for the rest of this algorithm; otherwise, use
        // the flex shrink factor."
        var sumHypothetical = 0d;
        for (var i = line.Start; i < line.End; i++)
        {
            var item = _items[i];
            if (!(collapse && item.IsCollapsed))
            {
                sumHypothetical += item.HypotheticalMainSize + item.OuterMainMargins;
            }
        }

        var growing = sumHypothetical < available;

        // 2. "Size inflexible items: freeze, setting its target main size to its hypothetical main size, any item that has a
        // flex factor of zero; if using the flex grow factor: any item that has a flex base size greater than its hypothetical
        // main size; if using the flex shrink factor: any item that has a flex base size smaller than its hypothetical main size"
        for (var i = line.Start; i < line.End; i++)
        {
            var item = _items[i];
            item.Frozen = false;
            item.TargetMainSize = item.HypotheticalMainSize;
            if (collapse && item.IsCollapsed)
            {
                item.TargetMainSize = 0;
                item.Frozen = true;
                continue;
            }

            var factor = growing ? item.Grow : item.Shrink;
            if (factor == 0 || (growing && item.FlexBaseSize > item.HypotheticalMainSize) || (!growing && item.FlexBaseSize < item.HypotheticalMainSize))
            {
                item.Frozen = true;
            }
        }

        // 3. "Calculate initial free space. Sum the outer sizes of all items on the line, and subtract this from the flex
        // container's inner main size. For frozen items, use their outer target main size; for other items, use their outer
        // flex base size."
        var initialFreeSpace = available - SumOuterSizes(line, collapse);

        // 4. "Loop"
        for (var iteration = 0; iteration <= line.Count; iteration++)
        {
            // a. "Check for flexible items. If all the flex items on the line are frozen, free space has been distributed; exit this loop."
            var sumFactors = 0d;
            var sumScaledShrink = 0d;
            var unfrozen = 0;
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                if (item.Frozen)
                    continue;

                unfrozen++;
                sumFactors += growing ? item.Grow : item.Shrink;
                sumScaledShrink += item.Shrink * item.InnerFlexBaseSize;
            }

            if (unfrozen == 0)
                break;

            // b. "Calculate the remaining free space as for initial free space, above. If the sum of the unfrozen flex items'
            // flex factors is less than one, multiply the initial free space by this sum. If the magnitude of this value is less
            // than the magnitude of the remaining free space, use this as the remaining free space."
            var remainingFreeSpace = available - SumOuterSizes(line, collapse);
            if (sumFactors < 1)
            {
                var scaled = initialFreeSpace * sumFactors;
                if (Math.Abs(scaled) < Math.Abs(remainingFreeSpace))
                {
                    remainingFreeSpace = scaled;
                }
            }

            // c. "Distribute free space proportional to the flex factors"
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                if (item.Frozen)
                    continue;

                if (remainingFreeSpace == 0 || double.IsInfinity(remainingFreeSpace))
                {
                    item.TargetMainSize = item.FlexBaseSize;
                }
                else if (growing)
                {
                    // "Find the ratio of the item's flex grow factor to the sum of the flex grow factors of all unfrozen items
                    // on the line. Set the item's target main size to its flex base size plus a fraction of the remaining free
                    // space proportional to the ratio."
                    // (a share that is not representable, e.g. next to an item with flex-grow: calc(infinity), is no space at all:
                    // an item must not get a size that only differs from zero by a rounding error)
                    var share = sumFactors > 0 ? remainingFreeSpace * item.Grow / sumFactors : 0;
                    item.TargetMainSize = item.FlexBaseSize + (Math.Abs(share) < Epsilon ? 0 : share);
                }
                else
                {
                    // "For every unfrozen item on the line, multiply its flex shrink factor by its inner flex base size, and note
                    // this as its scaled flex shrink factor. Find the ratio of the item's scaled flex shrink factor to the sum of
                    // the scaled flex shrink factors of all unfrozen items on the line. Set the item's target main size to its
                    // flex base size minus a fraction of the absolute value of the remaining free space proportional to the ratio."
                    var scaled = item.Shrink * item.InnerFlexBaseSize;
                    item.TargetMainSize = item.FlexBaseSize - (sumScaledShrink > 0 ? Math.Abs(remainingFreeSpace) * scaled / sumScaledShrink : 0);
                }
            }

            // d. "Fix min/max violations. Clamp each non-frozen item's target main size by its used min and max main sizes and
            // floor its content-box size at zero. If the item's target main size was made smaller by this, it's a max violation.
            // If the item's target main size was made larger by this, it's a min violation."
            var totalViolation = 0d;
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                if (item.Frozen)
                    continue;

                var clamped = item.ClampMainSize(item.TargetMainSize);
                item.Violation = clamped - item.TargetMainSize;
                totalViolation += item.Violation;
                item.TargetMainSize = clamped;
            }

            // e. "Freeze over-flexed items. The total violation is the sum of the adjustments from the previous step. If the
            // total violation is: Zero: Freeze all items. Positive: Freeze all the items with min violations. Negative: Freeze
            // all the items with max violations."
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                if (item.Frozen)
                    continue;

                if (Math.Abs(totalViolation) < Epsilon || (totalViolation > 0 && item.Violation > 0) || (totalViolation < 0 && item.Violation < 0))
                {
                    item.Frozen = true;
                }
            }
        }
    }

    /// <summary>Sums the outer sizes of the items of a line: the outer target main size of the frozen items, the outer flex base size of the others.</summary>
    private double SumOuterSizes(FlexLine line, bool collapse)
    {
        var sum = 0d;
        for (var i = line.Start; i < line.End; i++)
        {
            var item = _items[i];
            if (collapse && item.IsCollapsed)
                continue;

            sum += (item.Frozen ? item.TargetMainSize : item.FlexBaseSize) + item.OuterMainMargins;
        }

        return sum;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#algo-cross-line: the cross size of each line: the inner cross size of a
    /// single-line container whose cross size is definite; otherwise the largest outer hypothetical cross size of the items
    /// that are not baseline-aligned, and the largest sum of the distances of the baseline-aligned items from their shared
    /// baseline, for each baseline-sharing group. The cross size of the line of a single-line container is clamped by the min
    /// and max cross sizes of the container.
    /// </summary>
    private void ComputeLineCrossSizes(bool collapse)
    {
        foreach (var line in _lines)
        {
            var size = 0d;
            // (the baseline of an item may be outside of its margin box: the distance to the other edge is then negative)
            var startAbove = 0d;
            var startBelow = double.NegativeInfinity;
            var endAbove = double.NegativeInfinity;
            var endBelow = 0d;
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                if (item.IsCollapsed)
                {
                    // The strut of a collapsed item keeps the cross size of its line
                    if (collapse)
                    {
                        size = Math.Max(size, item.Strut);
                        continue;
                    }
                }

                var outer = item.HypotheticalCrossSize + item.MarginCrossStart + item.MarginCrossEnd;
                switch (item.BaselineGroup)
                {
                    case BaselineGroup.Start:
                        // The distances are measured from the margin edges of the cross-start side (cross-end for the last-baseline group)
                        var above = item.MarginCrossStart + item.BaselineOffset;
                        startAbove = double.IsNegativeInfinity(startBelow) ? above : Math.Max(startAbove, above);
                        startBelow = Math.Max(startBelow, outer - above);
                        break;

                    case BaselineGroup.End:
                        var below = item.MarginCrossEnd + item.BaselineOffset;
                        endBelow = double.IsNegativeInfinity(endAbove) ? below : Math.Max(endBelow, below);
                        endAbove = Math.Max(endAbove, outer - below);
                        break;

                    default:
                        size = Math.Max(size, outer);
                        break;
                }
            }

            line.StartBaseline = startAbove;
            line.EndBaseline = endBelow;
            size = Math.Max(size, Math.Max(startAbove + startBelow, endAbove + endBelow));
            line.ContentCrossSize = size;

            if (!_properties.IsMultiLine)
            {
                // "If the flex container is single-line and has a definite cross size, the cross size of the flex line is the flex
                // container's inner cross size. [...] If the flex container is single-line, then clamp the line's cross-size to be
                // within the container's computed min and max cross sizes."
                size = _lineDefiniteCrossSize ?? (_isRow ? Clamp(size, _content.MinBlockSize, _content.MaxBlockSize) : size);
            }

            line.CrossSize = size;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Baselines
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Computes the baseline of a baseline-aligned item in the cross axis (https://drafts.csswg.org/css-align-3/#baseline-values):
    /// its baseline-sharing group (an item whose block flow direction is opposite to the direction of the cross axis in the
    /// writing mode of the container has its first and last baseline preference flipped,
    /// https://drafts.csswg.org/css-align-3/#baseline-sharing-group) and the distance of its baseline from its border edge on
    /// the side of the group. An item without a baseline in the cross axis
    /// (an orthogonal item, an item without line box) gets a synthesized one (https://drafts.csswg.org/css-align-3/#synthesize-baseline).
    /// </summary>
    private void ComputeBaseline(FlexItem item, BoxFragment fragment)
    {
        item.BaselineGroup = BaselineGroup.None;
        if (item.Align is not (AxisAlignment.Baseline or AxisAlignment.LastBaseline))
            return;

        // The baselines are measured from the reference side of the cross axis (as in the browsers): the block-start side of a row
        // container, the inline-start side of a horizontal column container, the top side of a vertical column container
        var last = item.Align == AxisAlignment.LastBaseline;
        var crossSize = GetSizes(fragment).Cross;
        var reference = GetBaselineReferenceSide();
        var itemBlockStart = item.WritingDirection.BlockStart;
        var baseline = last ? fragment.LastBaseline : fragment.FirstBaseline;
        double fromReference;
        if (baseline is { } value && itemBlockStart == reference)
        {
            fromReference = value;
        }
        else if (baseline is { } flippedValue && itemBlockStart == Opposite(reference))
        {
            // The baselines of an item whose block-start edge is on the other side: its first baseline is aligned with the
            // other side (and its last baseline with the reference side)
            fromReference = crossSize - flippedValue;
            last = !last;
        }
        else
        {
            fromReference = SynthesizeBaseline(crossSize);
        }

        // The first-baseline group is aligned with the reference side of the line (the other side with wrap-reverse), the
        // last-baseline group with the other side: the distance of the baseline is measured from the border edge of that side
        var firstSide = _properties.IsCrossReverse ? Opposite(reference) : reference;
        var groupSide = last ? Opposite(firstSide) : firstSide;
        var crossStart = _isRow ? _writingDirection.BlockStart : _writingDirection.InlineStart;
        if (_properties.IsCrossReverse)
        {
            crossStart = Opposite(crossStart);
        }

        item.BaselineGroup = groupSide == crossStart ? BaselineGroup.Start : BaselineGroup.End;
        item.BaselineOffset = groupSide == reference ? fromReference : crossSize - fromReference;
        item.BaselineFromLogicalStart = reference == (_isRow ? _writingDirection.BlockStart : _writingDirection.InlineStart) ? fromReference : crossSize - fromReference;
    }

    /// <summary>The physical side of the cross axis the baselines of the items are measured from (see <see cref="ComputeBaseline"/>).</summary>
    private PhysicalSide GetBaselineReferenceSide() => _isRow ? _writingDirection.BlockStart : _writingDirection.IsHorizontal ? _writingDirection.InlineStart : PhysicalSide.Top;

    /// <summary>
    /// Returns the synthesized alignment baseline of a box from its border box in the cross axis, from the reference side (see
    /// <see cref="GetBaselineReferenceSide"/>), as in the browsers (https://drafts.csswg.org/css-align-3/#synthesize-baseline):
    /// the dominant baseline of a vertical container is central, at the center of the box (unless <c>text-orientation: sideways</c>
    /// makes it alphabetic, see <see cref="LayoutStyle.HasCentralDominantBaseline"/>); the alphabetic baseline is at the
    /// line-under edge: the bottom edge in the vertical axis, the left edge in the horizontal axis of a horizontal container.
    /// </summary>
    private double SynthesizeBaseline(double crossSize)
    {
        if (!_writingDirection.IsHorizontal && _box.Style.Layout.HasCentralDominantBaseline)
            return crossSize / 2;

        // The line-under edge (the alphabetic baseline): of the container in its block axis, else of the axis-compatible
        // writing mode (horizontal-tb in a vertical axis, vertical-lr in a horizontal axis)
        var lineUnder = _isRow ? _writingDirection.LineUnder : _writingDirection.IsHorizontal ? PhysicalSide.Left : PhysicalSide.Bottom;
        return GetBaselineReferenceSide() == lineUnder ? 0 : crossSize;
    }

    private static PhysicalSide Opposite(PhysicalSide side) => side switch
    {
        PhysicalSide.Top => PhysicalSide.Bottom,
        PhysicalSide.Bottom => PhysicalSide.Top,
        PhysicalSide.Left => PhysicalSide.Right,
        _ => PhysicalSide.Left,
    };

    // ------------------------------------------------------------------------------------------------------------------
    // Alignment
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#algo-cross-container, https://drafts.csswg.org/css-flexbox-1/#algo-line-align:
    /// determines the used cross size of the container (its definite cross size, or the sum of the cross sizes of its lines
    /// clamped by its min and max cross sizes), then distributes its free space between the lines with <c>align-content</c>.
    /// </summary>
    private void AlignLines()
    {
        var count = _lines.Count;
        var total = _crossGap * Math.Max(0, count - 1);
        foreach (var line in _lines)
        {
            total += line.CrossSize;
        }

        if (_definiteCrossSize is { } definiteCrossSize)
        {
            _crossSize = definiteCrossSize;
        }
        else
        {
            _crossSize = _isRow ? Clamp(total, _content.MinBlockSize, _content.MaxBlockSize) : total;
        }

        // https://drafts.csswg.org/css-flexbox-1/#align-content-property: "Only multi-line flex containers ever have free space
        // in the cross-axis for lines to be aligned in, because in a single-line flex container the sole line automatically
        // stretches to fill the space"
        var offset = 0d;
        var between = 0d;
        var endsAtCrossEnd = false;
        if (_properties.IsMultiLine && count > 0)
        {
            var freeSpace = _crossSize - total;
            var (align, safe) = ResolveContentAlignment(_properties.AlignContent, crossAxis: true);
            endsAtCrossEnd = align == AxisAlignment.SpaceBetween && freeSpace > 0 && count > 1;
            if (align == AxisAlignment.Stretch)
            {
                // "Lines stretch to take up the remaining space. If the leftover free-space is negative, this value is identical to
                // flex-start. Otherwise, the free-space is split equally between all of the lines, increasing their cross size."
                // (in units of 1/64 px, the last line gets the remainder)
                if (freeSpace > 0)
                {
                    var share = SnapDown(freeSpace / count);
                    foreach (var line in _lines)
                    {
                        line.CrossSize += share;
                    }

                    _lines[^1].CrossSize += freeSpace - share * count;
                }
            }
            else
            {
                (offset, between) = DistributeSpace(align, safe, freeSpace, count, _properties.IsCrossReverse);
            }
        }

        var position = offset;
        foreach (var line in _lines)
        {
            line.CrossOffset = position;
            position += line.CrossSize + _crossGap + between;
        }

        if (endsAtCrossEnd)
        {
            // space-between: the last line ends exactly at the cross-end edge
            _lines[^1].CrossOffset = _crossSize - _lines[^1].CrossSize;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#algo-stretch: "If a flex item has align-self: stretch, its computed cross size
    /// property is auto, and neither of its cross-axis margins are auto, the used outer cross size is the used cross size of
    /// its flex line, clamped according to the item's used min and max cross sizes. Otherwise, the used cross size is the
    /// item's hypothetical cross size." A stretched item is laid out again with its used cross size as a definite size.
    /// </summary>
    /// <remarks>
    /// In a multi-line container, and in a single-line container whose cross size is indefinite, the <c>stretch</c> keyword of the
    /// cross size properties of an item resolves against the cross size of its flex line (https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing,
    /// as in the browsers), once it is known: before, it resolves against the content box of the container (see
    /// <see cref="CreateSizing"/>; in an indefinite size, a minimum <c>stretch</c> behaves as zero and a maximum one as
    /// <c>none</c>, https://github.com/w3c/csswg-drafts/issues/11006). The cross size of an item whose min or max cross size is
    /// <c>stretch</c> is clamped again then.
    /// </remarks>
    private void StretchItems()
    {
        foreach (var line in _lines)
        {
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                item.CrossSize = item.HypotheticalCrossSize;
                item.UsedCrossSize = item.DefiniteCrossSize;
                if (item.IsCollapsed && line.IsCollapsedPass)
                    continue;

                var resolvesStretchInLine = (_properties.IsMultiLine || _definiteCrossSize is null) && HasStretchCrossSize(item);
                if (!item.Stretches && !resolvesStretchInLine)
                    continue;

                // The intrinsic size keywords of the min and max block sizes resolve to the automatic block size of the item
                var crossSizing = item.CrossSizing.IsInlineAxis || item.Fragment is not { } hypothetical ? item.CrossSizing : item.CrossSizing with { ContentSize = hypothetical.IntrinsicBlockSize };
                var outerMargins = item.MarginCrossStart + item.MarginCrossEnd;
                if (resolvesStretchInLine)
                {
                    crossSizing = crossSizing with { StretchSize = Math.Max(line.CrossSize - outerMargins, crossSizing.PaddingAndBorder) };
                }

                if (item.Stretches)
                {
                    item.UsedCrossSize = crossSizing.Clamp(line.CrossSize - outerMargins);
                }
                else
                {
                    // An item that is not stretched: its cross size before the min and max sizes (its definite cross size, or its
                    // fit-content size in the container, or its automatic block size), clamped with the size of the line
                    if (GetUnclampedCrossSize(item, crossSizing) is not { } unclamped)
                        continue;

                    var used = crossSizing.Clamp(unclamped);
                    if (Math.Abs(used - item.HypotheticalCrossSize) < 1e-9)
                        continue;

                    item.UsedCrossSize = used;
                }

                var fragment = LayoutItem(item, item.TargetMainSize, item.UsedCrossSize);
                item.CrossSize = GetSizes(fragment).Cross;
            }
        }
    }

    /// <summary>Returns whether the preferred, min or max cross size of an item is <c>stretch</c>.</summary>
    private bool HasStretchCrossSize(FlexItem item)
    {
        var boxModel = item.Style.Box;
        var horizontal = !_mainIsHorizontal;
        return boxModel.GetSize(horizontal).Kind == SizeKind.Stretch || boxModel.GetMinSize(horizontal).Kind == SizeKind.Stretch || boxModel.GetMaxSize(horizontal).Kind == SizeKind.Stretch;
    }

    /// <summary>
    /// Returns the border-box cross size of an item that is not stretched, before its min and max cross sizes apply: its preferred
    /// cross size when it is definite, else its fit-content size in the container (inline axis) or its automatic block size.
    /// </summary>
    private double? GetUnclampedCrossSize(FlexItem item, in AxisSizing crossSizing)
    {
        var preferred = item.Style.Box.GetSize(!_mainIsHorizontal);
        if ((crossSizing.IsInlineAxis || !preferred.IsContentBasedInBlockAxis) && item.CrossSizing.Resolve(preferred, SizingProperty.Preferred) is { } size)
            return size;

        return crossSizing.IsInlineAxis ? item.CrossSizing.Resolve(Size.FromKind(SizeKind.FitContent), SizingProperty.Preferred) : crossSizing.ContentSize;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#algo-main-align: distributes the remaining free space of each line to the
    /// <c>auto</c> margins of the main axis, or aligns the items with <c>justify-content</c>.
    /// </summary>
    private void AlignItemsInMainAxis()
    {
        var (align, safe) = ResolveContentAlignment(_properties.JustifyContent, crossAxis: false);
        foreach (var line in _lines)
        {
            var used = _mainGap * Math.Max(0, line.ItemCount - 1);
            var autoMargins = 0;
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                if (item.IsCollapsed && line.IsCollapsedPass)
                    continue;

                used += item.TargetMainSize + item.OuterMainMargins;
                autoMargins += (item.AutoMarginMainStart ? 1 : 0) + (item.AutoMarginMainEnd ? 1 : 0);
            }

            var freeSpace = _mainSize - used;
            var autoMargin = 0d;
            var offset = 0d;
            var between = 0d;

            // "If the remaining free space is positive and at least one main-axis margin on this line is auto, distribute the free
            // space equally among these margins. Otherwise, set all auto margins to zero."
            var distributesToAutoMargins = freeSpace > 0 && autoMargins > 0;
            if (distributesToAutoMargins)
            {
                autoMargin = SnapDown(freeSpace / autoMargins);
            }
            else
            {
                (offset, between) = DistributeSpace(align, safe, freeSpace, line.ItemCount, _properties.IsMainReverse);
            }

            var position = offset;
            var remainingAutoMargins = autoMargins;
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                item.UsedMarginMainStart = item.AutoMarginMainStart ? TakeAutoMargin() : item.MarginMainStart;
                item.UsedMarginMainEnd = item.AutoMarginMainEnd ? TakeAutoMargin() : item.MarginMainEnd;
                item.MainPosition = position;
                if (item.IsCollapsed && line.IsCollapsedPass)
                {
                    item.UsedMarginMainStart = 0;
                    item.UsedMarginMainEnd = 0;
                    continue;
                }

                position += item.TargetMainSize + item.UsedMarginMainStart + item.UsedMarginMainEnd + _mainGap + between;
            }

            // The space between the items is distributed in units of 1/64 px (as the layout units of the browsers), the last one
            // gets the remainder: the last item ends exactly at the main-end edge
            if (align == AxisAlignment.SpaceBetween && !distributesToAutoMargins && freeSpace > 0 && line.ItemCount > 1)
            {
                var last = line.End - 1;
                while (_items[last].IsCollapsed && line.IsCollapsedPass)
                {
                    last--;
                }

                _items[last].MainPosition += freeSpace - between * (line.ItemCount - 1);
            }

            double TakeAutoMargin()
            {
                if (!distributesToAutoMargins)
                    return 0;

                remainingAutoMargins--;
                return remainingAutoMargins == 0 ? freeSpace - autoMargin * (autoMargins - 1) : autoMargin;
            }
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#algo-cross-margins, https://drafts.csswg.org/css-flexbox-1/#algo-cross-align:
    /// resolves the <c>auto</c> margins of the cross axis, else aligns each item in its line with <c>align-self</c>.
    /// </summary>
    private void AlignItemsInCrossAxis()
    {
        foreach (var line in _lines)
        {
            for (var i = line.Start; i < line.End; i++)
            {
                var item = _items[i];
                item.UsedMarginCrossStart = item.MarginCrossStart;
                item.UsedMarginCrossEnd = item.MarginCrossEnd;
                var outer = item.CrossSize + item.MarginCrossStart + item.MarginCrossEnd;
                var freeSpace = line.CrossSize - outer;
                if (item.AutoMarginCrossStart || item.AutoMarginCrossEnd)
                {
                    // "If its outer cross size (treating those auto margins as zero) is less than the cross size of its flex line,
                    // distribute the difference in those sizes equally to the auto margins. Otherwise, if the block-start or
                    // inline-start margin (whichever is in the cross axis) is auto, set it to zero. Set the opposite margin so
                    // that the outer cross size of the item equals the cross size of its flex line."
                    if (freeSpace > 0)
                    {
                        var share = freeSpace / ((item.AutoMarginCrossStart ? 1 : 0) + (item.AutoMarginCrossEnd ? 1 : 0));
                        item.UsedMarginCrossStart += item.AutoMarginCrossStart ? share : 0;
                        item.UsedMarginCrossEnd += item.AutoMarginCrossEnd ? share : 0;
                    }
                    else if (_properties.IsCrossReverse)
                    {
                        // The margin on the logical start side keeps its value (0 when auto): the one on the logical end side (the
                        // cross-start side with wrap-reverse) absorbs the difference
                        item.UsedMarginCrossStart += freeSpace;
                    }
                    else
                    {
                        item.UsedMarginCrossEnd += freeSpace;
                    }

                    item.CrossPosition = 0;
                    continue;
                }

                // https://drafts.csswg.org/css-align-3/#overflow-values: "safe: If the size of the alignment subject overflows the
                // alignment container, the alignment subject is instead aligned as if the alignment mode were start": the start
                // edge in the writing mode of the container, which is the cross-end edge with wrap-reverse
                item.CrossPosition = item.Align is AxisAlignment.Baseline or AxisAlignment.LastBaseline
                    ? GetBaselinePosition(item, line, outer)
                    : item.Safe && freeSpace < 0 && item.Align != AxisAlignment.Stretch ? (_properties.IsCrossReverse ? freeSpace : 0)
                    : BoxAlignment.GetOffset(item.Align, freeSpace, safe: false);
            }
        }
    }

    /// <summary>Returns the position of the margin box of a baseline-aligned item from the cross-start edge of its line.</summary>
    private double GetBaselinePosition(FlexItem item, FlexLine line, double outer)
    {
        return item.BaselineGroup switch
        {
            BaselineGroup.Start => line.StartBaseline - (item.MarginCrossStart + item.BaselineOffset),
            BaselineGroup.End => line.CrossSize - outer - (line.EndBaseline - (item.MarginCrossEnd + item.BaselineOffset)),
            _ => 0,
        };
    }

    /// <summary>
    /// Returns the offset of the first alignment subject and the space between the subjects for a content distribution
    /// (https://drafts.csswg.org/css-align-3/#distribution-flex, see <see cref="BoxAlignment.Distribute"/>): the space between the
    /// subjects of <c>space-between</c> is snapped to 1/64 px (the caller gives the remainder to the last subject). The subjects
    /// that overflow with a <c>safe</c> positional alignment are aligned "as if the alignment mode were start"
    /// (https://drafts.csswg.org/css-align-3/#overflow-values): the start edge in the writing mode of the container, which is the
    /// flex-end edge of a reversed axis (<paramref name="reverse"/>).
    /// </summary>
    private static (double Offset, double Between) DistributeSpace(AxisAlignment align, bool safe, double freeSpace, int count, bool reverse)
    {
        if (safe && freeSpace < 0 && align is AxisAlignment.Start or AxisAlignment.End or AxisAlignment.Center)
            return (reverse ? freeSpace : 0, 0);

        var (offset, between) = BoxAlignment.Distribute(align, freeSpace, count, safe);
        return (offset, align == AxisAlignment.SpaceBetween ? SnapDown(between) : between);
    }

    /// <summary>
    /// Returns an axis of the container as an alignment container (https://drafts.csswg.org/css-align-3/#distribution-flex): the
    /// positions are measured from the main-start or cross-start edge (the <c>flex-start</c> side), <c>start</c>/<c>end</c> refer
    /// to the writing mode of the container.
    /// </summary>
    private AlignmentAxis GetAlignmentAxis(bool crossAxis)
    {
        var axis = crossAxis != _isRow ? AlignmentAxis.Inline(_writingDirection) : AlignmentAxis.Block(_writingDirection);
        var reverse = crossAxis ? _properties.IsCrossReverse : _properties.IsMainReverse;
        var flexStart = reverse ? Opposite(axis.WritingModeStart) : axis.WritingModeStart;
        return axis with { Start = flexStart, FlexStart = flexStart };
    }

    /// <summary>
    /// Resolves <c>justify-content</c> (main axis) or <c>align-content</c> (cross axis) to an alignment from the main-start or
    /// cross-start edge (see <see cref="BoxAlignment.ResolveContentAlignment"/>): <c>normal</c> behaves as <c>stretch</c>, which
    /// is <c>flex-start</c> in the main axis (https://drafts.csswg.org/css-align-3/#distribution-flex).
    /// </summary>
    private (AxisAlignment Align, bool Safe) ResolveContentAlignment(Alignment alignment, bool crossAxis)
    {
        var align = BoxAlignment.ResolveContentAlignment(alignment.Keyword, GetAlignmentAxis(crossAxis), AxisAlignment.Stretch);
        if (!crossAxis && align == AxisAlignment.Stretch)
        {
            align = AxisAlignment.Start;
        }

        return (align, alignment.Overflow == OverflowAlignment.Safe);
    }

    /// <summary>
    /// Resolves <c>align-self</c> to an alignment from the cross-start edge (see <see cref="BoxAlignment.ResolveSelfAlignment"/>):
    /// <c>start</c>/<c>end</c> refer to the writing mode of the container, <c>self-start</c>/<c>self-end</c> to the one of the item,
    /// <c>flex-start</c>/<c>flex-end</c> to the cross-start edge.
    /// </summary>
    private AxisAlignment ResolveSelfAlignment(FlexItem item, AlignmentKeyword keyword)
        => BoxAlignment.ResolveSelfAlignment(keyword, GetAlignmentAxis(crossAxis: true), item.WritingDirection, AxisAlignment.Stretch);

    // ------------------------------------------------------------------------------------------------------------------
    // Fragments
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lays out the items with their final sizes, adds their fragments at their positions (in order-modified document order,
    /// the painting order of the items, https://drafts.csswg.org/css-flexbox-1/#painting), records the static positions of the
    /// absolutely positioned children, and returns the block size of the contents and the baselines of the container.
    /// </summary>
    private ContentLayoutResult PlaceItems(FragmentBuilder builder)
    {
        var absoluteIndex = 0;
        foreach (var item in _items)
        {
            // The absolutely positioned children that precede the item in the document order (their fragments are in tree order)
            while (_absolutelyPositioned is not null && absoluteIndex < _absolutelyPositioned.Count && _absolutelyPositioned[absoluteIndex].Index < item.Index)
            {
                AddAbsolutelyPositionedChild(builder, _absolutelyPositioned[absoluteIndex++].Box);
            }

            var line = item.Line!;
            var collapsed = item.IsCollapsed && line.IsCollapsedPass;
            var fragment = LayoutItem(item, item.TargetMainSize, collapsed ? null : item.UsedCrossSize);
            var (mainSize, crossSize) = GetSizes(fragment);
            item.CrossSize = crossSize;

            // The positions of the border box from the main-start and cross-start edges of the content box
            var main = item.MainPosition + item.UsedMarginMainStart;
            var cross = line.CrossOffset + item.CrossPosition + item.UsedMarginCrossStart;
            var mainLogical = _properties.IsMainReverse ? _mainSize - main - mainSize : main;
            var crossLogical = _properties.IsCrossReverse ? _crossSize - cross - crossSize : cross;
            var offset = _isRow ? new LogicalOffset(mainLogical, crossLogical) : new LogicalOffset(crossLogical, mainLogical);
            item.LogicalOffset = offset;

            // The used margins (the auto margins resolved)
            fragment.Margin = _writingDirection.ToPhysical(FromFlexRelative(item.UsedMarginMainStart, item.UsedMarginMainEnd, item.UsedMarginCrossStart, item.UsedMarginCrossEnd));
            builder.AddChild(fragment, new LogicalOffset(_content.Offset.Inline + offset.Inline, _content.Offset.Block + offset.Block));
        }

        while (_absolutelyPositioned is not null && absoluteIndex < _absolutelyPositioned.Count)
        {
            AddAbsolutelyPositionedChild(builder, _absolutelyPositioned[absoluteIndex++].Box);
        }

        builder.GapGeometry = BuildGapGeometry();
        RecordLineGaps();
        var (firstBaseline, lastBaseline) = ComputeContainerBaselines();
        return new ContentLayoutResult(ComputeContentBlockSize(), firstBaseline, lastBaseline) { InFlowBlockSize = _isRow ? _crossSize : _mainSize };
    }

    /// <summary>
    /// Returns the block size of the contents of the container, its <c>auto</c> block size (which its intrinsic size keywords
    /// resolve to, https://drafts.csswg.org/css-flexbox-1/#intrinsic-cross-sizes, even when its block size is definite): the sum
    /// of the cross sizes of the lines given by their items for a row container, the largest sum of the outer hypothetical main
    /// sizes of the items of a line for a column container. The min and max block sizes are applied by the caller.
    /// </summary>
    private double ComputeContentBlockSize()
    {
        var size = 0d;
        if (_isRow)
        {
            size = _crossGap * Math.Max(0, _lines.Count - 1);
            foreach (var line in _lines)
            {
                size += line.ContentCrossSize;
            }
        }
        else
        {
            foreach (var line in _lines)
            {
                size = Math.Max(size, line.HypotheticalMainSize);
            }
        }

        return size;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#flex-baselines: the first and last baselines of the container (in its block
    /// axis, from the block-start edge of its content box): the shared baseline of the items of the first (last) line that
    /// participate in baseline alignment, else the baseline of the startmost (endmost) item of the line in the writing mode of
    /// the container (synthesized from its border box when it has none). The first line is the visually first one (the last
    /// one in order with <c>wrap-reverse</c>).
    /// </summary>
    private (double? First, double? Last) ComputeContainerBaselines()
    {
        if (_lines.Count == 0)
            return (null, null);

        var firstLine = _properties.IsCrossReverse ? _lines[^1] : _lines[0];
        var lastLine = _properties.IsCrossReverse ? _lines[0] : _lines[^1];
        return (GetLineBaseline(firstLine, last: false), GetLineBaseline(lastLine, last: true));
    }

    private double? GetLineBaseline(FlexLine line, bool last)
    {
        if (_isRow)
        {
            // The items that participate in baseline alignment: the group of the same edge first, then the other one
            var group = last ? BaselineGroup.End : BaselineGroup.Start;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = line.Start; i < line.End; i++)
                {
                    var item = _items[i];
                    if (item.BaselineGroup == group)
                        return item.LogicalOffset.Block + item.BaselineFromLogicalStart;
                }

                group = last ? BaselineGroup.Start : BaselineGroup.End;
            }
        }

        // The startmost (endmost) item of the line, in the writing mode of the container (the reverse directions place the first
        // item in order at the end)
        var fromEnd = last != _properties.IsMainReverse;
        for (var n = 0; n < line.Count; n++)
        {
            var item = _items[fromEnd ? line.End - 1 - n : line.Start + n];
            if (item.IsCollapsed && line.IsCollapsedPass || item.Fragment is not { } itemFragment)
                continue;

            return item.LogicalOffset.Block + GetBlockAxisBaseline(item, itemFragment, last);
        }

        return null;
    }

    /// <summary>
    /// Returns the baseline of an item in the block axis of the container, from the block-start edge of its border box: its
    /// baseline when its block axis is the one of the container, else a baseline synthesized from its border box.
    /// </summary>
    private double GetBlockAxisBaseline(FlexItem item, BoxFragment fragment, bool last)
    {
        var blockSize = _writingDirection.ToLogical(fragment.Size).Block;
        if (!item.WritingDirection.IsOrthogonalTo(_writingDirection) && (last ? fragment.LastBaseline : fragment.FirstBaseline) is { } baseline)
            return item.WritingDirection.BlockStart == _writingDirection.BlockStart ? baseline : blockSize - baseline;

        return _writingDirection.IsHorizontal ? blockSize : blockSize / 2;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#abspos-items: "The static position of an absolutely-positioned child of a flex
    /// container is determined such that the child is positioned as if it were the sole flex item in the flex container,
    /// assuming both the child and the flex container were fixed-size boxes of their used size": aligned in the content box
    /// with <c>justify-content</c> in the main axis and its <c>align-self</c> in the cross axis.
    /// </summary>
    private void AddAbsolutelyPositionedChild(FragmentBuilder builder, ContainerBox box)
    {
        var (mainAlign, mainSafe) = ResolveContentAlignment(_properties.JustifyContent, crossAxis: false);
        var mainEdge = mainAlign switch
        {
            AxisAlignment.End => StaticPositionEdge.End,
            AxisAlignment.Center or AxisAlignment.SpaceAround or AxisAlignment.SpaceEvenly => StaticPositionEdge.Center,
            _ => StaticPositionEdge.Start,
        };

        var item = new FlexItem(box, box.Style.Layout, 0, 0) { WritingDirection = WritingDirection.FromStyle(box.Style.Layout) };
        var alignSelf = _properties.GetAlignSelf(item.Style);
        var crossAlign = ResolveSelfAlignment(item, alignSelf.Keyword);

        // https://drafts.csswg.org/css-align-3/#overflow-values: the box with a safe alignment that overflows is aligned with the
        // start edge instead (see OutOfFlowCandidate.IsSafeHorizontally)
        var safeMain = mainSafe && mainEdge != StaticPositionEdge.Start;
        var safeCross = alignSelf.Overflow == OverflowAlignment.Safe && crossAlign is AxisAlignment.End or AxisAlignment.Center;
        var crossEdge = crossAlign switch
        {
            AxisAlignment.End => StaticPositionEdge.End,
            AxisAlignment.Center => StaticPositionEdge.Center,

            // The baseline alignments fall back to the start and end of the writing mode of the container
            AxisAlignment.Baseline => _properties.IsCrossReverse ? StaticPositionEdge.End : StaticPositionEdge.Start,
            AxisAlignment.LastBaseline => _properties.IsCrossReverse ? StaticPositionEdge.Start : StaticPositionEdge.End,
            _ => StaticPositionEdge.Start,
        };

        // The edges in the logical axes of the container (the reverse directions flip the flex-relative edges)
        var (mainPosition, mainLogicalEdge) = ToLogicalEdge(mainEdge, _properties.IsMainReverse, _mainSize);
        var (crossPosition, crossLogicalEdge) = ToLogicalEdge(crossEdge, _properties.IsCrossReverse, _crossSize);
        var position = _isRow ? new LogicalOffset(mainPosition, crossPosition) : new LogicalOffset(crossPosition, mainPosition);
        builder.AddOutOfFlowCandidate(
            box,
            new LogicalOffset(_content.Offset.Inline + position.Inline, _content.Offset.Block + position.Block),
            _isRow ? mainLogicalEdge : crossLogicalEdge,
            _isRow ? crossLogicalEdge : mainLogicalEdge,
            safeInline: _isRow ? safeMain : safeCross,
            safeBlock: _isRow ? safeCross : safeMain);

        static (double Position, StaticPositionEdge Edge) ToLogicalEdge(StaticPositionEdge edge, bool reverse, double size) => edge switch
        {
            StaticPositionEdge.Center => (size / 2, StaticPositionEdge.Center),
            StaticPositionEdge.End => reverse ? (0, StaticPositionEdge.Start) : (size, StaticPositionEdge.End),
            _ => reverse ? (size, StaticPositionEdge.End) : (0, StaticPositionEdge.Start),
        };
    }

    /// <summary>Rounds a positive length down to a multiple of 1/64 px (the layout unit of the browsers) to distribute a space between several boxes without accumulating rounding errors.</summary>
    private static double SnapDown(double value) => Math.Floor(value * 64) / 64;

    private static double Clamp(double size, double? min, double? max)
    {
        if (max is { } maximum && size > maximum)
        {
            size = maximum;
        }

        if (min is { } minimum && size < minimum)
        {
            size = minimum;
        }

        return size;
    }

    /// <summary>The baseline-sharing group of an item (https://drafts.csswg.org/css-align-3/#baseline-sharing-group), aligned to the logical start or end side of the cross axis.</summary>
    private enum BaselineGroup : byte
    {
        None,
        Start,
        End,
    }

    /// <summary>A flex line (https://drafts.csswg.org/css-flexbox-1/#flex-line): a range of the items in order.</summary>
    private sealed class FlexLine(int start, int end)
    {
        public int Start { get; } = start;

        public int End { get; } = end;

        public int Count => End - Start;

        /// <summary>The number of items that take space in the main axis: the items of the line that are not collapsed (see <see cref="IsCollapsedPass"/>).</summary>
        public int ItemCount { get; init; }

        /// <summary>The sum of the outer hypothetical main sizes of the items and of the gaps.</summary>
        public double HypotheticalMainSize { get; set; }

        /// <summary>The cross size of the line given by its items, before the size of the container is applied.</summary>
        public double ContentCrossSize { get; set; }

        public double CrossSize { get; set; }

        /// <summary>The position of the line from the cross-start edge of the content box.</summary>
        public double CrossOffset { get; set; }

        /// <summary>The distance of the shared baseline of the first-baseline group from the cross-start edge of the line.</summary>
        public double StartBaseline { get; set; }

        /// <summary>The distance of the shared baseline of the last-baseline group from the cross-end edge of the line.</summary>
        public double EndBaseline { get; set; }

        /// <summary>Whether the line was built by the second pass, where the collapsed items take no space.</summary>
        public bool IsCollapsedPass { get; set; }
    }

    /// <summary>A flex item (https://drafts.csswg.org/css-flexbox-1/#flex-item) and the state of its layout.</summary>
    private sealed class FlexItem(ContainerBox box, LayoutStyle style, int index, int order)
    {
        public ContainerBox Box { get; } = box;

        public LayoutStyle Style { get; } = style;

        /// <summary>The position of the item among the children of the container (document order).</summary>
        public int Index { get; } = index;

        public int Order { get; } = order;

        public WritingDirection WritingDirection { get; set; }

        /// <summary>Whether the main axis of the container is the inline axis of the item.</summary>
        public bool MainIsInline { get; set; }

        public bool IsCollapsed { get; set; }

        /// <summary>The cross size of the line of a collapsed item in the first pass (https://drafts.csswg.org/css-flexbox-1/#strut-size).</summary>
        public double Strut { get; set; }

        public double Grow { get; set; }

        public double Shrink { get; set; }

        public AxisAlignment Align { get; set; }

        public bool Safe { get; set; }

        /// <summary>Whether the item is stretched in the cross axis (align-self: stretch, auto cross size, no auto cross margin).</summary>
        public bool Stretches { get; set; }

        // The margins in the flex-relative directions (0 for the auto margins), and whether they are auto
        public double MarginMainStart { get; set; }

        public double MarginMainEnd { get; set; }

        public double MarginCrossStart { get; set; }

        public double MarginCrossEnd { get; set; }

        public bool AutoMarginMainStart { get; set; }

        public bool AutoMarginMainEnd { get; set; }

        public bool AutoMarginCrossStart { get; set; }

        public bool AutoMarginCrossEnd { get; set; }

        public double UsedMarginMainStart { get; set; }

        public double UsedMarginMainEnd { get; set; }

        public double UsedMarginCrossStart { get; set; }

        public double UsedMarginCrossEnd { get; set; }

        /// <summary>The sum of the main-axis margins (the auto margins are 0).</summary>
        public double OuterMainMargins => MarginMainStart + MarginMainEnd;

        public double MainPaddingBorder { get; set; }

        public double CrossPaddingBorder { get; set; }

        public double MainGutters { get; set; }

        public double CrossGutters { get; set; }

        public AxisSizing CrossSizing { get; set; }

        /// <summary>
        /// Whether the flex base size of the item is definite (https://drafts.csswg.org/css-flexbox-1/#algo-main-item): its used
        /// flex basis is definite (case A), or its definite cross size is transferred through its preferred aspect ratio (case B).
        /// </summary>
        public bool HasDefiniteFlexBasis { get; set; }

        /// <summary>The definite border-box cross size of the item before the lines are sized, or null.</summary>
        public double? DefiniteCrossSize { get; set; }

        /// <summary>The border-box flex base size (https://drafts.csswg.org/css-flexbox-1/#flex-base-size).</summary>
        public double FlexBaseSize { get; set; }

        /// <summary>The inner (content-box) flex base size, which scales the flex shrink factor.</summary>
        public double InnerFlexBaseSize => Math.Max(0, FlexBaseSize - MainPaddingBorder);

        public double HypotheticalMainSize { get; set; }

        public double MinMainSize { get; set; }

        public double MaxMainSize { get; set; }

        public double TargetMainSize { get; set; }

        public bool Frozen { get; set; }

        public double Violation { get; set; }

        public double HypotheticalCrossSize { get; set; }

        /// <summary>The used border-box cross size.</summary>
        public double CrossSize { get; set; }

        /// <summary>The border-box cross size imposed on the final layout of the item (its stretched size, or its definite cross size), or null.</summary>
        public double? UsedCrossSize { get; set; }

        public BaselineGroup BaselineGroup { get; set; }

        /// <summary>The distance of the baseline from the border edge of the side of the <see cref="BaselineGroup"/> (cross-start or cross-end).</summary>
        public double BaselineOffset { get; set; }

        /// <summary>The distance of the baseline from the border edge of the logical start side of the cross axis.</summary>
        public double BaselineFromLogicalStart { get; set; }

        public FlexLine? Line { get; set; }

        /// <summary>The position of the margin box from the main-start edge of the content box.</summary>
        public double MainPosition { get; set; }

        /// <summary>The position of the margin box from the cross-start edge of its line.</summary>
        public double CrossPosition { get; set; }

        /// <summary>The position of the border box in the content box, in the logical coordinates of the container.</summary>
        public LogicalOffset LogicalOffset { get; set; }

        /// <summary>The last layout of the item and its constraint space.</summary>
        public BoxFragment? Fragment { get; set; }

        public ConstraintSpace Space { get; set; }

        /// <summary>Clamps a border-box main size by the min and max main sizes (the minimum wins) and the paddings and borders.</summary>
        public double ClampMainSize(double size) => Math.Max(MinMainSize, Math.Min(size, MaxMainSize));

        /// <summary>Whether the minimum main size property of the item is <c>auto</c>.</summary>
        public bool HasAutomaticMinimumSize { get; set; }
    }
}
