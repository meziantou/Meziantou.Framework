namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a table wrapper box (https://drafts.csswg.org/css-tables-3/#table-wrapper-box): the principal box of a table,
/// whose children are its table grid box and its captions.
/// </summary>
/// <remarks>
/// <para>
/// The properties of the table are distributed between the two boxes (https://drafts.csswg.org/css-tables-3/#global-style-overrides):
/// the wrapper box takes the margins, the positioning (<c>position</c>, <c>float</c>, the insets), the transforms and
/// <c>overflow</c>; the grid box the sizes, the borders and the paddings. Both boxes have the style of the table: the wrapper
/// box has no border nor padding, and its inline size is the border-box inline size of the grid box ("The width of the
/// table-wrapper box is the border-edge width of the table grid box inside it"); the grid box has no margin. The percentages of
/// the sizes of the grid box resolve against the containing block of the wrapper box.
/// </para>
/// <para>
/// The used width of the table (<see cref="TableLayoutAlgorithm.ComputeTableInlineSize"/>) is never smaller than the
/// min-content contributions of its captions (CAPMIN); the captions are laid out in it, the top ones before the grid box and
/// the bottom ones after it, their margins do not collapse (as in the browsers). The baselines of the table are the ones of
/// its grid box (its first and last rows).
/// </para>
/// </remarks>
internal sealed class TableWrapperFormattingContext : FormattingContext
{
    public static TableWrapperFormattingContext Instance { get; } = new();

    private TableWrapperFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Block;

    public override BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        var layout = box.Style.Layout;
        var boxModel = layout.Box;
        var writingDirection = WritingDirection.FromStyle(layout);
        var containingBlock = space.WritingDirection;
        var isOrthogonal = writingDirection.IsOrthogonalTo(containingBlock);
        var grid = GetGrid(box);
        var margin = ResolveMargins(boxModel, space.PercentageInlineSize);
        var logicalMargin = writingDirection.ToLogical(margin);
        var builder = new FragmentBuilder(writingDirection);

        // The used width of the table (https://drafts.csswg.org/css-tables-3/#computing-the-table-width), in the available space
        // without the margins
        var algorithm = grid is null ? null : new TableLayoutAlgorithm(context, grid);
        var captionMin = GetCaptionsMinContent(context, box, writingDirection);
        var availableInlineSize = isOrthogonal
            ? space.AvailableBlockSize ?? (writingDirection.IsHorizontal ? context.InitialContainingBlockSize.Width : context.InitialContainingBlockSize.Height)
            : space.AvailableInlineSize;
        availableInlineSize -= logicalMargin.InlineSum();
        var fixedInlineSize = isOrthogonal ? space.FixedBlockSize : space.FixedInlineSize;
        var percentageBasis = space.GetPercentageBasis(writingDirection.IsHorizontal);
        double inlineSize;
        if (algorithm is null)
        {
            inlineSize = fixedInlineSize ?? captionMin;
        }
        else if (fixedInlineSize is { } fixedInline)
        {
            // The size imposed by the parent (a stretched flex or grid item): a table is never narrower than its minimum
            inlineSize = Math.Max(fixedInline, Math.Max(algorithm.ComputeGridIntrinsicSizes().MinContent, captionMin));
        }
        else if ((isOrthogonal ? space.StretchBlockSize : space.StretchInlineSize) is { } stretchInline && layout.Box.GetSize(writingDirection.IsHorizontal).IsAuto)
        {
            // An absolutely positioned table stretched by its self-alignment (https://drafts.csswg.org/css-position-3/#abspos-auto-size):
            // a table is never narrower than its minimum
            inlineSize = Math.Max(stretchInline, Math.Max(algorithm.ComputeGridIntrinsicSizes().MinContent, captionMin));
        }
        else if (!space.AutoInlineSizeIsFitContent && !isOrthogonal && layout.Box.AspectRatio.HasRatio && layout.Box.GetSize(writingDirection.IsHorizontal).IsAuto
            && box.Parent is { Display.IsGridContainer: true } && box.IsInFlow)
        {
            // A grid item with an aspect ratio and a normal alignment is sized as a block-level box: it fills its grid area, its
            // block size is transferred from its inline size (as in the browsers, WPT css/css-grid/alignment/grid-item-aspect-ratio-justify-self-002.html)
            inlineSize = Math.Max(availableInlineSize, Math.Max(algorithm.ComputeGridIntrinsicSizes().MinContent, captionMin));
        }
        else
        {
            inlineSize = algorithm.ComputeTableInlineSize(availableInlineSize, space.IsInlineSizeIndefinite ? null : percentageBasis, captionMin, space.GetPercentageBasis(!writingDirection.IsHorizontal));
        }

        // The auto margins of a block-level table in normal flow center it, else it is aligned by its justify-self
        // (https://drafts.csswg.org/css-align-3/#justify-block)
        if (!isOrthogonal && fixedInlineSize is null && box.IsBlockLevel && box.IsInFlow && !space.IsInlineSizeIndefinite && !space.AutoInlineSizeIsFitContent)
        {
            margin = ResolveAutoInlineMargins(boxModel, containingBlock, margin, space.AvailableInlineSize, inlineSize, isAutoInlineSize: false, GetBlockJustification(box, containingBlock));
        }

        // The captions are laid out in the width of the table (their percentages resolve against it)
        var captionSpace = ConstraintSpace.ForContentBox(writingDirection, inlineSize, blockSize: null);
        List<(BoxFragment Fragment, bool IsTop)>? captions = null;
        var captionsBlockSize = 0d;
        foreach (var child in box.Children)
        {
            if (child is not ContainerBox { Display.Internal: DisplayInternal.TableCaption } caption)
                continue;

            var fragment = context.LayoutBox(caption, captionSpace);
            if (fragment.WritingDirection.IsOrthogonalTo(writingDirection))
            {
                CenterOrthogonalCaption(caption, fragment, writingDirection, inlineSize);
            }

            (captions ??= []).Add((fragment, caption.Style.Layout.Table.CaptionSide != CaptionSide.Bottom));
            var captionMargin = writingDirection.ToLogical(fragment.Margin);
            captionsBlockSize += captionMargin.BlockStart + writingDirection.ToLogical(fragment.Size).Block + captionMargin.BlockEnd;
        }

        // The grid box, in the block size left by the captions when the parent imposes a block size
        var fixedBlockSize = isOrthogonal ? space.FixedInlineSize : space.FixedBlockSize;
        if (fixedBlockSize is null && (isOrthogonal ? space.StretchInlineSize : space.StretchBlockSize) is { } stretchBlock && layout.Box.GetSize(!writingDirection.IsHorizontal).IsAuto)
        {
            fixedBlockSize = stretchBlock;
        }

        BoxFragment? gridFragment = null;
        if (algorithm is not null)
        {
            var percentageBlockSize = space.GetPercentageBasis(!writingDirection.IsHorizontal);

            // https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk: the percentage height of a table resolves
            // against the nearest ancestor that is not an auto height block container
            if (percentageBlockSize is null && !isOrthogonal && space.QuirksPercentageBlockSize is { } quirksBasis && layout.Box.GetSize(!writingDirection.IsHorizontal) is { IsLengthPercentage: true, Value.HasPercentage: true }
                && !layout.IsAbsolutelyPositioned)
            {
                percentageBlockSize = quirksBasis;
            }

            if (space.MeasuresIntrinsicBlockSize && !isOrthogonal)
            {
                percentageBlockSize = null;
            }

            gridFragment = algorithm.Layout(inlineSize, space.PercentageInlineSize, percentageBlockSize, fixedBlockSize is { } fixedBlock ? Math.Max(0, fixedBlock - captionsBlockSize) : null);
            inlineSize = Math.Max(inlineSize, writingDirection.ToLogical(gridFragment.Size).Inline);
        }

        // The captions and the grid box, stacked in the block axis
        var blockOffset = 0d;
        double? firstBaseline = null;
        double? lastBaseline = null;
        var intrinsicBlockSize = captionsBlockSize;
        AddCaptions(isTop: true);
        if (gridFragment is not null)
        {
            builder.AddChild(gridFragment, new LogicalOffset(0, blockOffset));
            firstBaseline = gridFragment.FirstBaseline + blockOffset;
            lastBaseline = gridFragment.LastBaseline + blockOffset;
            blockOffset += writingDirection.ToLogical(gridFragment.Size).Block;
            intrinsicBlockSize += gridFragment.IntrinsicBlockSize;
        }

        AddCaptions(isTop: false);
        var blockSize = Math.Max(blockOffset, fixedBlockSize ?? 0);
        var size = writingDirection.ToPhysical(new LogicalSize(inlineSize, blockSize));
        var wrapperFragment = new BoxFragment(box, size, default, default, margin, builder.Build(size))
        {
            FirstBaseline = firstBaseline,
            LastBaseline = lastBaseline,
            ContentsBlockSize = blockSize,
            IntrinsicBlockSize = intrinsicBlockSize,
        };

        PositionedLayout.SetRelativeOffset(box, wrapperFragment, space);
        PositionedLayout.LayoutOutOfFlowDescendants(context, box, wrapperFragment, builder);
        return wrapperFragment;

        void AddCaptions(bool isTop)
        {
            if (captions is null)
                return;

            foreach (var (fragment, top) in captions)
            {
                if (top != isTop)
                    continue;

                var captionMargin = writingDirection.ToLogical(fragment.Margin);
                builder.AddChild(fragment, new LogicalOffset(captionMargin.InlineStart, blockOffset + captionMargin.BlockStart));
                blockOffset += captionMargin.BlockStart + writingDirection.ToLogical(fragment.Size).Block + captionMargin.BlockEnd;
            }
        }
    }

    /// <summary>
    /// Resolves the <c>auto</c> margins of a caption in an orthogonal writing mode in the inline axis of the table (they are not
    /// resolved by the sizing of an orthogonal flow): they center the caption in the table, as for the other captions
    /// (WPT css/css-tables/caption-writing-mode-001.html).
    /// </summary>
    private static void CenterOrthogonalCaption(ContainerBox caption, BoxFragment fragment, WritingDirection writingDirection, double inlineSize)
    {
        var margins = caption.Style.Layout.Box.Margin;
        var start = writingDirection.InlineStart;
        var end = writingDirection.InlineEnd;
        var startIsAuto = margins.Get(start).IsAuto;
        var endIsAuto = margins.Get(end).IsAuto;
        if (!startIsAuto && !endIsAuto)
            return;

        var margin = fragment.Margin;
        var free = inlineSize - writingDirection.ToLogical(fragment.Size).Inline - (startIsAuto ? 0 : margin.Get(start)) - (endIsAuto ? 0 : margin.Get(end));
        free = Math.Max(0, free);
        if (startIsAuto && endIsAuto)
        {
            margin = margin.With(start, free / 2).With(end, free / 2);
        }
        else if (startIsAuto)
        {
            margin = margin.With(start, free);
        }
        else
        {
            margin = margin.With(end, free);
        }

        fragment.Margin = margin;
    }

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content) => new(0);

    /// <summary>
    /// Returns the intrinsic inline sizes of a table wrapper box as a content box relative to the borders and paddings of the
    /// style of the table (the callers add them): the larger of the sizes of the grid box and of CAPMIN
    /// (https://drafts.csswg.org/css-tables-3/#computing-the-table-width).
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        var sizes = GetBorderBoxIntrinsicSizes(context, box);
        var layout = box.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);
        var edges = writingDirection.ToLogical(ResolvePadding(layout.Box, 0).Add(layout.Box.BorderWidth)).InlineSum();
        return sizes.Add(-edges);
    }

    /// <summary>Returns the border-box min-content and max-content inline sizes of a table: GRIDMIN/GRIDMAX and CAPMIN.</summary>
    private static IntrinsicSizes GetBorderBoxIntrinsicSizes(LayoutContext context, ContainerBox box, bool expandsPercentages = false)
    {
        var layout = box.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);
        var captionMin = GetCaptionsMinContent(context, box, writingDirection);
        var algorithm = GetGrid(box) is { } gridBox ? new TableLayoutAlgorithm(context, gridBox) : null;
        var grid = algorithm?.ComputeGridIntrinsicSizes(expandsPercentages) ?? default;
        var min = Math.Max(grid.MinContent, captionMin);
        var max = Math.Max(min, grid.MaxContent);

        // A table in fixed mode whose width is a percentage has an "infinite" max-content size, as in the browsers (WPT
        // css/css-tables/tentative/table-fixed-minmax.html: a 100% wide fixed table fills a shrink-to-fit container)
        if (algorithm is { IsFixed: true } && TableLayoutAlgorithm.Classify(layout.Box.GetSize(writingDirection.IsHorizontal), out _) == TableSizeKind.Percent)
        {
            max = Math.Max(max, TableLayoutAlgorithm.MaxTableInlineSize);
        }

        return new IntrinsicSizes(min, max);
    }

    /// <summary>
    /// Returns the min-content and max-content contributions of a table (https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution):
    /// its outer size with its <c>width</c>, <c>min-width</c> and <c>max-width</c> when they do not depend on the containing
    /// block, a table being never narrower than its min-content size, and its margins. As in the browsers, the max-content width of
    /// its percentage columns makes its max-content contribution wider, unless it is in a table cell (WPT
    /// css/css-tables/percent-width-ignored-*.tentative.html) or in a flex or grid container.
    /// </summary>
    public static IntrinsicSizes GetContribution(LayoutContext context, ContainerBox box)
    {
        var layout = box.Style.Layout;
        var boxModel = layout.Box;
        var writingDirection = WritingDirection.FromStyle(layout);
        var horizontal = writingDirection.IsHorizontal;
        var ignoresPercentages = IgnoresColumnPercentages(box);
        var sizes = GetBorderBoxIntrinsicSizes(context, box, expandsPercentages: !ignoresPercentages);
        var grid = GetGrid(box);
        var edges = 0d;
        if (grid is not null)
        {
            var algorithm = new TableLayoutAlgorithm(context, grid);
            edges = algorithm.Border.InlineSum() + algorithm.GetPadding(0).InlineSum();
        }

        var sizing = new AxisSizing(context, grid ?? box)
        {
            Horizontal = horizontal,
            IsInlineAxis = true,
            PaddingAndBorder = edges,
            Edges = edges,
        };

        var min = sizes.MinContent;
        var max = sizes.MaxContent;
        if (sizing.ResolveContribution(boxModel.GetSize(horizontal), SizingProperty.Preferred) is { } preferred)
        {
            min = Math.Max(min, preferred.MinContent);
            max = Math.Max(sizes.MinContent, preferred.MaxContent);
        }

        if (sizing.ResolveContribution(boxModel.GetMaxSize(horizontal), SizingProperty.Maximum) is { } maximum)
        {
            min = Math.Max(sizes.MinContent, Math.Min(min, maximum.MinContent));
            max = Math.Max(sizes.MinContent, Math.Min(max, maximum.MaxContent));
        }

        if (sizing.ResolveContribution(boxModel.GetMinSize(horizontal), SizingProperty.Minimum) is { } minimum)
        {
            min = Math.Max(min, minimum.MinContent);
            max = Math.Max(max, minimum.MaxContent);
        }

        var margins = writingDirection.ToLogical(ResolveMargins(boxModel, 0)).InlineSum();
        return new IntrinsicSizes(min + margins, Math.Max(min, max) + margins);
    }

    /// <summary>
    /// Returns whether the percentage columns of a table do not make its max-content contribution wider: the table is in a table
    /// cell, or in a flex or grid container (the "infinite" size of a table whose percentage columns leave no space to the other
    /// ones cannot be used by the flex and grid layouts, as in the browsers: WPT css/css-flexbox/item-with-table-with-infinite-max-intrinsic-width.html).
    /// </summary>
    private static bool IgnoresColumnPercentages(ContainerBox box)
    {
        for (var ancestor = box.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Display.Internal == DisplayInternal.TableCell || ancestor.Display.IsFlexContainer || ancestor.Display.IsGridContainer)
                return true;

            if (ancestor.EstablishesIndependentChildrenLayout)
                return false;
        }

        return false;
    }

    /// <summary>Returns CAPMIN (https://drafts.csswg.org/css-tables-3/#capmin): the largest min-content contribution of the captions.</summary>
    private static double GetCaptionsMinContent(LayoutContext context, ContainerBox box, WritingDirection writingDirection)
    {
        var result = 0d;
        foreach (var child in box.Children)
        {
            if (child is ContainerBox { Display.Internal: DisplayInternal.TableCaption } caption)
            {
                result = Math.Max(result, IntrinsicSizing.GetContribution(context, caption, writingDirection, containingBlockBlockSize: null).MinContent);
            }
        }

        return result;
    }

    /// <summary>Returns the table grid box of a table wrapper box.</summary>
    internal static ContainerBox? GetGrid(ContainerBox wrapper)
    {
        foreach (var child in wrapper.Children)
        {
            if (child is ContainerBox { Kind: ContainerBoxKind.TableGrid } grid)
                return grid;
        }

        return null;
    }
}
