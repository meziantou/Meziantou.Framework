using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The min-content and max-content sizes of a box in one axis (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes), or its contributions.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IntrinsicSizes(double MinContent, double MaxContent)
{
    /// <summary>Returns the sizes of a box whose min-content and max-content sizes are the same.</summary>
    public static IntrinsicSizes FromSize(double size) => new(size, size);

    /// <summary>Adds a length (the paddings, borders and margins) to both sizes.</summary>
    public IntrinsicSizes Add(double value) => new(MinContent + value, MaxContent + value);

    /// <summary>
    /// The fit-content size in an available space (https://drafts.csswg.org/css-sizing-3/#fit-content-size), the shrink-to-fit
    /// size of CSS 2 (https://drafts.csswg.org/css2/#shrink-to-fit-float): <c>min(max-content, max(min-content, available))</c>.
    /// </summary>
    public double FitContent(double available) => Math.Min(MaxContent, Math.Max(MinContent, available));
}

/// <summary>The input of the computation of the intrinsic inline sizes of a box (<see cref="FormattingContext.ComputeIntrinsicInlineSizes"/>), which is also the key of their cache.</summary>
/// <param name="ContentBlockSize">
/// The block size of the content box when it is definite (the basis of the percentage block sizes of the children, and the
/// available inline space of the orthogonal flows, https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto), else null.
/// </param>
/// <param name="MinInlineSize">
/// The definite minimum inline size of the content box, when the intrinsic sizes depend on it (the automatic repetitions of the
/// tracks of a grid container, see <see cref="GridLayoutAlgorithm.UsesInlineSizeLimits"/>), else null.
/// </param>
/// <param name="MaxInlineSize">The definite maximum inline size of the content box, when the intrinsic sizes depend on it, else null.</param>
/// <param name="QuirksPercentageBlockSize">
/// In quirks mode, when the block size of the box is indefinite: the basis of the percentage heights of its children (see
/// <see cref="ConstraintSpace.QuirksPercentageBlockSize"/>), which a replaced element with an aspect ratio transfers to its
/// inline size, else null.
/// </param>
/// <param name="MinBlockSize">
/// When <paramref name="ContentBlockSize"/> is null, the definite minimum block size of the content box, when the intrinsic sizes
/// depend on it (the automatic repetitions of the rows of a grid container, see <see cref="GridLayoutAlgorithm.UsesBlockSizeLimits"/>), else null.
/// </param>
/// <param name="MaxBlockSize">When <paramref name="ContentBlockSize"/> is null, the definite maximum block size of the content box, when the intrinsic sizes depend on it, else null.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IntrinsicSizingInput(double? ContentBlockSize, double? MinInlineSize = null, double? MaxInlineSize = null, double? QuirksPercentageBlockSize = null, double? MinBlockSize = null, double? MaxBlockSize = null);

/// <summary>The largest min-content and max-content contributions of the column spanners of a multi-column container (see <see cref="LayoutContext.ColumnSpannerSizes"/>).</summary>
internal sealed class ColumnSpannerIntrinsicSizes
{
    public double MinContent { get; set; }

    public double MaxContent { get; set; }
}

/// <summary>The cache of the intrinsic inline sizes of a box (<see cref="ContainerBox.IntrinsicSizesCache"/>): valid during one layout pass (and the next ones, see <see cref="LayoutContext.IsCacheOfPreviousPassValid"/>), for one input.</summary>
internal sealed class IntrinsicSizesCache
{
    public int Pass;
    public IntrinsicSizingInput Input;
    public IntrinsicSizes Sizes;

    /// <summary>The generation of the layout engine the sizes are valid for in the next passes, or -1 (see <see cref="LayoutContext.IsCacheOfPreviousPassValid"/>).</summary>
    public int Generation;

    public PhysicalSize InitialContainingBlockSize;
}

/// <summary>The cache of the intrinsic block size of a box (<see cref="ContainerBox.IntrinsicBlockSizeCache"/>): valid during one layout pass (and the next ones, see <see cref="LayoutContext.IsCacheOfPreviousPassValid"/>), for one constraint space.</summary>
internal sealed class IntrinsicBlockSizeCache
{
    public int Pass;
    public ConstraintSpace Space;
    public double Size;

    /// <summary>The generation of the layout engine the size is valid for in the next passes, or -1 (see <see cref="LayoutContext.IsCacheOfPreviousPassValid"/>).</summary>
    public int Generation;

    public PhysicalSize InitialContainingBlockSize;
}

/// <summary>The sizing properties (https://drafts.csswg.org/css-sizing-3/#sizing-properties).</summary>
internal enum SizingProperty : byte
{
    /// <summary>width, height (https://drafts.csswg.org/css-sizing-3/#preferred-size-properties).</summary>
    Preferred,

    /// <summary>min-width, min-height (https://drafts.csswg.org/css-sizing-3/#min-size-properties).</summary>
    Minimum,

    /// <summary>max-width, max-height (https://drafts.csswg.org/css-sizing-3/#max-size-properties).</summary>
    Maximum,
}

/// <summary>
/// The intrinsic sizing (https://drafts.csswg.org/css-sizing-3/#intrinsic): the min-content and max-content inline sizes of the
/// boxes, computed by their formatting context (<see cref="FormattingContext.ComputeIntrinsicInlineSizes"/>) and cached per
/// box for a layout pass, and the intrinsic size contributions of the boxes to their containing block
/// (https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution).
/// </summary>
/// <remarks>
/// <para>
/// The intrinsic sizes are the sizes of the content box in the inline axis of the box. They do not depend on the available
/// space (the percentages that refer to the inline size of the containing block are cyclic, see <see cref="GetContribution"/>),
/// only on the definite block size of the box (<see cref="IntrinsicSizingInput"/>): a box is measured once per layout pass,
/// however many times its ancestors and the shrink-to-fit sizing ask for it.
/// </para>
/// <para>
/// A formatting context provides the intrinsic sizes of its boxes by overriding <see cref="FormattingContext.ComputeIntrinsicInlineSizes"/>:
/// the block layout (the largest contribution of the children, the floats side by side), the inline layout (the lines broken
/// at every soft wrap opportunity, or only at the forced breaks), the replaced elements (their natural sizes), the flex and
/// grid layouts (with the contributions of their items, <see cref="GetContribution"/>), the table layout (the measures of
/// the columns and the captions).
/// </para>
/// </remarks>
internal static class IntrinsicSizing
{
    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a box (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes),
    /// computed by its formatting context and cached for the layout pass. A box with size containment in its inline axis uses
    /// <c>contain-intrinsic-size</c> (https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override) instead of its contents.
    /// </summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="box">The box.</param>
    /// <param name="contentBlockSize">The block size of the content box of the box when it is definite, else null.</param>
    public static IntrinsicSizes GetIntrinsicInlineSizes(LayoutContext context, ContainerBox box, double? contentBlockSize)
        => GetIntrinsicInlineSizesForInput(context, box, new IntrinsicSizingInput(contentBlockSize));

