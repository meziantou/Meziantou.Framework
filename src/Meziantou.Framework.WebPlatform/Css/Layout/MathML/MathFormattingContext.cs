using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A stretch size constraint of an embellished operator (https://w3c.github.io/mathml-core/#dfn-inline-stretch-size-constraint):
/// the target inline size of a core operator stretched along the inline axis, or the target ink line-ascent and line-descent of
/// a core operator stretched along the block axis.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MathStretchConstraint(MathStretchAxis Axis, double InlineSize, double Ascent, double Descent)
{
    public static MathStretchConstraint Inline(double inlineSize) => new(MathStretchAxis.Inline, inlineSize, 0, 0);

    public static MathStretchConstraint Block(double ascent, double descent) => new(MathStretchAxis.Block, 0, ascent, descent);
}

/// <summary>A graphical item drawn by a MathML box: a rule (a fraction bar, the overbar of a radical) or a shaped glyph (a stretched operator, the radical symbol).</summary>
/// <param name="Rect">The rectangle of a rule, or the box of a glyph (its inline size and its line-ascent + line-descent), in the coordinates of the border box of the fragment.</param>
/// <param name="Glyph">The shaped glyph, or null for a rule.</param>
/// <param name="Baseline">The alphabetic baseline of the glyph, from the top of <paramref name="Rect"/>.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MathPaintItem(PhysicalRect Rect, MathShapedGlyph? Glyph, double Baseline);

/// <summary>The metrics of a MathML box laid out by the math layout (see <see cref="BoxFragment.MathData"/>), for its border box.</summary>
internal sealed class MathFragmentData
{
    /// <summary>The ink line-ascent: the distance from the alphabetic baseline (<see cref="BoxFragment.FirstBaseline"/>) to the ink-over baseline.</summary>
    public double InkAscent { get; init; }

    /// <summary>The ink line-descent: the distance from the ink-under baseline to the alphabetic baseline.</summary>
    public double InkDescent { get; init; }

    /// <summary>The italic correction (https://w3c.github.io/mathml-core/#dfn-italic-correction), or 0.</summary>
    public double ItalicCorrection { get; init; }

    /// <summary>The top accent attachment (https://w3c.github.io/mathml-core/#dfn-top-accent-attachment) from the inline-start border edge, or null when it is not specified.</summary>
    public double? TopAccentAttachment { get; init; }

    /// <summary>The graphical items to paint after the text and the backgrounds of the box (https://w3c.github.io/mathml-core/#stacking-contexts), or empty.</summary>
    public IReadOnlyList<MathPaintItem> PaintItems { get; init; } = [];
}

/// <summary>
/// The layout of the MathML boxes (https://w3c.github.io/mathml-core/#layout-algorithms): the boxes whose display is
/// <c>block math</c> or <c>inline math</c> and the anonymous mrow boxes. Each box computes a "math content box" from its in-flow
/// children (its inline size, its line-ascent and line-descent from the alphabetic baseline, its ink extent, its italic
/// correction and top accent attachment, the positions of the children and the graphical items), which is placed in its
/// content box (start-aligned, or centered for the fractions and the under/over scripts). The algorithm depends on the element
/// (<see cref="MathBoxKind"/>).
/// </summary>
/// <remarks>
/// <para>
/// The automatic inline size of a MathML box is the inline size of its math content (a block-level <c>math</c> root that is not
/// in a math layout fills its containing block and centers its content). The content is laid out when the shared sizing asks
/// for the automatic inline size (<see cref="GetAutomaticInlineSize"/>), or by <see cref="LayoutContents"/> when the inline size is
/// specified; the state of the layout of a box is in <see cref="LayoutBox.LayoutState"/> (<see cref="MathLayoutState"/>).
/// </para>
/// <para>
/// The stretch size constraints of the embellished operators are given by <see cref="ConstraintSpace.MathStretch"/>. The children
/// are laid out with an auto inline size that is their fit-content size (<see cref="ConstraintSpace.AutoInlineSizeIsFitContent"/>);
/// the block-level children that are not MathML boxes (an <c>mtable</c>, the blockified inline boxes) are laid out by CSS and
/// aligned on their first baseline (the center of an <c>mtable</c> is aligned with the math axis).
/// </para>
/// </remarks>
internal sealed partial class MathFormattingContext : FormattingContext
{
    public static MathFormattingContext Instance { get; } = new();

    private MathFormattingContext()
    {
    }

    public override FormattingContextKind Kind => FormattingContextKind.Math;

    /// <summary>The intrinsic sizing modes of the algorithms: the children are measured by their contributions instead of being laid out.</summary>
    private enum MathMode : byte
    {
        Layout,
        MinContent,
        MaxContent,
    }

    /// <summary>The state of the layout of a MathML box (<see cref="LayoutBox.LayoutState"/>), valid during one call of <see cref="Layout"/>.</summary>
    private sealed class MathLayoutState
    {
        public MathStretchConstraint? Stretch;
        public double? AvailableInlineSize;
        public MathContent? Content;
        public double ContentInlineStart;
        public double BaselineOffset;
    }

    /// <summary>A child measured by an algorithm: the metrics of its margin box.</summary>
    private struct MathChild
    {
        public ContainerBox Box;
        public BoxFragment? Fragment;
        public double InlineSize;
        public double Ascent;
        public double Descent;
        public double InkAscent;
        public double InkDescent;
        public double ItalicCorrection;
        public double? TopAccentAttachment;
        public double MarginInlineStart;
        public double MarginBlockStart;

        /// <summary>The core operator when the child is an embellished operator, else null.</summary>
        public ContainerBox? Core;
        public MathOperatorProperties Operator;

        public readonly bool IsEmbellishedOperator => Core is not null;

        /// <summary>"A child box is slanted if it is not an embellished operator and has nonzero italic correction".</summary>
        public readonly bool IsSlanted => Core is null && ItalicCorrection != 0;
    }

    /// <summary>The placement of a child in the math content: the inline offset of its margin box, the offset of its baseline from the alphabetic baseline (positive towards the line-under).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct MathPlacement(int Child, double InlineOffset, double BaselineOffset);

    /// <summary>A graphical item of the math content: a rule (Glyph null) or a glyph, its inline offset and its block offset from the baseline (of the top of the rule, or of the baseline of the glyph).</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct MathItem(double InlineOffset, double BlockOffset, double InlineSize, double BlockSize, MathShapedGlyph? Glyph, bool SpansContentBox = false);

    /// <summary>The math content box of a box (https://w3c.github.io/mathml-core/#dfn-math-content-box).</summary>
    private sealed class MathContent
    {
        public double InlineSize;
        public double Ascent;
        public double Descent;
        public double InkAscent;
        public double InkDescent;
        public double ItalicCorrection;
        public double? TopAccentAttachment;

        /// <summary>Whether the math content box is centered in the content box (mfrac, munder, mover, munderover, block math root).</summary>
        public bool Centered;

        public MathChild[] Children = [];
        public List<MathPlacement> Placements = [];
        public List<MathItem>? Items;

        /// <summary>For a token element: its contents are laid out by the inline or block layout in <see cref="LayoutContents"/>.</summary>
        public bool IsTokenContent;

        public void SetInkFromLine()
        {
            InkAscent = Ascent;
            InkDescent = Descent;
        }

        public void AddItem(MathItem item) => (Items ??= []).Add(item);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // FormattingContext overrides
    // ----------------------------------------------------------------------------------------------------------------

    public override BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        var state = box.LayoutState as MathLayoutState ?? new MathLayoutState();
        box.LayoutState = state;
        state.Stretch = space.MathStretch;
        state.Content = null;

        // The available inline size of the children (the wrapping of the token elements): the one of the box, not its specified
        // inline size (the math content overflows a narrower content box); infinite when the box is measured for its contribution
        state.AvailableInlineSize = space.IsInlineSizeIndefinite ? double.PositiveInfinity : space.AvailableInlineSize;

        var fragment = base.Layout(context, box, space);
        if (state.Content is { } content)
        {
            fragment.MathData = CreateFragmentData(fragment, content, state);
        }

        return fragment;
    }

    /// <summary>
    /// The automatic inline size of a MathML box is the inline size of its math content (https://w3c.github.io/mathml-core/#layout-algorithms:
    /// "the box metrics [...] of the content box are set to the one calculated for the math content box"); a block-level math
    /// root that is not laid out by a math layout fills the available space (its content is centered in it).
    /// </summary>
    protected override double GetAutomaticInlineSize(LayoutContext context, ContainerBox box, double availableSize, double paddingAndBorder, in IntrinsicSizingInput input, bool isFitContent)
    {
        var contentBlockSize = input.ContentBlockSize;
        if (!isFitContent && IsBlockLevelMathRoot(box))
            return base.GetAutomaticInlineSize(context, box, availableSize, paddingAndBorder, input, isFitContent);

        // https://drafts.csswg.org/css-contain-2/#containment-size: the inline size of a box with size containment does not depend
        // on its contents (its max-content size is contain-intrinsic-size)
        if (IntrinsicSizing.HasSizeContainment(box, inlineAxis: true))
            return IntrinsicSizing.GetIntrinsicInlineSizes(context, box, contentBlockSize).MaxContent + paddingAndBorder;

        var state = (MathLayoutState)box.LayoutState!;
        if (!double.IsPositiveInfinity(state.AvailableInlineSize ?? 0))
        {
            state.AvailableInlineSize = Math.Max(0, availableSize - paddingAndBorder);
        }

        var content = EnsureContent(context, box, state);
        return Math.Max(0, content.InlineSize) + paddingAndBorder;
    }

    /// <summary>Whether a box is a block-level MathML box that is not laid out by a math layout (a <c>math</c> root with <c>display: block math</c>).</summary>
    private static bool IsBlockLevelMathRoot(ContainerBox box) => box.IsBlockLevel && MathLayoutBoxes.GetKind(box.Parent) == MathBoxKind.None;

    protected override ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        var state = box.LayoutState as MathLayoutState;
        if (state is null)
        {
            state = new MathLayoutState();
            box.LayoutState = state;
        }

        if (IsBlockLevelMathRoot(box) && state.Content is null)
        {
            state.AvailableInlineSize = content.InlineSize;
        }

        state.AvailableInlineSize ??= content.InlineSize;
        var math = EnsureContent(context, box, state);
        var isBlockLevelRoot = IsBlockLevelMathRoot(box);
        if (isBlockLevelRoot)
        {
            math.Centered = true;
        }

        // The contents of a token element (its line-ascent is known once they are laid out), the descent of an mspace
        if (math.IsTokenContent)
        {
            LayoutTokenContents(context, box, builder, content, math);
        }
        else if (MathLayoutBoxes.GetKind(box) == MathBoxKind.Space)
        {
            CompleteSpace(math, content);
        }

        // https://w3c.github.io/mathml-core/#layout-algorithms: "the inline-start and block-start edges of the math content box are
        // aligned with the ones of the content box", or their middles for the centered contents (a safe centering for a block-level
        // math root, as in the browsers: the contents wider than the box are start-aligned, so that they can be reached by scrolling)
        var centering = math.Centered ? (content.InlineSize - math.InlineSize) / 2 : 0;
        var inlineStart = content.Offset.Inline + (isBlockLevelRoot ? Math.Max(0, centering) : centering);
        var baseline = content.Offset.Block + math.Ascent;
        state.ContentInlineStart = inlineStart;
        state.BaselineOffset = baseline;

        foreach (var placement in math.Placements)
        {
            ref var child = ref math.Children[placement.Child];
            if (child.Fragment is null)
                continue;

            var offset = new LogicalOffset(inlineStart + placement.InlineOffset + child.MarginInlineStart, baseline + placement.BaselineOffset - child.Ascent + child.MarginBlockStart);
            builder.AddChild(child.Fragment, offset);
        }

        // The absolutely positioned children: their static position is the start of the content box
        foreach (var child in box.Children)
        {
            if (child is ContainerBox { IsOutOfFlow: true } outOfFlow)
            {
                builder.AddOutOfFlowCandidate(outOfFlow, content.Offset);
            }
        }

        var blockSize = Math.Max(0, math.Ascent + math.Descent);
        return new ContentLayoutResult(blockSize, math.Ascent, math.Ascent);
    }

    protected internal override IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input)
    {
        switch (MathLayoutBoxes.GetKind(box))
        {
            case MathBoxKind.Token:
                return ComputeTokenIntrinsicSizes(context, box, input);
            case MathBoxKind.Operator:
                return ComputeOperatorIntrinsicSizes(context, box, input);
            case MathBoxKind.Space or MathBoxKind.None:
                return default;
        }

        var min = ComputeContent(context, box, stretch: null, MathMode.MinContent).InlineSize;
        var max = ComputeContent(context, box, stretch: null, MathMode.MaxContent).InlineSize;
        return new IntrinsicSizes(Math.Max(0, min), Math.Max(0, Math.Max(min, max)));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Math content
    // ----------------------------------------------------------------------------------------------------------------

    private MathContent EnsureContent(LayoutContext context, ContainerBox box, MathLayoutState state)
        => state.Content ??= ComputeContent(context, box, state.Stretch, MathMode.Layout);

    /// <summary>Computes the math content of a box with the algorithm of its element.</summary>
    private MathContent ComputeContent(LayoutContext context, ContainerBox box, MathStretchConstraint? stretch, MathMode mode)
    {
        var kind = MathLayoutBoxes.GetKind(box);
        return kind switch
        {
            MathBoxKind.Token => LayoutToken(context, box),
            MathBoxKind.Operator => LayoutOperator(context, box, stretch),
            MathBoxKind.Space => LayoutSpace(box),
            MathBoxKind.Fraction => LayoutFraction(context, box, stretch, mode),
            MathBoxKind.SquareRoot => LayoutSquareRoot(context, box, mode),
            MathBoxKind.Root => LayoutRoot(context, box, mode),
            MathBoxKind.Padded => LayoutPadded(context, box, stretch, mode),
            MathBoxKind.Sub or MathBoxKind.Sup or MathBoxKind.SubSup or MathBoxKind.Multiscripts => LayoutScripts(context, box, kind, stretch, mode),
            MathBoxKind.Under or MathBoxKind.Over or MathBoxKind.UnderOver => LayoutUnderOver(context, box, kind, stretch, mode),
            _ => LayoutRow(context, box, stretch, mode),
        };
    }

    /// <summary>Returns the in-flow children of a box.</summary>
    private static List<ContainerBox> GetInFlowChildren(ContainerBox box)
    {
        var children = new List<ContainerBox>();
        foreach (var child in box.Children)
        {
            if (MathLayoutBoxes.IsInFlowChild(child))
            {
                children.Add((ContainerBox)child);
            }
        }

        return children;
    }

    /// <summary>Returns the core operator and the properties of a child when it is an embellished operator.</summary>
    private static (ContainerBox? Core, MathOperatorProperties Properties) GetOperator(ContainerBox child)
    {
        if (MathLayoutBoxes.GetCoreOperator(child) is not { } core)
            return (null, default);

        // The form of the operator is the one of its outermost embellished operator
        var embellished = core;
        while (embellished.Parent is { } parent && ReferenceEquals(MathLayoutBoxes.GetCoreOperator(parent), core))
        {
            embellished = parent;
        }

        return (core, MathLayoutBoxes.GetOperatorProperties(embellished, core));
    }

    /// <summary>
    /// Measures a child: lays it out (with a stretch size constraint) in the layout mode, or uses its min-content or max-content
    /// contribution in the intrinsic modes. Returns the metrics of its margin box.
    /// </summary>
    private static MathChild Measure(LayoutContext context, ContainerBox parent, ContainerBox child, MathStretchConstraint? stretch, MathMode mode)
    {
        var (core, properties) = GetOperator(child);
        var writingDirection = WritingDirection.FromStyle(parent.Style.Layout);
        if (mode != MathMode.Layout)
        {
            var contribution = IntrinsicSizing.GetContribution(context, child, writingDirection, containingBlockBlockSize: null);
            return new MathChild
            {
                Box = child,
                InlineSize = mode == MathMode.MinContent ? contribution.MinContent : contribution.MaxContent,
                ItalicCorrection = GetIntrinsicItalicCorrection(child),
                Core = core,
                Operator = properties,
            };
        }

        var state = parent.LayoutState as MathLayoutState;
        var available = state?.AvailableInlineSize ?? context.InitialContainingBlockSize.Width;
        var space = new ConstraintSpace
        {
            WritingDirection = writingDirection,
            AvailableInlineSize = available,
            PercentageInlineSize = double.IsPositiveInfinity(available) ? 0 : available,
            IsInlineSizeIndefinite = double.IsPositiveInfinity(available),
            AutoInlineSizeIsFitContent = true,
            MathStretch = stretch,
        };

        var fragment = context.LayoutBox(child, space);
        var margin = writingDirection.ToLogical(fragment.Margin);
        var size = writingDirection.ToLogical(fragment.Size);

        // The alphabetic baseline of a box that is not a MathML box is its first baseline (the block-end edge without baseline);
        // the center of a table is aligned with the math axis (https://w3c.github.io/mathml-core/#table-or-matrix-mtable)
        double baseline;
        if (child.Kind == ContainerBoxKind.TableWrapper && child.Element is { NamespaceUri: Namespaces.MathML, LocalName: "mtable" })
        {
            baseline = (size.Block / 2) + MathFontParameters.Get(child.Style).AxisHeight;
        }
        else
        {
            baseline = fragment.FirstBaseline ?? size.Block;
        }

        var result = new MathChild
        {
            Box = child,
            Fragment = fragment,
            InlineSize = size.Inline + margin.InlineSum(),
            Ascent = baseline + margin.BlockStart,
            Descent = size.Block - baseline + margin.BlockEnd,
            MarginInlineStart = margin.InlineStart,
            MarginBlockStart = margin.BlockStart,
            Core = core,
            Operator = properties,
        };

        // https://w3c.github.io/mathml-core/#layout-algorithms: the italic correction and the top accent attachment of the margin box
        // include the margins. The ink extent includes the margins too (the specification keeps the baselines of the border box,
        // the browsers extend the ink extent with the margins, WPT mathml/relations/css-styling/padding-border-margin/margin-003.html)
        if (fragment.MathData is { } data)
        {
            result.InkAscent = data.InkAscent + margin.BlockStart;
            result.InkDescent = data.InkDescent + margin.BlockEnd;
            result.ItalicCorrection = data.ItalicCorrection != 0 ? data.ItalicCorrection + margin.InlineEnd : 0;
            result.TopAccentAttachment = data.TopAccentAttachment + margin.InlineStart;
        }
        else
        {
            result.InkAscent = result.Ascent;
            result.InkDescent = result.Descent;
        }

        return result;
    }

    /// <summary>Returns the italic correction of a child used by the intrinsic sizes: the one of a token element made of a single glyph.</summary>
    private static double GetIntrinsicItalicCorrection(ContainerBox child)
    {
        var kind = MathLayoutBoxes.GetKind(child);
        if (kind is not (MathBoxKind.Token or MathBoxKind.Operator))
            return 0;

        return GetTokenGlyph(child) is var (font, glyph, emSize) && font.Font.MathTable?.GetItalicsCorrection(glyph) is { } italic ? italic * emSize / font.Font.UnitsPerEm : 0;
    }

    /// <summary>Sets the ascent, descent and ink extent of a content from the placements of its children.</summary>
    private static void ComputeBlockMetrics(MathContent content, bool includeInk = true)
    {
        double ascent = 0;
        double descent = 0;
        double inkAscent = 0;
        double inkDescent = 0;
        var first = true;
        foreach (var placement in content.Placements)
        {
            ref var child = ref content.Children[placement.Child];
            var childAscent = child.Ascent - placement.BaselineOffset;
            var childDescent = child.Descent + placement.BaselineOffset;
            var childInkAscent = child.InkAscent - placement.BaselineOffset;
            var childInkDescent = child.InkDescent + placement.BaselineOffset;
            if (first)
            {
                (ascent, descent, inkAscent, inkDescent) = (childAscent, childDescent, childInkAscent, childInkDescent);
                first = false;
            }
            else
            {
                ascent = Math.Max(ascent, childAscent);
                descent = Math.Max(descent, childDescent);
                inkAscent = Math.Max(inkAscent, childInkAscent);
                inkDescent = Math.Max(inkDescent, childInkDescent);
            }
        }

        content.Ascent = ascent;
        content.Descent = descent;
        if (includeInk)
        {
            content.InkAscent = inkAscent;
            content.InkDescent = inkDescent;
        }
    }

    /// <summary>Creates the data of the fragment of a box: the ink extent, italic correction and top accent attachment of its border box, and its graphical items.</summary>
    private static MathFragmentData CreateFragmentData(BoxFragment fragment, MathContent content, MathLayoutState state)
    {
        var writingDirection = fragment.WritingDirection;
        var border = writingDirection.ToLogical(fragment.Border);
        var padding = writingDirection.ToLogical(fragment.Padding);
        var size = writingDirection.ToLogical(fragment.Size);
        var baseline = fragment.FirstBaseline ?? state.BaselineOffset;

        // https://w3c.github.io/mathml-core/#layout-algorithms: "if the line-over border is positive then the ink-over baseline is
        // set to the line-over edge of the border box and if the line-under border is positive then the ink-under baseline is set
        // to the line-under edge of the border box"
        var inkAscent = border.BlockStart > 0 ? baseline : content.InkAscent;
        var inkDescent = border.BlockEnd > 0 ? size.Block - baseline : content.InkDescent;

        List<MathPaintItem>? items = null;
        if (content.Items is { } contentItems)
        {
            items = new List<MathPaintItem>(contentItems.Count);
            var contentStart = border.InlineStart + padding.InlineStart;
            var contentInlineSize = size.Inline - border.InlineSum() - padding.InlineSum();
            foreach (var item in contentItems)
            {
                var inlineOffset = item.SpansContentBox ? contentStart : state.ContentInlineStart + item.InlineOffset;
                var inlineSize = item.SpansContentBox ? contentInlineSize : item.InlineSize;
                var top = item.Glyph is { } glyph ? state.BaselineOffset + item.BlockOffset - glyph.Ascent : state.BaselineOffset + item.BlockOffset;
                var blockSize = item.Glyph is { } shaped ? shaped.Ascent + shaped.Descent : item.BlockSize;
                var rect = writingDirection.ToPhysical(new LogicalRect(new LogicalOffset(inlineOffset, top), new LogicalSize(inlineSize, blockSize)), fragment.Size);
                items.Add(new MathPaintItem(rect, item.Glyph, item.Glyph is { } g ? g.Ascent : 0));
            }
        }

        return new MathFragmentData
        {
            InkAscent = inkAscent,
            InkDescent = inkDescent,
            // "If the content box has an italic correction then the padding box has the same property, increased by the inline-end padding"
            ItalicCorrection = content.ItalicCorrection != 0 ? content.ItalicCorrection + padding.InlineEnd + border.InlineEnd : 0,
            TopAccentAttachment = content.TopAccentAttachment + state.ContentInlineStart,
            PaintItems = items is null ? [] : items,
        };
    }
}
