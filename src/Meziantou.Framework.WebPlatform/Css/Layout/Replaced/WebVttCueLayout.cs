using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.WebVtt;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout of the cues of the text tracks rendered over a video element: the rules for updating the display of WebVTT
/// text tracks (https://w3c.github.io/webvtt/#rules-for-updating-the-display-of-webvtt-text-tracks) and the processing of the
/// cue settings (https://w3c.github.io/webvtt/#processing-cue-settings).
/// </summary>
/// <remarks>
/// <para>
/// The video viewport is the content box of the video element. The boxes of the cues and of the regions (see
/// <see cref="BoxTreeBuilder.RebuildCueBoxes"/>) are the children of the replaced box of the element: each one is laid out as
/// an independent formatting context with the size given by the settings of its cue (the vw and vh units of the
/// specification are percentages of the video viewport), then positioned by this class rather than by the absolute
/// positioning of CSS: the boxes of a cue with a snap-to-lines flag are moved line by line until they do not overlap the
/// boxes of the cues that are already placed and are within the video viewport; the boxes of the other cues are moved to
/// the closest position where they do not overlap. The position of a cue is kept in its text track cue display state while
/// the cue is displayed.
/// </para>
/// <para>
/// The fragments are appended to the fragment of the video element like absolutely positioned boxes, so they are painted
/// over the frame of the video.
/// </para>
/// </remarks>
internal static class WebVttCueLayout
{
    private const double Epsilon = 0.001;

    public static void Layout(LayoutContext context, HtmlVideoElement video, ContainerBox box, BoxFragment fragment)
    {
        // (the boxes of the cues are built by the layout)
        context.PreventLayoutCaching();
        var area = fragment.ContentBoxRect;
        var display = video.CueDisplay;
        if (display is null || area.IsEmpty || box.SkipsContents || !display.Update(area.Width, area.Height))
        {
            if (display is not null && ReferenceEquals(display.BuiltBox, box))
            {
                context.Engine.BoxTree?.RebuildCueBoxes(box, []);
                display.BuiltBox = null;
            }

            return;
        }

        // The boxes are built again when the cues that are displayed change, and when the styles of the cues were computed
        // again with the size of the video element (a size container, see LayoutContext.OnSizeContainerLaidOut)
        if (!ReferenceEquals(display.BuiltBox, box) || display.BuiltGeneration != display.Generation || !HasCurrentStyles(box, display))
        {
            context.Engine.BoxTree?.RebuildCueBoxes(box, display.Items);
            display.BuiltBox = box;
            display.BuiltGeneration = display.Generation;

            // The images of the styles of the cues are fetched when the cues are rendered
            video.NodeDocument.FetchCueImages(video, display.Items);
        }

        // The output: the rectangles of the boxes that are placed, in the video viewport
        var output = new List<PhysicalRect>();
        List<BoxFragment>? fragments = null;
        var child = box.FirstChild;
        foreach (var item in display.Items)
        {
            if (child is not ContainerBox itemBox)
                break;

            child = child.NextSibling;
            var itemFragment = item.Region is { } region
                ? LayoutRegion(context, region, itemBox, area.Size, output)
                : LayoutCue(context, item.Cue!, itemBox, area.Size, output);

            // The boxes of the cues are in the output: they are the text track cue display state of the cues
            item.Cue?.DisplayState?.IsDisplayed = true;
            foreach (var regionCue in item.RegionCues)
            {
                regionCue.DisplayState?.IsDisplayed = true;
            }

            if (itemFragment is not null)
            {
                itemFragment.Offset = new PhysicalOffset(itemFragment.Offset.X + area.X, itemFragment.Offset.Y + area.Y);
                (fragments ??= []).Add(itemFragment);
            }
        }

        if (fragments is not null)
        {
            fragment.AppendOutOfFlowChildren(fragments);
        }
    }

