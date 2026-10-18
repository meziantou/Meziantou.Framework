using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>How the media segments appended to a <see cref="SourceBuffer"/> are placed on the media timeline (https://w3c.github.io/media-source/#dom-appendmode).</summary>
[IdlInterface("AppendMode", "https://w3c.github.io/media-source/#dom-appendmode")]
public enum AppendMode
{
    /// <summary>The timestamps of the media segments determine where the coded frames are placed: the segments can be appended in any order.</summary>
    [IdlMember("segments", "https://w3c.github.io/media-source/#dom-appendmode-segments")]
    Segments,

    /// <summary>The media segments are adjacent in time, whatever their timestamps: the coded frames of a segment are placed after the ones of the previous segment.</summary>
    [IdlMember("sequence", "https://w3c.github.io/media-source/#dom-appendmode-sequence")]
    Sequence,
}

/// <summary>
/// A track of a <see cref="SourceBuffer"/>: its track buffer, and its track on the media element. The media element can be
/// on another thread (the window of the worker of the source): it creates the track objects, and tells whether the track is
/// enabled (an audio track) or selected (a video track).
/// </summary>
internal sealed class SourceBufferTrack(SourceBuffer sourceBuffer, TrackBuffer buffer)
{
    private volatile bool _isActive;

    public SourceBuffer SourceBuffer { get; } = sourceBuffer;

    public TrackBuffer Buffer { get; } = buffer;

    /// <summary>Whether the track is an enabled audio track or the selected video track of the media element.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => _isActive = value;
    }

    /// <summary>The track of the media element, created by its thread.</summary>
    public AudioTrack? AudioTrack { get; set; }

    public VideoTrack? VideoTrack { get; set; }
}

/// <summary>
/// A source buffer of a <see cref="MediaSource"/> (https://w3c.github.io/media-source/#sourcebuffer): the scripts append the
/// initialization segments and the media segments of a byte stream, which the media backend parses
/// (<see cref="MediaSegmentParser"/>). The library keeps the position of the coded frames on the media timeline (not their
/// data) in the track buffers, as specified: the coded frame processing algorithm (<see cref="TimestampOffset"/>,
/// <see cref="Mode"/>, the append window, the coded frame groups, the frames that overlap), the removals, the eviction
/// when the buffer is full, and the <see cref="Buffered"/> ranges.
/// </summary>
[IdlInterface("SourceBuffer", "https://w3c.github.io/media-source/#sourcebuffer")]
public class SourceBuffer : EventTarget
{
    /// <summary>
    /// The size of the media segments a source buffer keeps before it is full (the buffer full flag), unless the media
    /// backend gives another one (<see cref="MediaSegmentParser.BufferCapacity"/>): 150 MB with a video track, 12 MB for
    /// audio, as in Chromium. The frames that were played are evicted first.
    /// </summary>
    private const long VideoCapacity = 150 * 1024 * 1024;
    private const long AudioCapacity = 12 * 1024 * 1024;

    private enum AppendState
    {
        WaitingForSegment,
        ParsingInitializationSegment,
        ParsingMediaSegment,
    }

    private readonly List<SourceBufferTrack> _tracks = [];
    private MediaSource? _parent;
    private MediaSegmentParser _parser;
    private byte[] _inputBuffer = [];
    private int _inputLength;
    private AppendState _appendState;
    private bool _bufferFull;
    private bool _firstInitializationSegmentReceived;
    private bool _pendingInitializationSegmentForChangeType;
    private bool _audioTrackAdded;
    private bool _videoTrackAdded;
    private double? _groupStartTimestamp;
    private double _groupEndTimestamp;
    private bool _generateTimestamps;
    private AppendMode _mode;
    private bool _updating;
    private bool _isRemoving;
    private int _updateId;
    private double _timestampOffset;
    private double _appendWindowStart;
    private double _appendWindowEnd = double.PositiveInfinity;
    private TimeRanges _buffered = new();

    internal SourceBuffer(MediaSource parent, MediaSegmentParser parser)
    {
        _parent = parent;
        _parser = parser;
        _generateTimestamps = parser.GeneratesTimestamps;
        _mode = _generateTimestamps ? AppendMode.Sequence : AppendMode.Segments;
    }

    /// <summary>The steps run at the completion of the operations that can change the buffered ranges (a managed source buffer fires <c>bufferedchange</c>).</summary>
    private protected virtual void OnBufferedRangesMayHaveChanged(MediaSource parent)
    {
    }

    internal void NotifyBufferedRangesMayHaveChanged()
    {
        if (_parent is { } parent)
        {
            OnBufferedRangesMayHaveChanged(parent);
        }
    }

