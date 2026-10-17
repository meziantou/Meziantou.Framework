using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The classic scrollbars that the pointer presses (see ScrollbarGeometry; the scrollbars are painted by Painter.Scrollbars.cs).
// As in the browsers, the press of the primary button of a pointer that hovers (the mouse, a pen) on a scrollbar is handled
// by the scrollbar: a press on the track scrolls by a page towards the pointer, and a drag of the thumb scrolls its box. The
// pointerdown, mousedown, pointerup and mouseup events are fired at the scroll container (the document element for the
// scrollbars of the viewport; WPT pointerevents/compat/pointerevent_mouse-pointer-on-scrollbar.html), whether the page cancels
// them or not, but the press does not change the selection, starts no drag-and-drop and has no click event, and the moves of
// the pointer fire no event until the release: the scrollbar captures the pointer. The press focuses as a press on the scroll
// container does, except on a scrollbar of the viewport, which keeps the focus where it is.
public sealed partial class UserInput
{
    /// <summary>The press of the primary button of a pointer on a scrollbar.</summary>
    /// <param name="Document">The document of the scrolling box.</param>
    /// <param name="State">The scrolling box.</param>
    /// <param name="IsVertical">Whether the scrollbar scrolls along the y axis.</param>
    /// <param name="Part">The part of the scrollbar that was pressed.</param>
    /// <param name="Position">The position of the pointer along the track when the button was pressed (in the coordinates of the scrollbar).</param>
    /// <param name="Offset">The scroll position of the box in the axis when the button was pressed.</param>
    internal sealed record ScrollbarPress(Document Document, ScrollState State, bool IsVertical, ScrollbarPart Part, double Position, double Offset)
    {
        /// <summary>Whether the scrollbar is a scrollbar of the viewport of its document.</summary>
        public bool IsViewport => State.IsViewport;
    }

    /// <summary>Whether the classic scrollbars are painted and interactive (<see cref="ViewportOptions.ScrollbarsVisible"/>).</summary>
    private bool AreScrollbarsVisible => (_browsingContext.Top.Options.Viewport ?? ViewportOptions.Default).ScrollbarsVisible;

