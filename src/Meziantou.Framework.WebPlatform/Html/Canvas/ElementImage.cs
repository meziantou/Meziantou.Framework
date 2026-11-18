using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A snapshot of the rendering of a drawable element of a canvas whose <c>content</c> is <c>drawable</c>
/// (https://github.com/WICG/html-in-canvas): the one recorded by the last rendering update, returned by
/// <see cref="HtmlCanvasElement.CaptureElementImage"/>. It can be drawn into the canvas it was captured from with
/// <see cref="CanvasRenderingContext2DBase.DrawElementImage(object, double, double, DrawElementImageOptions?)"/>, also after the
/// element changed, until it is closed.
/// </summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("ElementImage", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class ElementImage : Scripting.IStructuredTransferable
{
    internal ElementImage(HtmlCanvasElement canvas, Element element, ElementSnapshot snapshot, (double X, double Y) gridScale, (int Width, int Height) bitmapSize)
    {
        Canvas = canvas;
        Element = element;
        Snapshot = snapshot;
        GridScale = gridScale;
        BitmapSize = bitmapSize;
    }

    /// <summary>
    /// The scale from the CSS pixels of the canvas to the pixels of its bitmap when the image was captured (see
    /// <see cref="HtmlCanvasElement.GetGridScale"/>): an image transferred to a worker does not read the layout of the canvas.
    /// </summary>
    internal (double X, double Y) GridScale { get; }

    /// <summary>The size of the bitmap of the canvas when the image was captured.</summary>
    internal (int Width, int Height) BitmapSize { get; }

    /// <summary>The canvas the image was captured from.</summary>
    internal HtmlCanvasElement Canvas { get; }

    /// <summary>The element whose rendering the image is.</summary>
    internal Element Element { get; }

    /// <summary>The snapshot of the element, or null when the image is closed.</summary>
    internal ElementSnapshot? Snapshot { get; private set; }

    /// <summary>Releases the snapshot: the image can no longer be drawn nor measured.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("close", HtmlCanvasElement.HtmlInCanvasHref)]
    [Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
    public void Close() => Snapshot = null;

    /// <summary>A closed (or transferred) image cannot be transferred.</summary>
    void Scripting.IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        if (Snapshot is null)
            throw new DomException("The element image is closed", "DataCloneError");
    }

    /// <summary>The transfer steps: the image is detached (closed), the transferred image has the same snapshot.</summary>
    Scripting.ITransferredData Scripting.IStructuredTransferable.Transfer()
    {
        var data = new TransferredElementImage(Canvas, Element, Snapshot!, GridScale, BitmapSize);
        Snapshot = null;
        return data;
    }

    private sealed class TransferredElementImage(HtmlCanvasElement canvas, Element element, ElementSnapshot snapshot, (double X, double Y) gridScale, (int Width, int Height) bitmapSize) : Scripting.ITransferredData
    {
        private ElementImage? _received;

        public object Receive(GlobalScope? target) => _received ??= new ElementImage(canvas, element, snapshot, gridScale, bitmapSize);
    }
}

/// <summary>The size of an element image in the coordinates of the bitmap of its canvas (https://github.com/WICG/html-in-canvas).</summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("ElementImageDefaultSize", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class ElementImageDefaultSize
{
    /// <summary>Gets the width, in pixels of the bitmap of the canvas.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("width", HtmlCanvasElement.HtmlInCanvasHref)]
    public double Width { get; init; }

    /// <summary>Gets the height, in pixels of the bitmap of the canvas.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("height", HtmlCanvasElement.HtmlInCanvasHref)]
    public double Height { get; init; }
}

