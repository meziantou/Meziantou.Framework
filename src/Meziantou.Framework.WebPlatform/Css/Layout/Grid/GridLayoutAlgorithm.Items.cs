namespace Meziantou.Framework.WebPlatform.Css.Layout;

internal sealed partial class GridLayoutAlgorithm
{
    // ------------------------------------------------------------------------------------------------------------------
    // Layout of an item
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lays out an item in its grid area, whose size in each axis of the grid is given (null: indefinite, the axis whose
    /// contributions are measured). The last layout of the item is reused when the constraint space is the same.
    /// </summary>
    /// <param name="final">Whether the layout is the final one, in the grid area: the contents of the item are aligned by baseline (see <see cref="GridItem.GetContentBaseline"/>).</param>
    private BoxFragment LayoutItem(GridItem item, double? inlineArea, double? blockArea, bool final = false)
    {
        var space = CreateItemSpace(item, inlineArea, blockArea);
        if (final)
        {
            // https://drafts.csswg.org/css-align-3/#baseline-align-content: the shim of the baseline content-alignment is
            // space in the box of the item, on the side of the baseline of its contents
            var contentAxis = item.IsOrthogonal ? GridAxis.Columns : GridAxis.Rows;
            var contentBaseline = item.GetContentBaseline(contentAxis);
            if (contentBaseline != AxisAlignment.Start && item.GetShim(contentAxis) is var contentShim and > 0)
            {
                space = contentBaseline == AxisAlignment.Baseline ? space with { ContentShimStart = contentShim } : space with { ContentShimEnd = contentShim };
            }
        }

        if (item.Fragment is { } cached && item.Space == space)
            return cached;

        var fragment = _context.LayoutBox(item.Box, space);
        item.Fragment = fragment;
        item.Space = space;
        return fragment;
    }

    /// <summary>
    /// Returns the constraint space of an item: its containing block is its grid area (https://drafts.csswg.org/css-grid-2/#grid-item-sizing),
    /// a stretched item gets the stretch-fit size of the area (clamped by its min and max sizes) as a fixed size; the <c>auto</c>
    /// inline size of the other items is their fit-content size in the area (or the size of a block-level box: <see cref="GridItem.FillsColumns"/>).
    /// </summary>
    private ConstraintSpace CreateItemSpace(GridItem item, double? inlineArea, double? blockArea)
    {
        var margins = GetMargins(item, inlineArea ?? 0);
        double? fixedInline = null;
        double? fixedBlock = null;
        // (a subgrid fills the area in its subgridded axes: its min and max sizes are ignored)
        if (inlineArea is { } inlineSize && item.StretchesColumns)
        {
            fixedInline = item.SubgridsColumns ? Math.Max(0, inlineSize - margins.InlineSum()) : CreateItemSizing(item, GridAxis.Columns, inlineArea, blockArea).Clamp(inlineSize - margins.InlineSum());
        }

        if (blockArea is { } blockSize && item.StretchesRows)
        {
            fixedBlock = item.SubgridsRows ? Math.Max(0, blockSize - margins.BlockSum()) : CreateItemSizing(item, GridAxis.Rows, blockArea, inlineArea).Clamp(blockSize - margins.BlockSum());
        }

        return new ConstraintSpace
        {
            WritingDirection = _writingDirection,
            AvailableInlineSize = inlineArea ?? 0,
            AvailableBlockSize = blockArea,
            PercentageInlineSize = inlineArea ?? 0,
            PercentageBlockSize = blockArea,
            IsInlineSizeIndefinite = inlineArea is null,
            FixedInlineSize = fixedInline,
            FixedBlockSize = fixedBlock,
            AutoInlineSizeIsFitContent = fixedInline is null && !item.FillsColumns,
        };
    }

    /// <summary>
    /// Returns the sizing of an item in an axis of the grid (<see cref="AxisSizing"/>): its sizing properties resolve against the
    /// size of its grid area in that axis (null: indefinite), its paddings against the inline size of the area.
    /// </summary>
    private AxisSizing CreateItemSizing(GridItem item, GridAxis axis, double? areaSize, double? otherAreaSize)
    {
        var horizontal = IsHorizontal(axis);
        var boxModel = item.Style.Box;
        var inlineArea = axis == GridAxis.Columns ? areaSize : otherAreaSize;
        var paddingAndBorder = FormattingContext.ResolvePadding(boxModel, inlineArea ?? 0).Add(boxModel.BorderWidth);
        var gutters = IntrinsicSizing.GetScrollbarGutters(_context, item.Box, item.WritingDirection);
        var axisPaddingAndBorder = horizontal ? paddingAndBorder.HorizontalSum() : paddingAndBorder.VerticalSum();
        var isInlineAxis = item.IsInlineAxis(axis);
        var margins = GetMargins(item, inlineArea ?? 0);
        var axisMargins = axis == GridAxis.Columns ? margins.InlineSum() : margins.BlockSum();

        double? contentBlockSize = null;
        if (isInlineAxis)
        {
            // The definite block size of the item (its preferred block size, or its stretched size in the other axis): the input of
            // its intrinsic inline sizes
            var otherAxis = axis == GridAxis.Columns ? GridAxis.Rows : GridAxis.Columns;
            var otherPaddingAndBorder = horizontal ? paddingAndBorder.VerticalSum() : paddingAndBorder.HorizontalSum();
            var otherSizing = new AxisSizing(_context, item.Box)
            {
                Horizontal = !horizontal,
                IsInlineAxis = false,
                PercentageBasis = otherAreaSize,
                PaddingAndBorder = otherPaddingAndBorder,
                Edges = otherPaddingAndBorder + gutters.BlockSum(),
            };

            var blockSize = otherSizing.Resolve(item.Style.Box.GetSize(!horizontal), SizingProperty.Preferred);
            if (blockSize is null && otherAreaSize is { } other && item.Stretches(otherAxis))
            {
                blockSize = other - (otherAxis == GridAxis.Columns ? margins.InlineSum() : margins.BlockSum());
            }

            contentBlockSize = blockSize is { } size ? Math.Max(0, otherSizing.Clamp(size) - otherSizing.Edges) : null;
        }

        return new AxisSizing(_context, item.Box)
        {
            Horizontal = horizontal,
            IsInlineAxis = isInlineAxis,
            PercentageBasis = areaSize,
            PaddingAndBorder = axisPaddingAndBorder,
            Edges = axisPaddingAndBorder + (isInlineAxis ? gutters.InlineSum() : gutters.BlockSum()),
            StretchSize = areaSize is { } area ? Math.Max(area - axisMargins, axisPaddingAndBorder) : null,
            ContentBlockSize = contentBlockSize,
        };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Contributions
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns a contribution of a sizing item to the tracks it spans (an outer size: its margins, its extra margins as the item
    /// of a subgrid and its baseline shim included).
    /// </summary>
    private double GetContribution(GridSizingItem item, GridContributionKind kind)
    {
        if (item.IsPlaceholder)
            return item.ExtraStart + item.ExtraEnd;

        var contributions = item.Owner.GetContributions(item.Item, item.OwnerAxis);
        var extra = item.ExtraStart + item.ExtraEnd + item.Shim;
        switch (kind)
        {
            case GridContributionKind.Minimum:
                return GetMinimumContribution(item, contributions) + extra;
            case GridContributionKind.MinContent:
                return contributions.MinContent + extra;
            case GridContributionKind.MaxContent:
                return contributions.MaxContent + extra;
            default:
            {
                // https://drafts.csswg.org/css-grid-2/#limited-contribution: "its min-/max-content contribution (accordingly),
                // limited by the max track sizing function (which could be the argument to a fit-content() track sizing function)
                // if that is fixed and ultimately floored by its minimum contribution"; for a spanning item, "the sum of the fixed
                // max track sizing functions of any tracks it spans, and is applied if it only spans such tracks"
                var value = (kind == GridContributionKind.LimitedMinContent ? contributions.MinContent : contributions.MaxContent) + extra;
                var tracks = GetTracks(item.Axis);
                var limit = GetGuttersWithin(item.Axis, item.Start, item.End);
                for (var i = item.Start; i < item.End; i++)
                {
                    var track = tracks[i];
                    if (track.Max == GridMaxFunction.Fixed)
                    {
                        limit += track.MaxValue;
                    }
                    else if (track.Max == GridMaxFunction.FitContent && !double.IsPositiveInfinity(track.FitContentLimit))
                    {
                        limit += track.FitContentLimit;
                    }
                    else
                    {
                        limit = double.PositiveInfinity;
                        break;
                    }
                }

                return Math.Max(Math.Min(value, limit), GetMinimumContribution(item, contributions) + extra);
            }
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#minimum-contribution: "if the item's computed preferred size behaves as auto or depends
    /// on the size of its containing block in the relevant axis, its minimum contribution is the outer size that would result from
    /// assuming the item's used minimum size as its preferred size; else the item's minimum contribution is its min-content
    /// contribution". The used minimum size of <c>min-width/min-height: auto</c> is the automatic minimum size
    /// (https://drafts.csswg.org/css-grid-2/#min-size-auto), which depends on the tracks the item spans. Without the extra margins.
    /// </summary>
    private double GetMinimumContribution(GridSizingItem item, in GridContributions contributions)
    {
        if (!contributions.DependsOnContainingBlock)
            return contributions.MinContent;

        var size = contributions.MinSizeIsAuto ? GetAutomaticMinimumSize(item, contributions) : contributions.MinSize;
        return Math.Min(Math.Max(size, contributions.PaddingAndBorder) + contributions.Margins, contributions.MinContent);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#min-size-auto: the automatic minimum size of an item is its content-based minimum size
    /// when "its computed overflow is not a scrollable overflow value, it spans at least one track in that axis whose min track
    /// sizing function is auto, if it spans more than one track in that axis, none of those tracks are flexible", else 0; "if in a
    /// given dimension the grid item spans only grid tracks that have a fixed max track sizing function, then its specified size
    /// suggestion and content size suggestion in that dimension [...] are further clamped to less than or equal to the stretch fit
    /// into the grid area's maximum size in that dimension, as represented by the sum of those grid tracks' max track sizing
    /// functions plus any intervening fixed gutters" (the extra margins of the subgrids and the baseline shim are margins too).
    /// </summary>
    private double GetAutomaticMinimumSize(GridSizingItem item, in GridContributions contributions)
    {
        if (contributions.IsScrollable)
            return 0;

        var tracks = GetTracks(item.Axis);
        var spansAutoMin = false;
        var spansFlexible = false;
        var allFixedMax = true;
        var fixedMaxSum = GetGuttersWithin(item.Axis, item.Start, item.End);
        for (var i = item.Start; i < item.End; i++)
        {
            var track = tracks[i];
            spansAutoMin |= track.Min == GridMinFunction.Auto;
            spansFlexible |= track.IsFlexible;
            if (track.Max == GridMaxFunction.Fixed)
            {
                fixedMaxSum += track.MaxValue;
            }
            else
            {
                allFixedMax = false;
            }
        }

        if (!spansAutoMin || (item.Span > 1 && spansFlexible))
            return 0;

        var result = contributions.ContentSuggestion;
        if (allFixedMax)
        {
            result = Math.Min(result, Math.Max(0, fixedMaxSum - contributions.Margins - item.ExtraStart - item.ExtraEnd - item.Shim));
        }

        return result;
    }

    /// <summary>
    /// Returns the intrinsic size contributions of an item to an axis of its grid (https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution),
    /// cached for the size of its grid area in the other axis: in its inline axis, from its intrinsic sizes
    /// (<see cref="IntrinsicSizing.GetContribution"/>); in its block axis, from its layout with the size of its area in the other
    /// axis (the min-content and max-content contributions are its block size). The data of its minimum contribution: its used
    /// minimum size, or its content-based minimum size before the clamping by the tracks it spans.
    /// </summary>
    private GridContributions GetContributions(GridItem item, GridAxis axis)
    {
        var otherAxis = axis == GridAxis.Columns ? GridAxis.Rows : GridAxis.Columns;
        var otherAreaSize = GetAreaSize(item, otherAxis);
        var cached = axis == GridAxis.Columns ? item.ColumnContributions : item.RowContributions;
        if (cached.IsValid && cached.OtherAreaSize == otherAreaSize)
            return cached;

        var result = new GridContributions { IsValid = true, OtherAreaSize = otherAreaSize };
        var horizontal = IsHorizontal(axis);
        var boxModel = item.Style.Box;
        var preferred = boxModel.GetSize(horizontal);
        AxisSizing sizing;
        if (item.IsInlineAxis(axis))
        {
            // The containing block of the contributions: the grid (or, for an orthogonal item measured in the rows, the item itself:
            // the rows are its inline axis, the columns its block axis)
            var containingBlock = axis == GridAxis.Columns ? _writingDirection : item.WritingDirection;
            // (the block size of an item stretched in the rows is definite once the rows are sized)
            var stretches = axis == GridAxis.Columns && !item.IsOrthogonal && otherAreaSize is not null && (item.StretchesRows || item.TransfersStretchedBlockSize);
            var (minContent, maxContent) = IntrinsicSizing.GetContribution(_context, item.Box, containingBlock, otherAreaSize, stretches);
            result.MinContent = minContent;
            result.MaxContent = maxContent;
            result.DependsOnContainingBlock = DependsOnContainingBlock(preferred, inBlockAxis: false);
            var margins = FormattingContext.ResolveMargins(boxModel, 0);
            result.Margins = horizontal ? margins.HorizontalSum() : margins.VerticalSum();
            sizing = CreateItemSizing(item, axis, areaSize: null, otherAreaSize);
        }
        else
        {
            // The block axis of the item: its block size laid out with the size of its area in the other axis. The columns of
            // a subgrid that are not sized yet (its standalone axis, when its items are measured for the tracks of its parent) are
            // infinite (https://drafts.csswg.org/css-grid-2/#algo-grid-sizing: "assume the available space that it would have
            // if any row with a definite max track sizing function had that size and all other rows were infinite"): the area
            // of the item is its max-content contribution
            var inlineArea = otherAreaSize;
            if (axis == GridAxis.Rows && inlineArea is null && !_columnsSized)
            {
                inlineArea = IntrinsicSizing.GetContribution(_context, item.Box, _writingDirection, containingBlockBlockSize: null, stretchesBlockSize: false).MaxContent;
            }

            var fragment = axis == GridAxis.Rows ? LayoutItem(item, inlineArea, blockArea: null) : LayoutItem(item, inlineArea: null, otherAreaSize);
            var size = _writingDirection.ToLogical(fragment.Size);
            var margins = GetMargins(item, (axis == GridAxis.Rows ? otherAreaSize : null) ?? 0);
            result.Margins = axis == GridAxis.Rows ? margins.BlockSum() : margins.InlineSum();
            var outer = (axis == GridAxis.Rows ? size.Block : size.Inline) + result.Margins;
            result.MinContent = outer;
            result.MaxContent = outer;
            result.DependsOnContainingBlock = DependsOnContainingBlock(preferred, inBlockAxis: true);
            sizing = CreateItemSizing(item, axis, areaSize: null, otherAreaSize) with { ContentSize = fragment.IntrinsicBlockSize };
        }

        result.PaddingAndBorder = sizing.PaddingAndBorder;
        if (result.DependsOnContainingBlock)
        {
            var minSize = boxModel.GetMinSize(horizontal);
            result.MinSizeIsAuto = minSize.IsAuto;
            if (minSize.IsAuto)
            {
                result.IsScrollable = BlockFormattingContext.GetContentsStyle(item.Box) is var contentsStyle && ScrollContainers.IsScrollable(horizontal ? contentsStyle.OverflowX : contentsStyle.OverflowY);
                result.ContentSuggestion = GetContentBasedMinimumSize(item, axis, sizing);
            }
            else
            {
                result.MinSize = sizing.Resolve(minSize, SizingProperty.Minimum) ?? 0;
            }
        }

        if (axis == GridAxis.Columns)
        {
            item.ColumnContributions = result;
        }
        else
        {
            item.RowContributions = result;
        }

        return result;
    }

    /// <summary>
    /// Returns whether a preferred size behaves as <c>auto</c> or depends on the size of the containing block (a percentage,
    /// <c>stretch</c>), for the minimum contribution; the intrinsic size keywords of the block axis behave as <c>auto</c>
    /// (<c>fit-content</c> does not depend on the containing block for this purpose: WPT css/css-grid/grid-items/grid-item-min-contribution-fit-content-001.html).
    /// </summary>
    private static bool DependsOnContainingBlock(Size size, bool inBlockAxis)
    {
        if (size.BehavesAs == SizeKind.Auto || (inBlockAxis && size.IsContentBasedInBlockAxis))
            return true;

        return size.Kind switch
        {
            SizeKind.LengthPercentage or SizeKind.FitContentFunction => size.Value.HasPercentage,
            SizeKind.Stretch => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns the content-based minimum size of an item (https://drafts.csswg.org/css-grid-2/#content-based-minimum-size) before
    /// its clamping by the tracks it spans: "its specified size suggestion if it exists, otherwise its transferred size suggestion
    /// if that exists and the element is replaced, else its content size suggestion", clamped by its maximum size ("In all cases,
    /// the size suggestion is additionally clamped by the maximum size in the affected axis, if it's definite. If the item is a
    /// compressible replaced element, and has a definite preferred size or maximum size in the relevant axis, the size suggestion is
    /// capped by those sizes; for this purpose, any indefinite percentages in these sizes are resolved against zero").
    /// </summary>
    private double GetContentBasedMinimumSize(GridItem item, GridAxis axis, in AxisSizing sizing)
    {
        var horizontal = IsHorizontal(axis);
        var boxModel = item.Style.Box;
        double result;
        if (item.Box.Kind == ContainerBoxKind.Replaced && GetTransferredSizeSuggestion(item, axis, sizing) is { } transferred)
        {
            result = transferred;
        }
        else if (sizing.IsInlineAxis)
        {
            // "The content size suggestion is the min-content size in the relevant axis"
            result = sizing.GetIntrinsicSizes().MinContent;
        }
        else
        {
            result = sizing.ContentSize ?? sizing.PaddingAndBorder;
        }

        if (sizing.Resolve(boxModel.GetMaxSize(horizontal), SizingProperty.Maximum) is { } max)
        {
            result = Math.Min(result, max);
        }

        if (IntrinsicSizing.IsCompressibleReplacedElement(item.Box))
        {
            var zeroBasis = sizing with { PercentageBasis = sizing.PercentageBasis ?? 0 };
            if (zeroBasis.Resolve(boxModel.GetSize(horizontal), SizingProperty.Preferred) is { } preferredSize)
            {
                result = Math.Min(result, preferredSize);
            }

            if (zeroBasis.Resolve(boxModel.GetMaxSize(horizontal), SizingProperty.Maximum) is { } maxSize)
            {
                result = Math.Min(result, maxSize);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the transferred size suggestion of an item (https://drafts.csswg.org/css-grid-2/#transferred-size-suggestion): its
    /// definite preferred size in the other axis (clamped by its min and max sizes) converted through its preferred aspect ratio, or null.
    /// </summary>
    private double? GetTransferredSizeSuggestion(GridItem item, GridAxis axis, in AxisSizing sizing)
    {
        var boxModel = item.Style.Box;
        var horizontal = IsHorizontal(axis);
        var paddingAndBorder = item.WritingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, 0).Add(boxModel.BorderWidth));
        var naturalRatio = item.Box.Kind == ContainerBoxKind.Replaced ? ReplacedContent.Get(_context, item.Box).NaturalSize.AspectRatio : null;
        if (FormattingContext.GetPreferredAspectRatio(item.Box, item.WritingDirection.IsHorizontal, paddingAndBorder, naturalRatio) is not { } ratio)
            return null;

        // The other axis of the grid is the block axis of the item when the axis is its inline axis
        var otherIsItemInline = sizing.IsInlineAxis == false;
        var otherSizing = new AxisSizing(_context, item.Box)
        {
            Horizontal = !horizontal,
            IsInlineAxis = otherIsItemInline,
            PaddingAndBorder = otherIsItemInline ? paddingAndBorder.InlineSum() : paddingAndBorder.BlockSum(),
        };

        if (otherSizing.Resolve(boxModel.GetSize(!horizontal), SizingProperty.Preferred) is not { } other)
            return null;

        other = otherSizing.Clamp(other);
        var transferred = sizing.IsInlineAxis ? ratio.ToInline(other) : ratio.ToBlock(other);

        // "If the item has a definite preferred size or maximum size in the relevant axis, the transferred size suggestion is capped
        // by those sizes; for this purpose, any indefinite percentages in these sizes are resolved against zero"
        var zeroBasis = sizing with { PercentageBasis = sizing.PercentageBasis ?? 0 };
        if (zeroBasis.Resolve(boxModel.GetSize(horizontal), SizingProperty.Preferred) is { } preferredSize)
        {
            transferred = Math.Min(transferred, preferredSize);
        }

        if (zeroBasis.Resolve(boxModel.GetMaxSize(horizontal), SizingProperty.Maximum) is { } maxSize)
        {
            transferred = Math.Min(transferred, maxSize);
        }

        return transferred;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Baseline alignment
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The shared baselines of the baseline-sharing groups of the columns and of the rows, by track (first baseline: the start track, last baseline: the end track).</summary>
    private Dictionary<int, double>? _columnStartBaselines;
    private Dictionary<int, double>? _columnEndBaselines;
    private Dictionary<int, double>? _rowStartBaselines;
    private Dictionary<int, double>? _rowEndBaselines;

    /// <summary>
    /// https://drafts.csswg.org/css-grid-2/#algo-baseline-shims: "Shim baseline-aligned items so their intrinsic size contributions
    /// reflect their baseline alignment": before the rows (columns) are sized, the baselines of the items aligned by baseline in
    /// that axis are measured with the sizes of the other axis, and the shims added to their contributions.
    /// </summary>
    private void ComputeBaselineShims(GridAxis axis)
    {
        var hasBaselineItems = false;
        foreach (var item in _items)
        {
            item.LanesShims = null;
            if (axis == GridAxis.Columns)
            {
                item.ColumnShim = 0;
            }
            else
            {
                item.RowShim = 0;
            }

            // (the items of a subgrid may be aligned by baseline with the items of the grid)
            hasBaselineItems |= item.GetAlignment(axis) is AxisAlignment.Baseline or AxisAlignment.LastBaseline || item.Subgrid is not null
                || (!_isLanes && item.Style.Alignment.AlignContent.Keyword is AlignmentKeyword.Baseline or AlignmentKeyword.LastBaseline);
        }

        if (!hasBaselineItems)
            return;

        ComputeBaselines(axis, final: false);
    }

    /// <summary>Computes the baselines and the shared baselines of the items aligned by baseline in both axes, with their final layouts.</summary>
    private void ComputeItemBaselines(bool final)
    {
        ComputeBaselines(GridAxis.Columns, final);
        ComputeBaselines(GridAxis.Rows, final);
    }

    /// <summary>
    /// Computes the baseline-sharing groups of an axis (https://drafts.csswg.org/css-align-3/#baseline-sharing-group: the items of
    /// a row for <c>align-self</c>, of a column for <c>justify-self</c>, by their start track for the first baseline and their end
    /// track for the last baseline), the baseline of each item, the shared baselines and the shims. The items of the subgrids
    /// subgridded in the axis participate with the items of the grid ("The grid items of subgrids participate in alignment,
    /// including baseline alignment, together with their parent grid's items", https://drafts.csswg.org/css-grid-2/#alignment): the
    /// baselines are measured from the start (end) edge of the track, the extra margins of the items included, and the shared
    /// baselines of a subgrid include the ones given by its parent (<see cref="SubgridAxisInput.StartBaselines"/>).
    /// </summary>
    private void ComputeBaselines(GridAxis axis, bool final)
    {
        Dictionary<int, double>? startBaselines = null;
        Dictionary<int, double>? endBaselines = null;
        var tracks = GetTracks(axis);
        var participants = new List<GridSizingItem>();
        CollectSizingItems(axis, participants);
        participants.RemoveAll(static participant => participant.IsPlaceholder || participant.IsHypothetical);
        foreach (var participant in participants)
        {
            var item = participant.Item;
            var owner = participant.Owner;
            var ownerAxis = participant.OwnerAxis;
            var isOwn = ReferenceEquals(owner, this);
            var storedGroup = item.GetBaselineGroup(axis);
            if (isOwn)
            {
                SetBaselineGroup(item, axis, GridBaselineGroup.None);
            }

            // https://drafts.csswg.org/css-align-3/#baseline-align-content: "A grid item participates in first (last) baseline
            // content-alignment in either its row or column (whichever matches its inline axis) if its computed align-content
            // is first baseline (last baseline), and its computed align-self or justify-self (whichever affects its block
            // axis) is stretch or self-start (self-end)": the item is then in the baseline-sharing group of its track, with
            // the items that are aligned by baseline themselves (the items of the grid only, not the ones of its subgrids)
            var alignment = item.GetAlignment(ownerAxis);
            var contentAlignment = AxisAlignment.Start;
            if (isOwn && !_isLanes && alignment is not (AxisAlignment.Baseline or AxisAlignment.LastBaseline) && (axis == GridAxis.Rows) != item.IsOrthogonal
                && item.Box.IsBlockContainer && !item.Box.EstablishesIndependentChildrenLayout)
            {
                contentAlignment = item.Style.Alignment.AlignContent.Keyword switch
                {
                    AlignmentKeyword.Baseline => AxisAlignment.Baseline,
                    AlignmentKeyword.LastBaseline => AxisAlignment.LastBaseline,
                    _ => AxisAlignment.Start,
                };
            }

            var isContentAligned = contentAlignment != AxisAlignment.Start;
            if (alignment is not (AxisAlignment.Baseline or AxisAlignment.LastBaseline) && !isContentAligned)
            {
                ClearContentBaseline();
                continue;
            }

            // https://drafts.csswg.org/css-grid-2/#row-align: "If baseline alignment is specified on a grid item whose size in that
            // axis depends on the size of an intrinsically-sized track [...] that item does not participate in baseline alignment,
            // and instead uses its fallback alignment as if that were originally specified" (flexible tracks count as intrinsic when
            // the size of the grid container is indefinite). As in the browsers, only a self-aligned item that synthesizes its baseline
            // has such a cycle: the baseline of its contents does not depend on its size (WPT
            // css/css-grid/alignment/grid-self-baseline-not-applied-if-sizing-cyclic-dependency-003.html); the shim of a content-aligned
            // item is in its box, whose size depends on the track (WPT css/css-grid/alignment/grid-baseline-align-cycles-001.html)
            if (isOwn && DependsOnIntrinsicTrack(item, axis, tracks) && (isContentAligned || SynthesizesBaseline(item, axis, final, alignment == AxisAlignment.LastBaseline)))
            {
                if (isContentAligned)
                {
                    ClearContentBaseline();
                }
                else
                {
                    SetAlignment(item, axis, alignment == AxisAlignment.Baseline ? AxisAlignment.Start : AxisAlignment.End);
                }

                continue;
            }

            // An item with an auto margin in the axis is aligned by its margins: it does not participate in baseline alignment
            // (https://drafts.csswg.org/css-grid-2/#auto-margins)
            if (ownerAxis == GridAxis.Columns ? item.AutoMarginColumnStart || item.AutoMarginColumnEnd : item.AutoMarginRowStart || item.AutoMarginRowEnd)
            {
                ClearContentBaseline();
                continue;
            }

            GridBaselineGroup group;
            double fromTrackEdge;
            if (isContentAligned && final)
            {
                // (the final layout of the item has its shim: its baseline is the one that was measured without it)
                if (item.GetContentBaseline(axis) == AxisAlignment.Start || storedGroup == GridBaselineGroup.None)
                    continue;

                group = storedGroup;
                fromTrackEdge = axis == GridAxis.Columns ? item.ColumnBaseline : item.RowBaseline;
            }
            else
            {
                var fragment = isOwn && final ? item.Fragment! : owner.MeasureForBaseline(item, ownerAxis);
                var last = (isContentAligned ? contentAlignment : alignment) == AxisAlignment.LastBaseline;
                var size = owner._writingDirection.ToLogical(fragment.Size);
                var axisSize = ownerAxis == GridAxis.Columns ? size.Inline : size.Block;
                var margins = owner.GetMargins(item, ownerAxis == GridAxis.Rows ? owner.GetAreaSize(item, GridAxis.Columns) ?? 0 : 0);
                var (marginStart, marginEnd) = ownerAxis == GridAxis.Columns ? (margins.InlineStart, margins.InlineEnd) : (margins.BlockStart, margins.BlockEnd);

                // The baseline from the start edge of the border box in the axis (the baselines of an item whose block flow is opposite
                // are on the other side: its first baseline aligns with the last baselines, https://drafts.csswg.org/css-align-3/#compatible-baseline-alignment-preferences)
                var (baseline, flipped) = owner.GetItemBaseline(item, fragment, ownerAxis, last);
                if (participant.IsReversed)
                {
                    // The item of a subgrid whose tracks are in the opposite direction: its baseline is measured from the other side
                    // (its first baseline aligns with the last baselines of the grid)
                    baseline = axisSize - baseline;
                    flipped = !flipped;
                    (marginStart, marginEnd) = (marginEnd, marginStart);
                }

                if (flipped)
                {
                    last = !last;
                }

                // The distance of the baseline from the start (end) edge of the track
                group = last ? GridBaselineGroup.End : GridBaselineGroup.Start;
                fromTrackEdge = last ? participant.ExtraEnd + marginEnd + (axisSize - baseline) : participant.ExtraStart + marginStart + baseline;
                if (isContentAligned)
                {
                    // The edge of the box on the side of the baseline is the edge of its area: the box is stretched, or aligned
                    // to that side
                    if (alignment != AxisAlignment.Stretch && alignment != (last ? AxisAlignment.End : AxisAlignment.Start))
                    {
                        ClearContentBaseline();
                        continue;
                    }

                    if (axis == GridAxis.Columns)
                    {
                        item.ColumnContentBaseline = contentAlignment;
                    }
                    else
                    {
                        item.RowContentBaseline = contentAlignment;
                    }
                }
            }

            // (the baseline of the item of a reversed subgrid is the one of its own direction for the subgrid, which aligns it)
            if (!participant.IsReversed)
            {
                SetBaselineGroup(item, ownerAxis, group);
                if (ownerAxis == GridAxis.Columns)
                {
                    item.ColumnBaseline = fromTrackEdge;
                }
                else
                {
                    item.RowBaseline = fromTrackEdge;
                }
            }

            var isEndGroup = group == GridBaselineGroup.End;
            var key = isEndGroup ? participant.End - 1 : participant.Start;
            var baselines = isEndGroup ? endBaselines ??= [] : startBaselines ??= [];
            baselines[key] = baselines.TryGetValue(key, out var shared) ? Math.Max(shared, fromTrackEdge) : fromTrackEdge;

            void ClearContentBaseline()
            {
                if (!isOwn || final)
                    return;

                if (axis == GridAxis.Columns)
                {
                    item.ColumnContentBaseline = AxisAlignment.Start;
                }
                else
                {
                    item.RowContentBaseline = AxisAlignment.Start;
                }
            }
        }

        // The baselines shared with the items of the parent grid
        if (_input?.Get(axis) is { } input)
        {
            MergeBaselines(ref startBaselines, input.StartBaselines);
            MergeBaselines(ref endBaselines, input.EndBaselines);
        }

        if (axis == GridAxis.Columns)
        {
            _columnStartBaselines = startBaselines;
            _columnEndBaselines = endBaselines;
        }
        else
        {
            _rowStartBaselines = startBaselines;
            _rowEndBaselines = endBaselines;
        }

        // The shims: the space added on the side of the group so that the baselines of the items align
        foreach (var participant in participants)
        {
            var item = participant.Item;
            var ownerAxis = participant.OwnerAxis;
            var group = item.GetBaselineGroup(ownerAxis);
            if (participant.IsReversed && !ReferenceEquals(participant.Owner, this))
                continue;

            var baseline = ownerAxis == GridAxis.Columns ? item.ColumnBaseline : item.RowBaseline;
            var shim = group switch
            {
                GridBaselineGroup.Start when startBaselines is not null && startBaselines.TryGetValue(participant.Start, out var shared) => shared - baseline,
                GridBaselineGroup.End when endBaselines is not null && endBaselines.TryGetValue(participant.End - 1, out var shared) => shared - baseline,
                _ => 0,
            };

            // (an auto-placed item of a grid lanes container has a shim in each position it is sized in)
            if (_isLanes && !_lanesPlaced && item.IsLanesAuto && ReferenceEquals(participant.Owner, this) && ownerAxis == _laneAxis)
            {
                (item.LanesShims ??= new double[tracks.Length])[participant.Start] = shim;
            }

            if (ownerAxis == GridAxis.Columns)
            {
                item.ColumnShim = shim;
            }
            else
            {
                item.RowShim = shim;
            }
        }

        static void MergeBaselines(ref Dictionary<int, double>? baselines, Dictionary<int, double>? inherited)
        {
            if (inherited is null)
                return;

            baselines ??= [];
            foreach (var (key, value) in inherited)
            {
                baselines[key] = baselines.TryGetValue(key, out var own) ? Math.Max(own, value) : value;
            }
        }
    }

    /// <summary>Returns the layout of an item in its grid area with an indefinite size in an axis (the layout that measures its contributions in that axis).</summary>
    private BoxFragment MeasureForBaseline(GridItem item, GridAxis axis)
    {
        var otherAxis = axis == GridAxis.Columns ? GridAxis.Rows : GridAxis.Columns;
        return axis == GridAxis.Rows ? LayoutItem(item, GetAreaSize(item, otherAxis), blockArea: null) : LayoutItem(item, inlineArea: null, GetAreaSize(item, otherAxis));
    }

    /// <summary>
    /// Gives the shared baselines of the tracks of each axis to the subgrids subgridded in it (from the other side of the tracks for
    /// a subgrid whose lines go in the opposite direction): their items align with the items of the grid.
    /// </summary>
    private void PropagateBaselinesToSubgrids()
    {
        foreach (var item in _items)
        {
            if (item.Subgrid is not { } subgrid)
                continue;

            foreach (var axis in (ReadOnlySpan<GridAxis>)[GridAxis.Columns, GridAxis.Rows])
            {
                if (!subgrid.IsSubgriddedIn(axis, out var ownAxis, out var reversed) || subgrid._input?.Get(ownAxis) is not { } input)
                    continue;

                // (the tracks of a reversed subgrid are in the opposite direction: the baselines shared from the start edges of the
                // tracks of the grid are shared from their end edges for the subgrid)
                var (start, end) = axis == GridAxis.Columns ? (_columnStartBaselines, _columnEndBaselines) : (_rowStartBaselines, _rowEndBaselines);
                input.StartBaselines = Map(reversed ? end : start, item.GetStart(axis), item.GetSpan(axis), reversed);
                input.EndBaselines = Map(reversed ? start : end, item.GetStart(axis), item.GetSpan(axis), reversed);
            }
        }

        static Dictionary<int, double>? Map(Dictionary<int, double>? baselines, int start, int span, bool reversed)
        {
            if (baselines is null)
                return null;

            Dictionary<int, double>? result = null;
            foreach (var (key, value) in baselines)
            {
                if (key >= start && key < start + span)
                {
                    (result ??= [])[reversed ? span - 1 - (key - start) : key - start] = value;
                }
            }

            return result;
        }
    }

    /// <summary>Returns whether an item synthesizes its first (last) baseline in an axis: it has no baseline in that axis (an orthogonal item, or an item without a line box).</summary>
    private bool SynthesizesBaseline(GridItem item, GridAxis axis, bool final, bool last)
    {
        if ((axis == GridAxis.Rows) == item.IsOrthogonal)
            return true;

        var fragment = final ? item.Fragment! : MeasureForBaseline(item, axis);
        return (last ? fragment.LastBaseline : fragment.FirstBaseline) is null;
    }

    /// <summary>Returns whether the size of an item in an axis depends on the size of an intrinsically-sized track it spans (it then does not participate in baseline alignment).</summary>
    private bool DependsOnIntrinsicTrack(GridItem item, GridAxis axis, GridTrack[] tracks)
    {
        var preferred = GetPreferredSize(item, axis);
        if (!DependsOnContainingBlock(preferred, inBlockAxis: false) || preferred.IsAuto)
            return false;

        // (the inline size of a container sized to its contents is not definite, even once known)
        var containerSizeIsIndefinite = axis == GridAxis.Columns ? _inlineSize is null || _content.InlineSizeDependsOnContents : _blockSize is null;
        for (var i = item.GetStart(axis); i < item.GetEnd(axis); i++)
        {
            var track = tracks[i];
            if (track.HasIntrinsicMin || track.HasIntrinsicMax || (track.IsFlexible && containerSizeIsIndefinite))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the first (<paramref name="last"/>: last) baseline of an item (its alignment preference, in its own block flow) in
    /// an axis of the grid, from the start edge of its border box in that axis (in the writing mode of the grid), and whether the baselines of the item are flipped (its block-start side is the end side of the axis: its
    /// first baseline aligns with the last baselines, https://drafts.csswg.org/css-align-3/#compatible-baseline-alignment-preferences).
    /// The items without a baseline in the axis get a baseline synthesized from their border box
    /// (https://drafts.csswg.org/css-align-3/#synthesize-baseline): the line-under edge of their writing mode, or of an
    /// axis-compatible writing mode when their block flow direction is parallel to the axis of the alignment context (the
    /// writing mode of the grid for an orthogonal item in the rows; vertical-lr/vertical-rl (from the direction of the item) for an
    /// item in the columns of a horizontal grid); the central baseline in a vertical grid, as in the browsers.
    /// </summary>
    private (double Baseline, bool Flipped) GetItemBaseline(GridItem item, BoxFragment fragment, GridAxis axis, bool last)
    {
        var logicalSize = _writingDirection.ToLogical(fragment.Size);
        var size = axis == GridAxis.Columns ? logicalSize.Inline : logicalSize.Block;
        var startSide = axis == GridAxis.Columns ? _writingDirection.InlineStart : _writingDirection.BlockStart;

        // The items whose block axis is the axis have baselines in it (the rows for the parallel flows, the columns for the
        // orthogonal flows)
        if ((axis == GridAxis.Rows) != item.IsOrthogonal)
        {
            // The first (last) baseline of the item in its own block flow: when it is flipped, it is measured from the other side
            var flipped = item.WritingDirection.BlockStart != startSide;
            var baseline = last ? fragment.LastBaseline : fragment.FirstBaseline;
            double fromBlockStart;
            if (baseline is { } value)
            {
                fromBlockStart = value;
            }
            else if (!_writingDirection.IsHorizontal && _box.Style.Layout.HasCentralDominantBaseline)
            {
                fromBlockStart = size / 2;
            }
            else
            {
                // The line-under edge of the writing mode of the item: the block-end edge of a horizontal item, the left edge of a
                // vertical one (the right edge in sideways-lr)
                fromBlockStart = item.WritingDirection.LineUnder == item.WritingDirection.BlockStart ? 0 : size;
            }

            return (flipped ? size - fromBlockStart : fromBlockStart, flipped);
        }

        if (!_writingDirection.IsHorizontal)
        {
            if (_box.Style.Layout.HasCentralDominantBaseline)
                return (size / 2, false);

            // The alphabetic baseline (text-orientation: sideways): the line-under edge of the grid in the rows, the bottom edge
            // (horizontal-tb assumed) in the columns
            var lineUnder = axis == GridAxis.Rows ? _writingDirection.LineUnder : PhysicalSide.Bottom;
            return (lineUnder == startSide ? 0 : size, false);
        }

        // An orthogonal item in the rows: the line-under edge of the grid (its block-end edge)
        if (axis == GridAxis.Rows)
            return (size, false);

        // An item in the columns of a horizontal grid: vertical-lr (ltr) or vertical-rl (rtl) is assumed, whose line-under edge is
        // the left edge
        var assumedBlockStart = item.WritingDirection.IsRightToLeft ? PhysicalSide.Right : PhysicalSide.Left;
        var assumedFlipped = assumedBlockStart != startSide;
        var assumed = assumedBlockStart == PhysicalSide.Left ? 0 : size;
        return (assumedFlipped ? size - assumed : assumed, assumedFlipped);
    }

    private static void SetBaselineGroup(GridItem item, GridAxis axis, GridBaselineGroup group)
    {
        if (axis == GridAxis.Columns)
        {
            item.ColumnBaselineGroup = group;
        }
        else
        {
            item.RowBaselineGroup = group;
        }
    }

    private static void SetAlignment(GridItem item, GridAxis axis, AxisAlignment alignment)
    {
        if (axis == GridAxis.Columns)
        {
            item.JustifySelf = alignment;
        }
        else
        {
            item.AlignSelf = alignment;
        }
    }

    /// <summary>
    /// Returns the offset of the margin box of a baseline-aligned item from the start edge of its grid area: the shared baseline of
    /// its group minus its own baseline (from the start edge for the first-baseline group, from the end edge for the last-baseline
    /// group, which is aligned with the end of the area: its fallback alignment).
    /// </summary>
    private double GetBaselineOffset(GridItem item, GridAxis axis, double freeSpace)
    {
        var group = item.GetBaselineGroup(axis);
        var baseline = axis == GridAxis.Columns ? item.ColumnBaseline : item.RowBaseline;
        switch (group)
        {
            case GridBaselineGroup.Start:
            {
                var baselines = axis == GridAxis.Columns ? _columnStartBaselines : _rowStartBaselines;
                return baselines is not null && baselines.TryGetValue(item.GetStart(axis), out var shared) ? shared - baseline : 0;
            }

            case GridBaselineGroup.End:
            {
                var baselines = axis == GridAxis.Columns ? _columnEndBaselines : _rowEndBaselines;
                return baselines is not null && baselines.TryGetValue(item.GetEnd(axis) - 1, out var shared) ? freeSpace - (shared - baseline) : freeSpace;
            }

            default:
                return item.GetAlignment(axis) == AxisAlignment.LastBaseline ? freeSpace : 0;
        }
    }
}
