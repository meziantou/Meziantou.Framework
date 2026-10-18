using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The last remembered sizes of the elements (https://drafts.csswg.org/css-sizing-4/#last-remembered): an element whose
/// <c>contain-intrinsic-size</c> has the <c>auto</c> keyword remembers the size of the content box of its principal box, which
/// is its explicit intrinsic inner size when it skips its contents later (e.g. <c>content-visibility: auto</c> off-screen).
/// </summary>
/// <remarks>
/// <para>
/// The sizes are recorded by the rendering steps, "at the time that ResizeObserver events are determined and delivered"
/// (<see cref="LayoutEngine.UpdateRendering"/>), when the element does not skip its contents (as in the browsers: an element
/// with size containment that does not skip its contents records its contained size, WPT
/// css-sizing/contain-intrinsic-size/auto-012.html). They are logical (the inline and block sizes of the writing mode of the
/// box), stored by the element (<see cref="ElementRareData"/>): they survive the box changes (<c>display: none</c>, another
/// display type).
/// </para>
/// <para>
/// A size is forgotten when the element has a box whose <c>contain-intrinsic-size</c> has no <c>auto</c> keyword in that axis
/// (checked when the box tree is built, so immediately), or when the element is disconnected at a rendering update.
/// </para>
/// </remarks>
internal static class LastRememberedSizes
{
    /// <summary>
    /// Returns the explicit intrinsic inner size of a box with size containment in a physical axis
    /// (https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override): the last remembered size when <c>contain-intrinsic-size</c>
    /// has the <c>auto</c> keyword in this axis and the element skips its contents, else the length, or 0 for <c>none</c>.
    /// </summary>
    public static double GetContainIntrinsicSize(ContainerBox box, bool horizontal)
    {
        if (TryGetRememberedSize(box, horizontal, out var remembered))
            return remembered;

        var layout = box.Style.Layout;
        var value = horizontal ? layout.Box.ContainIntrinsicWidth : layout.Box.ContainIntrinsicHeight;
        return value.Length ?? 0;
    }

    /// <summary>Whether a box has an explicit intrinsic inner size in a physical axis: its contain-intrinsic-size is not <c>none</c>, or it has a last remembered size.</summary>
    public static bool HasExplicitContainIntrinsicSize(ContainerBox box, bool horizontal)
    {
        var layout = box.Style.Layout;
        return (horizontal ? layout.Box.ContainIntrinsicWidth : layout.Box.ContainIntrinsicHeight).Length is not null || TryGetRememberedSize(box, horizontal, out _);
    }

    /// <summary>
    /// Returns the last remembered size of a box in a physical axis when it is its explicit intrinsic inner size
    /// (https://drafts.csswg.org/css-sizing-4/#valdef-contain-intrinsic-width-auto-length: <c>auto</c> and the box skips its contents).
    /// </summary>
    public static bool TryGetRememberedSize(ContainerBox box, bool horizontal, out double size)
    {
        var layout = box.Style.Layout;
        var value = horizontal ? layout.Box.ContainIntrinsicWidth : layout.Box.ContainIntrinsicHeight;
        if ((value.IsAuto || layout.ContentVisibility == ContentVisibility.Auto) && box.SkipsContents && box.PseudoElement is null && box.Element?.RareDataOrNull is ElementRareData rareData
            && (horizontal == layout.IsHorizontalWritingMode ? rareData.LastRememberedInlineSize : rareData.LastRememberedBlockSize) is { } remembered)
        {
            size = remembered;
            return true;
        }

        size = 0;
        return false;
    }

    /// <summary>Returns whether the style of a box may have the <c>auto</c> keyword in <c>contain-intrinsic-size</c> (to collect the boxes cheaply while the box tree is built).</summary>
    public static bool MayHaveAutoKeyword(Cascade.ComputedStyle style) => style.Layout.ContentVisibility == ContentVisibility.Auto || style.MayHaveCascadedValueStartingWith("contain-intrinsic", "contain-intrinsic");

    /// <summary>
    /// Forgets the sizes of the axes in which the <c>contain-intrinsic-size</c> of the principal box of the elements has no
    /// <c>auto</c> keyword (the elements without a box keep their sizes), after the box tree is built.
    /// </summary>
    public static void ForgetWithoutAutoKeyword(BoxTree tree, HashSet<Element> elements)
    {
        List<Element>? removed = null;
        foreach (var element in elements)
        {
            if (tree.GetPrincipalBox(element) is not ContainerBox box || element.RareDataOrNull is not ElementRareData rareData)
                continue;

            var (inlineAuto, blockAuto) = GetAutoAxes(box);
            if (!inlineAuto)
            {
                rareData.LastRememberedInlineSize = null;
            }

            if (!blockAuto)
            {
                rareData.LastRememberedBlockSize = null;
            }

            if (rareData.LastRememberedInlineSize is null && rareData.LastRememberedBlockSize is null)
            {
                (removed ??= []).Add(element);
            }
        }

        if (removed is not null)
        {
            elements.ExceptWith(removed);
        }
    }

    /// <summary>
    /// Records the last remembered sizes of the elements of the boxes whose <c>contain-intrinsic-size</c> has the <c>auto</c>
    /// keyword and that do not skip their contents, and forgets the sizes of the disconnected elements
    /// (https://drafts.csswg.org/css-sizing-4/#last-remembered). The layout must be up to date.
    /// </summary>
    public static void Record(LayoutEngine engine, Document document, List<ContainerBox> boxes, HashSet<Element> elements)
    {
        List<Element>? removed = null;
        foreach (var element in elements)
        {
            if (!element.IsConnected || !ReferenceEquals(element.NodeDocument, document))
            {
                if (element.RareDataOrNull is ElementRareData rareData)
                {
                    rareData.LastRememberedInlineSize = null;
                    rareData.LastRememberedBlockSize = null;
                }

                (removed ??= []).Add(element);
            }
        }

        if (removed is not null)
        {
            elements.ExceptWith(removed);
        }

        foreach (var box in boxes)
        {
            // The non-atomic inline boxes have no inner size that sizing could use (as in the browsers, the size is kept)
            if (box.SkipsContents || box.IsInlineBox || box.Element is not { } element || !ReferenceEquals(engine.BoxTree?.GetPrincipalBox(element), box))
                continue;

            var (inlineAuto, blockAuto) = GetAutoAxes(box);
            if (!inlineAuto && !blockAuto)
                continue;

            // The inner size of the principal box: its content box, the block sizes of its fragments added
            double? inlineSize = null;
            var blockSize = 0d;
            for (var fragment = engine.GetLaidOutFirstFragment(box); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                if (fragment is not BoxFragment boxFragment)
                    continue;

                var size = boxFragment.WritingDirection.ToLogical(boxFragment.ContentBoxRect.Size);
                inlineSize = Math.Max(inlineSize ?? 0, size.Inline);
                blockSize += size.Block;
            }

            if (inlineSize is null)
                continue;

            var rareData = element.ElementRareData;
            if (inlineAuto)
            {
                rareData.LastRememberedInlineSize = inlineSize;
            }

            if (blockAuto)
            {
                rareData.LastRememberedBlockSize = blockSize;
            }

            elements.Add(element);
        }
    }

    /// <summary>Returns whether the <c>contain-intrinsic-size</c> of a box has the <c>auto</c> keyword in its inline and block axes.</summary>
    private static (bool Inline, bool Block) GetAutoAxes(ContainerBox box)
    {
        // content-visibility: auto implies the auto keyword (see CssValueComputer, for the initial value)
        var layout = box.Style.Layout;
        var implied = layout.ContentVisibility == ContentVisibility.Auto;
        var width = implied || layout.Box.ContainIntrinsicWidth.IsAuto;
        var height = implied || layout.Box.ContainIntrinsicHeight.IsAuto;
        return layout.IsHorizontalWritingMode ? (width, height) : (height, width);
    }
}
