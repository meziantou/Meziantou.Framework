namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// An image the display list draws: the image of an <c>img</c> element, of a poster frame, of a CSS image or a bitmap (a snapshot
/// of a canvas, an <c>ImageBitmap</c>). The encoded bytes are kept only when a <see cref="RenderingBackend"/> may need them (when
/// it is configured); the image is decoded by the backend when it is first drawn, and the decoded image is cached.
/// </summary>
/// <remarks>
/// <para>
/// An image resource has variants that share its bytes and its decoder: the image with or without its EXIF orientation
/// (<see cref="WithOrientation"/>, <c>image-orientation</c>), and the frames of an animated image (<see cref="GetFrame"/>). Each
/// variant caches its decoded pixels; only the last decoded frames of an animation are kept.
/// </para>
/// <para>
/// The frame of an animated image displayed at a time (<see cref="GetDisplayedFrame"/>) depends on the time elapsed since the
/// image became available, on the clock of its event loop (the virtual clock of the tests): the animation is shared by the
/// elements that display the same image, as in the browsers.
/// </para>
/// </remarks>
internal sealed class PaintImage
{
    /// <summary>
    /// The duration of the frames whose encoded duration is 10ms or less, in milliseconds: as in the browsers, such frames are
    /// displayed for 100ms (many GIF images were made for the user agents that did not honor the short durations).
    /// </summary>
    private const double ShortFrameDuration = 100;

    /// <summary>The number of decoded frames of an animated image kept in memory (the frames displayed most recently).</summary>
    private const int MaxDecodedFrames = 2;

    private readonly ImageResource _resource;
    private RenderingBackend? _decodedBy;
    private RenderingImage? _decoded;
    private bool _decodeFailed;

    /// <summary>Creates an image from its resource.</summary>
    /// <param name="url">The URL of the image (for the dumps), or an empty string.</param>
    /// <param name="width">The natural width of the image in image pixels, with its EXIF orientation.</param>
    /// <param name="height">The natural height of the image in image pixels, with its EXIF orientation.</param>
    /// <param name="data">The encoded bytes of the image, or empty when they are not kept.</param>
    /// <param name="mimeType">The MIME type essence of the resource, or null.</param>
    /// <param name="orientation">The EXIF orientation of the image (<see cref="ImageMetadata.GetExifOrientation"/>), applied by this image.</param>
    public PaintImage(string url, double width, double height, ReadOnlyMemory<byte> data, string? mimeType, ExifOrientation orientation = ExifOrientation.TopLeft)
    {
        _resource = new ImageResource(data, mimeType, orientation);
        _resource.Root = this;
        Url = url;
        Width = width;
        Height = height;
        Orientation = orientation;
    }

    /// <summary>Creates an image from a bitmap of a backend (e.g. the snapshot of a canvas).</summary>
    public PaintImage(string url, RenderingBackend backend, RenderingImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _resource = new ImageResource(default, mimeType: null, ExifOrientation.TopLeft);
        _resource.Root = this;
        Url = url;
        Width = image.Width;
        Height = image.Height;
        Orientation = ExifOrientation.TopLeft;
        _decodedBy = backend;
        _decoded = image;
    }

    /// <summary>Creates a variant of an image: another orientation or another frame.</summary>
    private PaintImage(PaintImage root, ExifOrientation orientation, int frame)
    {
        _resource = root._resource;
        Url = root.Url;
        var swap = orientation.SwapsAxes() != root.Orientation.SwapsAxes();
        Width = swap ? root.Height : root.Width;
        Height = swap ? root.Width : root.Height;
        Orientation = orientation;
        Frame = frame;
    }

    /// <summary>The URL of the image, or an empty string.</summary>
    public string Url { get; }

    /// <summary>The natural width of the image in image pixels (in the orientation of this image).</summary>
    public double Width { get; }

    /// <summary>The natural height of the image in image pixels (in the orientation of this image).</summary>
    public double Height { get; }

    /// <summary>The encoded bytes of the image, or empty when they were not kept.</summary>
    public ReadOnlyMemory<byte> Data => _resource.Data;

    /// <summary>The MIME type essence of the resource, or null.</summary>
    public string? MimeType => _resource.MimeType;

