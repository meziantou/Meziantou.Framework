using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

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

/// <summary>The offset and the stride of a plane copied by <see cref="VideoFrame.CopyTo"/> (https://w3c.github.io/webcodecs/#dictdef-planelayout).</summary>
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

/// <summary>
/// A frame of video or a decoded image (https://w3c.github.io/webcodecs/#videoframe): the frames created by an
/// <see cref="ImageDecoder"/>. Its pixels are RGBA (8 bits per sample, not premultiplied, in the sRGB color space, or Display
/// P3 for an image whose colors are outside of sRGB); its rotation and flip are the EXIF orientation of the image. A frame can
/// be drawn on a canvas and used by <c>createImageBitmap()</c>; the frames cannot be constructed by the scripts (the decoders
/// and the encoders of audio and video are not implemented).
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#videoframe"/>
[IdlInterface("VideoFrame", "https://w3c.github.io/webcodecs/#videoframe")]
public sealed class VideoFrame
{
    private BitmapPixels? _pixels;
    private BitmapPixels? _rendered;
    private VideoPixelFormat? _format;
    private uint _codedWidth;
    private uint _codedHeight;
    private double _rotation;
    private bool _flip;
    private uint _displayWidth;
    private uint _displayHeight;

    /// <summary>Creates a frame of pixels (https://w3c.github.io/webcodecs/#create-a-videoframe).</summary>
    /// <param name="pixels">The pixels of the frame (its coded size), before the rotation and the flip.</param>
    /// <param name="timestamp">The presentation timestamp in microseconds.</param>
    /// <param name="duration">The presentation duration in microseconds, or null.</param>
    /// <param name="rotation">The clockwise rotation applied when the frame is rendered: 0, 90, 180 or 270.</param>
    /// <param name="flip">Whether the frame is flipped horizontally after the rotation when it is rendered.</param>
    internal VideoFrame(BitmapPixels pixels, long timestamp, ulong? duration, int rotation, bool flip)
    {
        _pixels = pixels;
        _format = VideoPixelFormat.RGBA;
        _codedWidth = (uint)pixels.Width;
        _codedHeight = (uint)pixels.Height;
        _rotation = rotation;
        _flip = flip;
        (_displayWidth, _displayHeight) = rotation is 90 or 270 ? (_codedHeight, _codedWidth) : (_codedWidth, _codedHeight);
        Timestamp = timestamp;
        Duration = duration;

        // https://w3c.github.io/webcodecs/#pick-color-space: the color space of an RGB format is sRGB
        ColorSpace = pixels.ColorSpace == RenderingColorSpace.DisplayP3 ? VideoColorSpace.CreateDisplayP3() : VideoColorSpace.CreateSrgb();
    }

    private VideoFrame(VideoFrame frame)
    {
        _pixels = frame._pixels;
        _rendered = frame._rendered;
        _format = frame._format;
        _codedWidth = frame._codedWidth;
        _codedHeight = frame._codedHeight;
        _rotation = frame._rotation;
        _flip = frame._flip;
        _displayWidth = frame._displayWidth;
        _displayHeight = frame._displayHeight;
        Timestamp = frame.Timestamp;
        Duration = frame.Duration;
        ColorSpace = frame.ColorSpace;
    }

    /// <summary>Gets whether the frame is closed ([[Detached]]).</summary>
    internal bool IsDetached => _pixels is null;

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

