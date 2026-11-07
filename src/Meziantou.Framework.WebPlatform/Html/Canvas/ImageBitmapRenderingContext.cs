namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of an <see cref="ImageBitmapRenderingContext"/> (https://html.spec.whatwg.org/multipage/canvas.html#imagebitmaprenderingcontextsettings).</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#imagebitmaprenderingcontextsettings"/>
[IdlInterface("ImageBitmapRenderingContextSettings", "https://html.spec.whatwg.org/multipage/canvas.html#imagebitmaprenderingcontextsettings")]
public sealed class ImageBitmapRenderingContextSettings
{
    /// <summary>Gets whether the canvas has an alpha channel (an opaque canvas displays its bitmaps over black).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontextsettings-alpha"/>
    [IdlMember("alpha", "https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontextsettings-alpha")]
    public bool Alpha { get; init; } = true;
}

/// <summary>
/// The rendering context of a canvas that displays image bitmaps (https://html.spec.whatwg.org/multipage/canvas.html#the-imagebitmaprenderingcontext-interface):
/// <c>canvas.getContext("bitmaprenderer")</c>. Its output bitmap is the bitmap of the canvas: the bitmaps given to
/// <see cref="TransferFromImageBitmap"/> are displayed without copy.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#the-imagebitmaprenderingcontext-interface"/>
[IdlInterface("ImageBitmapRenderingContext", "https://html.spec.whatwg.org/multipage/canvas.html#the-imagebitmaprenderingcontext-interface")]
public sealed class ImageBitmapRenderingContext
{
    private readonly ICanvasHost _host;

    internal ImageBitmapRenderingContext(HtmlCanvasElement canvas, bool alpha)
        : this((ICanvasHost)canvas, alpha)
    {
    }

    internal ImageBitmapRenderingContext(OffscreenCanvas canvas, bool alpha)
        : this((ICanvasHost)canvas, alpha)
    {
    }

    private ImageBitmapRenderingContext(ICanvasHost host, bool alpha)
    {
        _host = host;
        Alpha = alpha;
    }

    /// <summary>Gets the canvas of the context: an <see cref="HtmlCanvasElement"/> or an <see cref="OffscreenCanvas"/>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontext-canvas"/>
    [IdlMember("canvas", "https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontext-canvas")]
    public object Canvas => _host;

    /// <summary>Whether the canvas has an alpha channel (https://html.spec.whatwg.org/multipage/canvas.html#concept-imagebitmaprenderingcontext-alpha): an opaque canvas is rendered over black.</summary>
    internal bool Alpha { get; }

    /// <summary>
    /// The output bitmap, or null when the bitmap mode is blank (https://html.spec.whatwg.org/multipage/canvas.html#concept-imagebitmaprenderingcontext-bitmap-mode):
    /// the bitmap is transparent black, of the size of the canvas.
    /// </summary>
    internal BitmapPixels? OutputBitmap { get; private set; }

    /// <summary>
    /// Displays a bitmap in the canvas (https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontext-transferfromimagebitmap):
    /// the bitmap is detached (its data is now the bitmap of the canvas), or the canvas is cleared when it is null.
    /// </summary>
    /// <param name="bitmap">The bitmap, or null.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the bitmap is detached.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontext-transferfromimagebitmap"/>
    [IdlMember("transferFromImageBitmap", "https://html.spec.whatwg.org/multipage/canvas.html#dom-imagebitmaprenderingcontext-transferfromimagebitmap")]
    public void TransferFromImageBitmap(ImageBitmap? bitmap)
    {
        if (bitmap is null)
        {
            SetOutputBitmap(null);
            return;
        }

        var data = bitmap.Detach() ?? throw new DomException("The image bitmap is detached", "InvalidStateError");
        SetOutputBitmap(data);
    }

    /// <summary>
    /// Returns the pixels of the canvas: the output bitmap, or transparent black pixels of the size of the canvas when the bitmap
    /// mode is blank. As in the browsers, the pixels of a context without alpha channel keep their alpha when they are read (only
    /// the rendering of the canvas is opaque).
    /// </summary>
    internal BitmapPixels GetBitmap() => OutputBitmap ?? BitmapPixels.CreateTransparent(_host.BitmapWidth, _host.BitmapHeight);

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#set-an-imagebitmaprenderingcontext's-output-bitmap</summary>
    private void SetOutputBitmap(BitmapPixels? bitmap)
    {
        OutputBitmap = bitmap;
        _host.OnBitmapChanged();
    }
}
