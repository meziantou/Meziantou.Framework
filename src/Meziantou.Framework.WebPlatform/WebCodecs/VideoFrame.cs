using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The arrangement of the bytes of the planes of a <see cref="VideoFrame"/> (https://w3c.github.io/webcodecs/#enumdef-videopixelformat).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-videopixelformat"/>
[IdlInterface("VideoPixelFormat", "https://w3c.github.io/webcodecs/#enumdef-videopixelformat")]
public enum VideoPixelFormat
{
    /// <summary>Planar 4:2:0 YUV, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i420"/>
    [IdlMember("I420", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i420")]
    I420,

    /// <summary>Planar 4:2:0 YUV, 10 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i420p10"/>
    [IdlMember("I420P10", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i420p10")]
    I420P10,

    /// <summary>Planar 4:2:0 YUV, 12 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i420p12"/>
    [IdlMember("I420P12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i420p12")]
    I420P12,

    /// <summary>Planar 4:2:0 YUV with an alpha plane, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i420a"/>
    [IdlMember("I420A", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i420a")]
    I420A,

    /// <summary>Planar 4:2:0 YUV with an alpha plane, 10 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i420ap10"/>
    [IdlMember("I420AP10", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i420ap10")]
    I420AP10,

    /// <summary>Planar 4:2:0 YUV with an alpha plane, 12 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i420ap12"/>
    [IdlMember("I420AP12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i420ap12")]
    I420AP12,

    /// <summary>Planar 4:2:2 YUV, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i422"/>
    [IdlMember("I422", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i422")]
    I422,

    /// <summary>Planar 4:2:2 YUV, 10 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i422p10"/>
    [IdlMember("I422P10", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i422p10")]
    I422P10,

    /// <summary>Planar 4:2:2 YUV, 12 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i422p12"/>
    [IdlMember("I422P12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i422p12")]
    I422P12,

    /// <summary>Planar 4:2:2 YUV with an alpha plane, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i422a"/>
    [IdlMember("I422A", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i422a")]
    I422A,

    /// <summary>Planar 4:2:2 YUV with an alpha plane, 10 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i422ap10"/>
    [IdlMember("I422AP10", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i422ap10")]
    I422AP10,

    /// <summary>Planar 4:2:2 YUV with an alpha plane, 12 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i422ap12"/>
    [IdlMember("I422AP12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i422ap12")]
    I422AP12,

    /// <summary>Planar 4:4:4 YUV, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i444"/>
    [IdlMember("I444", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i444")]
    I444,

    /// <summary>Planar 4:4:4 YUV, 10 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i444p10"/>
    [IdlMember("I444P10", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i444p10")]
    I444P10,

    /// <summary>Planar 4:4:4 YUV, 12 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i444p12"/>
    [IdlMember("I444P12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i444p12")]
    I444P12,

    /// <summary>Planar 4:4:4 YUV with an alpha plane, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i444a"/>
    [IdlMember("I444A", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i444a")]
    I444A,

    /// <summary>Planar 4:4:4 YUV with an alpha plane, 10 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i444ap10"/>
    [IdlMember("I444AP10", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i444ap10")]
    I444AP10,

    /// <summary>Planar 4:4:4 YUV with an alpha plane, 12 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-i444ap12"/>
    [IdlMember("I444AP12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-i444ap12")]
    I444AP12,

    /// <summary>4:2:0 YUV with a Y plane and an interleaved UV plane, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-nv12"/>
    [IdlMember("NV12", "https://w3c.github.io/webcodecs/#dom-videopixelformat-nv12")]
    NV12,

    /// <summary>One plane of interleaved red, green, blue and alpha samples, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-rgba"/>
    [IdlMember("RGBA", "https://w3c.github.io/webcodecs/#dom-videopixelformat-rgba")]
    RGBA,

    /// <summary>One plane of interleaved red, green, blue and padding samples (opaque), 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-rgbx"/>
    [IdlMember("RGBX", "https://w3c.github.io/webcodecs/#dom-videopixelformat-rgbx")]
    RGBX,

    /// <summary>One plane of interleaved blue, green, red and alpha samples, 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-bgra"/>
    [IdlMember("BGRA", "https://w3c.github.io/webcodecs/#dom-videopixelformat-bgra")]
    BGRA,

    /// <summary>One plane of interleaved blue, green, red and padding samples (opaque), 8 bits per sample.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videopixelformat-bgrx"/>
    [IdlMember("BGRX", "https://w3c.github.io/webcodecs/#dom-videopixelformat-bgrx")]
    BGRX,
}

/// <summary>Whether the alpha channel of a source is kept (https://w3c.github.io/webcodecs/#enumdef-alphaoption).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-alphaoption"/>
[IdlInterface("AlphaOption", "https://w3c.github.io/webcodecs/#enumdef-alphaoption")]
public enum AlphaOption
{
    /// <summary>The alpha channel is kept.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-alphaoption-keep"/>
    [IdlMember("keep", "https://w3c.github.io/webcodecs/#dom-alphaoption-keep")]
    Keep,

    /// <summary>The alpha channel is discarded: the frame has the equivalent opaque format.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-alphaoption-discard"/>
    [IdlMember("discard", "https://w3c.github.io/webcodecs/#dom-alphaoption-discard")]
    Discard,
}

/// <summary>The offset and the stride of a plane in a buffer (https://w3c.github.io/webcodecs/#dictdef-planelayout).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-planelayout"/>
[IdlInterface("PlaneLayout", "https://w3c.github.io/webcodecs/#dictdef-planelayout")]
public sealed class PlaneLayout
{
    /// <summary>Gets the offset in bytes of the plane in the buffer.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-planelayout-offset"/>
    [IdlMember("offset", "https://w3c.github.io/webcodecs/#dom-planelayout-offset")]
    public required uint Offset { get; init; }

    /// <summary>Gets the number of bytes of a row of the plane in the buffer, padding included.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-planelayout-stride"/>
    [IdlMember("stride", "https://w3c.github.io/webcodecs/#dom-planelayout-stride")]
    public required uint Stride { get; init; }
}

/// <summary>The options of <see cref="VideoFrame.CopyTo"/> and <see cref="VideoFrame.AllocationSize"/> (https://w3c.github.io/webcodecs/#dictdef-videoframecopytooptions).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoframecopytooptions"/>
[IdlInterface("VideoFrameCopyToOptions", "https://w3c.github.io/webcodecs/#dictdef-videoframecopytooptions")]
public sealed class VideoFrameCopyToOptions
{
    /// <summary>Gets the rectangle of pixels to copy (the visible rectangle by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-rect"/>
    [IdlMember("rect", "https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-rect")]
    public DomRectInit? Rect { get; init; }

    /// <summary>Gets the offset and the stride of each plane in the destination (tightly packed by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-layout"/>
    [IdlMember("layout", "https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-layout")]
    public IReadOnlyList<PlaneLayout>? Layout { get; init; }

    /// <summary>Gets the format of the pixels in the destination: <c>RGBA</c>, <c>RGBX</c>, <c>BGRA</c> or <c>BGRX</c> (the format of the frame by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-format")]
    public VideoPixelFormat? Format { get; init; }

    /// <summary>Gets the color space of the pixels in the destination when <see cref="Format"/> is given (sRGB by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-colorspace"/>
    [IdlMember("colorSpace", "https://w3c.github.io/webcodecs/#dom-videoframecopytooptions-colorspace")]
    public PredefinedColorSpace? ColorSpace { get; init; }
}

/// <summary>The metadata of a <see cref="VideoFrame"/> (https://w3c.github.io/webcodecs/#dictdef-videoframemetadata): no member is defined by the specification.</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoframemetadata"/>
[IdlInterface("VideoFrameMetadata", "https://w3c.github.io/webcodecs/#dictdef-videoframemetadata")]
public sealed class VideoFrameMetadata
{
}

/// <summary>The options of a <see cref="VideoFrame"/> created from an image or another frame (https://w3c.github.io/webcodecs/#dictdef-videoframeinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoframeinit"/>
[IdlInterface("VideoFrameInit", "https://w3c.github.io/webcodecs/#dictdef-videoframeinit")]
public sealed class VideoFrameInit
{
    /// <summary>Gets the presentation duration in microseconds (the one of the source frame by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-videoframeinit-duration")]
    public ulong? Duration { get; init; }

    /// <summary>Gets the presentation timestamp in microseconds: required for an image, the one of the source frame or video by default.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-videoframeinit-timestamp")]
    public long? Timestamp { get; init; }

    /// <summary>Gets whether the alpha channel of the source is kept.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-alpha"/>
    [IdlMember("alpha", "https://w3c.github.io/webcodecs/#dom-videoframeinit-alpha")]
    public AlphaOption Alpha { get; init; } = AlphaOption.Keep;

    /// <summary>Gets the visible rectangle of the frame (the one of the source by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-visiblerect"/>
    [IdlMember("visibleRect", "https://w3c.github.io/webcodecs/#dom-videoframeinit-visiblerect")]
    public DomRectInit? VisibleRect { get; init; }

    /// <summary>Gets the clockwise rotation in degrees added to the one of the source, rounded to a multiple of 90.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-rotation"/>
    [IdlMember("rotation", "https://w3c.github.io/webcodecs/#dom-videoframeinit-rotation")]
    public double Rotation { get; init; }

    /// <summary>Gets whether the frame is flipped horizontally, relative to the source.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-flip"/>
    [IdlMember("flip", "https://w3c.github.io/webcodecs/#dom-videoframeinit-flip")]
    public bool Flip { get; init; }

    /// <summary>Gets the display width (computed from the source and the visible rectangle by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-displaywidth"/>
    [IdlMember("displayWidth", "https://w3c.github.io/webcodecs/#dom-videoframeinit-displaywidth")]
    public uint? DisplayWidth { get; init; }

    /// <summary>Gets the display height (computed from the source and the visible rectangle by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-displayheight"/>
    [IdlMember("displayHeight", "https://w3c.github.io/webcodecs/#dom-videoframeinit-displayheight")]
    public uint? DisplayHeight { get; init; }

    /// <summary>Gets the metadata of the frame (the one of the source by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframeinit-metadata"/>
    [IdlMember("metadata", "https://w3c.github.io/webcodecs/#dom-videoframeinit-metadata")]
    public VideoFrameMetadata? Metadata { get; init; }
}

/// <summary>The description of the pixels of a <see cref="VideoFrame"/> created from a buffer (https://w3c.github.io/webcodecs/#dictdef-videoframebufferinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-videoframebufferinit"/>
[IdlInterface("VideoFrameBufferInit", "https://w3c.github.io/webcodecs/#dictdef-videoframebufferinit")]
public sealed class VideoFrameBufferInit
{
    /// <summary>Gets the format of the pixels.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-format")]
    public required VideoPixelFormat Format { get; init; }

    /// <summary>Gets the width of the pixels in the buffer.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-codedwidth"/>
    [IdlMember("codedWidth", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-codedwidth")]
    public required uint CodedWidth { get; init; }

    /// <summary>Gets the height of the pixels in the buffer.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-codedheight"/>
    [IdlMember("codedHeight", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-codedheight")]
    public required uint CodedHeight { get; init; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-timestamp")]
    public required long Timestamp { get; init; }

    /// <summary>Gets the presentation duration in microseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-duration")]
    public ulong? Duration { get; init; }

    /// <summary>Gets the offset and the stride of each plane in the buffer (tightly packed by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-layout"/>
    [IdlMember("layout", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-layout")]
    public IReadOnlyList<PlaneLayout>? Layout { get; init; }

    /// <summary>Gets the visible rectangle (the coded rectangle by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-visiblerect"/>
    [IdlMember("visibleRect", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-visiblerect")]
    public DomRectInit? VisibleRect { get; init; }

    /// <summary>Gets the clockwise rotation in degrees, rounded to a multiple of 90.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-rotation"/>
    [IdlMember("rotation", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-rotation")]
    public double Rotation { get; init; }

    /// <summary>Gets whether the frame is flipped horizontally after the rotation.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-flip"/>
    [IdlMember("flip", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-flip")]
    public bool Flip { get; init; }

    /// <summary>Gets the display width (the visible width by default, swapped with the height by a rotation of 90 or 270 degrees).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-displaywidth"/>
    [IdlMember("displayWidth", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-displaywidth")]
    public uint? DisplayWidth { get; init; }

    /// <summary>Gets the display height (the visible height by default, swapped with the width by a rotation of 90 or 270 degrees).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-displayheight"/>
    [IdlMember("displayHeight", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-displayheight")]
    public uint? DisplayHeight { get; init; }

    /// <summary>Gets the color space of the pixels (BT.709 for the YUV formats and sRGB for the RGB formats by default).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-colorspace"/>
    [IdlMember("colorSpace", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-colorspace")]
    public VideoColorSpaceInit? ColorSpace { get; init; }

    /// <summary>Gets the <c>ArrayBuffer</c> objects detached by the construction.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-transfer"/>
    [IdlMember("transfer", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-transfer")]
    public IReadOnlyList<object>? Transfer { get; init; }

    /// <summary>Gets the metadata of the frame.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframebufferinit-metadata"/>
    [IdlMember("metadata", "https://w3c.github.io/webcodecs/#dom-videoframebufferinit-metadata")]
    public VideoFrameMetadata? Metadata { get; init; }
}

/// <summary>
/// The media resource of <see cref="VideoFrame"/> objects (https://w3c.github.io/webcodecs/#media-resource): immutable
/// pixels shared by a frame and its clones. The planes are tightly packed (a row of a plane has the bytes of its samples
/// for the coded width).
/// </summary>
internal sealed class VideoFrameResource
{
    private readonly Lock _lock = new();
    private (VideoPixelFormat Format, VideoColorSpace ColorSpace, byte[] Rgba)? _rgba;
    private byte[][]? _planes;

    public VideoFrameResource(VideoPixelFormat format, int codedWidth, int codedHeight, byte[][] planes)
    {
        Format = format;
        CodedWidth = codedWidth;
        CodedHeight = codedHeight;
        _planes = planes;
    }

    public VideoFrameResource(BitmapPixels bitmap)
    {
        Format = VideoPixelFormat.RGBA;
        CodedWidth = bitmap.Width;
        CodedHeight = bitmap.Height;
        Bitmap = bitmap;
    }

    /// <summary>The format of the planes (a frame with the equivalent opaque format ignores the alpha plane).</summary>
    public VideoPixelFormat Format { get; }

    public int CodedWidth { get; }

    public int CodedHeight { get; }

    /// <summary>The bitmap of a frame created from an image (RGBA, 8 or 16 bits per sample, sRGB or Display P3), or null.</summary>
    public BitmapPixels? Bitmap { get; }

    /// <summary>The planes of the pixels, tightly packed.</summary>
    public byte[][] Planes
    {
        get
        {
            lock (_lock)
            {
                return _planes ??= [Bitmap!.Rgba];
            }
        }
    }

    /// <summary>The size in bytes of the pixels, to bound the memory used by the queues of the codecs.</summary>
    public long ByteLength => Bitmap is { } bitmap ? bitmap.PixelBytes.LongLength : Planes.Sum(plane => plane.LongLength);

    /// <summary>The pixels converted to RGBA (8 bits per sample, not premultiplied) in the RGB color space of the frame (cached for the last format and color space).</summary>
    public byte[] GetRgba(VideoPixelFormat format, VideoColorSpace colorSpace)
    {
        lock (_lock)
        {
            if (_rgba is { } cached && cached.Format == format && SameColorSpace(cached.ColorSpace, colorSpace))
                return cached.Rgba;
        }

        var rgba = Bitmap is { } bitmap && format == VideoPixelFormat.RGBA ? bitmap.Rgba : VideoPixelFormats.ConvertToRgba(format, Planes, CodedWidth, CodedHeight, colorSpace);
        lock (_lock)
        {
            _rgba = (format, colorSpace, rgba);
        }

        return rgba;

        static bool SameColorSpace(VideoColorSpace a, VideoColorSpace b) => a.Primaries == b.Primaries && a.Matrix == b.Matrix && a.FullRange == b.FullRange && a.Transfer == b.Transfer;
    }
}

/// <summary>
/// A frame of video or a decoded image (https://w3c.github.io/webcodecs/#videoframe): its pixels in one of the formats of
/// <see cref="VideoPixelFormat"/>, the visible rectangle, the display size, the orientation, the timestamp and the color
/// space. A frame is created from an image (a canvas, an image element, an <see cref="ImageBitmap"/>, a video element...),
/// from a buffer of pixels, from another frame, by an <see cref="ImageDecoder"/> or by a <see cref="VideoDecoder"/>. A
/// frame can be drawn on a canvas, used by <c>createImageBitmap()</c>, encoded by a <see cref="VideoEncoder"/>, posted to a
/// worker (serialized or transferred) and copied to a buffer in its format or converted to RGB.
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#videoframe"/>
[IdlInterface("VideoFrame", "https://w3c.github.io/webcodecs/#videoframe")]
public sealed class VideoFrame : IStructuredSerializable, IStructuredTransferable, IAgentClusterSerializable
{
    private VideoFrameResource? _resource;
    private BitmapPixels? _rendered;
    private VideoPixelFormat? _format;
    private uint _codedWidth;
    private uint _codedHeight;
    private uint _visibleLeft;
    private uint _visibleTop;
    private uint _visibleWidth;
    private uint _visibleHeight;
    private double _rotation;
    private bool _flip;
    private uint _displayWidth;
    private uint _displayHeight;
    private VideoFrameMetadata? _metadata;

    /// <summary>Creates a frame of pixels (https://w3c.github.io/webcodecs/#create-a-videoframe).</summary>
    /// <param name="pixels">The pixels of the frame (its coded size), before the rotation and the flip.</param>
    /// <param name="timestamp">The presentation timestamp in microseconds.</param>
    /// <param name="duration">The presentation duration in microseconds, or null.</param>
    /// <param name="rotation">The clockwise rotation applied when the frame is rendered: 0, 90, 180 or 270.</param>
    /// <param name="flip">Whether the frame is flipped horizontally after the rotation when it is rendered.</param>
    internal VideoFrame(BitmapPixels pixels, long timestamp, ulong? duration, int rotation, bool flip)
    {
        _resource = new VideoFrameResource(pixels);
        _format = VideoPixelFormat.RGBA;
        _codedWidth = (uint)pixels.Width;
        _codedHeight = (uint)pixels.Height;
        (_visibleWidth, _visibleHeight) = (_codedWidth, _codedHeight);
        _rotation = rotation;
        _flip = flip;
        (_displayWidth, _displayHeight) = rotation is 90 or 270 ? (_codedHeight, _codedWidth) : (_codedWidth, _codedHeight);
        Timestamp = timestamp;
        Duration = duration;

        // https://w3c.github.io/webcodecs/#pick-color-space: the color space of an RGB format is sRGB
        ColorSpace = pixels.ColorSpace == RenderingColorSpace.DisplayP3 ? VideoColorSpace.CreateDisplayP3() : VideoColorSpace.CreateSrgb();
    }

    /// <summary>Creates a frame that shares the resource of a frame (a clone, or the serialized form of a frame).</summary>
    private VideoFrame(VideoFrame frame)
    {
        _resource = frame._resource;
        _rendered = frame._rendered;
        _format = frame._format;
        _codedWidth = frame._codedWidth;
        _codedHeight = frame._codedHeight;
        _visibleLeft = frame._visibleLeft;
        _visibleTop = frame._visibleTop;
        _visibleWidth = frame._visibleWidth;
        _visibleHeight = frame._visibleHeight;
        _rotation = frame._rotation;
        _flip = frame._flip;
        _displayWidth = frame._displayWidth;
        _displayHeight = frame._displayHeight;
        _metadata = frame._metadata;
        Timestamp = frame.Timestamp;
        Duration = frame.Duration;
        ColorSpace = frame.ColorSpace;
    }

    /// <summary>
    /// Creates a frame from an image or another frame (https://w3c.github.io/webcodecs/#dom-videoframe-videoframe): a copy of
    /// the bitmap of an <c>img</c>, SVG <c>image</c> or <c>canvas</c> element, an <see cref="ImageBitmap"/> or an
    /// <see cref="OffscreenCanvas"/> (whose timestamp is required); the frame at the current playback position of a
    /// <c>video</c> element; or the pixels of another <see cref="VideoFrame"/> (shared, with a new visible rectangle,
    /// orientation or display size).
    /// </summary>
    /// <param name="image">The image source.</param>
    /// <param name="init">The options of the frame.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the image is not usable (e.g. a closed frame, a canvas without pixels), a <c>SecurityError</c> when it is not origin-clean.</exception>
    /// <exception cref="TypeErrorException">The timestamp is missing, or the options are invalid.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-videoframe"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-videoframe-videoframe")]
    public VideoFrame(object? image, VideoFrameInit? init = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        init ??= new VideoFrameInit();
        ColorSpace = VideoColorSpace.CreateSrgb();
        if (image is VideoFrame other)
        {
            if (other._resource is not { } otherResource)
                throw new DomException("The video frame is closed", "InvalidStateError");

            InitializeFromOtherFrame(init, other, otherResource);
            return;
        }

        if (image is HtmlVideoElement video)
        {
            // https://w3c.github.io/webcodecs/#dom-videoframe-videoframe: the frame at the current playback position
            if (video.ReadyState < HtmlMediaElement.HaveCurrentData || video.GetVideoFrame() is not { } mediaFrame)
                throw new DomException("The video has no frame", "InvalidStateError");

            if (!video.IsMediaDataCorsSameOrigin)
                throw new DomException("The video is not origin-clean", "SecurityError");

            var pixels = new BitmapPixels(mediaFrame.Width, mediaFrame.Height, mediaFrame.Pixels.ToArray());
            var timestamp = init.Timestamp ?? (long)Math.Round(video.CurrentTime * 1_000_000, MidpointRounding.AwayFromZero);
            InitializeFromBitmap(init, pixels, timestamp);
            return;
        }

        // The images of the styles are always tainted (as in Chromium)
        if (image is Css.TypedOm.CssImageValue)
            throw new DomException("The image is not origin-clean", "SecurityError");

        var global = GlobalScope.Current ?? (image as Node)?.OwnerDocument?.DefaultView ?? throw new DomException("The image cannot be used", "InvalidStateError");
        var bitmap = ImageBitmapFactory.GetSourceBitmap(global, image, new ImageBitmapOptions());
        if (!bitmap.IsOriginClean)
            throw new DomException("The image is not origin-clean", "SecurityError");

        if (init.Timestamp is not { } imageTimestamp)
            throw new TypeErrorException("The timestamp is required to create a frame from an image");

        InitializeFromBitmap(init, bitmap, imageTimestamp);
    }

    /// <summary>
    /// Creates a frame from a buffer of pixels (https://w3c.github.io/webcodecs/#dom-videoframe-videoframe-data-init): the
    /// planes of the format are read from the buffer at the offsets and strides of the layout (tightly packed by default),
    /// and copied.
    /// </summary>
    /// <param name="data">The pixels: a buffer source of the scripts, a <see cref="byte"/> array or a <see cref="ReadOnlyMemory{T}"/> of bytes.</param>
    /// <param name="init">The description of the pixels.</param>
    /// <exception cref="TypeErrorException">The description is invalid, or the buffer is too small.</exception>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the transfer list has a detached buffer or a buffer twice.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-videoframe-data-init"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-videoframe-videoframe-data-init")]
    public VideoFrame(object data, VideoFrameBufferInit init)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(init);
        var bytes = WebCodecsBuffers.GetBytes(data);

        // https://w3c.github.io/webcodecs/#validate-videoframebufferinit
        if (init.CodedWidth == 0 || init.CodedHeight == 0)
            throw new TypeErrorException("The coded width and height must not be zero");

        if (init.CodedWidth > VideoPixelFormats.MaxDimension || init.CodedHeight > VideoPixelFormats.MaxDimension)
            throw new TypeErrorException("The coded size is too large");

        if (init.VisibleRect is { } visibleRect)
            ValidateRectValues(visibleRect);

        ValidateDisplaySize(init.DisplayWidth, init.DisplayHeight);
        var (visibleLeft, visibleTop, visibleWidth, visibleHeight) = ParseVisibleRect((0, 0, init.CodedWidth, init.CodedHeight), init.VisibleRect, init.CodedWidth, init.CodedHeight, init.Format);
        var layout = ComputeLayout((0, 0, init.CodedWidth, init.CodedHeight), init.Format, init.Layout);
        if (bytes.Length < layout.AllocationSize)
            throw new TypeErrorException("The buffer is smaller than the pixels of the frame");

        var transfer = WebCodecsBuffers.ValidateTransfer(init.Transfer);

        // The visible pixels are copied, tightly packed: the coded size of the resource is the visible size
        var planeInfos = VideoPixelFormats.GetPlanes(init.Format);
        var planes = new byte[planeInfos.Length][];
        for (var planeIndex = 0; planeIndex < planeInfos.Length; planeIndex++)
        {
            var info = planeInfos[planeIndex];
            var rowBytes = (int)info.GetRowBytes(visibleWidth);
            var rows = (int)info.GetRows(visibleHeight);
            var top = visibleTop / info.SampleHeight;
            var leftBytes = (visibleLeft / info.SampleWidth) * info.SampleBytes;
            var plane = new byte[(long)rowBytes * rows];
            var planeLayout = layout.Planes[planeIndex];
            for (var row = 0; row < rows; row++)
            {
                bytes.Span.Slice((int)(planeLayout.DestinationOffset + ((top + row) * planeLayout.DestinationStride) + leftBytes), rowBytes).CopyTo(plane.AsSpan(row * rowBytes, rowBytes));
            }

            planes[planeIndex] = plane;
        }

        WebCodecsBuffers.DetachTransfer(transfer);
        _resource = new VideoFrameResource(init.Format, (int)visibleWidth, (int)visibleHeight, planes);
        _format = init.Format;
        _codedWidth = visibleWidth;
        _codedHeight = visibleHeight;
        (_visibleLeft, _visibleTop, _visibleWidth, _visibleHeight) = (0, 0, visibleWidth, visibleHeight);
        _rotation = ParseRotation(init.Rotation);
        _flip = init.Flip;
        if (init.DisplayWidth is { } displayWidth && init.DisplayHeight is { } displayHeight)
        {
            (_displayWidth, _displayHeight) = (displayWidth, displayHeight);
        }
        else
        {
            (_displayWidth, _displayHeight) = _rotation is 90 or 270 ? (visibleHeight, visibleWidth) : (visibleWidth, visibleHeight);
        }

        Timestamp = init.Timestamp;
        Duration = init.Duration;
        ColorSpace = PickColorSpace(init.ColorSpace, init.Format);
        _metadata = init.Metadata;
    }

    /// <summary>Gets whether the frame is closed ([[Detached]]).</summary>
    internal bool IsDetached => _resource is null;

    /// <summary>The resource of the frame, or null when the frame is closed.</summary>
    internal VideoFrameResource? Resource => _resource;

    /// <summary>Gets the format of the pixels, or null when the frame is closed.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-format"/>
    [IdlMember("format", "https://w3c.github.io/webcodecs/#dom-videoframe-format")]
    public VideoPixelFormat? Format => _format;

    /// <summary>Gets the width of the pixels (before the rotation).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-codedwidth"/>
    [IdlMember("codedWidth", "https://w3c.github.io/webcodecs/#dom-videoframe-codedwidth")]
    public uint CodedWidth => _codedWidth;

    /// <summary>Gets the height of the pixels (before the rotation).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-codedheight"/>
    [IdlMember("codedHeight", "https://w3c.github.io/webcodecs/#dom-videoframe-codedheight")]
    public uint CodedHeight => _codedHeight;

    /// <summary>Gets the rectangle of the pixels at (0, 0), or null when the frame is closed.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-codedrect"/>
    [IdlMember("codedRect", "https://w3c.github.io/webcodecs/#dom-videoframe-codedrect")]
    public DomRectReadOnly? CodedRect => IsDetached ? null : new DomRectReadOnly(0, 0, _codedWidth, _codedHeight);

    /// <summary>Gets the rectangle of the visible pixels, or null when the frame is closed.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-visiblerect"/>
    [IdlMember("visibleRect", "https://w3c.github.io/webcodecs/#dom-videoframe-visiblerect")]
    public DomRectReadOnly? VisibleRect => IsDetached ? null : new DomRectReadOnly(_visibleLeft, _visibleTop, _visibleWidth, _visibleHeight);

    /// <summary>Gets the clockwise rotation in degrees applied when the frame is rendered, before the flip.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-rotation"/>
    [IdlMember("rotation", "https://w3c.github.io/webcodecs/#dom-videoframe-rotation")]
    public double Rotation => _rotation;

    /// <summary>Gets whether the frame is flipped horizontally when it is rendered, after the rotation.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-flip"/>
    [IdlMember("flip", "https://w3c.github.io/webcodecs/#dom-videoframe-flip")]
    public bool Flip => _flip;

    /// <summary>Gets the width of the rendered frame (after the rotation).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-displaywidth"/>
    [IdlMember("displayWidth", "https://w3c.github.io/webcodecs/#dom-videoframe-displaywidth")]
    public uint DisplayWidth => _displayWidth;

    /// <summary>Gets the height of the rendered frame (after the rotation).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-displayheight"/>
    [IdlMember("displayHeight", "https://w3c.github.io/webcodecs/#dom-videoframe-displayheight")]
    public uint DisplayHeight => _displayHeight;

    /// <summary>Gets the presentation duration in microseconds, or null (e.g. a still image).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-videoframe-duration")]
    public ulong? Duration { get; private set; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-videoframe-timestamp")]
    public long Timestamp { get; private set; }

    /// <summary>Gets the color space of the pixels.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-colorspace"/>
    [IdlMember("colorSpace", "https://w3c.github.io/webcodecs/#dom-videoframe-colorspace")]
    public VideoColorSpace ColorSpace { get; private set; }

    /// <summary>Returns a copy of the metadata of the frame (https://w3c.github.io/webcodecs/#dom-videoframe-metadata).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the frame is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-metadata"/>
    [IdlMember("metadata", "https://w3c.github.io/webcodecs/#dom-videoframe-metadata")]
    public VideoFrameMetadata Metadata()
    {
        ThrowIfDetached();
        return new VideoFrameMetadata();
    }

    /// <summary>Returns the number of bytes <see cref="CopyTo"/> writes with the options (https://w3c.github.io/webcodecs/#dom-videoframe-allocationsize).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the frame is closed, a <c>NotSupportedError</c> for a destination format other than RGBA, RGBX, BGRA and BGRX.</exception>
    /// <exception cref="TypeErrorException">The rectangle or the layout is invalid.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-allocationsize"/>
    [IdlMember("allocationSize", "https://w3c.github.io/webcodecs/#dom-videoframe-allocationsize")]
    public uint AllocationSize(VideoFrameCopyToOptions? options = null)
    {
        ThrowIfDetached();
        return ParseCopyToOptions(options ?? new VideoFrameCopyToOptions()).Layout.AllocationSize;
    }

    /// <summary>
    /// Copies the pixels of the frame into a buffer (https://w3c.github.io/webcodecs/#dom-videoframe-copyto): the planes of
    /// its format, or the pixels converted to an RGB format and to a color space when the options give a format.
    /// </summary>
    /// <returns>A task that completes with the layout of the planes in the buffer (in a task of the event loop).</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-copyto"/>
    [IdlMember("copyTo", "https://w3c.github.io/webcodecs/#dom-videoframe-copyto")]
    public Task<IReadOnlyList<PlaneLayout>> CopyTo(Memory<byte> destination, VideoFrameCopyToOptions? options = null)
    {
        IReadOnlyList<PlaneLayout> planeLayouts;
        try
        {
            planeLayouts = CopyPixelsTo(destination.Span, options);
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException)
        {
            return Task.FromException<IReadOnlyList<PlaneLayout>>(ex);
        }

        var completion = new TaskCompletionSource<IReadOnlyList<PlaneLayout>>();
        if (GlobalScope.Current is { } global)
        {
            global.QueueGlobalTask(() => completion.TrySetResult(planeLayouts));
        }
        else
        {
            completion.TrySetResult(planeLayouts);
        }

        return completion.Task;
    }

    /// <summary>
    /// Copies the pixels of the frame into a buffer at once (the synchronous form of <see cref="CopyTo"/>, e.g. for the
    /// encoders of a <see cref="Media.MediaCodecBackend"/>): the planes of its format, or the pixels converted to an RGB
    /// format and to a color space when the options give a format.
    /// </summary>
    /// <param name="destination">The buffer: at least <see cref="AllocationSize"/> bytes.</param>
    /// <param name="options">The rectangle, the layout, the format and the color space of the copy.</param>
    /// <returns>The layout of the planes in the buffer.</returns>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the frame is closed, a <c>NotSupportedError</c> for a destination format other than RGBA, RGBX, BGRA and BGRX.</exception>
    /// <exception cref="TypeErrorException">The rectangle or the layout is invalid, or the buffer is too small.</exception>
    [NotInIdl("The synchronous copy of the pixels for the .NET code")]
    public IReadOnlyList<PlaneLayout> CopyPixelsTo(Span<byte> destination, VideoFrameCopyToOptions? options = null)
    {
        options ??= new VideoFrameCopyToOptions();
        if (_resource is not { } resource || _format is not { } frameFormat)
            throw new DomException("The frame is closed", "InvalidStateError");

        var parsed = ParseCopyToOptions(options);
        var layout = parsed.Layout;
        if (destination.Length < layout.AllocationSize)
            throw new TypeErrorException("The destination is smaller than the allocation size");

        var span = destination;
        if (options.Format is { } rgbFormat)
        {
            // https://w3c.github.io/webcodecs/#convert-to-rgb-frame: the pixels in RGBA, then in the color space of the options
            var rgba = GetRgba(resource, frameFormat, options.ColorSpace ?? PredefinedColorSpace.Srgb);
            var planeLayout = layout.Planes[0];
            for (var row = 0L; row < planeLayout.SourceHeight; row++)
            {
                var sourceRow = rgba.AsSpan((int)((((planeLayout.SourceTop + row) * resource.CodedWidth) * 4) + planeLayout.SourceLeftBytes), (int)planeLayout.SourceWidthBytes);
                var destinationRow = span.Slice((int)(planeLayout.DestinationOffset + (row * planeLayout.DestinationStride)), (int)planeLayout.SourceWidthBytes);
                CopyRgbaRow(sourceRow, destinationRow, rgbFormat);
            }
        }
        else
        {
            var planeInfos = VideoPixelFormats.GetPlanes(frameFormat);
            var planes = resource.Planes;
            for (var planeIndex = 0; planeIndex < layout.Planes.Length; planeIndex++)
            {
                var planeLayout = layout.Planes[planeIndex];
                var sourceRowBytes = planeInfos[planeIndex].GetRowBytes(resource.CodedWidth);
                for (var row = 0L; row < planeLayout.SourceHeight; row++)
                {
                    var destinationRow = span.Slice((int)(planeLayout.DestinationOffset + (row * planeLayout.DestinationStride)), (int)planeLayout.SourceWidthBytes);
                    planes[planeIndex].AsSpan((int)(((planeLayout.SourceTop + row) * sourceRowBytes) + planeLayout.SourceLeftBytes), (int)planeLayout.SourceWidthBytes).CopyTo(destinationRow);

                    // The padding byte of the opaque RGB formats is copied as an opaque alpha
                    if (frameFormat is VideoPixelFormat.RGBX or VideoPixelFormat.BGRX)
                    {
                        for (var i = 3; i < destinationRow.Length; i += 4)
                        {
                            destinationRow[i] = 255;
                        }
                    }
                }
            }
        }

        return [.. layout.Planes.Select(plane => new PlaneLayout { Offset = (uint)plane.DestinationOffset, Stride = (uint)plane.DestinationStride })];
    }

    /// <summary>Creates a frame that references the same pixels (https://w3c.github.io/webcodecs/#dom-videoframe-clone).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the frame is closed.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-clone"/>
    [IdlMember("clone", "https://w3c.github.io/webcodecs/#dom-videoframe-clone")]
    public VideoFrame Clone()
    {
        ThrowIfDetached();
        return new VideoFrame(this);
    }

    /// <summary>Releases the reference to the pixels (https://w3c.github.io/webcodecs/#close-videoframe): the frame cannot be used anymore.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-videoframe-close")]
    public void Close()
    {
        _resource = null;
        _rendered = null;
        _format = null;
        _codedWidth = 0;
        _codedHeight = 0;
        _visibleLeft = 0;
        _visibleTop = 0;
        _visibleWidth = 0;
        _visibleHeight = 0;
        _rotation = 0;
        _flip = false;
        _displayWidth = 0;
        _displayHeight = 0;
        ColorSpace = new VideoColorSpace();
    }

    /// <summary>
    /// Returns the pixels of the rendered frame (https://w3c.github.io/webcodecs/#videoframe-rendering): the visible
    /// rectangle scaled to the display size, rotated clockwise by the rotation, then flipped horizontally when
    /// <see cref="Flip"/> is set. Returns null when the frame is closed.
    /// </summary>
    internal BitmapPixels? GetRenderedPixels()
    {
        if (_resource is not { } resource || _format is not { } format)
            return null;

        if (_rendered is { } rendered)
            return rendered;

        BitmapPixels visible;
        if (resource.Bitmap is { } bitmap && format == VideoPixelFormat.RGBA && _visibleLeft == 0 && _visibleTop == 0 && _visibleWidth == resource.CodedWidth && _visibleHeight == resource.CodedHeight)
        {
            visible = bitmap;
        }
        else
        {
            var rgba = resource.GetRgba(format, ColorSpace);
            var cropped = new byte[(long)_visibleWidth * _visibleHeight * 4];
            for (var row = 0; row < _visibleHeight; row++)
            {
                rgba.AsSpan((int)(((_visibleTop + row) * (long)resource.CodedWidth + _visibleLeft) * 4), (int)_visibleWidth * 4).CopyTo(cropped.AsSpan((int)(row * (long)_visibleWidth * 4)));
            }

            visible = new BitmapPixels((int)_visibleWidth, (int)_visibleHeight, cropped, colorSpace: VideoPixelFormats.ToRenderingColorSpace(VideoPixelFormats.GetRgbColorSpace(ColorSpace)));
        }

        // The display size before the rotation; a display size larger than the bitmaps is not applied
        var (scaledWidth, scaledHeight) = _rotation is 90 or 270 ? (_displayHeight, _displayWidth) : (_displayWidth, _displayHeight);
        if ((scaledWidth != visible.Width || scaledHeight != visible.Height) && scaledWidth > 0 && scaledHeight > 0 && (long)scaledWidth * scaledHeight <= BitmapPixels.MaxPixelCount)
        {
            var resized = ImageBitmapFactory.Resize(visible.PixelBytes, visible.Width, visible.Height, (int)scaledWidth, (int)scaledHeight, ResizeQuality.Low, visible.PixelFormat);
            visible = new BitmapPixels((int)scaledWidth, (int)scaledHeight, resized, visible.IsOriginClean, visible.PixelFormat, visible.ColorSpace);
        }

        rendered = _rotation == 0 && !_flip ? visible : Orient(visible, (int)_rotation, _flip);
        _rendered = rendered;
        return rendered;
    }

    /// <summary>Replaces the color space of the frame (the color space of the configuration of a decoder).</summary>
    internal void OverrideColorSpace(VideoColorSpace colorSpace)
    {
        ColorSpace = colorSpace;
        _rendered = null;
    }

    /// <summary>Returns the visible pixels in RGBA (8 bits per sample, not premultiplied, sRGB) and their size, before the orientation (for the encoders and the sinks).</summary>
    internal (int Width, int Height, byte[] Rgba)? GetVisibleRgba()
    {
        if (_resource is not { } resource || _format is not { } format)
            return null;

        var rgba = GetRgba(resource, format, PredefinedColorSpace.Srgb);
        if (_visibleLeft == 0 && _visibleTop == 0 && _visibleWidth == resource.CodedWidth && _visibleHeight == resource.CodedHeight)
            return ((int)_visibleWidth, (int)_visibleHeight, rgba);

        var cropped = new byte[(long)_visibleWidth * _visibleHeight * 4];
        for (var row = 0; row < _visibleHeight; row++)
        {
            rgba.AsSpan((int)(((_visibleTop + row) * (long)resource.CodedWidth + _visibleLeft) * 4), (int)_visibleWidth * 4).CopyTo(cropped.AsSpan((int)(row * (long)_visibleWidth * 4)));
        }

        return ((int)_visibleWidth, (int)_visibleHeight, cropped);
    }

    /// <summary>Returns the pixels of the coded rectangle in RGBA, converted to a color space.</summary>
    private byte[] GetRgba(VideoFrameResource resource, VideoPixelFormat format, PredefinedColorSpace colorSpace)
    {
        var current = VideoPixelFormats.GetRgbColorSpace(ColorSpace);
        if (resource.Bitmap is { } bitmap && format == VideoPixelFormat.RGBA)
        {
            current = bitmap.ColorSpace == RenderingColorSpace.DisplayP3 ? PredefinedColorSpace.DisplayP3 : PredefinedColorSpace.Srgb;
            if (current != colorSpace && bitmap.PixelFormat == ImageDataPixelFormat.RgbaFloat16)
            {
                // The samples of a wide gamut keep their precision until they are converted
                var halves = (byte[])bitmap.PixelBytes.Clone();
                CanvasRenderingContext2DBase.ConvertColorSpace(unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(halves.AsSpan())), current, colorSpace);
                var bytes = new byte[(long)bitmap.Width * bitmap.Height * 4];
                CanvasRenderingContext2DBase.CopyHalfToBytes(halves, bytes);
                return bytes;
            }
        }

        var rgba = resource.GetRgba(format, ColorSpace);
        if (current == colorSpace)
            return rgba;

        var converted = (byte[])rgba.Clone();
        CanvasRenderingContext2DBase.ConvertColorSpace(converted, current, colorSpace);
        return converted;
    }

    private void ThrowIfDetached()
    {
        if (IsDetached)
            throw new DomException("The frame is closed", "InvalidStateError");
    }

    /// <summary>Initializes a frame created from an image (https://w3c.github.io/webcodecs/#dom-videoframe-videoframe).</summary>
    private void InitializeFromBitmap(VideoFrameInit init, BitmapPixels bitmap, long timestamp)
    {
        var format = init.Alpha == AlphaOption.Discard ? VideoPixelFormat.RGBX : VideoPixelFormat.RGBA;
        var (width, height) = ((uint)bitmap.Width, (uint)bitmap.Height);
        ValidateVideoFrameInit(init, format, width, height);
        _resource = new VideoFrameResource(bitmap);
        _format = format;
        (_codedWidth, _codedHeight) = (width, height);
        InitializeVisibleRectOrientationAndDisplaySize(init, (0, 0, width, height), baseRotation: 0, baseFlip: false, width, height);
        Timestamp = timestamp;
        Duration = init.Duration;
        ColorSpace = bitmap.ColorSpace == RenderingColorSpace.DisplayP3 ? VideoColorSpace.CreateDisplayP3() : VideoColorSpace.CreateSrgb();
        _metadata = init.Metadata;
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-initialize-frame-from-other-frame</summary>
    private void InitializeFromOtherFrame(VideoFrameInit init, VideoFrame other, VideoFrameResource resource)
    {
        var format = other._format!.Value;
        if (init.Alpha == AlphaOption.Discard)
            format = VideoPixelFormats.GetOpaqueFormat(format);

        ValidateVideoFrameInit(init, format, other._codedWidth, other._codedHeight);
        _resource = resource;
        _format = format;
        (_codedWidth, _codedHeight) = (other._codedWidth, other._codedHeight);
        ColorSpace = other.ColorSpace;
        InitializeVisibleRectOrientationAndDisplaySize(init, (other._visibleLeft, other._visibleTop, other._visibleWidth, other._visibleHeight), other._rotation, other._flip, other._displayWidth, other._displayHeight);
        Duration = init.Duration ?? other.Duration;
        Timestamp = init.Timestamp ?? other.Timestamp;
        _metadata = init.Metadata ?? other._metadata;
    }

    /// <summary>https://w3c.github.io/webcodecs/#validate-videoframeinit</summary>
    private static void ValidateVideoFrameInit(VideoFrameInit init, VideoPixelFormat format, uint codedWidth, uint codedHeight)
    {
        if (init.VisibleRect is { } visibleRect)
        {
            ValidateRectValues(visibleRect);
            ParseVisibleRect((0, 0, codedWidth, codedHeight), visibleRect, codedWidth, codedHeight, format);
        }

        ValidateDisplaySize(init.DisplayWidth, init.DisplayHeight);
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-initialize-visible-rect-and-display-size</summary>
    private void InitializeVisibleRectOrientationAndDisplaySize(VideoFrameInit init, (uint X, uint Y, uint Width, uint Height) defaultVisibleRect, double baseRotation, bool baseFlip, uint defaultDisplayWidth, uint defaultDisplayHeight)
    {
        var visible = init.VisibleRect is { } rect ? ParseVisibleRect(defaultVisibleRect, rect, _codedWidth, _codedHeight, _format) : defaultVisibleRect;
        (_visibleLeft, _visibleTop, _visibleWidth, _visibleHeight) = visible;
        var rotation = ParseRotation(init.Rotation);
        _rotation = AddRotations(baseRotation, baseFlip, rotation);
        _flip = baseFlip != init.Flip;
        if (init.DisplayWidth is { } displayWidth && init.DisplayHeight is { } displayHeight)
        {
            (_displayWidth, _displayHeight) = (displayWidth, displayHeight);
            return;
        }

        var (widthScale, heightScale) = baseRotation is 0 or 180
            ? ((double)defaultDisplayWidth / defaultVisibleRect.Width, (double)defaultDisplayHeight / defaultVisibleRect.Height)
            : ((double)defaultDisplayHeight / defaultVisibleRect.Width, (double)defaultDisplayWidth / defaultVisibleRect.Height);
        var width = Math.Round(visible.Width * widthScale, MidpointRounding.AwayFromZero);
        var height = Math.Round(visible.Height * heightScale, MidpointRounding.AwayFromZero);
        if (width < 1 || height < 1 || width > uint.MaxValue || height > uint.MaxValue)
            throw new TypeErrorException("The computed display size is invalid");

        (_displayWidth, _displayHeight) = _rotation is 0 or 180 ? ((uint)width, (uint)height) : ((uint)height, (uint)width);
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-parse-rotation: the nearest multiple of 90 (ties towards positive infinity), in [0, 360).</summary>
    private static double ParseRotation(double rotation)
    {
        if (!double.IsFinite(rotation))
            throw new TypeErrorException("The rotation must be finite");

        // The remainder of a division of doubles is exact: the rotation is reduced to [0, 360) before it is rounded
        var reduced = rotation % 360;
        if (reduced < 0)
            reduced += 360;

        var quarterTurns = Math.Floor((reduced / 90) + 0.5);
        return quarterTurns * 90 % 360;
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-add-rotations</summary>
    private static double AddRotations(double baseRotation, bool baseFlip, double rotation)
    {
        var combined = baseFlip ? baseRotation - rotation : baseRotation + rotation;
        combined %= 360;
        return combined < 0 ? combined + 360 : combined + 0;
    }

    private static void ValidateRectValues(DomRectInit rect)
    {
        if (!IsValid(rect.X) || !IsValid(rect.Y) || !IsValid(rect.Width) || !IsValid(rect.Height))
            throw new TypeErrorException("The coordinates of the rectangle must be finite and not negative");

        static bool IsValid(double value) => double.IsFinite(value) && value >= 0;
    }

    private static void ValidateDisplaySize(uint? displayWidth, uint? displayHeight)
    {
        if (displayWidth.HasValue != displayHeight.HasValue)
            throw new TypeErrorException("The display width and the display height must be given together");

        if (displayWidth == 0 || displayHeight == 0)
            throw new TypeErrorException("The display width and the display height must not be zero");
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-parse-visible-rect</summary>
    private static (uint X, uint Y, uint Width, uint Height) ParseVisibleRect((uint X, uint Y, uint Width, uint Height) defaultRect, DomRectInit? overrideRect, uint codedWidth, uint codedHeight, VideoPixelFormat? format)
    {
        var sourceRect = defaultRect;
        if (overrideRect is not null)
        {
            ValidateRectValues(overrideRect);
            if (overrideRect.Width == 0 || overrideRect.Height == 0)
                throw new TypeErrorException("The width and the height of the rectangle must not be zero");

            if (overrideRect.X + overrideRect.Width > codedWidth || overrideRect.Y + overrideRect.Height > codedHeight)
                throw new TypeErrorException("The rectangle is outside of the coded rectangle");

            sourceRect = ((uint)overrideRect.X, (uint)overrideRect.Y, (uint)overrideRect.Width, (uint)overrideRect.Height);
            if (sourceRect.Width == 0 || sourceRect.Height == 0)
                throw new TypeErrorException("The width and the height of the rectangle must not be zero");
        }

        if (!VideoPixelFormats.IsRectOffsetAligned(format, sourceRect.X, sourceRect.Y))
            throw new TypeErrorException("The offset of the rectangle is not aligned with the sub-sampling of the format");

        return sourceRect;
    }

    /// <summary>
    /// Parses the options of <c>copyTo()</c> (https://w3c.github.io/webcodecs/#videoframe-parse-videoframecopytooptions):
    /// the rectangle of pixels, the format of the destination and the layout of its planes.
    /// </summary>
    private (CombinedLayout Layout, VideoPixelFormat Format, (long X, long Y, long Width, long Height) Rect) ParseCopyToOptions(VideoFrameCopyToOptions options)
    {
        if (_format is not { } frameFormat)
            throw new DomException("The frame has no format", "NotSupportedError");

        var format = frameFormat;
        if (options.Format is { } requested)
        {
            if (!VideoPixelFormats.IsRgb(requested))
                throw new DomException("The pixels can only be converted to an RGB format", "NotSupportedError");

            format = requested;
        }

        var rect = ParseVisibleRect((_visibleLeft, _visibleTop, _visibleWidth, _visibleHeight), options.Rect, _codedWidth, _codedHeight, format);
        return (ComputeLayout(rect, format, options.Layout), format, (rect.X, rect.Y, rect.Width, rect.Height));
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-compute-layout-and-allocation-size</summary>
    private static CombinedLayout ComputeLayout((uint X, uint Y, uint Width, uint Height) rect, VideoPixelFormat format, IReadOnlyList<PlaneLayout>? layout)
    {
        var planeInfos = VideoPixelFormats.GetPlanes(format);
        if (layout is not null && layout.Count != planeInfos.Length)
            throw new TypeErrorException($"The layout must have {planeInfos.Length} planes");

        var minAllocationSize = 0L;
        var computed = new ComputedPlaneLayout[planeInfos.Length];
        var endOffsets = new long[planeInfos.Length];
        for (var planeIndex = 0; planeIndex < planeInfos.Length; planeIndex++)
        {
            var plane = planeInfos[planeIndex];
            var sourceTop = VideoPlaneInfo.DivideRoundUp(rect.Y, plane.SampleHeight);
            var sourceHeight = VideoPlaneInfo.DivideRoundUp(rect.Height, plane.SampleHeight);
            var sourceLeftBytes = VideoPlaneInfo.DivideRoundUp(rect.X, plane.SampleWidth) * plane.SampleBytes;
            var sourceWidthBytes = VideoPlaneInfo.DivideRoundUp(rect.Width, plane.SampleWidth) * plane.SampleBytes;
            long destinationOffset;
            long destinationStride;
            if (layout is not null)
            {
                var planeLayout = layout[planeIndex];
                if (planeLayout.Stride < sourceWidthBytes)
                    throw new TypeErrorException("The stride of a plane is smaller than its width");

                (destinationOffset, destinationStride) = (planeLayout.Offset, planeLayout.Stride);
            }
            else
            {
                (destinationOffset, destinationStride) = (minAllocationSize, sourceWidthBytes);
            }

            var planeSize = destinationStride * sourceHeight;
            var planeEnd = planeSize + destinationOffset;
            if (planeSize > uint.MaxValue || planeEnd > uint.MaxValue)
                throw new TypeErrorException("The plane is too large");

            endOffsets[planeIndex] = planeEnd;
            minAllocationSize = Math.Max(minAllocationSize, planeEnd);
            for (var earlierIndex = 0; earlierIndex < planeIndex; earlierIndex++)
            {
                if (planeEnd > computed[earlierIndex].DestinationOffset && endOffsets[earlierIndex] > destinationOffset)
                    throw new TypeErrorException("The planes of the layout overlap");
            }

            computed[planeIndex] = new ComputedPlaneLayout(destinationOffset, destinationStride, sourceTop, sourceHeight, sourceLeftBytes, sourceWidthBytes);
        }

        return new CombinedLayout((uint)minAllocationSize, computed);
    }

    /// <summary>https://w3c.github.io/webcodecs/#videoframe-pick-color-space</summary>
    private static VideoColorSpace PickColorSpace(VideoColorSpaceInit? overrideColorSpace, VideoPixelFormat format)
    {
        if (overrideColorSpace is not null)
            return new VideoColorSpace(overrideColorSpace);

        if (VideoPixelFormats.IsRgb(format))
            return VideoColorSpace.CreateSrgb();

        return new VideoColorSpace(new VideoColorSpaceInit { Primaries = VideoColorPrimaries.Bt709, Transfer = VideoTransferCharacteristics.Bt709, Matrix = VideoMatrixCoefficients.Bt709, FullRange = false });
    }

    /// <summary>Copies a row of RGBA pixels in an RGB format: the samples are swapped for BGR, and the alpha is opaque for the formats without alpha.</summary>
    private static void CopyRgbaRow(ReadOnlySpan<byte> source, Span<byte> destination, VideoPixelFormat format)
    {
        source.CopyTo(destination);
        if (VideoPixelFormats.IsBgr(format))
        {
            for (var i = 0; i + 3 < destination.Length; i += 4)
            {
                (destination[i], destination[i + 2]) = (destination[i + 2], destination[i]);
            }
        }

        if (!VideoPixelFormats.HasAlpha(format))
        {
            for (var i = 3; i < destination.Length; i += 4)
            {
                destination[i] = 255;
            }
        }
    }

    /// <summary>Rotates the pixels clockwise, then flips them horizontally.</summary>
    internal static BitmapPixels Orient(BitmapPixels pixels, int rotation, bool flip)
    {
        var bytesPerPixel = pixels.PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 8 : 4;
        var (width, height) = (pixels.Width, pixels.Height);
        var (outputWidth, outputHeight) = rotation is 90 or 270 ? (height, width) : (width, height);
        var source = pixels.PixelBytes;
        var output = new byte[source.Length];
        for (var y = 0; y < outputHeight; y++)
        {
            for (var x = 0; x < outputWidth; x++)
            {
                // The flip is undone, then the rotation: the source pixel of the output pixel
                var rotatedX = flip ? outputWidth - 1 - x : x;
                var (sourceX, sourceY) = rotation switch
                {
                    90 => (y, height - 1 - rotatedX),
                    180 => (width - 1 - rotatedX, height - 1 - y),
                    270 => (width - 1 - y, rotatedX),
                    _ => (rotatedX, y),
                };

                source.AsSpan(((sourceY * width) + sourceX) * bytesPerPixel, bytesPerPixel).CopyTo(output.AsSpan(((y * outputWidth) + x) * bytesPerPixel));
            }
        }

        return new BitmapPixels(outputWidth, outputHeight, output, pixels.IsOriginClean, pixels.PixelFormat, pixels.ColorSpace);
    }

    /// <summary>The serialization steps (https://w3c.github.io/webcodecs/#videoframe-serialization): the serialized frame shares the resource.</summary>
    object IStructuredSerializable.CloneForStructuredSerialization()
    {
        ThrowDataCloneErrorIfDetached();
        return new VideoFrame(this);
    }

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        if (serialized)
            ThrowDataCloneErrorIfDetached();
    }

    /// <summary>The transfer steps (https://w3c.github.io/webcodecs/#videoframe-transfer-and-serialization): the frame is closed, the received frame has its resource.</summary>
    ITransferredData IStructuredTransferable.Transfer()
    {
        var clone = new VideoFrame(this);
        Close();
        return new TransferredVideoFrame(clone, GlobalScope.Current?.AgentClusterKey);
    }

    private void ThrowDataCloneErrorIfDetached()
    {
        if (IsDetached)
            throw new DomException("The video frame is closed", "DataCloneError");
    }

    /// <summary>The data of a transferred frame: the receiving steps return the frame.</summary>
    private sealed class TransferredVideoFrame(VideoFrame frame, object? agentCluster) : ITransferredData
    {
        public object Receive(GlobalScope? target)
        {
            // The resource cannot leave the agent cluster
            if (agentCluster is not null && target?.AgentClusterKey is { } targetAgentCluster && !ReferenceEquals(agentCluster, targetAgentCluster))
                throw new DomException("The video frame cannot be received in another agent cluster", "DataCloneError");

            return frame;
        }
    }

    /// <summary>The layout of the planes of a copy (https://w3c.github.io/webcodecs/#combined-buffer-layout).</summary>
    private sealed record CombinedLayout(uint AllocationSize, ComputedPlaneLayout[] Planes);

    /// <summary>The layout of a plane of a copy (https://w3c.github.io/webcodecs/#computed-plane-layout).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct ComputedPlaneLayout(long DestinationOffset, long DestinationStride, long SourceTop, long SourceHeight, long SourceLeftBytes, long SourceWidthBytes);
}
