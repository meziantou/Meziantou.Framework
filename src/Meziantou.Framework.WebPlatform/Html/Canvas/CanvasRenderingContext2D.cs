using System.Globalization;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The 2D rendering context of a <c>canvas</c> element (https://html.spec.whatwg.org/multipage/canvas.html#canvasrenderingcontext2d):
/// <c>canvas.getContext("2d")</c>. The drawing is rasterized by the rendering backend of the browsing context; the bitmap is
/// painted in the box of the canvas.
/// </summary>
[IdlInterface("CanvasRenderingContext2D", "https://html.spec.whatwg.org/multipage/canvas.html#canvasrenderingcontext2d")]
public sealed class CanvasRenderingContext2D : CanvasRenderingContext2DBase
{
    internal CanvasRenderingContext2D(HtmlCanvasElement canvas, CanvasBitmap bitmap, CanvasRenderingContext2DSettings settings)
        : base(bitmap, settings)
    {
        Canvas = canvas;
    }

    /// <summary>Gets the canvas element of the context (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-canvas).</summary>
    [IdlMember("canvas", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-canvas")]
    public HtmlCanvasElement Canvas { get; }

    /// <inheritdoc/>
    internal override HtmlCanvasElement? CanvasElement => Canvas;

    /// <inheritdoc/>
    internal override GlobalScope? Global => Canvas.RelevantGlobal;

    /// <inheritdoc/>
    private protected override void OnBitmapChanged() => Canvas.OnBitmapChanged();
}

/// <summary>
/// The 2D rendering context of an <see cref="OffscreenCanvas"/> (https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvasrenderingcontext2d):
/// the same drawing as <see cref="CanvasRenderingContext2D"/>, without the focus rings; it is available in the workers.
/// </summary>
[IdlInterface("OffscreenCanvasRenderingContext2D", "https://html.spec.whatwg.org/multipage/canvas.html#offscreencanvasrenderingcontext2d")]
public sealed class OffscreenCanvasRenderingContext2D : CanvasRenderingContext2DBase
{
    internal OffscreenCanvasRenderingContext2D(OffscreenCanvas canvas, CanvasBitmap bitmap, CanvasRenderingContext2DSettings settings)
        : base(bitmap, settings)
    {
        Canvas = canvas;
    }

    /// <summary>Gets the canvas of the context (https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencontext2d-canvas).</summary>
    [IdlMember("canvas", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreencontext2d-canvas")]
    public OffscreenCanvas Canvas { get; }

    /// <inheritdoc/>
    internal override HtmlCanvasElement? CanvasElement => null;

    /// <inheritdoc/>
    internal override GlobalScope? Global => Canvas.RelevantGlobal;

    /// <inheritdoc/>
    private protected override void OnBitmapChanged() => Canvas.OnBitmapChanged();
}

/// <summary>A canvas whose bitmap a rendering context draws: a <c>canvas</c> element or an <see cref="OffscreenCanvas"/>.</summary>
internal interface ICanvasHost
{
    /// <summary>The width of the bitmap in pixels.</summary>
    int BitmapWidth { get; }

    /// <summary>The height of the bitmap in pixels.</summary>
    int BitmapHeight { get; }

    /// <summary>Called when the pixels of the bitmap changed.</summary>
    void OnBitmapChanged();
}

/// <summary>The creation of the 2D contexts, and the serialization of the bitmaps of the canvases as image files.</summary>
internal static class CanvasContexts
{
    /// <summary>
    /// Creates a 2D context for a canvas of the given size (https://html.spec.whatwg.org/multipage/canvas.html#2d-context-creation-algorithm),
    /// or returns null when there is no rendering backend or the canvas feature is disabled.
    /// </summary>
    public static CanvasBitmap? CreateBitmap(GlobalScope? global, RenderingBackend? backend, int width, int height, CanvasRenderingContext2DSettings settings)
    {
        backend ??= global?.Options.RenderingBackend;
        if (backend is null || (global is not null && !global.Options.EffectiveFeatures.CanvasEnabled))
            return null;

        var colorSpace = settings.ColorSpace is PredefinedColorSpace.DisplayP3 or PredefinedColorSpace.DisplayP3Linear ? RenderingColorSpace.DisplayP3 : RenderingColorSpace.Srgb;
        return new CanvasBitmap(backend, width, height, new RenderingSurfaceOptions(settings.Alpha, colorSpace, settings.WillReadFrequently));
    }

    /// <summary>
    /// Serializes a bitmap as a file (https://html.spec.whatwg.org/multipage/canvas.html#a-serialisation-of-the-bitmap-as-a-file):
    /// PNG (encoded by the library), JPEG or WebP (encoded by the rendering backend); the other types are serialized as PNG.
    /// Returns the bytes and the MIME type of the file, or null when the bitmap has no pixels.
    /// </summary>
    /// <param name="pixels">The pixels of the bitmap.</param>
    /// <param name="type">The requested MIME type (compared ASCII case-insensitively).</param>
    /// <param name="quality">The quality of the lossy formats from 0 to 1, else (or null) the default quality.</param>
    /// <param name="backend">The rendering backend that encodes the lossy formats, or null.</param>
    public static (byte[] Data, string MimeType)? Serialize(BitmapPixels pixels, string? type, double? quality, RenderingBackend? backend)
    {
        if (pixels.Width == 0 || pixels.Height == 0)
            return null;

        var mimeType = InfraAscii.ToAsciiLowercase(type ?? "image/png");
        if (mimeType is "image/jpeg" or "image/webp" && backend is not null)
        {
            if (backend.EncodeImage(pixels.Width, pixels.Height, pixels.Rgba, mimeType, quality is >= 0 and <= 1 ? quality : null) is { } encoded)
                return (encoded, mimeType);
        }

        return (PngEncoder.Encode(pixels.Width, pixels.Height, pixels.Rgba), "image/png");
    }

    /// <summary>Returns a <c>data:</c> URL of an image file (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-todataurl).</summary>
    public static string ToDataUrl((byte[] Data, string MimeType)? file) => file is var (data, mimeType) ? string.Create(CultureInfo.InvariantCulture, $"data:{mimeType};base64,{Convert.ToBase64String(data)}") : "data:,";
}
