using System.ComponentModel;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The color spaces of the canvases and of the image data (https://html.spec.whatwg.org/multipage/canvas.html#predefinedcolorspace).</summary>
[IdlInterface("PredefinedColorSpace", "https://html.spec.whatwg.org/multipage/canvas.html#predefinedcolorspace")]
public enum PredefinedColorSpace
{
    /// <summary>sRGB.</summary>
    [IdlMember("srgb", "https://html.spec.whatwg.org/multipage/canvas.html#dom-predefinedcolorspace-srgb")]
    Srgb,

    /// <summary>Linear-light sRGB.</summary>
    [IdlMember("srgb-linear", "https://html.spec.whatwg.org/multipage/canvas.html#dom-predefinedcolorspace-srgb-linear")]
    SrgbLinear,

    /// <summary>Display P3.</summary>
    [IdlMember("display-p3", "https://html.spec.whatwg.org/multipage/canvas.html#dom-predefinedcolorspace-display-p3")]
    DisplayP3,

    /// <summary>Linear-light Display P3.</summary>
    [IdlMember("display-p3-linear", "https://html.spec.whatwg.org/multipage/canvas.html#dom-predefinedcolorspace-display-p3-linear")]
    DisplayP3Linear,
}

/// <summary>The formats of the pixels of an <see cref="ImageData"/> (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedatapixelformat).</summary>
[IdlInterface("ImageDataPixelFormat", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedatapixelformat")]
public enum ImageDataPixelFormat
{
    /// <summary>4 bytes per pixel (R, G, B, A), in a <c>Uint8ClampedArray</c>.</summary>
    [IdlMember("rgba-unorm8", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedatapixelformat-rgba-unorm8")]
    RgbaUnorm8,

    /// <summary>4 half-precision floats per pixel (R, G, B, A, from 0 to 1), in a <c>Float16Array</c>.</summary>
    [IdlMember("rgba-float16", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedatapixelformat-rgba-float16")]
    RgbaFloat16,
}

/// <summary>The options of an <see cref="ImageData"/> (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedatasettings).</summary>
[IdlInterface("ImageDataSettings", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedatasettings")]
public sealed class ImageDataSettings
{
    /// <summary>Gets the color space of the pixels (sRGB when it is not set).</summary>
    [IdlMember("colorSpace", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedatasettings-colorspace")]
    public PredefinedColorSpace? ColorSpace { get; init; }

    /// <summary>Gets the format of the pixels.</summary>
    [IdlMember("pixelFormat", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedatasettings-pixelformat")]
    public ImageDataPixelFormat PixelFormat { get; init; }
}

/// <summary>
/// A rectangle of pixels (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedata): its width, its
/// height, and its pixels, row by row, 4 components per pixel (R, G, B, A, not premultiplied), as bytes or half-precision floats.
/// </summary>
/// <remarks>
/// The pixels are shared with the scripts: an <see cref="ImageData"/> created from a <c>Uint8ClampedArray</c> views its bytes,
/// and the array returned by the <c>data</c> attribute to the scripts is the one the .NET code reads and writes (<see cref="Data"/>).
/// </remarks>
[IdlInterface("ImageData", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedata")]
public sealed class ImageData : IStructuredSerializable
{
    // The maximum number of bytes of the pixels (the size of the largest array of the browsers is of the same order)
    private const long MaxByteLength = int.MaxValue;

    private readonly JsBufferSource _data;

    /// <summary>
    /// Creates an image data of transparent black pixels (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata).
    /// </summary>
    /// <param name="sw">The width in pixels.</param>
    /// <param name="sh">The height in pixels.</param>
    /// <param name="settings">The color space and the format of the pixels.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the width or the height is 0 or the pixels are too large.</exception>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata")]
    public ImageData(uint sw, uint sh, ImageDataSettings? settings = null)
    {
        if (sw == 0 || sh == 0)
            throw new DomException("The width and the height of the image data must not be zero", "IndexSizeError");

        var pixelFormat = settings?.PixelFormat ?? ImageDataPixelFormat.RgbaUnorm8;
        var elementSize = pixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 2 : 1;
        if ((double)sw * sh * 4 * elementSize > MaxByteLength)
            throw new DomException("The image data is too large", "IndexSizeError");

        var length = (long)sw * sh * 4 * elementSize;

        Width = sw;
        Height = sh;
        PixelFormat = pixelFormat;
        ColorSpace = settings?.ColorSpace ?? PredefinedColorSpace.Srgb;
        _data = new JsBufferSource(elementSize == 2 ? JsBufferKind.Float16Array : JsBufferKind.Uint8ClampedArray, new byte[length]);
    }

    /// <summary>
    /// Creates an image data viewing pixels (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-with-data):
    /// the <c>Uint8ClampedArray</c> or <c>Float16Array</c> of a script, or bytes (an array or a <see cref="Memory{T}"/> of bytes,
    /// which are not copied).
    /// </summary>
    /// <param name="data">The pixels.</param>
    /// <param name="sw">The width in pixels.</param>
    /// <param name="sh">The height in pixels, or null to compute it from the length of the data.</param>
    /// <param name="settings">The color space and the format of the pixels (the format of the data).</param>
    /// <exception cref="DomException">
    /// An <c>InvalidStateError</c> when the number of components is not a nonzero multiple of 4 or the format does not match the
    /// array, an <c>IndexSizeError</c> when the number of pixels is not a multiple of the width or does not match the height.
    /// </exception>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-with-data")]
    public ImageData(object data, uint sw, uint? sh = null, ImageDataSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data switch
        {
            JsBufferSource source => source,
            byte[] bytes => new JsBufferSource(settings?.PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? JsBufferKind.Float16Array : JsBufferKind.Uint8ClampedArray, bytes),
            Memory<byte> memory => new JsBufferSource(settings?.PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? JsBufferKind.Float16Array : JsBufferKind.Uint8ClampedArray, memory),
            _ => throw new ArgumentException("The data must be bytes or a Uint8ClampedArray or Float16Array", nameof(data)),
        };

        // The format of the pixels is the one of the array
        var pixelFormat = _data.Kind == JsBufferKind.Float16Array ? ImageDataPixelFormat.RgbaFloat16 : ImageDataPixelFormat.RgbaUnorm8;
        if (settings is not null && settings.PixelFormat != pixelFormat)
            throw new DomException("The format of the pixels does not match the array", "InvalidStateError");

        var length = _data.Bytes.Length / JsBufferSource.GetElementSize(_data.Kind);
        if (length == 0 || length % 4 != 0)
            throw new DomException("The length of the data must be a nonzero multiple of 4", "InvalidStateError");

        var pixels = length / 4;
        if (sw == 0 || pixels % sw != 0)
            throw new DomException("The length of the data is not a multiple of the width", "IndexSizeError");

        var height = (uint)(pixels / sw);
        if (sh is { } expectedHeight && expectedHeight != height)
            throw new DomException("The length of the data does not match the height", "IndexSizeError");

        Width = sw;
        Height = height;
        PixelFormat = pixelFormat;
        ColorSpace = settings?.ColorSpace ?? PredefinedColorSpace.Srgb;
    }

    /// <summary>Creates an image data of pixels in the format of the canvases (RGBA bytes, not premultiplied, sRGB), e.g. for <c>getImageData()</c>.</summary>
    internal ImageData(uint width, uint height, byte[] rgba)
    {
        Width = width;
        Height = height;
        PixelFormat = ImageDataPixelFormat.RgbaUnorm8;
        ColorSpace = PredefinedColorSpace.Srgb;
        _data = new JsBufferSource(JsBufferKind.Uint8ClampedArray, rgba);
    }

    /// <summary>Gets the width in pixels.</summary>
    [IdlMember("width", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-width")]
    public uint Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    [IdlMember("height", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-height")]
    public uint Height { get; }

    /// <summary>Gets the format of the pixels.</summary>
    [IdlMember("pixelFormat", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-pixelformat")]
    public ImageDataPixelFormat PixelFormat { get; }

    /// <summary>Gets the color space of the pixels.</summary>
    [IdlMember("colorSpace", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-colorspace")]
    public PredefinedColorSpace ColorSpace { get; }

    /// <summary>
    /// Gets the bytes of the pixels: 4 bytes per pixel (R, G, B, A) for <see cref="ImageDataPixelFormat.RgbaUnorm8"/>, 4
    /// half-precision floats (<see cref="Half"/>, in the byte order of the platform) for <see cref="ImageDataPixelFormat.RgbaFloat16"/>.
    /// The bytes are shared with the array of the scripts; they are empty when its buffer is detached.
    /// </summary>
    [NotInIdl("The pixels for the .NET code: the data attribute is DataArray")]
    public Memory<byte> Data => _data.Bytes;

    /// <summary>Gets the array of the pixels for the scripts (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-data): use <see cref="Data"/> in .NET code.</summary>
    [IdlMember("data", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagedata-data")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object DataArray => _data;

    /// <summary>Whether the buffer of the pixels is detached (e.g. transferred to a worker).</summary>
    internal bool IsDetached => _data.Bytes.Length != (long)Width * Height * 4 * (PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 2 : 1);

    /// <summary>
    /// Returns a copy of the pixels in the format of the canvases: RGBA bytes, not premultiplied (the float components are
    /// clamped and rounded). The colors are not converted from <see cref="ColorSpace"/>.
    /// </summary>
    internal byte[] ToRgbaBytes()
    {
        var bytes = _data.Bytes.Span;
        if (PixelFormat == ImageDataPixelFormat.RgbaUnorm8)
            return bytes.ToArray();

        var halves = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(bytes));
        var result = new byte[halves.Length];
        for (var i = 0; i < halves.Length; i++)
        {
            var value = (double)halves[i];
            result[i] = double.IsNaN(value) ? (byte)0 : (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
        }

        return result;
    }

    /// <summary>The serialization steps (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#imagedata): a copy of the pixels.</summary>
    object IStructuredSerializable.CloneForStructuredSerialization()
    {
        if (IsDetached)
            throw new DomException("The buffer of the image data is detached", "DataCloneError");

        return new ImageData(_data.Bytes.ToArray(), Width, Height, new ImageDataSettings { ColorSpace = ColorSpace, PixelFormat = PixelFormat });
    }
}
