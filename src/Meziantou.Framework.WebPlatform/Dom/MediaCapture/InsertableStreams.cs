using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of a <see cref="MediaStreamTrackProcessor"/> (https://w3c.github.io/mediacapture-transform/#dictdef-mediastreamtrackprocessorinit).</summary>
/// <seealso href="https://w3c.github.io/mediacapture-transform/#dictdef-mediastreamtrackprocessorinit"/>
[IdlInterface("MediaStreamTrackProcessorInit", "https://w3c.github.io/mediacapture-transform/#dictdef-mediastreamtrackprocessorinit")]
public sealed class MediaStreamTrackProcessorInit
{
    /// <summary>Gets the track to read: a <see cref="MediaStreamTrack"/> or a <see cref="MediaStreamTrackHandle"/>.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessorinit-track"/>
    [IdlMember("track", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessorinit-track")]
    public required object? Track { get; init; }

    /// <summary>Gets the number of frames buffered while no read is pending (1 by default): the oldest frame is discarded when a new one arrives.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessorinit-maxbuffersize"/>
    [IdlMember("maxBufferSize", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessorinit-maxbuffersize")]
    public ushort? MaxBufferSize { get; init; }
}

/// <summary>
/// Reads the media of a track as a stream (https://w3c.github.io/mediacapture-transform/#track-processor): the
/// <c>readable</c> stream of the scripts gives a <see cref="VideoFrame"/> for each frame of a video track, or an
/// <see cref="AudioData"/> for each block of samples of an audio track. The frames that arrive while no read is pending
/// are buffered (<see cref="MediaStreamTrackProcessorInit.MaxBufferSize"/>, 1 by default): the oldest one is closed and
/// discarded when the buffer is full. The stream is closed when the track ends.
/// </summary>
/// <remarks>
/// The specification exposes the interface in the dedicated workers only; it is also exposed in the windows, with the
/// constructor that takes a track (the legacy API of Chromium, https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackprocessor).
/// The frames of a source that produces them (a <see cref="VideoTrackGenerator"/>, a track transferred from another thread)
/// are read as they arrive; the other sources (a camera, a canvas, a media element...) are sampled on the clock of the
/// event loop, at the frame rate of the track (30 frames per second when it has none), and by blocks of 10 ms for the audio.
/// </remarks>
/// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor"/>
[IdlInterface("MediaStreamTrackProcessor", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor")]
public sealed class MediaStreamTrackProcessor
{
    /// <summary>The largest number of buffered frames, to bound the memory used by a page.</summary>
    private const int MaxBufferSizeLimit = 64;

    private readonly GlobalScope _global;
    private readonly int _maxBufferSize;
    private readonly Queue<object> _queue = new();
    private readonly PlatformReadableStream _readable;
    private readonly MediaStreamTrackReader _reader;
    private int _pendingReads;
    private bool _closed;

    /// <summary>Creates a processor of a track (https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-mediastreamtrackprocessor).</summary>
    /// <param name="init">The track and the size of the buffer.</param>
    /// <exception cref="TypeErrorException">The track is not a track or a handle, or it is ended or transferred.</exception>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-mediastreamtrackprocessor"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-mediastreamtrackprocessor")]
    public MediaStreamTrackProcessor(MediaStreamTrackProcessorInit init)
        : this(GetTrack(init), init.MaxBufferSize)
    {
    }

    /// <summary>Creates a processor of a track (the legacy constructor of Chromium).</summary>
    /// <param name="track">The track to read.</param>
    /// <param name="maxBufferSize">The number of frames buffered while no read is pending (1 by default).</param>
    /// <exception cref="TypeErrorException">The track is ended or transferred.</exception>
    /// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackprocessor"/>
    [IdlMember("constructor", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackprocessor")]
    public MediaStreamTrackProcessor(MediaStreamTrack track, ushort? maxBufferSize = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.IsDetached || track.ReadyState == MediaStreamTrackState.Ended)
            throw new TypeErrorException("The track is ended");

        _global = GlobalScope.Current ?? track.RelevantGlobal ?? throw DomException.InvalidState("The track has no global object");
        _maxBufferSize = maxBufferSize is > 1 ? Math.Min((int)maxBufferSize.Value, MaxBufferSizeLimit) : 1;
        _readable = new PlatformReadableStream(Pull, _ => Close());
        _reader = new MediaStreamTrackReader(_global, track, HandleNewFrame, HandleNewFrame, Close);
        _reader.Start();
    }

    /// <summary>Gets the <c>ReadableStream</c> of the frames (an object of the realm of the scripts: the frames are read by the scripts).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-readable"/>
    [IdlMember("readable", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-readable")]
    public object Readable => _readable;

    /// <summary>Gets the number of frames discarded because the buffer was full.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-discardedframes"/>
    [IdlMember("discardedFrames", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-discardedframes")]
    public ulong DiscardedFrames { get; private set; }

    /// <summary>Gets the number of frames received from the track (including the discarded ones).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-totalframes"/>
    [IdlMember("totalFrames", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackprocessor-totalframes")]
    public ulong TotalFrames { get; private set; }

    private static MediaStreamTrack GetTrack(MediaStreamTrackProcessorInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        return init.Track switch
        {
            MediaStreamTrack track => track,
            MediaStreamTrackHandle handle => handle.Track ?? throw new TypeErrorException("The handle was transferred"),
            _ => throw new TypeErrorException("The track must be a MediaStreamTrack or a MediaStreamTrackHandle"),
        };
    }

    /// <summary>https://w3c.github.io/mediacapture-transform/#processor-handle-new-frame</summary>
    private void HandleNewFrame(object media)
    {
        if (_closed)
        {
            CloseMedia(media);
            return;
        }

        if (_queue.Count >= _maxBufferSize)
        {
            CloseMedia(_queue.Dequeue());
            DiscardedFrames++;
        }

        TotalFrames++;
        _queue.Enqueue(media);
        _global.QueueGlobalTask(MaybeReadFrame);
    }

    /// <summary>https://w3c.github.io/mediacapture-transform/#processor-pull</summary>
    private void Pull()
    {
        _pendingReads++;
        _global.QueueGlobalTask(MaybeReadFrame);
    }

    /// <summary>https://w3c.github.io/mediacapture-transform/#maybe-read-frame</summary>
    private void MaybeReadFrame()
    {
        while (!_closed && _pendingReads > 0 && _queue.TryDequeue(out var media))
        {
            _pendingReads--;
            if (!_readable.Enqueue(media))
            {
                CloseMedia(media);
            }
        }
    }

    /// <summary>https://w3c.github.io/mediacapture-transform/#processor-close: the track ended, or the stream was canceled.</summary>
    private void Close()
    {
        if (_closed)
            return;

        _closed = true;
        _reader.Stop();
        _readable.Close();
        while (_queue.TryDequeue(out var media))
        {
            CloseMedia(media);
        }
    }

    private static void CloseMedia(object media)
    {
        switch (media)
        {
            case VideoFrame frame:
                frame.Close();
                break;

            case AudioData data:
                data.Close();
                break;
        }
    }
}

/// <summary>
/// Creates a video track from frames written to a stream (https://w3c.github.io/mediacapture-transform/#video-track-generator):
/// each <see cref="VideoFrame"/> written to the <c>writable</c> stream of the scripts is delivered to the sinks of the
/// track (and of its clones), then closed. The track can be played by a video element, read by a
/// <see cref="MediaStreamTrackProcessor"/>, recorded, or transferred to another thread. The stream is closed when the
/// track and all its clones are stopped; closing or aborting the stream ends the tracks.
/// </summary>
/// <remarks>The specification exposes the interface in the dedicated workers only; it is also exposed in the windows.</remarks>
/// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator"/>
[IdlInterface("VideoTrackGenerator", "https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator")]
public sealed class VideoTrackGenerator
{
    private readonly GlobalScope _global;
    private readonly GeneratorTrackSource _source;
    private readonly PlatformWritableStream _writable;
    private bool _muted;
    private bool _mutedTaskQueued;

    /// <summary>Creates a generator and its track (https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-videotrackgenerator).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-videotrackgenerator"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-videotrackgenerator")]
    public VideoTrackGenerator()
    {
        _global = GlobalScope.Current ?? throw DomException.InvalidState("The generator has no global object");
        _source = new GeneratorTrackSource(MediaTrackKinds.Video);
        _writable = new PlatformWritableStream(Write, _source.End, _ => _source.End());
        _source.Stopped = _writable.Close;
        Track = new MediaStreamTrack(_global, _source);
    }

    /// <summary>Gets the <c>WritableStream</c> to which the scripts write the frames (an object of the realm of the scripts).</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-writable"/>
    [IdlMember("writable", "https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-writable")]
    public object Writable => _writable;

    /// <summary>
    /// Gets or sets whether the generator is muted: the frames written while it is muted are discarded, and its tracks
    /// fire <c>mute</c> (then <c>unmute</c>) in a task.
    /// </summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-muted"/>
    [IdlMember("muted", "https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-muted")]
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value)
                return;

            _muted = value;

            // The tracks take the settled value once per task
            if (!_mutedTaskQueued)
            {
                _mutedTaskQueued = true;
                _global.QueueGlobalTask(() =>
                {
                    _mutedTaskQueued = false;
                    _source.SetMutedState(_muted);
                });
            }
        }
    }

    /// <summary>Gets the track of the generator.</summary>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-track"/>
    [IdlMember("track", "https://w3c.github.io/mediacapture-transform/#dom-videotrackgenerator-track")]
    public MediaStreamTrack Track { get; }

    /// <summary>https://w3c.github.io/mediacapture-transform/#generator-write</summary>
    private void Write(object? chunk)
    {
        if (chunk is not VideoFrame frame)
            throw new TypeErrorException("The chunk is not a VideoFrame");

        if (frame.IsDetached)
            throw new TypeErrorException("The frame is closed");

        try
        {
            if (!_muted)
            {
                _source.Write(frame);
            }
        }
        finally
        {
            frame.Close();
        }
    }
}

/// <summary>The options of a <see cref="MediaStreamTrackGenerator"/> (the legacy API of Chromium).</summary>
/// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator"/>
[IdlInterface("MediaStreamTrackGeneratorInit", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator")]
public sealed class MediaStreamTrackGeneratorInit
{
    /// <summary>Gets the kind of the track: <c>audio</c> or <c>video</c>.</summary>
    /// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator"/>
    [IdlMember("kind", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator")]
    public required string Kind { get; init; }
}

/// <summary>
/// A track created from the frames or the audio samples written to a stream (the legacy API of Chromium, replaced by
/// <see cref="VideoTrackGenerator"/>): each <see cref="VideoFrame"/> (video track) or <see cref="AudioData"/> (audio
/// track) written to the <c>writable</c> stream of the scripts is delivered to the sinks of the track, then closed.
/// </summary>
/// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator"/>
[IdlInterface("MediaStreamTrackGenerator", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator")]
public sealed class MediaStreamTrackGenerator : MediaStreamTrack
{
    private readonly GeneratorTrackSource _source;
    private readonly PlatformWritableStream _writable;

    /// <summary>Creates a generator of a kind.</summary>
    /// <param name="kind">The kind of the track: <c>audio</c> or <c>video</c>.</param>
    /// <exception cref="TypeErrorException">The kind is invalid.</exception>
    /// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator"/>
    [IdlMember("constructor", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator")]
    public MediaStreamTrackGenerator(string kind)
        : base(GlobalScope.Current ?? throw DomException.InvalidState("The generator has no global object"), new GeneratorTrackSource(ParseKind(kind)))
    {
        _source = (GeneratorTrackSource)Source;
        _writable = new PlatformWritableStream(Write, _source.End, _ => _source.End());
        _source.Stopped = _writable.Close;
    }

    /// <summary>Creates a generator of a kind.</summary>
    /// <param name="init">The kind of the track.</param>
    /// <exception cref="TypeErrorException">The kind is invalid.</exception>
    /// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator"/>
    [IdlMember("constructor", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator")]
    public MediaStreamTrackGenerator(MediaStreamTrackGeneratorInit init)
        : this((init ?? throw new ArgumentNullException(nameof(init))).Kind)
    {
    }

    /// <summary>Gets the <c>WritableStream</c> to which the scripts write the frames or the audio samples (an object of the realm of the scripts).</summary>
    /// <seealso href="https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator"/>
    [IdlMember("writable", "https://github.com/w3c/mediacapture-transform/blob/main/explainer.md#mediastreamtrackgenerator")]
    public object Writable => _writable;

    private static MediaTrackKinds ParseKind(string kind) => kind switch
    {
        "audio" => MediaTrackKinds.Audio,
        "video" => MediaTrackKinds.Video,
        _ => throw new TypeErrorException("The kind must be 'audio' or 'video'"),
    };

    private void Write(object? chunk)
    {
        switch (chunk)
        {
            case VideoFrame frame when _source.Kind == MediaTrackKinds.Video:
                if (frame.IsDetached)
                    throw new TypeErrorException("The frame is closed");

                try
                {
                    _source.Write(frame);
                }
                finally
                {
                    frame.Close();
                }

                break;

            case AudioData data when _source.Kind == MediaTrackKinds.Audio:
                if (data.IsDetached)
                    throw new TypeErrorException("The audio data is closed");

                try
                {
                    _source.Write(data);
                }
                finally
                {
                    data.Close();
                }

                break;

            default:
                throw new TypeErrorException(_source.Kind == MediaTrackKinds.Video ? "The chunk is not a VideoFrame" : "The chunk is not an AudioData");
        }
    }
}

/// <summary>
/// A handle of a track that can be transferred to a dedicated worker (https://w3c.github.io/mediacapture-transform/#mediastreamtrackhandle),
/// where a <see cref="MediaStreamTrackProcessor"/> reads the track. The track stays in its global object: the handle
/// received by the worker reads its media. A handle can be transferred once, within its agent cluster.
/// </summary>
/// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackhandle"/>
[IdlInterface("MediaStreamTrackHandle", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackhandle")]
public sealed class MediaStreamTrackHandle : IStructuredTransferable
{
    /// <summary>Creates a handle of a track (https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackhandle-mediastreamtrackhandle).</summary>
    /// <param name="track">The track.</param>
    /// <seealso href="https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackhandle-mediastreamtrackhandle"/>
    [IdlMember("constructor", "https://w3c.github.io/mediacapture-transform/#dom-mediastreamtrackhandle-mediastreamtrackhandle")]
    public MediaStreamTrackHandle(MediaStreamTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        Track = track;
    }

    /// <summary>The track of the handle, or null when the handle was transferred.</summary>
    internal MediaStreamTrack? Track { get; private set; }

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        if (Track is null)
            throw new DomException("The handle was transferred", "DataCloneError");
    }

    /// <summary>The transfer steps: the received handle reads the media of the track, which stays in its global object.</summary>
    ITransferredData IStructuredTransferable.Transfer()
    {
        var data = MediaStreamTrackRelay.CreateTransferredData(Track!);
        Track = null;
        return new TransferredHandle(data);
    }

    private sealed class TransferredHandle(ITransferredData track) : ITransferredData
    {
        private MediaStreamTrackHandle? _received;

        public object Receive(GlobalScope? target) => _received ??= new MediaStreamTrackHandle((MediaStreamTrack)track.Receive(target));
    }
}