    private static bool HasCurrentStyles(ContainerBox box, WebVttDisplay display)
    {
        var engine = box.Style.Engine;
        var child = box.FirstChild;
        foreach (var item in display.Items)
        {
            if (child is not ContainerBox itemBox || !ReferenceEquals(itemBox.Style, engine.GetStyle(item.Element)))
                return false;

            child = child.NextSibling;
        }

        return true;
    }

    /// <summary>
    /// Lays out the box of a WebVTT region object: its width is a percentage of the width of the video viewport, its height at
    /// most its number of lines (of 6vh), and the anchor of the region is at the anchor of the video viewport. The box of a
    /// region that is not full is aligned with the bottom of the region.
    /// </summary>
    private static BoxFragment LayoutRegion(LayoutContext context, VttRegion region, ContainerBox box, PhysicalSize area, List<PhysicalRect> output)
    {
        var width = region.Width * area.Width / 100;
        var height = area.Height * 0.06 * region.Lines;
        var left = (region.ViewportAnchorX * area.Width / 100) - (region.RegionAnchorX * width / 100);
        var top = (region.ViewportAnchorY * area.Height / 100) - (region.RegionAnchorY * height / 100);
        var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
        var space = ConstraintSpace.ForContentBox(writingDirection, width, blockSize: null) with { FixedInlineSize = width };
        var fragment = context.LayoutBox(box, space);

        // "If the CSS boxes together have a height less than the height of the region box, [...] increase top by diff"
        top += Math.Max(0, height - fragment.Size.Height);
        fragment.Offset = new PhysicalOffset(left, top);
        output.Add(new PhysicalRect(left, top, fragment.Size.Width, fragment.Size.Height));
        return fragment;
    }

    /// <summary>
    /// Applies the WebVTT cue settings to obtain the boxes of a cue (https://w3c.github.io/webvtt/#apply-webvtt-cue-settings),
    /// and returns the fragment of the root of the cue at its position in the video viewport, or null when the cue is not displayed.
    /// </summary>
    private static BoxFragment? LayoutCue(LayoutContext context, VttCue cue, ContainerBox box, PhysicalSize area, List<PhysicalRect> output)
    {
        if (cue.DisplayState is not { IsIgnored: false } state)
            return null;

        // The size of the cue box in the axis of the writing direction is a percentage of the video viewport; its size in
        // the other axis is the one of its lines
        var isHorizontal = cue.Vertical == DirectionSetting.Horizontal;
        var inlineSize = state.Size * (isHorizontal ? area.Width : area.Height) / 100;
        var writingDirection = WritingDirection.FromStyle(box.Style.Layout);
        var space = ConstraintSpace.ForContentBox(writingDirection, inlineSize, isHorizontal ? area.Height : area.Width) with { FixedInlineSize = inlineSize };
        var fragment = context.LayoutBox(box, space);
        var size = fragment.Size;
        if (state.Position is var (placedX, placedY))
        {
            // The boxes of the text track cue display state keep their position
            fragment.Offset = new PhysicalOffset(placedX, placedY);
            output.Add(new PhysicalRect(placedX, placedY, size.Width, size.Height));
            return fragment;
        }

        // "If there are no line boxes in boxes, skip the remainder of these substeps for cue. The cue is ignored." (a line box
        // without content has no height: it is treated as not existing)
        if (FindFirstLineBox(fragment, default) is not { } firstLine || (isHorizontal ? firstLine.Height : firstLine.Width) <= 0)
        {
            state.IsIgnored = true;
            return null;
        }

        // The temporary position: the position of the cue in the axis of the writing direction, and its line in the other
        // axis when the line is a percentage
        var positionOffset = state.PositionOffset * (isHorizontal ? area.Width : area.Height) / 100;
        var line = cue.ComputedLine;
        var lineOffset = cue.SnapToLines ? 0 : line * (isHorizontal ? area.Height : area.Width) / 100;
        var x = isHorizontal ? positionOffset : lineOffset;
        var y = isHorizontal ? lineOffset : positionOffset;
        var isPlaced = cue.SnapToLines
            ? SnapToLines(cue, line, firstLine, size, area, output, ref x, ref y)
            : PlaceWithoutSnapToLines(cue, size, area, output, ref x, ref y);

        if (!isPlaced)
        {
            // The boxes are removed: there is no position where the cue does not overlap the other cues
            state.IsIgnored = true;
            return null;
        }

        state.Position = (x, y);
        fragment.Offset = new PhysicalOffset(x, y);
        output.Add(new PhysicalRect(x, y, size.Width, size.Height));
        return fragment;
    }