    /// <summary>The parent media source, or an <c>InvalidStateError</c> when the source buffer was removed from it.</summary>
    private MediaSource Parent => _parent ?? throw DomException.InvalidState("The SourceBuffer was removed from its MediaSource");

    /// <summary>
    /// Gets or sets how the media segments are placed on the media timeline (https://w3c.github.io/media-source/#dom-sourcebuffer-mode).
    /// The setter throws an <c>InvalidStateError</c> when the source buffer is removed, is updating or is parsing a media
    /// segment, and a <c>TypeError</c> when the timestamps of the byte stream format are generated (the mode is always
    /// <see cref="AppendMode.Sequence"/>).
    /// </summary>
    [IdlMember("mode", "https://w3c.github.io/media-source/#dom-sourcebuffer-mode")]
    public AppendMode Mode
    {
        get => _mode;
        set
        {
            var parent = Parent;
            ThrowIfUpdating();
            if (_generateTimestamps && value == AppendMode.Segments)
                throw new TypeErrorException("The timestamps of the byte stream format are generated: the mode must be 'sequence'");

            parent.ReopenIfEnded();
            if (_appendState == AppendState.ParsingMediaSegment)
                throw DomException.InvalidState("The SourceBuffer is parsing a media segment");

            if (value == AppendMode.Sequence)
            {
                _groupStartTimestamp = _groupEndTimestamp;
            }

            _mode = value;
        }
    }

    /// <summary>Gets a value indicating whether an append or a removal is being processed (https://w3c.github.io/media-source/#dom-sourcebuffer-updating).</summary>
    [IdlMember("updating", "https://w3c.github.io/media-source/#dom-sourcebuffer-updating")]
    public bool Updating => _updating;

    /// <summary>
    /// Gets the time ranges that are buffered (https://w3c.github.io/media-source/#dom-sourcebuffer-buffered): the
    /// intersection of the ranges of the audio and video track buffers. The same object is returned while the ranges do not
    /// change. Throws an <c>InvalidStateError</c> when the source buffer is removed.
    /// </summary>
    [IdlMember("buffered", "https://w3c.github.io/media-source/#dom-sourcebuffer-buffered")]
    public TimeRanges Buffered
    {
        get
        {
            var parent = Parent;
            var ranges = ComputeBufferedRanges(parent.ReadyState == MediaSourceReadyState.Ended);
            if (!MediaTimeRanges.AreEqual(ranges, _buffered.Ranges))
            {
                _buffered = new TimeRanges(ranges);
            }

            return _buffered;
        }
    }

    /// <summary>
    /// Gets or sets the offset applied to the timestamps of the next media segments, in seconds
    /// (https://w3c.github.io/media-source/#dom-sourcebuffer-timestampoffset).
    /// </summary>
    [IdlMember("timestampOffset", "https://w3c.github.io/media-source/#dom-sourcebuffer-timestampoffset")]
    public double TimestampOffset
    {
        get => _timestampOffset;
        set
        {
            var parent = Parent;
            ThrowIfUpdating();
            parent.ReopenIfEnded();
            if (_appendState == AppendState.ParsingMediaSegment)
                throw DomException.InvalidState("The SourceBuffer is parsing a media segment");

            if (_mode == AppendMode.Sequence)
            {
                _groupStartTimestamp = value;
            }

            _timestampOffset = value;
        }
    }

    /// <summary>Gets the audio tracks created by the source buffer (https://w3c.github.io/media-source/#dom-sourcebuffer-audiotracks).</summary>
    [IdlMember("audioTracks", "https://w3c.github.io/media-source/#dom-sourcebuffer-audiotracks")]
    public AudioTrackList AudioTracks => field ??= new AudioTrackList(this);

    /// <summary>Gets the video tracks created by the source buffer (https://w3c.github.io/media-source/#dom-sourcebuffer-videotracks).</summary>
    [IdlMember("videoTracks", "https://w3c.github.io/media-source/#dom-sourcebuffer-videotracks")]
    public VideoTrackList VideoTracks => field ??= new VideoTrackList(this);

    /// <summary>
    /// Gets the text tracks created by the source buffer (https://w3c.github.io/media-source/#dom-sourcebuffer-texttracks):
    /// the list is always empty (the text tracks of the byte streams are not supported, as in the browsers).
    /// </summary>
    [IdlMember("textTracks", "https://w3c.github.io/media-source/#dom-sourcebuffer-texttracks")]
    public TextTrackList TextTracks => field ??= new TextTrackList(this);