    /// <summary>Gets the rectangle of the visible pixels (all the pixels of a decoded image), or null when the frame is closed.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-visiblerect"/>
    [IdlMember("visibleRect", "https://w3c.github.io/webcodecs/#dom-videoframe-visiblerect")]
    public DomRectReadOnly? VisibleRect => IsDetached ? null : new DomRectReadOnly(0, 0, _codedWidth, _codedHeight);

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
    public ulong? Duration { get; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-videoframe-timestamp")]
    public long Timestamp { get; }

    /// <summary>Gets the color space of the pixels.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-colorspace"/>
    [IdlMember("colorSpace", "https://w3c.github.io/webcodecs/#dom-videoframe-colorspace")]
    public VideoColorSpace ColorSpace { get; }

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
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the frame is closed, a <c>NotSupportedError</c> for a format other than RGBA, RGBX, BGRA and BGRX.</exception>
    /// <exception cref="TypeErrorException">The rectangle or the layout is invalid.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-allocationsize"/>
    [IdlMember("allocationSize", "https://w3c.github.io/webcodecs/#dom-videoframe-allocationsize")]
    public uint AllocationSize(VideoFrameCopyToOptions? options = null)
    {
        ThrowIfDetached();
        return ParseCopyToOptions(options ?? new VideoFrameCopyToOptions()).AllocationSize;
    }

    /// <summary>
    /// Copies the pixels of the frame into a buffer (https://w3c.github.io/webcodecs/#dom-videoframe-copyto), converted to the
    /// format and the color space of the options when they are given.
    /// </summary>
    /// <returns>A task that completes with the layout of the planes in the buffer (in a task of the event loop).</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-videoframe-copyto"/>
    [IdlMember("copyTo", "https://w3c.github.io/webcodecs/#dom-videoframe-copyto")]
    public Task<IReadOnlyList<PlaneLayout>> CopyTo(Memory<byte> destination, VideoFrameCopyToOptions? options = null)
    {
        options ??= new VideoFrameCopyToOptions();
        if (_pixels is not { } pixels)
            return Task.FromException<IReadOnlyList<PlaneLayout>>(new DomException("The frame is closed", "InvalidStateError"));

        CopyLayout layout;
        try
        {
            layout = ParseCopyToOptions(options);
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException)
        {
            return Task.FromException<IReadOnlyList<PlaneLayout>>(ex);
        }

        if (destination.Length < layout.AllocationSize)
            return Task.FromException<IReadOnlyList<PlaneLayout>>(new TypeErrorException("The destination is smaller than the allocation size"));

        // The pixels are RGBA: a copy in their format keeps their color space; a format given by the options converts them
        // to the color space of the options (sRGB by default, https://w3c.github.io/webcodecs/#convert-to-rgb-frame)
        var format = options.Format ?? VideoPixelFormat.RGBA;
        var source = options.Format is null ? pixels.Rgba : ConvertColorSpace(pixels, options.ColorSpace ?? PredefinedColorSpace.Srgb);
        var span = destination.Span;
        var sourceOffset = (layout.SourceTop * pixels.Width * 4) + layout.SourceLeftBytes;
        var destinationOffset = (long)layout.DestinationOffset;
        for (var row = 0; row < layout.SourceHeight; row++)
        {
            var sourceRow = source.AsSpan((int)sourceOffset, (int)layout.SourceWidthBytes);
            var destinationRow = span.Slice((int)destinationOffset, (int)layout.SourceWidthBytes);
            CopyRow(sourceRow, destinationRow, format);
            sourceOffset += pixels.Width * 4;
            destinationOffset += layout.DestinationStride;
        }

        IReadOnlyList<PlaneLayout> planeLayouts = [new PlaneLayout { Offset = layout.DestinationOffset, Stride = layout.DestinationStride }];
        var completion = new TaskCompletionSource<IReadOnlyList<PlaneLayout>>();
        if (Dom.GlobalScope.Current is { } global)
        {
            global.QueueGlobalTask(() => completion.TrySetResult(planeLayouts));
        }
        else
        {
            completion.TrySetResult(planeLayouts);
        }

        return completion.Task;
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
        _pixels = null;
        _rendered = null;
        _format = null;
        _codedWidth = 0;
        _codedHeight = 0;
        _rotation = 0;
        _flip = false;
        _displayWidth = 0;
        _displayHeight = 0;
    }

    /// <summary>
    /// Returns the pixels of the rendered frame (https://w3c.github.io/webcodecs/#videoframe-rendering): rotated clockwise by
    /// the rotation, then flipped horizontally when <see cref="Flip"/> is set; their size is the display size. Returns null
    /// when the frame is closed.
    /// </summary>
    internal BitmapPixels? GetRenderedPixels()
    {
        if (_pixels is not { } pixels)
            return null;

        if (_rotation == 0 && !_flip)
            return pixels;

        return _rendered ??= Orient(pixels, (int)_rotation, _flip);
    }

    private void ThrowIfDetached()
    {
        if (IsDetached)
            throw new DomException("The frame is closed", "InvalidStateError");
    }

    /// <summary>
    /// Parses the options of <c>copyTo()</c> (https://w3c.github.io/webcodecs/#parse-videoframecopytooptions): the rectangle
    /// of pixels and the layout of the only plane of the RGB formats
    /// (https://w3c.github.io/webcodecs/#compute-layout-and-allocation-size).
    /// </summary>
    private CopyLayout ParseCopyToOptions(VideoFrameCopyToOptions options)
    {
        // https://w3c.github.io/webcodecs/#parse-visible-rect: the coordinates are truncated
        var (x, y, width, height) = (0L, 0L, (long)_codedWidth, (long)_codedHeight);
        if (options.Rect is { } rect)
        {
            if (!IsValid(rect.X) || !IsValid(rect.Y) || !IsValid(rect.Width) || !IsValid(rect.Height))
                throw new TypeErrorException("The coordinates of the rectangle must be finite and not negative");

            (x, y, width, height) = ((long)rect.X, (long)rect.Y, (long)rect.Width, (long)rect.Height);
            if (rect.Width == 0 || rect.Height == 0)
                throw new TypeErrorException("The width and the height of the rectangle must not be zero");

            if (rect.X + rect.Width > _codedWidth || rect.Y + rect.Height > _codedHeight)
                throw new TypeErrorException("The rectangle is outside of the coded rectangle");
        }

        // The pixels can be copied in an RGB format only
        if (options.Format is { } format && format is not (VideoPixelFormat.RGBA or VideoPixelFormat.RGBX or VideoPixelFormat.BGRA or VideoPixelFormat.BGRX))
            throw new DomException("The pixels can only be converted to an RGB format", "NotSupportedError");

        if (options.Layout is { } layout && layout.Count != 1)
            throw new TypeErrorException("The layout must have one plane");

        var widthBytes = width * 4;
        var (destinationOffset, destinationStride) = options.Layout is [var planeLayout] ? ((long)planeLayout.Offset, (long)planeLayout.Stride) : (0L, widthBytes);
        if (destinationStride < widthBytes)
            throw new TypeErrorException("The stride of the plane is smaller than its width");

        var planeSize = destinationStride * height;
        var planeEnd = planeSize + destinationOffset;
        if (planeSize > uint.MaxValue || planeEnd > uint.MaxValue)
            throw new TypeErrorException("The plane is too large");

        return new CopyLayout((uint)planeEnd, (uint)destinationOffset, (uint)destinationStride, y, height, x * 4, widthBytes);

        static bool IsValid(double value) => double.IsFinite(value) && value >= 0;
    }

    /// <summary>Returns the pixels converted to a color space (8 bits per sample).</summary>
    private static byte[] ConvertColorSpace(BitmapPixels pixels, PredefinedColorSpace colorSpace)
    {
        var current = pixels.ColorSpace == RenderingColorSpace.DisplayP3 ? PredefinedColorSpace.DisplayP3 : PredefinedColorSpace.Srgb;
        if (current == colorSpace)
            return pixels.Rgba;

        if (pixels.PixelFormat == ImageDataPixelFormat.RgbaFloat16)
        {
            // The samples of a wide gamut keep their precision until they are converted
            var halves = (byte[])pixels.PixelBytes.Clone();
            CanvasRenderingContext2DBase.ConvertColorSpace(unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(halves.AsSpan())), current, colorSpace);
            var bytes = new byte[(long)pixels.Width * pixels.Height * 4];
            CanvasRenderingContext2DBase.CopyHalfToBytes(halves, bytes);
            return bytes;
        }

        var result = (byte[])pixels.Rgba.Clone();
        CanvasRenderingContext2DBase.ConvertColorSpace(result, current, colorSpace);
        return result;
    }

    /// <summary>Copies a row of RGBA pixels in a format: the samples are swapped for BGR, and the alpha is opaque for the formats without alpha.</summary>
    private static void CopyRow(ReadOnlySpan<byte> source, Span<byte> destination, VideoPixelFormat format)
    {
        source.CopyTo(destination);
        if (format is VideoPixelFormat.BGRA or VideoPixelFormat.BGRX)
        {
            for (var i = 0; i + 3 < destination.Length; i += 4)
            {
                (destination[i], destination[i + 2]) = (destination[i + 2], destination[i]);
            }
        }

        if (format is VideoPixelFormat.RGBX or VideoPixelFormat.BGRX)
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

    /// <summary>The layout of the plane of a copy (https://w3c.github.io/webcodecs/#combined-buffer-layout).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct CopyLayout(uint AllocationSize, uint DestinationOffset, uint DestinationStride, long SourceTop, long SourceHeight, long SourceLeftBytes, long SourceWidthBytes);
}