    /// <summary>Returns the min-content and max-content inline sizes of the content box of a box for an input (see <see cref="GetIntrinsicInlineSizes"/>).</summary>
    public static IntrinsicSizes GetIntrinsicInlineSizesForInput(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        var contentBlockSize = input.ContentBlockSize;
        // https://drafts.csswg.org/css-contain-2/#containment-size: "the intrinsic sizes of the size containment box are
        // determined as if the element had no content", or the explicit intrinsic inner size of contain-intrinsic-size (the
        // replaced elements get it as their natural size, see ReplacedContent)
        if (box.Kind != ContainerBoxKind.Replaced && HasSizeContainment(box, inlineAxis: true))
        {
            // Without an explicit intrinsic size, the formatting context gives the intrinsic sizes of the box without contents (the
            // explicit tracks of a grid)
            var horizontal = box.Style.Layout.IsHorizontalWritingMode;
            if (IsContainIntrinsicSizeNone(box, horizontal))
                return FormattingContext.For(box).ComputeEmptyIntrinsicInlineSizes(context, box, contentBlockSize);

            return IntrinsicSizes.FromSize(LastRememberedSizes.GetContainIntrinsicSize(box, horizontal));
        }

        // The natural size of a replaced element that skips its contents is its contain-intrinsic-size (see ReplacedContent)
        if (box.HasUnlaidSkippedContents && box.Kind != ContainerBoxKind.Replaced)
            return default;

        if (context.ColumnSpannerSizes is { } spannerSizes)
        {
            // The content of a multi-column container is being measured: the column spanners of the boxes of its block formatting
            // context are measured apart (the sizes of these boxes without their spanners are not cached)
            if (box.Parent is { ChildrenAreInline: false } parent && box.IsBlockLevel && box.IsInFlow && box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous
                && !BlockFormattingContext.EstablishesNewFormattingContext(box, WritingDirection.FromStyle(parent.Style.Layout)))
            {
                var participantSizes = FormattingContext.For(box).ComputeIntrinsicInlineSizes(context, box, input);
                return new IntrinsicSizes(Math.Max(0, participantSizes.MinContent), Math.Max(Math.Max(0, participantSizes.MinContent), participantSizes.MaxContent));
            }

            context.ColumnSpannerSizes = null;
            try
            {
                return GetIntrinsicInlineSizesForInput(context, box, input);
            }
            finally
            {
                context.ColumnSpannerSizes = spannerSizes;
            }
        }

        var cache = box.IntrinsicSizesCache;
        if (cache is not null && cache.Input == input && (cache.Pass == context.Pass || context.IsCacheOfPreviousPassValid(cache.Generation, cache.InitialContainingBlockSize)))
            return cache.Sizes;

        var preventionCount = context.LayoutCachingPreventionCount;
        var sizes = FormattingContext.For(box).ComputeIntrinsicInlineSizes(context, box, input);
        sizes = new IntrinsicSizes(Math.Max(0, sizes.MinContent), Math.Max(Math.Max(0, sizes.MinContent), sizes.MaxContent));
        cache ??= box.IntrinsicSizesCache = new IntrinsicSizesCache();
        cache.Pass = context.Pass;
        cache.Input = input;
        cache.Sizes = sizes;
        cache.Generation = context.GetCacheGeneration(preventionCount);
        cache.InitialContainingBlockSize = context.InitialContainingBlockSize;
        return sizes;
    }

    /// <summary>
    /// Returns whether the <c>contain-intrinsic-size</c> of a box behaves as <c>none</c> in a physical axis: no explicit intrinsic inner
    /// size (<c>none</c>, or <c>auto none</c> without a last remembered size).
    /// </summary>
    internal static bool IsContainIntrinsicSizeNone(ContainerBox box, bool horizontal)
    {
        var layout = box.Style.Layout;
        var value = horizontal ? layout.Box.ContainIntrinsicWidth : layout.Box.ContainIntrinsicHeight;
        return value.Length is null && !LastRememberedSizes.TryGetRememberedSize(box, horizontal, out _);
    }

    /// <summary>
    /// Returns whether size containment (https://drafts.csswg.org/css-contain-2/#containment-size) applies to a box in its inline
    /// axis (<c>contain: size | inline-size</c>, <c>container-type: size | inline-size</c>) or in its block axis
    /// (<c>contain: size</c>, <c>container-type: size</c>), or because it skips its contents (<c>content-visibility: hidden</c>,
    /// <c>content-visibility: auto</c> when the element is not relevant to the user, a closed <c>details</c>). "Giving an
    /// element size containment has no effect if its principal box is an internal table box, an inline box, or a ruby box", or
    /// if its inner display type is <c>table</c> (see <see cref="UsedContainment.CanApplySizeContainment(ContainerBox)"/>).
    /// </summary>
    public static bool HasSizeContainment(ContainerBox box, bool inlineAxis)
    {
        if (!box.SkipsContents && !box.Style.Layout.HasSizeContainment(inlineAxis))
            return false;

        return UsedContainment.CanApplySizeContainment(box);
    }