    /// <summary>
    /// Gets or sets the start of the append window, in seconds (https://w3c.github.io/media-source/#dom-sourcebuffer-appendwindowstart):
    /// the coded frames that start before it are dropped.
    /// </summary>
    [IdlMember("appendWindowStart", "https://w3c.github.io/media-source/#dom-sourcebuffer-appendwindowstart")]
    public double AppendWindowStart
    {
        get => _appendWindowStart;
        set
        {
            _ = Parent;
            ThrowIfUpdating();
            if (value < 0 || value >= _appendWindowEnd)
                throw new TypeErrorException("The start of the append window must not be negative and must be less than its end");

            _appendWindowStart = value;
        }
    }

    /// <summary>
    /// Gets or sets the end of the append window, in seconds (https://w3c.github.io/media-source/#dom-sourcebuffer-appendwindowend):
    /// the coded frames that end after it are dropped.
    /// </summary>
    [IdlMember("appendWindowEnd", "https://w3c.github.io/media-source/#dom-sourcebuffer-appendwindowend")]
    public double AppendWindowEnd
    {
        get => _appendWindowEnd;
        set
        {
            _ = Parent;
            ThrowIfUpdating();
            if (double.IsNaN(value) || value <= _appendWindowStart)
                throw new TypeErrorException("The end of the append window must be greater than its start");

            _appendWindowEnd = value;
        }
    }

    /// <summary>Gets or sets the event handler of the <c>updatestart</c> event.</summary>
    [IdlMember("onupdatestart", "https://w3c.github.io/media-source/#dom-sourcebuffer-onupdatestart")]
    public Action<Event>? OnUpdateStart
    {
        get => GetEventHandler("updatestart");
        set => SetEventHandler("updatestart", value);
    }

    /// <summary>Gets or sets the event handler of the <c>update</c> event.</summary>
    [IdlMember("onupdate", "https://w3c.github.io/media-source/#dom-sourcebuffer-onupdate")]
    public Action<Event>? OnUpdate
    {
        get => GetEventHandler("update");
        set => SetEventHandler("update", value);
    }

