using Meziantou.Framework.WebPlatform.Media;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The player of a <see cref="MediaStream"/> assigned to the <c>srcObject</c> of a media element
/// (https://w3c.github.io/mediacapture-main/#mediastreams-in-media-elements): a live, infinite timeline that advances with
/// the clock while the element plays, which cannot be buffered, seeked nor played at another rate. The playback ends when
/// the stream becomes inactive: the duration becomes the current position.
/// </summary>
internal sealed class MediaStreamPlayer : MediaPlayer
{
    private readonly MediaStream _stream;
    private readonly bool _isVideoElement;
    private readonly List<MediaStreamTrack> _observedTracks = [];
    private double _endedDuration = double.NaN;
    private bool _wasActive;

    public MediaStreamPlayer(MediaStream stream, TimeProvider timeProvider, bool isVideoElement)
        : base(timeProvider)
    {
        _stream = stream;
        _isVideoElement = isVideoElement;
        _wasActive = IsActive;
        stream.AddTracksChangedCallback(OnTracksChanged);
        ObserveTracks();
    }

    public MediaStream Stream => _stream;

    /// <summary>Whether the playback ended: the stream became inactive after it was active.</summary>
    public bool HasEnded => !double.IsNaN(_endedDuration);

    /// <summary>Whether the stream is active for the element: an audio element plays the audio tracks only (https://w3c.github.io/mediacapture-main/#stream-audible).</summary>
    private bool IsActive => _isVideoElement ? _stream.Active : _stream.Tracks.Any(track => track.Source.Kind == MediaTrackKinds.Audio && track.ReadyState == MediaStreamTrackState.Live);

    public override double Duration => double.IsNaN(_endedDuration) ? double.PositiveInfinity : _endedDuration;

    public override int VideoWidth => GetVideoTrack()?.LiveSettings.GetValueOrDefault("width") is double width ? (int)width : (GetVideoTrack()?.GetVideoFrame()?.Width ?? 0);

    public override int VideoHeight => GetVideoTrack()?.LiveSettings.GetValueOrDefault("height") is double height ? (int)height : (GetVideoTrack()?.GetVideoFrame()?.Height ?? 0);

    public override IReadOnlyList<MediaTrackInfo> AudioTracks => [.. _stream.GetAudioTracks().Select(track => new MediaTrackInfo(track.Id, "main", track.Label))];

    public override IReadOnlyList<MediaTrackInfo> VideoTracks => [.. _stream.GetVideoTracks().Select(track => new MediaTrackInfo(track.Id, "main", track.Label))];

    public override MediaReadyState ReadyState => _stream.Tracks.Count == 0 ? MediaReadyState.HaveMetadata : MediaReadyState.HaveEnoughData;

    public override IReadOnlyList<MediaTimeRange> Buffered => [];

    public override IReadOnlyList<MediaTimeRange> Seekable => [];

    public override double PlaybackRate
    {
        get => 1;
        set
        {
        }
    }

    public override void Seek(double time)
    {
        // A media stream is not seekable
    }

    public override MediaVideoFrame? GetVideoFrame() => _isVideoElement ? GetVideoTrack()?.GetVideoFrame() : null;

    /// <summary>Whether the stream has data for the element: a video element whose stream has a live video track needs its first frame.</summary>
    public bool HasData => !_isVideoElement || GetVideoTrack() is not { ReadyState: MediaStreamTrackState.Live } track || track.GetVideoFrame() is not null || track.Muted || !track.Enabled;

    /// <summary>The number of frames of the video track played: its frame rate times the playback position.</summary>
    public override long TotalVideoFrames => GetVideoTrack()?.Settings.GetValueOrDefault("frameRate") is double frameRate && frameRate > 0 ? (long)(CurrentTime * frameRate) : 0;

    /// <summary>The first live audio track of the stream, or null.</summary>
    public MediaStreamTrack? GetAudioTrack() => _stream.Tracks.FirstOrDefault(track => track.Source.Kind == MediaTrackKinds.Audio && track.ReadyState == MediaStreamTrackState.Live);

    /// <summary>The video track that is rendered: the first live video track of the stream.</summary>
    private MediaStreamTrack? GetVideoTrack() => _stream.Tracks.FirstOrDefault(track => track.Source.Kind == MediaTrackKinds.Video && track.ReadyState == MediaStreamTrackState.Live)
        ?? _stream.Tracks.FirstOrDefault(track => track.Source.Kind == MediaTrackKinds.Video);

    private void OnTracksChanged()
    {
        ObserveTracks();
        OnStateChanged();
    }

    /// <summary>Observes the tracks of the stream, and no longer the ones that were removed from it (so that they can be collected).</summary>
    private void ObserveTracks()
    {
        for (var i = _observedTracks.Count - 1; i >= 0; i--)
        {
            var observed = _observedTracks[i];
            if (!_stream.Tracks.Contains(observed))
            {
                observed.RemoveStateChangedCallback(OnStateChanged);
                _observedTracks.RemoveAt(i);
            }
        }

        foreach (var track in _stream.Tracks)
        {
            if (!_observedTracks.Contains(track))
            {
                _observedTracks.Add(track);
                track.AddStateChangedCallback(OnStateChanged);
            }
        }
    }

    private void OnStateChanged()
    {
        var isActive = IsActive;
        if (_wasActive && !isActive && double.IsNaN(_endedDuration))
        {
            // The playback ends: the duration is the position reached
            _endedDuration = CurrentTime;
        }
        else if (isActive && !double.IsNaN(_endedDuration))
        {
            _endedDuration = double.NaN;
        }

        _wasActive = isActive;
        NotifyStateChanged();
    }

    protected override void Dispose(bool disposing)
    {
        _stream.RemoveTracksChangedCallback(OnTracksChanged);
        foreach (var track in _observedTracks)
        {
            track.RemoveStateChangedCallback(OnStateChanged);
        }

        _observedTracks.Clear();
        base.Dispose(disposing);
    }
}
