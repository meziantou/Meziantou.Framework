namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The parts of a classic scrollbar that the pointer can press.</summary>
internal enum ScrollbarPart : byte
{
    /// <summary>The track before the thumb (above it, or at its left): a press scrolls backwards by a page.</summary>
    TrackBefore,

    /// <summary>The thumb: a drag scrolls.</summary>
    Thumb,

    /// <summary>The track after the thumb (below it, or at its right): a press scrolls forwards by a page.</summary>
    TrackAfter,
}

/// <summary>
/// A classic scrollbar of a scrolling box (https://drafts.csswg.org/css-overflow-3/#classic-scrollbars): its rectangle, which
/// is its track (the scrollbars have no buttons), and its thumb.
/// </summary>
/// <param name="IsVertical">Whether the scrollbar scrolls along the y axis.</param>
/// <param name="Rect">The rectangle of the scrollbar (its track).</param>
/// <param name="ThumbStart">The distance from the start of the track (its top or left edge) to the thumb.</param>
/// <param name="ThumbLength">The length of the thumb along the track, 0 when the box cannot be scrolled in the axis (the scrollbar has no thumb).</param>
internal readonly record struct Scrollbar(bool IsVertical, PhysicalRect Rect, double ThumbStart, double ThumbLength)
{
    /// <summary>The length of the track.</summary>
    public double Length => IsVertical ? Rect.Height : Rect.Width;

    /// <summary>The thickness of the scrollbar.</summary>
    public double Thickness => IsVertical ? Rect.Width : Rect.Height;

    /// <summary>Whether the scrollbar has a thumb: the box can be scrolled in the axis.</summary>
    public bool HasThumb => ThumbLength > 0;

    /// <summary>The rectangle of the thumb, as long as the track is thick (the painting insets it).</summary>
    public PhysicalRect ThumbRect => IsVertical
        ? new PhysicalRect(Rect.X, Rect.Y + ThumbStart, Rect.Width, ThumbLength)
        : new PhysicalRect(Rect.X + ThumbStart, Rect.Y, ThumbLength, Rect.Height);

    /// <summary>The coordinate of a point along the track.</summary>
    public double GetAxisCoordinate(PhysicalOffset point) => IsVertical ? point.Y : point.X;

    /// <summary>Returns the part of the scrollbar at a point of its rectangle.</summary>
    public ScrollbarPart GetPart(PhysicalOffset point)
    {
        var position = GetAxisCoordinate(point) - (IsVertical ? Rect.Y : Rect.X);
        if (!HasThumb || position < ThumbStart)
            return ScrollbarPart.TrackBefore;

        return position < ThumbStart + ThumbLength ? ScrollbarPart.Thumb : ScrollbarPart.TrackAfter;
    }
}

/// <summary>
/// The geometry of the classic scrollbars of a scrolling box (a scroll container or the viewport), in the scrollbar gutters
/// that the layout reserves (see <see cref="ScrollContainers.GetScrollbarSides"/>): the vertical scrollbar is in the gutter on
/// the right (on the left in a horizontal writing mode with <c>direction: rtl</c>), along the scrollport; the horizontal one in
/// the gutter at the bottom; the scroll corner is the square where their gutters meet. A gutter of <c>scrollbar-gutter</c>
/// without scrollbar is empty.
/// </summary>
/// <remarks>
/// The scrollbars have no buttons: the track is as long as the scrollport. The length of the thumb is the length of the track
/// multiplied by the ratio of the scrollport to the scrolling area (at least <see cref="MinimumThumbLength"/>, at most the
/// track), and its position follows the scroll position between its minimum and its maximum. A box that cannot be scrolled
/// in the axis (<c>overflow: scroll</c> without overflow) has a track without thumb.
/// </remarks>
internal sealed class ScrollbarGeometry
{
    /// <summary>The minimum length of a thumb, in CSS pixels (a thumb is never longer than its track).</summary>
    public const double MinimumThumbLength = 16;

    private ScrollbarGeometry()
    {
    }

    /// <summary>The vertical scrollbar, or null.</summary>
    public Scrollbar? Vertical { get; private init; }

    /// <summary>The horizontal scrollbar, or null.</summary>
    public Scrollbar? Horizontal { get; private init; }

    /// <summary>The scroll corner (the box has both scrollbars), or null.</summary>
    public PhysicalRect? Corner { get; private init; }