    /// <summary>Gets or sets the event handler of the <c>updateend</c> event.</summary>
    [IdlMember("onupdateend", "https://w3c.github.io/media-source/#dom-sourcebuffer-onupdateend")]
    public Action<Event>? OnUpdateEnd
    {
        get => GetEventHandler("updateend");
        set => SetEventHandler("updateend", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    [IdlMember("onerror", "https://w3c.github.io/media-source/#dom-sourcebuffer-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Gets or sets the event handler of the <c>abort</c> event.</summary>
    [IdlMember("onabort", "https://w3c.github.io/media-source/#dom-sourcebuffer-onabort")]
    public Action<Event>? OnAbort
    {
        get => GetEventHandler("abort");
        set => SetEventHandler("abort", value);
    }

    /// <summary>
    /// Appends segment data (https://w3c.github.io/media-source/#dom-sourcebuffer-appendbuffer): the data is parsed in a
    /// task (<c>updatestart</c>, then <c>update</c> or <c>error</c>, then <c>updateend</c>). Throws an
    /// <c>InvalidStateError</c> when the source buffer is removed or updating, or when the media element has an error, and a
    /// <c>QuotaExceededError</c> when the buffer is full.
    /// </summary>
    [IdlMember("appendBuffer", "https://w3c.github.io/media-source/#dom-sourcebuffer-appendbuffer")]
    public void AppendBuffer(ReadOnlySpan<byte> data)
    {
        var parent = PrepareAppend();
        if (_inputBuffer.Length < _inputLength + data.Length)
        {
            Array.Resize(ref _inputBuffer, Math.Max(_inputLength + data.Length, _inputBuffer.Length * 2));
        }

        data.CopyTo(_inputBuffer.AsSpan(_inputLength));
        _inputLength += data.Length;
        _updating = true;
        parent.QueueEvent(this, "updatestart");

        // The buffer append algorithm runs asynchronously
        var updateId = ++_updateId;
        parent.QueueTask(() =>
        {
            if (updateId == _updateId && _updating)
            {
                RunBufferAppend();
            }
        });
    }

    /// <summary>
    /// Aborts the current segment and resets the segment parser (https://w3c.github.io/media-source/#dom-sourcebuffer-abort):
    /// a pending append is aborted (<c>abort</c>, <c>updateend</c>), and the append window is reset.
    /// </summary>
    [IdlMember("abort", "https://w3c.github.io/media-source/#dom-sourcebuffer-abort")]
    public void Abort()
    {
        var parent = Parent;
        if (parent.ReadyState != MediaSourceReadyState.Open)
            throw DomException.InvalidState("The MediaSource is not open");

        if (_isRemoving)
            throw DomException.InvalidState("A range removal is running");

        AbortUpdate();
        ResetParserState();
        _appendWindowStart = 0;
        _appendWindowEnd = double.PositiveInfinity;
        parent.OnStateChanged();
    }

    /// <summary>
    /// Changes the MIME type of the next appended bytes (https://w3c.github.io/media-source/#dom-sourcebuffer-changetype): the
    /// next segment must be an initialization segment. Throws a <c>TypeError</c> for an empty type and a
    /// <c>NotSupportedError</c> when the media backend does not support the type.
    /// </summary>
    [IdlMember("changeType", "https://w3c.github.io/media-source/#dom-sourcebuffer-changetype")]
    public void ChangeType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Length == 0)
            throw new TypeErrorException("The type must not be empty");

        var parent = Parent;
        ThrowIfUpdating();
        var parser = MediaSource.CreateParser(parent.Global, type) ?? throw DomException.NotSupported($"The type '{type}' is not supported");
        parent.ReopenIfEnded();
        ResetParserState();
        _parser.Dispose();
        _parser = parser;
        _generateTimestamps = parser.GeneratesTimestamps;
        if (_generateTimestamps)
        {
            // Setting the mode attribute, with its steps
            _groupStartTimestamp = _groupEndTimestamp;
            _mode = AppendMode.Sequence;
        }

        _pendingInitializationSegmentForChangeType = true;
    }

    /// <summary>
    /// Removes the media of a time range (https://w3c.github.io/media-source/#dom-sourcebuffer-remove), in a task
    /// (<c>updatestart</c>, <c>update</c>, <c>updateend</c>).
    /// </summary>
    [IdlMember("remove", "https://w3c.github.io/media-source/#dom-sourcebuffer-remove")]
    public void Remove(double start, double end)
    {
        var parent = Parent;
        ThrowIfUpdating();
        var duration = parent.Duration;
        if (double.IsNaN(duration))
            throw new TypeErrorException("The duration of the MediaSource is NaN");

        if (start < 0 || start > duration)
            throw new TypeErrorException("The start of the range must not be negative nor greater than the duration");

        if (end <= start || double.IsNaN(end))
            throw new TypeErrorException("The end of the range must be greater than its start");

        parent.ReopenIfEnded();

        // The range removal algorithm
        _updating = true;
        _isRemoving = true;
        parent.QueueEvent(this, "updatestart");
        var updateId = ++_updateId;
        parent.QueueTask(() =>
        {
            if (updateId != _updateId || !_updating || _parent is not { } source)
                return;

            RunCodedFrameRemoval(start, end);
            _updating = false;
            _isRemoving = false;
            source.QueueEvent(this, "update");
            source.QueueEvent(this, "updateend");
            OnBufferedRangesMayHaveChanged(source);
            source.OnStateChanged();
            source.ScheduleMonitoring();
        });
    }

    private void ThrowIfUpdating()
    {
        if (_updating)
            throw DomException.InvalidState("The SourceBuffer is updating");
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Internal state used by the parent media source
    // ----------------------------------------------------------------------------------------------------------------

    internal bool HasReceivedInitializationSegment => _firstInitializationSegmentReceived;

    /// <summary>Whether the source buffer provides an enabled audio track or the selected video track.</summary>
    internal bool HasActiveTrack
    {
        get
        {
            foreach (var track in _tracks)
            {
                if (track.IsActive)
                    return true;
            }

            return false;
        }
    }

    /// <summary>The track buffer of the selected video track, if the source buffer provides it.</summary>
    internal TrackBuffer? SelectedVideoTrackBuffer
    {
        get
        {
            foreach (var track in _tracks)
            {
                if (track.IsActive && track.Buffer.IsVideo)
                    return track.Buffer;
            }

            return null;
        }
    }

    internal IReadOnlyList<SourceBufferTrack> Tracks => _tracks;

    /// <summary>The largest track buffer ranges end time across the track buffers of the source buffer.</summary>
    internal double HighestEndTime
    {
        get
        {
            var result = 0d;
            foreach (var track in _tracks)
            {
                result = Math.Max(result, track.Buffer.HighestEndTime);
            }

            return result;
        }
    }

    /// <summary>The largest gap between two frames of a track buffer that is not a discontinuity of its ranges.</summary>
    internal double GapTolerance
    {
        get
        {
            var result = 0d;
            foreach (var track in _tracks)
            {
                result = Math.Max(result, track.Buffer.GapTolerance);
            }

            return result;
        }
    }

    /// <summary>The highest presentation timestamp of the buffered frames, or null.</summary>
    internal double? HighestPresentationTimestamp
    {
        get
        {
            double? result = null;
            foreach (var track in _tracks)
            {
                if (track.Buffer.HighestPresentationTimestamp is { } timestamp && !(timestamp <= result))
                {
                    result = timestamp;
                }
            }

            return result;
        }
    }

    /// <summary>The steps of the <c>buffered</c> attribute: the intersection of the ranges of the track buffers.</summary>
    internal MediaTimeRange[] ComputeBufferedRanges(bool isEnded)
    {
        if (_tracks.Count == 0)
            return [];

        var highestEndTime = HighestEndTime;
        MediaTimeRange[] intersection = [new MediaTimeRange(0, highestEndTime)];
        foreach (var track in _tracks)
        {
            var ranges = track.Buffer.Ranges;
            if (isEnded && ranges.Length > 0)
            {
                ranges = [.. ranges];
                ranges[^1] = new MediaTimeRange(ranges[^1].Start, highestEndTime);
            }

            intersection = MediaTimeRanges.Intersect(intersection, ranges);
        }

        return intersection;
    }

    /// <summary>The steps of <c>removeSourceBuffer()</c> and <c>abort()</c> when the source buffer is updating: the append is aborted.</summary>
    internal void AbortUpdate()
    {
        if (!_updating || _parent is not { } parent)
            return;

        _updateId++;
        _updating = false;
        _isRemoving = false;
        parent.QueueEvent(this, "abort");
        parent.QueueEvent(this, "updateend");
    }

    /// <summary>The tracks of the source buffer are removed from its lists and from the lists of the media element (<c>removeSourceBuffer()</c>, or the source is detached).</summary>
    internal void RemoveTracks()
    {
        if (_tracks.Count == 0 || _parent is not { } parent)
            return;

        var tracks = _tracks.ToArray();
        foreach (var track in tracks)
        {
            track.IsActive = false;
        }

        parent.RunOnElement(element => element.RemoveMediaSourceTracks(tracks));
    }

    /// <summary>Destroys the resources of the source buffer: it is removed from its parent media source.</summary>
    internal void Destroy()
    {
        _updateId++;
        _updating = false;
        _isRemoving = false;
        _parent = null;
        _inputBuffer = [];
        _inputLength = 0;
        _tracks.Clear();
        _parser.Dispose();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Algorithms (https://w3c.github.io/media-source/#sourcebuffer-algorithms)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-prepare-append</summary>
    private MediaSource PrepareAppend()
    {
        var parent = Parent;
        ThrowIfUpdating();
        if (parent.ElementHasError)
            throw DomException.InvalidState("The media element has an error");

        parent.ReopenIfEnded();
        RunCodedFrameEviction(parent);
        if (_bufferFull)
            throw new QuotaExceededError("The SourceBuffer is full: remove buffered data before appending", quota: null, requested: null);

        return parent;
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-buffer-append</summary>
    private void RunBufferAppend()
    {
        if (!RunSegmentParserLoop() || _parent is not { } parent)
            return;

        _updating = false;
        parent.QueueEvent(this, "update");
        parent.QueueEvent(this, "updateend");
        OnBufferedRangesMayHaveChanged(parent);
        parent.ScheduleMonitoring();
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-segment-parser-loop: returns false when the append error algorithm ran.</summary>
    private bool RunSegmentParserLoop()
    {
        var madeProgress = true;
        while (_inputLength > 0 && _parent is { } parent)
        {
            var result = _parser.Parse(_inputBuffer.AsMemory(0, _inputLength));

            // A parser that consumes nothing twice in a row would never end
            var consumesInput = result.ByteCount > 0 || result.Status is MediaSegmentParseStatus.NeedMoreData or MediaSegmentParseStatus.Error;
            if (result.ByteCount > _inputLength || (!consumesInput && !madeProgress))
            {
                RunAppendError();
                return false;
            }

            madeProgress = consumesInput;

            switch (result.Status)
            {
                case MediaSegmentParseStatus.NeedMoreData:
                    if (result.IsParsingMediaSegment)
                    {
                        if (!_firstInitializationSegmentReceived || _pendingInitializationSegmentForChangeType)
                        {
                            RunAppendError();
                            return false;
                        }

                        _appendState = AppendState.ParsingMediaSegment;
                    }

                    return true;

                case MediaSegmentParseStatus.Ignored:
                    ConsumeInput(result.ByteCount);
                    break;

                case MediaSegmentParseStatus.InitializationSegment:
                    _appendState = AppendState.ParsingInitializationSegment;
                    if (!OnInitializationSegmentReceived(parent, result.InitializationSegment!))
                        return false;

                    ConsumeInput(result.ByteCount);
                    _appendState = AppendState.WaitingForSegment;
                    break;

                case MediaSegmentParseStatus.MediaSegment:
                    _appendState = AppendState.ParsingMediaSegment;
                    if (!_firstInitializationSegmentReceived || _pendingInitializationSegmentForChangeType)
                    {
                        RunAppendError();
                        return false;
                    }

                    if (!ProcessCodedFrames(parent, result.Frames, result.ByteCount))
                    {
                        RunAppendError();
                        return false;
                    }

                    _bufferFull = IsFull;
                    ConsumeInput(result.ByteCount);
                    if (!result.IsParsingMediaSegment)
                    {
                        _appendState = AppendState.WaitingForSegment;
                    }

                    break;

                default:
                    RunAppendError();
                    return false;
            }
        }

        return _parent is not null;
    }

    private void ConsumeInput(int byteCount)
    {
        _inputBuffer.AsSpan(byteCount, _inputLength - byteCount).CopyTo(_inputBuffer);
        _inputLength -= byteCount;
    }

    private long ByteCount
    {
        get
        {
            var result = 0L;
            foreach (var track in _tracks)
            {
                result += track.Buffer.ByteCount;
            }

            return result;
        }
    }

    private bool IsFull
    {
        get
        {
            var hasVideo = false;
            foreach (var track in _tracks)
            {
                hasVideo |= track.Buffer.IsVideo;
            }

            return ByteCount >= (_parser.BufferCapacity ?? (hasVideo ? VideoCapacity : AudioCapacity));
        }
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-reset-parser-state</summary>
    private void ResetParserState()
    {
        // The complete coded frames of the input buffer are already processed (see the segment parser loop)
        foreach (var track in _tracks)
        {
            track.Buffer.ResetGroup();
        }

        if (_mode == AppendMode.Sequence)
        {
            _groupStartTimestamp = _groupEndTimestamp;
        }

        _inputLength = 0;
        _appendState = AppendState.WaitingForSegment;
        _parser.Reset();
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-append-error</summary>
    private void RunAppendError()
    {
        if (_parent is not { } parent)
            return;

        ResetParserState();
        _updating = false;
        parent.QueueEvent(this, "error");
        parent.QueueEvent(this, "updateend");
        parent.RunEndOfStream(EndOfStreamError.Decode);
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-init-segment-received: returns false when the append error algorithm ran.</summary>
    private bool OnInitializationSegmentReceived(MediaSource parent, MediaInitializationSegment segment)
    {
        if (double.IsNaN(parent.DurationValue))
        {
            parent.ChangeDuration(double.IsNaN(segment.Duration) ? double.PositiveInfinity : segment.Duration);
        }

        if (segment.Tracks.Count == 0)
        {
            RunAppendError();
            return false;
        }

        var activeTrackFlag = false;
        if (_firstInitializationSegmentReceived)
        {
            // The number of audio and video tracks matches the first initialization segment, and the track IDs match when
            // there are several tracks of a type (the parser checks the codecs)
            if (!TryUpdateTrackDescriptions(segment))
            {
                RunAppendError();
                return false;
            }

            foreach (var track in _tracks)
            {
                track.Buffer.NeedRandomAccessPoint = true;
            }
        }
        else
        {
            var created = new List<(SourceBufferTrack Track, MediaSegmentTrack Description)>(segment.Tracks.Count);
            foreach (var description in segment.Tracks)
            {
                var track = new SourceBufferTrack(this, new TrackBuffer(description.Id, description.IsVideo) { Width = description.VideoWidth, Height = description.VideoHeight });

                // The first audio track of the source buffer is enabled, and its first video track is selected
                if (description.IsVideo ? !_videoTrackAdded : !_audioTrackAdded)
                {
                    track.IsActive = true;
                    activeTrackFlag = true;
                    if (description.IsVideo)
                    {
                        _videoTrackAdded = true;
                    }
                    else
                    {
                        _audioTrackAdded = true;
                    }
                }

                _tracks.Add(track);
                created.Add((track, description));
            }

            // The tracks are created on the media element
            parent.RunOnElement(element => element.AddMediaSourceTracks(created));
            _firstInitializationSegmentReceived = true;
            if (activeTrackFlag)
            {
                parent.UpdateActiveSourceBuffers();
            }
        }

        _pendingInitializationSegmentForChangeType = false;
        if (parent.AreAllSourceBuffersInitialized)
        {
            parent.OnStateChanged();
            parent.RunOnElement(static element => element.OnMediaSourceInitialized());
        }

        return _parent is not null;
    }

    /// <summary>Adds the track descriptions of an initialization segment that is not the first one to the track buffers, when its tracks match.</summary>
    private bool TryUpdateTrackDescriptions(MediaInitializationSegment segment)
    {
        var audioCount = 0;
        var videoCount = 0;
        foreach (var track in _tracks)
        {
            if (track.Buffer.IsVideo)
            {
                videoCount++;
            }
            else
            {
                audioCount++;
            }
        }

        var newAudioCount = 0;
        var newVideoCount = 0;
        foreach (var description in segment.Tracks)
        {
            if (description.IsVideo)
            {
                newVideoCount++;
            }
            else
            {
                newAudioCount++;
            }
        }

        if (audioCount != newAudioCount || videoCount != newVideoCount)
            return false;

        var matches = new List<(TrackBuffer Buffer, MediaSegmentTrack Description)>(segment.Tracks.Count);
        foreach (var description in segment.Tracks)
        {
            var single = (description.IsVideo ? videoCount : audioCount) == 1;
            TrackBuffer? match = null;
            foreach (var track in _tracks)
            {
                if (track.Buffer.IsVideo == description.IsVideo && (single || track.Buffer.TrackId == description.Id))
                {
                    match = track.Buffer;
                    break;
                }
            }

            if (match is null)
                return false;

            matches.Add((match, description));
        }

        foreach (var (buffer, description) in matches)
        {
            buffer.TrackId = description.Id;
            buffer.Width = description.VideoWidth;
            buffer.Height = description.VideoHeight;
        }

        return true;
    }

    private TrackBuffer? FindTrackBuffer(long trackId)
    {
        foreach (var track in _tracks)
        {
            if (track.Buffer.TrackId == trackId)
                return track.Buffer;
        }

        return null;
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-coded-frame-processing: returns false when a frame is not a frame of a track of the initialization segment.</summary>
    private bool ProcessCodedFrames(MediaSource parent, IReadOnlyList<MediaCodedFrame> frames, int byteCount)
    {
        // The tracks of the media segments must be identified by the initialization segment
        // (https://w3c.github.io/media-source/#byte-stream-formats)
        foreach (var frame in frames)
        {
            if (FindTrackBuffer(frame.TrackId) is null)
                return false;
        }

        var frameByteCount = frames.Count == 0 ? 0 : byteCount / frames.Count;
        foreach (var frame in frames)
        {
            var trackBuffer = FindTrackBuffer(frame.TrackId)!;

            double presentationTimestamp;
            double decodeTimestamp;
            while (true)
            {
                presentationTimestamp = _generateTimestamps ? 0 : frame.PresentationTimestamp;
                decodeTimestamp = _generateTimestamps ? 0 : frame.DecodeTimestamp;
                if (_mode == AppendMode.Sequence && _groupStartTimestamp is { } groupStartTimestamp)
                {
                    _timestampOffset = groupStartTimestamp - presentationTimestamp;
                    _groupEndTimestamp = groupStartTimestamp;
                    foreach (var track in _tracks)
                    {
                        track.Buffer.NeedRandomAccessPoint = true;
                    }

                    _groupStartTimestamp = null;
                }

                if (_timestampOffset != 0)
                {
                    presentationTimestamp += _timestampOffset;
                    decodeTimestamp += _timestampOffset;
                }

                // A discontinuity: the frame starts a new coded frame group
                if (trackBuffer.LastDecodeTimestamp is { } lastDecodeTimestamp
                    && (decodeTimestamp < lastDecodeTimestamp || decodeTimestamp - lastDecodeTimestamp > 2 * trackBuffer.LastFrameDuration.GetValueOrDefault()))
                {
                    if (_mode == AppendMode.Segments)
                    {
                        _groupEndTimestamp = presentationTimestamp;
                    }
                    else
                    {
                        _groupStartTimestamp = _groupEndTimestamp;
                    }

                    foreach (var track in _tracks)
                    {
                        track.Buffer.ResetGroup();
                    }

                    continue;
                }

                break;
            }

            var frameDuration = frame.Duration;
            var frameEndTimestamp = presentationTimestamp + frameDuration;
            if (presentationTimestamp < _appendWindowStart || frameEndTimestamp > _appendWindowEnd)
            {
                trackBuffer.NeedRandomAccessPoint = true;
                continue;
            }

            if (trackBuffer.NeedRandomAccessPoint)
            {
                if (!frame.IsRandomAccessPoint)
                    continue;

                trackBuffer.NeedRandomAccessPoint = false;
            }

            if (trackBuffer.LastDecodeTimestamp is null && trackBuffer.FindFrameAt(presentationTimestamp) is >= 0 and var overlappedIndex)
            {
                var overlapped = trackBuffer.Frames[overlappedIndex];
                if (trackBuffer.IsVideo)
                {
                    // A frame replaces an existing one when it is within 1 microsecond of its start time
                    if (presentationTimestamp < overlapped.PresentationTimestamp + 0.000001)
                    {
                        trackBuffer.RemoveAt(overlappedIndex);
                    }
                }
                else
                {
                    // The audio splice frame algorithm, without crossfade: the overlapped frame is replaced with a silence
                    // frame that ends where the new frame starts
                    var silenceDuration = presentationTimestamp - overlapped.PresentationTimestamp;
                    if (silenceDuration > 0)
                    {
                        trackBuffer.Replace(overlappedIndex, overlapped with { Duration = silenceDuration });
                    }
                    else
                    {
                        trackBuffer.RemoveAt(overlappedIndex);
                    }
                }
            }

            // Remove the existing coded frames the new frame overlaps, and their decoding dependencies
            if (trackBuffer.HighestEndTimestamp is not { } highestEndTimestamp)
            {
                trackBuffer.RemoveRange(presentationTimestamp, frameEndTimestamp, out _);
            }
            else if (highestEndTimestamp <= presentationTimestamp)
            {
                trackBuffer.RemoveRange(highestEndTimestamp, frameEndTimestamp, out _);
            }

            trackBuffer.Add(new BufferedFrame(presentationTimestamp, decodeTimestamp, frameDuration, frame.IsRandomAccessPoint, trackBuffer.Width, trackBuffer.Height, frameByteCount));
            trackBuffer.LastDecodeTimestamp = decodeTimestamp;
            trackBuffer.LastFrameDuration = frameDuration;
            if (trackBuffer.HighestEndTimestamp is not { } highest || frameEndTimestamp > highest)
            {
                trackBuffer.HighestEndTimestamp = frameEndTimestamp;
            }

            if (frameEndTimestamp > _groupEndTimestamp)
            {
                _groupEndTimestamp = frameEndTimestamp;
            }

            if (_generateTimestamps)
            {
                _timestampOffset = frameEndTimestamp;
            }
        }

        // The media segment contains data beyond the current duration
        if (_groupEndTimestamp > parent.DurationValue)
        {
            parent.ChangeDuration(Math.Max(parent.DurationValue, _groupEndTimestamp));
        }

        // The ready state of the media element follows the buffered ranges
        parent.OnStateChanged();
        return true;
    }

    /// <summary>https://w3c.github.io/media-source/#sourcebuffer-coded-frame-removal</summary>
    private void RunCodedFrameRemoval(double start, double end)
    {
        if (_parent is not { } parent)
            return;

        foreach (var track in _tracks)
        {
            var trackBuffer = track.Buffer;
            var removeEndTimestamp = parent.DurationValue;
            if (trackBuffer.FindRandomAccessPointAtOrAfter(end) is { } randomAccessPoint)
            {
                removeEndTimestamp = randomAccessPoint;
            }

            if (!trackBuffer.RemoveRange(start, removeEndTimestamp, out var lastDecodedFrameTimestamp) || lastDecodedFrameTimestamp is not { } presentationTimestamp)
                continue;

            // A frame of the current coded frame group is removed: the next frame starts a new group
            if (_mode == AppendMode.Segments)
            {
                _groupEndTimestamp = presentationTimestamp;
            }
            else
            {
                _groupStartTimestamp = _groupEndTimestamp;
            }

            foreach (var other in _tracks)
            {
                other.Buffer.ResetGroup();
            }
        }

        if (_bufferFull && !IsFull)
        {
            _bufferFull = false;
        }
    }

    /// <summary>
    /// https://w3c.github.io/media-source/#sourcebuffer-coded-frame-eviction: when the buffer is full, the frames that were
    /// played (the ones before the random access point of the current playback position of the media element) are removed.
    /// </summary>
    private void RunCodedFrameEviction(MediaSource parent)
    {
        if (!_bufferFull)
            return;

        var end = parent.ElementPosition;
        foreach (var track in _tracks)
        {
            end = Math.Min(end, track.Buffer.FindRandomAccessPointAtOrBefore(end) ?? 0);
        }

        if (end > 0)
        {
            RunCodedFrameRemoval(0, end);
            parent.OnStateChanged();
        }
    }
}