    /// <summary>Whether the image is an SVG image (https://svgwg.org/svg2-draft/conform.html#processing-modes), painted by its document (<see cref="Css.Layout.SvgImage"/>) instead of being decoded by the backend.</summary>
    public bool IsSvg => MimeType == "image/svg+xml";

    /// <summary>The document of an SVG image once it is parsed (see <see cref="Css.Layout.SvgImage.Get"/>), or null (shared by the variants of the image).</summary>
    internal Css.Layout.SvgImage? SvgImage
    {
        get => _resource.SvgImage;
        set => _resource.SvgImage = value;
    }

    /// <summary>Whether the document of an SVG image cannot be parsed (it is not painted).</summary>
    internal bool SvgImageFailed
    {
        get => _resource.SvgImageFailed;
        set => _resource.SvgImageFailed = value;
    }

    /// <summary>The orientation applied to the pixels of this image (<see cref="ExifOrientation.TopLeft"/>: as encoded).</summary>
    public ExifOrientation Orientation { get; }

    /// <summary>The frame of an animated image this image draws (0 for a still image).</summary>
    public int Frame { get; }

    /// <summary>
    /// Whether the image is CORS-same-origin with the document that displays it: <c>image-orientation: none</c> does not apply to
    /// the other images (https://drafts.csswg.org/css-images-3/#valdef-image-orientation-none), which keep their EXIF orientation.
    /// </summary>
    public bool IsCorsSameOrigin
    {
        get => _resource.IsCorsSameOrigin;
        init => _resource.IsCorsSameOrigin = value;
    }

    /// <summary>
    /// The clock of the animation of an animated image and the timestamp of its start (when the image became available), or
    /// null: the first frame is displayed.
    /// </summary>
    public (TimeProvider Clock, long Start)? AnimationClock
    {
        get => _resource.AnimationClock;
        init => _resource.AnimationClock = value;
    }

    /// <summary>
    /// Returns the image with or without the EXIF orientation of its resource (<c>image-orientation</c>): an image that is not
    /// CORS-same-origin always keeps its orientation.
    /// </summary>
    public PaintImage WithOrientation(bool fromImage)
    {
        var orientation = fromImage || !IsCorsSameOrigin ? _resource.EncodedOrientation : ExifOrientation.TopLeft;
        return GetVariant(orientation, Frame);
    }

    /// <summary>Returns the image that draws a frame of the animation of this image.</summary>
    public PaintImage GetFrame(int frame) => GetVariant(Orientation, frame);

    private PaintImage GetVariant(ExifOrientation orientation, int frame)
    {
        if (orientation == Orientation && frame == Frame)
            return this;

        var root = _resource.Root!;
        if (orientation == root.Orientation && frame == 0)
            return root;

        lock (_resource.Lock)
        {
            var variants = _resource.Variants ??= [];
            if (!variants.TryGetValue((orientation, frame), out var variant))
            {
                variant = new PaintImage(root, orientation, frame);
                variants.Add((orientation, frame), variant);
            }

            return variant;
        }
    }