    /// <summary>
    /// Adjusts the positions of the boxes of a cue whose snap-to-lines flag is true: the boxes are placed at the line of the
    /// cue (a number of lines from the start of the video viewport, or from its end when it is negative), then moved line by
    /// line until they do not overlap the boxes of the output and are within the video viewport; in the other direction when
    /// they reach the edge of the viewport. Returns false when the boxes are removed.
    /// </summary>
    private static bool SnapToLines(VttCue cue, double computedLine, PhysicalRect firstLine, PhysicalSize size, PhysicalSize area, List<PhysicalRect> output, ref double x, ref double y)
    {
        var isHorizontal = cue.Vertical == DirectionSetting.Horizontal;
        var fullDimension = isHorizontal ? area.Height : area.Width;
        var step = isHorizontal ? firstLine.Height : firstLine.Width;
        if (step <= 0)
            return true;

        var line = Math.Floor(computedLine + 0.5);
        if (cue.Vertical == DirectionSetting.Rl)
        {
            line = -(line + 1);
        }

        var position = step * line;
        if (cue.Vertical == DirectionSetting.Rl)
        {
            position -= size.Width;
            position += step;
        }

        if (line < 0)
        {
            position += fullDimension;
            step = -step;
        }

        if (isHorizontal)
        {
            y += position;
        }
        else
        {
            x += position;
        }

        var specifiedX = x;
        var specifiedY = y;
        var switched = false;

        // The number of steps is bounded by the number of lines of the video viewport in each direction
        var maximumSteps = (int)Math.Min(100_000, (2 * Math.Ceiling((fullDimension + (isHorizontal ? size.Height : size.Width)) / Math.Abs(step))) + 4);
        for (var i = 0; i < 2 * maximumSteps; i++)
        {
            var rect = new PhysicalRect(x, y, size.Width, size.Height);
            if (!Overlaps(rect, output) && IsWithin(rect, area))
                return true;

            // The first line box is beyond the edge of the title area in the direction of the steps: the other direction is tried
            var first = firstLine.Translate(new PhysicalOffset(x, y));
            var reachedEdge = isHorizontal
                ? (step < 0 && first.Top < -Epsilon) || (step > 0 && first.Bottom > area.Height + Epsilon)
                : (step < 0 && first.Left < -Epsilon) || (step > 0 && first.Right > area.Width + Epsilon);

            if (reachedEdge)
            {
                if (switched)
                    return false;

                x = specifiedX;
                y = specifiedY;
                step = -step;
                switched = true;
                continue;
            }

            if (isHorizontal)
            {
                y += step;
            }
            else
            {
                x += step;
            }
        }

        return false;
    }

