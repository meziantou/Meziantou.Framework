using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// The extensions of the media elements for the Media Source Extensions (https://w3c.github.io/media-source/): attaching and
// detaching a MediaSource, the tracks created by its source buffers, and the ready state, the duration, the buffered and
// the seekable ranges of an element whose media data is appended by the scripts.
public partial class HtmlMediaElement
{
    /// <summary>The media source attached to the element (the media provider object of its current media resource), or null.</summary>
    private MediaSource? _mediaSource;
    private int _mediaSourceAttachmentId;
    private GlobalScope? _mediaSourceGlobal;

    /// <summary>The failure steps of the resource selection algorithm for the attached media source (the "failed with ..." steps).</summary>
    private Action? _mediaSourceFailure;

    /// <summary>The media source of the <c>blob:</c> URL of the src attribute, resolved when the attribute was last changed (the URL can be revoked afterwards).</summary>
    private MediaSource? _srcMediaSource;

    /// <summary>The tracks created by the source buffers of the attached media source.</summary>
    private List<SourceBufferTrack>? _mediaSourceTracks;

    private TimeRanges? _mediaSourceBuffered;

    /// <summary>Whether the element is attached to a media source by the given attachment (the tasks of a previous attachment are ignored).</summary>
    internal bool IsAttachedTo(MediaSource source, int attachmentId) => ReferenceEquals(_mediaSource, source) && _mediaSourceAttachmentId == attachmentId;

    /// <summary>The current playback position, read by the thread of the attached media source to choose the frames it evicts.</summary>
    internal double MediaSourcePlaybackPosition => _currentPlaybackPosition;

    /// <summary>The window whose event loop runs the tasks of the element.</summary>
    private GlobalScope? MediaSourceElementGlobal => NodeDocument.RelevantWindow ?? NodeDocument.OriginDocument?.RelevantWindow;

    /// <summary>Returns the media source of a <c>blob:</c> URL (<see cref="Url.CreateObjectUrl(MediaSource)"/>), or null.</summary>
    private MediaSource? ResolveMediaSourceUrl(UrlRecord? url)
    {
        if (url is not { Scheme: "blob" })
            return null;

        var document = NodeDocument;
        var environment = document.BrowsingContext is null && document.OriginDocument is { } origin ? origin : document;
        return BlobUrlStore.Get(environment).ResolveMediaSource(url);
    }

    /// <summary>
    /// The steps of the resource fetch algorithm for a media provider object that is a media source
    /// (https://w3c.github.io/media-source/#mediasource-attach): the source opens, and the element waits for the
    /// initialization segments of its source buffers; when the source cannot be attached, the media data cannot be fetched
    /// at all.
    /// </summary>
    private void AttachMediaSource(int generation, MediaSource source, bool byHandle, Action onFailure)
    {
        if (MediaSourceElementGlobal is not { } global || source.TryAttach(this, global, byHandle) is not { } attachmentId)
        {
            QueueMediaElementTask(() =>
            {
                if (generation == _selectionGeneration)
                {
                    onFailure();
                }
            });
            return;
        }

        _mediaSource = source;
        _mediaSourceAttachmentId = attachmentId;
        _mediaSourceGlobal = global;
        _mediaSourceFailure = onFailure;
        _mediaSourceBuffered = null;
        IsMediaDataCorsSameOrigin = true;
        SetDelayingTheLoadEvent(false);
    }

    /// <summary>
    /// https://w3c.github.io/media-source/#mediasource-detach: the attached media source is closed. The tracks created by its
    /// source buffers are removed first (with their <c>removetrack</c> events, as when a source buffer is removed).
    /// </summary>
    private void DetachMediaSource()
    {
        if (_mediaSource is not { } source)
            return;

        if (_mediaSourceTracks is { Count: > 0 } tracks)
        {
            RemoveMediaSourceTracks([.. tracks]);
        }

        var global = _mediaSourceGlobal;
        _mediaSource = null;
        _mediaSourceAttachmentId = 0;
        _mediaSourceGlobal = null;
        _mediaSourceFailure = null;
        _mediaSourceTracks = null;
        _mediaSourceBuffered = null;
        if (global is not null)
        {
            source.Detach(this, global);
        }
    }

