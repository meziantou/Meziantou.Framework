namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The in-flow contents of a scroll container (the "alignment rectangle" of https://drafts.csswg.org/css-overflow-3/#scrollable-overflow-calculation):
/// its paddings are added around them in its scrollable overflow, "to enable scroll positions that satisfy the requirements
/// of both place-content: start and place-content: end alignment".
/// </summary>
internal static class InFlowBounds
{
    /// <summary>
    /// Returns the rectangle of the in-flow contents of a box, in the coordinates of its fragment, as Blink: in the
    /// block axis, its content box with the block size of its contents (the collapsed margins of the last children included);
    /// in the inline axis, its content box and the margin boxes of its in-flow children, floats and line box contents, at their
    /// position before the relative positioning (the negative margins shrink them). The out-of-flow boxes are appended after it
    /// is computed, they are not included.
    /// </summary>
    /// <param name="children">The in-flow children of the box (its children before its absolutely positioned boxes are appended).</param>
    /// <param name="writingDirection">The writing mode of the box.</param>
    /// <param name="contentRect">The content box, with the block size of the contents.</param>
    /// <param name="bothAxes">
    /// Whether the margin boxes of the children extend the rectangle in the block axis too: the flex items, which are not
    /// stacked in the block axis (as in the browsers, https://drafts.csswg.org/css-overflow-3/#scrollable-overflow-calculation:
    /// "the margin areas of grid item and flex item boxes").
    /// </param>
    /// <param name="includesMargins">Whether the positive margins of the children extend the rectangle (the negative ones always shrink it).</param>
    public static PhysicalRect Compute(ReadOnlySpan<Fragment> children, WritingDirection writingDirection, PhysicalRect contentRect, bool bothAxes = false, bool includesMargins = true)
    {
        var horizontal = writingDirection.IsHorizontal;
        var start = horizontal ? contentRect.Left : contentRect.Top;
        var end = horizontal ? contentRect.Right : contentRect.Bottom;
        var blockStart = horizontal ? contentRect.Top : contentRect.Left;
        var blockEnd = horizontal ? contentRect.Bottom : contentRect.Right;
        var percentageBasis = horizontal ? contentRect.Width : contentRect.Height;
        foreach (var child in children)
        {
            // The content hidden after the clamp point of a line-clamp container (see LineClamp)
            if (child.IsHiddenForPaint)
                continue;

            switch (child)
            {
                case BoxFragment box:
                    Add(GetStaticMarginRect(box, child.Offset, percentageBasis, includesMargins));
                    break;

                case LineBoxFragment line:
                    Add(line.Rect);
                    var nonHanging = line.NonHangingRect?.Translate(line.Offset);
                    foreach (var item in line.ChildrenSpan)
                    {
                        var rect = item is BoxFragment box ? GetStaticMarginRect(box, line.Offset + item.Offset, percentageBasis) : item.Rect.Translate(line.Offset);
                        if (nonHanging is { } bounds)
                        {
                            // The content that hangs at the end of the line is not scrollable overflow (https://drafts.csswg.org/css-text-4/#hanging)
                            var left = Math.Max(rect.Left, bounds.Left);
                            var top = Math.Max(rect.Top, bounds.Top);
                            var right = Math.Min(rect.Right, bounds.Right);
                            var bottom = Math.Min(rect.Bottom, bounds.Bottom);
                            if (right < left || bottom < top)
                                continue;

                            rect = new PhysicalRect(left, top, right - left, bottom - top);
                        }

                        Add(rect);
                    }

                    break;
            }
        }

        return horizontal
            ? new PhysicalRect(start, blockStart, end - start, blockEnd - blockStart)
            : new PhysicalRect(blockStart, start, blockEnd - blockStart, end - start);

        void Add(PhysicalRect rect)
        {
            start = Math.Min(start, horizontal ? rect.Left : rect.Top);
            end = Math.Max(end, horizontal ? rect.Right : rect.Bottom);
            if (bothAxes)
            {
                blockStart = Math.Min(blockStart, horizontal ? rect.Top : rect.Left);
                blockEnd = Math.Max(blockEnd, horizontal ? rect.Bottom : rect.Right);
            }
        }
    }

    /// <summary>
    /// The margin box of a child at its position before the relative positioning (the negative margins shrink it: WPT
    /// scrollWidthHeight-overflow-visible-negative-margins.html), with its computed margins (an over-constrained box keeps its
    /// inline-end margin, as in Blink: WPT scrollable-overflow-padding.html).
    /// </summary>
    private static PhysicalRect GetStaticMarginRect(BoxFragment box, PhysicalOffset offset, double percentageBasis, bool includesPositiveMargins = true)
    {
        var rect = new PhysicalRect(offset - box.RelativeOffset - box.StickyOffset, box.Size);
        var used = box.Margin;
        var computed = box.Box.Style.Layout.Box.Margin;
        var top = computed.Top.IsAuto ? used.Top : computed.Top.Value.Resolve(percentageBasis);
        var right = computed.Right.IsAuto ? used.Right : computed.Right.Value.Resolve(percentageBasis);
        var bottom = computed.Bottom.IsAuto ? used.Bottom : computed.Bottom.Value.Resolve(percentageBasis);
        var left = computed.Left.IsAuto ? used.Left : computed.Left.Value.Resolve(percentageBasis);
        if (!includesPositiveMargins)
        {
            top = Math.Min(0, top);
            right = Math.Min(0, right);
            bottom = Math.Min(0, bottom);
            left = Math.Min(0, left);
        }

        return new PhysicalRect(rect.X - left, rect.Y - top, Math.Max(0, rect.Width + left + right), Math.Max(0, rect.Height + top + bottom));
    }
}