    /// <summary>
    /// Returns the min-content and max-content contributions of a box to the inline size of its containing block
    /// (https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution): the outer size (the margin box) of the box sized as an
    /// <c>auto</c>-sized float in a zero-sized or infinitely-sized containing block.
    /// </summary>
    /// <remarks>
    /// The percentages that refer to the inline size of the containing block are cyclic
    /// (https://drafts.csswg.org/css-sizing-3/#cyclic-percentage-contribution): the preferred and maximum sizes behave as their
    /// initial value (a replaced element resolves them against zero for its min-content contribution), the minimum sizes, the
    /// margins and the paddings resolve against zero; <c>stretch</c> behaves the same way. The intrinsic size keywords use the
    /// intrinsic sizes of the box (<c>fit-content</c> contributes its min-content size to the min-content contribution and its
    /// max-content size to the max-content one). A box with a preferred aspect ratio and a definite block size contributes the
    /// size transferred through the ratio (https://drafts.csswg.org/css-sizing-4/#aspect-ratio), with its automatic minimum
    /// size. The replaced elements and the orthogonal flows are measured by laying them out (<see cref="MeasureContribution"/>).
    /// </remarks>
    /// <param name="context">The layout pass.</param>
    /// <param name="box">The box (an in-flow child, a float, an atomic inline).</param>
    /// <param name="containingBlock">The writing mode of the containing block: the contributions are in its inline axis.</param>
    /// <param name="containingBlockBlockSize">The block size of the containing block when it is definite (the basis of the percentage block sizes of the box), else null.</param>
    /// <param name="stretchesBlockSize">
    /// Whether the <c>auto</c> size of the box in the block axis of the containing block is definite: the box is stretched to
    /// <paramref name="containingBlockBlockSize"/> less its margins (a stretched item of a single-line flex container with a
    /// definite cross size, https://drafts.csswg.org/css-flexbox-1/#definite-sizes).
    /// </param>
    /// <param name="isFlexItem">
    /// Whether the box is an item of a row flex container, whose contribution "is the larger of its outer min-content
    /// [max-content] size and outer preferred size if that is not auto" (https://drafts.csswg.org/css-flexbox-1/#intrinsic-item-contributions):
    /// its definite preferred size does not make its contribution smaller than its contents.
    /// </param>
    /// <param name="quirksPercentageBlockSize">
    /// In quirks mode, the basis of a percentage <c>height</c> of the box when <paramref name="containingBlockBlockSize"/> is
    /// indefinite (see <see cref="ConstraintSpace.QuirksPercentageBlockSize"/>), else null.
    /// </param>
    public static IntrinsicSizes GetContribution(LayoutContext context, ContainerBox box, WritingDirection containingBlock, double? containingBlockBlockSize, bool stretchesBlockSize = false, bool isFlexItem = false, double? quirksPercentageBlockSize = null)
    {
        var layout = box.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);

        // In quirks mode, the block size of the containing block is the basis of the percentage heights of the descendants of
        // the box when it is definite (https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk)
        quirksPercentageBlockSize = context.IsQuirksMode ? containingBlockBlockSize ?? quirksPercentageBlockSize : null;

        if (box.Kind == ContainerBoxKind.Replaced || writingDirection.IsOrthogonalTo(containingBlock))
            return MeasureContribution(context, box, containingBlock, containingBlockBlockSize, stretchesBlockSize, quirksPercentageBlockSize);

        // The sizes of a table apply to its grid box, which a table is never narrower than (https://drafts.csswg.org/css-tables-3/#computing-the-table-width)
        if (box.Kind == ContainerBoxKind.TableWrapper)
            return TableWrapperFormattingContext.GetContribution(context, box);