    /// <summary>
    /// The tracks of the first initialization segment of a source buffer are created on the element
    /// (https://w3c.github.io/media-source/#sourcebuffer-init-segment-received): they are added to the lists of the element
    /// and, for a media source constructed in a window, to the lists of the source buffer.
    /// </summary>
    internal void AddMediaSourceTracks(IReadOnlyList<(SourceBufferTrack Track, MediaSegmentTrack Description)> tracks)
    {
        if (_mediaSource is not { } source)
            return;

        var unselected = false;
        foreach (var (track, description) in tracks)
        {
            // The byte stream track ID is the ID of the track, unless another track of the element already has it
            var id = description.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var suffix = 2; AudioTracks.GetTrackById(id) is not null || VideoTracks.GetTrackById(id) is not null; suffix++)
            {
                id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{description.Id}-{suffix}");
            }

            var info = new MediaTrackInfo(id, description.Kind, description.Label, description.Language == "und" ? "" : description.Language);

            // The source buffers of a media source constructed in a dedicated worker have no track lists
            var sourceBuffer = source.IsInWorker ? null : track.SourceBuffer;
            if (description.IsVideo)
            {
                var videoTrack = new VideoTrack(info) { MediaSourceTrack = track, SourceBuffer = sourceBuffer };
                videoTrack.SetSelectedSilently(track.IsActive);
                track.VideoTrack = videoTrack;
                sourceBuffer?.VideoTracks.AddMediaSourceTrack(videoTrack);
                unselected |= VideoTracks.AddMediaSourceTrack(videoTrack);
            }
            else
            {
                var audioTrack = new AudioTrack(info) { MediaSourceTrack = track, SourceBuffer = sourceBuffer };
                audioTrack.SetEnabledSilently(track.IsActive);
                track.AudioTrack = audioTrack;
                sourceBuffer?.AudioTracks.AddMediaSourceTrack(audioTrack);
                AudioTracks.AddMediaSourceTrack(audioTrack);
            }

            (_mediaSourceTracks ??= []).Add(track);
        }

