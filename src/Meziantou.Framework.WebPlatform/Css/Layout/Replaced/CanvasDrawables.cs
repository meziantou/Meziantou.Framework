using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The layout and the painting of the contents of a canvas whose <c>content</c> is <c>drawable</c> (HTML-in-canvas,
/// https://github.com/WICG/html-in-canvas). The children of the canvas are laid out in its content box, in a contents box that is
/// the child of its replaced box (see <see cref="FormControlContents"/>), but they are not painted nor hit tested in place (the
/// fragment of the contents box is hidden, <see cref="Fragment.IsHiddenForPaint"/>). The principal box of a <c>drawable</c>
/// element in these contents (<see cref="ContainerBox.IsCanvasDrawable"/>) is a stacking context and the containing block of all
/// its positioned descendants: each rendering update paints its snapshot (its stacking context, without its drawable
/// descendants), which <c>drawElementImage()</c> draws into the canvas.
/// </summary>
/// <remarks>
/// The geometry of a drawable element (<c>getBoundingClientRect()</c>...) is its border box mapped by its canvas transform
/// (<see cref="HtmlCanvasElement.GetElementTransform"/>, the identity by default) to the content box of the canvas, before its CSS
/// transforms: its position in the layout of the contents does not matter. Its descendants that are not drawable are placed
/// relative to it.
/// </remarks>
internal static class CanvasDrawables
{
    /// <summary>
    /// Whether a box is in the contents of a canvas whose content is drawable: the contents box of the canvas is its nearest
    /// ancestor (or itself) that is a contents box of a replaced box. Returns null when <paramref name="stopAt"/> is reached first.
    /// </summary>
    public static bool? IsInCanvasContents(ContainerBox box, ContainerBox? stopAt = null)
    {
        for (ContainerBox? current = box; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, stopAt))
                return null;

            if (current.Kind == ContainerBoxKind.Replaced)
                return false;

            if (current.IsFormControlContents)
                return current.Parent is { Kind: ContainerBoxKind.Replaced, Element: HtmlCanvasElement };
        }

        return false;
    }

    /// <summary>Returns the boxes of the drawable elements of the contents of the replaced box of a canvas, in tree order (one box per element).</summary>
    public static List<ContainerBox> GetDrawableBoxes(ContainerBox canvasBox)
    {
        var result = new List<ContainerBox>();
        if (FormControlContents.GetContentsBox(canvasBox) is not { } contents)
            return result;

        var stack = new Stack<ContainerBox>();
        stack.Push(contents);
        while (stack.TryPop(out var box))
        {
            if (box.IsCanvasDrawable && box.Element is not null && box.PreviousContinuation is null && !result.Exists(other => ReferenceEquals(other.Element, box.Element)))
            {
                result.Add(box);
            }

            // The contents of a nested canvas are the ones of that canvas
            if (box.Kind == ContainerBoxKind.Replaced && box.Element is HtmlCanvasElement)
                continue;

            var children = new List<ContainerBox>();
            for (var child = box.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is ContainerBox container)
                {
                    children.Add(container);
                }
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Paints the snapshot of a drawable element (see <see cref="Painter.PaintCanvasDrawable"/>) in unzoomed CSS pixels, with the
    /// canvases whose bitmap is drawn with it, or returns null when it has no fragment.
    /// </summary>
    /// <remarks>
    /// The snapshot is in the CSS pixels of the canvas (the zoom of the element relative to the one of the canvas scales it, as
    /// the element is displayed); <paramref name="zoomRatio"/> is the zoom of the canvas divided by the one of the element.
    /// </remarks>
    public static DisplayList? PaintSnapshot(LayoutEngine engine, Document document, ContainerBox box, List<PaintItem>? order, out PhysicalSize size, out double zoomRatio, out double? nextFrameDelay, out List<LiveCanvasImage>? canvases)
    {
        size = default;
        zoomRatio = 1;
        nextFrameDelay = null;
        canvases = null;
        if (box.FirstFragment is not BoxFragment fragment)
            return null;

        // (an inline box is painted from the origin of the union of its fragments)
        var bounds = GetBounds(box);
        var list = Painter.PaintCanvasDrawable(engine, document, fragment, fragment.GetOffsetInRoot() - bounds.Offset, bounds.Size, order, out nextFrameDelay, out canvases);
        var zoom = GetCanvasZoom(box);
        zoomRatio = GetZoomRatio(box);
        size = new PhysicalSize(bounds.Width / zoom, bounds.Height / zoom);
        return zoom == 1 ? list : new DisplayList(list.WithTransform(DisplayMatrix.Scale(1 / zoom, 1 / zoom)).Items.ToArray(), size);
    }

    /// <summary>
    /// Returns the painting order of the contents of the replaced box of a canvas with the markers of the stacking contexts (see
    /// <see cref="PaintOrder.Collect"/>), or null when it has no contents: the stacking context of a drawable element contains
    /// its positioned descendants whose containing block is not their parent (e.g. a fixed positioned descendant of an inline
    /// drawable element, which is not a descendant of its fragment).
    /// </summary>
    public static List<PaintItem>? GetPaintOrder(ContainerBox canvasBox)
    {
        if (FormControlContents.GetContentsBox(canvasBox)?.FirstFragment is not BoxFragment contents)
            return null;

        var order = new List<PaintItem>();
        PaintOrder.Collect(contents, order, markers: true);
        return order;
    }

    /// <summary>Returns the zoom of the canvas of a drawable box divided by the zoom of the box.</summary>
    public static double GetZoomRatio(ContainerBox box) => GetCanvasZoom(box) / box.Style.Layout.EffectiveZoom;

    /// <summary>Returns the zoom of the canvas of a drawable box (the one of the contents box of the canvas).</summary>
    private static double GetCanvasZoom(ContainerBox box)
    {
        for (var current = box.Parent; current is not null; current = current.Parent)
        {
            if (current.IsFormControlContents)
                return current.Style.Layout.EffectiveZoom;
        }

        return box.Style.Layout.EffectiveZoom;
    }

    /// <summary>
    /// Returns the canvas transform of a fragment of the principal box of a drawable element as a 4x4 matrix in column-major order,
    /// mapping the coordinates of the fragment (zoomed) to the coordinates of the contents box of its canvas (zoomed), and the
    /// fragment of the contents box; or false when the fragment is not the one of a drawable element. The fragments of the parts of
    /// a drawable inline box split around block-level boxes, and of the blocks split out of it, are placed by its canvas transform.
    /// </summary>
    public static bool TryGetCanvasTransform(Fragment fragment, out double[] matrix, out BoxFragment contents)
    {
        matrix = [];
        contents = null!;
        if (fragment is not BoxFragment { Box: ContainerBox fragmentBox } || GetDrawableOwner(fragmentBox) is not { Element: { } element } box)
            return false;

        // The nearest contents box of a canvas, whose fragment is an ancestor of the fragment
        Fragment? current = fragment.Parent;
        while (current is not (null or BoxFragment { Box: { IsFormControlContents: true, Parent: { Kind: ContainerBoxKind.Replaced, Element: HtmlCanvasElement } } }))
        {
            current = current.Parent;
        }

        // A fixed positioned drawable element is laid out in the viewport, out of the fragment of the contents box: the contents box
        // is its nearest ancestor box that is one (its position in the layout does not matter, the canvas transform places it)
        current ??= GetCanvasContentsBox(box)?.FirstFragment;

        if (current is not BoxFragment { Box.Parent.Element: HtmlCanvasElement canvas } contentsFragment)
            return false;

        // The border box of the element is the union of the fragments of an inline box
        var transform = canvas.GetCanvasTransform(element) ?? Identity;
        var elementZoom = box.Style.Layout.EffectiveZoom;
        var origin = fragment.GetOffsetInRoot() - GetBounds(box).Offset;

        // The coordinates of the canvas are the ones of the rectangle where its bitmap is painted (object-fit, object-position)
        var objectOffset = contentsFragment.Parent is BoxFragment { ReplacedContentRect: { } objectRect } canvasFragment ? objectRect.Offset - canvasFragment.ContentBoxRect.Offset : PhysicalOffset.Zero;
        // (the canvas transform is in the CSS pixels of the element: its zoom scales it, as its clip)
        matrix = CssTransforms.Multiply(Translation(objectOffset.X, objectOffset.Y), CssTransforms.Multiply(Scale(elementZoom), transform));
        matrix = CssTransforms.Multiply(CssTransforms.Multiply(matrix, Scale(1 / elementZoom)), Translation(origin.X, origin.Y));
        contents = contentsFragment;
        return true;
    }

    /// <summary>
    /// Maps a point of the contents box of a canvas to the coordinates of a fragment of one of its drawable elements, by the inverse
    /// of its canvas transform and of its CSS transform. Returns false when a transform is not invertible, or the point is outside
    /// the canvas clip of the element.
    /// </summary>
    public static bool TryGetHitTestPoint(BoxFragment fragment, PhysicalOffset point, out PhysicalOffset local)
    {
        local = default;
        if (!TryGetCanvasTransform(fragment, out var matrix, out var contents) || !LayoutTransforms.TryInverseTransformPoint(matrix, point, out var untransformed))
            return false;

        // As in Chromium, the point mapped to the element is rounded to the layout units (1/64 px): the rounding errors of the
        // inverse of a canvas transform that scales down do not move a point of the edge of the element inside of it (e.g. the
        // point at the bottom edge of an element of 1 px drawn with a scale of 0.01, WPT
        // html/canvas/element/manual/draw-element-image/hit-test/low-dpi-hit-test.tentative.html)
        untransformed = new PhysicalOffset(Math.Round(untransformed.X * 64, MidpointRounding.AwayFromZero) / 64, Math.Round(untransformed.Y * 64, MidpointRounding.AwayFromZero) / 64);

        if (LayoutEngine.GetTransform(fragment) is not { } transform)
        {
            local = untransformed;
        }
        else if (!LayoutTransforms.TryInverseTransformPoint(transform, untransformed, out local))
        {
            return false;
        }

        // The canvas clip is in the coordinates of the canvas transform: the border box of the element before its CSS transforms,
        // in CSS pixels (the union of the fragments of an inline box)
        var owner = GetDrawableOwner((ContainerBox)fragment.Box)!;
        var element = owner.Element!;
        if (contents.Box.Parent?.Element is HtmlCanvasElement canvas && canvas.GetCanvasClip(element) is { } clip)
        {
            var zoom = owner.Style.Layout.EffectiveZoom;
            var origin = fragment.GetOffsetInRoot() - GetBounds(owner).Offset;
            var x = (local.X + origin.X) / zoom;
            var y = (local.Y + origin.Y) / zoom;
            if (x < clip.Left || x >= clip.Right || y < clip.Top || y >= clip.Bottom)
                return false;
        }

        return true;
    }

    /// <summary>Whether a fragment is the one of a drawable element nested in another one (<paramref name="drawable"/>), or in its subtree.</summary>
    public static bool IsInNestedDrawable(Fragment fragment, ContainerBox drawable)
    {
        for (var current = fragment; current is not null; current = current.Parent)
        {
            if (current.Box is ContainerBox { IsCanvasDrawable: true } box)
                return !ReferenceEquals(box, drawable) && !IsContinuationOf(box, drawable);
        }

        return false;

        static bool IsContinuationOf(ContainerBox box, ContainerBox drawable)
        {
            for (var part = drawable.Continuation; part is not null; part = part.Continuation)
            {
                if (ReferenceEquals(part, box))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the principal box of the drawable element that places a box by its canvas transform, or null: the box of a drawable
    /// element, a part of a drawable inline box split around block-level boxes (https://drafts.csswg.org/css2/#anonymous-block-level),
    /// or a block-level box split out of it (it is in the inline box), also when a non-drawable inline box nested in the drawable one
    /// is the one split.
    /// </summary>
    private static ContainerBox? GetDrawableOwner(ContainerBox box)
    {
        if (box.IsCanvasDrawable)
            return box;

        if (box.PreviousContinuation is not null)
            return GetFirstPart(box) is { IsCanvasDrawable: true } first ? first : null;

        if (box.SplitInlineParent is null || box.IsOutOfFlow)
            return null;

        for (var current = box.SplitInlineParent; current is { IsInlineBox: true }; current = GetFirstPart(current).Parent)
        {
            if (GetFirstPart(current) is { IsCanvasDrawable: true } first)
                return first;
        }

        return null;

        static ContainerBox GetFirstPart(ContainerBox part)
        {
            while (part.PreviousContinuation is { } previous)
            {
                part = previous;
            }

            return part;
        }
    }

    /// <summary>Returns the contents box of the canvas whose contents contain a box, or null.</summary>
    private static ContainerBox? GetCanvasContentsBox(ContainerBox box)
    {
        for (var current = box.Parent; current is not null; current = current.Parent)
        {
            if (current.Kind == ContainerBoxKind.Replaced)
                return null;

            if (current.IsFormControlContents)
                return current.Parent is { Kind: ContainerBoxKind.Replaced, Element: HtmlCanvasElement } ? current : null;
        }

        return null;
    }

    /// <summary>Returns the union of the border boxes of the fragments of a box, in the coordinates of the root of the fragment tree.</summary>
    private static PhysicalRect GetBounds(ContainerBox box)
    {
        PhysicalRect? bounds = null;
        for (var part = box; part is not null; part = part.Continuation)
        {
            for (var fragment = part.FirstFragment; fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                var rect = new PhysicalRect(fragment.GetOffsetInRoot(), fragment.Size);
                bounds = bounds is { } union ? union.Union(rect) : rect;
            }
        }

        return bounds ?? default;
    }

    private static double[] Translation(double x, double y) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, 0, 1];

    private static readonly double[] Identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static double[] Scale(double scale) => [scale, 0, 0, 0, 0, scale, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}
