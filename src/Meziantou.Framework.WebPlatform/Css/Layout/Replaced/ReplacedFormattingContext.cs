using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of a replaced box (https://drafts.csswg.org/css2/#replaced-width, https://drafts.csswg.org/css2/#inline-replaced-height):
/// its content is outside of the CSS formatting model, its <c>auto</c> sizes come from the natural dimensions of its contents
/// (see <see cref="ReplacedContent"/>) and the default sizing algorithm (https://drafts.csswg.org/css-images-3/#default-sizing).
/// </summary>
/// <remarks>
/// <para>
/// The shared sizing of <see cref="FormattingContext.Layout"/> applies the preferred aspect ratio
/// (https://drafts.csswg.org/css-sizing-4/#aspect-ratio, the natural aspect ratio for <c>aspect-ratio: auto</c>): an
/// <c>auto</c> size is transferred from the size of the other axis, and the min/max sizes are transferred through the ratio,
/// which gives the results of the constraint table of CSS 2.1 §10.4 (https://drafts.csswg.org/css2/#min-max-widths) when
/// both sizes are <c>auto</c>. This class gives the <c>auto</c> sizes that do not depend on the ratio: the natural width or
/// height (completed by the ratio when only one of them is known), the available inline size for a box that only has a
/// ratio, else the default object size (300x150).
/// </para>
/// <para>
/// The fragment of the box gets the rectangle of its painted contents (<see cref="BoxFragment.ReplacedContentRect"/>, from
/// <c>object-fit</c> and <c>object-position</c>), and the baseline of the text of a form control. The children of the box
/// (the <c>foreignObject</c> boxes of an <c>svg</c> element) are laid out by the SVG layout (see <see cref="SvgForeignObjectLayout"/>);
    /// the inner editor of a text control is laid out as a block in its content box (see <see cref="TextControlLayout"/>).
/// </para>
/// </remarks>
internal sealed class ReplacedFormattingContext : FormattingContext
{
    public static ReplacedFormattingContext Instance { get; } = new();

    private ReplacedFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Replaced;

    public override BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        var state = CreateState(context, box);
        var content = state.Content;
        var layout = box.Style.Layout;
        box.LayoutState = state;

        // The percentage block sizes of a replaced element in a table cell whose height is a length resolve against that height
        var fragment = space.ReplacedPercentageBlockSize is { } percentageBlockSize && content.Kind == ReplacedContentKind.Object
            ? base.Layout(context, box, space with { PercentageBlockSize = percentageBlockSize })
            : base.Layout(context, box, space);
        if (box.FirstChild is not null && box.Element is SvgSvgElement)
        {
            SvgForeignObjectLayout.Layout(context, box, fragment);
        }
        else if (box.Element is HtmlVideoElement video && box.GeneratedImage is null)
        {
            // The cues of the text tracks are rendered over the video
            WebVttCueLayout.Layout(context, video, box, fragment);
        }

        if (content.Kind == ReplacedContentKind.Object)
        {
            fragment.ReplacedContentRect = GetObjectRect(layout, content, fragment.ContentBoxRect);
        }
        else if (content.Baseline != FormControlBaseline.None)
        {
            // The baseline of the line of the inner editor of a text control, else the one of a line of its primary font
            fragment.FirstBaseline = fragment.LastBaseline = (box.FirstChild is null ? null : TextControlLayout.AlignContents(fragment, content.Baseline))
                ?? GetFormControlBaseline(box, content.Baseline, content.FormControlLines, fragment);

            // As in Gecko and WebKit, a textarea (a scroll container of several lines) has no last baseline: with
            // baseline-source: last, it is aligned by the block-end edge of its margin box, as an inline-block that scrolls is
            // (WPT css/css-inline/baseline-source/baseline-source-last-textarea-001.tentative.html)
            if (TextControlLayout.IsMultiline(box))
            {
                fragment.LastBaseline = null;
            }
        }

        return fragment;
    }

    /// <summary>Returns the contents of a replaced box and their natural sizes in its writing mode.</summary>
    private static ReplacedLayoutState CreateState(LayoutContext context, ContainerBox box)
    {
        var content = ReplacedContent.Get(context, box);
        var layout = box.Style.Layout;
        var horizontal = layout.IsHorizontalWritingMode;

        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: "If a replaced element's only natural dimension is a natural
        // width or a natural height, giving it a preferred aspect ratio also gives it a natural height or width, whichever was
        // missing, by transferring the existing size through the preferred aspect ratio."
        var natural = content.NaturalSize;
        var (width, height) = (natural.Width, natural.Height);
        var ratio = GetPreferredContentAspectRatio(layout, natural);
        if ((width is null) != (height is null) && ratio is { } preferredRatio)
        {
            width ??= height * preferredRatio;
            height ??= width / preferredRatio;
        }

        var hasPreferredRatio = natural.AspectRatio is not null || layout.Box.AspectRatio.HasRatio;
        double? inlineOverBlock = ratio is { } contentRatio ? (horizontal ? contentRatio : 1 / contentRatio) : null;
        var (naturalInline, naturalBlock) = horizontal ? (width, height) : (height, width);

        // A natural block size of zero does not give the aspect ratio (an SVG image whose height is 0 takes the one of its
        // viewBox): as in the browsers, which transfer the natural inline size through the aspect ratio whenever they have
        // both, the natural block size is then the transferred one (WPT css/css-grid/alignment/grid-item-aspect-ratio-stretch-4.html)
        if (naturalBlock == 0 && naturalInline is > 0 && inlineOverBlock is { } transferRatio)
        {
            naturalBlock = naturalInline / transferRatio;
        }

        return new ReplacedLayoutState(content, naturalInline, naturalBlock, hasPreferredRatio) { ContentInlineOverBlock = inlineOverBlock };
    }

    /// <summary>
    /// The contents of a replaced box are outside of the CSS formatting model, except the inner editor or the placeholder of a
    /// text control (see <see cref="TextControlLayout"/>) and the contents box of the other form controls whose parts are boxes
    /// (see <see cref="FormControlContents"/>), laid out in its content box: its size does not depend on them.
    /// </summary>
    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        if (box.FirstChild is null || box.HasUnlaidSkippedContents || FormControlContents.GetContents(box) is not { } contents)
            return new(0);

        if (!contents.IsFormControlContents)
            return TextControlLayout.LayoutContents(context, contents, builder, content);

        // The block size of the content box, when it is not definite yet, is the natural one, clamped by the minimum and maximum
        // block sizes (as the block size of the box: e.g. a slider with a percentage max-height)
        if (content.BlockSize is null)
        {
            var blockSize = Math.Max(Math.Min(GetAutomaticBlockSize(context, box, 0, content.InlineSize), content.MaxBlockSize ?? double.PositiveInfinity), content.MinBlockSize ?? 0);
            return FormControlContents.LayoutContents(context, box, contents, builder, content with { BlockSize = blockSize });
        }

        return FormControlContents.LayoutContents(context, box, contents, builder, content);
    }

    /// <summary>
    /// The min-content and max-content inline sizes of a replaced element (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes):
    /// its natural inline size, or the definite block size of its content box transferred through its preferred aspect ratio
    /// (https://drafts.csswg.org/css-sizing-4/#aspect-ratio); a box that only has an aspect ratio has intrinsic sizes of zero
    /// (the stretch fit into the available space when it is definite: see <see cref="AxisSizing.GetIntrinsicSizes"/>; the
    /// specification uses the size of the initial containing block when it is not, the browsers zero: WPT
    /// css/css-sizing/svg-intrinsic-size-009.html); else the default object size (300x150).
    /// </summary>
    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        // The state is kept for HasOnlyAspectRatio, which the sizing of a box measured before its layout reads (a flex item)
        var state = CreateState(context, box);
        box.LayoutState = state;
        if (input.ContentBlockSize is { } blockSize && state.ContentInlineOverBlock is { } ratio)
            return IntrinsicSizes.FromSize(blockSize * ratio);

        var horizontal = box.Style.Layout.IsHorizontalWritingMode;
        if (state.NaturalInlineSize is { } naturalInline)
        {
            // https://drafts.csswg.org/css-sizing-3/#min-content-zero: as in the browsers, the min-content size of a compressible
            // form control whose inline size is a percentage is zero (min-width: min-content does not keep its default size:
            // WPT css/css-sizing/range-percent-intrinsic-size-1.html)
            return IntrinsicSizing.IsCompressibleFormControl(box) && IntrinsicSizing.HasPercentageSize(box, horizontal) ? new IntrinsicSizes(0, naturalInline) : IntrinsicSizes.FromSize(naturalInline);
        }

        if (state.NaturalBlockSize is null && state.HasPreferredAspectRatio)
            return default;

        var defaultSize = state.Content.DefaultObjectSize;
        return IntrinsicSizes.FromSize(horizontal ? defaultSize.Width : defaultSize.Height);
    }

    protected override double? GetNaturalAspectRatio(LayoutContext context, ContainerBox box) => GetState(box).Content.NaturalSize.AspectRatio;

    /// <summary>
    /// The <c>auto</c> inline size when it is not transferred from the block size through the aspect ratio
    /// (https://drafts.csswg.org/css2/#inline-replaced-width): the natural inline size; for a box that only has an aspect ratio
    /// (natural, or from the <c>aspect-ratio</c> property: WPT video-intrinsic-width-height.html),
    /// "the used value of 'width' is calculated from the constraint equation used for block-level, non-replaced elements in
    /// normal flow" (the available space, as in the browsers); else the default object size.
    /// </summary>
    protected override double GetAutomaticInlineSize(LayoutContext context, ContainerBox box, double availableSize, double paddingAndBorder, in IntrinsicSizingInput input, bool isFitContent)
    {
        var state = GetState(box);
        if (state.NaturalInlineSize is { } naturalInline)
            return naturalInline - GetDisplayedScrollbarSpace(context, box, state).Inline + paddingAndBorder;

        if (state.NaturalBlockSize is null && state.HasPreferredAspectRatio)
            return Math.Max(availableSize, paddingAndBorder);

        var defaultSize = state.Content.DefaultObjectSize;
        return (box.Style.Layout.IsHorizontalWritingMode ? defaultSize.Width : defaultSize.Height) + paddingAndBorder;
    }

    /// <summary>
    /// The <c>auto</c> block size when the box has no aspect ratio (https://drafts.csswg.org/css2/#inline-replaced-height): the
    /// natural block size, else the default object size.
    /// </summary>
    protected override double GetAutomaticBlockSize(LayoutContext context, ContainerBox box, double contentsBlockSize, double contentInlineSize)
    {
        var state = GetState(box);

        // The lines of a textarea with field-sizing: content wrap in its inline size (see TextControlLayout.LayoutContents)
        if (contentsBlockSize > 0 && TextControlLayout.SizesToContents(box))
            return Math.Max(contentsBlockSize, state.NaturalBlockSize ?? 0);

        if (state.NaturalBlockSize is { } naturalBlock)
            return naturalBlock - GetDisplayedScrollbarSpace(context, box, state).Block;

        var defaultSize = state.Content.DefaultObjectSize;
        return box.Style.Layout.IsHorizontalWritingMode ? defaultSize.Height : defaultSize.Width;
    }

    /// <summary>
    /// Sets the scrollbar gutters of the layout of a replaced box that is a scroll container (a <c>textarea</c>): its natural
    /// size includes the space of its scrollbars (https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2,
    /// see <see cref="FormControlSizing.GetTextAreaScrollbarSpace"/>), so the gutters of the scrollbars that are displayed are
    /// not added to it (unlike the ones of the other scroll containers, which are added to the size of their contents).
    /// </summary>
    internal static void SetScrollbarGutters(ContainerBox box, LogicalSides<double> gutters)
    {
        if (box.LayoutState is ReplacedLayoutState state)
        {
            state.InlineScrollbarGutters = gutters.InlineSum();
            state.BlockScrollbarGutters = gutters.BlockSum();
        }
    }

    /// <summary>Returns the part of the natural size of a textarea or of a list box that is the space of its scrollbars and that the scrollbar gutters of its layout take.</summary>
    private static (double Inline, double Block) GetDisplayedScrollbarSpace(LayoutContext context, ContainerBox box, ReplacedLayoutState state)
    {
        if ((state.InlineScrollbarGutters == 0 && state.BlockScrollbarGutters == 0) || !FormControlContents.IsScrollContainer(box))
            return default;

        // (the natural size of a list box has the space of the scrollbar of its block axis)
        var thickness = ScrollContainers.GetScrollbarThickness(context, box.Style.Layout);
        var (inline, block) = TextControlLayout.IsMultiline(box) ? FormControlSizing.GetTextAreaScrollbarSpace(box.Style, thickness) : (thickness, 0);
        return (Math.Min(inline, state.InlineScrollbarGutters), Math.Min(block, state.BlockScrollbarGutters));
    }

    protected override double? GetNaturalBlockSize(ContainerBox box, double contentInlineSize)
    {
        // An aspect-ratio: <ratio> replaces the natural aspect ratio: the block size is transferred through it (WPT
        // css/css-sizing/aspect-ratio/replaced-element-044.html)
        var property = box.Style.Layout.Box.AspectRatio;
        if (property.HasRatio && !property.IsAuto)
            return null;

        var state = GetState(box);
        return state.NaturalInlineSize is { } naturalInline && naturalInline == contentInlineSize ? state.NaturalBlockSize : null;
    }

    private static ReplacedLayoutState GetState(ContainerBox box) => (ReplacedLayoutState)box.LayoutState!;

    /// <summary>
    /// Returns whether a replaced box being laid out has a preferred aspect ratio but no natural size: its max-content inline size
    /// is the stretch fit into the available space when it is definite (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes).
    /// </summary>
    internal static bool HasOnlyAspectRatio(ContainerBox box)
        => box.Kind == ContainerBoxKind.Replaced && box.LayoutState is ReplacedLayoutState { NaturalInlineSize: null, NaturalBlockSize: null, HasPreferredAspectRatio: true };

    /// <summary>Returns the preferred aspect ratio (width / height) of the content box, or null.</summary>
    private static double? GetPreferredContentAspectRatio(LayoutStyle layout, ImageNaturalSize natural)
    {
        var property = layout.Box.AspectRatio;
        if ((property.IsAuto || !property.HasRatio) && natural.AspectRatio is { } naturalRatio)
            return naturalRatio;

        if (property.Ratio is not { } ratio)
            return null;

        // A <ratio> applies to the box specified by box-sizing: a ratio of the border box gives no natural size to the
        // contents (the transfer through the ratio of the layout applies it to the border box)
        return property.IsAuto || layout.BoxSizing == BoxSizing.ContentBox ? ratio : null;
    }

    /// <summary>
    /// Returns the rectangle of the replaced content in the content box (https://drafts.csswg.org/css-images-3/#the-object-fit):
    /// its concrete object size from <c>object-fit</c>, positioned by <c>object-position</c> (the percentages refer to the space
    /// left in the content box, https://drafts.csswg.org/css-images-3/#the-object-position). The natural dimensions are the
    /// ones of the contents, also when the box has size containment (as in the browsers). With an <c>object-view-box</c>, the
    /// view box is fitted and positioned, and the rectangle of the whole contents is returned (scaled as the view box).
    /// </summary>
    internal static PhysicalRect GetObjectRect(LayoutStyle layout, ReplacedContent content, PhysicalRect contentBox)
    {
        var rect = GetFittedRect(layout, content.ObjectNaturalSize, contentBox);
        if (content.ViewBox is not { } viewBox || rect.IsEmpty)
            return rect;

        var scaleX = rect.Width / viewBox.Width;
        var scaleY = rect.Height / viewBox.Height;
        return new PhysicalRect(rect.X - (viewBox.X * scaleX), rect.Y - (viewBox.Y * scaleY), content.ViewBoxContentSize.Width * scaleX, content.ViewBoxContentSize.Height * scaleY);
    }

    private static PhysicalRect GetFittedRect(LayoutStyle layout, ImageNaturalSize natural, PhysicalRect contentBox)
    {
        var replaced = layout.Replaced;
        var size = replaced.ObjectFit switch
        {
            ObjectFit.Contain => Constrain(natural, contentBox.Size, cover: false),
            ObjectFit.Cover => Constrain(natural, contentBox.Size, cover: true),
            ObjectFit.None => GetNaturalConcreteSize(natural, contentBox.Size),
            ObjectFit.ScaleDown => ScaleDown(natural, contentBox.Size),
            _ => contentBox.Size,
        };

        var x = contentBox.X + replaced.ObjectPositionX.Resolve(contentBox.Width - size.Width);
        var y = contentBox.Y + replaced.ObjectPositionY.Resolve(contentBox.Height - size.Height);
        return new PhysicalRect(new PhysicalOffset(x, y), size);

        // "contain: [...] its concrete object size is resolved as a contain constraint against the element's used width and
        // height"; "cover: [...] as a cover constraint". Without natural aspect ratio, the object fills the box.
        static PhysicalSize Constrain(ImageNaturalSize natural, PhysicalSize box, bool cover)
        {
            if (natural.AspectRatio is not { } ratio || box.Height <= 0)
                return box;

            var widthLimited = ratio > box.Width / box.Height;
            return widthLimited != cover ? new PhysicalSize(box.Width, box.Width / ratio) : new PhysicalSize(box.Height * ratio, box.Height);
        }

        // "none: [...] its concrete object size is determined using the default sizing algorithm with no specified size, and a
        // default object size equal to the replaced element's used width and height"
        static PhysicalSize GetNaturalConcreteSize(ImageNaturalSize natural, PhysicalSize box)
        {
            var (width, height) = natural.GetConcreteSize(box.Width, box.Height);
            return new PhysicalSize(width, height);
        }

        // "scale-down: Size the content as if none or contain were specified, whichever would result in a smaller concrete object size"
        static PhysicalSize ScaleDown(ImageNaturalSize natural, PhysicalSize box)
        {
            var none = GetNaturalConcreteSize(natural, box);
            var contain = Constrain(natural, box, cover: false);
            return none.Width * none.Height <= contain.Width * contain.Height ? none : contain;
        }
    }

    /// <summary>
    /// Returns the baseline of a form control from the block-start edge of its border box: the baseline of a line of text (with
    /// the line height of the control) centered in the content box (the last of the <paramref name="lines"/> lines centered in
    /// it), or at its block-start edge.
    /// </summary>
    private static double GetFormControlBaseline(ContainerBox box, FormControlBaseline kind, int lines, BoxFragment fragment)
    {
        var style = box.Style;
        var layout = style.Layout;
        var writingDirection = fragment.WritingDirection;
        var paddingAndBorder = writingDirection.ToLogical(fragment.Padding.Add(fragment.Border));
        var contentBlockSize = writingDirection.ToLogical(fragment.Size).Block - paddingAndBorder.BlockSum();
        var metrics = style.PrimaryFontMetrics;
        var zoom = layout.EffectiveZoom;
        var ascent = metrics.Ascent * zoom;
        var descent = metrics.Descent * zoom;
        var lineHeight = layout.UsedLineHeight;

        // https://drafts.csswg.org/css-inline-3/#inline-height: the half-leading is added above the ascent (floored, as the
        // half-leadings of the line boxes: see InlineLayoutAlgorithm.GetHalfLeadings)
        var baselineInLine = Math.Floor((lineHeight - (ascent + descent)) / 2) + ascent;
        var lineStart = kind == FormControlBaseline.CenteredLine ? ((contentBlockSize - (lines * lineHeight)) / 2) + ((lines - 1) * lineHeight) : 0;
        return paddingAndBorder.BlockStart + lineStart + baselineInLine;
    }

    /// <summary>The state of a replaced box during its layout (<see cref="LayoutBox.LayoutState"/>): its contents and their natural sizes in its writing mode.</summary>
    private sealed record ReplacedLayoutState(ReplacedContent Content, double? NaturalInlineSize, double? NaturalBlockSize, bool HasPreferredAspectRatio)
    {
        /// <summary>The preferred aspect ratio of the content box (inline size / block size), or null.</summary>
        public double? ContentInlineOverBlock { get; init; }

        /// <summary>The scrollbar gutters of the inline axis of the current layout of the box (see <see cref="SetScrollbarGutters"/>).</summary>
        public double InlineScrollbarGutters { get; set; }

        /// <summary>The scrollbar gutters of the block axis of the current layout of the box.</summary>
        public double BlockScrollbarGutters { get; set; }
    }
}