    /// <summary>
    /// Adjusts the positions of the boxes of a cue whose snap-to-lines flag is false: the boxes are aligned at the line of the
    /// cue according to its line alignment, then moved to the closest position where they do not overlap the boxes of the
    /// output and are within the video viewport (the highest one, then the leftmost one, among the positions at the same
    /// distance), if any.
    /// </summary>
    private static bool PlaceWithoutSnapToLines(VttCue cue, PhysicalSize size, PhysicalSize area, List<PhysicalRect> output, ref double x, ref double y)
    {
        var isHorizontal = cue.Vertical == DirectionSetting.Horizontal;
        var factor = cue.LineAlign switch
        {
            LineAlignSetting.Center => 0.5,
            LineAlignSetting.End => 1,
            _ => 0,
        };

        if (isHorizontal)
        {
            y -= size.Height * factor;
        }
        else
        {
            x -= size.Width * factor;
        }

        var rect = new PhysicalRect(x, y, size.Width, size.Height);
        if (!Overlaps(rect, output) && IsWithin(rect, area))
            return true;

        // The positions where the boxes may be the closest to their current position: the current position, and the positions
        // where an edge of the boxes touches an edge of the video viewport or of a box of the output, in each axis
        var maximumX = area.Width - size.Width;
        var maximumY = area.Height - size.Height;
        if (maximumX < -Epsilon || maximumY < -Epsilon)
            return true;

        maximumX = Math.Max(maximumX, 0);
        maximumY = Math.Max(maximumY, 0);
        var xs = new List<double> { Math.Clamp(x, 0, maximumX) };
        var ys = new List<double> { Math.Clamp(y, 0, maximumY) };
        foreach (var other in output)
        {
            AddCandidate(xs, other.Left - size.Width, maximumX);
            AddCandidate(xs, other.Right, maximumX);
            AddCandidate(ys, other.Top - size.Height, maximumY);
            AddCandidate(ys, other.Bottom, maximumY);
        }

        var found = false;
        var bestDistance = double.PositiveInfinity;
        var bestX = x;
        var bestY = y;
        foreach (var candidateY in ys)
        {
            foreach (var candidateX in xs)
            {
                if (Overlaps(new PhysicalRect(candidateX, candidateY, size.Width, size.Height), output))
                    continue;

                var distance = ((candidateX - x) * (candidateX - x)) + ((candidateY - y) * (candidateY - y));
                if (!found || distance < bestDistance - Epsilon
                    || (Math.Abs(distance - bestDistance) <= Epsilon && (candidateY < bestY - Epsilon || (Math.Abs(candidateY - bestY) <= Epsilon && candidateX < bestX))))
                {
                    found = true;
                    bestDistance = distance;
                    bestX = candidateX;
                    bestY = candidateY;
                }
            }
        }

        // Otherwise, the boxes unfortunately overlap
        if (found)
        {
            x = bestX;
            y = bestY;
        }

        return true;

        static void AddCandidate(List<double> candidates, double value, double maximum)
        {
            if (value >= -Epsilon && value <= maximum + Epsilon)
            {
                candidates.Add(Math.Clamp(value, 0, maximum));
            }
        }
    }

    private static bool Overlaps(PhysicalRect rect, List<PhysicalRect> output)
    {
        foreach (var other in output)
        {
            if (rect.Left < other.Right - Epsilon && other.Left < rect.Right - Epsilon && rect.Top < other.Bottom - Epsilon && other.Top < rect.Bottom - Epsilon)
                return true;
        }

        return false;
    }

    private static bool IsWithin(PhysicalRect rect, PhysicalSize area)
        => rect.Left >= -Epsilon && rect.Top >= -Epsilon && rect.Right <= area.Width + Epsilon && rect.Bottom <= area.Height + Epsilon;

    /// <summary>Returns the rectangle of the first line box of a fragment, in the coordinates of the fragment, or null when it has no line box.</summary>
    private static PhysicalRect? FindFirstLineBox(Fragment fragment, PhysicalOffset offset)
    {
        foreach (var child in fragment.Children)
        {
            var childOffset = new PhysicalOffset(offset.X + child.Offset.X, offset.Y + child.Offset.Y);
            if (child is LineBoxFragment line)
                return new PhysicalRect(childOffset, line.Size);

            if (child is BoxFragment && FindFirstLineBox(child, childOffset) is { } nested)
                return nested;
        }

        return null;
    }
}