    /// <summary>
    /// Returns the scrollbar at a point of the viewport of the top-level browsing context, with the position of the point in its
    /// coordinates, or null: a scrollbar of the viewport of the document at the point (the documents of the nested browsing
    /// contexts included), else a scrollbar of the scroll container that the hit testing finds at the point (the gutters of
    /// its scrollbars are a part of its box). The scrollbars of an inert element are not interactive.
    /// </summary>
    private (Document Document, ScrollState State, Scrollbar Scrollbar, PhysicalOffset Point)? HitTestScrollbar(double x, double y)
    {
        if (!AreScrollbarsVisible)
            return null;

        var document = _browsingContext.Document;
        while (true)
        {
            if (!document.IsFullyActive || document.BrowsingContext is null)
                return null;

            var engine = document.GetLayoutEngine();
            if (engine.EnsureLayout() is not { } root)
                return null;

            var point = new PhysicalOffset(x, y);
            if (GetViewportScrollbars(document, root) is { } viewportScrollbars && viewportScrollbars.GetScrollbar(point) is { } viewportScrollbar)
                return (document, engine.ViewportScrollState, viewportScrollbar, point);

            if (HitTestDocument(document, x, y) is not { } element)
                return null;

            if (NavigableContainers.GetContentNavigable(element) is { IsDestroyed: false } child && child.Document.DocumentElement is not null
                && TryMapToFrame(element, x, y, out var frameX, out var frameY, out var inContentBox) && inContentBox)
            {
                document = child.Document;
                (x, y) = (frameX, frameY);
                continue;
            }

            // The scrolling box of the element (the box of its fragment that scrolls: e.g. the anonymous content box of a fieldset)
            if (FocusModel.IsInert(element) || engine.GetScrollState(element) is not { Fragment: { } fragment } state
                || !LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(point), out var local) || ScrollbarGeometry.ForFragment(fragment)?.GetScrollbar(local) is not { } scrollbar)
            {
                return null;
            }

            return (document, state, scrollbar, local);
        }
    }

    private static ScrollbarGeometry? GetViewportScrollbars(Document document, BoxFragment root)
    {
        var environment = document.GetStyleEngine().Environment;
        return ScrollbarGeometry.ForViewport(root, new PhysicalSize(environment.Width, environment.Height), document.GetLayoutEngine().ViewportScrollState);
    }

    /// <summary>Returns the press of a pointer on the scrollbar at its position, or null.</summary>
    private ScrollbarPress? CreateScrollbarPress(PointerInput pointer)
    {
        if (HitTestScrollbar(pointer.X, pointer.Y) is not var (document, state, scrollbar, point))
            return null;

        return new ScrollbarPress(document, state, scrollbar.IsVertical, scrollbar.GetPart(point), scrollbar.GetAxisCoordinate(point), scrollbar.IsVertical ? state.Offset.Y : state.Offset.X);
    }

    /// <summary>The scrollbar handles the press: a press on the track scrolls by a page towards the pointer (once: the press does not repeat).</summary>
    private static void BeginScrollbarPress(PointerInput pointer, ScrollbarPress press)
    {
        // The press is not the start of a selection nor of a drag-and-drop operation
        pointer.DragNode = null;
        pointer.DragsSelection = false;
        if (press.Part == ScrollbarPart.Thumb || !press.Document.IsFullyActive || press.State.Fragment is null)
            return;

        var direction = press.Part == ScrollbarPart.TrackBefore ? -1d : 1d;
        var (deltaX, deltaY) = press.IsVertical ? (0d, direction) : (direction, 0d);
        ScrollBox(press.Document, press.State, ref deltaX, ref deltaY, UserScrollUnit.Page, UserScrollKind.Directional);
    }

    /// <summary>The release of the press on a scrollbar: the drag of its thumb ends (it snaps, then fires <c>scrollend</c>).</summary>
    private static void EndScrollbarPress(ScrollbarPress press)
    {
        if (press.Part == ScrollbarPart.Thumb && press.Document is { IsFullyActive: true, BrowsingContext: not null } document)
        {
            document.GetLayoutEngine().ReleaseUserGesture(press.State);
        }
    }

    /// <summary>
    /// The pointer moves while it presses a scrollbar: the drag of the thumb scrolls the box to the position where the thumb
    /// follows the pointer (a direct manipulation, which snaps when the press is released, see <see cref="LayoutEngine.ScrollByUser"/>).
    /// </summary>
    private void MoveScrollbarPress(PointerInput pointer, ScrollbarPress press)
    {
        var state = press.State;
        var document = press.Document;
        if (press.Part != ScrollbarPart.Thumb || !document.IsFullyActive || document.BrowsingContext is null)
            return;

        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is not { } root || state.Fragment is not { } fragment)
            return;

        // The position of the pointer in the coordinates of the scrollbar
        var (x, y) = MapToDocument(document, pointer.X, pointer.Y);
        var point = new PhysicalOffset(x, y);
        ScrollbarGeometry? geometry;
        if (state.IsViewport)
        {
            geometry = GetViewportScrollbars(document, root);
        }
        else
        {
            geometry = ScrollbarGeometry.ForFragment(fragment);
            if (!LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(point), out point))
                return;
        }

        if (geometry?.GetScrollbar(press.IsVertical) is not { HasThumb: true } scrollbar)
            return;

        // The thumb moves over the track minus its length while the box scrolls over its whole range
        var travel = scrollbar.Length - scrollbar.ThumbLength;
        var range = press.IsVertical ? state.MaxOffset.Y - state.MinOffset.Y : state.MaxOffset.X - state.MinOffset.X;
        if (travel <= 0 || range <= 0)
            return;

        var target = press.Offset + ((scrollbar.GetAxisCoordinate(point) - press.Position) * range / travel);
        var delta = press.IsVertical ? new PhysicalOffset(0, target - state.Offset.Y) : new PhysicalOffset(target - state.Offset.X, 0);
        engine.ScrollByUser(state, delta, UserScrollKind.ThumbDrag);
    }
}
