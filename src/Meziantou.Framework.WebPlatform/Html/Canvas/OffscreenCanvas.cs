using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A canvas that is not in a document (https://html.spec.whatwg.org/multipage/canvas.html#the-offscreencanvas-interface): created
/// in a window or in a worker, or by <c>transferControlToOffscreen()</c> for a <c>canvas</c> element (its placeholder), whose
/// frames are then displayed by the element. It is transferable (to a worker), and needs a rendering backend for its 2D context.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvas"/>
[IdlInterface("OffscreenCanvas", "https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvas")]
public sealed class OffscreenCanvas : EventTarget, IStructuredTransferable, ICanvasHost
{
    private ulong _width;
    private ulong _height;
    private bool _commitQueued;

    /// <summary>Creates an <see cref="OffscreenCanvas"/> of the given size, without context (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas")]
    public OffscreenCanvas(ulong width, ulong height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>The rendering context (https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvas-context-mode): null, an <see cref="OffscreenCanvasRenderingContext2D"/> or an <see cref="ImageBitmapRenderingContext"/>.</summary>
    internal object? RenderingContext { get; private set; }

    /// <summary>Whether the canvas was transferred (https://html.spec.whatwg.org/multipage/canvas.html#concept-offscreencanvas-detached).</summary>
    internal bool IsDetached { get; private set; }

    /// <summary>The <c>canvas</c> element whose frames the canvas renders (https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvas-placeholder), or null.</summary>
    internal HtmlCanvasElement? Placeholder { get; set; }

    /// <summary>Whether the direction of the placeholder canvas element was right to left when its control was transferred (the <c>inherit</c> direction of the texts).</summary>
    internal bool InheritsRightToLeft { get; init; }

    /// <summary>
    /// The language of the placeholder canvas element when its control was transferred (the <c>inherit</c> language of the
    /// texts, https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-transfercontroltooffscreen): an empty string when it
    /// is unknown, null when the canvas was not created by <see cref="HtmlCanvasElement.TransferControlToOffscreen"/>.
    /// </summary>
    internal string? InheritedLanguage { get; init; }

    /// <summary>
    /// Gets or sets the width of the bitmap (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-width): setting
    /// it resets the bitmap and the 2D context.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is detached.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-width"/>
    [IdlMember("width", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-width")]
    public ulong Width
    {
        get => _width;
        set
        {
            if (IsDetached)
                throw new DomException("The OffscreenCanvas is detached", "InvalidStateError");

            _width = value;
            ResetBitmap();
        }
    }

    /// <summary>
    /// Gets or sets the height of the bitmap (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-height):
    /// setting it resets the bitmap and the 2D context.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is detached.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-height"/>
    [IdlMember("height", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-height")]
    public ulong Height
    {
        get => _height;
        set
        {
            if (IsDetached)
                throw new DomException("The OffscreenCanvas is detached", "InvalidStateError");

            _height = value;
            ResetBitmap();
        }
    }

    int ICanvasHost.BitmapWidth => (int)Math.Min(_width, int.MaxValue);

    int ICanvasHost.BitmapHeight => (int)Math.Min(_height, int.MaxValue);

    /// <summary>Gets or sets the event handler of the <c>contextlost</c> event (never fired).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#handler-offscreencanvas-oncontextlost"/>
    [IdlMember("oncontextlost", "https://html.spec.whatwg.org/multipage/canvas.html#handler-offscreencanvas-oncontextlost")]
    public Action<Event>? OnContextLost
    {
        get => GetEventHandler("contextlost");
        set => SetEventHandler("contextlost", value);
    }

    /// <summary>Gets or sets the event handler of the <c>contextrestored</c> event (never fired).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#handler-offscreencanvas-oncontextrestored"/>
    [IdlMember("oncontextrestored", "https://html.spec.whatwg.org/multipage/canvas.html#handler-offscreencanvas-oncontextrestored")]
    public Action<Event>? OnContextRestored
    {
        get => GetEventHandler("contextrestored");
        set => SetEventHandler("contextrestored", value);
    }

    /// <summary>
    /// Returns the rendering context of the canvas (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-getcontext):
    /// an <see cref="OffscreenCanvasRenderingContext2D"/> for <c>"2d"</c> (null without rendering backend), an
    /// <see cref="ImageBitmapRenderingContext"/> for <c>"bitmaprenderer"</c>, or null for another type or when the canvas
    /// already has a context of another type.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is detached.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-getcontext"/>
    [IdlMember("getContext", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-getcontext")]
    public object? GetContext(OffscreenRenderingContextId contextId, CanvasRenderingContext2DSettings? options = null)
    {
        if (IsDetached)
            throw new DomException("The OffscreenCanvas is detached", "InvalidStateError");

        switch (RenderingContext)
        {
            case null when contextId == OffscreenRenderingContextId.TwoD:
            {
                var settings = options ?? new CanvasRenderingContext2DSettings();
                if (CanvasContexts.CreateBitmap(RelevantGlobal, backend: null, (int)Math.Min(_width, int.MaxValue), (int)Math.Min(_height, int.MaxValue), settings) is not { } bitmap)
                    return null;

                RenderingContext = new OffscreenCanvasRenderingContext2D(this, bitmap, settings);
                return RenderingContext;
            }

            case null when contextId == OffscreenRenderingContextId.BitmapRenderer:
                RenderingContext = new ImageBitmapRenderingContext(this, options?.Alpha ?? true);
                return RenderingContext;

            case OffscreenCanvasRenderingContext2D context when contextId == OffscreenRenderingContextId.TwoD:
                return context;

            case ImageBitmapRenderingContext context when contextId == OffscreenRenderingContextId.BitmapRenderer:
                return context;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns an <see cref="ImageBitmap"/> of the bitmap and replaces the bitmap with a new transparent black bitmap
    /// (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-transfertoimagebitmap).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is detached or has no context.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-transfertoimagebitmap"/>
    [IdlMember("transferToImageBitmap", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-transfertoimagebitmap")]
    public ImageBitmap TransferToImageBitmap()
    {
        if (IsDetached)
            throw new DomException("The OffscreenCanvas is detached", "InvalidStateError");

        switch (RenderingContext)
        {
            case OffscreenCanvasRenderingContext2D context:
            {
                var pixels = context.Bitmap.GetPixels();
                context.Bitmap.Clear();
                OnBitmapChanged();
                return new ImageBitmap(pixels);
            }

            case ImageBitmapRenderingContext context:
            {
                var pixels = context.GetBitmap();
                context.TransferFromImageBitmap(null);
                return new ImageBitmap(pixels);
            }

            default:
                throw new DomException("The OffscreenCanvas has no rendering context", "InvalidStateError");
        }
    }

    /// <summary>
    /// Returns a <see cref="Blob"/> of the bitmap serialized as an image file (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-converttoblob):
    /// PNG, JPEG or WebP (another type is serialized as PNG).
    /// </summary>
    /// <returns>A task completed in a task of the event loop; it fails with an <c>InvalidStateError</c> when the canvas is detached, a <c>SecurityError</c> when it is not origin-clean, an <c>IndexSizeError</c> when it has no pixels.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-converttoblob"/>
    [IdlMember("convertToBlob", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-converttoblob")]
    public Task<Blob> ConvertToBlob(ImageEncodeOptions? options = null)
    {
        // 1-3. A detached canvas, a canvas that is not origin-clean, a canvas without pixels
        if (IsDetached)
            return Task.FromException<Blob>(new DomException("The OffscreenCanvas is detached", "InvalidStateError"));

        var pixels = GetBitmapSnapshot();
        if (!pixels.IsOriginClean)
            return Task.FromException<Blob>(new DomException("The OffscreenCanvas has been tainted by cross-origin data", "SecurityError"));

        if (_width == 0 || _height == 0)
            return Task.FromException<Blob>(new DomException("The OffscreenCanvas has no pixels", "IndexSizeError"));

        // 4-6. The serialization, resolved in a task (a bitmap that cannot be serialized rejects with an EncodingError)
        var file = CanvasContexts.Serialize(pixels, options?.Type ?? "image/png", options?.Quality, GetBackend());
        var completion = new TaskCompletionSource<Blob>();
        void Complete()
        {
            if (file is var (data, mimeType))
            {
                completion.TrySetResult(new Blob([data], new BlobPropertyBag { Type = mimeType }));
            }
            else
            {
                completion.TrySetException(new DomException("The bitmap cannot be serialized", "EncodingError"));
            }
        }

        if (RelevantGlobal is { } global)
        {
            global.QueueGlobalTask(Complete);
        }
        else
        {
            Complete();
        }

        return completion.Task;
    }

    /// <summary>Returns a copy of the pixels of the bitmap and its origin-clean flag (transparent black without context).</summary>
    internal BitmapPixels GetBitmapSnapshot() => RenderingContext switch
    {
        OffscreenCanvasRenderingContext2D context => context.Bitmap.GetPixels(),
        ImageBitmapRenderingContext context => context.GetBitmap(),
        _ => BitmapPixels.CreateTransparent((int)Math.Min(_width, int.MaxValue), (int)Math.Min(_height, int.MaxValue)),
    };

    private RenderingBackend? GetBackend() => (RenderingContext as OffscreenCanvasRenderingContext2D)?.Bitmap.Backend ?? RelevantGlobal?.Options.RenderingBackend;

    /// <summary>The width or the height changed: the bitmap is replaced and the 2D context is reset (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencanvas-width).</summary>
    private void ResetBitmap()
    {
        if (RenderingContext is OffscreenCanvasRenderingContext2D context)
        {
            context.ResetState();
            context.Bitmap.Resize((int)Math.Min(_width, int.MaxValue), (int)Math.Min(_height, int.MaxValue));
        }

        OnBitmapChanged();
    }

    /// <summary>
    /// The bitmap changed: the canvas of a placeholder commits its frame to the placeholder at the end of the task
    /// (https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvas-placeholder).
    /// </summary>
    void ICanvasHost.OnBitmapChanged() => OnBitmapChanged();

    internal void OnBitmapChanged()
    {
        if (Placeholder is null || _commitQueued || RelevantGlobal is not { } global)
            return;

        _commitQueued = true;
        global.EventLoop.QueueTask(Commit);
    }

    /// <summary>Pushes the current frame to the placeholder canvas element, in a task of its event loop.</summary>
    private void Commit()
    {
        _commitQueued = false;
        if (Placeholder is not { } placeholder)
            return;

        var frame = GetBitmapSnapshot();
        var image = frame.Width > 0 && frame.Height > 0 && GetBackend() is { } backend ? frame.GetPaintImage(backend) : null;
        var eventLoop = placeholder.NodeDocument.EventLoop;
        if (eventLoop is null)
        {
            placeholder.SetPlaceholderFrame(frame, image);
            return;
        }

        eventLoop.QueueTask(() => placeholder.SetPlaceholderFrame(frame, image));
    }

    /// <summary>
    /// The transfer steps (https://html.spec.whatwg.org/multipage/canvas.html#the-offscreencanvas-interface): a canvas with a
    /// context cannot be transferred.
    /// </summary>
    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        // A canvas with a context cannot be transferred; a detached transferable is found after the serialization of the value
        // (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer)
        if (RenderingContext is not null)
            throw new DomException("An OffscreenCanvas with a rendering context cannot be transferred", "InvalidStateError");

        if (serialized && IsDetached)
            throw new DomException("The OffscreenCanvas is detached", "DataCloneError");
    }

    ITransferredData IStructuredTransferable.Transfer()
    {
        var data = new TransferredCanvas(_width, _height, Placeholder, InheritsRightToLeft, InheritedLanguage);
        IsDetached = true;
        Placeholder = null;
        _width = 0;
        _height = 0;
        return data;
    }

    /// <summary>The data of a transferred canvas: the receiving steps create a canvas of the same size, with the same placeholder.</summary>
    private sealed class TransferredCanvas(ulong width, ulong height, HtmlCanvasElement? placeholder, bool inheritsRightToLeft, string? inheritedLanguage) : ITransferredData
    {
        private OffscreenCanvas? _received;

        public object Receive(GlobalScope? target)
        {
            if (_received is null)
            {
                _received = new OffscreenCanvas(width, height) { Placeholder = placeholder, InheritsRightToLeft = inheritsRightToLeft, InheritedLanguage = inheritedLanguage };
                if (target is not null)
                {
                    _received.SetRelevantGlobalCore(target);
                }
            }

            return _received;
        }
    }
}
