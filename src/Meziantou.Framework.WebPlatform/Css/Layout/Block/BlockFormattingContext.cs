using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a block container whose children are block-level (https://drafts.csswg.org/css2/#block-formatting): the
/// in-flow children are stacked in the block axis, from the block-start edge of the content box, and their block-axis margins
/// collapse (https://drafts.csswg.org/css2/#collapsing-margins). It is also the fallback layout of the formatting contexts that
/// are not implemented yet (math, ruby...: their children are stacked as blocks, without collapsing their margins).
/// </summary>
/// <remarks>
/// <para>
/// The margins collapse between the boxes that participate in the same block formatting context: a box that does not
/// establish a new formatting context (<see cref="EstablishesNewFormattingContext"/>) receives the state of the context of its
/// parent (<see cref="ConstraintSpace.BlockFormattingContext"/>), its block-start margin collapses with the ones of its first
/// children when no border or padding separates them, and its block-end margin with the ones of its last children when its
/// block size is <c>auto</c> (see <see cref="FormattingContext.Layout"/>, which returns them in
/// <see cref="BoxFragment.CollapsedMargins"/>). The children are then placed by <see cref="MarginCollapsing"/>: the adjoining
/// margins of the siblings collapse, the margins of the empty boxes collapse through them, and the margins that collapse with
/// the margins of the parent are left outside of its content box.
/// </para>
/// <para>
/// The state of the block formatting context (<see cref="BlockFormattingContextState"/>) tracks the position of the boxes and
/// their pending margins in the coordinates of the root: the floats are placed in it at or below their static position
/// (<see cref="PlaceFloat"/>, https://drafts.csswg.org/css2/#float-position), the boxes with <c>clear</c> get their clearance
/// from it, the boxes that establish a new formatting context are placed next to the floats (<see cref="LayoutIndependentBox"/>),
/// and a block formatting context root contains its floats. The static position of the absolutely positioned boxes is recorded
/// for their containing block (see <see cref="PositionedLayout"/>).
/// </para>
/// </remarks>
internal sealed class BlockFormattingContext : FormattingContext
{
    public static BlockFormattingContext Instance { get; } = new();

    private BlockFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Block;

    /// <summary>
    /// Returns whether a block-level box establishes an independent formatting context (https://drafts.csswg.org/css-display/#independent-formatting-context),
    /// a new block formatting context for a block container (https://drafts.csswg.org/css2/#block-formatting): its margins do not
    /// collapse with the margins of its children, the floats inside it do not affect the outside, and the floats outside do not
    /// intrude into it. Otherwise the box participates in the block formatting context of its parent.
    /// </summary>
    /// <param name="box">The box.</param>
    /// <param name="containingBlock">The writing mode and the direction of its containing block.</param>
    public static bool EstablishesNewFormattingContext(ContainerBox box, WritingDirection containingBlock)
    {
        // The replaced boxes, the table wrapper (https://drafts.csswg.org/css-tables-3/#table-wrapper-box) and the formatting
        // contexts other than block and inline layout (flex, grid, table, ruby, math)
        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || GetKind(box) is not (FormattingContextKind.Block or FormattingContextKind.Inline))
            return true;

        // "Floats, absolutely positioned elements, block containers (such as inline-blocks, table-cells, and table-captions)
        // that are not block boxes, and block boxes with 'overflow' other than 'visible' (except when that value has been
        // propagated to the viewport) establish new block formatting contexts for their contents." The flex and grid items too
        // (https://drafts.csswg.org/css-flexbox-1/#flex-items), and display: flow-root (https://drafts.csswg.org/css-display/#valdef-display-flow-root)
        var display = box.Display;
        if (!display.IsBlockLevel || display.Internal != DisplayInternal.None || display.Inside == DisplayInside.FlowRoot || box.IsOutOfFlow
            || box.Parent is { } parent && (parent.EstablishesIndependentChildrenLayout || parent.LayoutApi is not null))
        {
            // (the children of a layout API container too: https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers)
            return true;
        }

        // The anonymous block boxes have the initial values of the non-inherited properties (and inherit the writing mode)
        if (box.IsAnonymous)
            return false;

        // https://drafts.csswg.org/css-display/#root: the root element's box establishes an independent formatting context
        var style = box.Style;
        if (style.IsRoot)
            return true;

        var layout = style.Layout;

        // https://drafts.csswg.org/css-contain-2/#containment-layout, https://drafts.csswg.org/css-contain-2/#containment-paint:
        // layout and paint containment (content-visibility: auto and hidden apply both); the size containers
        // (https://drafts.csswg.org/css-conditional-5/#container-type)
        if (UsedContainment.HasLayoutContainment(box) || UsedContainment.HasPaintContainment(box)
            || ((layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize)) != ContainerType.Normal && UsedContainment.CanApplySizeContainment(box)))
        {
            return true;
        }

        // https://drafts.csswg.org/css-overflow-3/#scroll-container: the scroll containers (except when their overflow is
        // propagated to the viewport)
        if (ScrollContainers.IsScrollContainer(box))
            return true;

        // https://drafts.csswg.org/css-multicol-1/#the-multi-column-model: a multi-column container establishes a new block formatting context
        if (layout.IsMultiColumnContainer)
            return true;

        // https://drafts.csswg.org/css-overflow-4/#line-clamp: a line-clamp container (its line boxes are counted in its block
        // formatting context, WPT css/css-overflow/line-clamp/line-clamp-bfc.html)
        if (LineClamp.IsLineClampContainer(box))
            return true;

        // https://drafts.csswg.org/css-rhythm/#block-step-size: a value other than none makes the box establish an independent
        // formatting context (the floats outside of it cannot change its rounded block size)
        if (layout.BlockStep is not null)
            return true;

        // https://drafts.csswg.org/css-writing-modes-4/#block-flow: "If a box has a different writing-mode value than its parent
        // box [...] If the box is a block container, then it establishes a new block formatting context."
        if (layout.WritingMode != containingBlock.WritingMode)
            return true;

        // https://drafts.csswg.org/css-align-3/#distribution-block: "All values other than normal force the block container to
        // establish an independent formatting context." (the computed value is read: the alignment group is not needed)
        if (style.GetComputedValue("align-content") != "normal")
            return true;

        // https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements: "The element is expected to
        // establish a new block formatting context"; the anonymous button content box of a button
        // (https://html.spec.whatwg.org/multipage/rendering.html#button-layout) establishes one too
        return box.PseudoElement is null && box.Element is { } element && (element.IsHtml(TagId.Fieldset) || element.IsHtml(TagId.Button));
    }

    /// <summary>
    /// Whether the <c>auto</c> inline size of a box is its shrink-to-fit size: the atomic inlines, the outside markers, the
    /// out-of-flow boxes (an absolutely positioned box whose inline insets are both set gets its stretch-fit size from the
    /// positioning instead, <see cref="ConstraintSpace.StretchInlineSize"/>), and the buttons: "If the computed value of
    /// 'inline-size' is 'auto', then the used value is the fit-content inline size"
    /// (https://html.spec.whatwg.org/multipage/rendering.html#button-layout); the size of a button that is a flex or grid item
    /// is given by its container. The rendered legend of a fieldset too: "If the computed value of 'inline-size' is 'auto', then
    /// the used value is the fit-content inline size" (https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements).
    /// </summary>
    internal static bool UsesShrinkToFit(ContainerBox box)
        => box.IsOutOfFlow || box.IsAtomicInline || box.IsOutsideMarker
        || box.PseudoElement is null && box.Element is { } element
            && (element.IsHtml(TagId.Button) && box.Parent is not { EstablishesIndependentChildrenLayout: true } || element.IsHtml(TagId.Legend) && IsRenderedLegend(box));

    /// <summary>
    /// Returns the offset, from the inline-start edge of the content box, of the inline-start edge of the content of an empty
    /// line box placed at the current position of the block formatting context: the line box is shortened by the floats
    /// (https://drafts.csswg.org/css2/#floats) and its content is aligned by <c>text-align</c>
    /// (https://drafts.csswg.org/css-text-4/#text-align-property). This is the static position, in the inline axis, of an
    /// absolutely positioned box whose original display is inline-level among block-level boxes.
    /// </summary>
    private static double GetEmptyLineStaticOffset(ContainerBox box, BlockFormattingContextState blockFormattingContext, in ContentArea content)
    {
        var isRightToLeft = content.WritingDirection.IsRightToLeft;
        var lineLeft = content.BlockFormattingContextOffset.Inline;
        var lineRight = lineLeft + content.InlineSize;
        var opportunity = blockFormattingContext.Exclusions.GetOpportunity(blockFormattingContext.BlockOffset + blockFormattingContext.CurrentMargin.Sum, blockSize: 0, lineLeft, content.InlineSize, useFloatAreas: true);
        // The floats that leave no space: the line box is empty, at its start edge
        var left = opportunity.LineLeft;
        var right = opportunity.LineRight;
        if (right < left)
        {
            (left, right) = isRightToLeft ? (right, right) : (left, left);
        }

        var position = box.Style.Layout.Text.TextAlign switch
        {
            TextAlign.Left => left,
            TextAlign.Right => right,
            TextAlign.Center => (left + right) / 2,
            TextAlign.End => isRightToLeft ? left : right,
            _ => isRightToLeft ? right : left,
        };

        return isRightToLeft ? lineRight - position : position - lineLeft;
    }

    /// <summary>
    /// Returns the inline size that the floats take at the inline-start edge of the content box, at the current position of the
    /// block formatting context (the offset of the start edge of an empty line box placed there).
    /// </summary>
    private static double GetEmptyLineStartOffset(BlockFormattingContextState blockFormattingContext, in ContentArea content)
    {
        var lineLeft = content.BlockFormattingContextOffset.Inline;
        var opportunity = blockFormattingContext.Exclusions.GetOpportunity(blockFormattingContext.BlockOffset + blockFormattingContext.CurrentMargin.Sum, blockSize: 0, lineLeft, content.InlineSize, useFloatAreas: true);
        var offset = content.WritingDirection.IsRightToLeft ? lineLeft + content.InlineSize - opportunity.LineRight : opportunity.LineLeft - lineLeft;
        return Math.Max(0, Math.Min(offset, content.InlineSize));
    }

    /// <summary>
    /// Whether a block container has the block size of a line when it has no content: the principal box of an editing host
    /// (https://html.spec.whatwg.org/multipage/interaction.html#editing-host), where the browsers reserve the line of the caret
    /// (the <c>HasLineIfEmpty</c> of Blink), so that an empty editing host can be seen and pressed.
    /// </summary>
    internal static bool HasCaretLineIfEmpty(ContainerBox box)
        => box is { Kind: ContainerBoxKind.Normal, IsAnonymous: false, PseudoElement: null, Element: HtmlElement element } && Editing.IsEditingHost(element);

    /// <summary>Whether an in-flow box precedes a box in its parent (the out-of-flow boxes and the outside marker are skipped).</summary>
    private static bool HasInFlowBoxBefore(ContainerBox box)
    {
        for (var sibling = box.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
        {
            if (sibling is not ContainerBox container || (!container.IsOutOfFlow && !container.IsOutsideMarker))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the box of a legend element is the rendered legend of its fieldset (https://html.spec.whatwg.org/multipage/rendering.html#rendered-legend):
    /// "the first child of a fieldset element, if it is a legend element, and is not floating or absolutely positioned" (the
    /// first in-flow legend box of the box of the fieldset).
    /// </summary>
    internal static bool IsRenderedLegend(ContainerBox box)
        => !box.IsOutOfFlow && box.Parent is { } parent && IsFieldset(parent) && ReferenceEquals(FindRenderedLegend(parent), box);

    /// <summary>Returns whether a box is the principal box of a fieldset element (see <see cref="FieldsetFormattingContext"/>).</summary>
    internal static bool IsFieldset(ContainerBox box)
        => box.Kind == ContainerBoxKind.Normal && box.PseudoElement is null && box.Element is { } element && element.IsHtml(TagId.Fieldset);

    /// <summary>Returns the rendered legend of the principal box of a fieldset (see <see cref="IsRenderedLegend"/>), or null.</summary>
    internal static ContainerBox? FindRenderedLegend(ContainerBox fieldset)
    {
        for (var child = fieldset.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is ContainerBox { IsOutOfFlow: false, PseudoElement: null, Element: { } legend, IsBlockLevel: true } legendBox && legend.IsHtml(TagId.Legend))
                return legendBox;
        }

        return null;
    }

    /// <summary>
    /// Returns the style whose <c>overflow</c> applies to the contents of a box: the one of the box; for the principal box of a
    /// fieldset, the one of its anonymous fieldset content box (a fieldset that scrolls has no automatic minimum size).
    /// </summary>
    internal static LayoutStyle GetContentsStyle(ContainerBox box)
        => IsFieldset(box) && FindFieldsetContent(box) is { } content ? content.Style.Layout : box.Style.Layout;

    /// <summary>Returns the anonymous fieldset content box of the principal box of a fieldset, or null.</summary>
    internal static ContainerBox? FindFieldsetContent(ContainerBox fieldset)
        => fieldset.LastChild is ContainerBox { IsFieldsetContent: true } content ? content : null;

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a block container
    /// (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes): the largest min-content contribution of its children, and the
    /// largest max-content contribution of its children (<see cref="IntrinsicSizing.GetContribution"/>), the floats placed side by
    /// side being added (and the boxes that avoid the floats next to them).
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        var layout = box.Style.Layout;
        var writingDirection = WritingDirection.FromStyle(layout);
        var minContent = 0d;
        var maxContent = 0d;
        var lineLeftFloats = 0d;
        var lineRightFloats = 0d;
        foreach (var child in box.Children)
        {
            if (child is not ContainerBox childBox || childBox.IsOutsideMarker || PositionedLayout.IsAbsolutelyPositioned(childBox))
                continue;

            if (context.ColumnSpannerSizes is { } spannerSizes && MultiColumnFlow.IsColumnSpanner(childBox))
            {
                // A column spanner is not content of the columns: the multi-column container adds its contribution
                context.ColumnSpannerSizes = null;
                var (spannerMin, spannerMax) = IntrinsicSizing.GetContribution(context, childBox, writingDirection, input.ContentBlockSize, quirksPercentageBlockSize: input.QuirksPercentageBlockSize);
                context.ColumnSpannerSizes = spannerSizes;
                spannerSizes.MinContent = Math.Max(spannerSizes.MinContent, spannerMin);
                spannerSizes.MaxContent = Math.Max(spannerSizes.MaxContent, spannerMax);
                continue;
            }

            var (childMin, childMax) = IntrinsicSizing.GetContribution(context, childBox, writingDirection, input.ContentBlockSize, quirksPercentageBlockSize: input.QuirksPercentageBlockSize);
            minContent = Math.Max(minContent, childMin);
            var childLayout = childBox.Style.Layout;
            var clear = ExclusionSpace.GetClearSides(childLayout.Clear, writingDirection);
            if (clear != ClearSides.None)
            {
                maxContent = Math.Max(maxContent, lineLeftFloats + lineRightFloats);
                lineLeftFloats = (clear & ClearSides.LineLeft) != ClearSides.None ? 0 : lineLeftFloats;
                lineRightFloats = (clear & ClearSides.LineRight) != ClearSides.None ? 0 : lineRightFloats;
            }

            if (childBox.IsOutOfFlow)
            {
                // The floats are placed side by side
                if (ExclusionSpace.GetFloatSide(childLayout.Float, writingDirection) == FloatSide.LineLeft)
                {
                    lineLeftFloats += childMax;
                }
                else
                {
                    lineRightFloats += childMax;
                }

                continue;
            }

            if (EstablishesNewFormattingContext(childBox, writingDirection))
            {
                // A box that avoids the floats is placed next to them
                maxContent = Math.Max(maxContent, lineLeftFloats + lineRightFloats + childMax);
            }
            else
            {
                maxContent = Math.Max(maxContent, Math.Max(lineLeftFloats + lineRightFloats, childMax));
                lineLeftFloats = 0;
                lineRightFloats = 0;
            }
        }

        maxContent = Math.Max(maxContent, lineLeftFloats + lineRightFloats);
        return new IntrinsicSizes(minContent, Math.Max(minContent, maxContent));
    }

    /// <summary>
    /// Returns the sides of a box whose adjacent margins of its in-flow children are trimmed (https://drafts.csswg.org/css-box-4/#margin-trim):
    /// the computed value of <c>margin-trim</c>, which applies to the block containers and to the multi-column containers.
    /// </summary>
    internal static MarginTrimSides GetMarginTrim(ContainerBox box)
        => !box.IsAnonymous && box.Kind == ContainerBoxKind.Normal && GetKind(box) is FormattingContextKind.Block or FormattingContextKind.Inline ? box.Style.Layout.MarginTrim : MarginTrimSides.None;

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        // https://drafts.csswg.org/css-overflow-4/#line-clamp: a line-clamp container measures its clamp point, then is clamped
        if (content.BlockFormattingContext is null && content.LineClamp is null && LineClamp.IsLineClampContainer(box))
            return LineClamp.Layout(context, this, box, builder, content);

        // https://drafts.csswg.org/css-text-4/#text-group-align-property: the line boxes are measured, then aligned as a group
        if (TextGroupAlignment.Applies(box, content))
            return TextGroupAlignment.Layout(context, this, box, builder, content);

        // https://drafts.csswg.org/css-box-4/#margin-trim: the margins adjacent to the block-end edge are the block-end margin of
        // the last child that does not collapse through (with the margins of its last children that collapse with it) and the
        // margins of the children that collapse through after it. They are only known once the children are laid out: when
        // some of them are not zero, the children are laid out again, these margins being trimmed (the children after the clamp
        // point of a line-clamp container are counted by the first layout: their margins are not trimmed)
        if (!content.TrimsBlockEndMargins || GetKind(box) != FormattingContextKind.Block || (content.BlockFormattingContext?.LineClamp ?? content.LineClamp) is not null)
            return LayoutChildren(context, box, builder, content, endTrimIndex: null, out _);

        var stateBeforeChildren = content.BlockFormattingContext?.Save();
        var markerWasPlaced = content.OutsideMarker?.IsPlaced;
        var result = LayoutChildren(context, box, builder, content, endTrimIndex: null, out var trimIndex);
        if (trimIndex is null)
            return result;

        if (stateBeforeChildren is { } state)
        {
            content.BlockFormattingContext!.Restore(state);
        }

        if (content.OutsideMarker is { } outsideMarker && markerWasPlaced == false)
        {
            outsideMarker.IsPlaced = false;
        }

        builder.Reset(builder.WritingDirection);
        return LayoutChildren(context, box, builder, content, trimIndex, out _);
    }

    /// <summary>Lays out the children of a block container (see <see cref="LayoutContents"/>).</summary>
    /// <param name="endTrimIndex">
    /// The index of the child whose block-end margin is trimmed by <c>margin-trim</c> (the last child that does not collapse
    /// through, -1 when all the children do): both margins of the children after it are trimmed too. Null when the margins
    /// adjacent to the block-end edge are not trimmed, or not known yet.
    /// </param>
    /// <param name="requiredEndTrimIndex">
    /// The value of <paramref name="endTrimIndex"/> the children must be laid out again with: the box trims the margins adjacent
    /// to its block-end edge (<see cref="ContentArea.TrimsBlockEndMargins"/>) and some of them are not zero. Null otherwise.
    /// </param>
    private ContentLayoutResult LayoutChildren(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content, int? endTrimIndex, out int? requiredEndTrimIndex)
    {
        var writingDirection = content.WritingDirection;
        var childSpace = content.ChildSpace;

        // A block formatting context root creates the state of its context; the boxes that participate in the context of their
        // parent continue it
        var isNewFormattingContext = content.BlockFormattingContext is null;
        var blockFormattingContext = content.BlockFormattingContext ?? new BlockFormattingContextState { MultiColumnFlow = content.MultiColumnFlow, LineClamp = content.LineClamp, TextGroupAlignment = content.TextGroupAlignment };

        // The fallback layout of the formatting contexts that are not implemented yet (math, ruby...): the margins of
        // their children do not collapse
        var collapsesMargins = GetKind(box) == FormattingContextKind.Block;

        // https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing: the margins of the in-flow children that would collapse with
        // the margins of the box are treated as zero for their stretch-fit block size
        var inFlowSpace = collapsesMargins
            ? childSpace with { StretchIgnoresBlockStartMargin = content.StartMarginCollapsesWithChildren, StretchIgnoresBlockEndMargin = content.EndMarginMayCollapseWithChildren }
            : childSpace;
        var participantSpace = inFlowSpace with { BlockFormattingContext = blockFormattingContext, BlockFormattingContextLineOffset = content.BlockFormattingContextOffset.Inline };
        var placement = new MarginCollapsing(content.StartMarginCollapsesWithChildren);
        double? firstBaseline = null;
        double? lastBaseline = null;

        // https://drafts.csswg.org/css-overflow-4/#line-clamp: the children after the clamp point are hidden, and the box ends at
        // the clamp point: the placement and the state of the block formatting context are restored at the end
        var lineClamp = collapsesMargins ? blockFormattingContext.LineClamp : null;
        ClampedState? clamped = null;

        // https://drafts.csswg.org/css-lists-3/#list-style-position-outside: the outside marker of a list item is placed next to
        // its first line box, which is in its first in-flow child (or in a descendant of it): the marker is passed to that child
        // https://drafts.csswg.org/css-box-4/#margin-trim
        var trimsStartMargins = collapsesMargins && content.TrimsBlockStartMargins;
        var childIndex = -1;
        var lastContentIndex = -1;
        var lastContentHasSeparateEndMargin = false;

        var marker = content.OutsideMarker;
        PendingOutsideMarker? ownMarker = null;
        foreach (var child in box.Children)
        {
            if (child is ContainerBox { IsOutsideMarker: true } markerBox)
            {
                ownMarker = new PendingOutsideMarker(markerBox, box);
                marker = ownMarker;
                break;
            }
        }

        foreach (var child in box.Children)
        {
            childIndex++;

            // The text runs of a block container with block-level children are collapsible white space (dropped by the box tree)
            if (child is not ContainerBox childBox || childBox.IsOutsideMarker)
                continue;

            if (lineClamp is { IsClamped: true } && clamped is null)
            {
                clamped = new ClampedState(placement, blockFormattingContext.Save(), firstBaseline, lastBaseline);
            }

            if (PositionedLayout.IsAbsolutelyPositioned(childBox))
            {
                // https://drafts.csswg.org/css-position-3/#staticpos-rect: "a zero-thickness rectangle spanning between the
                // inline-axis sides of the box's static-position containing block and positioned at its block-start static
                // position": the current position, after the margins collapsed so far, which the box does not affect; the box is
                // aligned in it with its self-alignment properties. It is laid out by its containing block (the grid containers
                // record the static positions of their children themselves)
                var (inlineEdge, blockEdge) = PositionedLayout.GetStaticPositionAlignment(childBox, box, writingDirection);
                var staticInline = content.Offset.Inline + inlineEdge switch
                {
                    StaticPositionEdge.End => content.InlineSize,
                    StaticPositionEdge.Center => content.InlineSize / 2,
                    _ => 0,
                };

                if (inlineEdge == StaticPositionEdge.Start && PositionedLayout.IsOriginalDisplayInlineLevel(childBox.Style))
                {
                    // A box whose original display is inline-level would be in a line box: its static position is where an empty
                    // line box would place it at the current position, in the space that the floats leave and aligned by
                    // `text-align` (as in the browsers, WPT css/css-position/static-position/inline-level-absolute-in-block-level-context-*.html)
                    if (collapsesMargins)
                    {
                        staticInline += GetEmptyLineStaticOffset(box, blockFormattingContext, content);
                    }

                    // The line would be the first line of the block container when no in-flow box precedes the box: its static
                    // position is after the indentation of that line (as in the browsers, WPT
                    // css/css-text/text-indent/text-indent-with-absolute-pos-child.html)
                    if (!box.IsAnonymous && box.Style.Layout.Text is { TextIndentHanging: false } text && !HasInFlowBoxBefore(childBox))
                    {
                        staticInline += text.TextIndent.Resolve(content.InlineSize);
                    }
                }

                builder.AddOutOfFlowCandidate(childBox, new LogicalOffset(staticInline, content.Offset.Block + placement.StaticPosition), inlineEdge, blockEdge);
                continue;
            }

            if (childBox.IsOutOfFlow)
            {
                // https://drafts.csswg.org/css2/#float-position: a float is placed in the block formatting context, at or below its
                // static position (the current position, after the margins collapsed so far), which it does not affect
                var floatFragment = context.LayoutBox(childBox, childSpace);
                floatFragment.IsHiddenForPaint = clamped is not null;
                if (collapsesMargins)
                {
                    PlaceFloat(context, blockFormattingContext, childBox, floatFragment, content, builder, blockFormattingContext.BlockOffset + blockFormattingContext.CurrentMargin.Sum, PhysicalOffset.Zero);
                }
                else
                {
                    var floatMargin = writingDirection.ToLogical(floatFragment.Margin);
                    builder.AddChild(floatFragment, new LogicalOffset(content.Offset.Inline + floatMargin.InlineStart, content.Offset.Block + placement.StaticPosition + floatMargin.BlockStart));
                }

                continue;
            }

            // The marker goes to the first in-flow child, when it is a block container (it stays unplaced otherwise). A box that
            // clips its overflow would clip the marker, which is outside of its content box: the marker is then aligned with
            // the first baseline of the child by the box
            var childLayout = childBox.Style.Layout;
            var clipsOverflow = childLayout.OverflowX != OverflowKind.Visible || childLayout.OverflowY != OverflowKind.Visible;
            var childMarker = marker is not null && !clipsOverflow && childBox.IsBlockContainer && childBox.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous ? marker : null;
            marker = null;

            BoxFragment fragment;
            CollapsedBlockMargins childMargins;
            double inlineOffset;
            var extraBlockSize = 0d;

            var startsAfterMaxLines = lineClamp?.BeginBox() ?? false;

            // Whether the block-start margin of the child is adjoining to the block-start margin of the box (and was added to the
            // state of the context with it): clearance separates them (see BlockFormattingContextState.Separate)
            var isStartMarginCollapsedWithParent = content.StartMarginCollapsesWithChildren && placement.NextMarginCollapsesWithParentStartMargin;

            // https://drafts.csswg.org/css-box-4/#margin-trim: "The block-start margin of a block-level first child, when
            // trimming at the block-start edge. The block-end margin of a block-level last child, when trimming at the block-end
            // edge. Any margin collapsed with these margins": the margins of the children are adjacent to the block-start edge
            // until a child does not collapse through; the ones adjacent to the block-end edge were found by a first layout
            var isAtBlockStart = trimsStartMargins && placement.IsBeforeFirstContent;
            var trimsStartMargin = isAtBlockStart || endTrimIndex is { } firstTrimmed && childIndex > firstTrimmed;
            var trimsEndMargin = endTrimIndex is { } lastContent && childIndex >= lastContent;
            var marginsBeforeChild = blockFormattingContext.CurrentMargin;
            var collapseCountBeforeChild = blockFormattingContext.CollapseCount;
            var hasSeparateEndMargin = false;
            if (collapsesMargins && blockFormattingContext.MultiColumnFlow is { } multiColumnFlow && multiColumnFlow.IsValidColumnSpanner(childBox))
            {
                // https://drafts.csswg.org/css-multicol-1/#column-span: a column spanner is laid out in the content box of the
                // multi-column container; the multi-column layout takes it out of the columns
                fragment = LayoutColumnSpanner(context, blockFormattingContext, multiColumnFlow, childBox, isStartMarginCollapsedWithParent, trimsStartMargin, trimsEndMargin, out childMargins);
                inlineOffset = content.Offset.Inline + writingDirection.ToLogical(fragment.Margin).InlineStart;

                // The block-end margin of the spanner is part of its extent: it does not collapse with the margins of the content after it
                extraBlockSize = multiColumnFlow.WritingDirection.ToLogical(fragment.Margin).BlockEnd;
                hasSeparateEndMargin = extraBlockSize != 0;
            }
            else if (collapsesMargins && !EstablishesNewFormattingContext(childBox, writingDirection))
            {
                // The child participates in the block formatting context: it updates the state itself and returns its margins
                // collapsed with the ones of its children. The block-start margin of the first children that collapse with the
                // margins of the box were looked ahead by the box (or by an ancestor)
                var space = participantSpace with
                {
                    OutsideMarker = childMarker,
                    IsStartMarginCollapsedWithAncestor = isStartMarginCollapsedWithParent,
                    TrimsBlockStartMargin = trimsStartMargin,
                    TrimsBlockEndMargin = trimsEndMargin,
                };
                fragment = context.LayoutBox(childBox, space);
                childMargins = fragment.CollapsedMargins;
                inlineOffset = content.Offset.Inline + writingDirection.ToLogical(fragment.Margin).InlineStart;

                // The content after a box that ends before a column spanner of its contents is placed after the spanner
                extraBlockSize = blockFormattingContext.MultiColumnFlow?.GetSpannerOverflow(childBox) ?? 0;
            }
            else if (collapsesMargins)
            {
                var space = childMarker is null && !trimsStartMargin && !trimsEndMargin
                    ? inFlowSpace
                    : inFlowSpace with { OutsideMarker = childMarker, TrimsBlockStartMargin = trimsStartMargin, TrimsBlockEndMargin = trimsEndMargin };
                fragment = LayoutIndependentBox(context, blockFormattingContext, childBox, space, content, isStartMarginCollapsedWithParent, out childMargins, out inlineOffset);
            }
            else
            {
                fragment = context.LayoutBox(childBox, childMarker is null ? childSpace : childSpace with { OutsideMarker = childMarker });
                childMargins = GetMargins(fragment, writingDirection);
                inlineOffset = content.Offset.Inline + writingDirection.ToLogical(fragment.Margin).InlineStart;
            }

            if (isAtBlockStart && childMargins is { CollapsedThrough: true, Clearance: null })
            {
                // The margins of a box that collapses through are all adjacent to the block-start edge: its block-end margin (and
                // the ones that collapsed with it) are trimmed too
                if (blockFormattingContext.CollapseCount == collapseCountBeforeChild)
                {
                    blockFormattingContext.RestoreMargins(marginsBeforeChild);
                }

                fragment.TrimMargins(writingDirection, blockStart: false, blockEnd: true);
                childMargins = TrimmedCollapsedThrough;
            }

            if (!childMargins.CollapsedThrough || childMargins.Clearance is not null)
            {
                lastContentIndex = childIndex;
                lastContentHasSeparateEndMargin = hasSeparateEndMargin;
            }

            var size = writingDirection.ToLogical(fragment.Size);
            var blockOffset = collapsesMargins ? placement.Place(childMargins, size.Block + extraBlockSize) : placement.PlaceWithoutCollapsing(childMargins, size.Block);
            builder.AddChild(fragment, new LogicalOffset(inlineOffset, content.Offset.Block + blockOffset));
            fragment.IsHiddenForPaint = clamped is not null;
            lineClamp?.EndBox(startsAfterMaxLines, hasContent: !childMargins.CollapsedThrough);

            // https://drafts.csswg.org/css-align-3/#baseline-export: the first and last baselines of the in-flow children
            // (with the same block-start side)
            if (fragment.WritingDirection.BlockStart == writingDirection.BlockStart)
            {
                if (fragment.FirstBaseline is { } first && firstBaseline is null)
                {
                    firstBaseline = blockOffset + first;
                }

                // A block-level table gives no last baseline to an inline-block whose baseline-source is auto (the baseline of an
                // inline-block is the one of its last line box, as in the browsers: WPT css/css-tables/tentative/baseline-table.html);
                // its last baseline is the one of its last row for baseline-source: last and for the last-baseline alignment
                // (https://drafts.csswg.org/css-align-3/#baseline-export). For the baseline of an inline-block,
                // a block container that is a scroll container in the block axis gives the block-end edge of its margin box, as
                // an inline-block does (https://drafts.csswg.org/css2/#leading: "unless it has either no in-flow line boxes or if
                // its 'overflow' property has a computed value other than 'visible', in which case the baseline is the bottom
                // margin edge"; as in the browsers, WPT css/CSS2/linebox/baseline-block-with-overflow-001.html)
                if (content.UsesInlineBlockBaseline && childBox.Kind == ContainerBoxKind.Normal && childBox.Display.Inside is DisplayInside.Flow or DisplayInside.FlowRoot
                    && childLayout.IsScrollContainer && ScrollContainers.IsScrollable(childLayout.IsHorizontalWritingMode ? childLayout.OverflowY : childLayout.OverflowX))
                {
                    lastBaseline = blockOffset + writingDirection.ToLogical(fragment.Size).Block + writingDirection.ToLogical(fragment.Margin).BlockEnd;
                }
                else if (fragment.LastBaseline is { } last && !(content.UsesInlineBlockBaseline && childBox.Kind == ContainerBoxKind.TableWrapper))
                {
                    lastBaseline = blockOffset + last;
                }
            }
        }

        // The box ends at the clamp point: the children after it do not change its size nor its baselines
        if (clamped is { } clampedState)
        {
            placement = clampedState.Placement;
            blockFormattingContext.Restore(clampedState.Context);
            firstBaseline = clampedState.FirstBaseline;
            lastBaseline = clampedState.LastBaseline;
        }

        // https://drafts.csswg.org/css2/#clearance: the margins that collapsed through a box with clearance do not collapse with the
        // block-end margin of the parent, they are part of its contents (in the state of the context too)
        if (collapsesMargins && placement.SeparatesEndMargin)
        {
            blockFormattingContext.CollapseMargins();
        }

        // https://drafts.csswg.org/css-box-4/#margin-trim: the margins adjacent to the block-end edge that are not zero are
        // trimmed by a second layout of the children (see LayoutContents)
        requiredEndTrimIndex = collapsesMargins && content.TrimsBlockEndMargins && endTrimIndex is null && clamped is null && (placement.HasEndMargins || lastContentHasSeparateEndMargin)
            ? lastContentIndex
            : null;

        var result = placement.Finish();

        // The marker of an ancestor list item that no line box placed: when the box has a first baseline (the one of a flex or
        // grid container, of a table, of a child that establishes a formatting context), the marker is aligned with it, next
        // to the content box of the box (as in the browsers). The list item places it itself when its content is fragmented in
        // columns: the offsets of the column boxes are not the ones of the list item
        if (ownMarker is null && content.OutsideMarker is { IsPlaced: false } inheritedMarker && firstBaseline is { } markerBaseline && blockFormattingContext.MultiColumnFlow is null)
        {
            inheritedMarker.PlaceAtBaseline(context, builder, content, markerBaseline);
        }

        // A list item whose marker was not placed by a line box: the marker is at the block-start edge of the content box, and
        // the content is at least as tall as the marker (as in the browsers)
        if (ownMarker is { IsPlaced: false })
        {
            // (an empty list item beside a float: the marker is beside the float, where its line box would be)
            var floatIntrusion = result.CollapsedThrough && collapsesMargins && !blockFormattingContext.Exclusions.IsEmpty ? GetEmptyLineStartOffset(blockFormattingContext, content) : 0;
            var markerSize = ownMarker.PlaceWithoutLine(context, builder, content, firstBaseline, floatIntrusion);
            var contentSize = result.BlockSize + result.EndMargin.Sum;
            if (result.CollapsedThrough || markerSize > contentSize)
            {
                var extra = result.CollapsedThrough ? markerSize : markerSize - contentSize;
                blockFormattingContext.CollapseMargins();
                blockFormattingContext.AdvanceBlockPosition(extra);
                result = (contentSize + extra, result.StartMargin, default, false);
            }
        }

        // As in the browsers, an editing host always has the line of its caret: its contents are at least as tall as a line
        if (HasCaretLineIfEmpty(box))
        {
            var lineHeight = box.Style.Layout.UsedLineHeight;
            var contentSize = result.BlockSize + result.EndMargin.Sum;
            if (result.CollapsedThrough || lineHeight > contentSize)
            {
                var extra = result.CollapsedThrough ? lineHeight : lineHeight - contentSize;
                blockFormattingContext.CollapseMargins();
                blockFormattingContext.AdvanceBlockPosition(extra);
                result = (contentSize + extra, result.StartMargin, default, false);
            }
        }

        // The state of the block formatting context follows the same steps as the placement of the children: at the end of the
        // contents of the root, its position (with the pending margins) is the block size of the contents
        Debug.Assert(!isNewFormattingContext || !collapsesMargins || Math.Abs(blockFormattingContext.BlockOffset + blockFormattingContext.CurrentMargin.Sum - (result.BlockSize + result.EndMargin.Sum)) < 0.01, "The block formatting context state is out of sync with the placement of the children");

        // https://drafts.csswg.org/css2/#root-height: "if the element has any floating descendants whose bottom margin edge is
        // below the element's bottom content edge, then the height is increased to include those edges. Only floats that
        // participate in this block formatting context are taken into account"
        // (the floats of a line-clamp container are given to its line clamping, see LineClamp)
        if (isNewFormattingContext && blockFormattingContext.Exclusions.GetContentEnd() is { } floatsEnd && floatsEnd > result.BlockSize + result.EndMargin.Sum)
        {
            if (blockFormattingContext.LineClamp is { } rootLineClamp)
            {
                rootLineClamp.FloatsEnd = floatsEnd;
            }
            else
            {
                result = (floatsEnd, result.StartMargin, default, false);
            }
        }

        // https://drafts.csswg.org/css2/#normal-block: the negative margins of the children may place the end of the contents
        // before the block-start edge of the content box, whose block size is never negative: the next boxes of the context are
        // placed after the content box
        if (result.BlockSize < 0 && !isNewFormattingContext && collapsesMargins)
        {
            blockFormattingContext.AdvanceBlockPosition(-result.BlockSize);
        }

        return new ContentLayoutResult(Math.Max(0, result.BlockSize), firstBaseline, lastBaseline)
        {
            StartMargin = result.StartMargin,
            EndMargin = result.EndMargin,
            CollapsedThrough = result.CollapsedThrough,
        };
    }

    /// <summary>The margins of a box that collapses through and whose margins are all trimmed by <c>margin-trim</c>.</summary>
    private static readonly CollapsedBlockMargins TrimmedCollapsedThrough = new(default, default, collapsedThrough: true, clearance: null);

    /// <summary>The placement of the children and the state of the block formatting context at the clamp point of a line-clamp container (see <see cref="LineClamp"/>).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ClampedState(MarginCollapsing Placement, BlockFormattingContextState.Snapshot Context, double? FirstBaseline, double? LastBaseline);

    /// <summary>Returns the block-axis margins of a box that does not collapse with its children.</summary>
    private static CollapsedBlockMargins GetMargins(BoxFragment fragment, WritingDirection writingDirection)
    {
        var margin = writingDirection.ToLogical(fragment.Margin);
        return CollapsedBlockMargins.Create(MarginStrut.FromMargin(margin.BlockStart), MarginStrut.FromMargin(margin.BlockEnd), collapsedThrough: false, clearance: null);
    }

    /// <summary>
    /// Places a float that was laid out (https://drafts.csswg.org/css2/#float-position) in a block formatting context, adds it
    /// to the exclusions of the context, and adds its fragment to the fragment of its containing block. Returns the margin box
    /// of the float in the coordinates of the block formatting context.
    /// </summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="blockFormattingContext">The block formatting context the float is placed in.</param>
    /// <param name="box">The float.</param>
    /// <param name="fragment">The fragment of the float.</param>
    /// <param name="content">The content box of the containing block of the float (with its position in the block formatting context).</param>
    /// <param name="builder">The builder of the fragment of the containing block.</param>
    /// <param name="ceiling">The highest position of the block-start margin edge of the float in the block formatting context: its static position.</param>
    /// <param name="adjustment">A physical offset added to the relative offset of the float (the relative offsets of the inline boxes it is in).</param>
    internal static LogicalRect PlaceFloat(LayoutContext context, BlockFormattingContextState blockFormattingContext, ContainerBox box, BoxFragment fragment, in ContentArea content, FragmentBuilder builder, double ceiling, PhysicalOffset adjustment)
    {
        var writingDirection = content.WritingDirection;
        var layout = box.Style.Layout;
        var exclusions = blockFormattingContext.Exclusions;

        // https://drafts.csswg.org/css2/#flow-control: "the top outer edge of the float must be below the bottom outer edge of
        // all earlier left-floating boxes (in the case of 'clear: left')..."
        if (exclusions.GetClearanceOffset(ExclusionSpace.GetClearSides(layout.Clear, writingDirection)) is { } clearOffset)
        {
            ceiling = Math.Max(ceiling, clearOffset);
        }

        // A float that does not fit in what remains of a fragmentainer is pushed to the start of the next one (see FloatPush)
        var visibleFrom = double.NegativeInfinity;
        var isDeferred = false;
        if (context.FloatPushes is { } pushes && pushes.TryGetValue(box, out var push))
        {
            ceiling = Math.Max(ceiling, push.MinBlockOffset);
            visibleFrom = push.MinBlockOffset;
            isDeferred = push.IsDeferred;
        }

        var side = ExclusionSpace.GetFloatSide(layout.Float, writingDirection);
        var size = writingDirection.ToLogical(fragment.Size);
        var lineLeftMargin = fragment.Margin.Get(writingDirection.LineLeft);
        var lineRightMargin = fragment.Margin.Get(writingDirection.LineRight);
        var blockStartMargin = fragment.Margin.Get(writingDirection.BlockStart);
        var blockEndMargin = fragment.Margin.Get(writingDirection.BlockEnd);
        var marginBoxSize = new LogicalSize(size.Inline + lineLeftMargin + lineRightMargin, size.Block + blockStartMargin + blockEndMargin);
        var position = exclusions.PlaceFloat(side, marginBoxSize, ceiling, content.BlockFormattingContextOffset.Inline, content.InlineSize);
        var marginBox = new LogicalRect(position, marginBoxSize);
        exclusions.Add(side, marginBox, FloatShapes.Create(box, fragment, writingDirection, marginBox, content.InlineSize), visibleFrom, isDeferred);
        if (context.MultiColumnDepth > 0)
        {
            context.FloatPlacements[box] = position.Block;
        }

        // The border box in the content box of the containing block (from its inline-start edge)
        var borderBoxLineLeft = position.Inline + lineLeftMargin - content.BlockFormattingContextOffset.Inline;
        var inlineStart = writingDirection.IsRightToLeft ? content.InlineSize - borderBoxLineLeft - size.Inline : borderBoxLineLeft;
        var blockStart = position.Block + blockStartMargin - content.BlockFormattingContextOffset.Block;
        fragment.RelativeOffset += adjustment;
        builder.AddChild(fragment, new LogicalOffset(content.Offset.Inline + inlineStart, content.Offset.Block + blockStart));
        return marginBox;
    }

    /// <summary>
    /// Lays out a column spanner (https://drafts.csswg.org/css-multicol-1/#column-span) in the content box of its multi-column
    /// container and updates the state of the block formatting context with it: "A spanning element establishes a new block
    /// formatting context", its block-start margin does not collapse with the margins of the content before it (it is placed as
    /// if it had clearance; it collapses with the margin of its parent when it is its first content, as in the browsers). The
    /// spanner ends the row of columns before it, so it is placed below the floats of that row; its margins do not
    /// collapse with the margins of the content of the columns (https://drafts.csswg.org/css-multicol-1/#spanning-columns:
    /// "margins on the elements inside a column box don't collapse with margins of a spanning element"), so its block-end
    /// margin is part of its extent in the strip (the margins of two adjacent spanners are collapsed by the multi-column layout).
    /// </summary>
    /// <param name="isStartMarginCollapsedWithParent">
    /// Whether the block-start margin of the spanner is adjoining to the block-start margin of its parent (and was added to the
    /// state of the context with it): they collapse (the parent has no content before the spanner).
    /// </param>
    /// <param name="trimsStartMargin">Whether the block-start margin of the spanner is trimmed by <c>margin-trim</c>.</param>
    /// <param name="trimsEndMargin">Whether the block-end margin of the spanner is trimmed by <c>margin-trim</c>.</param>
    private static BoxFragment LayoutColumnSpanner(LayoutContext context, BlockFormattingContextState blockFormattingContext, MultiColumnFlow multiColumnFlow, ContainerBox box, bool isStartMarginCollapsedWithParent, bool trimsStartMargin, bool trimsEndMargin, out CollapsedBlockMargins margins)
    {
        multiColumnFlow.SpannerBoxes.Add(box);
        var space = ConstraintSpace.ForContentBox(multiColumnFlow.WritingDirection, multiColumnFlow.InlineSize, multiColumnFlow.BlockSize) with { TrimsBlockStartMargin = trimsStartMargin, TrimsBlockEndMargin = trimsEndMargin };
        var fragment = context.LayoutBox(box, space);

        // https://drafts.csswg.org/css-box-4/#margin-trim: the margins of a spanner at an edge of the multi-column container
        fragment.TrimMargins(multiColumnFlow.WritingDirection, trimsStartMargin && fragment.UntrimmedMargin is null, trimsEndMargin && fragment.UntrimmedMargin is null);
        var margin = multiColumnFlow.WritingDirection.ToLogical(fragment.Margin);
        var blockSize = multiColumnFlow.WritingDirection.ToLogical(fragment.Size).Block;
        var contentEnd = Math.Max(blockFormattingContext.Exclusions.GetClearanceOffset(ClearSides.Both) ?? double.NegativeInfinity, multiColumnFlow.GetOverflowEnd() ?? double.NegativeInfinity);
        if (isStartMarginCollapsedWithParent && !blockFormattingContext.IsSeparated(box))
        {
            if (contentEnd <= blockFormattingContext.BlockOffset + blockFormattingContext.CurrentMargin.Sum - Math.Max(0, margin.BlockStart))
            {
                // The margins of the spanner and of its parent collapse: the spanner is at the block-start edge of its parent
                blockFormattingContext.CollapseMargins();
                var marginBoxStart = blockFormattingContext.BlockOffset - Math.Max(0, margin.BlockStart);
                blockFormattingContext.AdvanceBlockPosition(blockSize);
                blockFormattingContext.AdjoinMargin(margin.BlockEnd);
                blockFormattingContext.CollapseMargins();
                multiColumnFlow.AddSpannerRange(box, marginBoxStart, blockFormattingContext.BlockOffset);
                margins = CollapsedBlockMargins.Create(MarginStrut.FromMargin(margin.BlockStart), default, collapsedThrough: false, clearance: null);
                return fragment;
            }

            // The spanner is moved below the floats: its margins are separated from the ones of its parent
            blockFormattingContext.Separate(box);
        }

        blockFormattingContext.CollapseMargins();
        var clearance = 0d;
        if (contentEnd > blockFormattingContext.BlockOffset)
        {
            // The floats and the content that overflows the boxes before the spanner are in the row of columns before it
            clearance = contentEnd - blockFormattingContext.BlockOffset;
            blockFormattingContext.AdvanceBlockPosition(clearance);
        }

        var start = blockFormattingContext.BlockOffset;
        blockFormattingContext.AdjoinMargin(margin.BlockStart);
        blockFormattingContext.CollapseMargins();
        blockFormattingContext.AdvanceBlockPosition(blockSize);
        blockFormattingContext.AdjoinMargin(margin.BlockEnd);
        blockFormattingContext.CollapseMargins();
        multiColumnFlow.AddSpannerRange(box, start, blockFormattingContext.BlockOffset);
        margins = CollapsedBlockMargins.Create(MarginStrut.FromMargin(margin.BlockStart), default, collapsedThrough: false, clearance);
        return fragment;
    }

    /// <summary>
    /// Lays out an in-flow block-level box that establishes a new formatting context (or a replaced box) and updates the state
    /// of the block formatting context with it: its margins collapse with the adjoining margins of its siblings and parent, not
    /// with the ones of its children, and it never collapses through. Returns its fragment, its margins, and the offset of its
    /// border box from the inline-start edge of the border box of the parent.
    /// </summary>
    /// <remarks>
    /// https://drafts.csswg.org/css2/#bfc-next-to-float: "The border box of a table, a block-level replaced element, or an element
    /// in the normal flow that establishes a new block formatting context [...] must not overlap the margin box of any floats in
    /// the same block formatting context as the element itself. If necessary, implementations should clear the said element by
    /// placing it below any preceding floats, but may place it adjacent to such floats if there is sufficient space. They may
    /// even make the border box of said element narrower than defined by section 10.3.3." As in the browsers, the box is laid
    /// out in the first layout opportunity at or below its position where it fits: the space between the floats in the band of
    /// its block size, where its margins may overlap the floats (a margin narrows the space only when it is larger than the
    /// distance of the float from the edge of the containing block). A box moved down below floats has clearance: when its
    /// block-start margin was adjoining to the one of its parent, the margins are separated (see
    /// <see cref="BlockFormattingContextState.Separate"/>), as https://drafts.csswg.org/css2/#clearance describes for <c>clear</c>.
    /// </remarks>
    private static BoxFragment LayoutIndependentBox(LayoutContext context, BlockFormattingContextState blockFormattingContext, ContainerBox box, in ConstraintSpace space, in ContentArea content, bool isStartMarginCollapsedWithParent, out CollapsedBlockMargins margins, out double inlineOffset)
    {
        var writingDirection = content.WritingDirection;
        var fragment = context.LayoutBox(box, space);

        // https://drafts.csswg.org/css-box-4/#margin-trim: the layouts that do not trim the margins themselves (e.g. a table wrapper box)
        fragment.TrimMargins(writingDirection, space.TrimsBlockStartMargin && fragment.UntrimmedMargin is null, space.TrimsBlockEndMargin && fragment.UntrimmedMargin is null);
        var margin = writingDirection.ToLogical(fragment.Margin);
        var startMargin = MarginStrut.FromMargin(margin.BlockStart);
        var clearSides = ExclusionSpace.GetClearSides(box.Style.Layout.Clear, writingDirection);
        inlineOffset = content.Offset.Inline + margin.InlineStart;
        double? clearance;
        if (blockFormattingContext.IsSeparated(box))
        {
            // The margins of the box were separated from the ones of its parent: it is placed after the margins before it, below
            // the floats it clears, in the first layout opportunity where it fits
            var position = blockFormattingContext.GetSeparatedPosition(clearSides);
            if (!blockFormattingContext.Exclusions.IsEmpty)
            {
                position = PlaceNextToFloats(context, blockFormattingContext.Exclusions, box, space, content, position, ref fragment, ref inlineOffset);
            }

            clearance = position - blockFormattingContext.GetPositionWithZeroClearance(startMargin);
        }
        else
        {
            clearance = blockFormattingContext.ComputeClearance(clearSides, startMargin);
            if (!blockFormattingContext.Exclusions.IsEmpty)
            {
                var ceiling = clearance is { } amount ? blockFormattingContext.GetPositionWithZeroClearance(startMargin) + amount : blockFormattingContext.GetPositionWithoutClearance(startMargin);

                var blockOffset = PlaceNextToFloats(context, blockFormattingContext.Exclusions, box, space, content, ceiling, ref fragment, ref inlineOffset);
                if (clearance is not null || blockOffset > ceiling)
                {
                    clearance = blockOffset - blockFormattingContext.GetPositionWithZeroClearance(startMargin);
                }
            }

            if (clearance is not null && isStartMarginCollapsedWithParent)
            {
                blockFormattingContext.Separate(box);
            }
        }

        if (clearance is not null)
        {
            blockFormattingContext.CollapseMargins();
        }

        blockFormattingContext.AdjoinMargin(startMargin);
        blockFormattingContext.CollapseMargins();
        blockFormattingContext.AdvanceBlockPosition((clearance ?? 0) + writingDirection.ToLogical(fragment.Size).Block);
        blockFormattingContext.AdjoinMargin(margin.BlockEnd);
        margins = CollapsedBlockMargins.Create(startMargin, MarginStrut.FromMargin(margin.BlockEnd), collapsedThrough: false, clearance);
        return fragment;
    }

    /// <summary>
    /// Finds the position of a box that avoids the floats (see <see cref="LayoutIndependentBox"/>): the first layout opportunity
    /// at or below <paramref name="ceiling"/> (the block offset of its border box in the block formatting context) where it fits.
    /// The box is laid out again in the space of the opportunity when it is not the space of its first layout. Returns the
    /// block offset of its border box in the block formatting context.
    /// </summary>
    /// <remarks>
    /// In a strip of a fragmentation context, the box is fragmented once the strip is laid out: as in Blink, each of its fragments
    /// is in the layout opportunity beside the floats of its fragmentainer (https://drafts.csswg.org/css-break-3/#varying-size-boxes).
    /// When the fragmentation of the strip found the breaks inside the box (see <see cref="StripRelayout"/>), the box is placed in
    /// the opportunity of its first fragment, and laid out again in the opportunity of each next fragment that differs: the
    /// content of its fragment in the strip comes from these layouts (see <see cref="InlineVariant"/>).
    /// </remarks>
    private static double PlaceNextToFloats(LayoutContext context, ExclusionSpace exclusions, ContainerBox box, in ConstraintSpace space, in ContentArea content, double ceiling, ref BoxFragment fragment, ref double inlineOffset)
    {
        var writingDirection = content.WritingDirection;
        var containingBlockLineLeft = content.BlockFormattingContextOffset.Inline;
        var containingBlockInlineSize = content.InlineSize;

        // The margins of the box in the inline axis (the auto margins are 0)
        var computedMargins = ResolveMargins(box.Style.Layout.Box, space.PercentageInlineSize);
        var lineLeftMargin = computedMargins.Get(writingDirection.LineLeft);
        var lineRightMargin = computedMargins.Get(writingDirection.LineRight);

        // The breaks inside the box when it is fragmented (its fragments have the same writing mode as their containing block)
        var stripRelayout = context.StripRelayout;
        var breaks = stripRelayout?.GetFloatAvoiderBreaks(box);
        if (breaks is not null && WritingDirection.FromStyle(box.Style.Layout) != writingDirection)
        {
            breaks = null;
        }

        // The area the box is laid out in (its margin box fills it when its inline size is auto): first, the content box of the
        // containing block
        var area = new FloatAvoidingArea(containingBlockLineLeft, containingBlockInlineSize, double.NegativeInfinity, double.PositiveInfinity, IsNarrowed: false, containingBlockInlineSize);
        var blockOffset = ceiling;
        var blockSize = writingDirection.ToLogical(fragment.Size).Block;
        for (var attempt = 0; attempt < exclusions.Exclusions.Count * 4 + 8; attempt++)
        {
            // (the band of the first fragment of a box that is fragmented)
            var bandSize = GetBandSize(breaks, blockOffset, blockSize);
            var opportunity = exclusions.GetOpportunity(blockOffset, bandSize, containingBlockLineLeft, containingBlockInlineSize, useFloatAreas: false);
            var desired = GetArea(exclusions, blockOffset, bandSize, containingBlockLineLeft, containingBlockInlineSize, lineLeftMargin, lineRightMargin, writingDirection);
            if (desired.LineLeft != area.LineLeft || desired.InlineSize != area.InlineSize)
            {
                if (space.OutsideMarker is { } marker)
                {
                    marker.IsPlaced = false;
                }

                fragment = context.LayoutBox(box, space with { AvailableInlineSize = desired.InlineSize });
                var newBlockSize = writingDirection.ToLogical(fragment.Size).Block;
                if (newBlockSize > blockSize)
                {
                    // The box is taller in the narrower space: the floats of the larger band are checked again
                    area = desired;
                    blockSize = newBlockSize;
                    continue;
                }

                blockSize = newBlockSize;
            }

            area = desired;

            // The border box must not overlap the margin box of a float, nor overflow the containing block beside the floats
            // that narrow it: when it does, the box is moved down below the first of the floats that ends
            if (!Fits(fragment, area) && !double.IsPositiveInfinity(opportunity.NextBlockOffset))
            {
                blockOffset = opportunity.NextBlockOffset;
                continue;
            }

            break;
        }

        inlineOffset = content.Offset.Inline + GetInlineStart(fragment, area);
        if (stripRelayout is not null)
        {
            stripRelayout.RecordFloatAvoider(box, blockOffset);
            if (breaks is not null)
            {
                // (the outside marker is in the first fragment)
                var nextSpace = space with { OutsideMarker = null };
                LayoutNextFragments(fragment, area, nextSpace);
            }
        }

        return blockOffset;

        // The line-left edge of the border box in an area: the box starts at the inline-start edge of the area, after its margin
        double GetBorderBoxLineLeft(BoxFragment fragment, in FloatAvoidingArea area) => writingDirection.IsRightToLeft
            ? area.LineLeft + area.InlineSize - fragment.Margin.Get(writingDirection.LineRight) - writingDirection.ToLogical(fragment.Size).Inline
            : area.LineLeft + fragment.Margin.Get(writingDirection.LineLeft);

        // The border box in the content box of the containing block (from its inline-start edge)
        double GetInlineStart(BoxFragment fragment, in FloatAvoidingArea area)
        {
            var borderBoxOffset = GetBorderBoxLineLeft(fragment, area) - containingBlockLineLeft;
            return writingDirection.IsRightToLeft ? containingBlockInlineSize - borderBoxOffset - writingDirection.ToLogical(fragment.Size).Inline : borderBoxOffset;
        }

        bool Fits(BoxFragment fragment, in FloatAvoidingArea area)
        {
            const double Epsilon = 1e-6;
            var borderBoxLineLeft = GetBorderBoxLineLeft(fragment, area);
            var borderBoxLineRight = borderBoxLineLeft + writingDirection.ToLogical(fragment.Size).Inline;
            var limitLineLeft = area.IsNarrowed ? Math.Max(area.FloatLineLeft, containingBlockLineLeft) : area.FloatLineLeft;
            var limitLineRight = area.IsNarrowed ? Math.Min(area.FloatLineRight, containingBlockLineLeft + containingBlockInlineSize) : area.FloatLineRight;
            return borderBoxLineLeft >= limitLineLeft - Epsilon && borderBoxLineRight <= limitLineRight + Epsilon;
        }

        // The box is laid out again in the area of each next fragment whose area differs from the area of the previous one: its
        // fragment in the strip gets the content of these layouts (the content that starts in each fragment)
        void LayoutNextFragments(BoxFragment main, FloatAvoidingArea mainArea, ConstraintSpace nextSpace)
        {
            const double Epsilon = 0.01;
            var mainSize = main.Size;
            var mainInlineStart = GetInlineStart(main, mainArea);
            var end = blockOffset + writingDirection.ToLogical(mainSize).Block;
            List<(InlineVariant Variant, BoxFragment Fragment)>? variants = null;
            var previous = mainArea;
            for (var i = 0; i < breaks.Length; i++)
            {
                var start = breaks[i];
                if (start <= blockOffset + Epsilon || start >= end - Epsilon)
                    continue;

                var segmentEnd = i + 1 < breaks.Length ? Math.Min(end, breaks[i + 1]) : end;
                var segmentArea = GetArea(exclusions, start, segmentEnd - start, containingBlockLineLeft, containingBlockInlineSize, lineLeftMargin, lineRightMargin, writingDirection);
                if (segmentArea.LineLeft == previous.LineLeft && segmentArea.InlineSize == previous.InlineSize)
                    continue;

                BoxFragment segment;
                if (segmentArea.LineLeft == mainArea.LineLeft && segmentArea.InlineSize == mainArea.InlineSize)
                {
                    segment = main;
                }
                else
                {
                    var outerLaysOutNextFragments = context.LaysOutNextFragments;
                    context.LaysOutNextFragments = true;
                    try
                    {
                        segment = context.LayoutBox(box, nextSpace with { AvailableInlineSize = segmentArea.InlineSize });
                    }
                    finally
                    {
                        context.LaysOutNextFragments = outerLaysOutNextFragments;
                    }
                }

                // (a box that does not fit beside the floats of the fragment continues below them, see FragmentationFlow.GetFloatAvoider;
                // also when no space is left beside them)
                if (!Fits(segment, segmentArea) || (segmentArea.IsNarrowed && segmentArea.Space <= 0))
                    continue;

                previous = segmentArea;
                var inlineStart = GetInlineStart(segment, segmentArea);
                var physicalDelta = writingDirection.ToPhysical(new LogicalOffset(inlineStart, 0), PhysicalSize.Zero, segment.Size) - writingDirection.ToPhysical(new LogicalOffset(mainInlineStart, 0), PhysicalSize.Zero, mainSize);
                var variant = new InlineVariant(start - blockOffset, inlineStart - mainInlineStart, writingDirection.ToLogical(segment.Size).Inline, physicalDelta);
                (variants ??= []).Add((variant, segment));
            }

            box.LastLayoutFragment = main;
            if (variants is null)
                return;

            // The content of the fragment: the children of each layout that start in its fragments
            var children = new List<Fragment>();
            AddChildren(main, double.NegativeInfinity, variants[0].Variant.Start, default);
            for (var i = 0; i < variants.Count; i++)
            {
                var (variant, segment) = variants[i];
                AddChildren(segment, variant.Start, i + 1 < variants.Count ? variants[i + 1].Variant.Start : double.PositiveInfinity, variant.PhysicalDelta);
            }

            main.ReplaceInFlowChildren([.. children]);
            var result = new InlineVariant[variants.Count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = variants[i].Variant;
            }

            stripRelayout.SetInlineVariants(main, result);

            void AddChildren(BoxFragment source, double from, double to, PhysicalOffset delta)
            {
                var sourceChildren = source.ChildrenSpan[..source.InFlowChildCount];
                foreach (var child in sourceChildren)
                {
                    var rect = child.Rect;
                    if (child is BoxFragment childBox)
                    {
                        rect = rect.Translate(-(childBox.RelativeOffset + childBox.StickyOffset));
                    }

                    var childStart = writingDirection.ToLogical(rect, source.Size).Offset.Block;
                    if (childStart < from - Epsilon || childStart >= to - Epsilon)
                        continue;

                    if (!ReferenceEquals(source, main))
                    {
                        child.Offset += delta;
                    }

                    children.Add(child);
                }
            }
        }
    }

    /// <summary>Returns the block size of the band of a box at a block offset that is in its first fragment (before the first break inside it).</summary>
    private static double GetBandSize(double[]? breaks, double blockOffset, double blockSize)
    {
        if (breaks is not null)
        {
            foreach (var position in breaks)
            {
                if (position > blockOffset + 0.01 && position < blockOffset + blockSize)
                    return position - blockOffset;
            }
        }

        return blockSize;
    }

    /// <summary>
    /// Returns the area where a box that avoids the floats is laid out in a band of the block formatting context (see
    /// <see cref="PlaceNextToFloats"/>): the content box of the containing block, narrowed by the floats of the band.
    /// </summary>
    private static FloatAvoidingArea GetArea(ExclusionSpace exclusions, double blockOffset, double bandSize, double containingBlockLineLeft, double containingBlockInlineSize, double lineLeftMargin, double lineRightMargin, WritingDirection writingDirection)
    {
        // The floats that do not narrow the space (their margin box is empty or outside of the containing block) do not
        // change the layout of the box: its negative margins make it wider than the containing block (as in Blink, WPT
        // css/CSS2/floats/zero-width-floats.html)
        var (floatLineLeft, floatLineRight) = exclusions.GetFloatEdges(blockOffset, bandSize);
        var containingBlockLineRight = containingBlockLineLeft + containingBlockInlineSize;
        if (floatLineLeft <= containingBlockLineLeft && floatLineRight >= containingBlockLineRight)
            return new FloatAvoidingArea(containingBlockLineLeft, containingBlockInlineSize, floatLineLeft, floatLineRight, IsNarrowed: false, containingBlockInlineSize);

        // "Margins should only affect the available inline-size if they are larger than the opportunity's edges" (Blink).
        // The border box starts at the inline-start edge of the space its margin leaves: a margin larger than the space
        // beside a float leaves none
        var lineLeft = Math.Max(Math.Max(floatLineLeft, containingBlockLineLeft), containingBlockLineLeft + Math.Max(0, lineLeftMargin));
        var lineRight = Math.Min(Math.Min(floatLineRight, containingBlockLineRight), containingBlockLineRight - Math.Max(0, lineRightMargin));

        // The available inline size is not negative: the negative margins make the border box of a box whose inline size
        // is automatic at least as wide as they are (it may then not fit beside the float)
        var inlineSize = Math.Max(0, Math.Max(0, lineRight - lineLeft) + lineLeftMargin + lineRightMargin);
        var areaLineLeft = writingDirection.IsRightToLeft ? lineRight + lineRightMargin - inlineSize : lineLeft - lineLeftMargin;
        return new FloatAvoidingArea(areaLineLeft, inlineSize, floatLineLeft, floatLineRight, IsNarrowed: true, lineRight - lineLeft);
    }

    /// <summary>
    /// The area where a box that avoids the floats is laid out (its margin box fills it when its inline size is auto), in the
    /// line-relative coordinates of the block formatting context, the edges of the floats beside it, and the inline size of the
    /// space between them (negative when the floats overlap).
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FloatAvoidingArea(double LineLeft, double InlineSize, double FloatLineLeft, double FloatLineRight, bool IsNarrowed, double Space);

    /// <summary>
    /// Returns the block-start margins of the first in-flow children of a block container (and of their first children...) that
    /// collapse with its own block-start margin (https://drafts.csswg.org/css2/#collapsing-margins), before they are laid out: the
    /// margins of the children until one of them is separated from the box by a border, a padding, a line box, clearance, or
    /// establishes a new formatting context; the children whose margins collapse through are skipped (their margins included).
    /// </summary>
    /// <remarks>
    /// The margins are part of the position of the box in its block formatting context (where the floats of its first children
    /// are placed, and whether it has clearance), as in Servo. The margins of a child with <c>clear</c> are included: as in the
    /// browsers, its hypothetical position is the one where the floats before it are placed with the collapsed margins; when it
    /// gets clearance, its margins are separated from the ones of the box, and the outermost box whose margins collapsed with
    /// them lays out its contents again without them (see <see cref="BlockFormattingContextState.Separate"/>): the search ends
    /// at a separated child. The prediction of the children that collapse through may also be wrong (e.g. the stretch-fit
    /// sizes): the contents are then laid out again too (see <see cref="FormattingContext.Layout"/>).
    /// </remarks>
    /// <param name="box">The block container.</param>
    /// <param name="writingDirection">The writing mode of the box.</param>
    /// <param name="contentInlineSize">The inline size of the content box of the box (the basis of the percentages of the margins of the children).</param>
    /// <param name="blockFormattingContext">The block formatting context the box participates in.</param>
    /// <param name="trimsStartMargins">Whether the box trims the margins adjacent to its block-start edge (see <see cref="ContentArea.TrimsBlockStartMargins"/>): they are not collapsed.</param>
    /// <param name="trimsEndMargins">Whether the box trims the margins adjacent to its block-end edge: all the margins of its children when they all collapse through.</param>
    internal static MarginStrut FindMarginsCollapsingWithChildren(ContainerBox box, WritingDirection writingDirection, double contentInlineSize, BlockFormattingContextState blockFormattingContext, bool trimsStartMargins, bool trimsEndMargins)
    {
        var margins = default(MarginStrut);
        CollectChildrenStartMargins(box, writingDirection, contentInlineSize, blockFormattingContext, trimsStartMargins, trimsEndMargins, ref margins);
        return margins;
    }

    /// <summary>Adds the block-start margins of the first children of a box to <paramref name="margins"/>; returns whether all its children collapse through.</summary>
    private static bool CollectChildrenStartMargins(ContainerBox box, WritingDirection writingDirection, double contentInlineSize, BlockFormattingContextState blockFormattingContext, bool trimsStartMargins, bool trimsEndMargins, ref MarginStrut margins)
    {
        var marginsBeforeChildren = margins;
        if (box.SkipsContents || GetKind(box) is not (FormattingContextKind.Block or FormattingContextKind.Inline))
            return false;

        if (box.ChildrenAreInline)
            return !HasLineContent(box);

        foreach (var child in box.Children)
        {
            if (child is not ContainerBox childBox || childBox.IsOutsideMarker || childBox.IsOutOfFlow)
                continue;

            if (blockFormattingContext.IsSeparated(childBox))
                return false;

            var layout = childBox.Style.Layout;
            var boxModel = layout.Box;
            var margin = writingDirection.ToLogical(ResolveMargins(boxModel, contentInlineSize));
            if (!trimsStartMargins)
            {
                margins = margins.Append(margin.BlockStart);
            }

            if (EstablishesNewFormattingContext(childBox, writingDirection))
                return false;

            var paddingAndBorder = writingDirection.ToLogical(ResolvePadding(boxModel, contentInlineSize).Add(boxModel.BorderWidth));
            if (paddingAndBorder.BlockStart != 0)
                return false;

            // The content inline size of the child (the basis of the percentages of the margins of its children)
            var horizontal = writingDirection.IsHorizontal;
            var childInlineSize = ResolveBorderBoxSize(layout, boxModel.GetSize(horizontal), contentInlineSize, paddingAndBorder.InlineSum()) is { } specified
                ? specified - paddingAndBorder.InlineSum()
                : contentInlineSize - margin.InlineSum() - paddingAndBorder.InlineSum();
            // https://drafts.csswg.org/css-box-4/#margin-trim: the margins of the children that collapse with a trimmed margin are trimmed
            var childMarginTrim = GetMarginTrim(childBox);
            if (!CollectChildrenStartMargins(childBox, writingDirection, Math.Max(0, childInlineSize), blockFormattingContext, trimsStartMargins || (childMarginTrim & MarginTrimSides.BlockStart) != MarginTrimSides.None, (childMarginTrim & MarginTrimSides.BlockEnd) != MarginTrimSides.None, ref margins))
                return false;

            // "top and bottom margins of a box that does not establish a new block formatting context and that has zero computed
            // 'min-height', zero or 'auto' computed 'height', and no in-flow children"
            // (a percentage of the block size of the containing block is treated as auto); the stretch-fit size and the size
            // given by an aspect ratio are not zero
            var blockIsHorizontal = !horizontal;
            var blockSize = boxModel.GetSize(blockIsHorizontal);
            var minBlockSize = boxModel.GetMinSize(blockIsHorizontal);
            if (paddingAndBorder.BlockEnd != 0 || blockSize.TryResolve(null) is > 0 || minBlockSize.TryResolveMinimum(null) is > 0
                || blockSize.Kind == SizeKind.Stretch || minBlockSize.Kind == SizeKind.Stretch || blockSize.IsAuto && boxModel.AspectRatio.HasRatio && childInlineSize > 0)
            {
                return false;
            }

            if (!trimsStartMargins)
            {
                margins = margins.Append(margin.BlockEnd);
            }
        }

        // All the children collapse through: their margins are adjacent to the block-end edge too
        if (trimsEndMargins)
        {
            margins = marginsBeforeChildren;
        }

        return true;
    }

    /// <summary>
    /// Returns whether a block container whose children are inline may have a line box that is not empty (https://drafts.csswg.org/css2/#inline-formatting):
    /// a text that is not collapsible white space, a forced line break, an atomic inline, or an inline box with inline-axis
    /// margins, borders or paddings.
    /// </summary>
    private static bool HasLineContent(ContainerBox container)
    {
        foreach (var child in container.Children)
        {
            switch (child)
            {
                case TextRun run:
                    if (run.Kind is TextRunKind.LineBreak || run.Kind is TextRunKind.Text or TextRunKind.Generated && run.Length > 0 && !run.IsCollapsibleWhiteSpace)
                        return true;

                    break;

                case ContainerBox { IsOutOfFlow: true } or ContainerBox { IsOutsideMarker: true }:
                    break;

                case ContainerBox { IsInlineBox: true } inlineBox:
                    if (inlineBox.Style.Layout.MayHaveBoxEdges || HasLineContent(inlineBox))
                        return true;

                    break;

                default:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The placement of the in-flow children of a block container in the block axis, with the collapsing of their margins
    /// (https://drafts.csswg.org/css2/#collapsing-margins), relative to the block-start edge of its content box.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private struct MarginCollapsing
    {
        /// <summary>Whether the block-start margin of the next child collapses with the block-start margin of the parent: no child separated them yet.</summary>
        private bool _nextMarginCollapsesWithParentStartMargin;

        /// <summary>
        /// Whether the block-end margin of the last child may collapse with the block-end margin of the parent: "the child's
        /// bottom margin does not collapse with a top margin that has clearance".
        /// </summary>
        private bool _lastMarginCollapsesWithParentEndMargin;

        /// <summary>The margins of the first children that collapse with the block-start margin of the parent.</summary>
        private MarginStrut _startMargin;

        /// <summary>The adjoining margins at the current position, not collapsed yet.</summary>
        private MarginStrut _currentMargin;

        /// <summary>The current position, before the pending margins.</summary>
        private double _position;

        /// <summary>Whether a child that does not collapse through (or has clearance) was placed.</summary>
        private bool _hasContent;

        public MarginCollapsing(bool startMarginCollapsesWithParent)
        {
            _nextMarginCollapsesWithParentStartMargin = startMarginCollapsesWithParent;
            _lastMarginCollapsesWithParentEndMargin = true;
        }

        /// <summary>
        /// Whether the margins of the next child are adjacent to the block-start edge of the content box: the children placed so
        /// far collapse through (https://drafts.csswg.org/css-box-4/#margin-trim).
        /// </summary>
        public readonly bool IsBeforeFirstContent => !_hasContent;

        /// <summary>
        /// Whether a margin adjacent to the block-end edge of the content box is not zero: the block-end margin of the last child
        /// (collapsed with the ones of its last children) and the margins of the children that collapse through after it, or
        /// the margins of all the children when they all collapse through (https://drafts.csswg.org/css-box-4/#margin-trim).
        /// </summary>
        public readonly bool HasEndMargins => !_currentMargin.IsZero || _nextMarginCollapsesWithParentStartMargin && !_startMargin.IsZero;

        /// <summary>Whether the block-start margin of the next child collapses with the block-start margin of the parent.</summary>
        public readonly bool NextMarginCollapsesWithParentStartMargin => _nextMarginCollapsesWithParentStartMargin;

        /// <summary>The static position of an out-of-flow box in the block axis: the current position, after the pending margins.</summary>
        public readonly double StaticPosition => _position + _currentMargin.Sum;

        /// <summary>Places an in-flow child whose border box has the given block size and returns the block offset of its border box.</summary>
        public double Place(CollapsedBlockMargins margins, double blockSize)
        {
            if (margins.Clearance is { } clearance)
            {
                // https://drafts.csswg.org/css2/#clearance: the margins before the box do not collapse with its margins, and "if the
                // top and bottom margins of an element with clearance are adjoining, its margins collapse with the adjoining margins
                // of following siblings but that resulting margin does not collapse with the bottom margin of the parent block"
                blockSize += clearance;
                _position += _currentMargin.Sum;
                _currentMargin = default;
                _nextMarginCollapsesWithParentStartMargin = false;
                _hasContent = true;
                if (margins.CollapsedThrough)
                {
                    _lastMarginCollapsesWithParentEndMargin = false;
                }
            }
            else if (!margins.CollapsedThrough)
            {
                _lastMarginCollapsesWithParentEndMargin = true;
            }

            _hasContent |= !margins.CollapsedThrough;

            if (_nextMarginCollapsesWithParentStartMargin)
            {
                // "top margin of a box and top margin of its first in-flow child": the margin is outside of the parent, the child is
                // at the block-start edge of the content box
                _startMargin = _startMargin.Append(margins.Start);
                if (margins.CollapsedThrough)
                {
                    _startMargin = _startMargin.Append(margins.End);
                    return 0;
                }

                _nextMarginCollapsesWithParentStartMargin = false;
            }
            else
            {
                // "bottom margin of box and top margin of its next in-flow following sibling"
                _currentMargin = _currentMargin.Append(margins.Start);
            }

            // "If the top and bottom margins of a box are adjoining [...] the position of the element's top border edge is the
            // same as it would have been if the element had a non-zero bottom border": after its block-start margin
            var offset = _position + _currentMargin.Sum + (margins.Clearance ?? 0);
            if (margins.CollapsedThrough)
            {
                // "top and bottom margins of a box that does not establish a new block formatting context and that has zero
                // computed 'min-height', zero or 'auto' computed 'height', and no in-flow children": its margins collapse with
                // the next ones
                _position += blockSize;
                _currentMargin = _currentMargin.Append(margins.End);
            }
            else
            {
                _position += _currentMargin.Sum + blockSize;
                _currentMargin = margins.End;
            }

            return offset;
        }

        /// <summary>Places a child without collapsing its margins with the adjoining ones (the fallback layout of the formatting contexts that are not implemented yet).</summary>
        public double PlaceWithoutCollapsing(CollapsedBlockMargins margins, double blockSize)
        {
            _position += _currentMargin.Sum;
            _currentMargin = default;
            var offset = _position + margins.Start.Sum;
            _position = offset + blockSize + margins.End.Sum;
            _nextMarginCollapsesWithParentStartMargin = false;
            _hasContent = true;
            return offset;
        }

        /// <summary>
        /// Whether the adjoining margins at the end of the contents are separated from the block-end margin of the parent: the
        /// margins of the last children collapsed through a box that has clearance (see <see cref="Finish"/>).
        /// </summary>
        public readonly bool SeparatesEndMargin => !_lastMarginCollapsesWithParentEndMargin;

        /// <summary>Returns the block size of the contents, and the margins of the children that collapse with the margins of the parent.</summary>
        public (double BlockSize, MarginStrut StartMargin, MarginStrut EndMargin, bool CollapsedThrough) Finish()
        {
            if (!_lastMarginCollapsesWithParentEndMargin)
            {
                _position += _currentMargin.Sum;
                _currentMargin = default;
            }

            return (_position, _startMargin, _currentMargin, _nextMarginCollapsesWithParentStartMargin);
        }
    }
}
