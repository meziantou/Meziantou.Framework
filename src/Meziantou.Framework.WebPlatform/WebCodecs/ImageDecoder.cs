using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The options of an <see cref="ImageDecoder"/> (https://w3c.github.io/webcodecs/#dictdef-imagedecoderinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-imagedecoderinit"/>
[IdlInterface("ImageDecoderInit", "https://w3c.github.io/webcodecs/#dictdef-imagedecoderinit")]
public sealed class ImageDecoderInit
{
    /// <summary>Gets the MIME type of the image (e.g. <c>image/png</c>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderinit-type"/>
    [IdlMember("type", "https://w3c.github.io/webcodecs/#dom-imagedecoderinit-type")]
    public required string Type { get; init; }

    /// <summary>
    /// Gets the encoded image: a <c>byte[]</c> or a <see cref="ReadOnlyMemory{T}"/> of bytes (copied by the
    /// constructor), or, from the scripts, a buffer source or a <c>ReadableStream</c> whose chunks are <c>Uint8Array</c> objects.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderinit-data"/>
    [IdlMember("data", "https://w3c.github.io/webcodecs/#dom-imagedecoderinit-data")]
    public required object? Data { get; init; }

    /// <summary>Gets whether the colors are converted to sRGB (<see cref="ColorSpaceConversion.Default"/>) or keep their encoded values.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderinit-colorspaceconversion"/>
    [IdlMember("colorSpaceConversion", "https://w3c.github.io/webcodecs/#dom-imagedecoderinit-colorspaceconversion")]
    public ColorSpaceConversion ColorSpaceConversion { get; init; } = ColorSpaceConversion.Default;

    /// <summary>Gets the width the decoded frames are scaled to (with <see cref="DesiredHeight"/>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderinit-desiredwidth"/>
    [IdlMember("desiredWidth", "https://w3c.github.io/webcodecs/#dom-imagedecoderinit-desiredwidth")]
    public uint? DesiredWidth { get; init; }

    /// <summary>Gets the height the decoded frames are scaled to (with <see cref="DesiredWidth"/>).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderinit-desiredheight"/>
    [IdlMember("desiredHeight", "https://w3c.github.io/webcodecs/#dom-imagedecoderinit-desiredheight")]
    public uint? DesiredHeight { get; init; }

    /// <summary>Gets whether an animated track is selected by default when the image has several tracks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderinit-preferanimation"/>
    [IdlMember("preferAnimation", "https://w3c.github.io/webcodecs/#dom-imagedecoderinit-preferanimation")]
    public bool? PreferAnimation { get; init; }
}

/// <summary>The options of <see cref="ImageDecoder.Decode"/> (https://w3c.github.io/webcodecs/#dictdef-imagedecodeoptions).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-imagedecodeoptions"/>
[IdlInterface("ImageDecodeOptions", "https://w3c.github.io/webcodecs/#dictdef-imagedecodeoptions")]
public sealed class ImageDecodeOptions
{
    /// <summary>Gets the index of the frame to decode.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecodeoptions-frameindex"/>
    [IdlMember("frameIndex", "https://w3c.github.io/webcodecs/#dom-imagedecodeoptions-frameindex")]
    public uint FrameIndex { get; init; }

    /// <summary>Gets whether only the full-detail image is returned (the progressive images are always decoded completely).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecodeoptions-completeframesonly"/>
    [IdlMember("completeFramesOnly", "https://w3c.github.io/webcodecs/#dom-imagedecodeoptions-completeframesonly")]
    public bool CompleteFramesOnly { get; init; } = true;
}

/// <summary>The result of <see cref="ImageDecoder.Decode"/> (https://w3c.github.io/webcodecs/#dictdef-imagedecoderesult).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-imagedecoderesult"/>
[IdlInterface("ImageDecodeResult", "https://w3c.github.io/webcodecs/#dictdef-imagedecoderesult")]
public sealed class ImageDecodeResult
{
    /// <summary>Gets the decoded frame.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderesult-image"/>
    [IdlMember("image", "https://w3c.github.io/webcodecs/#dom-imagedecoderesult-image")]
    public required VideoFrame Image { get; init; }

    /// <summary>Gets whether the frame is the full-detail image.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoderesult-complete"/>
    [IdlMember("complete", "https://w3c.github.io/webcodecs/#dom-imagedecoderesult-complete")]
    public required bool Complete { get; init; }
}

/// <summary>
/// Decodes the frames of an encoded image (https://w3c.github.io/webcodecs/#imagedecoder) with the image decoders of the
/// rendering backend (<see cref="BrowsingContextOptions.RenderingBackend"/>), the ones of the <c>img</c> elements: PNG
/// (including the animated PNG), JPEG, GIF, WebP, AVIF, JPEG XL, BMP and ICO with the backend of
/// Meziantou.Framework.WebPlatform.Imaging. The data is a buffer, or a stream whose frames can be decoded as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// The decoding runs in tasks of the event loop of the global object, in the order of the calls of <see cref="Decode"/>. An
/// image has one track: the frames of an animation, or its still image (an AVIF image that has both a still image and an image
/// sequence has the track of the sequence). The frames are RGBA; their rotation and flip are the EXIF orientation of the image.
/// </para>
/// <para>
/// The frame count of a stream is the number of frames whose data is complete: the frames of a GIF and of an animated WebP
/// image are counted as they arrive, the other formats are parsed again when a chunk arrives.
/// </para>
/// </remarks>
/// <seealso href="https://w3c.github.io/webcodecs/#imagedecoder"/>
[IdlInterface("ImageDecoder", "https://w3c.github.io/webcodecs/#imagedecoder")]
public sealed class ImageDecoder
{
    // The MIME types of the formats of the image decoders of the backends (https://w3c.github.io/webcodecs/#imagedecoder-check-type-support)
    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "image/png", "image/apng", "image/jpeg", "image/gif", "image/webp", "image/avif", "image/jxl", "image/bmp", "image/x-icon", "image/vnd.microsoft.icon",
    };

    private readonly GlobalScope? _global;
    private readonly string _essence;
    private readonly TaskCompletionSource _completed = new();
    private readonly List<PendingDecode> _pendingDecodes = [];
    private readonly ColorSpaceConversion _colorSpaceConversion;
    private readonly uint? _desiredWidth;
    private readonly uint? _desiredHeight;
    private readonly bool? _preferAnimation;

    // [[encoded data]]: the bytes received so far, and the decoder of the backend opened for them
    private byte[] _data = [];
    private int _length;
    private RenderingImageDecoder? _codec;
    private int _codecLength = -1;

    private bool _complete;
    private bool _tracksEstablished;
    private int _internalSelectedTrackIndex = -1;
    private uint[] _frameCounts = [];
    private bool _processQueued;
    private Action? _cancelStream;

    /// <summary>Creates a decoder (https://w3c.github.io/webcodecs/#dom-imagedecoder-imagedecoder).</summary>
    /// <param name="init">The type and the data of the image, and the decoding options.</param>
    /// <exception cref="TypeErrorException">The type is not an image MIME type, the data is empty, detached, or a locked or disturbed stream, or only one of the desired width and height is given.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-imagedecoder"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-imagedecoder-imagedecoder")]
    public ImageDecoder(ImageDecoderInit init)
        : this(init, GlobalScope.Current)
    {
    }

    /// <summary>Creates a decoder whose tasks run on the event loop of a global object (https://w3c.github.io/webcodecs/#dom-imagedecoder-imagedecoder).</summary>
    /// <param name="global">The global object (a <see cref="Window"/> or a worker global scope): its rendering backend decodes the image, and the tasks of the decoder run on its event loop.</param>
    /// <param name="init">The type and the data of the image (a <c>byte[]</c> or a <see cref="ReadOnlyMemory{T}"/> of bytes), and the decoding options.</param>
    /// <exception cref="TypeErrorException">The type is not an image MIME type, the data is empty, or only one of the desired width and height is given.</exception>
    [NotInIdl("The .NET overload of the constructor whose global object is explicit")]
    public ImageDecoder(GlobalScope global, ImageDecoderInit init)
        : this(init, global ?? throw new ArgumentNullException(nameof(global)))
    {
    }

    private ImageDecoder(ImageDecoderInit init, GlobalScope? global)
    {
        ArgumentNullException.ThrowIfNull(init);

        // 1. https://w3c.github.io/webcodecs/#valid-imagedecoderinit
        if (ParseImageMimeType(init.Type) is not { } mimeType)
            throw new TypeErrorException("The type is not a valid image MIME type");

        byte[]? bytes = null;
        switch (init.Data)
        {
            case JsReadableStream stream when stream.Source.IsLocked || stream.Source.IsDisturbed:
                throw new TypeErrorException("The stream is locked or disturbed");

            case JsReadableStream:
                break;

            case JsBufferSource buffer:
                bytes = buffer.IsDetached ? [] : buffer.ToArray();
                break;

            // The buffers of .NET
            case byte[] array:
                bytes = (byte[])array.Clone();
                break;

            case ReadOnlyMemory<byte> memory:
                bytes = memory.ToArray();
                break;

            case Memory<byte> memory:
                bytes = memory.ToArray();
                break;

            default:
                throw new TypeErrorException("The data is not a buffer source or a ReadableStream");
        }

        if (bytes is { Length: 0 })
            throw new TypeErrorException("The data is empty or detached");

        if (init.DesiredWidth.HasValue != init.DesiredHeight.HasValue)
            throw new TypeErrorException("The desired width and height must be given together");

        // 4-17. The decoder and its track list
        _global = global;
        _essence = mimeType.Essence;
        Type = init.Type;
        Tracks = new ImageTrackList();
        _colorSpaceConversion = init.ColorSpaceConversion;
        _desiredWidth = init.DesiredWidth;
        _desiredHeight = init.DesiredHeight;
        _preferAnimation = init.PreferAnimation;
        PromiseSlots.MarkAsHandled(_completed.Task);

        if (bytes is null)
        {
            var readableStream = (JsReadableStream)init.Data!;
            // 18. The chunks of the stream are appended to the data as they arrive (the first chunks may already be queued
            // in the stream: they are read synchronously)
            Configure();
            _cancelStream = readableStream.Source.ReadChunks(OnChunk, OnStreamClosed, OnStreamErrored, OnInvalidChunk);
            if (IsClosed)
            {
                CancelStream();
            }
        }
        else
        {
            // 19. A copy of the bytes
            _data = bytes;
            _length = _data.Length;
            _complete = true;
            _completed.TrySetResult();
            Configure();
            if (!IsClosed)
            {
                QueueTask(EstablishTracks);
            }
        }
    }

    /// <summary>Gets the MIME type given to the constructor.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-type"/>
    [IdlMember("type", "https://w3c.github.io/webcodecs/#dom-imagedecoder-type")]
    public string Type { get; }

    /// <summary>Gets whether all the data of the image has been received.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-complete"/>
    [IdlMember("complete", "https://w3c.github.io/webcodecs/#dom-imagedecoder-complete")]
    public bool Complete => _complete;

    /// <summary>Gets a task that completes when all the data of the image has been received, or fails when the decoder is closed before.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-completed"/>
    [IdlMember("completed", "https://w3c.github.io/webcodecs/#dom-imagedecoder-completed")]
    public Task Completed => _completed.Task;

    /// <summary>Gets the tracks of the image.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-tracks"/>
    [IdlMember("tracks", "https://w3c.github.io/webcodecs/#dom-imagedecoder-tracks")]
    public ImageTrackList Tracks { get; }

    /// <summary>[[closed]].</summary>
    internal bool IsClosed { get; private set; }

    /// <summary>Returns whether a type is a MIME type of an image the decoders support (https://w3c.github.io/webcodecs/#dom-imagedecoder-istypesupported).</summary>
    /// <returns>A task that completes in a task of the event loop, or fails with a <c>TypeError</c> when the type is not a valid image MIME type.</returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-istypesupported"/>
    [IdlMember("isTypeSupported", "https://w3c.github.io/webcodecs/#dom-imagedecoder-istypesupported")]
    public static Task<bool> IsTypeSupported(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (ParseImageMimeType(type) is not { } mimeType)
            return Task.FromException<bool>(new TypeErrorException("The type is not a valid image MIME type"));

        var global = GlobalScope.Current;
        var supported = IsSupported(global, mimeType.Essence);
        if (global is null)
            return Task.FromResult(supported);

        var completion = new TaskCompletionSource<bool>();
        global.QueueGlobalTask(() => completion.TrySetResult(supported));
        return completion.Task;
    }

    /// <summary>Decodes a frame of the selected track (https://w3c.github.io/webcodecs/#dom-imagedecoder-decode).</summary>
    /// <param name="options">The index of the frame.</param>
    /// <returns>
    /// A task that completes with the frame once its data has been received and decoded, or fails with an <c>InvalidStateError</c>
    /// when the decoder is closed or no track is selected, a <see cref="RangeErrorException"/> when the image has no such frame, an
    /// <c>EncodingError</c> when the data is corrupted, or an <c>AbortError</c> when the decoder is reset or closed before.
    /// </returns>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-decode"/>
    [IdlMember("decode", "https://w3c.github.io/webcodecs/#dom-imagedecoder-decode")]
    public Task<ImageDecodeResult> Decode(ImageDecodeOptions? options = null)
    {
        // 1. A closed decoder
        if (IsClosed)
            return Task.FromException<ImageDecodeResult>(new DomException("The decoder is closed", "InvalidStateError"));

        // 2. No track is selected. The selected index of the track list is -1 until the tracks are established, so this step
        // only applies once they are: the decode requests made before wait for the tracks ("wait for [[tracks established]]
        // to become true", as in the browsers)
        if (Tracks.HasTracks && Tracks.SelectedIndex == -1)
            return Task.FromException<ImageDecodeResult>(new DomException("No track is selected", "InvalidStateError"));

        // 3-8. The request is processed after the previous ones
        options ??= new ImageDecodeOptions();
        var completion = new TaskCompletionSource<ImageDecodeResult>();
        _pendingDecodes.Add(new PendingDecode(options.FrameIndex, completion));
        QueueProcessDecodes();
        return completion.Task;
    }

    /// <summary>Aborts the pending decode requests with an <c>AbortError</c> (https://w3c.github.io/webcodecs/#dom-imagedecoder-reset).</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-reset"/>
    [IdlMember("reset", "https://w3c.github.io/webcodecs/#dom-imagedecoder-reset")]
    public void Reset() => Reset(AbortError());

    /// <summary>Aborts the pending decode requests and releases the resources of the decoder (https://w3c.github.io/webcodecs/#dom-imagedecoder-close): the decoder cannot be used anymore.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-imagedecoder-close"/>
    [IdlMember("close", "https://w3c.github.io/webcodecs/#dom-imagedecoder-close")]
    public void Close() => Close(AbortError());

    /// <summary>A track was selected or unselected (https://w3c.github.io/webcodecs/#dom-imagetrack-selected, steps 10-12).</summary>
    internal void OnTrackSelected(int selectedIndex)
    {
        Reset(AbortError());
        _internalSelectedTrackIndex = selectedIndex;
    }

    private static DomException AbortError() => new("The decoding was aborted", "AbortError");

    /// <summary>Parses a valid image MIME type (https://w3c.github.io/webcodecs/#valid-image-mime-type): a MIME type whose type is <c>image</c>.</summary>
    private static MimeType? ParseImageMimeType(string? type) => type is not null && MimeType.Parse(type) is { Type: "image" } mimeType ? mimeType : null;

    /// <summary>https://w3c.github.io/webcodecs/#imagedecoder-check-type-support: the formats decoded by the backend.</summary>
    private static bool IsSupported(GlobalScope? global, string essence) => global?.Options.RenderingBackend is not null && SupportedTypes.Contains(essence);

    /// <summary>The control message that configures the decoder (https://w3c.github.io/webcodecs/#imagedecoder-constructor).</summary>
    private void Configure()
    {
        if (!IsSupported(_global, _essence))
        {
            Close(new DomException("The type is not supported", "NotSupportedError"));
        }
    }

    private void QueueTask(Action task)
    {
        if (_global is { } global)
        {
            global.QueueGlobalTask(task);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The data (https://w3c.github.io/webcodecs/#imagedecoder-fetch-stream-data-loop)
    // ----------------------------------------------------------------------------------------------------------------

    private void OnChunk(byte[] chunk)
    {
        // 1. Nothing is read after the decoder is closed
        if (IsClosed)
        {
            CancelStream();
            return;
        }

        // 3-4. The bytes are appended
        if (_data.Length - _length < chunk.Length)
        {
            var data = new byte[Math.Max(checked(_length + chunk.Length), _data.Length * 2)];
            _data.AsSpan(0, _length).CopyTo(data);
            _data = data;
        }

        chunk.CopyTo(_data.AsSpan(_length));
        _length += chunk.Length;

        // 5-6. The tracks
        if (!_tracksEstablished)
        {
            EstablishTracks();
        }
        else
        {
            UpdateTracks();
        }

        QueueProcessDecodes();
    }

    private void OnStreamClosed()
    {
        if (IsClosed)
            return;

        _cancelStream = null;
        _complete = true;
        _completed.TrySetResult();

        // The tracks of an image whose data ended before they could be determined cannot be established
        if (!_tracksEstablished)
        {
            EstablishTracks();
        }

        QueueProcessDecodes();
    }

    private void OnStreamErrored()
    {
        _cancelStream = null;
        QueueTask(() => Close(new DomException("The stream is errored", "NotReadableError")));
    }

    private void OnInvalidChunk()
    {
        _cancelStream = null;
        QueueTask(() => Close(new DomException("The chunk is not a Uint8Array", "DataError")));
    }

    private void CancelStream()
    {
        var cancel = _cancelStream;
        _cancelStream = null;
        cancel?.Invoke();
    }

    /// <summary>Returns the decoder of the backend for the data received so far, or null when it cannot be opened (not enough data, or corrupted data).</summary>
    private RenderingImageDecoder? GetCodec()
    {
        if (_codecLength == _length)
            return _codec;

        _codec?.Dispose();
        _codec = _global?.Options.RenderingBackend?.CreateImageDecoder(_data.AsMemory(0, _length), _essence);
        _codecLength = _length;
        return _codec;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The tracks (https://w3c.github.io/webcodecs/#imagedecoder-establish-tracks)
    // ----------------------------------------------------------------------------------------------------------------

    private void EstablishTracks()
    {
        if (IsClosed || _tracksEstablished)
            return;

        // 2. The data is not enough to know the tracks
        if (GetCodec() is not { } codec)
        {
            if (_complete)
            {
                QueueTask(() => Close(new DomException("The tracks of the image cannot be determined", "InvalidStateError")));
            }

            return;
        }

        // 4-5. The track of the image: an animation, or a still image (the repetition count of a still image is 0)
        var frameCount = (uint)codec.FrameCount;
        var animated = frameCount > 1;
        var repetitionCount = !animated ? 0f : codec.RepetitionCount < 0 ? float.PositiveInfinity : codec.RepetitionCount;
        var tracks = new List<ImageTrack> { new(this, Tracks, animated, frameCount, repetitionCount) };
        _frameCounts = [frameCount];

        // 6-10. The selected track
        var selectedIndex = GetDefaultSelectedTrackIndex(tracks);
        tracks[selectedIndex].SelectedValue = true;
        _internalSelectedTrackIndex = selectedIndex;
        _tracksEstablished = true;

        // 11. The track list
        QueueTask(() =>
        {
            if (!IsClosed)
            {
                Tracks.SetTracks(tracks, selectedIndex);
            }
        });
    }

    /// <summary>https://w3c.github.io/webcodecs/#imagedecoder-get-default-selected-track-index: the image has no other track than its primary track.</summary>
    private int GetDefaultSelectedTrackIndex(List<ImageTrack> tracks)
    {
        if (_preferAnimation is { } preferAnimation)
        {
            var index = tracks.FindIndex(track => track.Animated == preferAnimation);
            if (index >= 0 && tracks[0].Animated != preferAnimation)
                return index;
        }

        return 0;
    }

    /// <summary>The frame counts of the tracks increase as the data arrives (https://w3c.github.io/webcodecs/#imagedecoder-update-tracks).</summary>
    private void UpdateTracks()
    {
        if (GetCodec() is not { } codec)
            return;

        var frameCount = (uint)codec.FrameCount;
        if (frameCount <= _frameCounts[0])
            return;

        _frameCounts[0] = frameCount;
        QueueTask(() =>
        {
            if (Tracks[0] is { } track && track.FrameCount < frameCount)
            {
                track.FrameCount = frameCount;
            }
        });
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The decoding (https://w3c.github.io/webcodecs/#imagedecoder-decode-complete-frame)
    // ----------------------------------------------------------------------------------------------------------------

    private void QueueProcessDecodes()
    {
        if (_processQueued || _pendingDecodes.Count == 0)
            return;

        _processQueued = true;
        QueueTask(ProcessDecodes);
    }

    /// <summary>Decodes the requests in order: a request waits for the tracks, and for the data of its frame.</summary>
    private void ProcessDecodes()
    {
        _processQueued = false;
        while (!IsClosed && _tracksEstablished && _pendingDecodes.Count > 0)
        {
            var request = _pendingDecodes[0];
            var outcome = DecodeFrame(request.FrameIndex, out var frame);
            switch (outcome)
            {
                case DecodeOutcome.Wait:
                    return;

                case DecodeOutcome.Decoded:
                    // https://w3c.github.io/webcodecs/#imagedecoder-resolve-decode
                    _pendingDecodes.RemoveAt(0);
                    request.Completion.TrySetResult(new ImageDecodeResult { Image = frame!, Complete = true });
                    break;

                case DecodeOutcome.Infeasible:
                    // https://w3c.github.io/webcodecs/#imagedecoder-reject-infeasible-decode
                    _pendingDecodes.RemoveAt(0);
                    request.Completion.TrySetException(new RangeErrorException("The image has no frame at this index"));
                    break;

                default:
                    // https://w3c.github.io/webcodecs/#imagedecoder-fatally-reject-bad-data
                    Close(new DomException("The image cannot be decoded", "EncodingError"));
                    return;
            }
        }
    }

    private DecodeOutcome DecodeFrame(uint frameIndex, out VideoFrame? frame)
    {
        frame = null;

        // 4. The data of the frame must be complete: the request waits for more data until the stream ends
        if (GetCodec() is not { } codec)
            return _complete ? DecodeOutcome.BadData : DecodeOutcome.Wait;

        if (_internalSelectedTrackIndex < 0 || frameIndex >= (uint)codec.FrameCount)
            return _complete ? DecodeOutcome.Infeasible : DecodeOutcome.Wait;

        // 7-8. The frame is decoded; an incomplete frame waits for the rest of its data, or is an error once the data is complete
        var index = (int)frameIndex;
        using var image = codec.DecodeFrame(index, ExifOrientation.TopLeft, convertToSrgb: _colorSpaceConversion != ColorSpaceConversion.None);
        if (image is null || codec.IsLastFrameIncomplete)
            return _complete ? DecodeOutcome.BadData : DecodeOutcome.Wait;

        var bytesPerPixel = image.PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 8 : 4;
        var pixelBytes = new byte[(long)image.Width * image.Height * bytesPerPixel];
        image.ReadPixels(pixelBytes, image.PixelFormat, image.ColorSpace);
        var pixels = new BitmapPixels(image.Width, image.Height, pixelBytes, originClean: true, image.PixelFormat, image.ColorSpace);

        // The orientation of the image (https://w3c.github.io/webcodecs/#imagedecoder-decode-complete-frame, step 11.4)
        var (rotation, flip) = ImageMetadata.GetExifOrientation(_data.AsSpan(0, _length)) switch
        {
            ExifOrientation.TopRight => (0, true),
            ExifOrientation.BottomRight => (180, false),
            ExifOrientation.BottomLeft => (180, true),
            ExifOrientation.LeftTop => (90, true),
            ExifOrientation.RightTop => (90, false),
            ExifOrientation.RightBottom => (270, true),
            ExifOrientation.LeftBottom => (270, false),
            _ => (0, false),
        };

        pixels = ScaleToDesiredSize(pixels, rotation is 90 or 270);

        // 11.2-11.3. The timestamp of a frame of an animation is the sum of the durations of the frames before it; a still
        // image has no duration
        long timestamp = 0;
        ulong? duration = null;
        if (codec.FrameCount > 1)
        {
            var milliseconds = 0d;
            for (var i = 0; i < index; i++)
            {
                milliseconds += codec.GetFrameDuration(i);
            }

            timestamp = ToMicroseconds(milliseconds);
            duration = (ulong)ToMicroseconds(codec.GetFrameDuration(index));
        }

        frame = new VideoFrame(pixels, timestamp, duration, rotation, flip);
        return DecodeOutcome.Decoded;

        static long ToMicroseconds(double milliseconds) => (long)Math.Round(milliseconds * 1000, MidpointRounding.AwayFromZero);
    }

    /// <summary>Scales the pixels to the desired size given to the constructor, if any (the desired size is the size of the rendered frame).</summary>
    private BitmapPixels ScaleToDesiredSize(BitmapPixels pixels, bool swapsAxes)
    {
        if (_desiredWidth is not { } desiredWidth || _desiredHeight is not { } desiredHeight || desiredWidth == 0 || desiredHeight == 0)
            return pixels;

        var (width, height) = swapsAxes ? (desiredHeight, desiredWidth) : (desiredWidth, desiredHeight);
        if (width == pixels.Width && height == pixels.Height)
            return pixels;

        return ImageBitmapFactory.Format(pixels, sourceRect: null, new ImageBitmapOptions { ResizeWidth = width, ResizeHeight = height, ResizeQuality = ResizeQuality.High }) ?? pixels;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Reset and close (https://w3c.github.io/webcodecs/#imagedecoder-reset-imagedecoder)
    // ----------------------------------------------------------------------------------------------------------------

    private void Reset(Exception exception)
    {
        var pending = _pendingDecodes.ToArray();
        _pendingDecodes.Clear();
        foreach (var request in pending)
        {
            request.Completion.TrySetException(exception);
        }
    }

    /// <summary>https://w3c.github.io/webcodecs/#imagedecoder-close-imagedecoder: the stream is canceled.</summary>
    private void Close(Exception exception)
    {
        // 1-3. The pending requests are rejected, and the resources are released
        Reset(exception);
        IsClosed = true;
        _codec?.Dispose();
        _codec = null;
        _codecLength = -1;
        CancelStream();

        // 4. The track list
        if (!Tracks.HasTracks)
        {
            Tracks.Reject(exception);
        }
        else
        {
            Tracks.Clear();
        }

        // 5. The completed promise
        if (!_complete)
        {
            _completed.TrySetException(exception);
        }
    }

    private enum DecodeOutcome
    {
        Wait,
        Decoded,
        Infeasible,
        BadData,
    }

    private sealed record PendingDecode(uint FrameIndex, TaskCompletionSource<ImageDecodeResult> Completion);
}
