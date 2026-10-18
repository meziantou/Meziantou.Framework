using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The orientation of the image of an <see cref="ImageBitmap"/> (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imageorientation):
/// the IDL enumeration <c>ImageOrientation</c>.
/// </summary>
[IdlInterface("ImageOrientation", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imageorientation")]
public enum ImageBitmapOrientation
{
    /// <summary>The image is oriented by its EXIF metadata.</summary>
    [IdlMember("from-image", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imageorientation-from-image")]
    FromImage,

    /// <summary>The image is oriented by its EXIF metadata, then flipped vertically.</summary>
    [IdlMember("flipY", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imageorientation-flipy")]
    FlipY,
}

/// <summary>Whether the colors of an <see cref="ImageBitmap"/> are premultiplied by their alpha (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#premultiplyalpha).</summary>
[IdlInterface("PremultiplyAlpha", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#premultiplyalpha")]
public enum PremultiplyAlpha
{
    /// <summary>The colors are not premultiplied.</summary>
    [IdlMember("none", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-premultiplyalpha-none")]
    None,

    /// <summary>The colors are premultiplied (the precision of the colors of the transparent pixels is reduced).</summary>
    [IdlMember("premultiply", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-premultiplyalpha-premultiply")]
    Premultiply,

    /// <summary>The choice of the user agent (the colors are not premultiplied).</summary>
    [IdlMember("default", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-premultiplyalpha-default")]
    Default,
}

/// <summary>Whether the colors of a decoded image are converted to sRGB (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#colorspaceconversion).</summary>
[IdlInterface("ColorSpaceConversion", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#colorspaceconversion")]
public enum ColorSpaceConversion
{
    /// <summary>The colors are the encoded values (the color profile of the image is ignored).</summary>
    [IdlMember("none", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-colorspaceconversion-none")]
    None,

    /// <summary>The colors are converted to sRGB.</summary>
    [IdlMember("default", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-colorspaceconversion-default")]
    Default,
}

/// <summary>The quality of the scaling of an <see cref="ImageBitmap"/> (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#resizequality).</summary>
[IdlInterface("ResizeQuality", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#resizequality")]
public enum ResizeQuality
{
    /// <summary>The nearest pixel (the pixels stay sharp).</summary>
    [IdlMember("pixelated", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-resizequality-pixelated")]
    Pixelated,

    /// <summary>A bilinear interpolation.</summary>
    [IdlMember("low", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-resizequality-low")]
    Low,

    /// <summary>A bilinear interpolation, with the average of the covered pixels when the image is reduced.</summary>
    [IdlMember("medium", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-resizequality-medium")]
    Medium,

    /// <summary>A bilinear interpolation, with the average of the covered pixels when the image is reduced.</summary>
    [IdlMember("high", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-resizequality-high")]
    High,
}

/// <summary>The options of <c>createImageBitmap()</c> (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagebitmapoptions).</summary>
[IdlInterface("ImageBitmapOptions", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagebitmapoptions")]
public sealed class ImageBitmapOptions
{
    /// <summary>Gets the orientation of the image.</summary>
    [IdlMember("imageOrientation", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-imageorientation")]
    public ImageBitmapOrientation ImageOrientation { get; init; }

    /// <summary>Gets whether the colors are premultiplied by their alpha.</summary>
    [IdlMember("premultiplyAlpha", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-premultiplyalpha")]
    public PremultiplyAlpha PremultiplyAlpha { get; init; } = PremultiplyAlpha.Default;

    /// <summary>Gets whether the colors of a decoded image are converted to sRGB.</summary>
    [IdlMember("colorSpaceConversion", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-colorspaceconversion")]
    public ColorSpaceConversion ColorSpaceConversion { get; init; } = ColorSpaceConversion.Default;

    /// <summary>Gets the width of the bitmap, or null for the width of the source rectangle (or the one of its aspect ratio when <see cref="ResizeHeight"/> is set).</summary>
    [IdlMember("resizeWidth", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-resizewidth")]
    public uint? ResizeWidth { get; init; }

    /// <summary>Gets the height of the bitmap, or null for the height of the source rectangle (or the one of its aspect ratio when <see cref="ResizeWidth"/> is set).</summary>
    [IdlMember("resizeHeight", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-resizeheight")]
    public uint? ResizeHeight { get; init; }

    /// <summary>Gets the quality of the scaling.</summary>
    [IdlMember("resizeQuality", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-resizequality")]
    public ResizeQuality ResizeQuality { get; init; } = ResizeQuality.Low;
}

/// <summary>
/// A bitmap image that can be drawn without delay (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagebitmap):
/// created by <c>createImageBitmap()</c> (<see cref="GlobalScope.CreateImageBitmap(object?, ImageBitmapOptions?)"/>) from an
/// image, a canvas, a blob, an image data or another bitmap. It is serializable and transferable.
/// </summary>
[IdlInterface("ImageBitmap", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagebitmap")]
public sealed class ImageBitmap : IStructuredSerializable, IStructuredTransferable
{
    internal ImageBitmap(BitmapPixels bitmap)
    {
        Bitmap = bitmap;
    }

    /// <summary>The bitmap data, or null when the bitmap is detached (closed or transferred).</summary>
    internal BitmapPixels? Bitmap { get; private set; }

    /// <summary>Gets the width of the bitmap in pixels (0 when it is detached).</summary>
    [IdlMember("width", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmap-width")]
    public uint Width => (uint)(Bitmap?.Width ?? 0);

    /// <summary>Gets the height of the bitmap in pixels (0 when it is detached).</summary>
    [IdlMember("height", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmap-height")]
    public uint Height => (uint)(Bitmap?.Height ?? 0);

    /// <summary>Releases the bitmap data (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmap-close): the bitmap is detached.</summary>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmap-close")]
    public void Close() => Bitmap = null;

    /// <summary>Detaches the bitmap and returns its data (e.g. <c>transferFromImageBitmap()</c>), or null when it is detached.</summary>
    internal BitmapPixels? Detach()
    {
        var bitmap = Bitmap;
        Bitmap = null;
        return bitmap;
    }

    /// <summary>The serialization steps: the bitmap data is immutable, the clone shares it.</summary>
    object IStructuredSerializable.CloneForStructuredSerialization()
    {
        // https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#the-imagebitmap-interface: a bitmap that is
        // not origin-clean cannot be serialized
        if (Bitmap is not { } bitmap)
            throw new DomException("The image bitmap is detached", "DataCloneError");

        if (!bitmap.IsOriginClean)
            throw new DomException("The image bitmap is not origin-clean", "DataCloneError");

        return new ImageBitmap(bitmap);
    }

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        // https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer: a detached transferable
        // is found after the serialization of the value (whose errors are thrown first)
        if (!serialized)
            return;

        if (Bitmap is not { } bitmap)
            throw new DomException("The image bitmap is detached", "DataCloneError");

        if (!bitmap.IsOriginClean)
            throw new DomException("The image bitmap is not origin-clean", "DataCloneError");
    }

    ITransferredData IStructuredTransferable.Transfer() => new TransferredBitmap(Detach()!);

    /// <summary>The data of a transferred bitmap: the receiving steps create a new bitmap with it.</summary>
    private sealed class TransferredBitmap(BitmapPixels bitmap) : ITransferredData
    {
        private ImageBitmap? _received;

        public object Receive(GlobalScope? target) => _received ??= new ImageBitmap(bitmap);
    }
}