/// <summary>The options of <see cref="HtmlCanvasElement.UpdateElementGeometry"/> (https://github.com/WICG/html-in-canvas).</summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("UpdateElementGeometryOptions", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class UpdateElementGeometryOptions
{
    /// <summary>Gets whether an element already in the hit testing order of the canvas keeps its place (else it moves to the top).</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("preserveHitTestOrder", HtmlCanvasElement.HtmlInCanvasHref)]
    public bool PreserveHitTestOrder { get; init; }

    /// <summary>Gets the clip of the element for the hit testing, in the coordinates of its canvas transform, or null to keep it.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("canvasClip", HtmlCanvasElement.HtmlInCanvasHref)]
    public DomRectInit? CanvasClip { get; init; }

    /// <summary>
    /// Gets the canvas transform of the element, which maps its border box (before its CSS transforms) to the content box of the
    /// canvas, in CSS pixels, or null to keep it.
    /// </summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("canvasTransform", HtmlCanvasElement.HtmlInCanvasHref)]
    public DomMatrixInit? CanvasTransform { get; init; }
}

/// <summary>The options of <see cref="CanvasRenderingContext2DBase.DrawElementImage(object, double, double, DrawElementImageOptions?)"/> (https://github.com/WICG/html-in-canvas).</summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("DrawElementImageOptions", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class DrawElementImageOptions
{
    /// <summary>Gets whether the canvas transform of the element is kept (else it is set to where the element is drawn).</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("preserveElementGeometry", HtmlCanvasElement.HtmlInCanvasHref)]
    public bool PreserveElementGeometry { get; init; }
}

/// <summary>The initialization dictionary of <see cref="CanvasPaintEvent"/> (https://github.com/WICG/html-in-canvas).</summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("CanvasPaintEventInit", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CanvasPaintEventInit : EventInit
{
    /// <summary>Gets the drawable elements whose rendering changed.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("changedElements", HtmlCanvasElement.HtmlInCanvasHref)]
    public IReadOnlyList<Element> ChangedElements { get; init; } = [];
}

/// <summary>
/// The <c>paint</c> event of a canvas whose <c>content</c> is <c>drawable</c> (https://github.com/WICG/html-in-canvas): fired by a
/// rendering update when the rendering of drawable elements changed, or after <see cref="HtmlCanvasElement.RequestPaint"/>.
/// </summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("CanvasPaintEvent", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CanvasPaintEvent : Event
{
    /// <summary>Creates a paint event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("constructor", HtmlCanvasElement.HtmlInCanvasHref)]
    public CanvasPaintEvent(string type, CanvasPaintEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        ChangedElements = new ReadOnlyCollection<Element>([.. eventInitDict?.ChangedElements ?? []]);
    }

    /// <summary>Gets the drawable elements whose rendering changed.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("changedElements", HtmlCanvasElement.HtmlInCanvasHref)]
    public IReadOnlyList<Element> ChangedElements { get; }
}

/// <summary>The initialization dictionary of <see cref="ElementGeometryUpdateEvent"/> (https://github.com/WICG/html-in-canvas).</summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("ElementGeometryUpdateEventInit", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class ElementGeometryUpdateEventInit : EventInit
{
    /// <summary>Gets the elements whose geometry was updated.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("elements", HtmlCanvasElement.HtmlInCanvasHref)]
    public IReadOnlyList<Element> Elements { get; init; } = [];
}

/// <summary>
/// The <c>elementgeometryupdate</c> event of a canvas (https://github.com/WICG/html-in-canvas): fired when the geometry updates of
/// its <see cref="OffscreenCanvas"/> were applied to its elements.
/// </summary>
/// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
[IdlInterface("ElementGeometryUpdateEvent", HtmlCanvasElement.HtmlInCanvasHref)]
[Experimental(ExperimentalApis.HtmlInCanvas, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class ElementGeometryUpdateEvent : Event
{
    /// <summary>Creates an element geometry update event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("constructor", HtmlCanvasElement.HtmlInCanvasHref)]
    public ElementGeometryUpdateEvent(string type, ElementGeometryUpdateEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Elements = new ReadOnlyCollection<Element>([.. eventInitDict?.Elements ?? []]);
    }

    /// <summary>Gets the elements whose geometry was updated.</summary>
    /// <seealso href="https://github.com/WICG/html-in-canvas#idl-changes"/>
    [IdlMember("elements", HtmlCanvasElement.HtmlInCanvasHref)]
    public IReadOnlyList<Element> Elements { get; }
}
