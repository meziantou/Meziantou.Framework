using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A box alignment value resolved in one axis of an alignment container (https://drafts.csswg.org/css-align-3/#alignment-values):
/// the positional keywords become the edge of the container they align to, relative to the side the positions are measured from
/// (<see cref="AlignmentAxis.Start"/>).
/// </summary>
internal enum AxisAlignment : byte
{
    /// <summary>Flush with the edge the positions are measured from (<see cref="AlignmentAxis.Start"/>).</summary>
    Start,

    /// <summary>Flush with the opposite edge.</summary>
    End,
    Center,

    /// <summary><c>stretch</c> (or a <c>normal</c> that behaves as <c>stretch</c>): the subject fills the container when it can, else it is start-aligned.</summary>
    Stretch,

    /// <summary>First-baseline alignment (https://drafts.csswg.org/css-align-3/#baseline-values).</summary>
    Baseline,

    /// <summary>Last-baseline alignment.</summary>
    LastBaseline,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

/// <summary>
/// An axis of an alignment container (https://drafts.csswg.org/css-align-3/#alignment-container), as the physical sides the
/// alignment keywords refer to: <c>start</c>/<c>end</c> refer to the writing mode of the container, <c>flex-start</c>/<c>flex-end</c>
/// to the main-start/cross-start side of a flex container (the start side otherwise), <c>self-start</c>/<c>self-end</c> to the
/// writing mode of the alignment subject, <c>left</c>/<c>right</c> to the physical sides of a horizontal axis or the line-left and
/// line-right sides of a vertical inline axis.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AlignmentAxis
{
    /// <summary>The side the positions are measured from: <see cref="AxisAlignment.Start"/> is flush with it.</summary>
    public PhysicalSide Start { get; init; }

    /// <summary>The start side of the axis in the writing mode of the alignment container (the <c>start</c> keyword).</summary>
    public PhysicalSide WritingModeStart { get; init; }

    /// <summary>The side of the <c>flex-start</c> keyword: the main-start or cross-start side of a flex container, else <see cref="WritingModeStart"/>.</summary>
    public PhysicalSide FlexStart { get; init; }

    /// <summary>Whether the axis is the inline axis of the alignment container.</summary>
    public bool IsInlineAxis { get; init; }

    /// <summary>The line-left side of the alignment container (the <c>left</c> keyword in a vertical inline axis).</summary>
    public PhysicalSide LineLeft { get; init; }

    /// <summary>Whether the axis is horizontal.</summary>
    public bool IsHorizontal => Start is PhysicalSide.Left or PhysicalSide.Right;

    /// <summary>Returns the inline axis of an alignment container whose writing mode and direction are <paramref name="container"/>.</summary>
    public static AlignmentAxis Inline(WritingDirection container) => new()
    {
        Start = container.InlineStart,
        WritingModeStart = container.InlineStart,
        FlexStart = container.InlineStart,
        IsInlineAxis = true,
        LineLeft = container.LineLeft,
    };

    /// <summary>Returns the block axis of an alignment container whose writing mode and direction are <paramref name="container"/>.</summary>
    public static AlignmentAxis Block(WritingDirection container) => new()
    {
        Start = container.BlockStart,
        WritingModeStart = container.BlockStart,
        FlexStart = container.BlockStart,
        LineLeft = container.LineLeft,
    };

    /// <summary>Returns the alignment flush with a side of the axis.</summary>
    public AxisAlignment ToAlignment(PhysicalSide side) => side == Start ? AxisAlignment.Start : AxisAlignment.End;

    /// <summary>
    /// Returns the side of the alignment container a positional keyword aligns to (https://drafts.csswg.org/css-align-3/#positional-values),
    /// or null for the other keywords. <paramref name="subject"/> is the writing mode of the alignment subject (<c>self-start</c>/<c>self-end</c>).
    /// </summary>
    public PhysicalSide? GetSide(AlignmentKeyword keyword, WritingDirection subject)
    {
        switch (keyword)
        {
            case AlignmentKeyword.Start:
                return WritingModeStart;
            case AlignmentKeyword.End:
                return WritingDirection.Opposite(WritingModeStart);
            case AlignmentKeyword.FlexStart:
                return FlexStart;
            case AlignmentKeyword.FlexEnd:
                return WritingDirection.Opposite(FlexStart);
            case AlignmentKeyword.SelfStart or AlignmentKeyword.SelfEnd:
            {
                // "the edge of the alignment container corresponding to the alignment subject's start side in the appropriate axis"
                var start = subject.IsHorizontal == IsHorizontal ? subject.InlineStart : subject.BlockStart;
                return keyword == AlignmentKeyword.SelfStart ? start : WritingDirection.Opposite(start);
            }

            case AlignmentKeyword.Left or AlignmentKeyword.Right:
            {
                // "flush with the alignment container's line-left or physical left edge, whichever is in the appropriate axis. If the
                // property's axis is not parallel with either left↔right axis, this value behaves as start"
                PhysicalSide left;
                if (IsHorizontal)
                {
                    left = PhysicalSide.Left;
                }
                else if (IsInlineAxis)
                {
                    left = LineLeft;
                }
                else
                {
                    return WritingModeStart;
                }

                return keyword == AlignmentKeyword.Left ? left : WritingDirection.Opposite(left);
            }

            default:
                return null;
        }
    }
}

/// <summary>
/// The box alignment shared by the layout modes (https://drafts.csswg.org/css-align-3/): the resolution of the alignment values in
/// an axis of an alignment container, the self-alignment of an alignment subject with its overflow alignment, and the content
/// distribution. The layout modes decide what <c>normal</c> means, which boxes take part in a baseline alignment, and the default
/// overflow alignment (https://drafts.csswg.org/css-align-3/#auto-safety).
/// </summary>
internal static class BoxAlignment
{
    /// <summary>
    /// Resolves a self-alignment value (<c>justify-self</c>, <c>align-self</c>) in an axis (https://drafts.csswg.org/css-align-3/#self-alignment):
    /// <c>normal</c> (and an unresolved <c>auto</c>) is <paramref name="normal"/>, <c>anchor-center</c> is <c>center</c> (without a
    /// default anchor box, https://drafts.csswg.org/css-anchor-position-1/#anchor-center).
    /// </summary>
    public static AxisAlignment ResolveSelfAlignment(AlignmentKeyword keyword, in AlignmentAxis axis, WritingDirection subject, AxisAlignment normal) => keyword switch
    {
        AlignmentKeyword.Normal or AlignmentKeyword.Auto or AlignmentKeyword.Legacy => normal,
        AlignmentKeyword.Stretch => AxisAlignment.Stretch,
        AlignmentKeyword.Center or AlignmentKeyword.AnchorCenter => AxisAlignment.Center,
        AlignmentKeyword.Baseline => AxisAlignment.Baseline,
        AlignmentKeyword.LastBaseline => AxisAlignment.LastBaseline,
        AlignmentKeyword.SpaceBetween or AlignmentKeyword.SpaceAround or AlignmentKeyword.SpaceEvenly => AxisAlignment.Start,
        _ => axis.ToAlignment(axis.GetSide(keyword, subject) ?? axis.WritingModeStart),
    };

    /// <summary>
    /// Resolves a content-distribution value (<c>justify-content</c>, <c>align-content</c>) in an axis (https://drafts.csswg.org/css-align-3/#content-distribution):
    /// <c>normal</c> is <paramref name="normal"/>; the baseline values take their fallback alignment (<c>start</c>, or <c>end</c>
    /// for <c>last baseline</c>, https://drafts.csswg.org/css-align-3/#baseline-values) as the layout modes do not implement the
    /// baseline content-alignment.
    /// </summary>
    public static AxisAlignment ResolveContentAlignment(AlignmentKeyword keyword, in AlignmentAxis axis, AxisAlignment normal) => keyword switch
    {
        AlignmentKeyword.Normal or AlignmentKeyword.Auto or AlignmentKeyword.Legacy => normal,
        AlignmentKeyword.Stretch => AxisAlignment.Stretch,
        AlignmentKeyword.Center or AlignmentKeyword.AnchorCenter => AxisAlignment.Center,
        AlignmentKeyword.SpaceBetween => AxisAlignment.SpaceBetween,
        AlignmentKeyword.SpaceAround => AxisAlignment.SpaceAround,
        AlignmentKeyword.SpaceEvenly => AxisAlignment.SpaceEvenly,
        AlignmentKeyword.Baseline => axis.ToAlignment(axis.WritingModeStart),
        AlignmentKeyword.LastBaseline => axis.ToAlignment(WritingDirection.Opposite(axis.WritingModeStart)),
        _ => axis.ToAlignment(axis.GetSide(keyword, WritingDirection.HorizontalLtr) ?? axis.WritingModeStart),
    };

    /// <summary>
    /// Returns the fallback alignment of a baseline alignment (https://drafts.csswg.org/css-align-3/#baseline-values): "the fallback
    /// alignment for first baseline is safe self-start (for self-alignment) or safe start (for content-distribution)"; as in the
    /// browsers, the start (end for <c>last baseline</c>) of the writing mode of the alignment container is used.
    /// </summary>
    public static AxisAlignment GetBaselineFallback(AxisAlignment alignment, in AlignmentAxis axis) => alignment switch
    {
        AxisAlignment.Baseline => axis.ToAlignment(axis.WritingModeStart),
        AxisAlignment.LastBaseline => axis.ToAlignment(WritingDirection.Opposite(axis.WritingModeStart)),
        _ => alignment,
    };

    /// <summary>
    /// Returns the offset of an alignment subject from the start of its alignment container (<see cref="AlignmentAxis.Start"/>)
    /// for a positional alignment, given the free space (the size of the container minus the size of the subject). With
    /// <paramref name="safe"/>, "if the size of the alignment subject overflows the alignment container, the alignment subject is
    /// instead aligned as if the alignment mode were flex-start" (https://drafts.csswg.org/css-align-3/#overflow-values).
    /// <c>stretch</c> and the baseline alignments (resolved by the layout modes) are start-aligned.
    /// </summary>
    public static double GetOffset(AxisAlignment alignment, double freeSpace, bool safe)
    {
        if (safe && freeSpace < 0)
            return 0;

        return alignment switch
        {
            AxisAlignment.End => freeSpace,
            AxisAlignment.Center or AxisAlignment.SpaceAround or AxisAlignment.SpaceEvenly => freeSpace / 2,
            _ => 0,
        };
    }

    /// <summary>
    /// Returns the offset of the first alignment subject and the extra space between the subjects of a content distribution
    /// (https://drafts.csswg.org/css-align-3/#distribution-values) of <paramref name="freeSpace"/> between <paramref name="count"/>
    /// subjects. "When space cannot be distributed in this way, these values behave as their fallback alignment": <c>safe
    /// flex-start</c> for <c>space-between</c>, <c>safe center</c> for <c>space-around</c> and <c>space-evenly</c>, <c>flex-start</c>
    /// for <c>stretch</c> (the layout modes stretch their subjects themselves).
    /// </summary>
    public static (double Offset, double Between) Distribute(AxisAlignment alignment, double freeSpace, int count, bool safe)
    {
        if (count <= 0)
            return (GetOffset(alignment, freeSpace, safe), 0);

        switch (alignment)
        {
            case AxisAlignment.SpaceBetween:
                return freeSpace <= 0 || count < 2 ? (0, 0) : (0, freeSpace / (count - 1));
            case AxisAlignment.SpaceAround:
                return freeSpace <= 0 ? (0, 0) : (freeSpace / count / 2, freeSpace / count);
            case AxisAlignment.SpaceEvenly:
                return freeSpace <= 0 ? (0, 0) : (freeSpace / (count + 1), freeSpace / (count + 1));
            case AxisAlignment.Stretch:
                return (0, 0);
            default:
                return (GetOffset(alignment, freeSpace, safe), 0);
        }
    }

    /// <summary>
    /// Returns the used <c>justify-self</c> of a box whose parent box has the style <paramref name="parent"/>
    /// (https://drafts.csswg.org/css-align-3/#valdef-justify-self-auto): <c>auto</c> "behaves as the computed justify-items value
    /// of the parent box (minus any legacy keywords)", or as <c>normal</c> without parent.
    /// </summary>
    public static Alignment GetJustifySelf(LayoutStyle box, LayoutStyle? parent)
    {
        var value = box.Alignment.JustifySelf;
        if (value.Keyword != AlignmentKeyword.Auto)
            return value;

        if (parent is null)
            return Alignment.Normal;

        var items = parent.Alignment.JustifyItems;
        if (items.Keyword is AlignmentKeyword.Legacy or AlignmentKeyword.Auto)
            return Alignment.Normal;

        return items with { IsLegacy = false };
    }

    /// <summary>
    /// Returns the used <c>align-self</c> of a box whose parent box has the style <paramref name="parent"/>
    /// (https://drafts.csswg.org/css-align-3/#valdef-align-self-auto): <c>auto</c> "computes to itself, but always behaves like
    /// the computed value of align-items on the box's parent box", or as <c>normal</c> without parent.
    /// </summary>
    public static Alignment GetAlignSelf(LayoutStyle box, LayoutStyle? parent)
    {
        var value = box.Alignment.AlignSelf;
        if (value.Keyword != AlignmentKeyword.Auto)
            return value;

        if (parent is null)
            return Alignment.Normal;

        var items = parent.Alignment.AlignItems;
        return items.Keyword == AlignmentKeyword.Auto ? Alignment.Normal : items;
    }
}