        if (unselected)
        {
            OnMediaSourceTrackStateChanged();
        }
    }

    /// <summary>The tracks of a source buffer are removed from the element (https://w3c.github.io/media-source/#dom-mediasource-removesourcebuffer).</summary>
    internal void RemoveMediaSourceTracks(IReadOnlyList<SourceBufferTrack> tracks)
    {
        foreach (var track in tracks)
        {
            if (_mediaSourceTracks is null || !_mediaSourceTracks.Remove(track))
                continue;

            if (track.AudioTrack is { } audioTrack)
            {
                var sourceBuffer = audioTrack.SourceBuffer;
                audioTrack.SourceBuffer = null;
                audioTrack.MediaSourceTrack = null;
                sourceBuffer?.AudioTracks.RemoveMediaSourceTrack(audioTrack);
                AudioTracks.RemoveMediaSourceTrack(audioTrack);
            }

            if (track.VideoTrack is { } videoTrack)
            {
                var sourceBuffer = videoTrack.SourceBuffer;
                videoTrack.SourceBuffer = null;
                videoTrack.MediaSourceTrack = null;
                sourceBuffer?.VideoTracks.RemoveMediaSourceTrack(videoTrack);
                VideoTracks.RemoveMediaSourceTrack(videoTrack);
            }
        }
    }

    /// <summary>
    /// The enabled state of an audio track or the selected video track changed: the tracks of the source buffers follow the
    /// tracks of the element, and the media source updates its active source buffers
    /// (https://w3c.github.io/media-source/#active-source-buffer-changes).
    /// </summary>
    private void OnMediaSourceTrackStateChanged()
    {
        if (_mediaSource is not { } source || _mediaSourceTracks is not { } tracks || _mediaSourceGlobal is not { } global)
            return;

        foreach (var track in tracks)
        {
            track.IsActive = track.AudioTrack is { Enabled: true } || track.VideoTrack is { Selected: true };
        }

        source.OnTrackStateChanged(global);
    }

    /// <summary>
    /// Every source buffer of the attached media source received its first initialization segment
    /// (https://w3c.github.io/media-source/#sourcebuffer-init-segment-received): the element has the metadata of its media
    /// resource (<c>HAVE_METADATA</c>).
    /// </summary>
    internal void OnMediaSourceInitialized()
    {
        if (_mediaSource is not { } source || _readyState != HaveNothing || NodeDocument.EventLoop is not { } eventLoop)
            return;

        // The resource is established to be usable: an error is no longer a failure of the resource selection algorithm
        _mediaSourceFailure = null;
        EstablishMediaResource(new MediaSourcePlayer(source, eventLoop.TimeProvider), eventLoop, isCorsSameOrigin: true);
    }

    /// <summary>Whether the ready state can rise: the steps of the monitoring of the attached media source are running.</summary>
    private bool _isMonitoringMediaSource;

    /// <summary>
    /// Queues a timer that raises the ready state of the element when the attached media source has more data around the
    /// current playback position (https://w3c.github.io/media-source/#buffer-monitoring).
    /// </summary>
    internal void ScheduleMediaSourceMonitoring()
    {
        if (_mediaSource is not { } source || NodeDocument.EventLoop is not { } eventLoop)
            return;

        var attachmentId = _mediaSourceAttachmentId;
        eventLoop.QueueTimer(TimeSpan.Zero, () =>
        {
            if (!IsAttachedTo(source, attachmentId) || _player is not MediaSourcePlayer)
                return;

            _isMonitoringMediaSource = true;
            try
            {
                OnPlayerStateChanged();
            }
            finally
            {
                _isMonitoringMediaSource = false;
            }
        });
    }

    /// <summary>
    /// The state of the attached media source changed (the buffered ranges, the duration, <c>endOfStream()</c>...): the ready
    /// state of the element follows the buffered ranges around the current playback position
    /// (https://w3c.github.io/media-source/#buffer-monitoring), its duration follows the one of the source, and a pending
    /// seek completes once its position is buffered.
    /// </summary>
    internal void OnMediaSourceStateChanged()
    {
        if (_mediaSource is not { } source)
            return;

        if (_player is MediaSourcePlayer)
        {
            OnPlayerStateChanged();
            return;
        }

        // Before the element has the metadata, its duration already follows the duration of the source
        // (https://w3c.github.io/media-source/#duration-change-algorithm)
        var duration = source.Snapshot.Duration;
        if (!duration.Equals(_duration))
        {
            _duration = duration;
            QueueMediaEvent("durationchange");
        }
    }

    /// <summary>
    /// The attached media source signals an error (https://w3c.github.io/media-source/#end-of-stream-algorithm): before the
    /// element has the metadata, the media data cannot be fetched at all (or is in an unsupported format); afterwards, the
    /// connection is interrupted (<c>MEDIA_ERR_NETWORK</c>) or the media data is corrupted (<c>MEDIA_ERR_DECODE</c>).
    /// </summary>
    internal void OnMediaSourceError(bool isNetworkError)
    {
        if (_mediaSource is not { } source)
            return;

        if (_readyState == HaveNothing)
        {
            _mediaSourceFailure?.Invoke();
            return;
        }

        if (_error is not null)
            return;

        _error = isNetworkError
            ? new MediaError(MediaError.MediaErrNetwork, "The media source signaled a network error")
            : new MediaError(MediaError.MediaErrDecode, "The media source signaled a decode error");
        source.SetElementHasError(true);
        _selectionGeneration++;
        SetNetworkState(NetworkIdle);
        SetDelayingTheLoadEvent(false);
        QueueMediaEvent("error");
        UpdatePlayback();
    }

    /// <summary>
    /// The player of an element attached to a media source: the metadata and the ranges come from the source (the copy of its
    /// state that the thread of the element can read), and the position advances with the clock of the event loop (the
    /// default playback of <see cref="MediaPlayer"/>) within the buffered ranges.
    /// </summary>
    private sealed class MediaSourcePlayer(MediaSource source, TimeProvider timeProvider) : MediaPlayer(timeProvider)
    {
        /// <summary>
        /// The time that must be buffered after the current playback position to have enough data to ensure uninterrupted
        /// playback (<c>HAVE_ENOUGH_DATA</c>), unless the buffered range reaches the end of the stream: the condition is
        /// implementation-defined; the specification gives 500ms as an example.
        /// </summary>
        private const double EnoughDataSeconds = 0.5;

        private double _position;
        private double _seekTarget = double.NaN;

        public MediaSource Source => source;

        private MediaSourceSnapshot Snapshot => source.Snapshot;

        public override double Duration => Snapshot.Duration;

        public override int VideoWidth => Snapshot.GetVideoSize(_position).Width;

        public override int VideoHeight => Snapshot.GetVideoSize(_position).Height;

        /// <summary>https://w3c.github.io/media-source/#dom-htmlmediaelement.buffered</summary>
        public override IReadOnlyList<MediaTimeRange> Buffered => source.IsWorkerClosing ? [] : Snapshot.Buffered;

        /// <summary>https://w3c.github.io/media-source/#dom-htmlmediaelement.seekable</summary>
        public override IReadOnlyList<MediaTimeRange> Seekable => source.IsWorkerClosing ? [] : Snapshot.GetSeekable();

        /// <summary>
        /// The time before the first buffered range that is played as if it was buffered when the range starts less than
        /// one second after the start of the timeline: the tracks of a media resource rarely start at 0 exactly (e.g. the
        /// first video frame is presented a few frames after the first audio frame). Chromium does the same.
        /// </summary>
        private const double StartFudgeSeconds = 1;

        /// <summary>
        /// The longest gap between two buffered ranges that the playback skips. Appending a segment over another one removes
        /// the frames of the overlapped group of pictures up to the next key frame, which leaves short gaps in the buffered
        /// ranges; the browsers play over them (Firefox skips the gaps shorter than 125ms, Chromium coalesces the ranges
        /// closer than twice the largest distance between two frames).
        /// </summary>
        private const double PlaybackGapSeconds = 0.125;

        private MediaSourceSnapshot? _playbackRangesSnapshot;
        private MediaTimeRange[] _playbackRanges = [];

        /// <summary>The ranges of the media timeline that can be played without interruption: the buffered ranges, without their short gaps.</summary>
        private MediaTimeRange[] PlaybackRanges
        {
            get
            {
                var snapshot = Snapshot;
                if (ReferenceEquals(snapshot, _playbackRangesSnapshot))
                    return _playbackRanges;

                var tolerance = Math.Max(snapshot.GapTolerance, PlaybackGapSeconds);
                var ranges = new List<MediaTimeRange>(snapshot.Buffered.Length);
                foreach (var range in snapshot.Buffered)
                {
                    if (ranges.Count > 0 && range.Start - ranges[^1].End < tolerance)
                    {
                        ranges[^1] = new MediaTimeRange(ranges[^1].Start, range.End);
                    }
                    else
                    {
                        ranges.Add(range);
                    }
                }

                _playbackRangesSnapshot = snapshot;
                return _playbackRanges = [.. ranges];
            }
        }

        /// <summary>
        /// The range that can be played without interruption that contains a position (its end included), or null. A
        /// position that is before the start of a range by less than the gap that the playback skips is in the range (the
        /// timestamps of the byte streams are rounded).
        /// </summary>
        private MediaTimeRange? FindRange(double position)
        {
            var snapshot = Snapshot;
            var ranges = PlaybackRanges;
            if (ranges.Length > 0 && position < ranges[0].Start && ranges[0].Start < StartFudgeSeconds)
                return new MediaTimeRange(position, ranges[0].End);

            var tolerance = Math.Max(snapshot.GapTolerance, PlaybackGapSeconds);
            foreach (var range in ranges)
            {
                if (position <= range.End && position > range.Start - tolerance)
                    return new MediaTimeRange(Math.Min(position, range.Start), range.End);
            }

            return null;
        }

        /// <summary>The buffered range that contains the current playback position (its end included), or null.</summary>
        private MediaTimeRange? CurrentRange => FindRange(_position);

        /// <summary>
        /// The ready state of the element (https://w3c.github.io/media-source/#buffer-monitoring): <c>HAVE_METADATA</c>
        /// when the current playback position is not buffered, <c>HAVE_CURRENT_DATA</c> when the buffered range ends at the
        /// position, <c>HAVE_FUTURE_DATA</c> when it goes beyond it, and <c>HAVE_ENOUGH_DATA</c> when it goes far enough or
        /// to the end of the stream.
        /// </summary>
        public override MediaReadyState ReadyState
        {
            get
            {
                var snapshot = Snapshot;
                if (CurrentRange is not { } range)
                    return MediaReadyState.HaveMetadata;

                // The end of the stream: the media element has all of the media data
                var reachesEnd = range.End >= snapshot.Duration || (snapshot.IsEnded && range.End >= snapshot.Buffered[^1].End);
                if (range.End > _position)
                    return reachesEnd || range.End - _position >= EnoughDataSeconds ? MediaReadyState.HaveEnoughData : MediaReadyState.HaveFutureData;

                return reachesEnd ? MediaReadyState.HaveEnoughData : MediaReadyState.HaveCurrentData;
            }
        }

        /// <summary>The time until the playback reaches the end of the buffered range it plays, or null.</summary>
        public double? TimeToBufferedEnd => CurrentRange is { } range ? range.End - _position : null;

        /// <summary>The position advances with the clock, up to the end of the buffered range that contains it.</summary>
        public override double CurrentTime
        {
            get
            {
                var time = base.CurrentTime;
                if (time == _position)
                    return time;

                if (CurrentRange is { } range)
                {
                    time = Math.Clamp(time, range.Start, range.End);
                }
                else
                {
                    time = _position;
                }

                if (time != base.CurrentTime)
                {
                    // The playback is stalled at the end of the buffered data: the clock restarts from there
                    base.Seek(time);
                }

                return _position = time;
            }
        }

        public override void Seek(double time)
        {
            base.Seek(time);
            _position = base.CurrentTime;
            _seekTarget = _position;
        }

        /// <summary>The element waits until the media data of the new playback position is appended (https://w3c.github.io/media-source/#mediasource-seeking).</summary>
        public override bool IsSeeking
        {
            get
            {
                if (double.IsNaN(_seekTarget))
                    return false;

                if (FindRange(_seekTarget) is null)
                    return true;

                _seekTarget = double.NaN;
                return false;
            }
        }
    }
}
