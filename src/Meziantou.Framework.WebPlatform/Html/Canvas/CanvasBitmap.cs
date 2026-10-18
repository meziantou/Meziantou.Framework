using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The output bitmap of a 2D context (https://html.spec.whatwg.org/multipage/canvas.html#output-bitmap): a surface of the
/// rendering backend, and its origin-clean flag. The drawing operations of the context are recorded in a display list, which
/// is rasterized on the surface when the pixels are needed (read by <c>getImageData()</c>, drawn into another canvas or into
/// the page, serialized), so a sequence of drawing operations is rasterized at once. The surface lives as long as the context
/// (it is released by the garbage collector).
/// </summary>
internal sealed class CanvasBitmap
{
    private readonly RenderingBackend _backend;
    private RenderingSurface? _surface;
    private DisplayListBuilder? _pending;
    private PaintImage? _snapshot;
    private bool _isRecording;

    public CanvasBitmap(RenderingBackend backend, int width, int height, RenderingSurfaceOptions options)
    {
        _backend = backend;
        Options = options;
        Resize(width, height);
    }

    private CanvasBitmap(RenderingBackend backend, RenderingSurfaceOptions options)
    {
        _backend = backend;
        Options = options;
        _isRecording = true;
    }

    /// <summary>
    /// Creates a bitmap that only records its drawing operations (the output bitmap of a <see cref="PaintRenderingContext2D"/>):
    /// it has no surface, and its display list is taken by <see cref="TakeRecording"/> to be drawn at any resolution. Its
    /// pixels cannot be read.
    /// </summary>
    public static CanvasBitmap CreateRecording(RenderingBackend backend, int width, int height, RenderingSurfaceOptions options)
    {
        var bitmap = new CanvasBitmap(backend, options);
        bitmap.Resize(width, height);
        return bitmap;
    }

    /// <summary>The rendering backend of the surface.</summary>
    public RenderingBackend Backend => _backend;

    /// <summary>The properties of the surface (alpha, color space, will read frequently).</summary>
    public RenderingSurfaceOptions Options { get; }

    /// <summary>The width of the bitmap in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>The height of the bitmap in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Whether the bitmap has pixels: false for a bitmap of zero width or height, or too large for the backend.</summary>
    public bool HasPixels => _surface is not null || (_isRecording && Width > 0 && Height > 0);

    /// <summary>
    /// The origin-clean flag of the bitmap (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-origin-clean):
    /// false once an image of another origin was drawn; the pixels can then no longer be read by the scripts.
    /// </summary>
    public bool IsOriginClean { get; set; } = true;

    /// <summary>Incremented each time the pixels change.</summary>
    public int Version { get; private set; }

    /// <summary>The display list of the drawing operations that are not rasterized yet.</summary>
    public DisplayListBuilder Builder => _pending ??= new DisplayListBuilder();

    /// <summary>Records that drawing operations were added to <see cref="Builder"/> (the snapshot of the pixels is outdated).</summary>
    public void OnChanged()
    {
        _snapshot = null;
        Version++;
    }

    /// <summary>
    /// Replaces the bitmap with a new one of the given size, transparent black (opaque black without alpha), with a clean
    /// origin (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-set-bitmap-dimensions).
    /// </summary>
    public void Resize(int width, int height)
    {
        _surface?.Dispose();
        _pending = null;
        Width = width;
        Height = height;
        _surface = !_isRecording && width > 0 && height > 0 && (long)width * height <= BitmapPixels.MaxPixelCount ? _backend.CreateSurface(width, height, Options with { IsCanvasBitmap = true }) : null;
        IsOriginClean = true;
        OnChanged();
    }

    /// <summary>Clears the bitmap to transparent black (opaque black without alpha), dropping the operations not rasterized yet.</summary>
    public void Clear()
    {
        _pending = null;
        _surface?.Clear(Options.HasAlpha ? DisplayColor.Transparent : DisplayColor.Black);
        OnChanged();
    }

    /// <summary>
    /// Returns the display list of the drawing operations of a recording bitmap (see <see cref="CreateRecording"/>), and
    /// stops the recording: the next drawing operations are ignored.
    /// </summary>
    public DisplayList TakeRecording()
    {
        var list = _pending?.ToDisplayList(new PhysicalSize(Width, Height)) ?? DisplayList.Empty;
        _pending = null;
        _isRecording = false;
        return list;
    }

    /// <summary>Rasterizes the drawing operations recorded since the last call.</summary>
    public void Flush()
    {
        // The operations of a recording bitmap are kept in the display list
        if (_isRecording)
            return;

        if (_pending is not { Count: > 0 } pending)
            return;

        _pending = null;
        _surface?.Draw(pending.ToDisplayList(new PhysicalSize(Width, Height)));
    }

    /// <summary>Copies the pixels of a rectangle (RGBA, not premultiplied, in the color space of the bitmap); the pixels outside of the bitmap are transparent black.</summary>
    public void ReadPixels(int x, int y, int width, int height, Span<byte> destination)
    {
        Flush();
        if (_surface is null)
        {
            destination[..(width * height * 4)].Clear();
            return;
        }

        _surface.ReadPixels(x, y, width, height, destination);
    }

    /// <summary>Replaces the pixels of a rectangle (RGBA, not premultiplied, in the color space of the bitmap), without compositing.</summary>
    public void WritePixels(int x, int y, int width, int height, ReadOnlySpan<byte> source)
    {
        Flush();
        _surface?.WritePixels(x, y, width, height, source);
        OnChanged();
    }

    /// <summary>Returns an immutable image of the current pixels (cached until they change), or null when the bitmap has no pixels.</summary>
    public PaintImage? GetSnapshot()
    {
        if (_snapshot is not null)
            return _snapshot;

        Flush();
        if (_surface is null)
            return null;

        _snapshot = new PaintImage("", _backend, _surface.CreateSnapshot());
        return _snapshot;
    }

    /// <summary>
    /// Returns a copy of the pixels (RGBA, not premultiplied, sRGB: the pixels of a Display P3 bitmap are converted) and the
    /// origin-clean flag (e.g. for <c>createImageBitmap()</c> and the serialization).
    /// </summary>
    public BitmapPixels GetPixels()
    {
        var pixels = new byte[(long)Width * Height * 4];
        if (Width > 0 && Height > 0)
        {
            ReadPixels(0, 0, Width, Height, pixels);
            if (Options.ColorSpace == RenderingColorSpace.DisplayP3)
            {
                CanvasRenderingContext2DBase.ConvertColorSpace(pixels, PredefinedColorSpace.DisplayP3, PredefinedColorSpace.Srgb);
            }
        }

        return new BitmapPixels(Width, Height, pixels, IsOriginClean);
    }
}
