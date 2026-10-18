using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The bitmap data of an <see cref="ImageBitmap"/> or of the output bitmap of an <see cref="ImageBitmapRenderingContext"/>
/// (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#concept-imagebitmap-bitmap-data): immutable pixels in
/// the format of the canvases (4 bytes per pixel, R, G, B, A, not premultiplied, sRGB), and its origin-clean flag. The image a
/// rendering backend draws is created on demand (<see cref="GetPaintImage"/>).
/// </summary>
internal sealed class BitmapPixels
{
    /// <summary>The maximum number of pixels of a bitmap (the limit of the surfaces of the backends).</summary>
    public const long MaxPixelCount = 268_435_456;

    private readonly Lock _lock = new();
    private PaintImage? _paintImage;

    public BitmapPixels(int width, int height, byte[] rgba, bool originClean = true)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        IsOriginClean = originClean;
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixels, row by row (4 bytes per pixel, not premultiplied, sRGB). They must not be modified.</summary>
    public byte[] Rgba { get; }

    /// <summary>
    /// Whether the pixels do not come from a resource of another origin (https://html.spec.whatwg.org/multipage/canvas.html#concept-canvas-origin-clean):
    /// a canvas that draws a bitmap that is not origin-clean cannot be read.
    /// </summary>
    public bool IsOriginClean { get; }

    /// <summary>Creates a transparent black bitmap.</summary>
    public static BitmapPixels CreateTransparent(int width, int height) => new(width, height, new byte[(long)width * height * 4]);

    /// <summary>Returns the image a backend draws for the bitmap (created the first time), or null when the backend cannot create it.</summary>
    public PaintImage? GetPaintImage(RenderingBackend backend)
    {
        lock (_lock)
        {
            if (_paintImage is not null && _paintImage.GetDecodedImage(backend) is not null)
                return _paintImage;

            if (Width <= 0 || Height <= 0 || backend.CreateImage(Width, Height, Rgba) is not { } image)
                return null;

            _paintImage = new PaintImage("", backend, image);
            return _paintImage;
        }
    }
}