    /// <summary>
    /// Returns the decoder of the image opened by a backend (<see cref="RenderingBackend.CreateImageDecoder"/>), opened the first
    /// time, or null when the bytes were not kept or the backend cannot decode them.
    /// </summary>
    public RenderingImageDecoder? GetDecoder(RenderingBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_resource.Lock)
            return GetDecoderCore(backend);
    }

    private RenderingImageDecoder? GetDecoderCore(RenderingBackend backend)
    {
        var resource = _resource;
        if (ReferenceEquals(resource.DecoderBackend, backend))
            return resource.Decoder;

        if (resource.Data.IsEmpty)
            return null;

        resource.Decoder?.Dispose();
        resource.DecoderBackend = backend;
        resource.Decoder = backend.CreateImageDecoder(resource.Data, resource.MimeType);
        return resource.Decoder;
    }

    /// <summary>
    /// Returns the image decoded by a backend (<see cref="RenderingImageDecoder.DecodeFrame"/>), decoded the first time, or null
    /// when it cannot be decoded (its bytes were not kept, or the backend does not support its format, or the data is corrupted).
    /// </summary>
    public RenderingImage? GetDecodedImage(RenderingBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_resource.Lock)
        {
            if (ReferenceEquals(_decodedBy, backend))
                return _decoded;

            if (_decodeFailed || GetDecoderCore(backend) is not { } decoder || Frame >= decoder.FrameCount)
                return null;

            var decoded = decoder.DecodeFrame(Frame, Orientation, convertToSrgb: true);
            if (decoded is null)
            {
                _decodeFailed = true;
                return null;
            }

            _decodedBy = backend;
            _decoded = decoded;

            // Only the last decoded frames of an animation are kept (the display lists that draw an older frame decode it again)
            if (Frame > 0)
            {
                var frames = _resource.DecodedFrames ??= new Queue<PaintImage>();
                frames.Enqueue(this);
                while (frames.Count > MaxDecodedFrames)
                {
                    var evicted = frames.Dequeue();
                    if (!ReferenceEquals(evicted, this))
                    {
                        evicted._decoded = null;
                        evicted._decodedBy = null;
                    }
                }
            }

            return decoded;
        }
    }

    /// <summary>
    /// Returns the frame of the animation displayed now (https://html.spec.whatwg.org/multipage/images.html#img-req-data: an
    /// animated image is played from the time it became available, on the clock of its event loop), and the time in
    /// milliseconds until the next frame is displayed, or null when the image no longer changes (a still image, an animation
    /// that ended, or an image without clock or that the backend cannot decode).
    /// </summary>
    public PaintImage GetDisplayedFrame(RenderingBackend backend, out double? nextChange)
    {
        nextChange = null;
        if (AnimationClock is not ({ } clock, var start))
            return this;

        int frameCount;
        int repetitions;
        double[] durations;
        lock (_resource.Lock)
        {
            if (GetDecoderCore(backend) is not { FrameCount: > 1 } decoder)
                return this;

            frameCount = decoder.FrameCount;
            repetitions = decoder.RepetitionCount;
            durations = _resource.FrameDurations ??= GetDurations(decoder);
        }

        var total = 0.0;
        foreach (var duration in durations)
        {
            total += duration;
        }

        var elapsed = Math.Max(0, clock.GetElapsedTime(start).TotalMilliseconds);
        if (repetitions >= 0 && elapsed >= total * (repetitions + 1))
            return GetFrame(frameCount - 1);

        var time = elapsed % total;
        for (var i = 0; i < frameCount; i++)
        {
            if (time < durations[i])
            {
                nextChange = durations[i] - time;
                return GetFrame(i);
            }

            time -= durations[i];
        }

        return GetFrame(frameCount - 1);

        static double[] GetDurations(RenderingImageDecoder decoder)
        {
            var result = new double[decoder.FrameCount];
            for (var i = 0; i < result.Length; i++)
            {
                var duration = decoder.GetFrameDuration(i);
                result[i] = duration <= 10 ? ShortFrameDuration : duration;
            }

            return result;
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var name = Url.Length > 0 ? Url : "(image)";
        return Frame > 0 ? name + " frame " + Frame.ToString(System.Globalization.CultureInfo.InvariantCulture) : name;
    }

    /// <summary>The state shared by the variants of an image resource.</summary>
    private sealed class ImageResource(ReadOnlyMemory<byte> data, string? mimeType, ExifOrientation encodedOrientation)
    {
        public Lock Lock { get; } = new();

        public ReadOnlyMemory<byte> Data { get; } = data;

        public string? MimeType { get; } = mimeType;

        /// <summary>The EXIF orientation of the encoded image.</summary>
        public ExifOrientation EncodedOrientation { get; } = encodedOrientation;

        public PaintImage? Root { get; set; }

        public bool IsCorsSameOrigin { get; set; } = true;

        public (TimeProvider Clock, long Start)? AnimationClock { get; set; }

        public Dictionary<(ExifOrientation Orientation, int Frame), PaintImage>? Variants { get; set; }

        public RenderingBackend? DecoderBackend { get; set; }

        public RenderingImageDecoder? Decoder { get; set; }

        public double[]? FrameDurations { get; set; }

        public Queue<PaintImage>? DecodedFrames { get; set; }

        public Css.Layout.SvgImage? SvgImage { get; set; }

        public bool SvgImageFailed { get; set; }
    }
}