        var boxModel = layout.Box;
        var horizontal = writingDirection.IsHorizontal;
        var margin = FormattingContext.ResolveMargins(boxModel, 0);
        var paddingAndBorder = writingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, 0).Add(boxModel.BorderWidth));
        var gutters = GetScrollbarGutters(context, box, writingDirection);
        var marginSum = horizontal ? margin.HorizontalSum() : margin.VerticalSum();

        // The block size of the box when it is definite: the percentages of its children resolve against it, and its aspect ratio transfers it
        var blockSizing = new AxisSizing(context, box)
        {
            Horizontal = !horizontal,
            IsInlineAxis = false,
            PercentageBasis = containingBlockBlockSize,
            PaddingAndBorder = paddingAndBorder.BlockSum(),
            Edges = paddingAndBorder.BlockSum() + gutters.BlockSum(),
            StretchSize = containingBlockBlockSize is { } basis ? Math.Max(basis - (horizontal ? margin.VerticalSum() : margin.HorizontalSum()), paddingAndBorder.BlockSum()) : null,
        };

        // https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk: a percentage height resolves against the
        // height of the nearest ancestor that is not an auto height block container
        var usesQuirksBasis = quirksPercentageBlockSize is not null && FormattingContext.UsesQuirksPercentageHeight(box, layout, boxModel.GetSize(!horizontal));
        var preferredBlockSize = (usesQuirksBasis ? blockSizing with { PercentageBasis = quirksPercentageBlockSize } : blockSizing).Resolve(boxModel.GetSize(!horizontal), SizingProperty.Preferred);
        if (stretchesBlockSize && boxModel.GetSize(!horizontal).IsAuto)
        {
            preferredBlockSize = blockSizing.StretchSize;
        }

        double? contentBlockSize = preferredBlockSize is { } blockSize ? Math.Max(0, blockSizing.Clamp(blockSize) - blockSizing.Edges) : null;

        // The percentage block sizes of the children of an anonymous block box refer to its containing block (see ContentArea.AnonymousPercentageBlockSize)
        if (contentBlockSize is null && box is { Kind: ContainerBoxKind.Anonymous, IsInlineBox: false, Display.Internal: DisplayInternal.None })
        {
            contentBlockSize = containingBlockBlockSize;
        }

        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: "the resolved preferred size in the ratio-determining axis (before
        // applying min/max) gets transferred thru the ratio"
        var ratio = FormattingContext.GetPreferredAspectRatio(box, horizontal, paddingAndBorder, naturalRatio: null);
        var sizing = new AxisSizing(context, box)
        {
            Horizontal = horizontal,
            IsInlineAxis = true,
            PercentageBasis = null,
            PaddingAndBorder = paddingAndBorder.InlineSum(),
            Edges = paddingAndBorder.InlineSum() + gutters.InlineSum(),
            ContentBlockSize = contentBlockSize,
            QuirksPercentageBlockSize = contentBlockSize is null && quirksPercentageBlockSize is not null && FormattingContext.PassesQuirksPercentageBlockSize(box, layout) ? quirksPercentageBlockSize : null,
            TransferredSize = ratio is { } transfer && preferredBlockSize is { } ratioDeterminingSize ? transfer.ToInline(ratioDeterminingSize) : null,
            TransferredLimits = ratio is { } limitsRatio && GridLayoutAlgorithm.UsesInlineSizeLimits(box) ? FormattingContext.GetTransferredMinMax(blockSizing, limitsRatio, toInline: true) : default,
            BlockLimits = contentBlockSize is null && GridLayoutAlgorithm.UsesBlockSizeLimits(box) ? blockSizing.GetContentBoxLimits() : default,
        };

        // The preferred size: a size that does not depend on the containing block, or the size transferred through the aspect
        // ratio, or the intrinsic sizes of the box
        var preferredSize = boxModel.GetSize(horizontal);
        var preferred = sizing.TransferredSize is not null && preferredSize.IsIntrinsic ? null : sizing.ResolveContribution(preferredSize, SizingProperty.Preferred);
        var isRatioDependent = preferred is null && sizing.TransferredSize is not null;
        var result = preferred ?? sizing.GetIntrinsicSizes();
        if (isFlexItem && preferred is { } definite && !preferredSize.IsIntrinsic)
        {
            var intrinsic = sizing.GetIntrinsicSizes();
            result = new IntrinsicSizes(Math.Max(definite.MinContent, intrinsic.MinContent), Math.Max(definite.MaxContent, intrinsic.MaxContent));
        }

        // The minimum and maximum sizes, and the ones transferred through the aspect ratio (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers)
        var min = sizing.ResolveContribution(boxModel.GetMinSize(horizontal), SizingProperty.Minimum);
        var max = sizing.ResolveContribution(boxModel.GetMaxSize(horizontal), SizingProperty.Maximum);
        double? transferredMin = null;
        double? transferredMax = null;
        if ((preferred is null || preferredSize.IsIntrinsic) && ratio is { } blockRatio)
        {
            // (the minimum and maximum block sizes are transferred to an automatic inline size, ratio-dependent or not, and to an
            // intrinsic size keyword: "any indefinite preferred size")
            (transferredMin, transferredMax) = FormattingContext.GetTransferredMinMax(blockSizing, blockRatio, toInline: true);

            // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum: the automatic minimum size in the ratio-dependent axis is
            // the min-content size (when the box is not a scroll container)
            var minSize = boxModel.GetMinSize(horizontal);
            if (isRatioDependent && minSize.IsAuto && !ScrollContainers.IsScrollable(horizontal ? layout.OverflowX : layout.OverflowY))
            {
                var minContent = sizing.GetContentIntrinsicSizes().MinContent;
                min = IntrinsicSizes.FromSize(Math.Min(minContent, max?.MaxContent ?? double.PositiveInfinity));
            }
            else if (isRatioDependent && minSize is { Kind: SizeKind.CalcSize, BehavesAs: SizeKind.Auto } && !ScrollContainers.IsScrollable(horizontal ? layout.OverflowX : layout.OverflowY))
            {
                // https://drafts.csswg.org/css-values-5/#calc-size: the size keyword of min-width: calc-size(auto, …) is that
                // automatic minimum size (as in the layout of the box)
                var minContent = sizing.GetContentIntrinsicSizes().MinContent;
                if ((sizing with { AutomaticSize = minContent, ContentSize = minContent }).Resolve(minSize, SizingProperty.Minimum) is { } minimum)
                {
                    min = IntrinsicSizes.FromSize(Math.Min(minimum, max?.MaxContent ?? double.PositiveInfinity));
                }
            }
        }

        var minContribution = Clamp(result.MinContent, min?.MinContent, max?.MinContent, transferredMin, transferredMax, paddingAndBorder.InlineSum());
        var maxContribution = Clamp(result.MaxContent, min?.MaxContent, max?.MaxContent, transferredMin, transferredMax, paddingAndBorder.InlineSum());
        return new IntrinsicSizes(minContribution + marginSum, Math.Max(minContribution, maxContribution) + marginSum);
    }

    /// <summary>Clamps a border-box size between a minimum and a maximum (the maximum is applied first: the minimum wins), and the transferred ones.</summary>
    private static double Clamp(double size, double? min, double? max, double? transferredMin, double? transferredMax, double paddingAndBorder)
    {
        if (transferredMin is { } transferredMinimum)
        {
            min = Math.Max(min ?? 0, max is { } maximum ? Math.Min(transferredMinimum, maximum) : transferredMinimum);
        }

        if (transferredMax is { } transferredMaximum)
        {
            transferredMaximum = Math.Max(transferredMaximum, min ?? 0);
            max = max is { } maximum ? Math.Min(maximum, transferredMaximum) : transferredMaximum;
        }

        if (max is { } maxSize && size > maxSize)
        {
            size = maxSize;
        }

        if (min is { } minSize && size < minSize)
        {
            size = minSize;
        }

        return Math.Max(size, paddingAndBorder);
    }

    /// <summary>
    /// Returns the contributions of a replaced element or of an orthogonal flow, measured by laying the box out: the box is laid
    /// out in a containing block whose inline size is indefinite for its max-content contribution (its percentages and
    /// <c>stretch</c> behave as <c>auto</c>), and zero-sized for the min-content contribution of a replaced element (its cyclic
    /// percentages resolve against zero, https://drafts.csswg.org/css-sizing-3/#replaced-percentage-min-contribution). The
    /// available inline space is zero in both cases: a replaced element with only an aspect ratio contributes zero, as in the
    /// browsers (WPT css/css-sizing/svg-intrinsic-size-009.html; the specification uses the size of the initial containing block).
    /// </summary>
    /// <remarks>
    /// An orthogonal flow (https://drafts.csswg.org/css-writing-modes-4/#orthogonal-flows) is laid out with the available inline
    /// space of its block size (https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto): its block size, in the inline axis
    /// of the containing block, is both its min-content and its max-content contribution.
    /// </remarks>
    private static IntrinsicSizes MeasureContribution(LayoutContext context, ContainerBox box, WritingDirection containingBlock, double? containingBlockBlockSize, bool stretchesBlockSize, double? quirksPercentageBlockSize)
    {
        var space = new ConstraintSpace
        {
            WritingDirection = containingBlock,
            AvailableInlineSize = 0,
            AvailableBlockSize = containingBlockBlockSize,
            PercentageInlineSize = 0,
            PercentageBlockSize = containingBlockBlockSize,
            QuirksPercentageBlockSize = quirksPercentageBlockSize,
            IsInlineSizeIndefinite = true,
        };

        // The stretched size of the box in the block axis of the containing block is imposed (see GetContribution)
        var boxModel = box.Style.Layout.Box;
        var blockIsHorizontal = !containingBlock.IsHorizontal;
        if (stretchesBlockSize && containingBlockBlockSize is { } stretchBasis && boxModel.GetSize(blockIsHorizontal).IsAuto)
        {
            var blockMargins = containingBlock.ToLogical(FormattingContext.ResolveMargins(boxModel, 0)).BlockSum();
            var blockPaddingAndBorder = containingBlock.ToLogical(FormattingContext.ResolvePadding(boxModel, 0).Add(boxModel.BorderWidth)).BlockSum();
            var blockSizing = new AxisSizing(context, box)
            {
                Horizontal = blockIsHorizontal,
                IsInlineAxis = box.Style.Layout.IsHorizontalWritingMode == blockIsHorizontal,
                PercentageBasis = containingBlockBlockSize,
                PaddingAndBorder = blockPaddingAndBorder,
                Edges = blockPaddingAndBorder,
            };

            space = space with { FixedBlockSize = blockSizing.Clamp(Math.Max(stretchBasis - blockMargins, blockPaddingAndBorder)) };
        }

        // The margins resolve against zero, the auto margins are zero (the used margins of a block-level box absorb the remaining space)
        var margins = containingBlock.ToLogical(FormattingContext.ResolveMargins(box.Style.Layout.Box, 0)).InlineSum();
        var max = containingBlock.ToLogical(context.LayoutBox(box, space).Size).Inline + margins;
        // The percentages of a compressible replaced element resolve against zero in its inline axis only: as in the browsers,
        // the percentage block size of an orthogonal replaced element behaves as auto for both contributions (WPT
        // css/css-sizing/intrinsic-percent-replaced-029.tentative.html, https://github.com/w3c/csswg-drafts/issues/12133)
        if (!IsCompressibleReplacedElement(box) || WritingDirection.FromStyle(box.Style.Layout).IsOrthogonalTo(containingBlock))
            return IntrinsicSizes.FromSize(max);

        // As in the browsers, a percentage maximum size alone does not compress a form control: only its preferred size does
        // (WPT css/css-sizing/range-percent-intrinsic-size-1.html)
        if (IsCompressibleFormControl(box) && !HasPercentageSize(box, containingBlock.IsHorizontal))
            return IntrinsicSizes.FromSize(max);

        var min = containingBlock.ToLogical(context.LayoutBox(box, space with { IsInlineSizeIndefinite = false }).Size).Inline + margins;
        return new IntrinsicSizes(Math.Min(min, max), max);
    }

    /// <summary>
    /// Returns the classic scrollbar gutters of a scroll container (https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing),
    /// which are added to the intrinsic sizes of its contents: the scrollbars forced by <c>overflow: scroll</c> and
    /// <c>scrollbar-gutter</c>, and the ones that its last layout displayed.
    /// </summary>
    public static LogicalSides<double> GetScrollbarGutters(LayoutContext context, ContainerBox box, WritingDirection writingDirection)
    {
        var layout = box.Style.Layout;
        if (context.ScrollbarThickness <= 0 || box.Kind == ContainerBoxKind.Replaced || !layout.IsScrollContainer || !ScrollContainers.IsScrollContainer(box))
            return default;

        var thickness = ScrollContainers.GetScrollbarThickness(context, layout);
        if (thickness <= 0)
            return default;

        var overflow = ScrollContainers.GetUsedOverflow(box);
        var scrollbars = ScrollContainers.GetForcedScrollbars(overflow);
        if (box.ScrollingElement is { } element && context.Engine.TryGetScrollState(element) is { } previous)
        {
            scrollbars |= previous.Scrollbars & ((overflow.X == OverflowKind.Auto ? Scrollbars.Horizontal : Scrollbars.None) | (overflow.Y == OverflowKind.Auto ? Scrollbars.Vertical : Scrollbars.None));
        }

        // The layout of the box may display other scrollbars: the document is then laid out again (see LayoutEngine.UpdateLayout).
        // The intrinsic sizes computed with these gutters are computed again by the next passes (the pass that computes them
        // detects the scrollbars it did not assume)
        context.PreventLayoutCaching();
        box.ScrollbarGuttersPass = context.Pass;
        box.AssumedScrollbars = scrollbars;
        return writingDirection.ToLogical(ScrollContainers.GetScrollbarSides(scrollbars, overflow, writingDirection, layout.Overflow.ScrollbarGutter, thickness));
    }

    /// <summary>
    /// Returns whether a box is a compressible replaced element (https://drafts.csswg.org/css-sizing-3/#min-content-zero) whose
    /// cyclic percentage sizes resolve against zero for its min-content contribution: a replaced element, or a form control
    /// that is not button-like.
    /// </summary>
    internal static bool IsCompressibleReplacedElement(ContainerBox box) => box.Kind == ContainerBoxKind.Replaced
        && !(box.PseudoElement is null && box.Element is HtmlInputElement { InputType: InputType.Button or InputType.Reset or InputType.Submit or InputType.Color } or HtmlInputElement { InputType: InputType.Image, ImageButtonRepresentsImage: false });

    /// <summary>Returns whether the preferred size of a box in a physical axis has a percentage, which makes a form control compressible.</summary>
    internal static bool HasPercentageSize(ContainerBox box, bool horizontal) => box.Style.Layout.Box.GetSize(horizontal) is { IsLengthPercentage: true, Value.HasPercentage: true };

    /// <summary>Returns whether a box is a compressible form control (see <see cref="IsCompressibleReplacedElement"/>): not an element with replaced content (an image, a canvas, an embedded document).</summary>
    internal static bool IsCompressibleFormControl(ContainerBox box) => IsCompressibleReplacedElement(box)
        && box.PseudoElement is null && box.Element is HtmlInputElement { ImageButtonRepresentsImage: false } or HtmlTextAreaElement or HtmlSelectElement or HtmlProgressElement or HtmlMeterElement;
}

