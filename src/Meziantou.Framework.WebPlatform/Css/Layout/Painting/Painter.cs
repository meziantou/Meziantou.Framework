using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The painting of a document (https://drafts.csswg.org/css2/#painting, https://drafts.csswg.org/css2/#elaborate-stacking-contexts):
/// turns the fragment tree into a <see cref="DisplayList"/>, in the painting order of the fragments (<see cref="PaintOrder"/>).
/// </summary>
/// <remarks>
/// <para>
/// The stacking contexts are the groups of the display list: a stacking context saves the state, applies its transform
/// (<see cref="LayoutTransforms.GetMatrix"/>, the origin of the coordinates of its contents becomes its border box), and starts
/// a layer for its opacity, its blend mode or its isolation; it is restored at its end. The transforms and the effects nest
/// with the stacking contexts, so the contents of a stacking context are painted in its coordinates.
/// </para>
/// <para>
/// The overflow clips do not nest with the painting order (a positioned descendant is painted after the in-flow content of its
/// ancestors, and escapes the clips of the ancestors that are not in its containing block chain): as the property trees of
/// Blink, each painted fragment has its chain of clips, the ancestors in its fragment tree (its containing block chain) that
/// clip their overflow, up to its stacking context; the painter keeps the clips of the previous fragment and only restores
/// and applies the clips that differ.
/// </para>
/// <para>
/// Each fragment is painted at its position relative to the origin of its group: <see cref="Fragment.GetOffsetInRoot"/> applies
/// the scroll positions of the scroll containers, and the whole list is translated by the scroll position of the viewport (the
/// fixed positioned boxes of the viewport do not move). The outlines are painted at the end of their stacking context
/// (https://drafts.csswg.org/css2/#elaborate-stacking-contexts, step 10).
/// </para>
/// </remarks>
internal sealed partial class Painter
{
    /// <summary>The extent of a clip in an axis that does not clip (<c>overflow-x: hidden; overflow-y: visible</c>).</summary>
    private const double UnclippedExtent = 1e7;

    private readonly LayoutEngine _engine;
    private readonly Document _document;
    private DisplayListBuilder _builder = new();
    private readonly List<ClipEntry> _chain = [];
    private readonly double _devicePixelRatio;
    private Group _group;

    /// <summary>Whether a box of the painting order has a clip rectangle (<c>clip</c>, see <see cref="GetClipChain"/>).</summary>
    private bool _hasCssClips;

    /// <summary>The stacking contexts that contain a stacking context with a blend mode (they are isolated groups), or null.</summary>
    private HashSet<BoxFragment>? _isolatedGroups;

    /// <summary>The stacking contexts that contain a stacking context with a <c>backdrop-filter</c> (the backdrop of the filter is painted in them or below them).</summary>
    private HashSet<BoxFragment>? _backdropFilterAncestors;

    /// <summary>The root of the fragment tree when the background of the canvas is painted in the stacking context of the root element (see <see cref="HasCanvasEffects"/>).</summary>
    private BoxFragment? _deferredCanvasBackground;

    /// <summary>The viewport in the coordinates of the fragment tree (scrolled), for the fixed backgrounds.</summary>
    private PhysicalRect _viewportRect;

    // The time in milliseconds until the next frame of an animated image painted in the document, or null
    private double? _nextFrameDelay;

    /// <summary>The root of the fragment tree of the document.</summary>
    private BoxFragment? _rootFragment;

    /// <summary>The painting order of the document with the markers of the stacking contexts, for the images of the elements captured by a view transition.</summary>
    private List<PaintItem>? _paintOrder;

    /// <summary>
    /// Whether the painting is the image of an element captured by a view transition (https://drafts.csswg.org/css-view-transitions-1/#capture-the-image):
    /// the view transition layer is not painted.
    /// </summary>
    private bool _isViewTransitionCapture;

    /// <summary>
    /// Whether the elements captured in a view transition are not painted (https://drafts.csswg.org/css-view-transitions-1/#capture-rendering-characteristics,
    /// https://drafts.csswg.org/css-view-transitions-1/#view-transition-painting-order): the image of an element does not show its
    /// captured descendants, and the document does not show the captured elements while the transition is animating (their images
    /// are painted by the pseudo-elements of the view transition layer).
    /// </summary>
    private bool _skipsCapturedElements;

    /// <summary>Whether the rendering of the root element of a scoped view transition is suppressed (see <see cref="GetFrozenFrame"/>).</summary>
    private readonly bool _hasFrozenViewTransitionScopes;

    /// <summary>The element whose image is painted (see <see cref="_isViewTransitionCapture"/>), which is painted although it is captured.</summary>
    private Element? _captureElement;

    /// <summary>The pseudo-element of <see cref="_captureElement"/> whose image is painted, or null for the element.</summary>
    private string? _capturePseudoElement;

    /// <summary>
    /// The fragment of the element whose image is painted, or null for the document element: the root of the painting, whose
    /// transform and whose ancestors (their clips, their effects) are ignored.
    /// </summary>
    private BoxFragment? _captureRoot;

    private Painter(LayoutEngine engine, Document document, PhysicalOffset rootOrigin)
    {
        _engine = engine;
        _document = document;
        _group = new Group(fragment: null, rootOrigin, parent: null, baseSaveCount: 0, saveCount: 0);
        var ratio = _document.GetStyleEngine().Environment.DevicePixelRatio;
        _devicePixelRatio = ratio > 0 && double.IsFinite(ratio) ? ratio : 1;
        _hasFrozenViewTransitionScopes = document.HasFrozenViewTransitionScopes;
        _paintsScrollbars = document.BrowsingContext is { } browsingContext && (browsingContext.Top.Options.Viewport ?? ViewportOptions.Default).ScrollbarsVisible;
    }

    /// <summary>
    /// Snaps a coordinate to the nearest device pixel (in the coordinates of the current group, see <see cref="SnapToDevicePixels(PhysicalRect)"/>).
    /// </summary>
    private double SnapToDevicePixels(double value) => Math.Floor((value * _devicePixelRatio) + 0.5) / _devicePixelRatio;

    /// <summary>
    /// Snaps a rectangle to the device pixels (the pixel snapping of the browsers): its edges are rounded to the nearest device
    /// pixel, so the boxes laid out at fractional positions are painted with sharp edges, the adjacent boxes stay adjacent, and a
    /// box and the same box translated by a fractional offset cover the same pixels. A side that is not empty keeps at least one
    /// device pixel. The geometry of the layout (and of the CSSOM View) is not changed: only the painted rectangles are.
    /// </summary>
    /// <remarks>
    /// The snapping is done in the coordinates of the group (the nearest transformed stacking context), with the device pixel
    /// ratio of the viewport (<see cref="ViewportOptions.DevicePixelRatio"/>): a screenshot at another ratio is
    /// painted with the same snapped rectangles.
    /// </remarks>
    private PhysicalRect SnapToDevicePixels(PhysicalRect rect)
    {
        var left = SnapToDevicePixels(rect.Left);
        var top = SnapToDevicePixels(rect.Top);
        var right = SnapToDevicePixels(rect.Right);
        var bottom = SnapToDevicePixels(rect.Bottom);
        var pixel = 1 / _devicePixelRatio;
        if (right <= left && rect.Width > 0)
        {
            right = left + pixel;
        }

        if (bottom <= top && rect.Height > 0)
        {
            bottom = top + pixel;
        }

        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>Returns the border box of a fragment at an offset, snapped to the device pixels.</summary>
    private PhysicalRect GetSnappedBorderBox(BoxFragment fragment, PhysicalOffset offset) => SnapToDevicePixels(GetDecorationBox(fragment).Translate(offset));

    /// <summary>
    /// Returns the box that the background, the borders and the shadows of a fragment are painted in: its border box; the one of
    /// a fieldset starts where its block-start border is painted, centered on its rendered legend (see <see cref="FieldsetState"/>).
    /// </summary>
    private static PhysicalRect GetDecorationBox(BoxFragment fragment)
    {
        var rect = fragment.BorderBoxRect;
        if (fragment.Box.LayoutState is not FieldsetState { HasLegend: true, BorderOffset: > 0 and var borderOffset } || !fragment.IsFirstFragment)
            return rect;

        var writingDirection = fragment.WritingDirection;
        borderOffset = Math.Min(borderOffset, writingDirection.IsHorizontal ? rect.Height : rect.Width);
        return writingDirection.BlockStart switch
        {
            PhysicalSide.Top => new PhysicalRect(rect.X, rect.Y + borderOffset, rect.Width, rect.Height - borderOffset),
            PhysicalSide.Left => new PhysicalRect(rect.X + borderOffset, rect.Y, rect.Width - borderOffset, rect.Height),
            PhysicalSide.Bottom => new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height - borderOffset),
            _ => new PhysicalRect(rect.X, rect.Y, rect.Width - borderOffset, rect.Height),
        };
    }

    /// <summary>Returns the offset of the stitched box of a fragment of a box split by the block fragmentation (<see cref="BoxFragment.StitchedBox"/>) from the fragment.</summary>
    private static PhysicalOffset GetStitchedOrigin(BoxFragment fragment, BoxFragment stitched)
    {
        return fragment.WritingDirection.BlockStart switch
        {
            PhysicalSide.Top => new PhysicalOffset(0, -fragment.StitchedOffset),
            PhysicalSide.Left => new PhysicalOffset(-fragment.StitchedOffset, 0),
            PhysicalSide.Bottom => new PhysicalOffset(0, fragment.Size.Height + fragment.StitchedOffset - stitched.Size.Height),
            _ => new PhysicalOffset(fragment.Size.Width + fragment.StitchedOffset - stitched.Size.Width, 0),
        };
    }

    /// <summary>
    /// Returns the area where the decorations of the stitched box of a fragment are painted: the border box of the fragment,
    /// without limit on the sides where the box is not broken (the shadows are not painted at the breaks).
    /// </summary>
    private PhysicalRect GetSliceClip(BoxFragment fragment, BoxFragment stitched, PhysicalOffset offset)
    {
        const double Infinite = 1e7;
        var rect = GetSnappedBorderBox(fragment, offset);
        var writingDirection = fragment.WritingDirection;
        var blockSize = writingDirection.ToLogical(fragment.Size).Block;
        var stitchedBlockSize = writingDirection.ToLogical(stitched.Size).Block;
        var isBrokenAtStart = fragment.StitchedOffset > 0.01;
        var isBrokenAtEnd = fragment.StitchedOffset + blockSize < stitchedBlockSize - 0.01;
        var (left, top, right, bottom) = (rect.Left, rect.Top, rect.Right, rect.Bottom);
        var startSide = writingDirection.BlockStart;
        var endSide = WritingDirection.Opposite(startSide);
        foreach (var side in (ReadOnlySpan<PhysicalSide>)[PhysicalSide.Top, PhysicalSide.Right, PhysicalSide.Bottom, PhysicalSide.Left])
        {
            if ((side == startSide && isBrokenAtStart) || (side == endSide && isBrokenAtEnd))
                continue;

            switch (side)
            {
                case PhysicalSide.Top:
                    top -= Infinite;
                    break;

                case PhysicalSide.Right:
                    right += Infinite;
                    break;

                case PhysicalSide.Bottom:
                    bottom += Infinite;
                    break;

                default:
                    left -= Infinite;
                    break;
            }
        }

        return new PhysicalRect(left, top, right - left, bottom - top);
    }

    /// <summary>A stacking context being painted: the origin of the coordinates of its contents and the clips applied in it.</summary>
    private sealed class Group(BoxFragment? fragment, PhysicalOffset origin, Group? parent, int baseSaveCount, int saveCount)
    {
        /// <summary>The root of the stacking context, or null for the group of the document (the stacking contexts of the top layer are in it).</summary>
        public BoxFragment? Fragment { get; } = fragment;

        /// <summary>The position in the fragment tree (<see cref="Fragment.GetOffsetInRoot"/>) of the origin of the coordinates of the group: the border box of the nearest transformed stacking context.</summary>
        public PhysicalOffset Origin { get; } = origin;

        public Group? Parent { get; } = parent;

        /// <summary>The number of saves before the stacking context.</summary>
        public int BaseSaveCount { get; } = baseSaveCount;

        /// <summary>The number of saves of the stacking context (its transform and layer), before its clips.</summary>
        public int SaveCount { get; set; } = saveCount;

        /// <summary>The clips applied in the group (one save each), from the outermost.</summary>
        public List<ClipEntry> Clips { get; } = [];

        /// <summary>The number of saves before the filter layer, where the masks are painted at the end of the stacking context, or -1 when it has no mask.</summary>
        public int MaskSaveCount { get; set; } = -1;

        /// <summary>The masks of the stacking context (its clip path referencing a clipPath element, its mask layers), painted at its end.</summary>
        public PendingMask? Mask { get; set; }

        /// <summary>Whether a 3D transform was applied by the stacking context or one of its ancestors (the canvas uses a 3D matrix).</summary>
        public bool Has3DTransform { get; init; }

        /// <summary>The fragments whose outline is painted at the end of the stacking context.</summary>
        public List<BoxFragment>? Outlines { get; set; }

        /// <summary>
        /// Whether the group is the stacking context of the <c>::view-transition</c> pseudo-element of a scoped view transition,
        /// painted in a group that only has the clips of the ancestors and the transform of its root element: the parent group,
        /// which ends with it.
        /// </summary>
        public bool EndsParentGroup { get; set; }

        /// <summary>
        /// Whether the stacking context applied a transform (its contents are not in the coordinates of the viewport), or declares
        /// one with <c>will-change</c>: the fixed backgrounds in it are not fixed to the viewport.
        /// </summary>
        public bool IsTransformed { get; init; }
    }

    /// <summary>
    /// Paints the fragment tree of a document into a display list in the coordinates of the viewport: the background of the
    /// canvas, then the fragments in painting order.
    /// </summary>
    /// <param name="engine">The layout of the document.</param>
    /// <param name="document">The document.</param>
    /// <param name="root">The root of the fragment tree (the fragment of the initial containing block).</param>
    /// <param name="viewportScrollOffset">The scroll position of the viewport.</param>
    /// <param name="baseColor">The color below the canvas (white for a top-level document, transparent for a nested document).</param>
    public static DisplayList Paint(LayoutEngine engine, Document document, BoxFragment root, PhysicalOffset viewportScrollOffset, DisplayColor baseColor)
        => Paint(engine, document, root, viewportScrollOffset, baseColor, out _);

    /// <summary>
    /// Paints the fragment tree of a document (see <see cref="Paint(LayoutEngine, Document, BoxFragment, PhysicalOffset, DisplayColor)"/>),
    /// and returns the time in milliseconds after which the painting changes without any change of the document: the next frame
    /// of an animated image, or null.
    /// </summary>
    public static DisplayList Paint(LayoutEngine engine, Document document, BoxFragment root, PhysicalOffset viewportScrollOffset, DisplayColor baseColor, out double? nextFrameDelay)
        => Paint(engine, document, root, viewportScrollOffset, baseColor, capture: false, hidesAnimatedCaret: false, out nextFrameDelay, out _);

    /// <summary>
    /// Paints the fragment tree of a document (see <see cref="Paint(LayoutEngine, Document, BoxFragment, PhysicalOffset, DisplayColor, out double?)"/>)
    /// in a phase of the blinking of its caret (see <see cref="PaintedCaret"/>).
    /// </summary>
    /// <param name="hidesAnimatedCaret">Whether a caret that blinks is in its hidden phase: it is not painted.</param>
    /// <param name="hasAnimatedCaret">Whether the document displays a caret that blinks (painted or hidden).</param>
    public static DisplayList Paint(LayoutEngine engine, Document document, BoxFragment root, PhysicalOffset viewportScrollOffset, DisplayColor baseColor, bool hidesAnimatedCaret, out double? nextFrameDelay, out bool hasAnimatedCaret)
        => Paint(engine, document, root, viewportScrollOffset, baseColor, capture: false, hidesAnimatedCaret, out nextFrameDelay, out hasAnimatedCaret);

    /// <summary>
    /// Paints the fragment tree of a printed page (the content of its page area, see <see cref="LayoutPage"/>) in the coordinates of
    /// the page box: the painting is snapped to the device pixels of the page, as the document would be painted at the
    /// position of the page area in the page (the page area may be at a fractional position: a margin of <c>2cm</c>).
    /// </summary>
    /// <param name="engine">The layout of the document.</param>
    /// <param name="document">The document.</param>
    /// <param name="root">The fragment of the page area.</param>
    /// <param name="pageAreaOrigin">The position of the page area in the page box.</param>
    internal static DisplayList PaintPageContent(LayoutEngine engine, Document document, BoxFragment root, PhysicalOffset pageAreaOrigin)
        => Paint(engine, document, root, PhysicalOffset.Zero, DisplayColor.Transparent, capture: false, hidesAnimatedCaret: false, out _, out _, -pageAreaOrigin);

    /// <summary>
    /// Paints the image of the document element captured by a view transition (https://drafts.csswg.org/css-view-transitions-1/#capture-the-image):
    /// the document "including its canvas background and any top layer content", on a canvas the size of the snapshot
    /// containing block, without the view transition layer nor the other captured elements. As in the browsers, the canvas has
    /// the color the document is drawn over (<paramref name="baseColor"/>: the canvas color of a top-level document), so the
    /// image of a top-level document is opaque.
    /// </summary>
    public static DisplayList PaintViewTransitionRootCapture(LayoutEngine engine, Document document, BoxFragment root, PhysicalOffset viewportScrollOffset, DisplayColor baseColor)
        => Paint(engine, document, root, viewportScrollOffset, baseColor, capture: true, hidesAnimatedCaret: false, out _, out _);

    /// <summary>
    /// Paints the image of an element captured by a view transition (https://drafts.csswg.org/css-view-transitions-1/#capture-the-image),
    /// in the coordinates of its border box: its stacking context with its effects (opacity, filters, clips), without its
    /// transform, without the clips and the effects of its ancestors, and without its descendants that are captured too.
    /// </summary>
    /// <param name="snapOrigin">The position of the origin of the border box in the display list (the fractional part of its position in the viewport: the painting is snapped to the device pixels of the viewport).</param>
    /// <param name="order">The painting order of the document with the markers of the stacking contexts (<see cref="PaintOrder.Collect"/>), or null to compute it.</param>
    public static DisplayList PaintViewTransitionCapture(LayoutEngine engine, Document document, BoxFragment fragment, PhysicalOffset snapOrigin, List<PaintItem>? order = null)
    {
        var root = fragment;
        while (root.Parent is BoxFragment parent)
        {
            root = parent;
        }

        var origin = fragment.GetOffsetInRoot() - snapOrigin;
        var painter = new Painter(engine, document, origin)
        {
            _rootFragment = root,
            _isViewTransitionCapture = true,
            _skipsCapturedElements = true,
            _captureElement = fragment.Box.Element,
            _capturePseudoElement = fragment.Box.PseudoElement,
            _captureRoot = fragment,
        };
        var builder = painter._builder;
        builder.Save();
        painter._group = new Group(fragment: null, origin, parent: null, baseSaveCount: builder.SaveCount, saveCount: builder.SaveCount);
        var scroll = engine.ViewportScrollState.Offset;
        painter._viewportRect = new PhysicalRect(root.GetOffsetInRoot().X + scroll.X, root.GetOffsetInRoot().Y + scroll.Y, root.Size.Width, root.Size.Height);

        // The stacking context of the element in the painting order of the document: it contains the positioned descendants whose
        // containing block is outside of the element (e.g. a fixed positioned descendant)
        var items = new List<PaintItem>();
        if (order is null)
        {
            order = [];
            PaintOrder.Collect(root, order, markers: true);
        }

        var begin = order.FindIndex(item => item.Marker == PaintItemMarker.BeginStackingContext && ReferenceEquals(item.Fragment, fragment));
        if (begin >= 0)
        {
            items.AddRange(order.GetRange(begin, FindEndOfStackingContext(order, begin) - begin + 1));
        }
        else
        {
            PaintOrder.CollectStackingContext(fragment, items, markers: true);
        }

        painter.CollectIsolatedGroups(items);
        painter.PaintItems(items);
        painter.PaintOutlines(painter._group);
        return builder.ToDisplayList(fragment.Size);
    }

    private static DisplayList Paint(LayoutEngine engine, Document document, BoxFragment root, PhysicalOffset viewportScrollOffset, DisplayColor baseColor, bool capture, bool hidesAnimatedCaret, out double? nextFrameDelay, out bool hasAnimatedCaret, PhysicalOffset rootOrigin = default)
    {
        // The display list of a document is the one of its viewport, with the gutters of its classic scrollbars: the scrollport
        // (the root fragment) is at its place in the viewport, the canvas extends under the gutters, and the scrollbars are
        // painted over the document. The image of the document captured by a view transition is the scrollport alone.
        var environment = document.GetStyleEngine().Environment;
        var viewportSize = capture ? root.Size : new PhysicalSize(Math.Max(root.Size.Width, environment.Width), Math.Max(root.Size.Height, environment.Height));
        var origin = capture ? root.GetOffsetInRoot() : rootOrigin;
        var hasGutters = !capture && (root.Offset != PhysicalOffset.Zero || root.Size != viewportSize);
        var painter = new Painter(engine, document, origin) { _rootFragment = root };
        var builder = painter._builder;

        // https://drafts.csswg.org/css-view-transitions-1/#view-transition-painting-order: "the boxes generated by any element with
        // captured in a view transition [...] are not painted (as if they had opacity: 0)" while the transition is animating
        var rootCaptured = false;
        if (capture)
        {
            painter._isViewTransitionCapture = true;
            painter._skipsCapturedElements = true;
            painter._captureElement = document.DocumentElement;
        }
        else if (document.IsViewTransitionAnimating && document.HasElementsCapturedInViewTransition)
        {
            painter._skipsCapturedElements = true;
            rootCaptured = document.DocumentElement is { } documentElement && document.IsCapturedInViewTransition(documentElement);
        }

        if (!baseColor.IsTransparent)
        {
            builder.Add(new DrawPaintItem(new DisplayPaint(baseColor)));
        }

        var documentSaveCount = builder.SaveCount;
        builder.Save();
        builder.Transform(DisplayMatrix.Translation(-painter.SnapToDevicePixels(viewportScrollOffset.X), -painter.SnapToDevicePixels(viewportScrollOffset.Y)));
        painter._group = new Group(fragment: null, origin, parent: null, baseSaveCount: builder.SaveCount, saveCount: builder.SaveCount);
        painter._viewportRect = new PhysicalRect(root.GetOffsetInRoot().X + viewportScrollOffset.X, root.GetOffsetInRoot().Y + viewportScrollOffset.Y, root.Size.Width, root.Size.Height);
        var items = new List<PaintItem>();
        PaintOrder.Collect(root, items, markers: true);
        painter._paintOrder = items;
        painter.PrepareCaret(items, hidesAnimatedCaret);

        // The image of the captured document element shows the whole document (its canvas background and the top layer): only the
        // view transition layer is painted
        if (rootCaptured)
        {
            items = GetViewTransitionLayer(items);
        }

        painter.CollectIsolatedGroups(items);

        // https://drafts.fxtf.org/filter-effects/#FilterProperty, https://drafts.fxtf.org/css-masking/: the effects of the root
        // element (its filters, clip path, masks, opacity) apply to the background of the canvas, which is then painted in its
        // stacking context (also when it is an isolated group: its blending descendants blend with the background of the canvas)
        if (!rootCaptured)
        {
            if (GetCanvasBackgroundStyle(root, out var rootElement) is not null && (HasCanvasEffects(rootElement) || painter._isolatedGroups?.Contains(rootElement) == true))
            {
                painter._deferredCanvasBackground = root;
            }
            else
            {
                painter.PaintCanvasBackground(root);
            }
        }

        // The document is clipped to the scrollport: the gutters of the scrollbars of the viewport only show the canvas (a
        // background of the canvas that has the effects of the root element is painted with it, in the scrollport)
        if (hasGutters)
        {
            builder.Add(new ClipRectItem(painter.SnapToDevicePixels(painter._viewportRect.Translate(-origin))));
        }

        painter.PaintItems(items);
        painter.PaintOutlines(painter._group);
        if (!capture && painter._paintsScrollbars)
        {
            // The scrollbars of the viewport, in the coordinates of the viewport
            builder.RestoreTo(documentSaveCount);
            painter.PaintViewportScrollbars(root, viewportSize);
        }

        nextFrameDelay = painter._nextFrameDelay;
        hasAnimatedCaret = painter._hasAnimatedCaret;
        return builder.ToDisplayList(viewportSize);
    }

    /// <summary>Returns the items of the stacking context of the <c>::view-transition</c> pseudo-element (the view transition layer).</summary>
    private static List<PaintItem> GetViewTransitionLayer(List<PaintItem> items)
    {
        var result = new List<PaintItem>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Marker == PaintItemMarker.BeginStackingContext && items[i].Fragment is BoxFragment { Box.IsViewTransitionRoot: true })
            {
                var end = FindEndOfStackingContext(items, i);
                result.AddRange(items.GetRange(i, end - i + 1));
                i = end;
            }
        }

        return result;
    }

    /// <summary>
    /// Whether a stacking context is not painted: the view transition layer in the image of a captured element, and the
    /// elements captured in a view transition (see <see cref="_skipsCapturedElements"/>).
    /// </summary>
    private bool SkipsStackingContext(BoxFragment fragment)
    {
        var box = fragment.Box;
        if (box.IsViewTransitionRoot)
            return _isViewTransitionCapture;

        return _skipsCapturedElements && box is { Element: { } element, IsAnonymous: false } && box.Kind != ContainerBoxKind.Viewport
            && !(ReferenceEquals(element, _captureElement) && box.PseudoElement == _capturePseudoElement) && _document.IsCapturedInViewTransition(element, box.PseudoElement);
    }

    /// <summary>
    /// Returns the frame of an animated image displayed now (<see cref="PaintImage.GetDisplayedFrame"/>), and records when the
    /// painting must be done again to display its next frame. The images are drawn as they are without rendering backend.
    /// </summary>
    internal PaintImage GetDisplayedFrame(PaintImage image)
    {
        if (_document.RenderingBackend is not { } backend)
            return image;

        var frame = image.GetDisplayedFrame(backend, out var nextChange);
        if (nextChange is { } delay && (_nextFrameDelay is null || delay < _nextFrameDelay))
        {
            _nextFrameDelay = delay;
        }

        return frame;
    }

    /// <summary>Paints items of a painting order collected with the markers of the stacking contexts.</summary>
    private void PaintItems(List<PaintItem> items) => PaintItems(items, 0, items.Count);

    private void PaintItems(List<PaintItem> items, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var item = items[i];
            switch (item.Marker)
            {
                case PaintItemMarker.BeginStackingContext:
                {
                    var fragment = (BoxFragment)item.Fragment;

                    // https://drafts.csswg.org/css-view-transitions-2/#scoped-vt-layout: the ::view-transition pseudo-element of a
                    // scoped view transition "paints separately from, and on top of, the painting of the root element": the
                    // effects and the clips of the root element do not apply to it, its transform does
                    if (fragment.Box.IsScopedViewTransitionRoot && fragment.Parent is BoxFragment scope)
                    {
                        BeginStackingContext(scope, planeMatrix: null, transformOnly: true);
                        BeginStackingContext(fragment);
                        _group.EndsParentGroup = true;
                        break;
                    }

                    if ((_skipsCapturedElements || _isViewTransitionCapture) && SkipsStackingContext(fragment))
                    {
                        i = FindEndOfStackingContext(items, i);
                        break;
                    }

                    // "Rendering is paused within the view transition root element" of a scoped view transition: the frame of
                    // its old state is painted in place of its stacking context, with its transform
                    if (_hasFrozenViewTransitionScopes && GetFrozenFrame(fragment) is { } frozen)
                    {
                        BeginStackingContext(fragment, planeMatrix: null, transformOnly: true);
                        var position = fragment.GetOffsetInRoot() - _group.Origin;
                        _builder.Save();
                        _builder.Transform(DisplayMatrix.Translation(position.X - frozen.Origin.X, position.Y - frozen.Origin.Y));
                        _builder.AddList(frozen.Content);
                        _builder.Restore();
                        EndStackingContext();
                        i = FindEndOfStackingContext(items, i);
                        break;
                    }

                    // The root of the image of a captured element is painted without its transform
                    if (ReferenceEquals(fragment, _captureRoot))
                    {
                        BeginStackingContext(fragment);
                        break;
                    }

                    if (Is3DRenderingContextRoot(fragment))
                    {
                        var endIndex = FindEndOfStackingContext(items, i);
                        Paint3DRenderingContext(items, i, endIndex);
                        i = endIndex;
                        break;
                    }

                    // https://drafts.csswg.org/css-transforms-2/#backface-visibility-property: a stacking context whose back face is
                    // visible is not painted (with its descendants, which are flattened into its plane); nor a stacking context
                    // whose transform is not invertible (https://drafts.csswg.org/css-transforms-1/#transform-function-lists)
                    var matrix = fragment.Box.Kind == ContainerBoxKind.Viewport ? null : LayoutTransforms.GetMatrixWithPerspective(fragment);
                    if (IsBackFaceHidden(fragment, matrix) || (matrix is not null && !LayoutTransforms.IsInvertible(matrix)))
                    {
                        i = FindEndOfStackingContext(items, i);
                        break;
                    }

                    BeginStackingContext(fragment);
                    break;
                }

                case PaintItemMarker.EndStackingContext:
                    EndStackingContext();
                    break;

                case PaintItemMarker.Outlines:
                    PaintOutlines(_group);
                    break;

                default:
                    // The band of a block split out of inline boxes (hit tested as the inline boxes): as in the browsers, the
                    // backgrounds of the inline boxes are not painted behind the block (css/CSS2/box-display/block-in-inline-003.xht)
                    if (item.IsSplitInlineBand)
                        break;

                    if (item.Part == PaintItemPart.Backplate)
                    {
                        PaintBackplates((BoxFragment)item.Fragment);
                        break;
                    }

                    PaintFragment(item.Fragment, item.Truncation, item.Part);
                    PaintPendingCollapsedBorders(item.Fragment);
                    if (_caretAnchor is not null)
                    {
                        PaintCaretAfter(item);
                    }

                    break;
            }
        }

        // The stacking contexts that participate in a 3D rendering context are painted by Paint3DRenderingContext: another one with
        // transform-style: preserve-3d establishes a 3D rendering context
        static bool Is3DRenderingContextRoot(BoxFragment fragment) => LayoutTransforms.UsesPreserve3D(fragment.Box);
    }

    /// <summary>
    /// Collects the stacking contexts whose child stacking contexts have a <c>mix-blend-mode</c> (<see cref="_isolatedGroups"/>), the
    /// ones that contain a stacking context with a <c>backdrop-filter</c> (<see cref="_backdropFilterAncestors"/>), and whether a
    /// box has a clip rectangle (<see cref="_hasCssClips"/>).
    /// </summary>
    private void CollectIsolatedGroups(List<PaintItem> items)
    {
        var stack = new Stack<BoxFragment>();
        foreach (var item in items)
        {
            switch (item.Marker)
            {
                case PaintItemMarker.BeginStackingContext:
                {
                    var fragment = (BoxFragment)item.Fragment;
                    if (stack.Count > 0 && fragment.Box is { IsAnonymous: false } box && box.Kind != ContainerBoxKind.Viewport && box.Style.GetComputedValue("mix-blend-mode") is not ("normal" or ""))
                    {
                        (_isolatedGroups ??= new(ReferenceEqualityComparer.Instance)).Add(stack.Peek());
                    }

                    if (stack.Count > 0 && fragment.Box is { IsAnonymous: false } filtered && filtered.Kind != ContainerBoxKind.Viewport && FilterEffects.HasFilter(filtered.Style, "backdrop-filter"))
                    {
                        _backdropFilterAncestors ??= new(ReferenceEqualityComparer.Instance);
                        foreach (var ancestor in stack)
                        {
                            _backdropFilterAncestors.Add(ancestor);
                        }
                    }

                    stack.Push(fragment);
                    break;
                }

                case PaintItemMarker.EndStackingContext:
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    break;

                case PaintItemMarker.Outlines:
                    break;

                default:
                    _hasCssClips |= item.Fragment is BoxFragment boxFragment && HasCssClip(boxFragment);
                    break;
            }
        }
    }

    /// <summary>Returns the index of the marker of the end of the stacking context that starts at <paramref name="begin"/>.</summary>
    private static int FindEndOfStackingContext(List<PaintItem> items, int begin)
    {
        var depth = 0;
        for (var i = begin; i < items.Count; i++)
        {
            switch (items[i].Marker)
            {
                case PaintItemMarker.BeginStackingContext:
                    depth++;
                    break;

                case PaintItemMarker.EndStackingContext:
                    depth--;
                    if (depth == 0)
                        return i;

                    break;
            }
        }

        return items.Count - 1;
    }

    /// <summary>
    /// Starts a stacking context: its clips (the ancestors that clip it in the parent stacking context), its transform (with the
    /// perspective of its parent, flattened in the plane of its parent unless it is in a 3D rendering context), then its effects
    /// (https://drafts.fxtf.org/filter-effects/#placement: the filters, the clip path, the masks, then the opacity and the blend mode;
    /// <see cref="ApplyEffects"/>).
    /// </summary>
    /// <param name="fragment">The root of the stacking context.</param>
    /// <param name="planeMatrix">
    /// The transform of a plane of a 3D rendering context in the coordinates of the current group (the accumulated 3D transform),
    /// which replaces the transform of the fragment, or null.
    /// </param>
    /// <param name="transformOnly">
    /// Whether only the clips of the ancestors and the transform of the fragment are applied, not its own clips nor its effects:
    /// the group of what is painted for a box apart from its stacking context (the pseudo-element tree of a scoped view
    /// transition, the frame of its root element while its rendering is suppressed).
    /// </param>
    private void BeginStackingContext(BoxFragment fragment, double[]? planeMatrix = null, bool transformOnly = false)
    {
        // The image of an element captured by a view transition: "if the referenced element has a transform applied to it (or its
        // ancestors), then the transform is ignored", "effects applied to the element from its ancestors are ignored"
        var isCaptureRoot = ReferenceEquals(fragment, _captureRoot);
        if (!isCaptureRoot)
        {
            SyncClips(GetClipChain(fragment, includeSelf: false, includeSelfCssClip: false));
        }

        var baseSaveCount = _builder.SaveCount;
        _builder.Save();

        var offset = fragment.GetOffsetInRoot();
        var origin = _group.Origin;
        var box = fragment.Box;
        var has3D = _group.Has3DTransform;
        var isTransformed = false;
        if (planeMatrix is not null)
        {
            _builder.Transform(DisplayMatrix.FromMatrix4x4(planeMatrix));
            origin = offset;
            has3D = true;
            isTransformed = true;
        }
        else if (!isCaptureRoot && box.Kind != ContainerBoxKind.Viewport && LayoutTransforms.GetMatrixWithPerspective(fragment) is { } matrix)
        {
            if (IsPlaneTranslation(matrix, out var translation) && !LayoutTransforms.UsesPreserve3D(box))
            {
                // A transform that only translates the plane of the box (or leaves it in place, e.g. translateZ(0) with the
                // perspective of the parent) moves the box as a relative positioning does: the stacking context is painted in the
                // coordinates of its parent at its translated position, so that its boxes are snapped to the device pixels as the
                // boxes that are not transformed (as in the browsers; in a transformed group, they would be snapped from the
                // fractional position of the box). The root of a 3D rendering context keeps its matrix: its perspective and the
                // depth of its plane apply to the planes of its descendants
                origin = new PhysicalOffset(origin.X - translation.X, origin.Y - translation.Y);
            }
            else
            {
                // https://drafts.csswg.org/css-transforms-2/#transform-3d-rendering: a 3D transform outside of a 3D rendering
                // context is flattened into the plane of its parent (the depth of the points of its parent is dropped); the
                // stacking contexts of the 3D rendering contexts are painted with their accumulated transform (planeMatrix)
                var is3D = LayoutTransforms.HasZOutput(matrix);
                if (is3D && has3D)
                {
                    _builder.Transform(Flattening);
                }

                // https://drafts.csswg.org/css-transforms-1/#transform-rendering: the transform is in the coordinates of the border box
                var local = offset - origin;
                _builder.Transform(DisplayMatrix.Translation(local.X, local.Y).PreConcat(DisplayMatrix.FromMatrix4x4(matrix)));
                origin = offset;
                has3D |= is3D;
            }

            isTransformed = true;
        }
        else if (box.Kind != ContainerBoxKind.Viewport && !box.IsAnonymous && !box.IsInlineBox
            && Array.Exists(box.Style.Layout.WillChangeProperties, static property => property is "transform" or "translate" or "rotate" or "scale" or "offset-path"))
        {
            // https://github.com/w3c/csswg-drafts/issues/6951: as in the browsers, a box that declares a transform with
            // will-change is treated as a transformed box by the fixed backgrounds
            isTransformed = true;
        }

        var group = new Group(fragment, origin, _group, baseSaveCount, _builder.SaveCount) { IsTransformed = isTransformed, Has3DTransform = has3D };
        _group = group;
        if (transformOnly)
            return;

        // https://drafts.fxtf.org/css-masking/#clip-property: the clip rectangle of the root of the stacking context clips the result
        // of its filters (as the clip paths)
        if (box.Kind != ContainerBoxKind.Viewport && HasCssClip(fragment))
        {
            AddCssClip(fragment, offset - origin);
        }

        var effectsSaveCount = _builder.SaveCount;
        if (box.Kind != ContainerBoxKind.Viewport && !box.IsAnonymous)
        {
            ApplyEffects(fragment, offset - origin, group);
        }

        // https://drafts.fxtf.org/compositing-1/#isolation: "an element that has a stacking context is an isolated group": the
        // stacking contexts whose contents blend are layers, so the blending does not see the content painted below them
        if (_builder.SaveCount == effectsSaveCount && _isolatedGroups is { } isolatedGroups && isolatedGroups.Contains(fragment))
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null) { Source = box.Element });
        }

        group.SaveCount = _builder.SaveCount;
        if (_deferredCanvasBackground is { } canvasRoot && box.Kind != ContainerBoxKind.Viewport && box.Style.IsRoot && box.PseudoElement is null && !box.IsAnonymous)
        {
            _deferredCanvasBackground = null;
            PaintCanvasBackground(canvasRoot);
        }
    }

    /// <summary>
    /// Whether a transform (a 4x4 matrix in column-major order) maps the points of the plane z = 0 to themselves translated in the
    /// plane, once flattened (the depth of the result is dropped): the identity, a 2D translation, a translation along the z axis
    /// without perspective, or the perspective of the parent alone.
    /// </summary>
    private static bool IsPlaneTranslation(double[] matrix, out PhysicalOffset translation)
    {
        translation = new PhysicalOffset(matrix[12], matrix[13]);
        return matrix[0] == 1 && matrix[1] == 0 && matrix[3] == 0 && matrix[4] == 0 && matrix[5] == 1 && matrix[7] == 0 && matrix[15] == 1
            && double.IsFinite(matrix[12]) && double.IsFinite(matrix[13]);
    }

    /// <summary>
    /// Whether the transform of a box only translates its plane: its stacking context is then painted at its translated position
    /// in the coordinates of its parent, where its boxes are snapped to the device pixels (see <see cref="BeginStackingContext"/>).
    /// </summary>
    internal static bool IsPlaneTranslation(BoxFragment fragment, out PhysicalOffset translation)
    {
        translation = default;
        return fragment.Box.Kind != ContainerBoxKind.Viewport && LayoutTransforms.GetMatrixWithPerspective(fragment) is { } matrix
            && IsPlaneTranslation(matrix, out translation) && !LayoutTransforms.UsesPreserve3D(fragment.Box);
    }

    /// <summary>
    /// Whether the effects of the root element apply to the background of the canvas: it has a filter, a clip path, a mask, an opacity
    /// or a blend mode, and no transform (the background of the canvas is not transformed).
    /// </summary>
    private static bool HasCanvasEffects(BoxFragment rootElement)
    {
        var style = rootElement.Box.Style;
        if (style.Layout.HasTransform)
            return false;

        return GetOpacity(style) < 1 || FilterEffects.HasFilter(style) || style.GetComputedValue("clip-path") is not ("none" or "")
            || style.GetComputedValue("mask-image") is not ("none" or "") || style.GetComputedValue("mix-blend-mode") is not ("normal" or "");
    }

    /// <summary>The matrix that projects the points on the plane z = 0 (the flattening of a 3D transform).</summary>
    private static DisplayMatrix Flattening { get; } = DisplayMatrix.Identity with { Matrix3d = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1] };

    private void EndStackingContext()
    {
        var group = _group;
        PaintOutlines(group);
        if (group.Mask is { } mask)
        {
            // The masks apply to the result of the filters (the filter layer is composited), before the opacity
            _builder.RestoreTo(group.MaskSaveCount);
            group.Clips.Clear();
            PaintMask(mask);
        }

        _builder.RestoreTo(group.BaseSaveCount);
        _group = group.Parent!;
        if (group.EndsParentGroup)
        {
            EndStackingContext();
        }
    }

    /// <summary>
    /// Returns the frame displayed in place of the stacking context of a box: the one of the root element of a scoped view
    /// transition whose rendering is suppressed (<see cref="ViewTransition.FrozenFrame"/>), or null. The image of the element
    /// itself (its old state) is painted from its boxes.
    /// </summary>
    private ViewTransitionFrozenFrame? GetFrozenFrame(BoxFragment fragment)
    {
        if (fragment.Box is not { IsAnonymous: false, PseudoElement: null, Element: { } element } box || box.Kind == ContainerBoxKind.Viewport || ReferenceEquals(fragment, _captureRoot))
            return null;

        return _document.GetScopedViewTransition(element)?.FrozenFrame;
    }

    /// <summary>
    /// Paints the pending outlines of the fragments of a stacking context: at the end of the in-flow content of the stacking
    /// context or of a positioned box (<see cref="PaintItemMarker.Outlines"/>), or at its end
    /// (https://drafts.csswg.org/css2/#elaborate-stacking-contexts, step 10).
    /// </summary>
    private void PaintOutlines(Group group)
    {
        if (group.Outlines is not { } outlines)
            return;

        group.Outlines = null;
        foreach (var fragment in outlines)
        {
            SyncClips(GetClipChain(fragment, includeSelf: false));
            PaintOutline(fragment, fragment.GetOffsetInRoot() - _group.Origin);
        }
    }

    private void PaintFragment(Fragment fragment, LineTruncation? truncation, PaintItemPart part = PaintItemPart.All)
    {
        switch (fragment)
        {
            case TextFragment text:
                SyncClips(GetClipChain(text, includeSelf: false));
                var clipped = ApplyTruncationClip(truncation);
                PaintText(text, text.GetOffsetInRoot() - _group.Origin);
                if (clipped)
                {
                    _builder.Restore();
                }

                break;

            case BoxFragment box:
                PaintBox(box, truncation, part);
                break;

            case LineBoxFragment line:
                PaintFirstLineBackground(line);
                break;
        }
    }

    /// <summary>
    /// Paints the background of the <c>::first-line</c> pseudo-element (https://drafts.csswg.org/css-backgrounds/#first-line-background:
    /// "The ::first-line pseudo-element is like an inline-level element for the purposes of the background") of a horizontal
    /// line: as the background of an inline box that wraps the content of the line, its color and its images cover the content
    /// area of the root inline box (the ascent and the descent of its font) along the in-flow content of the line (not the
    /// outside marker), which is also the positioning area of the images.
    /// </summary>
    private void PaintFirstLineBackground(LineBoxFragment line)
    {
        if (line.Style is not { } style || !style.Layout.IsHorizontalWritingMode)
            return;

        var color = GetColor(style, "background-color");
        var hasImages = style.GetComputedValue("background-image") is not ("none" or "");
        if (color.IsTransparent && !hasImages)
            return;

        var (left, right) = (double.PositiveInfinity, double.NegativeInfinity);
        foreach (var child in line.ChildrenSpan)
        {
            if (child is BoxFragment { Box: { IsOutOfFlow: true } or { IsOutsideMarker: true } })
                continue;

            left = Math.Min(left, child.Offset.X);
            right = Math.Max(right, child.Offset.X + child.Size.Width);
        }

        if (right <= left)
            return;

        var metrics = style.Fonts.PrimaryFont.GetMetrics(style.Layout.FontSize);
        var offset = line.GetOffsetInRoot() - _group.Origin;
        SyncClips(GetClipChain(line, includeSelf: false));
        var rect = SnapToDevicePixels(new PhysicalRect(offset.X + left, offset.Y + (line.FirstLineBoxBaseline ?? line.Baseline) - metrics.Ascent, right - left, metrics.Ascent + metrics.Descent));
        if (!color.IsTransparent)
        {
            _builder.Add(new DrawRectItem(rect, new DisplayPaint(color)) { Source = style.Element });
        }

        if (!hasImages)
            return;

        var layers = GetBackgroundLayers(style);
        var blends = HasBlendModes(layers);
        if (blends)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null) { Source = style.Element });
        }

        for (var i = layers.Length - 1; i >= 0; i--)
        {
            PaintBackgroundLayer(fragment: null, style, layers[i], rect, border: default, padding: default, new RoundedRect(rect, default), blends);
        }

        if (blends)
        {
            _builder.Restore();
        }
    }

    /// <summary>
    /// Returns the style of the backgrounds, of the shadows and of the colors of the borders and of the outline of a fragment:
    /// the style of its box, or, for a fragment of an inline box on the first formatted line of a block container that has a
    /// <c>::first-line</c> style, the style of the box inheriting from the pseudo-element
    /// (https://drafts.csswg.org/css-pseudo-4/#first-line-inheritance: "the portion of a child box that occurs on the first
    /// line only inherits properties applicable to the ::first-line pseudo-element from the ::first-line pseudo-element", e.g.
    /// the <c>currentcolor</c> of its background), as the style of its text.
    /// </summary>
    private ComputedStyle GetDecorationStyle(BoxFragment fragment) => (fragment.Box.IsInlineBox ? GetFirstLineStyle(fragment) : null) ?? fragment.Box.Style;

    /// <summary>The styles of the inline boxes on the first formatted line of the block containers that have a <c>::first-line</c> style (see <see cref="GetFirstLineStyle"/>).</summary>
    private Dictionary<ContainerBox, ComputedStyle>? _firstLineStyles;

    /// <summary>Returns the style of an inline box on the first formatted line of a block container that has a <c>::first-line</c> style, or null for the other fragments.</summary>
    private ComputedStyle? GetFirstLineStyle(BoxFragment fragment)
    {
        var parentStyle = fragment.Parent switch
        {
            LineBoxFragment { Style: { PseudoElement: "first-line" } lineStyle } => lineStyle,
            BoxFragment { Box.IsInlineBox: true } parent => GetFirstLineStyle(parent),
            _ => null,
        };
        if (parentStyle is null)
            return null;

        _firstLineStyles ??= new Dictionary<ContainerBox, ComputedStyle>(ReferenceEqualityComparer.Instance);
        if (!_firstLineStyles.TryGetValue(fragment.Box, out var style))
        {
            style = fragment.Box.Style.WithParent(parentStyle);
            _firstLineStyles[fragment.Box] = style;
        }

        return style;
    }

    /// <summary>
    /// Clips the painting of the content of a line truncated by <c>text-overflow</c> (<see cref="LineTruncator"/>) to its visible
    /// range: the elided characters are not painted, nor the parts of the inline boxes behind the ellipses. Returns whether a clip
    /// was saved (the caller restores it).
    /// </summary>
    private bool ApplyTruncationClip(LineTruncation? truncation)
    {
        if (truncation is null)
            return false;

        var line = truncation.Line;
        _builder.Save();
        _builder.Add(new ClipRectItem(SnapToDevicePixels(truncation.GetContentClip(UnclippedExtent).Translate(line.GetOffsetInRoot() - _group.Origin))));
        return true;
    }

    /// <summary>
    /// Paints a box fragment (https://drafts.csswg.org/css2/#elaborate-stacking-contexts): its background and its borders, the
    /// gap decorations of a grid, flex or multi-column container, the graphical items of a MathML box, the contents of a replaced box (clipped
    /// to its overflow clip edge); its outline is painted at the end of its stacking context.
    /// </summary>
    private void PaintBox(BoxFragment fragment, LineTruncation? truncation = null, PaintItemPart part = PaintItemPart.All)
    {
        var box = fragment.Box;
        if (box.Kind == ContainerBoxKind.Viewport)
            return;

        // The replaced content of a block-level replaced box, painted after the backgrounds of the blocks and the floats
        if (part == PaintItemPart.ReplacedContent)
        {
            if (box.Style.Layout.Visibility == Visibility.Visible || IsSvgRootBox(box))
            {
                SyncClips(GetClipChain(fragment, includeSelf: true));
                PaintReplacedContent(fragment, fragment.GetOffsetInRoot() - _group.Origin);
            }

            return;
        }

        if (box.IsAnonymous)
        {
            // An anonymous table paints the backgrounds of its parts and its collapsed borders
            if (box.Kind == ContainerBoxKind.TableGrid)
            {
                PaintTableGrid(fragment);
            }
            else if (box.IsFieldsetContent && box.Style.Layout.Visibility == Visibility.Visible)
            {
                // The anonymous fieldset content box paints the gap decorations of the fieldset (the column rules) and its
                // scrollbars (it is the scroll container of the contents of the fieldset)
                SyncClips(GetClipChain(fragment, includeSelf: false));
                PaintGapDecorations(fragment, fragment.GetOffsetInRoot() - _group.Origin);
                if (_paintsScrollbars && fragment.ScrollbarsDisplayed != Scrollbars.None)
                {
                    PaintScrollbars(fragment, fragment.GetOffsetInRoot() - _group.Origin);
                }
            }

            return;
        }

        var style = box.Style;
        if (style.Layout.Visibility != Visibility.Visible)
        {
            // https://svgwg.org/svg2-draft/render.html#VisibilityControl: the visibility applies to each graphics element: the
            // visible descendants of a hidden svg element are painted (and the filters of its hidden descendants)
            if (part == PaintItemPart.All && IsSvgRootBox(box))
            {
                SyncClips(GetClipChain(fragment, includeSelf: true));
                PaintReplacedContent(fragment, fragment.GetOffsetInRoot() - _group.Origin);
            }

            return;
        }

        SyncClips(GetClipChain(fragment, includeSelf: false));
        var offset = fragment.GetOffsetInRoot() - _group.Origin;

        // The inline boxes of a truncated line are clipped to its visible content (they are not replaced)
        var clipped = box.IsInlineBox && ApplyTruncationClip(truncation);

        // The table wrapper box has the margins of the table: the table grid box paints the background and the borders. The
        // backgrounds of the rows, the row groups, the columns and the column groups are painted in the cells.
        if (box.Kind != ContainerBoxKind.TableWrapper && !IsTablePart(box))
        {
            var hasShadow = style.GetComputedValue("box-shadow") is not ("none" or "");
            if (hasShadow)
            {
                PaintSlicedBoxShadows(fragment, offset, inset: false);
            }

            PaintBackground(fragment, offset);
            if (hasShadow)
            {
                PaintSlicedBoxShadows(fragment, offset, inset: true);
            }

            PaintBorder(fragment, offset);
            if (box.LayoutState is FramesetState { Borders.Count: > 0 } frameset)
            {
                PaintFramesetBorders(fragment, offset, frameset);
            }

            if (box.PseudoElement is not null)
            {
                PaintNativePart(fragment, offset);
            }

            if (box.Kind == ContainerBoxKind.TableGrid)
            {
                PaintTableGrid(fragment);
            }

            PaintGapDecorations(fragment, offset);
            PaintMath(fragment, offset);
            if (_paintsScrollbars && fragment.ScrollbarsDisplayed != Scrollbars.None)
            {
                PaintScrollbars(fragment, offset);
            }
        }
        else if (box.Display.Internal is DisplayInternal.TableRow or DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup)
        {
            PaintTablePartLayer(fragment);
        }

        if (clipped)
        {
            _builder.Restore();
        }

        if (box.Kind == ContainerBoxKind.Replaced && part == PaintItemPart.All)
        {
            SyncClips(GetClipChain(fragment, includeSelf: true));
            PaintReplacedContent(fragment, offset);
        }

        // The columns and the column groups have no outline (as in the browsers: they are not painted)
        if (HasOutline(style) && box.Display.Internal is not (DisplayInternal.TableColumn or DisplayInternal.TableColumnGroup))
        {
            (_group.Outlines ??= []).Add(fragment);
        }
    }

    /// <summary>Whether a box is the replaced box of an outermost svg element.</summary>
    private static bool IsSvgRootBox(ContainerBox box) => box.Kind == ContainerBoxKind.Replaced && box.Element is SvgSvgElement svg && BoxGeneration.IsSvgRoot(svg);

    /// <summary>A clip of the clip chain of a fragment: the overflow clip of a box, or its <c>clip</c> rectangle (https://drafts.fxtf.org/css-masking/#clip-property).</summary>
    /// <param name="Fragment">The box that clips.</param>
    /// <param name="IsCssClip">Whether the clip is the <c>clip</c> rectangle of the box (else its overflow clip).</param>
    /// <param name="IsLineClampClip">Whether the clip is the block-end content edge of a clamped line-clamp container, which clips its floats (see <see cref="LineClamp"/>).</param>
    private readonly record struct ClipEntry(BoxFragment Fragment, bool IsCssClip, bool IsLineClampClip = false);

    /// <summary>
    /// Returns the clips of a fragment in the current stacking context, from the outermost: its ancestors in the fragment tree (its
    /// containing block chain) that clip their overflow or have a <c>clip</c> rectangle, up to the root of the stacking context (its
    /// clips apply to its descendants), the <c>clip</c> rectangle of the fragment itself (it clips the box and its contents), and
    /// its overflow clip when <paramref name="includeSelf"/> is set (the contents of a replaced box). The clips of the ancestors of
    /// the stacking context were applied when it started.
    /// </summary>
    private List<ClipEntry> GetClipChain(Fragment fragment, bool includeSelf, bool includeSelfCssClip = true)
    {
        var chain = _chain;
        chain.Clear();

        // "If the root element clips its overflow, the ::view-transition pseudo-element is exempted from that clip": the clips of
        // the ancestors of the root element of a scoped view transition were applied by the group of its transform
        if (fragment is BoxFragment { Box.IsScopedViewTransitionRoot: true })
            return chain;

        var groupFragment = _group.Fragment;
        if (includeSelf && fragment is BoxFragment { ClipsOverflow: true, Parent: not null } self)
        {
            chain.Add(new ClipEntry(self, IsCssClip: false));
        }

        // The clip rectangle of the root of the stacking context is applied by the stacking context (after its filters)
        if (includeSelfCssClip && fragment is BoxFragment selfClip && !ReferenceEquals(fragment, groupFragment) && HasCssClip(selfClip))
        {
            chain.Add(new ClipEntry(selfClip, IsCssClip: true));
        }

        if (!ReferenceEquals(fragment, groupFragment))
        {
            // https://drafts.csswg.org/css-overflow-4/#line-clamp: the floats of a line-clamp container (that are not hidden) are
            // clipped to its block-end content edge (WPT css/css-overflow/line-clamp/line-clamp-with-floats-005.html)
            var inFloat = fragment is BoxFragment { Box.IsOutOfFlow: true } floatFragment && !PositionedLayout.IsAbsolutelyPositioned(floatFragment.Box);
            for (var ancestor = fragment.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (inFloat && ancestor is BoxFragment { IsLineClampContainer: true } clamped)
                {
                    chain.Add(new ClipEntry(clamped, IsCssClip: false, IsLineClampClip: true));
                }

                // The viewport only clips to itself
                if (ancestor is BoxFragment { ClipsOverflow: true, Parent: not null } clipper)
                {
                    chain.Add(new ClipEntry(clipper, IsCssClip: false));
                }

                if (ancestor is BoxFragment { Box.IsOutOfFlow: true } floatAncestor && !PositionedLayout.IsAbsolutelyPositioned(floatAncestor.Box))
                {
                    inFloat = true;
                }

                if (ReferenceEquals(ancestor, groupFragment))
                    break;

                if (ancestor is BoxFragment cssClipper && HasCssClip(cssClipper))
                {
                    chain.Add(new ClipEntry(cssClipper, IsCssClip: true));
                }
            }

            // https://drafts.fxtf.org/css-masking/#clip-property: the clip rectangle clips all the descendants, also the ones whose
            // containing block is outside of the box (e.g. a fixed positioned descendant)
            if (_hasCssClips && fragment.Box is { } startBox)
            {
                for (var box = startBox.Parent; box is not null && !ReferenceEquals(box, groupFragment?.Box); box = box.Parent)
                {
                    if (box.IsAnonymous || _engine.GetFirstFragment(box) is not BoxFragment boxFragment || !HasCssClip(boxFragment) || chain.Contains(new ClipEntry(boxFragment, IsCssClip: true)))
                        continue;

                    chain.Add(new ClipEntry(boxFragment, IsCssClip: true));
                }
            }
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>Restores the clips of the current group that are not in <paramref name="chain"/>, and applies the missing ones.</summary>
    private void SyncClips(List<ClipEntry> chain)
    {
        var clips = _group.Clips;
        var common = 0;
        while (common < clips.Count && common < chain.Count && clips[common] == chain[common])
        {
            common++;
        }

        if (common < clips.Count)
        {
            _builder.RestoreTo(_group.SaveCount + common);
            clips.RemoveRange(common, clips.Count - common);
        }

        for (var i = common; i < chain.Count; i++)
        {
            var clip = chain[i];
            _builder.Save();
            var offset = clip.Fragment.GetOffsetInRoot() - _group.Origin;
            if (clip.IsLineClampClip)
            {
                AddLineClampClip(clip.Fragment, offset);
            }
            else if (clip.IsCssClip)
            {
                AddCssClip(clip.Fragment, offset);
            }
            else
            {
                AddOverflowClip(clip.Fragment, offset);
            }

            clips.Add(clip);
        }
    }

    /// <summary>
    /// Clips the floats of a line-clamp container to its block-end content edge (https://drafts.csswg.org/css-overflow-4/#line-clamp:
    /// the floats before the clamp point are not hidden, they do not extend a clamped container): the other sides are not clipped.
    /// </summary>
    private void AddLineClampClip(BoxFragment fragment, PhysicalOffset offset)
    {
        var content = fragment.ContentBoxRect;
        var clip = fragment.WritingDirection.BlockEnd switch
        {
            PhysicalSide.Bottom => new PhysicalRect(-UnclippedExtent, -UnclippedExtent, 2 * UnclippedExtent, content.Bottom + UnclippedExtent),
            PhysicalSide.Left => new PhysicalRect(content.Left, -UnclippedExtent, UnclippedExtent, 2 * UnclippedExtent),
            _ => new PhysicalRect(-UnclippedExtent, -UnclippedExtent, content.Right + UnclippedExtent, 2 * UnclippedExtent),
        };

        _builder.Add(new ClipRectItem(SnapToDevicePixels(clip.Translate(offset))));
    }

    /// <summary>
    /// Clips to the overflow clip edge of a box (https://drafts.csswg.org/css-overflow-3/#overflow-clip-edge) in the axes it clips,
    /// rounded by its inner border radii (https://drafts.csswg.org/css-backgrounds/#corner-clipping) when it clips both axes. The
    /// corners of an edge expanded beyond the border edge by <c>overflow-clip-margin</c> are shaped as the spread of a box shadow.
    /// </summary>
    private void AddOverflowClip(BoxFragment fragment, PhysicalOffset offset)
    {
        var clip = fragment.OverflowClipRect;
        if (!fragment.ClipsOverflowX)
        {
            clip = new PhysicalRect(-UnclippedExtent, clip.Y, 2 * UnclippedExtent, clip.Height);
        }
        else if (!fragment.ClipsOverflowY)
        {
            clip = new PhysicalRect(clip.X, -UnclippedExtent, clip.Width, 2 * UnclippedExtent);
        }

        clip = SnapToDevicePixels(clip.Translate(offset));
        if (fragment.ClipsOverflowX && fragment.ClipsOverflowY && BorderShapeGeometry.Get(fragment) is { } shape)
        {
            // https://drafts.csswg.org/css-borders-4/#border-shape-overflow-interaction: "The inner border-shape clips the
            // overflow content of the element, in the same manner as border-radius"
            _builder.Add(new ClipRectItem(clip));
            ClipToPath(PathOperations.Translate(shape.GetOverflowClip(fragment), fragment.BorderBoxRect.Translate(offset).Offset));
            return;
        }

        if (fragment.ClipsOverflowX && fragment.ClipsOverflowY && BorderRadii.Get(fragment) is not null)
        {
            // The radii of the clip edge are the border radii reduced by the distance from the border edge; beyond the border edge
            // (https://drafts.csswg.org/css-overflow-4/#overflow-clip-margin): "The overflow clip edge is shaped in the corners
            // exactly the same way as an outer box-shadow with a spread radius of the same cumulative offset from the box's border
            // edge" (the outset-adjusted radii; each radius of a corner follows the offset of the side of its axis)
            var border = GetSnappedBorderBox(fragment, offset);
            var rounded = RoundedRect.FromBorderBox(fragment, border).Outset(new Sides<double>(border.Top - clip.Top, clip.Right - border.Right, clip.Bottom - border.Bottom, border.Left - clip.Left), border.Size);
            if (!rounded.IsRect)
            {
                _builder.Add(new ClipRoundedRectItem(rounded));
                return;
            }
        }

        _builder.Add(new ClipRectItem(clip));
    }

    /// <summary>Returns the color of a component of a computed value (e.g. a color of a gradient, of a shadow): currentcolor is the color of the style.</summary>
    internal static DisplayColor? ResolveColor(ComputedStyle style, Css.Syntax.CssComponentValue value)
    {
        if (value is Css.Syntax.CssPreservedToken { Type: Css.Syntax.CssTokenType.Ident } ident && Infra.InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "currentcolor"))
            return GetColor(style, "color");

        // The colors that depend on currentcolor (color-mix(), the relative colors) are computed with the color of the style
        var serialized = Css.Values.CssSerializer.SerializeValue([value], property: null);
        if (serialized.Contains("currentcolor", StringComparison.Ordinal))
        {
            serialized = Css.Cascade.CssValueComputer.ResolveCurrentColor(style, "background-color", serialized, style.GetUsedValue("color"));
        }

        return DisplayColor.Parse(serialized);
    }

    /// <summary>Returns the used color of a color property of a style (currentcolor is resolved), or transparent.</summary>
    private static DisplayColor GetColor(ComputedStyle style, string property) => DisplayColor.Parse(style.GetUsedValue(property)) ?? DisplayColor.Transparent;

    /// <summary>Returns the computed value of <c>opacity</c> (https://drafts.csswg.org/css-color-4/#transparency), clamped to [0, 1].</summary>
    private static double GetOpacity(ComputedStyle style)
    {
        var value = style.GetComputedValue("opacity");
        if (value == "1")
            return 1;

        if (value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            return Math.Clamp(percentage / 100, 0, 1);

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) ? Math.Clamp(opacity, 0, 1) : 1;
    }

    /// <summary>Returns the blend mode of a &lt;blend-mode&gt; keyword (https://drafts.fxtf.org/compositing-1/#ltblendmodegt), or <see cref="BlendMode.Normal"/>.</summary>
    internal static BlendMode ParseBlendMode(string value) => value switch
    {
        "multiply" => BlendMode.Multiply,
        "screen" => BlendMode.Screen,
        "overlay" => BlendMode.Overlay,
        "darken" => BlendMode.Darken,
        "lighten" => BlendMode.Lighten,
        "color-dodge" => BlendMode.ColorDodge,
        "color-burn" => BlendMode.ColorBurn,
        "hard-light" => BlendMode.HardLight,
        "soft-light" => BlendMode.SoftLight,
        "difference" => BlendMode.Difference,
        "exclusion" => BlendMode.Exclusion,
        "hue" => BlendMode.Hue,
        "saturation" => BlendMode.Saturation,
        "color" => BlendMode.Color,
        "luminosity" => BlendMode.Luminosity,
        "plus-lighter" => BlendMode.PlusLighter,
        _ => BlendMode.Normal,
    };
}
