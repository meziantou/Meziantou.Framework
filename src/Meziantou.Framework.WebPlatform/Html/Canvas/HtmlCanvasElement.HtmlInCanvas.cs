using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The snapshot of the rendering of a drawable element (https://github.com/WICG/html-in-canvas), recorded by a rendering update:
/// its display list in the coordinates of its border box, in CSS pixels (unzoomed), and the size of its border box.
/// </summary>
internal sealed class ElementSnapshot(DisplayList content, PhysicalSize size, string key)
{
    public DisplayList Content { get; } = content;

    public PhysicalSize Size { get; } = size;

    /// <summary>The text of the display list and the size, to know whether the rendering changed.</summary>
    public string Key { get; } = key;
}

// HTML-in-canvas (https://github.com/WICG/html-in-canvas): the content attribute, the paint event, the snapshots of the drawable
// elements and their canvas transforms
public partial class HtmlCanvasElement
{
    internal const string HtmlInCanvasHref = "https://github.com/WICG/html-in-canvas#idl-changes";

    /// <summary>The snapshots of the drawable elements recorded by the last rendering update, or null.</summary>
    private Dictionary<Element, ElementSnapshot>? _snapshots;

    /// <summary>The canvas transforms of the elements (4x4 matrices in column-major order, and whether they are 2D), or null.</summary>
    private Dictionary<Element, (double[] Matrix, bool Is2D)>? _canvasTransforms;

    /// <summary>Whether a rendering update is scheduled to record the next frame of an animated image of a snapshot.</summary>
    private bool _frameUpdateScheduled;

    /// <summary>The clips of the elements for the hit testing, or null.</summary>
    private Dictionary<Element, PhysicalRect>? _canvasClips;

    /// <summary>The elements that are hit tested, from the bottom (see <see cref="HitTestOrder"/>), or null.</summary>
    private List<Element>? _hitTestOrder;

    /// <summary>Whether <see cref="RequestPaint"/> was called since the last <c>paint</c> event.</summary>
    private bool _paintRequested;

    /// <summary>The elements whose geometry was updated by an <see cref="OffscreenCanvas"/> since the last <c>elementgeometryupdate</c> event, or null.</summary>
    private List<Element>? _pendingGeometryUpdates;

    /// <summary>
    /// Gets or sets the <c>content</c> attribute: <c>"drawable"</c> when the children of the canvas are laid out and can be drawn
    /// into it, else <c>"fallback"</c> (they are its fallback content).
    /// </summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("content", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public string Content
    {
        get => IsContentDrawable ? "drawable" : "fallback";
        set => Reflection.SetString(this, "content", value);
    }

    /// <summary>Whether the <c>content</c> attribute is <c>drawable</c> (ASCII case-insensitive).</summary>
    internal bool IsContentDrawable => GetAttributeValue("content") is { } value && InfraAscii.EqualsIgnoreAsciiCase(value, "drawable");

    /// <summary>Fires a <c>paint</c> event at the next rendering update, even when no drawable element changed.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("requestPaint", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void RequestPaint()
    {
        _paintRequested = true;
        RegisterForRendering();
    }

    /// <summary>Returns the last snapshot of a drawable element, which stays the same when the element changes.</summary>
    /// <param name="element">A drawable element of the canvas.</param>
    /// <returns>The image of the element.</returns>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, or it has no snapshot.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("captureElementImage", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public ElementImage CaptureElementImage(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new ElementImage(this, element, GetSnapshot(element), GetGridScale(), BitmapSize);
    }

    /// <summary>Sets the canvas transform of an element (its geometry), which maps its border box to the content box of the canvas.</summary>
    /// <param name="element">An element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <param name="options">The transform and the clip of the element.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas.</exception>
    /// <exception cref="TypeErrorException">The matrix is not valid.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("updateElementGeometry", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void UpdateElementGeometry(object? element, UpdateElementGeometryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var target = GetGeometryTarget(element);
        var matrix = options?.CanvasTransform is { } init ? DomMatrix.FromMatrixInit(init) : null;

        // (a rectangle with a negative width or height is normalized)
        PhysicalRect? clip = options?.CanvasClip is { } clipInit
            ? new PhysicalRect(Math.Min(clipInit.X, clipInit.X + clipInit.Width), Math.Min(clipInit.Y, clipInit.Y + clipInit.Height), Math.Abs(clipInit.Width), Math.Abs(clipInit.Height))
            : null;
        UpdateGeometry(target, matrix is null ? null : [.. matrix.Elements], matrix?.Is2DMatrix ?? true, clip, options?.PreserveHitTestOrder ?? false);
    }

    /// <summary>Clears the canvas transform of an element: it is the identity again.</summary>
    /// <param name="element">An element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("clearElementGeometry", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void ClearElementGeometry(object? element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ClearGeometry(GetGeometryTarget(element));
    }

    /// <summary>Clears the canvas transform and the canvas clip of an element, and removes it from the hit testing order.</summary>
    internal void ClearGeometry(Element element)
    {
        _canvasTransforms?.Remove(element);
        _canvasClips?.Remove(element);
        _hitTestOrder?.Remove(element);
        OnGeometryChanged();
    }

    /// <summary>Returns the canvas transform of an element (a new matrix), the identity when none is set.</summary>
    /// <param name="element">A drawable element of the canvas.</param>
    /// <returns>The canvas transform.</returns>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("getElementTransform", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public DomMatrix GetElementTransform(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfNotDrawable(element);
        return _canvasTransforms is not null && _canvasTransforms.TryGetValue(element, out var transform) ? DomMatrix.Create(transform.Matrix, transform.Is2D) : new DomMatrix();
    }

    /// <summary>
    /// Returns the size of the last snapshot of an element in the coordinates of the bitmap of the canvas: the size of its border box
    /// scaled by the ratio of the size of the bitmap to the size of the content box of the canvas.
    /// </summary>
    /// <param name="element">A drawable element of the canvas, or an <see cref="ElementImage"/> of the canvas.</param>
    /// <returns>The size.</returns>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, or it has no snapshot.</exception>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("getElementImageDefaultSize", HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public ElementImageDefaultSize GetElementImageDefaultSize(object? element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var (snapshot, _, (scaleX, scaleY)) = GetDrawableSnapshot(element, this, BitmapSize);
        return new ElementImageDefaultSize { Width = snapshot.Size.Width * scaleX, Height = snapshot.Size.Height * scaleY };
    }

    /// <summary>Returns the canvas transform of an element, or null when none is set.</summary>
    internal double[]? GetCanvasTransform(Element element) => _canvasTransforms is not null && _canvasTransforms.TryGetValue(element, out var transform) ? transform.Matrix : null;

    /// <summary>
    /// Updates the geometry of an element: its canvas transform and its canvas clip (unless they are null), and its place in the
    /// hit testing order: it moves to the top, unless it is already in it and <paramref name="preserveHitTestOrder"/> is true.
    /// </summary>
    internal void UpdateGeometry(Element element, double[]? matrix, bool is2D, PhysicalRect? clip, bool preserveHitTestOrder)
    {
        if (matrix is not null)
        {
            (_canvasTransforms ??= new(ReferenceEqualityComparer.Instance))[element] = (matrix, is2D);
        }

        if (clip is { } clipRect)
        {
            (_canvasClips ??= new(ReferenceEqualityComparer.Instance))[element] = clipRect;
        }

        _hitTestOrder ??= [];
        if (!preserveHitTestOrder || !_hitTestOrder.Contains(element))
        {
            _hitTestOrder.Remove(element);
            _hitTestOrder.Add(element);
        }

        OnGeometryChanged();
    }

    /// <summary>The elements whose geometry was updated, in the hit testing order (the last one is the topmost), or null.</summary>
    internal IReadOnlyList<Element>? HitTestOrder => _hitTestOrder;

    /// <summary>Returns the clip of an element for the hit testing (in its coordinates before its canvas transform, in CSS pixels), or null.</summary>
    internal PhysicalRect? GetCanvasClip(Element element) => _canvasClips is not null && _canvasClips.TryGetValue(element, out var clip) ? clip : null;

    /// <summary>
    /// Applies a geometry update of an element made by the <see cref="OffscreenCanvas"/> of the canvas, in the event loop of the
    /// canvas (in a task when the OffscreenCanvas is in a worker), and fires an <c>elementgeometryupdate</c> event in a microtask.
    /// The elements that are no longer connected are ignored.
    /// </summary>
    internal void UpdateGeometryFromOffscreenCanvas(Element element, DomEventLoop? currentEventLoop, Action update)
    {
        if (NodeDocument.EventLoop is not { } eventLoop)
            return;

        if (ReferenceEquals(eventLoop, currentEventLoop) || currentEventLoop is null)
        {
            Apply(element, update);
            return;
        }

        // "Worker-thread updates are batched via a microtask and asynchronously posted to the main thread"
        bool first;
        lock (_offscreenUpdatesLock)
        {
            _offscreenUpdates ??= [];
            first = _offscreenUpdates.Count == 0;
            _offscreenUpdates.Add((element, update));
        }

        if (first)
        {
            currentEventLoop.QueueMicrotask(() =>
            {
                List<(Element Element, Action Update)>? updates;
                lock (_offscreenUpdatesLock)
                {
                    updates = _offscreenUpdates;
                    _offscreenUpdates = null;
                }

                if (updates is not null)
                {
                    eventLoop.QueueTask(() => updates.ForEach(item => Apply(item.Element, item.Update)));
                }
            });
        }

        void Apply(Element target, Action apply)
        {
            if (!target.IsConnected)
                return;

            apply();
            QueueGeometryUpdateEvent(target);
        }
    }

    private readonly Lock _offscreenUpdatesLock = new();

    /// <summary>The geometry updates made by an <see cref="OffscreenCanvas"/> of a worker in its current task, or null.</summary>
    private List<(Element Element, Action Update)>? _offscreenUpdates;

    /// <summary>Records that an <see cref="OffscreenCanvas"/> updated the geometry of an element: an <c>elementgeometryupdate</c> event is fired in a microtask.</summary>
    private void QueueGeometryUpdateEvent(Element element)
    {
        if (_pendingGeometryUpdates is null)
        {
            _pendingGeometryUpdates = [];
            NodeDocument.EventLoop?.QueueMicrotask(() =>
            {
                var elements = _pendingGeometryUpdates;
                _pendingGeometryUpdates = null;
                if (elements is null)
                    return;

                var connected = elements.FindAll(element => element.IsConnected);
                FireEvent(new ElementGeometryUpdateEvent("elementgeometryupdate", new ElementGeometryUpdateEventInit { Elements = connected }));
            });
        }

        if (!_pendingGeometryUpdates.Contains(element))
        {
            _pendingGeometryUpdates.Add(element);
        }
    }

    /// <summary>The geometry of the elements changed (their canvas transforms): the geometry APIs see the new positions.</summary>
    private void OnGeometryChanged() => NodeDocument.LayoutEngineOrNull?.InvalidatePaint();

    /// <summary>
    /// Returns the scale from the CSS pixels of the canvas to the pixels of its bitmap: its width divided by the width of the
    /// rectangle where it is painted (its content box, with object-fit and object-position), in unzoomed CSS pixels; 1 when the
    /// canvas is not rendered.
    /// </summary>
    internal (double X, double Y) GetGridScale()
    {
        var (width, height) = BitmapSize;
        if (NodeDocument.LayoutEngineOrNull?.GetFragments(this) is not [var fragment, ..])
            return (1, 1);

        // (the bitmap is painted in the rectangle of object-fit and object-position)
        var content = fragment.ReplacedContentRect ?? fragment.ContentBoxRect;
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        var cssWidth = content.Width / zoom;
        var cssHeight = content.Height / zoom;
        return (cssWidth > 0 ? width / cssWidth : 1, cssHeight > 0 ? height / cssHeight : 1);
    }

    /// <summary>Returns the element whose geometry an <see cref="UpdateElementGeometry"/> or a <see cref="ClearElementGeometry"/> changes.</summary>
    private Element GetGeometryTarget(object? element)
    {
        switch (element)
        {
            case ElementImage image:
                if (!ReferenceEquals(image.Canvas, this))
                    throw new DomException("The element image was captured from another canvas", "InvalidStateError");

                if (image.Snapshot is null)
                    throw new DomException("The element image is closed", "InvalidStateError");

                return image.Element;

            case Element target:
                ThrowIfNotDrawable(target);
                return target;

            default:
                throw new TypeErrorException("The element is not an Element or an ElementImage");
        }
    }

    /// <summary>Throws an <c>InvalidStateError</c> when an element is not a drawable element of the canvas (connected, in its contents, with a <c>drawable</c> attribute).</summary>
    internal void ThrowIfNotDrawable(Element element)
    {
        if (!IsConnected || !element.IsConnected || !IsContentDrawable || !element.HasAttribute("drawable") || !ReferenceEquals(GetDrawableCanvas(element), this))
            throw new DomException("The element is not a drawable element of the canvas", "InvalidStateError");
    }

    /// <summary>Returns the nearest canvas ancestor of an element in the flat tree (its shadow-including ancestors), or null.</summary>
    internal static HtmlCanvasElement? GetDrawableCanvas(Element element)
    {
        for (var node = GetFlatTreeParent(element); node is not null; node = GetFlatTreeParent(node))
        {
            if (node is HtmlCanvasElement canvas)
                return canvas;
        }

        return null;

        static Node? GetFlatTreeParent(Node node) => node is Element { AssignedSlot: { } slot } ? slot : node.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode;
    }

    /// <summary>
    /// Returns the snapshot to draw for an element or an element image, and the element, for a canvas (the canvas of a 2D context,
    /// or the placeholder canvas of an <see cref="OffscreenCanvas"/>).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the element is not a drawable element of the canvas, when it has no snapshot, or the image is closed.</exception>
    /// <remarks>
    /// The scale is the one from the CSS pixels of the canvas to the pixels of a bitmap of size <paramref name="bitmapSize"/> (the
    /// bitmap of the context that draws the image): the one recorded by an element image when it was captured (it may be drawn in
    /// a worker), else the current one.
    /// </remarks>
    internal static (ElementSnapshot Snapshot, Element Element, (double X, double Y) Scale) GetDrawableSnapshot(object? element, HtmlCanvasElement? canvas, (int Width, int Height) bitmapSize)
    {
        switch (element)
        {
            case ElementImage image:
                if (canvas is null || !ReferenceEquals(image.Canvas, canvas))
                    throw new DomException("The element image was captured from another canvas", "InvalidStateError");

                if (image.Snapshot is not { } imageSnapshot)
                    throw new DomException("The element image is closed", "InvalidStateError");

                return (imageSnapshot, image.Element, ScaleTo(image.GridScale, image.BitmapSize, bitmapSize));

            case Element target:
                if (canvas is null)
                    throw new DomException("The canvas has no element", "InvalidStateError");

                return (canvas.GetSnapshot(target), target, ScaleTo(canvas.GetGridScale(), canvas.BitmapSize, bitmapSize));

            default:
                throw new TypeErrorException("The element is not an Element or an ElementImage");
        }
    }

    private static (double X, double Y) ScaleTo((double X, double Y) scale, (int Width, int Height) from, (int Width, int Height) to)
        => (from.Width > 0 ? scale.X * to.Width / from.Width : scale.X, from.Height > 0 ? scale.Y * to.Height / from.Height : scale.Y);

    /// <summary>Returns the last snapshot of a drawable element of the canvas.</summary>
    private ElementSnapshot GetSnapshot(Element element)
    {
        ThrowIfNotDrawable(element);
        if (_snapshots is null || !_snapshots.TryGetValue(element, out var snapshot))
            throw new DomException("The element has no snapshot: it was not rendered since it was added", "InvalidStateError");

        return snapshot;
    }

    /// <summary>
    /// Registers the canvas in its document, so that the rendering updates record the snapshots of its drawable elements and fire
    /// its <c>paint</c> events, and schedules a rendering update.
    /// </summary>
    private void RegisterForRendering()
    {
        if (IsConnected)
        {
            NodeDocument.AddRenderedCanvas(this);
        }
    }

    /// <inheritdoc/>
    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        if (IsContentDrawable)
        {
            RegisterForRendering();
        }
    }

    /// <summary>
    /// Records the snapshots of the drawable elements of the canvas (the rendering update, https://github.com/WICG/html-in-canvas:
    /// "a snapshot of the rendering commands of all drawable elements [...] is recorded on every rendering update"), and returns
    /// the elements whose snapshot changed and whether a <c>paint</c> event is fired; null when the canvas is no longer rendered.
    /// </summary>
    internal (List<Element> Changed, bool Fires)? RecordSnapshots(LayoutEngine? engine)
    {
        var changed = new List<Element>();
        var previous = _snapshots;
        double? nextFrameDelay = null;
        Dictionary<Element, ElementSnapshot>? snapshots = null;
        if (engine is not null && IsContentDrawable && engine.BoxTree?.GetPrincipalBox(this) is { Kind: ContainerBoxKind.Replaced } canvasBox)
        {
            foreach (var box in CanvasDrawables.GetDrawableBoxes(canvasBox))
            {
                var element = box.Element!;
                if (CanvasDrawables.PaintSnapshot(engine, NodeDocument, box, out var size, out var delay) is not { } content)
                    continue;

                if (delay is { } frameDelay && (nextFrameDelay is null || frameDelay < nextFrameDelay))
                {
                    nextFrameDelay = frameDelay;
                }

                // (the CSS transform of the element is not painted, but its change is a change of the rendering, as in Chromium)
                var transform = box.FirstFragment is BoxFragment fragment && LayoutTransforms.GetMatrix(fragment) is { } matrix ? string.Join(',', matrix.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) : "";
                var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size.Width}x{size.Height} {transform} {GetImagesKey(content)}\n{content.Dump()}");
                if (previous is not null && previous.TryGetValue(element, out var old) && old.Key == key)
                {
                    (snapshots ??= new(ReferenceEqualityComparer.Instance))[element] = old;
                    continue;
                }

                (snapshots ??= new(ReferenceEqualityComparer.Instance))[element] = new ElementSnapshot(content, size, key);
                changed.Add(element);
            }
        }

        // An element that is no longer drawn (removed, display: none...) changes the rendering too, but it is not reported
        var removed = false;
        if (previous is not null)
        {
            foreach (var element in previous.Keys)
            {
                if (snapshots is null || !snapshots.ContainsKey(element))
                {
                    removed = true;
                    break;
                }
            }
        }

        // The snapshots of the animated images change with their frames: a rendering update records them again then
        if (nextFrameDelay is { } wait && !_frameUpdateScheduled && NodeDocument.EventLoop is { } eventLoop)
        {
            _frameUpdateScheduled = true;
            eventLoop.QueueTimer(TimeSpan.FromMilliseconds(Math.Max(wait, 0)), () =>
            {
                _frameUpdateScheduled = false;
                eventLoop.RequestRenderingUpdate();
            });
        }

        _snapshots = snapshots;
        var fires = (IsContentDrawable && (changed.Count > 0 || removed)) || _paintRequested;
        _paintRequested = false;
        return fires || IsContentDrawable ? (changed, fires) : null;
    }

    /// <summary>The identities of the images of a display list: the frames of an animated image are different images.</summary>
    private static string GetImagesKey(DisplayList content)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var item in content.Items)
        {
            if (item is DrawImageItem image)
            {
                builder.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(image.Image)).Append(',');
            }
        }

        return builder.ToString();
    }
}
