using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a <see cref="MediaSource"/> (https://w3c.github.io/media-source/#dom-readystate).</summary>
/// <seealso href="https://w3c.github.io/media-source/#dom-readystate"/>
[IdlInterface("ReadyState", "https://w3c.github.io/media-source/#dom-readystate")]
public enum MediaSourceReadyState
{
    /// <summary>The source is not attached to a media element.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-readystate-closed"/>
    [IdlMember("closed", "https://w3c.github.io/media-source/#dom-readystate-closed")]
    Closed,

    /// <summary>The source is attached to a media element and is ready to receive data.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-readystate-open"/>
    [IdlMember("open", "https://w3c.github.io/media-source/#dom-readystate-open")]
    Open,

    /// <summary>The source is attached to a media element and <c>endOfStream()</c> was called.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-readystate-ended"/>
    [IdlMember("ended", "https://w3c.github.io/media-source/#dom-readystate-ended")]
    Ended,
}

/// <summary>The error signaled by <see cref="MediaSource.EndOfStream"/> (https://w3c.github.io/media-source/#dom-endofstreamerror).</summary>
/// <seealso href="https://w3c.github.io/media-source/#dom-endofstreamerror"/>
[IdlInterface("EndOfStreamError", "https://w3c.github.io/media-source/#dom-endofstreamerror")]
public enum EndOfStreamError
{
    /// <summary>The playback ends with a network error.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-endofstreamerror-network"/>
    [IdlMember("network", "https://w3c.github.io/media-source/#dom-endofstreamerror-network")]
    Network,

    /// <summary>The playback ends with a decode error.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-endofstreamerror-decode"/>
    [IdlMember("decode", "https://w3c.github.io/media-source/#dom-endofstreamerror-decode")]
    Decode,
}

/// <summary>
/// The state of a <see cref="MediaSource"/> that its media element reads: the element can be in the window that owns the
/// dedicated worker of the source, so it reads an immutable copy made each time the state of the source changes.
/// </summary>
internal sealed record MediaSourceSnapshot(double Duration, MediaTimeRange[] Buffered, double GapTolerance, MediaTimeRange? LiveSeekableRange, bool IsEnded, bool HasVideo, int VideoWidth, int VideoHeight, (double Time, int Width, int Height)[] VideoSizes)
{
    public static MediaSourceSnapshot Empty { get; } = new(double.NaN, [], 0, null, IsEnded: false, HasVideo: false, 0, 0, []);

    /// <summary>The natural size of the video at a position: the size of the frame of the selected video track, else the size of its track description.</summary>
    public (int Width, int Height) GetVideoSize(double time)
    {
        if (VideoSizes.Length == 0)
            return (VideoWidth, VideoHeight);

        // Before the first frame, the size is the one of the first frame
        var size = (VideoSizes[0].Width, VideoSizes[0].Height);
        foreach (var change in VideoSizes)
        {
            if (change.Time > time)
                break;

            size = (change.Width, change.Height);
        }

        return size;
    }

    /// <summary>https://w3c.github.io/media-source/#dom-htmlmediaelement.seekable</summary>
    public MediaTimeRange[] GetSeekable()
    {
        if (double.IsNaN(Duration))
            return [];

        if (!double.IsPositiveInfinity(Duration))
            return [new MediaTimeRange(0, Duration)];

        if (LiveSeekableRange is { } live)
        {
            // The union of the live seekable range and of the buffered ranges, as a single range
            return [new MediaTimeRange(Buffered.Length > 0 ? Math.Min(live.Start, Buffered[0].Start) : live.Start, Buffered.Length > 0 ? Math.Max(live.End, Buffered[^1].End) : live.End)];
        }

        return Buffered.Length == 0 ? [] : [new MediaTimeRange(0, Buffered[^1].End)];
    }
}

/// <summary>
/// A media source (https://w3c.github.io/media-source/#mediasource): a media resource whose data is appended by the scripts
/// to its <see cref="SourceBuffer"/> objects, attached to a media element by a <c>blob:</c> URL
/// (<see cref="Url.CreateObjectUrl(MediaSource)"/>) or, for a source created in a dedicated worker, by its <see cref="Handle"/>
/// (<see cref="HtmlMediaElement.SrcObject"/>).
/// </summary>
/// <remarks>
/// The media backend of the browsing context parses the appended bytes (<see cref="MediaBackend.CreateSegmentParser"/>); the
/// library implements the algorithms of the specification on the coded frames it reports. Without a backend, no type is
/// supported.
/// </remarks>
/// <seealso href="https://w3c.github.io/media-source/#mediasource"/>
[IdlInterface("MediaSource", "https://w3c.github.io/media-source/#mediasource")]
public class MediaSource : EventTarget
{
    private readonly GlobalScope? _global;
    private readonly Lock _lock = new();
    private MediaSourceReadyState _readyState;
    private double _duration = double.NaN;
    private MediaTimeRange? _liveSeekableRange;
    private MediaSourceHandle? _handle;
    private MediaSourceSnapshot _snapshot = MediaSourceSnapshot.Empty;

    // The attachment (https://w3c.github.io/media-source/#mediasource-attach): the fields are written under the lock, by the
    // thread of the media element
    private HtmlMediaElement? _element;
    private GlobalScope? _elementGlobal;
    private int _attachmentId;
    private bool _hasEverBeenAttached;
    private volatile bool _elementHasError;

    /// <summary>Creates a media source (https://w3c.github.io/media-source/#dom-mediasource-constructor): it is closed until it is attached to a media element.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/media-source/#dom-mediasource-constructor")]
    public MediaSource()
    {
        _global = GlobalScope.Current;
        SourceBuffers = new SourceBufferList();
        ActiveSourceBuffers = new SourceBufferList();
    }

    /// <summary>Creates a source buffer of the source (a managed source buffer for a managed media source).</summary>
    private protected virtual SourceBuffer CreateSourceBuffer(MediaSegmentParser parser) => new SourceBuffer(this, parser);

    /// <summary>The state of the source changed (<see cref="ReadyState"/>).</summary>
    private protected virtual void OnReadyStateChanged()
    {
    }

    /// <summary>The media element can, or can no longer, play without interruption (its ready state is, or is no longer, <c>HAVE_ENOUGH_DATA</c>).</summary>
    private protected virtual void OnElementCanPlayUninterruptedChanged(bool canPlayUninterrupted)
    {
    }

    /// <summary>Whether the ready state of the media element is <c>HAVE_ENOUGH_DATA</c>.</summary>
    private protected bool ElementCanPlayUninterrupted { get; private set; }

    /// <summary>The media element tells the source whether it has enough data to play without interruption (https://w3c.github.io/media-source/#managedmediasource-monitoring).</summary>
    internal void SetElementCanPlayUninterrupted(GlobalScope elementGlobal, bool value) => RunOnSource(elementGlobal, () =>
    {
        if (ElementCanPlayUninterrupted == value)
            return;

        ElementCanPlayUninterrupted = value;
        OnElementCanPlayUninterruptedChanged(value);
    });

    /// <summary>The global object that created the source: its tasks are queued on its event loop.</summary>
    internal GlobalScope? Global => _global;

    /// <summary>Whether the source was constructed in a dedicated worker: it can only be attached by its handle.</summary>
    internal bool IsInWorker => _global is WorkerGlobalScope;

    /// <summary>The state read by the media element (see <see cref="MediaSourceSnapshot"/>).</summary>
    internal MediaSourceSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>Whether the worker of the source is terminated or closing: the media element then has no buffered nor seekable range.</summary>
    internal bool IsWorkerClosing => _global is WorkerGlobalScope { Agent.IsClosing: true };

    /// <summary>
    /// Gets the handle used to attach the source of a dedicated worker to a media element of its window
    /// (https://w3c.github.io/media-source/#dom-mediasource-handle): it is transferred to the window and assigned to
    /// <c>srcObject</c>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-handle"/>
    [IdlMember("handle", "https://w3c.github.io/media-source/#dom-mediasource-handle")]
    public MediaSourceHandle Handle => _handle ??= new MediaSourceHandle(this);

    /// <summary>Gets the source buffers of the source (https://w3c.github.io/media-source/#dom-mediasource-sourcebuffers).</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-sourcebuffers"/>
    [IdlMember("sourceBuffers", "https://w3c.github.io/media-source/#dom-mediasource-sourcebuffers")]
    public SourceBufferList SourceBuffers { get; }

    /// <summary>
    /// Gets the source buffers that provide the selected video track and the enabled audio tracks
    /// (https://w3c.github.io/media-source/#dom-mediasource-activesourcebuffers), in the order of <see cref="SourceBuffers"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-activesourcebuffers"/>
    [IdlMember("activeSourceBuffers", "https://w3c.github.io/media-source/#dom-mediasource-activesourcebuffers")]
    public SourceBufferList ActiveSourceBuffers { get; }

    /// <summary>Gets the state of the source (https://w3c.github.io/media-source/#dom-mediasource-readystate).</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-readystate"/>
    [IdlMember("readyState", "https://w3c.github.io/media-source/#dom-mediasource-readystate")]
    public MediaSourceReadyState ReadyState => _readyState;

    /// <summary>
    /// Gets or sets the duration of the presentation in seconds (https://w3c.github.io/media-source/#dom-mediasource-duration):
    /// NaN while the source is closed. The setter throws a <c>TypeError</c> for a negative value or NaN, and an
    /// <c>InvalidStateError</c> when the source is not open, a source buffer is updating, or the value is less than the
    /// highest presentation timestamp of the buffered frames.
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-duration"/>
    [IdlMember("duration", "https://w3c.github.io/media-source/#dom-mediasource-duration")]
    public double Duration
    {
        get => _readyState == MediaSourceReadyState.Closed ? double.NaN : _duration;
        set
        {
            if (value < 0 || double.IsNaN(value))
                throw new TypeErrorException("The duration must not be negative nor NaN");

            if (_readyState != MediaSourceReadyState.Open)
                throw DomException.InvalidState("The MediaSource is not open");

            ThrowIfUpdating();
            ChangeDuration(value);
            ScheduleMonitoring();
        }
    }

    /// <summary>Gets or sets the event handler of the <c>sourceopen</c> event.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-onsourceopen"/>
    [IdlMember("onsourceopen", "https://w3c.github.io/media-source/#dom-mediasource-onsourceopen")]
    public Action<Event>? OnSourceOpen
    {
        get => GetEventHandler("sourceopen");
        set => SetEventHandler("sourceopen", value);
    }

    /// <summary>Gets or sets the event handler of the <c>sourceended</c> event.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-onsourceended"/>
    [IdlMember("onsourceended", "https://w3c.github.io/media-source/#dom-mediasource-onsourceended")]
    public Action<Event>? OnSourceEnded
    {
        get => GetEventHandler("sourceended");
        set => SetEventHandler("sourceended", value);
    }

    /// <summary>Gets or sets the event handler of the <c>sourceclose</c> event.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-onsourceclose"/>
    [IdlMember("onsourceclose", "https://w3c.github.io/media-source/#dom-mediasource-onsourceclose")]
    public Action<Event>? OnSourceClose
    {
        get => GetEventHandler("sourceclose");
        set => SetEventHandler("sourceclose", value);
    }

    /// <summary>Gets a value indicating whether a media source can be created in a dedicated worker (https://w3c.github.io/media-source/#dom-mediasource-canconstructindedicatedworker): always true.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-canconstructindedicatedworker"/>
    [IdlMember("canConstructInDedicatedWorker", "https://w3c.github.io/media-source/#dom-mediasource-canconstructindedicatedworker")]
    public static bool CanConstructInDedicatedWorker => true;

    /// <summary>
    /// Creates a source buffer for a MIME type (https://w3c.github.io/media-source/#dom-mediasource-addsourcebuffer). Throws a
    /// <c>TypeError</c> for an empty type, a <c>NotSupportedError</c> when the media backend does not support the type, and
    /// an <c>InvalidStateError</c> when the source is not open.
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-addsourcebuffer"/>
    [IdlMember("addSourceBuffer", "https://w3c.github.io/media-source/#dom-mediasource-addsourcebuffer")]
    public SourceBuffer AddSourceBuffer(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Length == 0)
            throw new TypeErrorException("The type must not be empty");

#pragma warning disable CA2000 // The source buffer owns the parser
        var parser = CreateParser(_global, type) ?? throw DomException.NotSupported($"The type '{type}' is not supported");
#pragma warning restore CA2000
        if (_readyState != MediaSourceReadyState.Open)
        {
            parser.Dispose();
            throw DomException.InvalidState("The MediaSource is not open");
        }

        var buffer = CreateSourceBuffer(parser);
        lock (_lock)
        {
            SourceBuffers.Add(buffer);
        }

        QueueEvent(SourceBuffers, "addsourcebuffer");
        return buffer;
    }

    /// <summary>
    /// Removes a source buffer (https://w3c.github.io/media-source/#dom-mediasource-removesourcebuffer): its pending append is
    /// aborted, and its tracks are removed from the media element. Throws a <c>NotFoundError</c> when the buffer is not in
    /// <see cref="SourceBuffers"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-removesourcebuffer"/>
    [IdlMember("removeSourceBuffer", "https://w3c.github.io/media-source/#dom-mediasource-removesourcebuffer")]
    public void RemoveSourceBuffer(SourceBuffer sourceBuffer)
    {
        ArgumentNullException.ThrowIfNull(sourceBuffer);
        if (!SourceBuffers.Contains(sourceBuffer))
            throw DomException.NotFound("The SourceBuffer is not in the list of the source buffers of the MediaSource");

        sourceBuffer.AbortUpdate();
        sourceBuffer.RemoveTracks();
        bool wasActive;
        lock (_lock)
        {
            wasActive = ActiveSourceBuffers.Remove(sourceBuffer);
            SourceBuffers.Remove(sourceBuffer);
        }

        if (wasActive)
        {
            QueueEvent(ActiveSourceBuffers, "removesourcebuffer");
        }

        QueueEvent(SourceBuffers, "removesourcebuffer");
        sourceBuffer.Destroy();
        OnStateChanged();
    }

    /// <summary>
    /// Signals the end of the stream (https://w3c.github.io/media-source/#dom-mediasource-endofstream): without error, the
    /// duration becomes the end of the buffered data and the media element can play to the end; with an error, the media
    /// element fails. Throws an <c>InvalidStateError</c> when the source is not open or a source buffer is updating.
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-endofstream"/>
    [IdlMember("endOfStream", "https://w3c.github.io/media-source/#dom-mediasource-endofstream")]
    public void EndOfStream(EndOfStreamError? error = null)
    {
        if (_readyState != MediaSourceReadyState.Open)
            throw DomException.InvalidState("The MediaSource is not open");

        ThrowIfUpdating();
        RunEndOfStream(error);
        ScheduleMonitoring();
    }

    /// <summary>
    /// Sets the range the media element can seek to when the duration is infinite
    /// (https://w3c.github.io/media-source/#dom-mediasource-setliveseekablerange).
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-setliveseekablerange"/>
    [IdlMember("setLiveSeekableRange", "https://w3c.github.io/media-source/#dom-mediasource-setliveseekablerange")]
    public void SetLiveSeekableRange(double start, double end)
    {
        if (_readyState != MediaSourceReadyState.Open)
            throw DomException.InvalidState("The MediaSource is not open");

        if (start < 0 || start > end)
            throw new TypeErrorException("The start of the range must not be negative nor greater than its end");

        _liveSeekableRange = new MediaTimeRange(start, end);
        OnStateChanged();
    }

    /// <summary>Clears the live seekable range (https://w3c.github.io/media-source/#dom-mediasource-clearliveseekablerange).</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-clearliveseekablerange"/>
    [IdlMember("clearLiveSeekableRange", "https://w3c.github.io/media-source/#dom-mediasource-clearliveseekablerange")]
    public void ClearLiveSeekableRange()
    {
        if (_readyState != MediaSourceReadyState.Open)
            throw DomException.InvalidState("The MediaSource is not open");

        if (_liveSeekableRange is not null)
        {
            _liveSeekableRange = null;
            OnStateChanged();
        }
    }

    /// <summary>
    /// Returns whether a source buffer can be created for a MIME type (https://w3c.github.io/media-source/#dom-mediasource-istypesupported):
    /// the media backend of the current global object is confident that it can play the type
    /// (<see cref="MediaBackend.CanPlayType"/> is <see cref="MediaTypeSupport.Probably"/>: as in the browsers, a type that
    /// allows the <c>codecs</c> parameter and does not have it is not supported, though <c>addSourceBuffer()</c> accepts it)
    /// and creates a byte stream parser for it (<see cref="MediaBackend.CreateSegmentParser"/>).
    /// </summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-mediasource-istypesupported"/>
    [IdlMember("isTypeSupported", "https://w3c.github.io/media-source/#dom-mediasource-istypesupported")]
    public static bool IsTypeSupported(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        using var parser = CreateParser(GlobalScope.Current, type, requireCodecs: true);
        return parser is not null;
    }

    /// <summary>Creates the byte stream parser of a type with the media backend of a global object, or null when the type is not a valid MIME type or is not supported.</summary>
    internal static MediaSegmentParser? CreateParser(GlobalScope? global, string type, bool requireCodecs = false)
    {
        if (type.Length == 0 || global?.Options.MediaBackend is not { } backend || MimeType.Parse(type) is not { } mimeType)
            return null;

        string[] codecs = [];
        if (mimeType.GetParameter("codecs") is { } parameter)
        {
            codecs = parameter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        if (requireCodecs && backend.CanPlayType(mimeType.Essence, codecs) != MediaTypeSupport.Probably)
            return null;

        return backend.CreateSegmentParser(mimeType.Essence, codecs);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tasks and state
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Queues a task of the event loop of the source.</summary>
    internal void QueueTask(Action task)
    {
        if (_global is { } global)
        {
            global.QueueGlobalTask(task);
        }
    }

    /// <summary>Queues a task to fire an event at an object of the source.</summary>
    internal void QueueEvent(EventTarget target, string type) => QueueTask(() => target.FireEvent(new Event(type)));

    private void ThrowIfUpdating()
    {
        foreach (var buffer in SourceBuffers)
        {
            if (buffer.Updating)
                throw DomException.InvalidState("A SourceBuffer is updating");
        }
    }

    /// <summary>Whether the <c>error</c> attribute of the media element is not null (the appends then fail).</summary>
    internal bool ElementHasError => _elementHasError;

    /// <summary>The media element tells the source that its <c>error</c> attribute changed.</summary>
    internal void SetElementHasError(bool value) => _elementHasError = value;

    /// <summary>The current playback position of the media element, or 0 (used to choose the frames to evict).</summary>
    internal double ElementPosition => _element?.MediaSourcePlaybackPosition ?? 0;

    /// <summary>
    /// The steps of the setters and methods of a source buffer that reopen an ended source: the state is <c>open</c> again
    /// and a <c>sourceopen</c> event is fired.
    /// </summary>
    internal void ReopenIfEnded()
    {
        if (_readyState != MediaSourceReadyState.Ended)
            return;

        _readyState = MediaSourceReadyState.Open;
        QueueEvent(this, "sourceopen");
        OnStateChanged();
        OnReadyStateChanged();
    }

    /// <summary>The duration of the presentation, whatever the state of the source.</summary>
    internal double DurationValue => _duration;

    /// <summary>The largest track buffer ranges end time across all the track buffers of all the source buffers.</summary>
    private double HighestEndTime
    {
        get
        {
            var result = 0d;
            foreach (var buffer in SourceBuffers)
            {
                result = Math.Max(result, buffer.HighestEndTime);
            }

            return result;
        }
    }

    /// <summary>https://w3c.github.io/media-source/#duration-change-algorithm</summary>
    internal void ChangeDuration(double newDuration)
    {
        if (_duration.Equals(newDuration))
            return;

        foreach (var buffer in SourceBuffers)
        {
            if (buffer.HighestPresentationTimestamp is { } highest && newDuration < highest)
                throw DomException.InvalidState("The duration is less than the highest presentation timestamp of the buffered frames: remove them first");
        }

        // The coded frame removal algorithm preserves the frames that start before the start of the removal range
        var highestEndTime = HighestEndTime;
        if (newDuration < highestEndTime)
        {
            newDuration = highestEndTime;
        }

        _duration = newDuration;
        OnStateChanged();
    }

    /// <summary>https://w3c.github.io/media-source/#end-of-stream-algorithm</summary>
    internal void RunEndOfStream(EndOfStreamError? error)
    {
        _readyState = MediaSourceReadyState.Ended;
        QueueEvent(this, "sourceended");
        OnReadyStateChanged();
        if (error is null)
        {
            // The duration is the end of the appended media segments, and the media element has all of the media data
            ChangeDuration(HighestEndTime);
            OnStateChanged();
            foreach (var buffer in SourceBuffers)
            {
                buffer.NotifyBufferedRangesMayHaveChanged();
            }

            return;
        }

        OnStateChanged();
        var isNetworkError = error == EndOfStreamError.Network;
        RunOnElement(element => element.OnMediaSourceError(isNetworkError));
    }

    /// <summary>
    /// The state the media element depends on changed (the buffered ranges, the duration, the state, the active source
    /// buffers): the copy of the state it reads is updated, and the element updates its duration, its pending seek and its
    /// ready state, which only decreases here (see <see cref="ScheduleMonitoring"/>).
    /// </summary>
    internal void OnStateChanged()
    {
        UpdateSnapshot();
        RunOnElement(static element => element.OnMediaSourceStateChanged());
    }

    /// <summary>
    /// Schedules the steps that raise the ready state of the media element when more data is available around its current
    /// playback position (https://w3c.github.io/media-source/#buffer-monitoring). As in the browsers, where the appended
    /// frames are decoded asynchronously, the ready state rises after the events of the operation that made the data
    /// available were fired (<c>updateend</c>), from a timer queued by a task.
    /// </summary>
    internal void ScheduleMonitoring() => QueueTask(() => RunOnElement(static element => element.ScheduleMediaSourceMonitoring()));

    private void UpdateSnapshot()
    {
        lock (_lock)
        {
            // https://w3c.github.io/media-source/#dom-htmlmediaelement.buffered
            var isEnded = _readyState == MediaSourceReadyState.Ended;
            MediaTimeRange[] buffered = [];
            var hasVideo = false;
            var gapTolerance = 0d;
            var videoWidth = 0;
            var videoHeight = 0;
            (double Time, int Width, int Height)[] videoSizes = [];
            if (ActiveSourceBuffers.Count > 0)
            {
                var highestEndTime = 0d;
                var activeRanges = new List<MediaTimeRange[]>(ActiveSourceBuffers.Count);
                foreach (var buffer in ActiveSourceBuffers)
                {
                    var ranges = buffer.ComputeBufferedRanges(isEnded);
                    activeRanges.Add(ranges);
                    gapTolerance = Math.Max(gapTolerance, buffer.GapTolerance);
                    if (ranges.Length > 0)
                    {
                        highestEndTime = Math.Max(highestEndTime, ranges[^1].End);
                    }

                    if (!hasVideo && buffer.SelectedVideoTrackBuffer is { } video)
                    {
                        hasVideo = true;
                        videoWidth = video.Width;
                        videoHeight = video.Height;
                        videoSizes = GetVideoSizes(video);
                    }
                }

                buffered = [new MediaTimeRange(0, highestEndTime)];
                foreach (var ranges in activeRanges)
                {
                    var sourceRanges = ranges;
                    if (isEnded && sourceRanges.Length > 0)
                    {
                        sourceRanges = [.. sourceRanges];
                        sourceRanges[^1] = new MediaTimeRange(sourceRanges[^1].Start, highestEndTime);
                    }

                    buffered = MediaTimeRanges.Intersect(buffered, sourceRanges);
                }
            }

            Volatile.Write(ref _snapshot, new MediaSourceSnapshot(_readyState == MediaSourceReadyState.Closed ? double.NaN : _duration, buffered, gapTolerance, _liveSeekableRange, isEnded, hasVideo, videoWidth, videoHeight, videoSizes));
        }
    }

    /// <summary>The positions of the media timeline where the size of the frames of a video track changes.</summary>
    private static (double Time, int Width, int Height)[] GetVideoSizes(TrackBuffer buffer)
    {
        var result = new List<(double Time, int Width, int Height)>();
        foreach (var frame in buffer.Frames)
        {
            if (result.Count == 0 || result[^1].Width != frame.Width || result[^1].Height != frame.Height)
            {
                result.Add((frame.PresentationTimestamp, frame.Width, frame.Height));
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Updates <see cref="ActiveSourceBuffers"/> after the enabled or selected state of a track changed, or a source buffer
    /// received its first initialization segment (https://w3c.github.io/media-source/#active-source-buffer-changes).
    /// </summary>
    internal void UpdateActiveSourceBuffers()
    {
        var added = false;
        var removed = false;
        lock (_lock)
        {
            var index = 0;
            foreach (var buffer in SourceBuffers)
            {
                var isActive = ActiveSourceBuffers.Contains(buffer);
                if (buffer.HasActiveTrack)
                {
                    if (!isActive)
                    {
                        // The active source buffers are in the order of the source buffers
                        ActiveSourceBuffers.Insert(index, buffer);
                        added = true;
                    }

                    index++;
                }
                else if (isActive)
                {
                    ActiveSourceBuffers.Remove(buffer);
                    removed = true;
                }
            }
        }

        if (removed)
        {
            QueueEvent(ActiveSourceBuffers, "removesourcebuffer");
        }

        if (added)
        {
            QueueEvent(ActiveSourceBuffers, "addsourcebuffer");
        }

        if (added || removed)
        {
            OnStateChanged();
            ScheduleMonitoring();
        }
    }

    /// <summary>Whether every source buffer received its first initialization segment: the media element then has its metadata.</summary>
    internal bool AreAllSourceBuffersInitialized
    {
        get
        {
            foreach (var buffer in SourceBuffers)
            {
                if (!buffer.HasReceivedInitializationSegment)
                    return false;
            }

            return SourceBuffers.Count > 0;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Attachment (https://w3c.github.io/media-source/#mediasource-attach)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the tasks of the media element and the ones of the source run on the same event loop (the source was constructed in a window).</summary>
    private static bool IsSameLoop(GlobalScope? first, GlobalScope? second) => first is not null && second is not null && ReferenceEquals(first.EventLoop, second.EventLoop);

    /// <summary>
    /// Runs steps on the media element the source is attached to (https://w3c.github.io/media-source/#mirror-if-necessary):
    /// at once for a source constructed in a window, in a task of the event loop of the element for a source constructed in
    /// a dedicated worker.
    /// </summary>
    internal void RunOnElement(Action<HtmlMediaElement> steps)
    {
        HtmlMediaElement? element;
        GlobalScope? elementGlobal;
        int attachmentId;
        lock (_lock)
        {
            element = _element;
            elementGlobal = _elementGlobal;
            attachmentId = _attachmentId;
        }

        if (element is null || elementGlobal is null)
            return;

        if (IsSameLoop(elementGlobal, _global))
        {
            steps(element);
            return;
        }

        elementGlobal.EventLoop.QueueTask(() =>
        {
            if (element.IsAttachedTo(this, attachmentId))
            {
                steps(element);
            }
        });
    }

    /// <summary>Runs steps of the source requested by the media element: at once, or in a task of the event loop of the worker of the source.</summary>
    private void RunOnSource(GlobalScope elementGlobal, Action steps)
    {
        if (_global is null || IsSameLoop(elementGlobal, _global))
        {
            steps();
            return;
        }

        _global.EventLoop.QueueTask(steps);
    }

    /// <summary>
    /// The steps of the resource fetch algorithm of a media element whose media provider object is the source
    /// (https://w3c.github.io/media-source/#mediasource-attach): returns false when the source cannot be attached (it is not
    /// closed, or it was already attached by its handle), else the source opens and a <c>sourceopen</c> event is fired.
    /// </summary>
    /// <param name="element">The media element (the method is called on its thread).</param>
    /// <param name="elementGlobal">The window of the element.</param>
    /// <param name="byHandle">Whether the media provider object is the handle of the source (else a URL of the source).</param>
    /// <returns>The identifier of the attachment, or null.</returns>
    internal int? TryAttach(HtmlMediaElement element, GlobalScope elementGlobal, bool byHandle)
    {
        int attachmentId;
        lock (_lock)
        {
            // A source constructed in a dedicated worker can only be attached by its handle, and only once
            if ((!byHandle && IsInWorker) || (byHandle && _hasEverBeenAttached) || _readyState != MediaSourceReadyState.Closed || _element is not null)
                return null;

            _hasEverBeenAttached = true;
            _element = element;
            _elementGlobal = elementGlobal;
            _elementHasError = false;
            attachmentId = ++_attachmentId;
        }

        // The source opens in a task of its event loop ("implementations may delay the steps until the MediaSource object
        // is ready for use"): the sourceclose event of a previous attachment is fired first
        (_global?.EventLoop ?? elementGlobal.EventLoop).QueueTask(() =>
        {
            lock (_lock)
            {
                if (_attachmentId != attachmentId || _element is null || _readyState != MediaSourceReadyState.Closed)
                    return;

                _readyState = MediaSourceReadyState.Open;
            }

            UpdateSnapshot();
            ElementCanPlayUninterrupted = false;
            OnReadyStateChanged();
            FireEvent(new Event("sourceopen"));
        });
        return attachmentId;
    }

    /// <summary>
    /// The media element detaches the source (https://w3c.github.io/media-source/#mediasource-detach): the source is closed,
    /// its source buffers are removed and a <c>sourceclose</c> event is fired.
    /// </summary>
    internal void Detach(HtmlMediaElement element, GlobalScope elementGlobal)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_element, element))
                return;

            _element = null;
            _elementGlobal = null;
            _attachmentId++;
        }

        RunOnSource(elementGlobal, RunDetach);
    }

    private void RunDetach()
    {
        if (_readyState == MediaSourceReadyState.Closed)
            return;

        _readyState = MediaSourceReadyState.Closed;
        OnReadyStateChanged();
        _duration = double.NaN;
        _liveSeekableRange = null;
        SourceBuffer[] buffers;
        bool hadActiveBuffers;
        lock (_lock)
        {
            buffers = [.. SourceBuffers];
            hadActiveBuffers = ActiveSourceBuffers.Count > 0;
            ActiveSourceBuffers.Clear();
            SourceBuffers.Clear();
        }

        foreach (var buffer in buffers)
        {
            buffer.AbortUpdate();
            buffer.RemoveTracks();
            buffer.Destroy();
        }

        // As in the browsers, the events of the lists are only fired when a source buffer is removed
        if (hadActiveBuffers)
        {
            QueueEvent(ActiveSourceBuffers, "removesourcebuffer");
        }

        if (buffers.Length > 0)
        {
            QueueEvent(SourceBuffers, "removesourcebuffer");
        }

        QueueEvent(this, "sourceclose");
        UpdateSnapshot();
    }

    /// <summary>
    /// The enabled or selected state of a track of the media element changed
    /// (https://w3c.github.io/media-source/#active-source-buffer-changes): the active source buffers are updated by the thread
    /// of the source.
    /// </summary>
    internal void OnTrackStateChanged(GlobalScope elementGlobal) => RunOnSource(elementGlobal, UpdateActiveSourceBuffers);

    /// <summary>The lock that protects the lists of the source buffers and the track buffers, which the thread of the media element reads.</summary>
    internal Lock SyncRoot => _lock;
}

/// <summary>
/// The handle of a <see cref="MediaSource"/> of a dedicated worker (https://w3c.github.io/media-source/#mediasourcehandle):
/// it is transferred to the window of the worker, and assigned to the <c>srcObject</c> attribute of a media element to
/// attach the source.
/// </summary>
/// <seealso href="https://w3c.github.io/media-source/#mediasourcehandle"/>
[IdlInterface("MediaSourceHandle", "https://w3c.github.io/media-source/#mediasourcehandle")]
public sealed class MediaSourceHandle : IStructuredTransferable
{
    internal MediaSourceHandle(MediaSource source)
    {
        Source = source;
    }

    /// <summary>The underlying source.</summary>
    internal MediaSource Source { get; }

    /// <summary>Whether the handle was transferred: it cannot be used nor transferred again.</summary>
    internal bool IsDetached { get; private set; }

    /// <summary>Whether the handle was ever assigned to the <c>srcObject</c> attribute of a media element: it cannot be transferred anymore.</summary>
    internal bool HasEverBeenAssignedAsSrcObject { get; set; }

    void IStructuredTransferable.ValidateTransfer(bool serialized)
    {
        if (IsDetached)
            throw new DomException("The MediaSourceHandle is detached", "DataCloneError");

        if (HasEverBeenAssignedAsSrcObject)
            throw new DomException("The MediaSourceHandle was assigned to the srcObject attribute of a media element", "DataCloneError");
    }

    ITransferredData IStructuredTransferable.Transfer()
    {
        IsDetached = true;
        return new TransferredHandle(Source);
    }

    private sealed class TransferredHandle(MediaSource source) : ITransferredData
    {
        private MediaSourceHandle? _handle;

        public object Receive(GlobalScope? target)
        {
            // The handle is only exposed to the windows and to the dedicated workers
            if (target is not null && !target.Exposure.HasFlag(ExposureSet.Window) && !target.Exposure.HasFlag(ExposureSet.DedicatedWorker))
                throw new DomException("A MediaSourceHandle cannot be transferred to this global object", "DataCloneError");

            return _handle ??= new MediaSourceHandle(source);
        }
    }
}

/// <summary>A list of source buffers (https://w3c.github.io/media-source/#sourcebufferlist).</summary>
/// <seealso href="https://w3c.github.io/media-source/#sourcebufferlist"/>
[IdlInterface("SourceBufferList", "https://w3c.github.io/media-source/#sourcebufferlist")]
public sealed class SourceBufferList : EventTarget, IReadOnlyList<SourceBuffer>
{
    private readonly List<SourceBuffer> _buffers = [];

    internal SourceBufferList()
    {
    }

    /// <summary>Gets the number of source buffers in the list.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-sourcebufferlist-length"/>
    [IdlMember("length", "https://w3c.github.io/media-source/#dom-sourcebufferlist-length")]
    public uint Length => (uint)_buffers.Count;

    /// <summary>Gets the source buffer at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dfn-sourcebufferlist-getter"/>
    [IdlMember("getter", "https://w3c.github.io/media-source/#dfn-sourcebufferlist-getter")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public SourceBuffer? this[uint index] => index < (uint)_buffers.Count ? _buffers[(int)index] : null;

    /// <summary>Gets the source buffer at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public SourceBuffer this[int index] => _buffers[index];

    /// <summary>Gets the number of source buffers in the list.</summary>
    [NotInIdl]
    public int Count => _buffers.Count;

    /// <summary>Gets or sets the event handler of the <c>addsourcebuffer</c> event.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-sourcebufferlist-onaddsourcebuffer"/>
    [IdlMember("onaddsourcebuffer", "https://w3c.github.io/media-source/#dom-sourcebufferlist-onaddsourcebuffer")]
    public Action<Event>? OnAddSourceBuffer
    {
        get => GetEventHandler("addsourcebuffer");
        set => SetEventHandler("addsourcebuffer", value);
    }

    /// <summary>Gets or sets the event handler of the <c>removesourcebuffer</c> event.</summary>
    /// <seealso href="https://w3c.github.io/media-source/#dom-sourcebufferlist-onremovesourcebuffer"/>
    [IdlMember("onremovesourcebuffer", "https://w3c.github.io/media-source/#dom-sourcebufferlist-onremovesourcebuffer")]
    public Action<Event>? OnRemoveSourceBuffer
    {
        get => GetEventHandler("removesourcebuffer");
        set => SetEventHandler("removesourcebuffer", value);
    }

    internal bool Contains(SourceBuffer buffer) => _buffers.Contains(buffer);

    internal void Add(SourceBuffer buffer) => _buffers.Add(buffer);

    internal void Insert(int index, SourceBuffer buffer) => _buffers.Insert(index, buffer);

    internal bool Remove(SourceBuffer buffer) => _buffers.Remove(buffer);

    internal void Clear() => _buffers.Clear();

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<SourceBuffer> GetEnumerator() => _buffers.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