    /// <summary>Returns the scrollbar at a point, or null.</summary>
    public Scrollbar? GetScrollbar(PhysicalOffset point)
    {
        if (Vertical is { } vertical && Contains(vertical.Rect, point))
            return vertical;

        if (Horizontal is { } horizontal && Contains(horizontal.Rect, point))
            return horizontal;

        return null;

        static bool Contains(PhysicalRect rect, PhysicalOffset point) => point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    /// <summary>Returns the scrollbar of an axis, or null.</summary>
    public Scrollbar? GetScrollbar(bool vertical) => vertical ? Vertical : Horizontal;

    /// <summary>
    /// Returns the scrollbars of the fragment of a scroll container, in the coordinates of the fragment (its border box), or
    /// null when it displays no scrollbar.
    /// </summary>
    public static ScrollbarGeometry? ForFragment(BoxFragment fragment)
    {
        var displayed = fragment.ScrollbarsDisplayed;
        if (displayed == Scrollbars.None || fragment.Box.Kind == ContainerBoxKind.Viewport)
            return null;

        var gutters = fragment.Scrollbars;
        var writingDirection = fragment.WritingDirection;
        var onLeft = gutters.Left > 0 && (gutters.Right <= 0 || (writingDirection.IsHorizontal && writingDirection.IsRightToLeft));
        return Create(fragment.BorderBoxRect.Contract(fragment.Border), fragment.PaddingBoxRect, displayed, onLeft, fragment.ScrollState);
    }

    /// <summary>
    /// Returns the scrollbars of the viewport of a document, in the coordinates of the viewport (its origin is its top left
    /// corner, scrollbars included), or null when it displays no scrollbar.
    /// </summary>
    /// <param name="root">The root of the fragment tree (the fragment of the initial containing block: the scrollport).</param>
    /// <param name="viewportSize">The size of the viewport.</param>
    /// <param name="state">The scrolling box of the viewport.</param>
    public static ScrollbarGeometry? ForViewport(BoxFragment root, PhysicalSize viewportSize, ScrollState? state)
    {
        var displayed = root.ScrollbarsDisplayed;
        if (displayed == Scrollbars.None)
            return null;

        var scrollport = new PhysicalRect(root.Offset, root.Size);
        var onLeft = scrollport.Left > 0 && scrollport.Right >= viewportSize.Width;
        return Create(new PhysicalRect(0, 0, viewportSize.Width, viewportSize.Height), scrollport, displayed, onLeft, state);
    }

    private static ScrollbarGeometry? Create(PhysicalRect outer, PhysicalRect scrollport, Scrollbars displayed, bool verticalOnLeft, ScrollState? state)
    {
        var verticalThickness = verticalOnLeft ? scrollport.Left - outer.Left : outer.Right - scrollport.Right;
        var horizontalThickness = outer.Bottom - scrollport.Bottom;
        var hasVertical = (displayed & Scrollbars.Vertical) != Scrollbars.None && verticalThickness > 0 && scrollport.Height > 0;
        var hasHorizontal = (displayed & Scrollbars.Horizontal) != Scrollbars.None && horizontalThickness > 0 && scrollport.Width > 0;
        if (!hasVertical && !hasHorizontal)
            return null;

        var verticalX = verticalOnLeft ? outer.Left : scrollport.Right;
        var min = state?.MinOffset ?? default;
        var max = state?.MaxOffset ?? default;
        var offset = state?.Offset ?? default;
        return new ScrollbarGeometry
        {
            Vertical = hasVertical ? CreateScrollbar(isVertical: true, new PhysicalRect(verticalX, scrollport.Top, verticalThickness, scrollport.Height), min.Y, max.Y, offset.Y) : null,
            Horizontal = hasHorizontal ? CreateScrollbar(isVertical: false, new PhysicalRect(scrollport.Left, scrollport.Bottom, scrollport.Width, horizontalThickness), min.X, max.X, offset.X) : null,
            Corner = hasVertical && hasHorizontal ? new PhysicalRect(verticalX, scrollport.Bottom, verticalThickness, horizontalThickness) : null,
        };
    }

    private static Scrollbar CreateScrollbar(bool isVertical, PhysicalRect rect, double min, double max, double offset)
    {
        var length = isVertical ? rect.Height : rect.Width;
        var range = max - min;
        if (!(range > 0) || length <= 0)
            return new Scrollbar(isVertical, rect, 0, 0);

        // The scrollport is as long as the track
        var thumbLength = Math.Min(length, Math.Max(MinimumThumbLength, length * length / (length + range)));
        var progress = Math.Clamp((offset - min) / range, 0, 1);
        return new Scrollbar(isVertical, rect, progress * (length - thumbLength), thumbLength);
    }
}