/// <summary>
/// The resolution of the sizing properties of a box in one of its axes (https://drafts.csswg.org/css-sizing-3/#sizing-values,
/// https://drafts.csswg.org/css-sizing-4/#sizing-values) to border-box sizes: the &lt;length-percentage&gt; values with
/// <c>box-sizing</c>, the intrinsic size keywords, <c>stretch</c> and <c>calc-size()</c>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AxisSizing
{
    public AxisSizing(LayoutContext context, ContainerBox box)
    {
        Context = context;
        Box = box;
    }

    public LayoutContext Context { get; }

    public ContainerBox Box { get; }

    /// <summary>Whether the axis is the horizontal axis.</summary>
    public bool Horizontal { get; init; }

    /// <summary>Whether the axis is the inline axis of the box.</summary>
    public bool IsInlineAxis { get; init; }

    /// <summary>The basis of the percentages (the size of the containing block in the axis), or null when it is indefinite.</summary>
    public double? PercentageBasis { get; init; }

    /// <summary>The paddings and borders of the axis, which <c>box-sizing: content-box</c> adds to the lengths.</summary>
    public double PaddingAndBorder { get; init; }

    /// <summary>The paddings, borders and scrollbar gutters of the axis, added to the content-box intrinsic sizes.</summary>
    public double Edges { get; init; }

    /// <summary>The border-box stretch-fit size (https://drafts.csswg.org/css-sizing-3/#stretch-fit-sizing), or null when it is indefinite (<c>stretch</c> then behaves as <c>auto</c>).</summary>
    public double? StretchSize { get; init; }

    /// <summary>In the inline axis: the definite block size of the content box, the input of the intrinsic inline sizes.</summary>
    public double? ContentBlockSize { get; init; }

    /// <summary>In the inline axis, in quirks mode, when <see cref="ContentBlockSize"/> is null: the basis of the percentage heights of the children (see <see cref="IntrinsicSizingInput.QuirksPercentageBlockSize"/>).</summary>
    public double? QuirksPercentageBlockSize { get; init; }

    /// <summary>
    /// In the block axis: the border-box automatic block size of the box once its contents are laid out (the intrinsic size
    /// keywords resolve to it), or null before: the keywords then behave as <c>auto</c>, <c>0</c> and <c>none</c>.
    /// </summary>
    public double? ContentSize { get; init; }

    /// <summary>The border-box automatic size of the box (the size of an <c>auto</c> basis of <c>calc-size()</c>), or null when it is not known.</summary>
    public double? AutomaticSize { get; init; }

    /// <summary>
    /// In the inline axis: the border-box size transferred from the definite block size through the preferred aspect ratio, or
    /// null. It is both the min-content and the max-content size of the box: "When a box has a preferred aspect ratio, its
    /// automatic sizes are calculated the same as for a replaced element with a natural aspect ratio and no natural size in that
    /// axis" (https://drafts.csswg.org/css-sizing-4/#aspect-ratio, https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes).
    /// </summary>
    public double? TransferredSize { get; init; }

    private LayoutStyle Style => Box.Style.Layout;

    /// <summary>
    /// Returns the border-box intrinsic sizes of the box in the inline axis: the size transferred through the aspect ratio
    /// (<see cref="TransferredSize"/>), or the intrinsic sizes of its contents (<see cref="GetContentIntrinsicSizes"/>).
    /// </summary>
    public IntrinsicSizes GetIntrinsicSizes()
    {
        if (TransferredSize is { } transferred)
            return IntrinsicSizes.FromSize(transferred);

        var sizes = GetContentIntrinsicSizes();

        // https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes: the max-content size of a replaced element that only has an
        // aspect ratio is the stretch fit into the available space when it is definite
        if (StretchSize is { } stretch && ContentBlockSize is null && ReplacedFormattingContext.HasOnlyAspectRatio(Box))
            return sizes with { MaxContent = Math.Max(sizes.MinContent, stretch) };

        return sizes;
    }

    /// <summary>Returns the border-box intrinsic sizes of the contents of the box in the inline axis (see <see cref="IntrinsicSizing.GetIntrinsicInlineSizes"/>).</summary>
    public IntrinsicSizes GetContentIntrinsicSizes() => IntrinsicSizing.GetIntrinsicInlineSizesForInput(Context, Box, GetIntrinsicSizingInput()).Add(Edges);

    /// <summary>
    /// The definite minimum and maximum block sizes transferred to the inline axis through the preferred aspect ratio
    /// (border-box sizes, https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers), for <see cref="GetIntrinsicSizingInput"/>.
    /// </summary>
    public (double? Min, double? Max) TransferredLimits { get; init; }

    /// <summary>
    /// In the inline axis, when <see cref="ContentBlockSize"/> is null: the definite minimum and maximum block sizes of the content box
    /// of a grid container whose rows have an automatic repetition (see <see cref="GridLayoutAlgorithm.UsesBlockSizeLimits"/>), for
    /// <see cref="GetIntrinsicSizingInput"/>.
    /// </summary>
    public (double? Min, double? Max) BlockLimits { get; init; }

    /// <summary>
    /// Returns the input of the intrinsic inline sizes of the box: its definite block size, and, for a grid container whose
    /// tracks of the inline axis have an automatic repetition, its definite minimum and maximum inline sizes (the lengths, the
    /// percentages of a definite basis and the sizes transferred from the block axis), which give the number of repetitions
    /// (https://drafts.csswg.org/css-grid-2/#auto-repeat), and the ones of the block axis (<see cref="BlockLimits"/>).
    /// </summary>
    public IntrinsicSizingInput GetIntrinsicSizingInput()
    {
        if (!IsInlineAxis || !GridLayoutAlgorithm.UsesInlineSizeLimits(Box))
            return new IntrinsicSizingInput(ContentBlockSize, QuirksPercentageBlockSize: QuirksPercentageBlockSize, MinBlockSize: BlockLimits.Min, MaxBlockSize: BlockLimits.Max);

        var box = Style.Box;
        var min = ResolveLimit(box.GetMinSize(Horizontal));
        var max = ResolveLimit(box.GetMaxSize(Horizontal));
        if (TransferredLimits.Min is { } transferredMin)
        {
            min = Math.Max(min ?? 0, max is { } maximum ? Math.Min(transferredMin, maximum) : transferredMin);
        }

        if (TransferredLimits.Max is { } transferredMax)
        {
            transferredMax = Math.Max(transferredMax, min ?? 0);
            max = max is { } maximum ? Math.Min(maximum, transferredMax) : transferredMax;
        }

        return new IntrinsicSizingInput(ContentBlockSize, min is { } a ? Math.Max(0, a - Edges) : null, max is { } b ? Math.Max(0, b - Edges) : null, QuirksPercentageBlockSize, BlockLimits.Min, BlockLimits.Max);
    }

    /// <summary>Returns the definite minimum and maximum sizes of the content box in this axis (null when they behave as <c>0</c> and <c>none</c>).</summary>
    public (double? Min, double? Max) GetContentBoxLimits()
    {
        var box = Style.Box;
        var min = Resolve(box.GetMinSize(Horizontal), SizingProperty.Minimum);
        var max = Resolve(box.GetMaxSize(Horizontal), SizingProperty.Maximum);
        return (min is { } a ? Math.Max(0, a - Edges) : null, max is { } b ? Math.Max(0, b - Edges) : null);
    }

    private double? ResolveLimit(Size size) => size.IsLengthPercentage && size.TryResolve(PercentageBasis) is { } value ? ToBorderBox(value) : null;

    /// <summary>Returns the border-box size of a &lt;length-percentage&gt; with <c>box-sizing</c>.</summary>
    private double ToBorderBox(double value) => Style.BoxSizing == BoxSizing.BorderBox ? Math.Max(value, PaddingAndBorder) : Math.Max(0, value) + PaddingAndBorder;

    /// <summary>
    /// Resolves a value of a sizing property to a border-box size, or returns null when it behaves as <c>auto</c> (a preferred
    /// size), <c>0</c> (a minimum size) or <c>none</c> (a maximum size): <c>auto</c>, <c>none</c>, a percentage of an indefinite
    /// size, <c>stretch</c> in an indefinite size, an intrinsic size keyword in the block axis before the contents are laid out.
    /// </summary>
    public double? Resolve(Size size, SizingProperty property)
    {
        switch (size.Kind)
        {
            case SizeKind.LengthPercentage:
                // (the percentages of a minimum size resolve against zero when their basis is indefinite, see Size.TryResolveMinimum:
                // a minimum that is then not positive behaves as 0)
                if (property == SizingProperty.Minimum && PercentageBasis is null && size.Value.HasPercentage)
                    return size.Value.Resolve(0) > 0 ? ToBorderBox(size.Value.Resolve(0)) : null;

                return size.TryResolve(PercentageBasis) is { } value ? ToBorderBox(value) : null;

            case SizeKind.MinContent or SizeKind.MaxContent or SizeKind.FitContent or SizeKind.FitContentFunction or SizeKind.Content:
                // https://drafts.csswg.org/css-sizing-3/#valdef-width-min-content: "for a box's block size, unless otherwise
                // specified, this is equivalent to its automatic size"
                if (!IsInlineAxis)
                    return ContentSize;

                var intrinsic = GetIntrinsicSizes();
                return size.Kind switch
                {
                    SizeKind.MinContent => intrinsic.MinContent,

                    // flex-basis: content (https://drafts.csswg.org/css-flexbox-1/#valdef-flex-basis-content): the max-content size
                    SizeKind.MaxContent or SizeKind.Content => intrinsic.MaxContent,

                    // https://drafts.csswg.org/css-sizing-3/#valdef-width-fit-content: min(max-content, max(min-content, stretch))
                    SizeKind.FitContent => StretchSize is { } stretch ? intrinsic.FitContent(stretch) : intrinsic.MaxContent,

                    // https://drafts.csswg.org/css-sizing-4/#valdef-width-fit-content-length-percentage: the fit-content formula with
                    // the argument as the available space
                    _ => size.Value.TryResolve(PercentageBasis) is { } limit ? intrinsic.FitContent(ToBorderBox(limit)) : ResolveCyclicFitContent(intrinsic, property),
                };

            case SizeKind.Stretch:
                return StretchSize;

            case SizeKind.CalcSize:
                return size.CalcSize is { } calcSize ? ResolveCalcSize(calcSize, property) : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves a <c>calc-size()</c> (https://drafts.csswg.org/css-values-5/#resolving-calc-size): "the calc-size calculation is
    /// resolved just like the value of a calc() expression, by substituting in the used value of the calc-size basis for the
    /// size keyword in the calc-size calculation, and evaluating any percentages normally (if they are definite) or against
    /// 0px (if they are not)". The sizes are in the box of <c>box-sizing</c>.
    /// </summary>
    private double? ResolveCalcSize(CalcSizeValue value, SizingProperty property)
    {
        double basis;
        switch (value.BasisKind)
        {
            case SizeKind.None:
                basis = 0;
                break;

            case SizeKind.CalcSize:
                if (ResolveCalcSize(value.NestedBasis!, property) is not { } nested)
                    return null;

                basis = nested - BoxSizingEdges;
                break;

            case SizeKind.LengthPercentage:
                // "For all purposes other than actually resolving the size, the calc-size() acts like this calculation wrapped in a
                // calc()": a percentage of an indefinite size behaves as auto
                if (value.BasisLength.TryResolve(PercentageBasis) is not { } length)
                    return null;

                basis = length;
                break;

            case SizeKind.Auto:
                if ((IsInlineAxis ? AutomaticSize : ContentSize) is { } automatic)
                {
                    basis = automatic - BoxSizingEdges;
                }
                else if (property == SizingProperty.Minimum)
                {
                    // The automatic minimum size is zero (https://drafts.csswg.org/css-sizing-3/#valdef-min-width-auto), but for
                    // the flex and grid items and the boxes with an aspect ratio, whose callers give the automatic size
                    basis = 0;
                }
                else
                {
                    return null;
                }

                break;

            default:
                if (Resolve(Size.FromKind(value.BasisKind), property) is not { } keyword)
                    return null;

                basis = keyword - BoxSizingEdges;
                break;
        }

        return ToBorderBox(value.Evaluate(Math.Max(0, basis), PercentageBasis));
    }

    /// <summary>The paddings and borders outside of the box of <c>box-sizing</c>.</summary>
    private double BoxSizingEdges => Style.BoxSizing == BoxSizing.BorderBox ? 0 : PaddingAndBorder;

    /// <summary>
    /// Clamps a border-box size between the minimum and the maximum sizes of the axis (https://drafts.csswg.org/css-sizing-3/#min-size-properties),
    /// and the ones transferred from the other axis through the aspect ratio (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers):
    /// "This transferred minimum is capped by any definite preferred or maximum size in the destination axis. [...] This
    /// transferred maximum is floored by any definite preferred or minimum size in the destination axis as well as by the
    /// transferred minimum, if any." The maximum is applied first: the minimum wins when they conflict (https://drafts.csswg.org/css2/#min-max-widths).
    /// </summary>
    public double Clamp(double size, double? transferredMin = null, double? transferredMax = null)
    {
        var box = Style.Box;
        var max = Resolve(box.GetMaxSize(Horizontal), SizingProperty.Maximum);

        // min-width: auto is 0 for the boxes that are not flex or grid items (https://drafts.csswg.org/css-sizing-3/#valdef-width-auto)
        var min = Resolve(box.GetMinSize(Horizontal), SizingProperty.Minimum);
        if (transferredMin is { } transferredMinimum)
        {
            if (max is { } maximum)
            {
                transferredMinimum = Math.Min(transferredMinimum, maximum);
            }

            min = Math.Max(min ?? 0, transferredMinimum);
        }

        if (transferredMax is { } transferredMaximum)
        {
            transferredMaximum = Math.Max(transferredMaximum, min ?? 0);
            max = max is { } maximum ? Math.Min(maximum, transferredMaximum) : transferredMaximum;
        }

        if (max is { } maxSize && size > maxSize)
        {
            size = maxSize;
        }

        if (min is { } minSize && size < minSize)
        {
            size = minSize;
        }

        return Math.Max(size, PaddingAndBorder);
    }

    /// <summary>
    /// Resolves a value of a sizing property for the intrinsic size contributions of the box (inline axis,
    /// https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution): the min-content and the max-content contributions it gives,
    /// or null when it behaves as its initial value (see <see cref="IntrinsicSizing.GetContribution"/>).
    /// </summary>
    public IntrinsicSizes? ResolveContribution(Size size, SizingProperty property)
    {
        switch (size.Kind)
        {
            case SizeKind.LengthPercentage:
                // https://drafts.csswg.org/css-sizing-3/#cyclic-percentage-contribution: a cyclic percentage of a preferred or
                // maximum size behaves as the initial value, the one of a minimum size resolves against zero (as Blink; Gecko and
                // WebKit ignore the minimum, https://github.com/w3c/csswg-drafts/issues/10969; the block axis does the same, see
                // Size.TryResolveMinimum: WPT intrinsic-percent-non-replaced-006.html)
                if (size.Value.HasPercentage && property != SizingProperty.Minimum)
                    return null;

                return IntrinsicSizes.FromSize(ToBorderBox(size.Value.Resolve(0)));

            case SizeKind.MinContent:
                return IntrinsicSizes.FromSize(GetIntrinsicSizes().MinContent);

            case SizeKind.MaxContent:
                return IntrinsicSizes.FromSize(GetIntrinsicSizes().MaxContent);

            case SizeKind.FitContent:
                return GetIntrinsicSizes();

            case SizeKind.FitContentFunction:
                if (size.Value.HasPercentage)
                    return ResolveCyclicFitContent(GetIntrinsicSizes(), property) is { } cyclic ? IntrinsicSizes.FromSize(cyclic) : null;

                return IntrinsicSizes.FromSize(GetIntrinsicSizes().FitContent(ToBorderBox(size.Value.Resolve(0))));

            case SizeKind.CalcSize when size.CalcSize is { } calcSize:
                return ResolveCalcSizeContribution(calcSize, property);

            default:
                // auto, none, stretch (cyclic: https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution)
                return null;
        }
    }

    /// <summary>
    /// Resolves a <c>fit-content()</c> whose argument is a cyclic percentage (https://drafts.csswg.org/css-sizing-3/#cyclic-percentage-contribution):
    /// the result of the function stays between the min-content and the max-content sizes, so a minimum size (whose percentage
    /// resolves against zero) is the min-content size, and a maximum size (which behaves as <c>none</c>, an infinite argument)
    /// the max-content size; a preferred size behaves as <c>auto</c> (null).
    /// </summary>
    private static double? ResolveCyclicFitContent(IntrinsicSizes intrinsic, SizingProperty property) => property switch
    {
        SizingProperty.Minimum => intrinsic.MinContent,
        SizingProperty.Maximum => intrinsic.MaxContent,
        _ => null,
    };

    private IntrinsicSizes? ResolveCalcSizeContribution(CalcSizeValue value, SizingProperty property)
    {
        IntrinsicSizes basis;
        switch (value.BasisKind)
        {
            case SizeKind.None:
                basis = default;
                break;

            case SizeKind.CalcSize:
                if (ResolveCalcSizeContribution(value.NestedBasis!, property) is not { } nested)
                    return null;

                basis = nested.Add(-BoxSizingEdges);
                break;

            case SizeKind.LengthPercentage:
                if (value.BasisLength.HasPercentage)
                    return null;

                basis = IntrinsicSizes.FromSize(value.BasisLength.Resolve(0));
                break;

            case SizeKind.Auto:
                if (property != SizingProperty.Preferred)
                    return null;

                basis = GetIntrinsicSizes().Add(-BoxSizingEdges);
                break;

            default:
                if (ResolveContribution(Size.FromKind(value.BasisKind), property) is not { } keyword)
                    return null;

                basis = keyword.Add(-BoxSizingEdges);
                break;
        }

        return new IntrinsicSizes(ToBorderBox(value.Evaluate(Math.Max(0, basis.MinContent), percentageBasis: null)), ToBorderBox(value.Evaluate(Math.Max(0, basis.MaxContent), percentageBasis: null)));
    }
}
